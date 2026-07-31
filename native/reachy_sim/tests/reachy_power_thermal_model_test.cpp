#include "reachy_power_thermal_model.hpp"

#include <array>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <string_view>

namespace {
using namespace reachy::servo;

[[noreturn]] void Fail(std::string_view message)
{
    std::cerr << "FAIL: " << message << '\n';
    std::exit(EXIT_FAILURE);
}

void Expect(bool condition, std::string_view message)
{
    if(!condition)
    {
        Fail(message);
    }
}

void ExpectNear(double actual, double expected, double tolerance, std::string_view message)
{
    if(std::fabs(actual - expected) > tolerance)
    {
        std::cerr << "actual=" << actual << " expected=" << expected << '\n';
        Fail(message);
    }
}

class ConstantServoModel final : public ServoModel {
public:
    ConstantServoModel(ActuatorRole role, double torque, double current) noexcept
        : torque_(torque), current_(current)
    {
        parameters_.id = "test";
        parameters_.role = role;
    }

    const ServoParameterSet& Parameters() const noexcept override { return parameters_; }
    void Reset(const ServoObservation& observation) override
    {
        last_reset_faults_ = observation.fault_flags;
        ++reset_count_;
    }
    ServoStepResult Step(
        const ServoCommand& command,
        const ServoObservation& observation,
        double) override
    {
        if(!command.torque_enabled || command.mode == ServoMode::Disabled)
        {
            return ServoStepResult{0.0, 0.0, observation.temperature_celsius, observation.fault_flags};
        }
        return ServoStepResult{torque_, current_, observation.temperature_celsius, observation.fault_flags};
    }
    std::uint32_t ResetCount() const noexcept { return reset_count_; }
    std::uint32_t LastResetFaults() const noexcept { return last_reset_faults_; }

private:
    ServoParameterSet parameters_{};
    double torque_{0.0};
    double current_{0.0};
    std::uint32_t reset_count_{0U};
    std::uint32_t last_reset_faults_{0U};
};

PowerThermalScalar Scalar(double value)
{
    return PowerThermalScalar{
        value,
        PowerThermalEvidenceClass::EngineeringEstimate,
        "test_evidence",
    };
}

ServoThermalParameters Thermal(ActuatorRole role)
{
    return ServoThermalParameters{
        "test_thermal",
        role,
        PowerThermalEvidenceClass::EngineeringEstimate,
        "test_source",
        Scalar(1.0),
        Scalar(0.1),
        Scalar(0.01),
        Scalar(26.0),
        Scalar(27.0),
        Scalar(25.5),
    };
}

SharedPowerSupplyParameters Supply(double resistance, double limit)
{
    return SharedPowerSupplyParameters{
        "test_supply",
        PowerThermalEvidenceClass::EngineeringEstimate,
        "test_source",
        Scalar(5.0),
        Scalar(resistance),
        Scalar(limit),
        Scalar(3.7),
    };
}

std::array<ServoCommand, kReachyPowerThermalActuatorCount> Commands(bool enabled = true)
{
    std::array<ServoCommand, kReachyPowerThermalActuatorCount> commands{};
    for(std::size_t index = 0U; index < commands.size(); ++index)
    {
        commands[index] = ServoCommand{
            static_cast<std::uint64_t>(index + 1U),
            0.0,
            enabled ? ServoMode::Torque : ServoMode::Disabled,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            enabled,
        };
    }
    return commands;
}

std::array<ServoObservation, kReachyPowerThermalActuatorCount> Observations(double temperature = 25.0)
{
    std::array<ServoObservation, kReachyPowerThermalActuatorCount> observations{};
    for(auto& observation : observations)
    {
        observation = ServoObservation{0.0, 0.0, 0.0, 0.0, 0.0, 5.0, temperature, 0U};
    }
    return observations;
}

struct Fixture {
    ConstantServoModel body{ActuatorRole::BodyYaw, 1.0, 1.0};
    ConstantServoModel s1{ActuatorRole::Stewart, 1.0, 1.0};
    ConstantServoModel s2{ActuatorRole::Stewart, 1.0, 1.0};
    ConstantServoModel s3{ActuatorRole::Stewart, 1.0, 1.0};
    ConstantServoModel s4{ActuatorRole::Stewart, 1.0, 1.0};
    ConstantServoModel s5{ActuatorRole::Stewart, 1.0, 1.0};
    ConstantServoModel s6{ActuatorRole::Stewart, 1.0, 1.0};
    ConstantServoModel a1{ActuatorRole::Antenna, 1.0, 1.0};
    ConstantServoModel a2{ActuatorRole::Antenna, 1.0, 1.0};
    ServoThermalParameters body_thermal{Thermal(ActuatorRole::BodyYaw)};
    ServoThermalParameters stewart_thermal{Thermal(ActuatorRole::Stewart)};
    ServoThermalParameters antenna_thermal{Thermal(ActuatorRole::Antenna)};
    std::array<ServoModel*, kReachyPowerThermalActuatorCount> models{{
        &body, &s1, &s2, &s3, &s4, &s5, &s6, &a1, &a2,
    }};
    std::array<const ServoThermalParameters*, kReachyPowerThermalActuatorCount> thermals{{
        &body_thermal,
        &stewart_thermal,
        &stewart_thermal,
        &stewart_thermal,
        &stewart_thermal,
        &stewart_thermal,
        &stewart_thermal,
        &antenna_thermal,
        &antenna_thermal,
    }};
};

void TestRegistryAndValidation()
{
    Expect(ValidateSharedPowerSupplyParameters(EngineeringSharedPowerSupply()) ==
               PowerThermalValidationError::None,
           "committed supply must validate");
    const auto& baselines = EngineeringServoThermalBaselines();
    for(const auto& baseline : baselines)
    {
        Expect(ValidateServoThermalParameters(baseline) == PowerThermalValidationError::None,
               "committed thermal baseline must validate");
    }
    Expect(PowerThermalBindings().size() == kReachyPowerThermalActuatorCount,
           "all actuator bindings must exist");
    Expect(FindServoThermalBaselineForActuator("yaw_body") != nullptr,
           "body binding must resolve");
    Expect(FindServoThermalBaselineForActuator("stewart_6") != nullptr,
           "Stewart binding must resolve");
    Expect(FindServoThermalBaselineForActuator("left_antenna") != nullptr,
           "antenna binding must resolve");
}

void TestSharedCurrentAndSag()
{
    Fixture fixture;
    auto supply = Supply(0.1, 3.0);
    PowerThermalModel model(supply, fixture.models, fixture.thermals);
    const auto observations = Observations();
    model.Reset(observations, 25.0);
    const PowerThermalStepResult result = model.Step(Commands(), observations, 25.0, 0.001);
    Expect(result.bus.current_limited, "simultaneous load must hit shared current limit");
    Expect(result.bus.requested_current_amperes > 8.9, "all current requests must be aggregated");
    Expect(result.bus.delivered_current_amperes <= 3.0 + 1.0e-9,
           "shared current must not exceed source budget");
    Expect(result.bus.bus_voltage_volts < result.bus.open_circuit_voltage_volts,
           "source impedance must produce voltage sag");
    Expect(result.servo_results[0].requested_torque_newton_metres < 1.0,
           "individual torque must be scaled by shared load");
    ExpectNear(result.servo_diagnostics[0].bus_voltage_volts,
               result.bus.bus_voltage_volts,
               1.0e-12,
               "per-servo diagnostics must expose shared voltage");
}

void TestDerating()
{
    Fixture fixture;
    auto supply = Supply(0.01, 20.0);
    PowerThermalModel model(supply, fixture.models, fixture.thermals);
    const auto observations = Observations(26.5);
    model.Reset(observations, 25.0);
    const PowerThermalStepResult result = model.Step(Commands(), observations, 25.0, 0.0001);
    ExpectNear(result.servo_diagnostics[0].derating_factor, 0.5, 0.02,
               "temperature between warning and shutdown must derate");
    Expect(result.servo_results[0].requested_torque_newton_metres < 0.6,
           "derating must reduce torque");
}

void TestShutdownLatchAndExplicitClear()
{
    Fixture fixture;
    fixture.s1 = ConstantServoModel(ActuatorRole::Stewart, 0.0, 0.0);
    fixture.s2 = ConstantServoModel(ActuatorRole::Stewart, 0.0, 0.0);
    fixture.s3 = ConstantServoModel(ActuatorRole::Stewart, 0.0, 0.0);
    fixture.s4 = ConstantServoModel(ActuatorRole::Stewart, 0.0, 0.0);
    fixture.s5 = ConstantServoModel(ActuatorRole::Stewart, 0.0, 0.0);
    fixture.s6 = ConstantServoModel(ActuatorRole::Stewart, 0.0, 0.0);
    fixture.a1 = ConstantServoModel(ActuatorRole::Antenna, 0.0, 0.0);
    fixture.a2 = ConstantServoModel(ActuatorRole::Antenna, 0.0, 0.0);
    fixture.models = {{&fixture.body, &fixture.s1, &fixture.s2, &fixture.s3, &fixture.s4,
                       &fixture.s5, &fixture.s6, &fixture.a1, &fixture.a2}};
    auto supply = Supply(0.01, 20.0);
    PowerThermalModel model(supply, fixture.models, fixture.thermals);
    auto observations = Observations();
    model.Reset(observations, 25.0);

    const PowerThermalStepResult hot = model.Step(Commands(), observations, 25.0, 0.021);
    Expect(model.ThermalShutdownLatched(0U), "thermal shutdown must latch");
    Expect(hot.servo_results[0].requested_torque_newton_metres == 0.0,
           "shutdown must visibly remove torque");
    Expect((hot.servo_results[0].fault_flags & ToMask(ServoFaultFlag::OverTemperature)) != 0U,
           "shutdown must expose over-temperature fault");
    Expect(model.ClearThermalShutdown(0U) == ThermalFaultClearResult::TorqueEnabled,
           "clear must be rejected while torque remains enabled");

    const auto disabled_commands = Commands(false);
    const PowerThermalStepResult cooled = model.Step(disabled_commands, observations, 25.0, 0.05);
    Expect(cooled.servo_results[0].requested_torque_newton_metres == 0.0,
           "latched shutdown must remain disabled while cooling");
    Expect(model.ServoTemperatureCelsius(0U) <= 25.5,
           "disabled channel must cool below recovery threshold");

    const PowerThermalStepResult still_latched =
        model.Step(Commands(), observations, 25.0, 0.001);
    Expect(still_latched.servo_results[0].requested_torque_newton_metres == 0.0,
           "thermal latch must not silently re-enable after cooling");
    Expect(model.ClearThermalShutdown(0U) == ThermalFaultClearResult::TorqueEnabled,
           "enabled command after cooling still blocks clear");
    static_cast<void>(model.Step(disabled_commands, observations, 25.0, 0.001));
    Expect(model.ClearThermalShutdown(0U) == ThermalFaultClearResult::Cleared,
           "explicit safe clear must succeed");
    const PowerThermalStepResult recovered = model.Step(Commands(), observations, 25.0, 0.001);
    Expect(recovered.servo_results[0].requested_torque_newton_metres > 0.0,
           "torque may resume only after explicit clear");
    Expect((fixture.body.LastResetFaults() & ToMask(ServoFaultFlag::OverTemperature)) == 0U,
           "safe reset must remove only the thermal latch");
}

void TestInvalidRoleFailsClosed()
{
    Fixture fixture;
    fixture.thermals[0] = &fixture.stewart_thermal;
    auto supply = Supply(0.1, 3.0);
    PowerThermalModel model(supply, fixture.models, fixture.thermals);
    const auto observations = Observations();
    model.Reset(observations, 25.0);
    const PowerThermalStepResult result = model.Step(Commands(), observations, 25.0, 0.001);
    Expect((result.servo_results[0].fault_flags & ToMask(ServoFaultFlag::ModelRejected)) != 0U,
           "role mismatch must fail closed");
}

}  // namespace

int main()
{
    TestRegistryAndValidation();
    TestSharedCurrentAndSag();
    TestDerating();
    TestShutdownLatchAndExplicitClear();
    TestInvalidRoleFailsClosed();
    std::cout << "RMA-064 power/thermal tests passed\n";
    return EXIT_SUCCESS;
}
