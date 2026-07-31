#include "reachy_electrical_servo_model.hpp"

#include "reachy_electrical_baseline.generated.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <limits>

namespace reachy::servo {
namespace {

constexpr double kTimeToleranceSeconds = 1.0e-12;
constexpr std::uint32_t kVoltageFaultMask =
    ToMask(ServoFaultFlag::UnderVoltage) | ToMask(ServoFaultFlag::OverVoltage);

[[nodiscard]] double RequiredValue(const QualifiedScalar& scalar) noexcept
{
    return scalar.value.value_or(std::numeric_limits<double>::quiet_NaN());
}

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
    return std::isfinite(command.sample_time_seconds) &&
           command.sample_time_seconds >= 0.0 && IsKnownMode(command.mode) &&
           std::isfinite(command.target_position_radians) &&
           std::isfinite(command.target_velocity_radians_per_second) &&
           std::isfinite(command.profile_velocity_radians_per_second) &&
           command.profile_velocity_radians_per_second >= 0.0 &&
           std::isfinite(command.profile_acceleration_radians_per_second_squared) &&
           command.profile_acceleration_radians_per_second_squared >= 0.0 &&
           std::isfinite(command.feedforward_torque_newton_metres);
}

[[nodiscard]] bool IsFiniteObservation(const ServoObservation& observation) noexcept
{
    return std::isfinite(observation.sample_time_seconds) &&
           observation.sample_time_seconds >= 0.0 &&
           std::isfinite(observation.position_radians) &&
           std::isfinite(observation.velocity_radians_per_second) &&
           std::isfinite(observation.applied_torque_newton_metres) &&
           std::isfinite(observation.estimated_current_amperes) &&
           std::isfinite(observation.supply_voltage_volts) &&
           std::isfinite(observation.temperature_celsius);
}

[[nodiscard]] ElectricalValidationError ValidateControllerScalar(
    const QualifiedScalar& scalar,
    bool allow_zero = false) noexcept
{
    if(scalar.evidence_id.empty())
    {
        return ElectricalValidationError::MissingEvidence;
    }
    if(!scalar.value.has_value())
    {
        return ElectricalValidationError::PlaceholderValue;
    }
    if(scalar.quality == ParameterQuality::Placeholder)
    {
        return ElectricalValidationError::PlaceholderValue;
    }
    if(scalar.quality == ParameterQuality::Calibrated)
    {
        return ElectricalValidationError::CalibratedClaim;
    }
    const double value = *scalar.value;
    if(!std::isfinite(value))
    {
        return ElectricalValidationError::InvalidPositiveValue;
    }
    if(allow_zero ? value < 0.0 : value <= 0.0)
    {
        return allow_zero ? ElectricalValidationError::InvalidNonNegativeValue
                          : ElectricalValidationError::InvalidPositiveValue;
    }
    return ElectricalValidationError::None;
}

[[nodiscard]] double MoveTowards(double current, double target, double maximum_delta) noexcept
{
    if(current < target)
    {
        return std::min(current + maximum_delta, target);
    }
    return std::max(current - maximum_delta, target);
}

}  // namespace

ElectricalServoModel::ElectricalServoModel(const ElectricalServoBaseline& baseline) noexcept
    : baseline_(baseline),
      configuration_valid_(ValidateElectricalBaseline(baseline) == ElectricalValidationError::None)
{
}

const ServoParameterSet& ElectricalServoModel::Parameters() const noexcept
{
    return baseline_.servo_parameters;
}

const ElectricalControllerParameters& ElectricalServoModel::ControllerParameters() const noexcept
{
    return baseline_.controller_parameters;
}

