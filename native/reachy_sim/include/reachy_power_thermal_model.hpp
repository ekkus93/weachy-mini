#ifndef REACHY_POWER_THERMAL_MODEL_HPP
#define REACHY_POWER_THERMAL_MODEL_HPP

#include "reachy_servo_model.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace reachy::servo {

inline constexpr std::size_t kReachyPowerThermalActuatorCount = 9U;

enum class PowerThermalEvidenceClass : std::uint8_t {
    ManufacturerDerived = 0,
    EngineeringEstimate = 1,
    Calibrated = 2,
};

enum class PowerThermalValidationError : std::uint8_t {
    None = 0,
    EmptyIdentity,
    MissingEvidence,
    CalibratedClaim,
    NonFiniteValue,
    InvalidPositiveValue,
    InvalidNonNegativeValue,
    InvalidVoltageOrder,
    InvalidTemperatureOrder,
    RoleMismatch,
    DuplicateCrossRoleParameters,
    InvalidBindingCount,
    InvalidCurrentBudget,
};

enum class ThermalFaultClearResult : std::uint8_t {
    Cleared = 0,
    InvalidIndex,
    NotLatched,
    TorqueEnabled,
    AboveRecoveryTemperature,
    InvalidConfiguration,
};

struct PowerThermalScalar {
    double value;
    PowerThermalEvidenceClass evidence_class;
    std::string_view evidence_id;
};

struct SharedPowerSupplyParameters {
    std::string_view id;
    PowerThermalEvidenceClass overall_evidence_class;
    std::string_view source_evidence_id;
    PowerThermalScalar open_circuit_voltage_volts;
    PowerThermalScalar source_resistance_ohms;
    PowerThermalScalar current_limit_amperes;
    PowerThermalScalar minimum_bus_voltage_volts;
};

struct ServoThermalParameters {
    std::string_view id;
    ActuatorRole role;
    PowerThermalEvidenceClass overall_evidence_class;
    std::string_view source_evidence_id;
    PowerThermalScalar winding_resistance_ohms;
    PowerThermalScalar thermal_resistance_celsius_per_watt;
    PowerThermalScalar thermal_capacitance_joules_per_celsius;
    PowerThermalScalar warning_temperature_celsius;
    PowerThermalScalar shutdown_temperature_celsius;
    PowerThermalScalar recovery_temperature_celsius;
};

struct PowerBusDiagnostics {
    double open_circuit_voltage_volts{0.0};
    double evaluation_voltage_volts{0.0};
    double bus_voltage_volts{0.0};
    double source_voltage_drop_volts{0.0};
    double requested_current_amperes{0.0};
    double delivered_current_amperes{0.0};
    double current_limit_scale{1.0};
    double voltage_scale{1.0};
    bool current_limited{false};
    bool undervoltage{false};
};

struct ServoPowerThermalDiagnostics {
    double requested_current_amperes{0.0};
    double delivered_current_amperes{0.0};
    double bus_voltage_volts{0.0};
    double temperature_celsius{0.0};
    double heating_power_watts{0.0};
    double cooling_power_watts{0.0};
    double derating_factor{1.0};
    bool thermal_shutdown_latched{false};
    std::uint32_t fault_flags{0U};
};

struct PowerThermalStepResult {
    std::array<ServoStepResult, kReachyPowerThermalActuatorCount> servo_results{};
    std::array<ServoPowerThermalDiagnostics, kReachyPowerThermalActuatorCount>
        servo_diagnostics{};
    PowerBusDiagnostics bus{};
};

class PowerThermalModel final {
public:
    PowerThermalModel(
        const SharedPowerSupplyParameters& supply_parameters,
        const std::array<ServoModel*, kReachyPowerThermalActuatorCount>& servo_models,
        const std::array<const ServoThermalParameters*, kReachyPowerThermalActuatorCount>&
            thermal_parameters) noexcept;

    void Reset(
        const std::array<ServoObservation, kReachyPowerThermalActuatorCount>& observations,
        double ambient_temperature_celsius) noexcept;

