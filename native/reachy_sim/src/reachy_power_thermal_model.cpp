#include "reachy_power_thermal_model.hpp"

#include "reachy_power_thermal_baseline.generated.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <limits>

namespace reachy::servo {
namespace {

constexpr std::size_t kVoltageSolveIterations = 8U;

[[nodiscard]] bool IsKnownMode(ServoMode mode) noexcept
{
    switch(mode)
    {
        case ServoMode::Disabled:
        case ServoMode::Position:
        case ServoMode::Velocity:
        case ServoMode::Torque:
            return true;
    }
    return false;
}

[[nodiscard]] bool IsFiniteCommand(const ServoCommand& command) noexcept
{
    return std::isfinite(command.sample_time_seconds) && command.sample_time_seconds >= 0.0 &&
           IsKnownMode(command.mode) && std::isfinite(command.target_position_radians) &&
           std::isfinite(command.target_velocity_radians_per_second) &&
           std::isfinite(command.profile_velocity_radians_per_second) &&
           command.profile_velocity_radians_per_second >= 0.0 &&
           std::isfinite(command.profile_acceleration_radians_per_second_squared) &&
           command.profile_acceleration_radians_per_second_squared >= 0.0 &&
           std::isfinite(command.feedforward_torque_newton_metres);
}

[[nodiscard]] bool IsFiniteObservation(const ServoObservation& observation) noexcept
{
    return std::isfinite(observation.sample_time_seconds) && observation.sample_time_seconds >= 0.0 &&
           std::isfinite(observation.position_radians) &&
           std::isfinite(observation.velocity_radians_per_second) &&
           std::isfinite(observation.applied_torque_newton_metres) &&
           std::isfinite(observation.estimated_current_amperes) &&
           std::isfinite(observation.supply_voltage_volts) &&
           std::isfinite(observation.temperature_celsius);
}

[[nodiscard]] bool IsFiniteResult(const ServoStepResult& result) noexcept
{
    return std::isfinite(result.requested_torque_newton_metres) &&
           std::isfinite(result.estimated_current_amperes) &&
           result.estimated_current_amperes >= 0.0 &&
           std::isfinite(result.temperature_celsius);
}

[[nodiscard]] PowerThermalValidationError ValidateScalar(
    const PowerThermalScalar& scalar,
    bool allow_zero = false) noexcept
{
    if(scalar.evidence_id.empty())
    {
        return PowerThermalValidationError::MissingEvidence;
    }
    if(scalar.evidence_class == PowerThermalEvidenceClass::Calibrated)
    {
        return PowerThermalValidationError::CalibratedClaim;
    }
    if(!std::isfinite(scalar.value))
    {
        return PowerThermalValidationError::NonFiniteValue;
    }
    if(allow_zero ? scalar.value < 0.0 : scalar.value <= 0.0)
    {
        return allow_zero ? PowerThermalValidationError::InvalidNonNegativeValue
                          : PowerThermalValidationError::InvalidPositiveValue;
    }
    return PowerThermalValidationError::None;
}

[[nodiscard]] constexpr bool SameThermalValues(
    const ServoThermalParameters& left,
    const ServoThermalParameters& right) noexcept
{
    return left.winding_resistance_ohms.value == right.winding_resistance_ohms.value &&
           left.thermal_resistance_celsius_per_watt.value ==
               right.thermal_resistance_celsius_per_watt.value &&
           left.thermal_capacitance_joules_per_celsius.value ==
               right.thermal_capacitance_joules_per_celsius.value &&
           left.warning_temperature_celsius.value == right.warning_temperature_celsius.value &&
           left.shutdown_temperature_celsius.value == right.shutdown_temperature_celsius.value &&
           left.recovery_temperature_celsius.value == right.recovery_temperature_celsius.value;
}

[[nodiscard]] double ClampFiniteNonNegative(double value) noexcept
{
    if(!std::isfinite(value) || value <= 0.0)
    {
        return 0.0;
    }
    return value;
}

}  // namespace

PowerThermalModel::PowerThermalModel(
    const SharedPowerSupplyParameters& supply_parameters,
    const std::array<ServoModel*, kReachyPowerThermalActuatorCount>& servo_models,
    const std::array<const ServoThermalParameters*, kReachyPowerThermalActuatorCount>&
        thermal_parameters) noexcept
    : supply_parameters_(supply_parameters),
      servo_models_(servo_models),
      thermal_parameters_(thermal_parameters)
{
    configuration_valid_ =
        ValidateSharedPowerSupplyParameters(supply_parameters_) ==
        PowerThermalValidationError::None;
    for(std::size_t index = 0U; index < kReachyPowerThermalActuatorCount; ++index)
    {
        const ServoModel* const model = servo_models_[index];
        const ServoThermalParameters* const thermal = thermal_parameters_[index];
        if(model == nullptr || thermal == nullptr ||
           ValidateServoThermalParameters(*thermal) != PowerThermalValidationError::None ||
           model->Parameters().role != thermal->role)
        {
            configuration_valid_ = false;
        }
    }

    const double open_circuit_voltage = supply_parameters_.open_circuit_voltage_volts.value;
    last_bus_diagnostics_.open_circuit_voltage_volts = open_circuit_voltage;
    last_bus_diagnostics_.evaluation_voltage_volts = open_circuit_voltage;
    last_bus_diagnostics_.bus_voltage_volts = open_circuit_voltage;
}

void PowerThermalModel::Reset(
    const std::array<ServoObservation, kReachyPowerThermalActuatorCount>& observations,
    double ambient_temperature_celsius) noexcept
{
    const double open_circuit_voltage = supply_parameters_.open_circuit_voltage_volts.value;
    last_bus_diagnostics_ = {};
    last_bus_diagnostics_.open_circuit_voltage_volts = open_circuit_voltage;
    last_bus_diagnostics_.evaluation_voltage_volts = open_circuit_voltage;
    last_bus_diagnostics_.bus_voltage_volts = open_circuit_voltage;
    last_servo_diagnostics_ = {};

    for(std::size_t index = 0U; index < kReachyPowerThermalActuatorCount; ++index)
    {
        const ServoThermalParameters* const thermal = thermal_parameters_[index];
        const double shutdown_temperature =
            thermal != nullptr ? thermal->shutdown_temperature_celsius.value : 0.0;
        const double initial_temperature =
            IsFiniteObservation(observations[index])
                ? observations[index].temperature_celsius
                : ambient_temperature_celsius;
        temperatures_celsius_[index] = std::isfinite(initial_temperature)
                                           ? initial_temperature
                                           : 0.0;
        thermal_shutdown_latched_[index] =
            temperatures_celsius_[index] >= shutdown_temperature ||
            (observations[index].fault_flags & ToMask(ServoFaultFlag::OverTemperature)) != 0U;
        last_torque_enabled_[index] = false;
        last_observations_[index] = observations[index];
        last_observations_[index].supply_voltage_volts = open_circuit_voltage;
        last_observations_[index].temperature_celsius = temperatures_celsius_[index];
        last_fault_flags_[index] = observations[index].fault_flags;
        if(thermal_shutdown_latched_[index])
        {
            last_fault_flags_[index] |= ToMask(ServoFaultFlag::OverTemperature);
        }
        if(servo_models_[index] != nullptr)
        {
            servo_models_[index]->Reset(last_observations_[index]);
        }
        last_servo_diagnostics_[index].bus_voltage_volts = open_circuit_voltage;
        last_servo_diagnostics_[index].temperature_celsius = temperatures_celsius_[index];
        last_servo_diagnostics_[index].derating_factor = DeratingFactor(index);
        last_servo_diagnostics_[index].thermal_shutdown_latched =
            thermal_shutdown_latched_[index];
        last_servo_diagnostics_[index].fault_flags = last_fault_flags_[index];
    }
}

PowerThermalStepResult PowerThermalModel::Step(
    const std::array<ServoCommand, kReachyPowerThermalActuatorCount>& commands,
    const std::array<ServoObservation, kReachyPowerThermalActuatorCount>& observations,
    double ambient_temperature_celsius,
    double timestep_seconds) noexcept
{
    if(!ConfigurationValid() ||
       !InputsFinite(commands, observations, ambient_temperature_celsius, timestep_seconds))
    {
        return RejectedResult(observations);
    }

    PowerThermalStepResult output{};
    const double open_circuit_voltage = supply_parameters_.open_circuit_voltage_volts.value;
    const double previous_bus_voltage = last_bus_diagnostics_.bus_voltage_volts;
    const double evaluation_voltage = std::clamp(
        std::isfinite(previous_bus_voltage) && previous_bus_voltage > 0.0
            ? previous_bus_voltage
            : open_circuit_voltage,
        0.0,
        open_circuit_voltage);

    std::array<double, kReachyPowerThermalActuatorCount> requested_torques{};
    std::array<double, kReachyPowerThermalActuatorCount> requested_currents{};
    std::array<double, kReachyPowerThermalActuatorCount> applied_derating_factors{};
    std::array<std::uint32_t, kReachyPowerThermalActuatorCount> fault_flags{};
    double total_requested_current = 0.0;

    for(std::size_t index = 0U; index < kReachyPowerThermalActuatorCount; ++index)
    {
        if(temperatures_celsius_[index] >=
           thermal_parameters_[index]->shutdown_temperature_celsius.value)
        {
            thermal_shutdown_latched_[index] = true;
        }

        ServoCommand effective_command = commands[index];
        last_torque_enabled_[index] =
            commands[index].torque_enabled && commands[index].mode != ServoMode::Disabled;
        if(thermal_shutdown_latched_[index])
        {
            effective_command.torque_enabled = false;
        }

        ServoObservation effective_observation = observations[index];
        effective_observation.supply_voltage_volts = evaluation_voltage;
        effective_observation.temperature_celsius = temperatures_celsius_[index];
        if(thermal_shutdown_latched_[index])
        {
            effective_observation.fault_flags |= ToMask(ServoFaultFlag::OverTemperature);
        }

        const ServoStepResult inner_result = servo_models_[index]->Step(
            effective_command,
            effective_observation,
            timestep_seconds);
        if(!IsFiniteResult(inner_result))
        {
            return RejectedResult(observations);
        }

        const double derating_factor = DeratingFactor(index);
        applied_derating_factors[index] = derating_factor;
        requested_torques[index] = thermal_shutdown_latched_[index]
                                       ? 0.0
                                       : inner_result.requested_torque_newton_metres *
                                             derating_factor;
        requested_currents[index] = thermal_shutdown_latched_[index]
                                         ? 0.0
                                         : inner_result.estimated_current_amperes *
                                               derating_factor;
        total_requested_current += requested_currents[index];
        fault_flags[index] = inner_result.fault_flags;
        if(thermal_shutdown_latched_[index])
        {
            fault_flags[index] |= ToMask(ServoFaultFlag::OverTemperature);
        }
    }

    const double current_limit = supply_parameters_.current_limit_amperes.value;
    const double current_limit_scale = total_requested_current > current_limit
                                           ? current_limit / total_requested_current
                                           : 1.0;
    double voltage_scale = 1.0;
    double provisional_bus_voltage = open_circuit_voltage;
    const double source_resistance = supply_parameters_.source_resistance_ohms.value;
    for(std::size_t iteration = 0U; iteration < kVoltageSolveIterations; ++iteration)
    {
        const double provisional_current =
            total_requested_current * current_limit_scale * voltage_scale;
        provisional_bus_voltage = std::max(
            0.0,
            open_circuit_voltage - source_resistance * provisional_current);
        voltage_scale = evaluation_voltage > 0.0
                            ? std::clamp(
                                  provisional_bus_voltage / evaluation_voltage,
                                  0.0,
                                  1.0)
                            : 0.0;
    }

    const bool undervoltage =
        evaluation_voltage < supply_parameters_.minimum_bus_voltage_volts.value ||
        provisional_bus_voltage < supply_parameters_.minimum_bus_voltage_volts.value;
    double final_delivered_current = 0.0;

    for(std::size_t index = 0U; index < kReachyPowerThermalActuatorCount; ++index)
    {
        const double shared_scale = current_limit_scale * voltage_scale;
        double delivered_current = requested_currents[index] * shared_scale;
        double delivered_torque = requested_torques[index] * shared_scale;
        double heating_power = 0.0;
        double cooling_power = 0.0;
        UpdateTemperature(
            index,
            delivered_current,
            ambient_temperature_celsius,
            timestep_seconds,
            heating_power,
            cooling_power);

        if(temperatures_celsius_[index] >=
           thermal_parameters_[index]->shutdown_temperature_celsius.value)
        {
            thermal_shutdown_latched_[index] = true;
        }
        if(thermal_shutdown_latched_[index])
        {
            delivered_current = 0.0;
            delivered_torque = 0.0;
            fault_flags[index] |= ToMask(ServoFaultFlag::OverTemperature);
        }
        if(undervoltage)
        {
            fault_flags[index] |= ToMask(ServoFaultFlag::UnderVoltage);
        }

        final_delivered_current += delivered_current;
        output.servo_results[index] = ServoStepResult{
            delivered_torque,
            delivered_current,
            temperatures_celsius_[index],
            fault_flags[index],
        };
        output.servo_diagnostics[index] = ServoPowerThermalDiagnostics{
            requested_currents[index],
            delivered_current,
            0.0,
            temperatures_celsius_[index],
            heating_power,
            cooling_power,
            applied_derating_factors[index],
            thermal_shutdown_latched_[index],
            fault_flags[index],
        };

        last_observations_[index] = observations[index];
        last_observations_[index].supply_voltage_volts = provisional_bus_voltage;
        last_observations_[index].temperature_celsius = temperatures_celsius_[index];
        last_fault_flags_[index] = fault_flags[index];
    }

    const double final_bus_voltage = std::max(
        0.0,
        open_circuit_voltage - source_resistance * final_delivered_current);
    output.bus = PowerBusDiagnostics{
        open_circuit_voltage,
        evaluation_voltage,
        final_bus_voltage,
        open_circuit_voltage - final_bus_voltage,
        total_requested_current,
        final_delivered_current,
        current_limit_scale,
        voltage_scale,
        total_requested_current > current_limit,
        undervoltage ||
            final_bus_voltage < supply_parameters_.minimum_bus_voltage_volts.value,
    };
    for(std::size_t index = 0U; index < kReachyPowerThermalActuatorCount; ++index)
    {
        output.servo_diagnostics[index].bus_voltage_volts = final_bus_voltage;
    }

    last_bus_diagnostics_ = output.bus;
    last_servo_diagnostics_ = output.servo_diagnostics;
    return output;
}

ThermalFaultClearResult PowerThermalModel::ClearThermalShutdown(
    std::size_t actuator_index) noexcept
{
    if(!ConfigurationValid())
    {
        return ThermalFaultClearResult::InvalidConfiguration;
    }
    if(actuator_index >= kReachyPowerThermalActuatorCount)
    {
        return ThermalFaultClearResult::InvalidIndex;
    }
    if(!thermal_shutdown_latched_[actuator_index])
    {
        return ThermalFaultClearResult::NotLatched;
    }
    if(last_torque_enabled_[actuator_index])
    {
        return ThermalFaultClearResult::TorqueEnabled;
    }
    if(temperatures_celsius_[actuator_index] >
       thermal_parameters_[actuator_index]->recovery_temperature_celsius.value)
    {
        return ThermalFaultClearResult::AboveRecoveryTemperature;
    }

    thermal_shutdown_latched_[actuator_index] = false;
    ServoObservation reset_observation = last_observations_[actuator_index];
    reset_observation.supply_voltage_volts = last_bus_diagnostics_.bus_voltage_volts;
    reset_observation.temperature_celsius = temperatures_celsius_[actuator_index];
    reset_observation.fault_flags =
        last_fault_flags_[actuator_index] & ~ToMask(ServoFaultFlag::OverTemperature);
    servo_models_[actuator_index]->Reset(reset_observation);
    last_fault_flags_[actuator_index] = reset_observation.fault_flags;
    last_servo_diagnostics_[actuator_index].thermal_shutdown_latched = false;
    last_servo_diagnostics_[actuator_index].fault_flags = reset_observation.fault_flags;
    return ThermalFaultClearResult::Cleared;
}

const SharedPowerSupplyParameters& PowerThermalModel::SupplyParameters() const noexcept
{
    return supply_parameters_;
}

double PowerThermalModel::BusVoltageVolts() const noexcept
{
    return last_bus_diagnostics_.bus_voltage_volts;
}

double PowerThermalModel::ServoTemperatureCelsius(std::size_t actuator_index) const noexcept
{
    return actuator_index < kReachyPowerThermalActuatorCount
               ? temperatures_celsius_[actuator_index]
               : std::numeric_limits<double>::quiet_NaN();
}

bool PowerThermalModel::ThermalShutdownLatched(std::size_t actuator_index) const noexcept
{
    return actuator_index < kReachyPowerThermalActuatorCount &&
           thermal_shutdown_latched_[actuator_index];
}

PowerBusDiagnostics PowerThermalModel::LastBusDiagnostics() const noexcept
{
    return last_bus_diagnostics_;
}

ServoPowerThermalDiagnostics PowerThermalModel::LastServoDiagnostics(
    std::size_t actuator_index) const noexcept
{
    return actuator_index < kReachyPowerThermalActuatorCount
               ? last_servo_diagnostics_[actuator_index]
               : ServoPowerThermalDiagnostics{};
}

bool PowerThermalModel::ConfigurationValid() const noexcept
{
    return configuration_valid_;
}

bool PowerThermalModel::InputsFinite(
    const std::array<ServoCommand, kReachyPowerThermalActuatorCount>& commands,
    const std::array<ServoObservation, kReachyPowerThermalActuatorCount>& observations,
    double ambient_temperature_celsius,
    double timestep_seconds) const noexcept
{
    if(!std::isfinite(ambient_temperature_celsius) ||
       !std::isfinite(timestep_seconds) || timestep_seconds <= 0.0)
    {
        return false;
    }
    for(std::size_t index = 0U; index < kReachyPowerThermalActuatorCount; ++index)
    {
        if(!IsFiniteCommand(commands[index]) || !IsFiniteObservation(observations[index]))
        {
            return false;
        }
    }
    return true;
}

double PowerThermalModel::DeratingFactor(std::size_t actuator_index) const noexcept
{
    if(actuator_index >= kReachyPowerThermalActuatorCount ||
       thermal_shutdown_latched_[actuator_index] || thermal_parameters_[actuator_index] == nullptr)
    {
        return 0.0;
    }
    const ServoThermalParameters& parameters = *thermal_parameters_[actuator_index];
    const double temperature = temperatures_celsius_[actuator_index];
    const double warning = parameters.warning_temperature_celsius.value;
    const double shutdown = parameters.shutdown_temperature_celsius.value;
    if(temperature <= warning)
    {
        return 1.0;
    }
    if(temperature >= shutdown)
    {
        return 0.0;
    }
    return std::clamp((shutdown - temperature) / (shutdown - warning), 0.0, 1.0);
}

void PowerThermalModel::UpdateTemperature(
    std::size_t actuator_index,
    double delivered_current_amperes,
    double ambient_temperature_celsius,
    double timestep_seconds,
    double& heating_power_watts,
    double& cooling_power_watts) noexcept
{
    const ServoThermalParameters& parameters = *thermal_parameters_[actuator_index];
    const double current = ClampFiniteNonNegative(delivered_current_amperes);
    heating_power_watts = current * current * parameters.winding_resistance_ohms.value;
    cooling_power_watts = std::max(
        0.0,
        (temperatures_celsius_[actuator_index] - ambient_temperature_celsius) /
            parameters.thermal_resistance_celsius_per_watt.value);
    const double temperature_rate =
        (heating_power_watts - cooling_power_watts) /
        parameters.thermal_capacitance_joules_per_celsius.value;
    const double next_temperature =
        temperatures_celsius_[actuator_index] + temperature_rate * timestep_seconds;
    temperatures_celsius_[actuator_index] =
        std::max(ambient_temperature_celsius, next_temperature);
}

PowerThermalStepResult PowerThermalModel::RejectedResult(
    const std::array<ServoObservation, kReachyPowerThermalActuatorCount>& observations) noexcept
{
    PowerThermalStepResult output{};
    const double bus_voltage = std::isfinite(last_bus_diagnostics_.bus_voltage_volts)
                                   ? last_bus_diagnostics_.bus_voltage_volts
                                   : 0.0;
    output.bus = last_bus_diagnostics_;
    for(std::size_t index = 0U; index < kReachyPowerThermalActuatorCount; ++index)
    {
        const double temperature = std::isfinite(temperatures_celsius_[index])
                                       ? temperatures_celsius_[index]
                                       : observations[index].temperature_celsius;
        const std::uint32_t faults =
            observations[index].fault_flags | ToMask(ServoFaultFlag::ModelRejected);
        output.servo_results[index] = ServoStepResult{0.0, 0.0, temperature, faults};
        output.servo_diagnostics[index] = ServoPowerThermalDiagnostics{
            0.0,
            0.0,
            bus_voltage,
            temperature,
            0.0,
            0.0,
            0.0,
            thermal_shutdown_latched_[index],
            faults,
        };
        last_fault_flags_[index] = faults;
    }
    last_servo_diagnostics_ = output.servo_diagnostics;
    return output;
}

std::string_view ToString(PowerThermalEvidenceClass value) noexcept
{
    switch(value)
    {
        case PowerThermalEvidenceClass::ManufacturerDerived:
            return "manufacturer_derived";
        case PowerThermalEvidenceClass::EngineeringEstimate:
            return "engineering_estimate";
        case PowerThermalEvidenceClass::Calibrated:
            return "calibrated";
    }
    return "unknown";
}

std::string_view ToString(PowerThermalValidationError value) noexcept
{
    switch(value)
    {
        case PowerThermalValidationError::None:
            return "none";
        case PowerThermalValidationError::EmptyIdentity:
            return "empty_identity";
        case PowerThermalValidationError::MissingEvidence:
            return "missing_evidence";
        case PowerThermalValidationError::CalibratedClaim:
            return "calibrated_claim";
        case PowerThermalValidationError::NonFiniteValue:
            return "non_finite_value";
        case PowerThermalValidationError::InvalidPositiveValue:
            return "invalid_positive_value";
        case PowerThermalValidationError::InvalidNonNegativeValue:
            return "invalid_non_negative_value";
        case PowerThermalValidationError::InvalidVoltageOrder:
            return "invalid_voltage_order";
        case PowerThermalValidationError::InvalidTemperatureOrder:
            return "invalid_temperature_order";
        case PowerThermalValidationError::RoleMismatch:
            return "role_mismatch";
        case PowerThermalValidationError::DuplicateCrossRoleParameters:
            return "duplicate_cross_role_parameters";
        case PowerThermalValidationError::InvalidBindingCount:
            return "invalid_binding_count";
        case PowerThermalValidationError::InvalidCurrentBudget:
            return "invalid_current_budget";
    }
    return "unknown";
}

std::string_view ToString(ThermalFaultClearResult value) noexcept
{
    switch(value)
    {
        case ThermalFaultClearResult::Cleared:
            return "cleared";
        case ThermalFaultClearResult::InvalidIndex:
            return "invalid_index";
        case ThermalFaultClearResult::NotLatched:
            return "not_latched";
        case ThermalFaultClearResult::TorqueEnabled:
            return "torque_enabled";
        case ThermalFaultClearResult::AboveRecoveryTemperature:
            return "above_recovery_temperature";
        case ThermalFaultClearResult::InvalidConfiguration:
            return "invalid_configuration";
    }
    return "unknown";
}

PowerThermalValidationError ValidateSharedPowerSupplyParameters(
    const SharedPowerSupplyParameters& parameters) noexcept
{
    if(parameters.id.empty())
    {
        return PowerThermalValidationError::EmptyIdentity;
    }
    if(parameters.source_evidence_id.empty())
    {
        return PowerThermalValidationError::MissingEvidence;
    }
    if(parameters.overall_evidence_class == PowerThermalEvidenceClass::Calibrated)
    {
        return PowerThermalValidationError::CalibratedClaim;
    }
    const std::array<const PowerThermalScalar SharedPowerSupplyParameters::*, 4> required{{
        &SharedPowerSupplyParameters::open_circuit_voltage_volts,
        &SharedPowerSupplyParameters::source_resistance_ohms,
        &SharedPowerSupplyParameters::current_limit_amperes,
        &SharedPowerSupplyParameters::minimum_bus_voltage_volts,
    }};
    for(const auto member : required)
    {
        const PowerThermalValidationError error = ValidateScalar(parameters.*member);
        if(error != PowerThermalValidationError::None)
        {
            return error;
        }
    }
    if(parameters.minimum_bus_voltage_volts.value >=
       parameters.open_circuit_voltage_volts.value)
    {
        return PowerThermalValidationError::InvalidVoltageOrder;
    }
    return PowerThermalValidationError::None;
}

PowerThermalValidationError ValidateServoThermalParameters(
    const ServoThermalParameters& parameters) noexcept
{
    if(parameters.id.empty())
    {
        return PowerThermalValidationError::EmptyIdentity;
    }
    if(parameters.source_evidence_id.empty())
    {
        return PowerThermalValidationError::MissingEvidence;
    }
    if(parameters.overall_evidence_class == PowerThermalEvidenceClass::Calibrated)
    {
        return PowerThermalValidationError::CalibratedClaim;
    }
    const std::array<const PowerThermalScalar ServoThermalParameters::*, 6> required{{
        &ServoThermalParameters::winding_resistance_ohms,
        &ServoThermalParameters::thermal_resistance_celsius_per_watt,
        &ServoThermalParameters::thermal_capacitance_joules_per_celsius,
        &ServoThermalParameters::warning_temperature_celsius,
        &ServoThermalParameters::shutdown_temperature_celsius,
        &ServoThermalParameters::recovery_temperature_celsius,
    }};
    for(const auto member : required)
    {
        const PowerThermalValidationError error = ValidateScalar(parameters.*member);
        if(error != PowerThermalValidationError::None)
        {
            return error;
        }
    }
    if(!(parameters.recovery_temperature_celsius.value <
             parameters.warning_temperature_celsius.value &&
         parameters.warning_temperature_celsius.value <
             parameters.shutdown_temperature_celsius.value))
    {
        return PowerThermalValidationError::InvalidTemperatureOrder;
    }
    return PowerThermalValidationError::None;
}

const SharedPowerSupplyParameters& EngineeringSharedPowerSupply() noexcept
{
    return generated::kSharedPowerSupply;
}

const std::array<ServoThermalParameters, 3>&
EngineeringServoThermalBaselines() noexcept
{
    return generated::kServoThermalBaselines;
}

const std::array<ServoActuatorBinding, kReachyPowerThermalActuatorCount>&
PowerThermalBindings() noexcept
{
    return generated::kPowerThermalBindings;
}

const ServoThermalParameters* FindServoThermalBaseline(std::string_view id) noexcept
{
    const auto& baselines = EngineeringServoThermalBaselines();
    const auto found = std::find_if(
        baselines.begin(),
        baselines.end(),
        [id](const ServoThermalParameters& baseline) { return baseline.id == id; });
    return found == baselines.end() ? nullptr : &*found;
}

const ServoThermalParameters* FindServoThermalBaselineForActuator(
    std::string_view actuator_name) noexcept
{
    const auto& bindings = PowerThermalBindings();
    const auto found = std::find_if(
        bindings.begin(),
        bindings.end(),
        [actuator_name](const ServoActuatorBinding& binding) {
            return binding.actuator_name == actuator_name;
        });
    return found == bindings.end() ? nullptr : FindServoThermalBaseline(found->parameter_set_id);
}

static_assert(!SameThermalValues(
    generated::kServoThermalBaselines[0U],
    generated::kServoThermalBaselines[1U]));
static_assert(!SameThermalValues(
    generated::kServoThermalBaselines[0U],
    generated::kServoThermalBaselines[2U]));
static_assert(!SameThermalValues(
    generated::kServoThermalBaselines[1U],
    generated::kServoThermalBaselines[2U]));

}  // namespace reachy::servo
