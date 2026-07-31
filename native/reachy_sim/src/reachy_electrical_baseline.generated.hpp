#ifndef REACHY_ELECTRICAL_BASELINE_GENERATED_HPP
#define REACHY_ELECTRICAL_BASELINE_GENERATED_HPP

#include "reachy_electrical_servo_model.hpp"

#include <array>
#include <optional>

namespace reachy::servo::generated {

inline constexpr std::array<ElectricalServoBaseline, 3> kElectricalBaselines{{
    {
        ServoParameterSet{
            "body_yaw_xc330_m288_pg_estimate",
            ActuatorRole::BodyYaw,
            ParameterQuality::ManufacturerEstimate,
            "xc330_m288_pg_proxy",
            "pollen_hardware_mapping_body_yaw_to_xc330_m288_pg_proxy",
            QualifiedScalar{std::optional<double>{0.01}, ParameterQuality::ManufacturerEstimate, "pollen_sdk_default_play_frequency_100_hz"},
            QualifiedScalar{std::optional<double>{0.01}, ParameterQuality::ManufacturerEstimate, "rma062_one_command_period_latency_estimate"},
            QualifiedScalar{std::optional<double>{0.0015339807878856412}, ParameterQuality::ManufacturerEstimate, "robotis_4096_pulses_per_revolution"},
            QualifiedScalar{std::optional<double>{0.023980823922402087}, ParameterQuality::ManufacturerEstimate, "robotis_velocity_unit_0_229_rpm"},
            QualifiedScalar{std::optional<double>{1.075}, ParameterQuality::ManufacturerEstimate, "rma062_conservative_half_stall_current_continuous_estimate"},
            QualifiedScalar{std::optional<double>{2.1499999999999999}, ParameterQuality::ManufacturerEstimate, "robotis_xc330_m288_pg_proxy_six_volt_stall_current"},
            QualifiedScalar{std::optional<double>{0.25}, ParameterQuality::ManufacturerEstimate, "rma062_bounded_peak_window_engineering_estimate"},
            QualifiedScalar{std::optional<double>{1.1000000000000001}, ParameterQuality::ManufacturerEstimate, "robotis_xc330_m288_pg_proxy_six_volt_stall_torque"},
            QualifiedScalar{std::optional<double>{10.157816246606997}, ParameterQuality::ManufacturerEstimate, "robotis_xc330_m288_pg_proxy_six_volt_no_load_speed"},
            QualifiedScalar{std::optional<double>{5}, ParameterQuality::ManufacturerEstimate, "robotis_recommended_operating_voltage_5_volts"},
            QualifiedScalar{std::optional<double>{3.7000000000000002}, ParameterQuality::ManufacturerEstimate, "robotis_xc330_m288_pg_proxy_documented_minimum_input_voltage"},
            QualifiedScalar{std::optional<double>{6}, ParameterQuality::ManufacturerEstimate, "robotis_documented_maximum_input_voltage_6_volts"},
            QualifiedScalar{std::optional<double>{25}, ParameterQuality::ManufacturerEstimate, "rma062_room_temperature_initial_condition"},
            QualifiedScalar{std::optional<double>{65}, ParameterQuality::ManufacturerEstimate, "rma062_five_degree_margin_below_robotis_limit"},
            QualifiedScalar{std::optional<double>{70}, ParameterQuality::ManufacturerEstimate, "robotis_xc330_m288_pg_proxy_temperature_limit"},
            ToMask(ServoFaultFlag::OverCurrent) |
                    ToMask(ServoFaultFlag::OverTemperature) |
                    ToMask(ServoFaultFlag::UnderVoltage) |
                    ToMask(ServoFaultFlag::OverVoltage) |
                    ToMask(ServoFaultFlag::Encoder) |
                    ToMask(ServoFaultFlag::Communication) |
                    ToMask(ServoFaultFlag::ModelRejected),
            ToMask(ServoFaultFlag::OverCurrent) |
                    ToMask(ServoFaultFlag::OverTemperature) |
                    ToMask(ServoFaultFlag::Encoder) |
                    ToMask(ServoFaultFlag::Communication) |
                    ToMask(ServoFaultFlag::ModelRejected),
            ParameterQuality::ManufacturerEstimate,
            "pollen_shutdown_mask_52_plus_rma062_fail_closed_mapping",
        },
        ElectricalControllerParameters{
            "body_yaw_xc330_m288_pg_estimate_controller",
            ActuatorRole::BodyYaw,
            ParameterQuality::ManufacturerEstimate,
            "pollen_hardware_mapping_body_yaw_to_xc330_m288_pg_proxy",
            QualifiedScalar{std::optional<double>{0.0015339807878856412}, ParameterQuality::ManufacturerEstimate, "robotis_goal_position_one_pulse"},
            QualifiedScalar{std::optional<double>{0.023980823922402087}, ParameterQuality::ManufacturerEstimate, "robotis_goal_velocity_unit_0_229_rpm"},
            QualifiedScalar{std::optional<double>{6.3025357464390561}, ParameterQuality::ManufacturerEstimate, "rma062_body_yaw_stall_at_ten_degree_error_gain"},
            QualifiedScalar{std::optional<double>{0.12605071492878112}, ParameterQuality::ManufacturerEstimate, "rma062_body_yaw_twenty_millisecond_damping_estimate"},
            QualifiedScalar{std::optional<double>{6}, ParameterQuality::ManufacturerEstimate, "robotis_xc330_m288_pg_proxy_highest_documented_performance_point"},
        },
    },
    {
        ServoParameterSet{
            "stewart_xl330_m288_estimate",
            ActuatorRole::Stewart,
            ParameterQuality::ManufacturerEstimate,
            "xl330_m288_t",
            "pollen_hardware_mapping_stewart_to_xl330_m288_t",
            QualifiedScalar{std::optional<double>{0.01}, ParameterQuality::ManufacturerEstimate, "pollen_sdk_default_play_frequency_100_hz"},
            QualifiedScalar{std::optional<double>{0.01}, ParameterQuality::ManufacturerEstimate, "rma062_one_command_period_latency_estimate"},
            QualifiedScalar{std::optional<double>{0.0015339807878856412}, ParameterQuality::ManufacturerEstimate, "robotis_4096_pulses_per_revolution"},
            QualifiedScalar{std::optional<double>{0.023980823922402087}, ParameterQuality::ManufacturerEstimate, "robotis_velocity_unit_0_229_rpm"},
            QualifiedScalar{std::optional<double>{0.87}, ParameterQuality::ManufacturerEstimate, "rma062_conservative_half_stall_current_continuous_estimate"},
            QualifiedScalar{std::optional<double>{1.74}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m288_t_six_volt_stall_current"},
            QualifiedScalar{std::optional<double>{0.25}, ParameterQuality::ManufacturerEstimate, "rma062_bounded_peak_window_engineering_estimate"},
            QualifiedScalar{std::optional<double>{0.59999999999999998}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m288_t_six_volt_stall_torque"},
            QualifiedScalar{std::optional<double>{12.880529879718152}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m288_t_six_volt_no_load_speed"},
            QualifiedScalar{std::optional<double>{5}, ParameterQuality::ManufacturerEstimate, "robotis_recommended_operating_voltage_5_volts"},
            QualifiedScalar{std::optional<double>{3.7000000000000002}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m288_t_documented_minimum_input_voltage"},
            QualifiedScalar{std::optional<double>{6}, ParameterQuality::ManufacturerEstimate, "robotis_documented_maximum_input_voltage_6_volts"},
            QualifiedScalar{std::optional<double>{25}, ParameterQuality::ManufacturerEstimate, "rma062_room_temperature_initial_condition"},
            QualifiedScalar{std::optional<double>{65}, ParameterQuality::ManufacturerEstimate, "rma062_five_degree_margin_below_robotis_limit"},
            QualifiedScalar{std::optional<double>{70}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m288_t_temperature_limit"},
            ToMask(ServoFaultFlag::OverCurrent) |
                    ToMask(ServoFaultFlag::OverTemperature) |
                    ToMask(ServoFaultFlag::UnderVoltage) |
                    ToMask(ServoFaultFlag::OverVoltage) |
                    ToMask(ServoFaultFlag::Encoder) |
                    ToMask(ServoFaultFlag::Communication) |
                    ToMask(ServoFaultFlag::ModelRejected),
            ToMask(ServoFaultFlag::OverCurrent) |
                    ToMask(ServoFaultFlag::OverTemperature) |
                    ToMask(ServoFaultFlag::Encoder) |
                    ToMask(ServoFaultFlag::Communication) |
                    ToMask(ServoFaultFlag::ModelRejected),
            ParameterQuality::ManufacturerEstimate,
            "pollen_shutdown_mask_52_plus_rma062_fail_closed_mapping",
        },
        ElectricalControllerParameters{
            "stewart_xl330_m288_estimate_controller",
            ActuatorRole::Stewart,
            ParameterQuality::ManufacturerEstimate,
            "pollen_hardware_mapping_stewart_to_xl330_m288_t",
            QualifiedScalar{std::optional<double>{0.0015339807878856412}, ParameterQuality::ManufacturerEstimate, "robotis_goal_position_one_pulse"},
            QualifiedScalar{std::optional<double>{0.023980823922402087}, ParameterQuality::ManufacturerEstimate, "robotis_goal_velocity_unit_0_229_rpm"},
            QualifiedScalar{std::optional<double>{3.4377467707849392}, ParameterQuality::ManufacturerEstimate, "rma062_stewart_stall_at_ten_degree_error_gain"},
            QualifiedScalar{std::optional<double>{0.068754935415698784}, ParameterQuality::ManufacturerEstimate, "rma062_stewart_twenty_millisecond_damping_estimate"},
            QualifiedScalar{std::optional<double>{6}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m288_t_highest_documented_performance_point"},
        },
    },
    {
        ServoParameterSet{
            "antenna_xl330_m077_estimate",
            ActuatorRole::Antenna,
            ParameterQuality::ManufacturerEstimate,
            "xl330_m077_t",
            "pollen_hardware_mapping_antenna_to_xl330_m077_t",
            QualifiedScalar{std::optional<double>{0.01}, ParameterQuality::ManufacturerEstimate, "pollen_sdk_default_play_frequency_100_hz"},
            QualifiedScalar{std::optional<double>{0.01}, ParameterQuality::ManufacturerEstimate, "rma062_one_command_period_latency_estimate"},
            QualifiedScalar{std::optional<double>{0.0015339807878856412}, ParameterQuality::ManufacturerEstimate, "robotis_4096_pulses_per_revolution"},
            QualifiedScalar{std::optional<double>{0.023980823922402087}, ParameterQuality::ManufacturerEstimate, "robotis_velocity_unit_0_229_rpm"},
            QualifiedScalar{std::optional<double>{0.87}, ParameterQuality::ManufacturerEstimate, "rma062_conservative_half_stall_current_continuous_estimate"},
            QualifiedScalar{std::optional<double>{1.74}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m077_t_six_volt_stall_current"},
            QualifiedScalar{std::optional<double>{0.25}, ParameterQuality::ManufacturerEstimate, "rma062_bounded_peak_window_engineering_estimate"},
            QualifiedScalar{std::optional<double>{0.22800000000000001}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m077_t_six_volt_stall_torque"},
            QualifiedScalar{std::optional<double>{47.752208334564855}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m077_t_six_volt_no_load_speed"},
            QualifiedScalar{std::optional<double>{5}, ParameterQuality::ManufacturerEstimate, "robotis_recommended_operating_voltage_5_volts"},
            QualifiedScalar{std::optional<double>{3.7000000000000002}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m077_t_documented_minimum_input_voltage"},
            QualifiedScalar{std::optional<double>{6}, ParameterQuality::ManufacturerEstimate, "robotis_documented_maximum_input_voltage_6_volts"},
            QualifiedScalar{std::optional<double>{25}, ParameterQuality::ManufacturerEstimate, "rma062_room_temperature_initial_condition"},
            QualifiedScalar{std::optional<double>{65}, ParameterQuality::ManufacturerEstimate, "rma062_five_degree_margin_below_robotis_limit"},
            QualifiedScalar{std::optional<double>{70}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m077_t_temperature_limit"},
            ToMask(ServoFaultFlag::OverCurrent) |
                    ToMask(ServoFaultFlag::OverTemperature) |
                    ToMask(ServoFaultFlag::UnderVoltage) |
                    ToMask(ServoFaultFlag::OverVoltage) |
                    ToMask(ServoFaultFlag::Encoder) |
                    ToMask(ServoFaultFlag::Communication) |
                    ToMask(ServoFaultFlag::ModelRejected),
            ToMask(ServoFaultFlag::OverCurrent) |
                    ToMask(ServoFaultFlag::OverTemperature) |
                    ToMask(ServoFaultFlag::Encoder) |
                    ToMask(ServoFaultFlag::Communication) |
                    ToMask(ServoFaultFlag::ModelRejected),
            ParameterQuality::ManufacturerEstimate,
            "pollen_shutdown_mask_52_plus_rma062_fail_closed_mapping",
        },
        ElectricalControllerParameters{
            "antenna_xl330_m077_estimate_controller",
            ActuatorRole::Antenna,
            ParameterQuality::ManufacturerEstimate,
            "pollen_hardware_mapping_antenna_to_xl330_m077_t",
            QualifiedScalar{std::optional<double>{0.0015339807878856412}, ParameterQuality::ManufacturerEstimate, "robotis_goal_position_one_pulse"},
            QualifiedScalar{std::optional<double>{0.023980823922402087}, ParameterQuality::ManufacturerEstimate, "robotis_goal_velocity_unit_0_229_rpm"},
            QualifiedScalar{std::optional<double>{1.306343772898277}, ParameterQuality::ManufacturerEstimate, "rma062_antenna_stall_at_ten_degree_error_gain"},
            QualifiedScalar{std::optional<double>{0.026126875457965541}, ParameterQuality::ManufacturerEstimate, "rma062_antenna_twenty_millisecond_damping_estimate"},
            QualifiedScalar{std::optional<double>{6}, ParameterQuality::ManufacturerEstimate, "robotis_xl330_m077_t_highest_documented_performance_point"},
        },
    },
}};

inline constexpr std::array<ServoActuatorBinding, 9> kElectricalBindings{{
    {"yaw_body", "body_yaw_xc330_m288_pg_estimate", ActuatorRole::BodyYaw},
    {"stewart_1", "stewart_xl330_m288_estimate", ActuatorRole::Stewart},
    {"stewart_2", "stewart_xl330_m288_estimate", ActuatorRole::Stewart},
    {"stewart_3", "stewart_xl330_m288_estimate", ActuatorRole::Stewart},
    {"stewart_4", "stewart_xl330_m288_estimate", ActuatorRole::Stewart},
    {"stewart_5", "stewart_xl330_m288_estimate", ActuatorRole::Stewart},
    {"stewart_6", "stewart_xl330_m288_estimate", ActuatorRole::Stewart},
    {"right_antenna", "antenna_xl330_m077_estimate", ActuatorRole::Antenna},
    {"left_antenna", "antenna_xl330_m077_estimate", ActuatorRole::Antenna},
}};

}  // namespace reachy::servo::generated

#endif
