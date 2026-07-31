#include "reachy_mechanical_servo_model.hpp"

#include "reachy_mechanical_baseline.generated.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <limits>

namespace reachy::servo {
namespace {

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

[[nodiscard]] int Sign(double value) noexcept
{
    return (0.0 < value) - (value < 0.0);
}

[[nodiscard]] bool IsFiniteCommand(const ServoCommand& command) noexcept
{
    return std::isfinite(command.sample_time_seconds) && command.sample_time_seconds >= 0.0 &&
           IsKnownMode(command.mode) && std::isfinite(command.target_position_radians) &&
           std::isfinite(command.target_velocity_radians_per_second) &&
           std::isfinite(command.profile_velocity_radians_per_second) &&
           command.profile_velocity_radians_per_second >= 0.0 &&
           std::isfinite(command.profile_acceleration_radians_per_second_squared) &&
           command.profile_acceleration_radians_per_second_squared >= 0.0 &&
           std::isfinite(command.feedforward_torque_newton_metres);
}

[[nodiscard]] bool IsFiniteObservation(const ServoObservation& observation) noexcept
{
    return std::isfinite(observation.sample_time_seconds) && observation.sample_time_seconds >= 0.0 &&
           std::isfinite(observation.position_radians) &&
           std::isfinite(observation.velocity_radians_per_second) &&
           std::isfinite(observation.applied_torque_newton_metres) &&
           std::isfinite(observation.estimated_current_amperes) &&
           std::isfinite(observation.supply_voltage_volts) &&
           std::isfinite(observation.temperature_celsius);
}

[[nodiscard]] bool IsFiniteResult(const ServoStepResult& result) noexcept
{
    return std::isfinite(result.requested_torque_newton_metres) &&
           std::isfinite(result.estimated_current_amperes) &&
           std::isfinite(result.temperature_celsius);
}

[[nodiscard]] MechanicalValidationError ValidateScalar(
    const MechanicalScalar& scalar,
    bool positive) noexcept
{
    if(scalar.evidence_id.empty())
    {
        return MechanicalValidationError::MissingEvidence;
    }
    if(scalar.evidence_class == MechanicalEvidenceClass::Calibrated)
    {
        return MechanicalValidationError::CalibratedClaim;
    }
    if(!std::isfinite(scalar.value))
    {
        return MechanicalValidationError::NonFiniteValue;
    }
    if(positive ? scalar.value <= 0.0 : scalar.value < 0.0)
    {
        return positive ? MechanicalValidationError::InvalidPositiveValue
                        : MechanicalValidationError::InvalidNonNegativeValue;
    }
    return MechanicalValidationError::None;
}

[[nodiscard]] constexpr bool SameMechanicalValues(
    const MechanicalEffectsParameters& left,
    const MechanicalEffectsParameters& right) noexcept
{
    return left.coulomb_friction_newton_metres.value == right.coulomb_friction_newton_metres.value &&
           left.viscous_friction_newton_metre_seconds_per_radian.value ==
               right.viscous_friction_newton_metre_seconds_per_radian.value &&
           left.breakaway_torque_newton_metres.value == right.breakaway_torque_newton_metres.value &&
           left.stiction_enter_velocity_radians_per_second.value ==
               right.stiction_enter_velocity_radians_per_second.value &&
           left.stiction_exit_velocity_radians_per_second.value ==
               right.stiction_exit_velocity_radians_per_second.value &&
           left.backlash_half_width_radians.value == right.backlash_half_width_radians.value &&
           left.compliance_stiffness_newton_metres_per_radian.value ==
               right.compliance_stiffness_newton_metres_per_radian.value &&
           left.compliance_damping_newton_metre_seconds_per_radian.value ==
               right.compliance_damping_newton_metre_seconds_per_radian.value &&
           left.maximum_elastic_deflection_radians.value ==
               right.maximum_elastic_deflection_radians.value;
}

}  // namespace

MechanicalServoModel::MechanicalServoModel(
    ServoModel& inner_model,
    const MechanicalEffectsParameters& parameters,
    MechanicalEffectConfiguration effects) noexcept
    : inner_model_(inner_model),
      mechanical_parameters_(parameters),
      effects_(effects),
      configuration_valid_(
          ValidateMechanicalParameters(parameters) == MechanicalValidationError::None &&
          inner_model.Parameters().role == parameters.role)
{
}

const ServoParameterSet& MechanicalServoModel::Parameters() const noexcept
{
    return inner_model_.Parameters();
}

const MechanicalEffectsParameters& MechanicalServoModel::MechanicalParameters() const noexcept
{
    return mechanical_parameters_;
}

MechanicalEffectConfiguration MechanicalServoModel::Effects() const noexcept
{
    return effects_;
}

void MechanicalServoModel::SetEffects(MechanicalEffectConfiguration effects) noexcept
{
    effects_ = effects;
    stuck_ = false;
    compliance_torque_state_newton_metres_ = 0.0;
    elastic_deflection_radians_ = 0.0;
    backlash_initialized_ = true;
    backlash_transmitted_target_radians_ = last_observed_position_radians_;
    last_backlash_input_target_radians_ = last_observed_position_radians_;
    last_backlash_direction_ = 0;
}

void MechanicalServoModel::Reset(const ServoObservation& observation)
{
    inner_model_.Reset(observation);
    stuck_ = false;
    compliance_torque_state_newton_metres_ = 0.0;
    elastic_deflection_radians_ = 0.0;
    last_observed_position_radians_ = std::isfinite(observation.position_radians)
                                          ? observation.position_radians
                                          : 0.0;
    backlash_initialized_ = std::isfinite(observation.position_radians);
    backlash_transmitted_target_radians_ = last_observed_position_radians_;
    last_backlash_input_target_radians_ = last_observed_position_radians_;
    last_backlash_direction_ = 0;
    ResetIdentification();
}

ServoStepResult MechanicalServoModel::Step(
    const ServoCommand& command,
    const ServoObservation& observation,
    double timestep_seconds)
{
    if(!ConfigurationValid() || !InputsFinite(command, observation, timestep_seconds))
    {
        const double temperature = std::isfinite(observation.temperature_celsius)
                                       ? observation.temperature_celsius
                                       : 0.0;
        return ServoStepResult{
            0.0,
            0.0,
            temperature,
            observation.fault_flags | ToMask(ServoFaultFlag::ModelRejected),
        };
    }

    last_observed_position_radians_ = observation.position_radians;
    const ServoCommand transmitted_command = ApplyBacklash(command, observation);
    const ServoStepResult electrical_result =
        inner_model_.Step(transmitted_command, observation, timestep_seconds);
    if(!IsFiniteResult(electrical_result))
    {
        return ServoStepResult{
            0.0,
            0.0,
            observation.temperature_celsius,
            electrical_result.fault_flags | ToMask(ServoFaultFlag::ModelRejected),
        };
    }

    const double compliance_torque = ApplyCompliance(
        electrical_result.requested_torque_newton_metres,
        timestep_seconds);
    double friction_torque = 0.0;
    const double output_torque = ApplyFrictionAndStiction(
        compliance_torque,
        observation.velocity_radians_per_second,
        friction_torque);

    RecordIdentification(
        command,
        observation,
        electrical_result.requested_torque_newton_metres,
        compliance_torque,
        friction_torque,
        output_torque);

    return ServoStepResult{
        output_torque,
        electrical_result.estimated_current_amperes,
        electrical_result.temperature_celsius,
        electrical_result.fault_flags,
    };
}

MechanicalIdentificationSample MechanicalServoModel::LastIdentificationSample() const noexcept
{
    return last_identification_sample_;
}

MechanicalIdentificationAccumulator MechanicalServoModel::IdentificationAccumulator() const noexcept
{
    return identification_accumulator_;
}

void MechanicalServoModel::ResetIdentification() noexcept
{
    last_identification_sample_ = {};
    identification_accumulator_ = {};
}

bool MechanicalServoModel::IsStuck() const noexcept
{
    return stuck_;
}

double MechanicalServoModel::BacklashTransmittedTargetRadians() const noexcept
{
    return backlash_transmitted_target_radians_;
}

double MechanicalServoModel::ElasticDeflectionRadians() const noexcept
{
    return elastic_deflection_radians_;
}

bool MechanicalServoModel::ConfigurationValid() const noexcept
{
    return configuration_valid_;
}

bool MechanicalServoModel::InputsFinite(
    const ServoCommand& command,
    const ServoObservation& observation,
    double timestep_seconds) const noexcept
{
    return std::isfinite(timestep_seconds) && timestep_seconds > 0.0 &&
           IsFiniteCommand(command) && IsFiniteObservation(observation);
}

ServoCommand MechanicalServoModel::ApplyBacklash(
    const ServoCommand& command,
    const ServoObservation& observation) noexcept
{
    ServoCommand transmitted = command;
    if(!effects_.backlash_enabled || command.mode != ServoMode::Position)
    {
        backlash_transmitted_target_radians_ = command.target_position_radians;
        last_backlash_input_target_radians_ = command.target_position_radians;
        last_backlash_direction_ = 0;
        return transmitted;
    }

    if(!backlash_initialized_)
    {
        backlash_transmitted_target_radians_ = observation.position_radians;
        last_backlash_input_target_radians_ = observation.position_radians;
        backlash_initialized_ = true;
    }

    const double input_target = command.target_position_radians;
    const int direction = Sign(input_target - last_backlash_input_target_radians_);
    if(direction != 0 && last_backlash_direction_ != 0 && direction != last_backlash_direction_)
    {
        ++identification_accumulator_.reversal_count;
    }
    if(direction != 0)
    {
        last_backlash_direction_ = direction;
    }
    last_backlash_input_target_radians_ = input_target;

    const double half_width = mechanical_parameters_.backlash_half_width_radians.value;
    if(input_target > backlash_transmitted_target_radians_ + half_width)
    {
        backlash_transmitted_target_radians_ = input_target - half_width;
    }
    else if(input_target < backlash_transmitted_target_radians_ - half_width)
    {
        backlash_transmitted_target_radians_ = input_target + half_width;
    }
    transmitted.target_position_radians = backlash_transmitted_target_radians_;
    return transmitted;
}

double MechanicalServoModel::ApplyCompliance(
    double drive_torque,
    double timestep_seconds) noexcept
{
    if(!effects_.compliance_enabled)
    {
        compliance_torque_state_newton_metres_ = drive_torque;
        elastic_deflection_radians_ = 0.0;
        return drive_torque;
    }

    const double stiffness =
        mechanical_parameters_.compliance_stiffness_newton_metres_per_radian.value;
    const double damping =
        mechanical_parameters_.compliance_damping_newton_metre_seconds_per_radian.value;
    const double maximum_deflection =
        mechanical_parameters_.maximum_elastic_deflection_radians.value;
    const double maximum_torque = stiffness * maximum_deflection;
    const double bounded_drive = std::clamp(drive_torque, -maximum_torque, maximum_torque);
    const double time_constant = damping / stiffness;
    const double alpha = timestep_seconds / (time_constant + timestep_seconds);
    compliance_torque_state_newton_metres_ +=
        alpha * (bounded_drive - compliance_torque_state_newton_metres_);
    compliance_torque_state_newton_metres_ = std::clamp(
        compliance_torque_state_newton_metres_,
        -maximum_torque,
        maximum_torque);
    elastic_deflection_radians_ = compliance_torque_state_newton_metres_ / stiffness;
    return compliance_torque_state_newton_metres_;
}

double MechanicalServoModel::ApplyFrictionAndStiction(
    double transmitted_torque,
    double velocity_radians_per_second,
    double& friction_torque) noexcept
{
    const double absolute_velocity = std::fabs(velocity_radians_per_second);
    const double enter_velocity =
        mechanical_parameters_.stiction_enter_velocity_radians_per_second.value;
    const double exit_velocity =
        mechanical_parameters_.stiction_exit_velocity_radians_per_second.value;
    const double breakaway_torque =
        mechanical_parameters_.breakaway_torque_newton_metres.value;

    if(effects_.stiction_enabled)
    {
        if(stuck_)
        {
            if(absolute_velocity >= exit_velocity ||
               std::fabs(transmitted_torque) > breakaway_torque)
            {
                stuck_ = false;
            }
        }
        else if(absolute_velocity <= enter_velocity &&
                std::fabs(transmitted_torque) <= breakaway_torque)
        {
            stuck_ = true;
        }
    }
    else
    {
        stuck_ = false;
    }

    if(stuck_)
    {
        friction_torque = -transmitted_torque;
        return 0.0;
    }

    if(!effects_.friction_enabled)
    {
        friction_torque = 0.0;
        return transmitted_torque;
    }

    const int direction = absolute_velocity > enter_velocity
                              ? Sign(velocity_radians_per_second)
                              : Sign(transmitted_torque);
    if(direction == 0)
    {
        friction_torque = 0.0;
        return transmitted_torque;
    }

    double friction_magnitude =
        mechanical_parameters_.coulomb_friction_newton_metres.value +
        mechanical_parameters_.viscous_friction_newton_metre_seconds_per_radian.value *
            absolute_velocity;
    if(absolute_velocity <= enter_velocity)
    {
        friction_magnitude = std::min(friction_magnitude, std::fabs(transmitted_torque));
    }
    friction_torque = -static_cast<double>(direction) * friction_magnitude;
    return transmitted_torque + friction_torque;
}

void MechanicalServoModel::RecordIdentification(
    const ServoCommand& command,
    const ServoObservation& observation,
    double electrical_torque,
    double compliance_torque,
    double friction_torque,
    double output_torque) noexcept
{
    last_identification_sample_ = MechanicalIdentificationSample{
        command.sequence,
        observation.sample_time_seconds,
        observation.position_radians,
        observation.velocity_radians_per_second,
        electrical_torque,
        compliance_torque,
        friction_torque,
        output_torque,
        backlash_transmitted_target_radians_,
        elastic_deflection_radians_,
        stuck_,
    };
    ++identification_accumulator_.sample_count;
    if(stuck_)
    {
        ++identification_accumulator_.stuck_sample_count;
    }
    identification_accumulator_.sum_absolute_velocity_radians_per_second +=
        std::fabs(observation.velocity_radians_per_second);
    identification_accumulator_.sum_absolute_electrical_torque_newton_metres +=
        std::fabs(electrical_torque);
    identification_accumulator_.sum_absolute_friction_torque_newton_metres +=
        std::fabs(friction_torque);
    identification_accumulator_.maximum_absolute_elastic_deflection_radians = std::max(
        identification_accumulator_.maximum_absolute_elastic_deflection_radians,
        std::fabs(elastic_deflection_radians_));
}

std::string_view ToString(MechanicalEvidenceClass value) noexcept
{
    switch(value)
    {
        case MechanicalEvidenceClass::UpstreamApproximation:
            return "upstream_approximation";
        case MechanicalEvidenceClass::EngineeringEstimate:
            return "engineering_estimate";
        case MechanicalEvidenceClass::Calibrated:
            return "calibrated";
    }
    return "unknown";
}

std::string_view ToString(MechanicalValidationError value) noexcept
{
    switch(value)
    {
        case MechanicalValidationError::None:
            return "none";
        case MechanicalValidationError::EmptyIdentity:
            return "empty_identity";
        case MechanicalValidationError::MissingEvidence:
            return "missing_evidence";
        case MechanicalValidationError::RoleMismatch:
            return "role_mismatch";
        case MechanicalValidationError::CalibratedClaim:
            return "calibrated_claim";
        case MechanicalValidationError::NonFiniteValue:
            return "non_finite_value";
        case MechanicalValidationError::InvalidNonNegativeValue:
            return "invalid_non_negative_value";
        case MechanicalValidationError::InvalidPositiveValue:
            return "invalid_positive_value";
        case MechanicalValidationError::InvalidBreakawayOrder:
            return "invalid_breakaway_order";
        case MechanicalValidationError::InvalidStictionVelocityOrder:
            return "invalid_stiction_velocity_order";
        case MechanicalValidationError::DuplicateCrossRoleParameters:
            return "duplicate_cross_role_parameters";
    }
    return "unknown";
}

MechanicalValidationError ValidateMechanicalParameters(
    const MechanicalEffectsParameters& parameters) noexcept
{
    if(parameters.id.empty() || parameters.source_evidence_id.empty())
    {
        return MechanicalValidationError::EmptyIdentity;
    }
    if(parameters.overall_evidence_class == MechanicalEvidenceClass::Calibrated)
    {
        return MechanicalValidationError::CalibratedClaim;
    }

    constexpr std::array<const MechanicalScalar MechanicalEffectsParameters::*, 9>
        scalars{{
            &MechanicalEffectsParameters::coulomb_friction_newton_metres,
            &MechanicalEffectsParameters::viscous_friction_newton_metre_seconds_per_radian,
            &MechanicalEffectsParameters::breakaway_torque_newton_metres,
            &MechanicalEffectsParameters::stiction_enter_velocity_radians_per_second,
            &MechanicalEffectsParameters::stiction_exit_velocity_radians_per_second,
            &MechanicalEffectsParameters::backlash_half_width_radians,
            &MechanicalEffectsParameters::compliance_stiffness_newton_metres_per_radian,
            &MechanicalEffectsParameters::compliance_damping_newton_metre_seconds_per_radian,
            &MechanicalEffectsParameters::maximum_elastic_deflection_radians,
        }};
    for(std::size_t index = 0U; index < scalars.size(); ++index)
    {
        const bool positive = index == 2U || index == 4U || index >= 6U;
        const MechanicalValidationError error = ValidateScalar(parameters.*scalars[index], positive);
        if(error != MechanicalValidationError::None)
        {
            return error;
        }
    }
    if(parameters.breakaway_torque_newton_metres.value <
       parameters.coulomb_friction_newton_metres.value)
    {
        return MechanicalValidationError::InvalidBreakawayOrder;
    }
    if(!(parameters.stiction_enter_velocity_radians_per_second.value <
         parameters.stiction_exit_velocity_radians_per_second.value))
    {
        return MechanicalValidationError::InvalidStictionVelocityOrder;
    }
    return MechanicalValidationError::None;
}

const std::array<MechanicalEffectsParameters, 3>&
EngineeringMechanicalBaselines() noexcept
{
    return generated::kMechanicalBaselines;
}

const std::array<ServoActuatorBinding, 9>& MechanicalBindings() noexcept
{
    return generated::kMechanicalBindings;
}

const MechanicalEffectsParameters* FindMechanicalBaseline(std::string_view id) noexcept
{
    const auto& baselines = EngineeringMechanicalBaselines();
    const auto found = std::find_if(
        baselines.begin(),
        baselines.end(),
        [id](const MechanicalEffectsParameters& parameters) {
            return parameters.id == id;
        });
    return found == baselines.end() ? nullptr : &*found;
}

const MechanicalEffectsParameters* FindMechanicalBaselineForActuator(
    std::string_view actuator_name) noexcept
{
    const auto& bindings = MechanicalBindings();
    const auto found = std::find_if(
        bindings.begin(),
        bindings.end(),
        [actuator_name](const ServoActuatorBinding& binding) {
            return binding.actuator_name == actuator_name;
        });
    return found == bindings.end() ? nullptr : FindMechanicalBaseline(found->parameter_set_id);
}

static_assert(
    !SameMechanicalValues(generated::kMechanicalBaselines[0], generated::kMechanicalBaselines[1]));
static_assert(
    !SameMechanicalValues(generated::kMechanicalBaselines[0], generated::kMechanicalBaselines[2]));
static_assert(
    !SameMechanicalValues(generated::kMechanicalBaselines[1], generated::kMechanicalBaselines[2]));

}  // namespace reachy::servo
