#include "reachy_servo_model.hpp"

#include <array>
#include <cassert>
#include <cmath>
#include <cstdint>
#include <string_view>
#include <type_traits>

namespace {

using reachy::servo::ActuatorRole;
using reachy::servo::ParameterQuality;
using reachy::servo::QualifiedScalar;
using reachy::servo::ServoCommand;
using reachy::servo::ServoModel;
using reachy::servo::ServoObservation;
using reachy::servo::ServoParameterSet;
using reachy::servo::ServoStepResult;

class EchoServoModel final : public ServoModel {
public:
    explicit EchoServoModel(const ServoParameterSet& parameters) : parameters_(parameters) {}

    const ServoParameterSet& Parameters() const noexcept override
    {
        return parameters_;
    }

    void Reset(const ServoObservation& observation) override
    {
        last_temperature_ = observation.temperature_celsius;
    }

    ServoStepResult Step(
        const ServoCommand& command,
        const ServoObservation& observation,
        double timestep_seconds) override
    {
        assert(timestep_seconds > 0.0);
        last_temperature_ = observation.temperature_celsius;
        return ServoStepResult{
            command.torque_enabled ? command.feedforward_torque_newton_metres : 0.0,
            observation.estimated_current_amperes,
            last_temperature_,
            observation.fault_flags,
        };
    }

private:
    const ServoParameterSet& parameters_;
    double last_temperature_{0.0};
};

QualifiedScalar Scalar(double value, ParameterQuality quality)
{
    return QualifiedScalar{value, quality, "test_evidence"};
}

ServoParameterSet CompleteSet()
{
    const QualifiedScalar positive = Scalar(1.0, ParameterQuality::ManufacturerEstimate);
    const QualifiedScalar non_negative = Scalar(0.0, ParameterQuality::ManufacturerEstimate);
    return ServoParameterSet{
        "complete",
        ActuatorRole::BodyYaw,
        ParameterQuality::ManufacturerEstimate,
        "test_class",
        "test_source",
        positive,
        non_negative,
        positive,
        positive,
        positive,
        positive,
        positive,
        positive,
        positive,
        Scalar(6.0, ParameterQuality::ManufacturerEstimate),
        Scalar(5.0, ParameterQuality::ManufacturerEstimate),
        Scalar(7.0, ParameterQuality::ManufacturerEstimate),
        Scalar(20.0, ParameterQuality::ManufacturerEstimate),
        Scalar(60.0, ParameterQuality::ManufacturerEstimate),
        Scalar(80.0, ParameterQuality::ManufacturerEstimate),
        UINT32_C(0x7f),
        UINT32_C(0x01),
        ParameterQuality::ManufacturerEstimate,
        "test_faults",
    };
}

}  // namespace

static_assert(std::is_standard_layout_v<ServoCommand>);
static_assert(std::is_standard_layout_v<ServoObservation>);
static_assert(std::is_standard_layout_v<ServoStepResult>);
static_assert(std::is_standard_layout_v<ServoParameterSet>);

int main()
{
    using namespace reachy::servo;

    assert(ToString(ParameterQuality::Placeholder) == "placeholder");
    assert(ToString(ParameterQuality::ManufacturerEstimate) == "manufacturer_estimate");
    assert(ToString(ParameterQuality::Calibrated) == "calibrated");
    assert(ToString(ServoMode::Position) == "position");

    const auto& sets = UpstreamPlaceholderParameterSets();
    assert(sets.size() == 3U);
    for(const ServoParameterSet& set : sets)
    {
        assert(set.overall_quality == ParameterQuality::Placeholder);
        assert(ValidateParameterSet(set) == ParameterValidationError::None);
        assert(!IsTorqueModelReady(set));
    }

    const auto& bindings = UpstreamActuatorBindings();
    constexpr std::array<std::string_view, 9> expected_names{{
        "yaw_body",
        "stewart_1",
        "stewart_2",
        "stewart_3",
        "stewart_4",
        "stewart_5",
        "stewart_6",
        "right_antenna",
        "left_antenna",
    }};
    for(std::size_t index = 0U; index < expected_names.size(); ++index)
    {
        assert(bindings[index].actuator_name == expected_names[index]);
        const ServoParameterSet* parameters = FindParameterSet(bindings[index].parameter_set_id);
        assert(parameters != nullptr);
        assert(parameters->role == bindings[index].role);
    }
    assert(FindActuatorBinding("stewart_4") != nullptr);
    assert(FindActuatorBinding("unknown") == nullptr);
    assert(FindParameterSet("unknown") == nullptr);

    ServoParameterSet complete = CompleteSet();
    assert(ValidateParameterSet(complete) == ParameterValidationError::None);
    assert(IsTorqueModelReady(complete));

    ServoParameterSet invalid = complete;
    invalid.minimum_voltage_volts = Scalar(8.0, ParameterQuality::ManufacturerEstimate);
    assert(ValidateParameterSet(invalid) == ParameterValidationError::InvalidVoltageOrder);

    invalid = complete;
    invalid.shutdown_temperature_celsius = Scalar(50.0, ParameterQuality::ManufacturerEstimate);
    assert(ValidateParameterSet(invalid) == ParameterValidationError::InvalidTemperatureOrder);

    invalid = complete;
    invalid.latching_fault_mask = UINT32_C(0x80);
    assert(ValidateParameterSet(invalid) == ParameterValidationError::InvalidFaultMask);

    invalid = complete;
    invalid.overall_quality = ParameterQuality::Calibrated;
    assert(ValidateParameterSet(invalid) == ParameterValidationError::CalibratedSetIncomplete);

    EchoServoModel model(complete);
    const ServoObservation observation{1.0, 0.1, 0.2, 0.3, 0.4, 6.0, 30.0, 0U};
    model.Reset(observation);
    const ServoCommand command{
        7U,
        1.0,
        ServoMode::Position,
        0.5,
        0.0,
        1.0,
        2.0,
        0.25,
        true,
    };
    const ServoStepResult result = model.Step(command, observation, 0.002);
    assert(std::fabs(result.requested_torque_newton_metres - 0.25) < 1.0e-12);
    assert(result.temperature_celsius == observation.temperature_celsius);

    const std::uint32_t mask = ServoFaultFlag::OverCurrent | ServoFaultFlag::Encoder;
    assert((mask & ToMask(ServoFaultFlag::OverCurrent)) != 0U);
    assert((mask & ToMask(ServoFaultFlag::Encoder)) != 0U);

    return 0;
}
