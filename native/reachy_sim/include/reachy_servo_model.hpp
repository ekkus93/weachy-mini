#ifndef REACHY_SERVO_MODEL_HPP
#define REACHY_SERVO_MODEL_HPP

#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string_view>

namespace reachy::servo {

enum class ParameterQuality : std::uint8_t {
    Placeholder = 0,
    ManufacturerEstimate = 1,
    Calibrated = 2,
};

enum class ServoMode : std::uint8_t {
    Disabled = 0,
    Position = 1,
    Velocity = 2,
    Torque = 3,
};

enum class ActuatorRole : std::uint8_t {
    BodyYaw = 0,
    Stewart = 1,
    Antenna = 2,
};

enum class ServoFaultFlag : std::uint32_t {
    None = 0U,
    OverCurrent = UINT32_C(1) << 0U,
    OverTemperature = UINT32_C(1) << 1U,
    UnderVoltage = UINT32_C(1) << 2U,
    OverVoltage = UINT32_C(1) << 3U,
    Encoder = UINT32_C(1) << 4U,
    Communication = UINT32_C(1) << 5U,
    ModelRejected = UINT32_C(1) << 6U,
};

constexpr std::uint32_t ToMask(ServoFaultFlag flag) noexcept
{
    return static_cast<std::uint32_t>(flag);
}

constexpr std::uint32_t operator|(ServoFaultFlag left, ServoFaultFlag right) noexcept
{
    return ToMask(left) | ToMask(right);
}

struct QualifiedScalar {
    std::optional<double> value;
    ParameterQuality quality;
    std::string_view evidence_id;
};

struct ServoCommand {
    std::uint64_t sequence;
    double sample_time_seconds;
    ServoMode mode;
    double target_position_radians;
    double target_velocity_radians_per_second;
    double profile_velocity_radians_per_second;
    double profile_acceleration_radians_per_second_squared;
    double feedforward_torque_newton_metres;
    bool torque_enabled;
};

struct ServoObservation {
    double sample_time_seconds;
    double position_radians;
    double velocity_radians_per_second;
    double applied_torque_newton_metres;
    double estimated_current_amperes;
    double supply_voltage_volts;
    double temperature_celsius;
    std::uint32_t fault_flags;
};

struct ServoStepResult {
    double requested_torque_newton_metres;
    double estimated_current_amperes;
    double temperature_celsius;
    std::uint32_t fault_flags;
};

struct ServoParameterSet {
    std::string_view id;
    ActuatorRole role;
    ParameterQuality overall_quality;
    std::string_view source_actuator_class;
    std::string_view source_evidence_id;
    QualifiedScalar command_sample_period_seconds;
    QualifiedScalar command_latency_seconds;
    QualifiedScalar encoder_position_quantum_radians;
    QualifiedScalar encoder_velocity_quantum_radians_per_second;
    QualifiedScalar continuous_current_limit_amperes;
    QualifiedScalar peak_current_limit_amperes;
    QualifiedScalar peak_current_duration_seconds;
    QualifiedScalar stall_torque_newton_metres;
    QualifiedScalar no_load_speed_radians_per_second;
    QualifiedScalar nominal_voltage_volts;
    QualifiedScalar minimum_voltage_volts;
    QualifiedScalar maximum_voltage_volts;
    QualifiedScalar ambient_temperature_celsius;
    QualifiedScalar warning_temperature_celsius;
    QualifiedScalar shutdown_temperature_celsius;
    std::uint32_t supported_fault_mask;
    std::uint32_t latching_fault_mask;
    ParameterQuality fault_model_quality;
    std::string_view fault_model_evidence_id;
};

struct ServoActuatorBinding {
    std::string_view actuator_name;
    std::string_view parameter_set_id;
    ActuatorRole role;
};

enum class ParameterValidationError : std::uint8_t {
    None = 0,
    EmptyIdentity,
    MissingEvidence,
    NonFiniteValue,
    InvalidPositiveValue,
    InvalidNonNegativeValue,
    InvalidVoltageOrder,
    InvalidTemperatureOrder,
    InvalidFaultMask,
    CalibratedSetIncomplete,
};

class ServoModel {
public:
    virtual ~ServoModel() = default;

    [[nodiscard]] virtual const ServoParameterSet& Parameters() const noexcept = 0;
    virtual void Reset(const ServoObservation& observation) = 0;
    [[nodiscard]] virtual ServoStepResult Step(
        const ServoCommand& command,
        const ServoObservation& observation,
        double timestep_seconds) = 0;
};

[[nodiscard]] std::string_view ToString(ParameterQuality quality) noexcept;
[[nodiscard]] std::string_view ToString(ServoMode mode) noexcept;
[[nodiscard]] std::string_view ToString(ActuatorRole role) noexcept;
[[nodiscard]] std::string_view ToString(ParameterValidationError error) noexcept;

[[nodiscard]] const std::array<ServoParameterSet, 3>& UpstreamPlaceholderParameterSets() noexcept;
[[nodiscard]] const std::array<ServoActuatorBinding, 9>& UpstreamActuatorBindings() noexcept;
[[nodiscard]] const ServoParameterSet* FindParameterSet(std::string_view id) noexcept;
[[nodiscard]] const ServoActuatorBinding* FindActuatorBinding(std::string_view actuator_name) noexcept;
[[nodiscard]] ParameterValidationError ValidateParameterSet(const ServoParameterSet& parameters) noexcept;
[[nodiscard]] bool IsTorqueModelReady(const ServoParameterSet& parameters) noexcept;

}  // namespace reachy::servo

#endif
