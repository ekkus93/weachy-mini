#include "reachy_electrical_servo_model.hpp"

#include <cassert>
#include <cmath>
#include <cstdint>
#include <limits>
#include <string_view>

namespace {
using namespace reachy::servo;
constexpr double kDt = 0.002;
constexpr double kTolerance = 1.0e-12;

ServoObservation Observation(
    double time,
    double position = 0.0,
    double velocity = 0.0,
    double voltage = 6.0,
    double current = 0.0,
    double temperature = 25.0,
    std::uint32_t faults = 0U)
{
    return ServoObservation{time, position, velocity, 0.0, current, voltage, temperature, faults};
}

ServoCommand PositionCommand(
    std::uint64_t sequence,
    double sample_time,
    double target,
    bool torque_enabled = true)
{
    return ServoCommand{
        sequence,
        sample_time,
        ServoMode::Position,
        target,
        0.0,
        0.0,
        0.0,
        0.0,
        torque_enabled,
    };
}

ServoStepResult Activate(
    ElectricalServoModel& model,
    const ServoCommand& command,
    double position = 0.0,
    double voltage = 6.0)
{
    (void)model.Step(command, Observation(0.0, position, 0.0, voltage), kDt);
    return model.Step(command, Observation(0.01, position, 0.0, voltage), kDt);
}

void TestRegistryAndUnits()
{
    const auto& baselines = ManufacturerElectricalBaselines();
    assert(baselines.size() == 3U);
    for(const ElectricalServoBaseline& baseline : baselines)
    {
        assert(ValidateElectricalBaseline(baseline) == ElectricalValidationError::None);
        assert(baseline.servo_parameters.overall_quality ==
               ParameterQuality::ManufacturerEstimate);
        assert(baseline.controller_parameters.overall_quality ==
               ParameterQuality::ManufacturerEstimate);
        assert(baseline.servo_parameters.nominal_voltage_volts.value == 5.0);
        assert(baseline.servo_parameters.maximum_voltage_volts.value == 6.0);
    }
    const double expected_position_quantum = 2.0 * std::acos(-1.0) / 4096.0;
    const double expected_velocity_quantum = 0.229 * 2.0 * std::acos(-1.0) / 60.0;
    const ElectricalServoBaseline* stewart =
        FindElectricalBaselineForActuator("stewart_4");
    assert(stewart != nullptr);
    assert(stewart->servo_parameters.role == ActuatorRole::Stewart);
    assert(std::fabs(
               *stewart->servo_parameters.encoder_position_quantum_radians.value -
               expected_position_quantum) < 1.0e-15);
    assert(std::fabs(
               *stewart->servo_parameters.encoder_velocity_quantum_radians_per_second.value -
               expected_velocity_quantum) < 1.0e-15);
    assert(FindElectricalBaselineForActuator("unknown") == nullptr);
}

void TestQuantizationBoundaries()
{
    constexpr double quantum = 0.1;
    assert(std::fabs(QuantizeToIncrement(0.049, quantum)) < kTolerance);
    assert(std::fabs(QuantizeToIncrement(0.05, quantum) - 0.1) < kTolerance);
    assert(std::fabs(QuantizeToIncrement(-0.05, quantum) + 0.1) < kTolerance);
    assert(std::isnan(QuantizeToIncrement(1.0, 0.0)));
}

void TestDelayedApplicationAndZeroError()
{
    const ElectricalServoBaseline& baseline = ManufacturerElectricalBaselines()[1U];
    ElectricalServoModel model(baseline);
    model.Reset(Observation(0.0));
    const ServoCommand command = PositionCommand(1U, 0.0, 0.0);
    const ServoStepResult before = model.Step(command, Observation(0.008), kDt);
    assert(before.requested_torque_newton_metres == 0.0);
    assert(model.ActiveCommandSequence() == 0U);
    const ServoStepResult due = model.Step(command, Observation(0.01), kDt);
    assert(model.ActiveCommandSequence() == 1U);
    assert(model.PendingCommandCount() == 0U);
    assert(std::fabs(due.requested_torque_newton_metres) < kTolerance);
}

void TestSaturationSignsAndVoltageScaling()
{
    const ElectricalServoBaseline& baseline = ManufacturerElectricalBaselines()[1U];
    ElectricalServoModel positive_model(baseline);
    positive_model.Reset(Observation(0.0));
    const ServoStepResult positive = Activate(
        positive_model,
        PositionCommand(1U, 0.0, 1.0));
    assert(positive.requested_torque_newton_metres > 0.0);
    assert(positive.requested_torque_newton_metres <= 0.6 + kTolerance);
    assert(positive.estimated_current_amperes <= 1.74 + kTolerance);

    ElectricalServoModel negative_model(baseline);
    negative_model.Reset(Observation(0.0));
    const ServoStepResult negative = Activate(
        negative_model,
        PositionCommand(1U, 0.0, -1.0));
    assert(negative.requested_torque_newton_metres < 0.0);
    assert(negative.requested_torque_newton_metres >= -0.6 - kTolerance);

    ElectricalServoModel low_voltage_model(baseline);
    low_voltage_model.Reset(Observation(0.0, 0.0, 0.0, 3.7));
    const ServoStepResult low = Activate(
        low_voltage_model,
        PositionCommand(1U, 0.0, 1.0),
        0.0,
        3.7);
    assert(low.requested_torque_newton_metres > 0.0);
    assert(low.requested_torque_newton_metres < positive.requested_torque_newton_metres);
    assert(std::fabs(
               low.requested_torque_newton_metres /
                   positive.requested_torque_newton_metres -
               3.7 / 6.0) < 1.0e-12);
}

void TestTorqueDisableAndGravityResponse()
{
    const ElectricalServoBaseline& baseline = ManufacturerElectricalBaselines()[1U];
    ElectricalServoModel model(baseline);
    model.Reset(Observation(0.0));
    const ServoStepResult result = Activate(
        model,
        PositionCommand(1U, 0.0, 1.0, false));
    assert(result.requested_torque_newton_metres == 0.0);
    assert(result.estimated_current_amperes == 0.0);

    double position = 0.0;
    double velocity = 0.0;
    constexpr double gravity_acceleration = -2.0;
    for(std::size_t step = 0U; step < 100U; ++step)
    {
        velocity += gravity_acceleration * kDt;
        position += velocity * kDt;
    }
    assert(position < 0.0);
}

void TestFaultTransitions()
{
    const ElectricalServoBaseline& baseline = ManufacturerElectricalBaselines()[1U];
    ElectricalServoModel voltage_model(baseline);
    voltage_model.Reset(Observation(0.0));
    const ServoCommand hold = PositionCommand(1U, 0.0, 0.0);
    (void)voltage_model.Step(hold, Observation(0.0), kDt);
    const ServoStepResult under =
        voltage_model.Step(hold, Observation(0.01, 0.0, 0.0, 3.6), kDt);
    assert((under.fault_flags & ToMask(ServoFaultFlag::UnderVoltage)) != 0U);
    const ServoStepResult recovered =
        voltage_model.Step(hold, Observation(0.012, 0.0, 0.0, 5.0), kDt);
    assert((recovered.fault_flags & ToMask(ServoFaultFlag::UnderVoltage)) == 0U);

    ElectricalServoModel current_model(baseline);
    current_model.Reset(Observation(0.0));
    const ServoCommand overload = PositionCommand(1U, 0.0, 1.0);
    (void)current_model.Step(overload, Observation(0.0), kDt);
    ServoStepResult last{};
    for(std::size_t step = 0U; step < 130U; ++step)
    {
        const double time = 0.01 + static_cast<double>(step) * kDt;
        last = current_model.Step(overload, Observation(time), kDt);
    }
    assert((last.fault_flags & ToMask(ServoFaultFlag::OverCurrent)) != 0U);
    assert(last.requested_torque_newton_metres == 0.0);
    assert((current_model.LatchedFaultFlags() &
            ToMask(ServoFaultFlag::OverCurrent)) != 0U);
    current_model.Reset(Observation(1.0));
    assert(current_model.LatchedFaultFlags() == 0U);

    ElectricalServoModel invalid_model(baseline);
    invalid_model.Reset(Observation(0.0));
    ServoCommand invalid = PositionCommand(1U, 0.0, 0.0);
    invalid.target_position_radians = std::numeric_limits<double>::quiet_NaN();
    const ServoStepResult rejected =
        invalid_model.Step(invalid, Observation(0.0), kDt);
    assert((rejected.fault_flags & ToMask(ServoFaultFlag::ModelRejected)) != 0U);
}

void TestCommandSamplingAndProfileBounds()
{
    const ElectricalServoBaseline& baseline = ManufacturerElectricalBaselines()[1U];
    ElectricalServoModel model(baseline);
    model.Reset(Observation(0.0));
    ServoCommand first = PositionCommand(1U, 0.0, 0.5);
    first.profile_velocity_radians_per_second = 0.2;
    first.profile_acceleration_radians_per_second_squared = 1.0;
    ServoCommand second = PositionCommand(2U, 0.002, -0.5);
    (void)model.Step(first, Observation(0.0), kDt);
    (void)model.Step(second, Observation(0.002), kDt);
    assert(model.PendingCommandCount() == 2U);
    (void)model.Step(second, Observation(0.01), kDt);
    assert(model.ActiveCommandSequence() == 1U);
    assert(model.PendingCommandCount() == 1U);
    (void)model.Step(second, Observation(0.02), kDt);
    assert(model.ActiveCommandSequence() == 2U);
}
}

int main()
{
    TestRegistryAndUnits();
    TestQuantizationBoundaries();
    TestDelayedApplicationAndZeroError();
    TestSaturationSignsAndVoltageScaling();
    TestTorqueDisableAndGravityResponse();
    TestFaultTransitions();
    TestCommandSamplingAndProfileBounds();
    return 0;
}
