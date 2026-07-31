#include "reachy_mechanical_servo_model.hpp"

#include <array>
#include <cassert>
#include <cmath>
#include <cstdint>
#include <string_view>

namespace {

using namespace reachy::servo;

constexpr double kTolerance = 1.0e-12;

QualifiedScalar Scalar(double value)
{
    return QualifiedScalar{value, ParameterQuality::ManufacturerEstimate, "test_evidence"};
}

ServoParameterSet CompleteServoParameters(ActuatorRole role)
{
    return ServoParameterSet{
        "test_servo",
        role,
        ParameterQuality::ManufacturerEstimate,
        "test_class",
        "test_source",
        Scalar(0.01),
        Scalar(0.01),
        Scalar(0.001),
        Scalar(0.01),
        Scalar(1.0),
        Scalar(2.0),
        Scalar(0.25),
        Scalar(1.0),
        Scalar(10.0),
        Scalar(5.0),
        Scalar(3.7),
        Scalar(6.0),
        Scalar(25.0),
        Scalar(65.0),
        Scalar(70.0),
        UINT32_C(0x7f),
        UINT32_C(0x73),
        ParameterQuality::ManufacturerEstimate,
        "test_faults",
    };
}

class FakeServoModel final : public ServoModel {
public:
    explicit FakeServoModel(ActuatorRole role) : parameters_(CompleteServoParameters(role)) {}

    const ServoParameterSet& Parameters() const noexcept override
    {
        return parameters_;
    }

    void Reset(const ServoObservation& observation) override
    {
        last_reset_position_ = observation.position_radians;
        step_count_ = 0U;
    }

    ServoStepResult Step(
        const ServoCommand& command,
        const ServoObservation& observation,
        double timestep_seconds) override
    {
        assert(timestep_seconds > 0.0);
        last_command_ = command;
        ++step_count_;
        const bool enabled = command.torque_enabled && command.mode != ServoMode::Disabled;
        const double torque = enabled ? command.feedforward_torque_newton_metres : 0.0;
        return ServoStepResult{
            torque,
            std::fabs(torque) * 2.0,
            observation.temperature_celsius,
            observation.fault_flags,
        };
    }

    const ServoCommand& LastCommand() const noexcept
    {
        return last_command_;
    }

    std::uint64_t StepCount() const noexcept
    {
        return step_count_;
    }

private:
    ServoParameterSet parameters_;
    ServoCommand last_command_{};
    double last_reset_position_{0.0};
    std::uint64_t step_count_{0U};
};

ServoObservation Observation(double velocity = 0.0, double position = 0.0)
{
    return ServoObservation{0.0, position, velocity, 0.0, 0.0, 5.0, 25.0, 0U};
}

ServoCommand Command(
    std::uint64_t sequence,
    double torque,
    double target_position = 0.0,
    ServoMode mode = ServoMode::Torque)
{
    return ServoCommand{
        sequence,
        static_cast<double>(sequence) * 0.01,
        mode,
        target_position,
        0.0,
        0.0,
        0.0,
        torque,
        true,
    };
}

MechanicalEffectConfiguration DisabledEffects()
{
    return MechanicalEffectConfiguration{false, false, false, false};
}

}  // namespace

