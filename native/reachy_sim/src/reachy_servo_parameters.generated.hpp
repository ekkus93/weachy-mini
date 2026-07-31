#ifndef REACHY_SERVO_PARAMETERS_GENERATED_HPP
#define REACHY_SERVO_PARAMETERS_GENERATED_HPP

#include "reachy_servo_model.hpp"

#include <array>
#include <optional>

namespace reachy::servo::generated {

inline constexpr std::array<ServoParameterSet, 3> kParameterSets{{
    {
        "body_yaw_upstream_placeholder",
        ActuatorRole::BodyYaw,
        ParameterQuality::Placeholder,
        "chosen_actuator",
        "rma041_active_chosen_actuator_placeholder",
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_command_sample_period_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_command_latency_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_encoder_position_quantum_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_encoder_velocity_quantum_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_continuous_current_limit_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_peak_current_limit_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_peak_current_duration_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_stall_torque_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_no_load_speed_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_nominal_voltage_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_minimum_voltage_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_maximum_voltage_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_ambient_temperature_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_warning_temperature_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_body_yaw_shutdown_temperature_unknown"},
        ToMask(ServoFaultFlag::OverCurrent) |
            ToMask(ServoFaultFlag::OverTemperature) |
            ToMask(ServoFaultFlag::UnderVoltage) |
            ToMask(ServoFaultFlag::OverVoltage) |
            ToMask(ServoFaultFlag::Encoder) |
            ToMask(ServoFaultFlag::Communication) |
            ToMask(ServoFaultFlag::ModelRejected),
        0U,
        ParameterQuality::Placeholder,
        "rma061_body_yaw_fault_semantics_placeholder",
    },
    {
        "stewart_upstream_placeholder",
        ActuatorRole::Stewart,
        ParameterQuality::Placeholder,
        "chosen_actuator",
        "rma041_active_chosen_actuator_placeholder",
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_command_sample_period_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_command_latency_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_encoder_position_quantum_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_encoder_velocity_quantum_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_continuous_current_limit_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_peak_current_limit_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_peak_current_duration_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_stall_torque_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_no_load_speed_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_nominal_voltage_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_minimum_voltage_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_maximum_voltage_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_ambient_temperature_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_warning_temperature_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_stewart_shutdown_temperature_unknown"},
        ToMask(ServoFaultFlag::OverCurrent) |
            ToMask(ServoFaultFlag::OverTemperature) |
            ToMask(ServoFaultFlag::UnderVoltage) |
            ToMask(ServoFaultFlag::OverVoltage) |
            ToMask(ServoFaultFlag::Encoder) |
            ToMask(ServoFaultFlag::Communication) |
            ToMask(ServoFaultFlag::ModelRejected),
        0U,
        ParameterQuality::Placeholder,
        "rma061_stewart_fault_semantics_placeholder",
    },
    {
        "antenna_upstream_placeholder",
        ActuatorRole::Antenna,
        ParameterQuality::Placeholder,
        "chosen_actuator",
        "rma041_active_chosen_actuator_placeholder",
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_command_sample_period_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_command_latency_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_encoder_position_quantum_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_encoder_velocity_quantum_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_continuous_current_limit_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_peak_current_limit_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_peak_current_duration_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_stall_torque_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_no_load_speed_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_nominal_voltage_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_minimum_voltage_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_maximum_voltage_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_ambient_temperature_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_warning_temperature_unknown"},
        QualifiedScalar{std::nullopt, ParameterQuality::Placeholder, "rma061_antenna_shutdown_temperature_unknown"},
        ToMask(ServoFaultFlag::OverCurrent) |
            ToMask(ServoFaultFlag::OverTemperature) |
            ToMask(ServoFaultFlag::UnderVoltage) |
            ToMask(ServoFaultFlag::OverVoltage) |
            ToMask(ServoFaultFlag::Encoder) |
            ToMask(ServoFaultFlag::Communication) |
            ToMask(ServoFaultFlag::ModelRejected),
        0U,
        ParameterQuality::Placeholder,
        "rma061_antenna_fault_semantics_placeholder",
    },
}};

inline constexpr std::array<ServoActuatorBinding, 9> kActuatorBindings{{
    {"yaw_body", "body_yaw_upstream_placeholder", ActuatorRole::BodyYaw },
    {"stewart_1", "stewart_upstream_placeholder", ActuatorRole::Stewart },
    {"stewart_2", "stewart_upstream_placeholder", ActuatorRole::Stewart },
    {"stewart_3", "stewart_upstream_placeholder", ActuatorRole::Stewart },
    {"stewart_4", "stewart_upstream_placeholder", ActuatorRole::Stewart },
    {"stewart_5", "stewart_upstream_placeholder", ActuatorRole::Stewart },
    {"stewart_6", "stewart_upstream_placeholder", ActuatorRole::Stewart },
    {"right_antenna", "antenna_upstream_placeholder", ActuatorRole::Antenna },
    {"left_antenna", "antenna_upstream_placeholder", ActuatorRole::Antenna },
}};

}  // namespace reachy::servo::generated

#endif
