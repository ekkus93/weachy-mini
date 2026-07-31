#ifndef REACHY_MECHANICAL_SERVO_MODEL_HPP
#define REACHY_MECHANICAL_SERVO_MODEL_HPP

#include "reachy_servo_model.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace reachy::servo {

enum class MechanicalEvidenceClass : std::uint8_t {
    UpstreamApproximation = 0,
    EngineeringEstimate = 1,
    Calibrated = 2,
};

enum class MechanicalValidationError : std::uint8_t {
    None = 0,
    EmptyIdentity,
    MissingEvidence,
    RoleMismatch,
    CalibratedClaim,
    NonFiniteValue,
    InvalidNonNegativeValue,
    InvalidPositiveValue,
    InvalidBreakawayOrder,
    InvalidStictionVelocityOrder,
    DuplicateCrossRoleParameters,
};

struct MechanicalScalar {
    double value;
    MechanicalEvidenceClass evidence_class;
    std::string_view evidence_id;
};

struct MechanicalEffectsParameters {
    std::string_view id;
    ActuatorRole role;
    MechanicalEvidenceClass overall_evidence_class;
    std::string_view source_evidence_id;
    MechanicalScalar coulomb_friction_newton_metres;
    MechanicalScalar viscous_friction_newton_metre_seconds_per_radian;
    MechanicalScalar breakaway_torque_newton_metres;
    MechanicalScalar stiction_enter_velocity_radians_per_second;
    MechanicalScalar stiction_exit_velocity_radians_per_second;
    MechanicalScalar backlash_half_width_radians;
    MechanicalScalar compliance_stiffness_newton_metres_per_radian;
    MechanicalScalar compliance_damping_newton_metre_seconds_per_radian;
    MechanicalScalar maximum_elastic_deflection_radians;
};

struct MechanicalEffectConfiguration {
    bool friction_enabled{true};
    bool stiction_enabled{true};
    bool backlash_enabled{true};
    bool compliance_enabled{true};
};

struct MechanicalIdentificationSample {
    std::uint64_t command_sequence{0U};
    double sample_time_seconds{0.0};
    double position_radians{0.0};
    double velocity_radians_per_second{0.0};
    double electrical_torque_newton_metres{0.0};
    double compliance_torque_newton_metres{0.0};
    double friction_torque_newton_metres{0.0};
    double output_torque_newton_metres{0.0};
    double backlash_transmitted_target_radians{0.0};
    double elastic_deflection_radians{0.0};
    bool stuck{false};
};

struct MechanicalIdentificationAccumulator {
    std::uint64_t sample_count{0U};
    std::uint64_t reversal_count{0U};
    std::uint64_t stuck_sample_count{0U};
    double sum_absolute_velocity_radians_per_second{0.0};
    double sum_absolute_electrical_torque_newton_metres{0.0};
    double sum_absolute_friction_torque_newton_metres{0.0};
    double maximum_absolute_elastic_deflection_radians{0.0};
};

class MechanicalServoModel final : public ServoModel {
public:
    MechanicalServoModel(
        ServoModel& inner_model,
        const MechanicalEffectsParameters& parameters,
        MechanicalEffectConfiguration effects = {}) noexcept;

    [[nodiscard]] const ServoParameterSet& Parameters() const noexcept override;
    [[nodiscard]] const MechanicalEffectsParameters& MechanicalParameters() const noexcept;
    [[nodiscard]] MechanicalEffectConfiguration Effects() const noexcept;
    void SetEffects(MechanicalEffectConfiguration effects) noexcept;
    void Reset(const ServoObservation& observation) override;
    [[nodiscard]] ServoStepResult Step(
        const ServoCommand& command,
        const ServoObservation& observation,
        double timestep_seconds) override;

    [[nodiscard]] MechanicalIdentificationSample LastIdentificationSample() const noexcept;
    [[nodiscard]] MechanicalIdentificationAccumulator IdentificationAccumulator() const noexcept;
    void ResetIdentification() noexcept;
    [[nodiscard]] bool IsStuck() const noexcept;
    [[nodiscard]] double BacklashTransmittedTargetRadians() const noexcept;
    [[nodiscard]] double ElasticDeflectionRadians() const noexcept;

private:
    [[nodiscard]] bool ConfigurationValid() const noexcept;
    [[nodiscard]] bool InputsFinite(
        const ServoCommand& command,
        const ServoObservation& observation,
        double timestep_seconds) const noexcept;
    [[nodiscard]] ServoCommand ApplyBacklash(
        const ServoCommand& command,
        const ServoObservation& observation) noexcept;
    [[nodiscard]] double ApplyCompliance(double drive_torque, double timestep_seconds) noexcept;
    [[nodiscard]] double ApplyFrictionAndStiction(
        double transmitted_torque,
        double velocity_radians_per_second,
        double& friction_torque) noexcept;
    void RecordIdentification(
        const ServoCommand& command,
        const ServoObservation& observation,
        double electrical_torque,
        double compliance_torque,
        double friction_torque,
        double output_torque) noexcept;

    ServoModel& inner_model_;
    const MechanicalEffectsParameters& mechanical_parameters_;
    MechanicalEffectConfiguration effects_;
    bool configuration_valid_{false};
    bool stuck_{false};
    bool backlash_initialized_{false};
    double backlash_transmitted_target_radians_{0.0};
    double last_backlash_input_target_radians_{0.0};
    int last_backlash_direction_{0};
    double compliance_torque_state_newton_metres_{0.0};
    double elastic_deflection_radians_{0.0};
    double last_observed_position_radians_{0.0};
    MechanicalIdentificationSample last_identification_sample_{};
    MechanicalIdentificationAccumulator identification_accumulator_{};
};

[[nodiscard]] std::string_view ToString(MechanicalEvidenceClass value) noexcept;
[[nodiscard]] std::string_view ToString(MechanicalValidationError value) noexcept;
[[nodiscard]] MechanicalValidationError ValidateMechanicalParameters(
    const MechanicalEffectsParameters& parameters) noexcept;
[[nodiscard]] const std::array<MechanicalEffectsParameters, 3>&
EngineeringMechanicalBaselines() noexcept;
[[nodiscard]] const std::array<ServoActuatorBinding, 9>& MechanicalBindings() noexcept;
[[nodiscard]] const MechanicalEffectsParameters* FindMechanicalBaseline(
    std::string_view id) noexcept;
[[nodiscard]] const MechanicalEffectsParameters* FindMechanicalBaselineForActuator(
    std::string_view actuator_name) noexcept;

}  // namespace reachy::servo

#endif
