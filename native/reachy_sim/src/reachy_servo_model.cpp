#include "reachy_servo_model.hpp"

#include "reachy_servo_parameters.generated.hpp"

#include <algorithm>
#include <array>
#include <cmath>

namespace reachy::servo {
namespace {

constexpr std::array<const QualifiedScalar ServoParameterSet::*, 15> kRequiredScalars{{
    &ServoParameterSet::command_sample_period_seconds,
    &ServoParameterSet::command_latency_seconds,
    &ServoParameterSet::encoder_position_quantum_radians,
    &ServoParameterSet::encoder_velocity_quantum_radians_per_second,
    &ServoParameterSet::continuous_current_limit_amperes,
    &ServoParameterSet::peak_current_limit_amperes,
    &ServoParameterSet::peak_current_duration_seconds,
    &ServoParameterSet::stall_torque_newton_metres,
    &ServoParameterSet::no_load_speed_radians_per_second,
    &ServoParameterSet::nominal_voltage_volts,
    &ServoParameterSet::minimum_voltage_volts,
    &ServoParameterSet::maximum_voltage_volts,
    &ServoParameterSet::ambient_temperature_celsius,
    &ServoParameterSet::warning_temperature_celsius,
    &ServoParameterSet::shutdown_temperature_celsius,
}};

bool HasEvidence(const QualifiedScalar& scalar) noexcept
{
    return !scalar.evidence_id.empty();
}

ParameterValidationError ValidateScalar(
    const QualifiedScalar& scalar,
    bool allow_zero) noexcept
{
    if(!HasEvidence(scalar))
    {
        return ParameterValidationError::MissingEvidence;
    }
    if(!scalar.value.has_value())
    {
        return scalar.quality == ParameterQuality::Placeholder
                   ? ParameterValidationError::None
                   : ParameterValidationError::InvalidPositiveValue;
    }
    const double value = *scalar.value;
    if(!std::isfinite(value))
    {
        return ParameterValidationError::NonFiniteValue;
    }
    if(allow_zero ? value < 0.0 : value <= 0.0)
    {
        return allow_zero ? ParameterValidationError::InvalidNonNegativeValue
                          : ParameterValidationError::InvalidPositiveValue;
    }
    return ParameterValidationError::None;
}

bool CompleteAndCalibrated(const ServoParameterSet& parameters) noexcept
{
    return std::all_of(
        kRequiredScalars.begin(),
        kRequiredScalars.end(),
        [&parameters](const auto member) {
            const QualifiedScalar& scalar = parameters.*member;
            return scalar.value.has_value() && scalar.quality == ParameterQuality::Calibrated;
        });
}

}  // namespace

std::string_view ToString(ParameterQuality quality) noexcept
{
    switch(quality)
    {
        case ParameterQuality::Placeholder:
            return "placeholder";
        case ParameterQuality::ManufacturerEstimate:
            return "manufacturer_estimate";
        case ParameterQuality::Calibrated:
            return "calibrated";
    }
    return "unknown";
}

std::string_view ToString(ServoMode mode) noexcept
{
    switch(mode)
    {
        case ServoMode::Disabled:
            return "disabled";
        case ServoMode::Position:
            return "position";
        case ServoMode::Velocity:
            return "velocity";
        case ServoMode::Torque:
            return "torque";
    }
    return "unknown";
}

std::string_view ToString(ActuatorRole role) noexcept
{
    switch(role)
    {
        case ActuatorRole::BodyYaw:
            return "body_yaw";
        case ActuatorRole::Stewart:
            return "stewart";
        case ActuatorRole::Antenna:
            return "antenna";
    }
    return "unknown";
}

std::string_view ToString(ParameterValidationError error) noexcept
{
    switch(error)
    {
        case ParameterValidationError::None:
            return "none";
        case ParameterValidationError::EmptyIdentity:
            return "empty_identity";
        case ParameterValidationError::MissingEvidence:
            return "missing_evidence";
        case ParameterValidationError::NonFiniteValue:
            return "non_finite_value";
        case ParameterValidationError::InvalidPositiveValue:
            return "invalid_positive_value";
        case ParameterValidationError::InvalidNonNegativeValue:
            return "invalid_non_negative_value";
        case ParameterValidationError::InvalidVoltageOrder:
            return "invalid_voltage_order";
        case ParameterValidationError::InvalidTemperatureOrder:
            return "invalid_temperature_order";
        case ParameterValidationError::InvalidFaultMask:
            return "invalid_fault_mask";
        case ParameterValidationError::CalibratedSetIncomplete:
            return "calibrated_set_incomplete";
    }
    return "unknown";
}

const std::array<ServoParameterSet, 3>& UpstreamPlaceholderParameterSets() noexcept
{
    return generated::kParameterSets;
}

const std::array<ServoActuatorBinding, 9>& UpstreamActuatorBindings() noexcept
{
    return generated::kActuatorBindings;
}

const ServoParameterSet* FindParameterSet(std::string_view id) noexcept
{
    const auto& sets = UpstreamPlaceholderParameterSets();
    const auto found = std::find_if(
        sets.begin(),
        sets.end(),
        [id](const ServoParameterSet& parameters) { return parameters.id == id; });
    return found == sets.end() ? nullptr : &*found;
}

const ServoActuatorBinding* FindActuatorBinding(std::string_view actuator_name) noexcept
{
    const auto& bindings = UpstreamActuatorBindings();
    const auto found = std::find_if(
        bindings.begin(),
        bindings.end(),
        [actuator_name](const ServoActuatorBinding& binding) {
            return binding.actuator_name == actuator_name;
        });
    return found == bindings.end() ? nullptr : &*found;
}

ParameterValidationError ValidateParameterSet(const ServoParameterSet& parameters) noexcept
{
    if(parameters.id.empty() || parameters.source_actuator_class.empty() ||
       parameters.source_evidence_id.empty() || parameters.fault_model_evidence_id.empty())
    {
        return ParameterValidationError::EmptyIdentity;
    }

    for(std::size_t index = 0U; index < kRequiredScalars.size(); ++index)
    {
        const QualifiedScalar& scalar = parameters.*kRequiredScalars[index];
        const bool allow_zero = index == 1U;
        const ParameterValidationError error = ValidateScalar(scalar, allow_zero);
        if(error != ParameterValidationError::None)
        {
            return error;
        }
    }

    if((parameters.latching_fault_mask & ~parameters.supported_fault_mask) != 0U)
    {
        return ParameterValidationError::InvalidFaultMask;
    }

    if(parameters.minimum_voltage_volts.value.has_value() &&
       parameters.nominal_voltage_volts.value.has_value() &&
       parameters.maximum_voltage_volts.value.has_value())
    {
        const double minimum = *parameters.minimum_voltage_volts.value;
        const double nominal = *parameters.nominal_voltage_volts.value;
        const double maximum = *parameters.maximum_voltage_volts.value;
        if(!(minimum <= nominal && nominal <= maximum))
        {
            return ParameterValidationError::InvalidVoltageOrder;
        }
    }

    if(parameters.ambient_temperature_celsius.value.has_value() &&
       parameters.warning_temperature_celsius.value.has_value() &&
       parameters.shutdown_temperature_celsius.value.has_value())
    {
        const double ambient = *parameters.ambient_temperature_celsius.value;
        const double warning = *parameters.warning_temperature_celsius.value;
        const double shutdown = *parameters.shutdown_temperature_celsius.value;
        if(!(ambient < warning && warning < shutdown))
        {
            return ParameterValidationError::InvalidTemperatureOrder;
        }
    }

    if(parameters.overall_quality == ParameterQuality::Calibrated &&
       (!CompleteAndCalibrated(parameters) ||
        parameters.fault_model_quality != ParameterQuality::Calibrated))
    {
        return ParameterValidationError::CalibratedSetIncomplete;
    }

    return ParameterValidationError::None;
}

bool IsTorqueModelReady(const ServoParameterSet& parameters) noexcept
{
    return ValidateParameterSet(parameters) == ParameterValidationError::None &&
           std::all_of(
               kRequiredScalars.begin(),
               kRequiredScalars.end(),
               [&parameters](const auto member) {
                   return (parameters.*member).value.has_value();
               });
}

}  // namespace reachy::servo