int main()
{
    using namespace reachy::servo;

    assert(ToString(MechanicalEvidenceClass::EngineeringEstimate) == "engineering_estimate");
    assert(ToString(MechanicalValidationError::InvalidBreakawayOrder) == "invalid_breakaway_order");

    const auto& baselines = EngineeringMechanicalBaselines();
    assert(baselines.size() == 3U);
    assert(baselines[0].role == ActuatorRole::BodyYaw);
    assert(baselines[1].role == ActuatorRole::Stewart);
    assert(baselines[2].role == ActuatorRole::Antenna);
    for(const MechanicalEffectsParameters& parameters : baselines)
    {
        assert(ValidateMechanicalParameters(parameters) == MechanicalValidationError::None);
        assert(parameters.overall_evidence_class == MechanicalEvidenceClass::EngineeringEstimate);
    }
    assert(baselines[0].id != baselines[1].id);
    assert(baselines[1].id != baselines[2].id);
    assert(FindMechanicalBaselineForActuator("yaw_body") == &baselines[0]);
    assert(FindMechanicalBaselineForActuator("stewart_4") == &baselines[1]);
    assert(FindMechanicalBaselineForActuator("left_antenna") == &baselines[2]);
    assert(FindMechanicalBaselineForActuator("unknown") == nullptr);

    MechanicalEffectsParameters invalid = baselines[0];
    invalid.breakaway_torque_newton_metres.value =
        invalid.coulomb_friction_newton_metres.value * 0.5;
    assert(ValidateMechanicalParameters(invalid) ==
           MechanicalValidationError::InvalidBreakawayOrder);
    invalid = baselines[0];
    invalid.stiction_exit_velocity_radians_per_second.value =
        invalid.stiction_enter_velocity_radians_per_second.value;
    assert(ValidateMechanicalParameters(invalid) ==
           MechanicalValidationError::InvalidStictionVelocityOrder);
    invalid = baselines[0];
    invalid.overall_evidence_class = MechanicalEvidenceClass::Calibrated;
    assert(ValidateMechanicalParameters(invalid) == MechanicalValidationError::CalibratedClaim);

    const ServoObservation reset_observation = Observation();

    // Every effect disabled is an exact pass-through to the prior electrical baseline.
    FakeServoModel passthrough_inner(ActuatorRole::BodyYaw);
    MechanicalServoModel passthrough(passthrough_inner, baselines[0], DisabledEffects());
    passthrough.Reset(reset_observation);
    const ServoCommand passthrough_command = Command(1U, 0.25, 0.4, ServoMode::Position);
    const ServoStepResult passthrough_result =
        passthrough.Step(passthrough_command, Observation(0.7), 0.002);
    assert(std::fabs(passthrough_result.requested_torque_newton_metres - 0.25) < kTolerance);
    assert(std::fabs(passthrough_result.estimated_current_amperes - 0.5) < kTolerance);
    assert(std::fabs(passthrough_inner.LastCommand().target_position_radians - 0.4) < kTolerance);

    // Coulomb and viscous friction oppose measured velocity with correct signs.
    FakeServoModel friction_inner(ActuatorRole::BodyYaw);
    MechanicalServoModel friction_model(
        friction_inner,
        baselines[0],
        MechanicalEffectConfiguration{true, false, false, false});
    friction_model.Reset(reset_observation);
    ServoStepResult friction_result =
        friction_model.Step(Command(1U, 0.25), Observation(1.0), 0.002);
    assert(friction_result.requested_torque_newton_metres < 0.25);
    assert(friction_model.LastIdentificationSample().friction_torque_newton_metres < 0.0);
    friction_result = friction_model.Step(Command(2U, -0.25), Observation(-1.0), 0.002);
    assert(friction_result.requested_torque_newton_metres > -0.25);
    assert(friction_model.LastIdentificationSample().friction_torque_newton_metres > 0.0);

    // Stateful stiction uses hysteresis and does not chatter under sub-threshold velocity noise.
    FakeServoModel stiction_inner(ActuatorRole::BodyYaw);
    MechanicalServoModel stiction_model(
        stiction_inner,
        baselines[0],
        MechanicalEffectConfiguration{false, true, false, false});
    stiction_model.Reset(reset_observation);
    const double below_breakaway = baselines[0].breakaway_torque_newton_metres.value * 0.8;
    ServoStepResult stiction_result =
        stiction_model.Step(Command(1U, below_breakaway), Observation(0.001), 0.002);
    assert(stiction_result.requested_torque_newton_metres == 0.0);
    assert(stiction_model.IsStuck());
    stiction_result =
        stiction_model.Step(Command(2U, below_breakaway), Observation(-0.001), 0.002);
    assert(stiction_result.requested_torque_newton_metres == 0.0);
    assert(stiction_model.IsStuck());
    stiction_result = stiction_model.Step(
        Command(3U, baselines[0].breakaway_torque_newton_metres.value * 1.1),
        Observation(0.001),
        0.002);
    assert(stiction_result.requested_torque_newton_metres > 0.0);
    assert(!stiction_model.IsStuck());

    // Backlash play operator retains target through reversal until the dead zone is crossed.
    FakeServoModel backlash_inner(ActuatorRole::BodyYaw);
    MechanicalServoModel backlash_model(
        backlash_inner,
        baselines[0],
        MechanicalEffectConfiguration{false, false, true, false});
    backlash_model.Reset(reset_observation);
    const double half_width = baselines[0].backlash_half_width_radians.value;
    [[maybe_unused]] const ServoStepResult backlash_step_1 =
        backlash_model.Step(Command(1U, 0.0, 0.1, ServoMode::Position), Observation(), 0.002);
    const double first_transmitted = 0.1 - half_width;
    assert(std::fabs(backlash_inner.LastCommand().target_position_radians - first_transmitted) <
           kTolerance);
    [[maybe_unused]] const ServoStepResult backlash_step_2 =
        backlash_model.Step(Command(2U, 0.0, 0.095, ServoMode::Position), Observation(), 0.002);
    assert(std::fabs(backlash_inner.LastCommand().target_position_radians - first_transmitted) <
           kTolerance);
    [[maybe_unused]] const ServoStepResult backlash_step_3 =
        backlash_model.Step(Command(3U, 0.0, 0.05, ServoMode::Position), Observation(), 0.002);
    assert(std::fabs(backlash_inner.LastCommand().target_position_radians - (0.05 + half_width)) <
           kTolerance);
    assert(backlash_model.IdentificationAccumulator().reversal_count == 1U);

    // Compliance is bounded, initially attenuates torque, and converges deterministically.
    FakeServoModel compliance_inner(ActuatorRole::BodyYaw);
    MechanicalServoModel compliance_model(
        compliance_inner,
        baselines[0],
        MechanicalEffectConfiguration{false, false, false, true});
    compliance_model.Reset(reset_observation);
    ServoStepResult compliance_result =
        compliance_model.Step(Command(1U, 0.5), Observation(), 0.002);
    assert(compliance_result.requested_torque_newton_metres > 0.0);
    assert(compliance_result.requested_torque_newton_metres < 0.5);
    for(std::uint64_t sequence = 2U; sequence <= 500U; ++sequence)
    {
        compliance_result = compliance_model.Step(Command(sequence, 0.5), Observation(), 0.002);
    }
    assert(std::fabs(compliance_result.requested_torque_newton_metres - 0.5) < 1.0e-6);
    assert(std::fabs(compliance_model.ElasticDeflectionRadians()) <=
           baselines[0].maximum_elastic_deflection_radians.value + kTolerance);

    // Each effect can be switched off independently and returns to exact pass-through behavior.
    friction_model.SetEffects(DisabledEffects());
    friction_result = friction_model.Step(Command(3U, 0.25), Observation(1.0), 0.002);
    assert(std::fabs(friction_result.requested_torque_newton_metres - 0.25) < kTolerance);
    stiction_model.SetEffects(DisabledEffects());
    stiction_result = stiction_model.Step(Command(4U, below_breakaway), Observation(0.0), 0.002);
    assert(std::fabs(stiction_result.requested_torque_newton_metres - below_breakaway) < kTolerance);
    backlash_model.SetEffects(DisabledEffects());
    [[maybe_unused]] const ServoStepResult backlash_step_4 =
        backlash_model.Step(Command(4U, 0.0, -0.2, ServoMode::Position), Observation(), 0.002);
    assert(std::fabs(backlash_inner.LastCommand().target_position_radians + 0.2) < kTolerance);
    compliance_model.SetEffects(DisabledEffects());
    compliance_result = compliance_model.Step(Command(501U, 0.5), Observation(), 0.002);
    assert(std::fabs(compliance_result.requested_torque_newton_metres - 0.5) < kTolerance);

    // Identification hooks retain bounded per-step data without a physics-thread callback.
    const MechanicalIdentificationSample sample = compliance_model.LastIdentificationSample();
    const MechanicalIdentificationAccumulator accumulator =
        compliance_model.IdentificationAccumulator();
    assert(sample.command_sequence == 501U);
    assert(accumulator.sample_count == 501U);
    assert(accumulator.maximum_absolute_elastic_deflection_radians <=
           baselines[0].maximum_elastic_deflection_radians.value + kTolerance);
    compliance_model.ResetIdentification();
    assert(compliance_model.IdentificationAccumulator().sample_count == 0U);

    // A cross-role wrapper is rejected before invoking the inner model.
    FakeServoModel wrong_role_inner(ActuatorRole::Antenna);
    MechanicalServoModel wrong_role_model(wrong_role_inner, baselines[0], DisabledEffects());
    wrong_role_model.Reset(reset_observation);
    const ServoStepResult wrong_role_result =
        wrong_role_model.Step(Command(1U, 0.5), Observation(), 0.002);
    assert(wrong_role_result.requested_torque_newton_metres == 0.0);
    assert((wrong_role_result.fault_flags & ToMask(ServoFaultFlag::ModelRejected)) != 0U);
    assert(wrong_role_inner.StepCount() == 0U);

    return 0;
}