void ElectricalServoModel::Reset(const ServoObservation& observation)
{
    pending_commands_ = {};
    pending_command_count_ = 0U;
    active_command_ = {};
    has_active_command_ = false;
    has_queued_sequence_ = false;
    last_queued_sequence_ = 0U;
    last_command_sample_slot_seconds_ = 0.0;
    last_applied_sample_time_seconds_ = 0.0;
    peak_current_elapsed_seconds_ = 0.0;
    latched_fault_flags_ = 0U;

    if(!configuration_valid_ || !IsFiniteObservation(observation))
    {
        profile_position_radians_ = 0.0;
        profile_velocity_radians_per_second_ = 0.0;
        latched_fault_flags_ |= ToMask(ServoFaultFlag::ModelRejected);
        return;
    }

    profile_position_radians_ = QuantizeToIncrement(
        observation.position_radians,
        RequiredValue(Parameters().encoder_position_quantum_radians));
    profile_velocity_radians_per_second_ = QuantizeToIncrement(
        observation.velocity_radians_per_second,
        RequiredValue(Parameters().encoder_velocity_quantum_radians_per_second));
    const std::uint32_t supported = observation.fault_flags & Parameters().supported_fault_mask;
    latched_fault_flags_ |= supported & Parameters().latching_fault_mask;
}

ServoStepResult ElectricalServoModel::Step(
    const ServoCommand& command,
    const ServoObservation& observation,
    double timestep_seconds)
{
    if(!ConfigurationValid() || !InputsFinite(command, observation, timestep_seconds))
    {
        latched_fault_flags_ |= ToMask(ServoFaultFlag::ModelRejected);
        return ZeroTorqueResult(
            observation,
            latched_fault_flags_ | ToMask(ServoFaultFlag::ModelRejected));
    }

    IngestCommand(command);
    ApplyDueCommands(observation.sample_time_seconds);
    std::uint32_t faults = UpdateFaults(observation);

    if(!has_active_command_ || !active_command_.torque_enabled ||
       active_command_.mode == ServoMode::Disabled ||
       (faults & (Parameters().latching_fault_mask | kVoltageFaultMask)) != 0U)
    {
        peak_current_elapsed_seconds_ = 0.0;
        return ZeroTorqueResult(observation, faults);
    }

    UpdateProfile(timestep_seconds);

    const double measured_position = QuantizeToIncrement(
        observation.position_radians,
        RequiredValue(Parameters().encoder_position_quantum_radians));
    const double measured_velocity = QuantizeToIncrement(
        observation.velocity_radians_per_second,
        RequiredValue(Parameters().encoder_velocity_quantum_radians_per_second));
    const ElectricalControllerParameters& controller = ControllerParameters();

    double raw_torque = active_command_.feedforward_torque_newton_metres;
    switch(active_command_.mode)
    {
        case ServoMode::Position:
            raw_torque +=
                RequiredValue(controller.position_gain_newton_metres_per_radian) *
                    (profile_position_radians_ - measured_position) +
                RequiredValue(controller.velocity_gain_newton_metre_seconds_per_radian) *
                    (profile_velocity_radians_per_second_ - measured_velocity);
            break;
        case ServoMode::Velocity:
            raw_torque +=
                RequiredValue(controller.velocity_gain_newton_metre_seconds_per_radian) *
                (profile_velocity_radians_per_second_ - measured_velocity);
            break;
        case ServoMode::Torque:
            break;
        case ServoMode::Disabled:
            return ZeroTorqueResult(observation, faults);
    }

    const double reference_voltage =
        RequiredValue(controller.performance_reference_voltage_volts);
    const double voltage_factor = std::clamp(
        observation.supply_voltage_volts / reference_voltage,
        0.0,
        1.0);
    const double stall_torque =
        RequiredValue(Parameters().stall_torque_newton_metres);
    const double no_load_speed =
        RequiredValue(Parameters().no_load_speed_radians_per_second);
    const double peak_current =
        RequiredValue(Parameters().peak_current_limit_amperes);
    const double continuous_current =
        RequiredValue(Parameters().continuous_current_limit_amperes);
    const double peak_duration =
        RequiredValue(Parameters().peak_current_duration_seconds);
    const double torque_constant = stall_torque / peak_current;
    const double scaled_no_load_speed = no_load_speed * voltage_factor;
    const double speed_factor = scaled_no_load_speed > 0.0
                                    ? std::clamp(
                                          1.0 - std::fabs(measured_velocity) /
                                                    scaled_no_load_speed,
                                          0.0,
                                          1.0)
                                    : 0.0;
    const double torque_speed_limit = stall_torque * voltage_factor * speed_factor;
    const double requested_current = std::fabs(raw_torque) / torque_constant;

    if(requested_current > continuous_current)
    {
        peak_current_elapsed_seconds_ += timestep_seconds;
    }
    else
    {
        peak_current_elapsed_seconds_ = 0.0;
    }

    if(peak_current_elapsed_seconds_ > peak_duration + kTimeToleranceSeconds)
    {
        latched_fault_flags_ |= ToMask(ServoFaultFlag::OverCurrent);
        faults |= ToMask(ServoFaultFlag::OverCurrent);
        return ZeroTorqueResult(observation, faults);
    }

    const double current_limit = requested_current > continuous_current
                                     ? peak_current
                                     : continuous_current;
    const double current_torque_limit = current_limit * torque_constant;
    const double torque_limit = std::min(torque_speed_limit, current_torque_limit);
    const double torque = std::clamp(raw_torque, -torque_limit, torque_limit);
    const double estimated_current = std::fabs(torque) / torque_constant;

    return ServoStepResult{
        torque,
        estimated_current,
        observation.temperature_celsius,
        faults,
    };
}

