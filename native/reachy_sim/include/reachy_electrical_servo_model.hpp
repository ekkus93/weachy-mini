#ifndef REACHY_ELECTRICAL_SERVO_MODEL_HPP
#define REACHY_ELECTRICAL_SERVO_MODEL_HPP

#include "reachy_servo_model.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace reachy::servo {

enum class ElectricalValidationError : std::uint8_t {
    None = 0,
    InvalidServoParameters,
    EmptyIdentity,
    MissingEvidence,
    RoleMismatch,
    PlaceholderValue,
    CalibratedClaim,
    InvalidPositiveValue,
    InvalidNonNegativeValue,
    InvalidReferenceVoltage,
};

struct ElectricalControllerParameters {
    std::string_view id;
    ActuatorRole role;
    ParameterQuality overall_quality;
    std::string_view source_evidence_id;
    QualifiedScalar target_position_quantum_radians;
    QualifiedScalar target_velocity_quantum_radians_per_second;
    QualifiedScalar position_gain_newton_metres_per_radian;
    QualifiedScalar velocity_gain_newton_metre_seconds_per_radian;
    QualifiedScalar performance_reference_voltage_volts;
};

struct ElectricalServoBaseline {
    ServoParameterSet servo_parameters;
    ElectricalControllerParameters controller_parameters;
};

class ElectricalServoModel final : public ServoModel {
public:
    static constexpr std::size_t kPendingCommandCapacity = 16U;

    explicit ElectricalServoModel(const ElectricalServoBaseline& baseline) noexcept;

    [[nodiscard]] const ServoParameterSet& Parameters() const noexcept override;
    [[nodiscard]] const ElectricalControllerParameters& ControllerParameters() const noexcept;
    void Reset(const ServoObservation& observation) override;
    [[nodiscard]] ServoStepResult Step(
        const ServoCommand& command,
        const ServoObservation& observation,
        double timestep_seconds) override;

    [[nodiscard]] std::uint64_t ActiveCommandSequence() const noexcept;
    [[nodiscard]] std::uint32_t LatchedFaultFlags() const noexcept;
    [[nodiscard]] std::size_t PendingCommandCount() const noexcept;
    [[nodiscard]] double LastAppliedSampleTimeSeconds() const noexcept;

private:
    struct PendingCommand {
        ServoCommand command{};
        double apply_time_seconds{0.0};
    };

    [[nodiscard]] bool ConfigurationValid() const noexcept;
    [[nodiscard]] bool InputsFinite(
        const ServoCommand& command,
        const ServoObservation& observation,
        double timestep_seconds) const noexcept;
    void IngestCommand(const ServoCommand& command) noexcept;
    void ApplyDueCommands(double observation_time_seconds) noexcept;
    void UpdateProfile(double timestep_seconds) noexcept;
    [[nodiscard]] std::uint32_t UpdateFaults(
        const ServoObservation& observation) noexcept;
    [[nodiscard]] ServoStepResult ZeroTorqueResult(
        const ServoObservation& observation,
        std::uint32_t faults) const noexcept;

    const ElectricalServoBaseline& baseline_;
    std::array<PendingCommand, kPendingCommandCapacity> pending_commands_{};
    std::size_t pending_command_count_{0U};
    ServoCommand active_command_{};
    bool has_active_command_{false};
    bool has_queued_sequence_{false};
    std::uint64_t last_queued_sequence_{0U};
    double last_command_sample_slot_seconds_{0.0};
    double last_applied_sample_time_seconds_{0.0};
    double profile_position_radians_{0.0};
    double profile_velocity_radians_per_second_{0.0};
    double peak_current_elapsed_seconds_{0.0};
    std::uint32_t latched_fault_flags_{0U};
    bool configuration_valid_{false};
};

[[nodiscard]] std::string_view ToString(ElectricalValidationError error) noexcept;
[[nodiscard]] double QuantizeToIncrement(double value, double quantum) noexcept;
[[nodiscard]] ElectricalValidationError ValidateElectricalBaseline(
    const ElectricalServoBaseline& baseline) noexcept;
[[nodiscard]] const std::array<ElectricalServoBaseline, 3>&
ManufacturerElectricalBaselines() noexcept;
[[nodiscard]] const std::array<ServoActuatorBinding, 9>&
ManufacturerElectricalBindings() noexcept;
[[nodiscard]] const ElectricalServoBaseline* FindElectricalBaseline(
    std::string_view id) noexcept;
[[nodiscard]] const ElectricalServoBaseline* FindElectricalBaselineForActuator(
    std::string_view actuator_name) noexcept;

}  // namespace reachy::servo

#endif