    [[nodiscard]] PowerThermalStepResult Step(
        const std::array<ServoCommand, kReachyPowerThermalActuatorCount>& commands,
        const std::array<ServoObservation, kReachyPowerThermalActuatorCount>& observations,
        double ambient_temperature_celsius,
        double timestep_seconds) noexcept;

    [[nodiscard]] ThermalFaultClearResult ClearThermalShutdown(
        std::size_t actuator_index) noexcept;

    [[nodiscard]] const SharedPowerSupplyParameters& SupplyParameters() const noexcept;
    [[nodiscard]] double BusVoltageVolts() const noexcept;
    [[nodiscard]] double ServoTemperatureCelsius(std::size_t actuator_index) const noexcept;
    [[nodiscard]] bool ThermalShutdownLatched(std::size_t actuator_index) const noexcept;
    [[nodiscard]] PowerBusDiagnostics LastBusDiagnostics() const noexcept;
    [[nodiscard]] ServoPowerThermalDiagnostics LastServoDiagnostics(
        std::size_t actuator_index) const noexcept;

private:
    [[nodiscard]] bool ConfigurationValid() const noexcept;
    [[nodiscard]] bool InputsFinite(
        const std::array<ServoCommand, kReachyPowerThermalActuatorCount>& commands,
        const std::array<ServoObservation, kReachyPowerThermalActuatorCount>& observations,
        double ambient_temperature_celsius,
        double timestep_seconds) const noexcept;
    [[nodiscard]] double DeratingFactor(std::size_t actuator_index) const noexcept;
    void UpdateTemperature(
        std::size_t actuator_index,
        double delivered_current_amperes,
        double ambient_temperature_celsius,
        double timestep_seconds,
        double& heating_power_watts,
        double& cooling_power_watts) noexcept;
    [[nodiscard]] PowerThermalStepResult RejectedResult(
        const std::array<ServoObservation, kReachyPowerThermalActuatorCount>& observations) noexcept;

    const SharedPowerSupplyParameters& supply_parameters_;
    std::array<ServoModel*, kReachyPowerThermalActuatorCount> servo_models_{};
    std::array<const ServoThermalParameters*, kReachyPowerThermalActuatorCount>
        thermal_parameters_{};
    std::array<double, kReachyPowerThermalActuatorCount> temperatures_celsius_{};
    std::array<bool, kReachyPowerThermalActuatorCount> thermal_shutdown_latched_{};
    std::array<bool, kReachyPowerThermalActuatorCount> last_torque_enabled_{};
    std::array<ServoObservation, kReachyPowerThermalActuatorCount> last_observations_{};
    std::array<std::uint32_t, kReachyPowerThermalActuatorCount> last_fault_flags_{};
    PowerBusDiagnostics last_bus_diagnostics_{};
    std::array<ServoPowerThermalDiagnostics, kReachyPowerThermalActuatorCount>
        last_servo_diagnostics_{};
    bool configuration_valid_{false};
};

[[nodiscard]] std::string_view ToString(PowerThermalEvidenceClass value) noexcept;
[[nodiscard]] std::string_view ToString(PowerThermalValidationError value) noexcept;
[[nodiscard]] std::string_view ToString(ThermalFaultClearResult value) noexcept;
[[nodiscard]] PowerThermalValidationError ValidateSharedPowerSupplyParameters(
    const SharedPowerSupplyParameters& parameters) noexcept;
[[nodiscard]] PowerThermalValidationError ValidateServoThermalParameters(
    const ServoThermalParameters& parameters) noexcept;
[[nodiscard]] const SharedPowerSupplyParameters& EngineeringSharedPowerSupply() noexcept;
[[nodiscard]] const std::array<ServoThermalParameters, 3>&
EngineeringServoThermalBaselines() noexcept;
[[nodiscard]] const std::array<ServoActuatorBinding, kReachyPowerThermalActuatorCount>&
PowerThermalBindings() noexcept;
[[nodiscard]] const ServoThermalParameters* FindServoThermalBaseline(
    std::string_view id) noexcept;
[[nodiscard]] const ServoThermalParameters* FindServoThermalBaselineForActuator(
    std::string_view actuator_name) noexcept;

}  // namespace reachy::servo

#endif