std::uint64_t ElectricalServoModel::ActiveCommandSequence() const noexcept
{
    return has_active_command_ ? active_command_.sequence : UINT64_C(0);
}

std::uint32_t ElectricalServoModel::LatchedFaultFlags() const noexcept
{
    return latched_fault_flags_;
}

std::size_t ElectricalServoModel::PendingCommandCount() const noexcept
{
    return pending_command_count_;
}

double ElectricalServoModel::LastAppliedSampleTimeSeconds() const noexcept
{
    return last_applied_sample_time_seconds_;
}

bool ElectricalServoModel::ConfigurationValid() const noexcept
{
    return configuration_valid_;
}

bool ElectricalServoModel::InputsFinite(
    const ServoCommand& command,
    const ServoObservation& observation,
    double timestep_seconds) const noexcept
{
    return std::isfinite(timestep_seconds) && timestep_seconds > 0.0 &&
           IsFiniteCommand(command) && IsFiniteObservation(observation);
}

void ElectricalServoModel::IngestCommand(const ServoCommand& command) noexcept
{
    if(has_queued_sequence_ && command.sequence < last_queued_sequence_)
    {
        latched_fault_flags_ |= ToMask(ServoFaultFlag::Communication);
        return;
    }
    if(has_queued_sequence_ && command.sequence == last_queued_sequence_)
    {
        return;
    }
    if(pending_command_count_ >= pending_commands_.size())
    {
        latched_fault_flags_ |= ToMask(ServoFaultFlag::Communication);
        return;
    }

    const double sample_period =
        RequiredValue(Parameters().command_sample_period_seconds);
    const double latency = RequiredValue(Parameters().command_latency_seconds);
    const double sample_slot = has_queued_sequence_
                                   ? std::max(
                                         command.sample_time_seconds,
                                         last_command_sample_slot_seconds_ + sample_period)
                                   : command.sample_time_seconds;
    pending_commands_[pending_command_count_] = PendingCommand{
        command,
        sample_slot + latency,
    };
    ++pending_command_count_;
    has_queued_sequence_ = true;
    last_queued_sequence_ = command.sequence;
    last_command_sample_slot_seconds_ = sample_slot;
}

void ElectricalServoModel::ApplyDueCommands(double observation_time_seconds) noexcept
{
    while(pending_command_count_ > 0U &&
          pending_commands_[0U].apply_time_seconds <=
              observation_time_seconds + kTimeToleranceSeconds)
    {
        active_command_ = pending_commands_[0U].command;
        has_active_command_ = true;
        last_applied_sample_time_seconds_ = active_command_.sample_time_seconds;
        for(std::size_t index = 1U; index < pending_command_count_; ++index)
        {
            pending_commands_[index - 1U] = pending_commands_[index];
        }
        --pending_command_count_;
    }
}

void ElectricalServoModel::UpdateProfile(double timestep_seconds) noexcept
{
    const ElectricalControllerParameters& controller = ControllerParameters();
    const double target_position = QuantizeToIncrement(
        active_command_.target_position_radians,
        RequiredValue(controller.target_position_quantum_radians));
    const double target_velocity = QuantizeToIncrement(
        active_command_.target_velocity_radians_per_second,
        RequiredValue(controller.target_velocity_quantum_radians_per_second));
    const double profile_speed = active_command_.profile_velocity_radians_per_second;
    const double profile_acceleration =
        active_command_.profile_acceleration_radians_per_second_squared;

    if(active_command_.mode == ServoMode::Position)
    {
        if(profile_speed <= 0.0 || profile_acceleration <= 0.0)
        {
            profile_position_radians_ = target_position;
            profile_velocity_radians_per_second_ = target_velocity;
            return;
        }
        const double position_error = target_position - profile_position_radians_;
        const double desired_velocity = std::clamp(
            position_error / timestep_seconds + target_velocity,
            -profile_speed,
            profile_speed);
        profile_velocity_radians_per_second_ = MoveTowards(
            profile_velocity_radians_per_second_,
            desired_velocity,
            profile_acceleration * timestep_seconds);
        const double next_position =
            profile_position_radians_ + profile_velocity_radians_per_second_ * timestep_seconds;
        if((position_error > 0.0 && next_position >= target_position) ||
           (position_error < 0.0 && next_position <= target_position))
        {
            profile_position_radians_ = target_position;
            profile_velocity_radians_per_second_ = target_velocity;
        }
        else
        {
            profile_position_radians_ = next_position;
        }
        return;
    }

    if(active_command_.mode == ServoMode::Velocity)
    {
        const double bounded_velocity = profile_speed > 0.0
                                            ? std::clamp(
                                                  target_velocity,
                                                  -profile_speed,
                                                  profile_speed)
                                            : target_velocity;
        profile_velocity_radians_per_second_ = profile_acceleration > 0.0
                                                    ? MoveTowards(
                                                          profile_velocity_radians_per_second_,
                                                          bounded_velocity,
                                                          profile_acceleration * timestep_seconds)
                                                    : bounded_velocity;
        profile_position_radians_ +=
            profile_velocity_radians_per_second_ * timestep_seconds;
    }
}

std::uint32_t ElectricalServoModel::UpdateFaults(
    const ServoObservation& observation) noexcept
{
    const ServoParameterSet& parameters = Parameters();
    const std::uint32_t observed = observation.fault_flags & parameters.supported_fault_mask;
    latched_fault_flags_ |= observed & parameters.latching_fault_mask;

    if(std::fabs(observation.estimated_current_amperes) >
       RequiredValue(parameters.peak_current_limit_amperes))
    {
        latched_fault_flags_ |= ToMask(ServoFaultFlag::OverCurrent);
    }
    if(observation.temperature_celsius >=
       RequiredValue(parameters.shutdown_temperature_celsius))
    {
        latched_fault_flags_ |= ToMask(ServoFaultFlag::OverTemperature);
    }

    std::uint32_t transient = observed & ~parameters.latching_fault_mask;
    if(observation.supply_voltage_volts <
       RequiredValue(parameters.minimum_voltage_volts))
    {
        transient |= ToMask(ServoFaultFlag::UnderVoltage);
    }
    if(observation.supply_voltage_volts >
       RequiredValue(parameters.maximum_voltage_volts))
    {
        transient |= ToMask(ServoFaultFlag::OverVoltage);
    }
    return transient | latched_fault_flags_;
}

ServoStepResult ElectricalServoModel::ZeroTorqueResult(
    const ServoObservation& observation,
    std::uint32_t faults) const noexcept
{
    const double temperature = std::isfinite(observation.temperature_celsius)
                                   ? observation.temperature_celsius
                                   : RequiredValue(Parameters().ambient_temperature_celsius);
    return ServoStepResult{0.0, 0.0, temperature, faults};
}

std::string_view ToString(ElectricalValidationError error) noexcept
{
    switch(error)
    {
        case ElectricalValidationError::None:
            return "none";
        case ElectricalValidationError::InvalidServoParameters:
            return "invalid_servo_parameters";
        case ElectricalValidationError::EmptyIdentity:
            return "empty_identity";
        case ElectricalValidationError::MissingEvidence:
            return "missing_evidence";
        case ElectricalValidationError::RoleMismatch:
            return "role_mismatch";
        case ElectricalValidationError::PlaceholderValue:
            return "placeholder_value";
        case ElectricalValidationError::CalibratedClaim:
            return "calibrated_claim";
        case ElectricalValidationError::InvalidPositiveValue:
            return "invalid_positive_value";
        case ElectricalValidationError::InvalidNonNegativeValue:
            return "invalid_non_negative_value";
        case ElectricalValidationError::InvalidReferenceVoltage:
            return "invalid_reference_voltage";
    }
    return "unknown";
}

double QuantizeToIncrement(double value, double quantum) noexcept
{
    if(!std::isfinite(value) || !std::isfinite(quantum) || quantum <= 0.0)
    {
        return std::numeric_limits<double>::quiet_NaN();
    }
    return std::round(value / quantum) * quantum;
}

ElectricalValidationError ValidateElectricalBaseline(
    const ElectricalServoBaseline& baseline) noexcept
{
    const ServoParameterSet& servo = baseline.servo_parameters;
    const ElectricalControllerParameters& controller = baseline.controller_parameters;
    if(ValidateParameterSet(servo) != ParameterValidationError::None ||
       !IsTorqueModelReady(servo))
    {
        return ElectricalValidationError::InvalidServoParameters;
    }
    if(servo.id.empty() || controller.id.empty() || controller.source_evidence_id.empty())
    {
        return ElectricalValidationError::EmptyIdentity;
    }
    if(servo.role != controller.role)
    {
        return ElectricalValidationError::RoleMismatch;
    }
    if(servo.overall_quality == ParameterQuality::Placeholder ||
       servo.fault_model_quality == ParameterQuality::Placeholder ||
       controller.overall_quality == ParameterQuality::Placeholder)
    {
        return ElectricalValidationError::PlaceholderValue;
    }
    if(servo.overall_quality == ParameterQuality::Calibrated ||
       servo.fault_model_quality == ParameterQuality::Calibrated ||
       controller.overall_quality == ParameterQuality::Calibrated)
    {
        return ElectricalValidationError::CalibratedClaim;
    }

    constexpr std::array<const QualifiedScalar ElectricalControllerParameters::*, 5>
        required{{
            &ElectricalControllerParameters::target_position_quantum_radians,
            &ElectricalControllerParameters::target_velocity_quantum_radians_per_second,
            &ElectricalControllerParameters::position_gain_newton_metres_per_radian,
            &ElectricalControllerParameters::velocity_gain_newton_metre_seconds_per_radian,
            &ElectricalControllerParameters::performance_reference_voltage_volts,
        }};
    for(const auto member : required)
    {
        const ElectricalValidationError error = ValidateControllerScalar(controller.*member);
        if(error != ElectricalValidationError::None)
        {
            return error;
        }
    }

    const double reference_voltage =
        RequiredValue(controller.performance_reference_voltage_volts);
    if(reference_voltage < RequiredValue(servo.minimum_voltage_volts) ||
       reference_voltage > RequiredValue(servo.maximum_voltage_volts))
    {
        return ElectricalValidationError::InvalidReferenceVoltage;
    }
    return ElectricalValidationError::None;
}

const std::array<ElectricalServoBaseline, 3>&
ManufacturerElectricalBaselines() noexcept
{
    return generated::kElectricalBaselines;
}

const std::array<ServoActuatorBinding, 9>&
ManufacturerElectricalBindings() noexcept
{
    return generated::kElectricalBindings;
}

const ElectricalServoBaseline* FindElectricalBaseline(std::string_view id) noexcept
{
    const auto& baselines = ManufacturerElectricalBaselines();
    const auto found = std::find_if(
        baselines.begin(),
        baselines.end(),
        [id](const ElectricalServoBaseline& baseline) {
            return baseline.servo_parameters.id == id;
        });
    return found == baselines.end() ? nullptr : &*found;
}

const ElectricalServoBaseline* FindElectricalBaselineForActuator(
    std::string_view actuator_name) noexcept
{
    const auto& bindings = ManufacturerElectricalBindings();
    const auto found = std::find_if(
        bindings.begin(),
        bindings.end(),
        [actuator_name](const ServoActuatorBinding& binding) {
            return binding.actuator_name == actuator_name;
        });
    return found == bindings.end() ? nullptr : FindElectricalBaseline(found->parameter_set_id);
}

}  // namespace reachy::servo
