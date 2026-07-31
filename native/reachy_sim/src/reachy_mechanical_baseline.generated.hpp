#ifndef REACHY_MECHANICAL_BASELINE_GENERATED_HPP
#define REACHY_MECHANICAL_BASELINE_GENERATED_HPP

#include "reachy_mechanical_servo_model.hpp"

#include <array>

namespace reachy::servo::generated {

inline constexpr std::array<MechanicalEffectsParameters, 3> kMechanicalBaselines{{
    {
        "body_yaw_xc330_m288_pg_mechanical_estimate",
        ActuatorRole::BodyYaw,
        MechanicalEvidenceClass::EngineeringEstimate,
        "rma063_body_custom_plastic_gear_role_specific_derivation",
        MechanicalScalar{0.044000000000000004, MechanicalEvidenceClass::EngineeringEstimate, "rma063_body_four_percent_of_rma062_proxy_stall_torque"},
        MechanicalScalar{0.0010829099220685664, MechanicalEvidenceClass::EngineeringEstimate, "rma063_body_one_percent_stall_torque_at_proxy_no_load_speed"},
        MechanicalScalar{0.066000000000000003, MechanicalEvidenceClass::EngineeringEstimate, "rma063_body_one_point_five_times_coulomb_breakaway"},
        MechanicalScalar{0.011990411961201044, MechanicalEvidenceClass::EngineeringEstimate, "rma063_half_rma062_velocity_quantum_enter_threshold"},
        MechanicalScalar{0.035971235883603132, MechanicalEvidenceClass::EngineeringEstimate, "rma063_one_point_five_rma062_velocity_quanta_exit_threshold"},
        MechanicalScalar{0.0030679615757712823, MechanicalEvidenceClass::EngineeringEstimate, "rma063_body_two_encoder_pulses_custom_plastic_gear_estimate"},
        MechanicalScalar{31.428571428571427, MechanicalEvidenceClass::EngineeringEstimate, "rma063_body_proxy_stall_torque_over_0_035_rad_deflection"},
        MechanicalScalar{0.62857142857142856, MechanicalEvidenceClass::EngineeringEstimate, "rma063_body_twenty_millisecond_torsional_time_constant"},
        MechanicalScalar{0.035000000000000003, MechanicalEvidenceClass::EngineeringEstimate, "rma063_body_custom_plastic_gear_deflection_bound"},
    },
    {
        "stewart_xl330_m288_mechanical_estimate",
        ActuatorRole::Stewart,
        MechanicalEvidenceClass::EngineeringEstimate,
        "rma063_stewart_xl330_m288_role_specific_derivation",
        MechanicalScalar{0.024, MechanicalEvidenceClass::EngineeringEstimate, "rma063_stewart_four_percent_of_rma062_stall_torque"},
        MechanicalScalar{0.00046581934563481566, MechanicalEvidenceClass::EngineeringEstimate, "rma063_stewart_one_percent_stall_torque_at_no_load_speed"},
        MechanicalScalar{0.035999999999999997, MechanicalEvidenceClass::EngineeringEstimate, "rma063_stewart_one_point_five_times_coulomb_breakaway"},
        MechanicalScalar{0.011990411961201044, MechanicalEvidenceClass::EngineeringEstimate, "rma063_half_rma062_velocity_quantum_enter_threshold"},
        MechanicalScalar{0.035971235883603132, MechanicalEvidenceClass::EngineeringEstimate, "rma063_one_point_five_rma062_velocity_quanta_exit_threshold"},
        MechanicalScalar{0.0015339807878856412, MechanicalEvidenceClass::EngineeringEstimate, "rma063_stewart_one_encoder_pulse_estimate"},
        MechanicalScalar{30, MechanicalEvidenceClass::EngineeringEstimate, "rma063_stewart_stall_torque_over_0_02_rad_deflection"},
        MechanicalScalar{0.29999999999999999, MechanicalEvidenceClass::EngineeringEstimate, "rma063_stewart_ten_millisecond_torsional_time_constant"},
        MechanicalScalar{0.02, MechanicalEvidenceClass::EngineeringEstimate, "rma063_stewart_linkage_drive_deflection_bound"},
    },
    {
        "antenna_xl330_m077_mechanical_estimate",
        ActuatorRole::Antenna,
        MechanicalEvidenceClass::EngineeringEstimate,
        "rma063_antenna_xl330_m077_role_specific_derivation",
        MechanicalScalar{0.0068399999999999997, MechanicalEvidenceClass::EngineeringEstimate, "rma063_antenna_three_percent_of_rma062_stall_torque"},
        MechanicalScalar{4.7746482927568608e-05, MechanicalEvidenceClass::EngineeringEstimate, "rma063_antenna_one_percent_stall_torque_at_no_load_speed"},
        MechanicalScalar{0.01026, MechanicalEvidenceClass::EngineeringEstimate, "rma063_antenna_one_point_five_times_coulomb_breakaway"},
        MechanicalScalar{0.011990411961201044, MechanicalEvidenceClass::EngineeringEstimate, "rma063_half_rma062_velocity_quantum_enter_threshold"},
        MechanicalScalar{0.035971235883603132, MechanicalEvidenceClass::EngineeringEstimate, "rma063_one_point_five_rma062_velocity_quanta_exit_threshold"},
        MechanicalScalar{0.0023009711818284618, MechanicalEvidenceClass::EngineeringEstimate, "rma063_antenna_one_point_five_encoder_pulse_estimate"},
        MechanicalScalar{5.7000000000000002, MechanicalEvidenceClass::EngineeringEstimate, "rma063_antenna_stall_torque_over_0_04_rad_deflection"},
        MechanicalScalar{0.085499999999999993, MechanicalEvidenceClass::EngineeringEstimate, "rma063_antenna_fifteen_millisecond_torsional_time_constant"},
        MechanicalScalar{0.040000000000000001, MechanicalEvidenceClass::EngineeringEstimate, "rma063_antenna_low_torque_drive_deflection_bound"},
    },
}};

inline constexpr std::array<ServoActuatorBinding, 9> kMechanicalBindings{{
    {"yaw_body", "body_yaw_xc330_m288_pg_mechanical_estimate", ActuatorRole::BodyYaw },
    {"stewart_1", "stewart_xl330_m288_mechanical_estimate", ActuatorRole::Stewart },
    {"stewart_2", "stewart_xl330_m288_mechanical_estimate", ActuatorRole::Stewart },
    {"stewart_3", "stewart_xl330_m288_mechanical_estimate", ActuatorRole::Stewart },
    {"stewart_4", "stewart_xl330_m288_mechanical_estimate", ActuatorRole::Stewart },
    {"stewart_5", "stewart_xl330_m288_mechanical_estimate", ActuatorRole::Stewart },
    {"stewart_6", "stewart_xl330_m288_mechanical_estimate", ActuatorRole::Stewart },
    {"right_antenna", "antenna_xl330_m077_mechanical_estimate", ActuatorRole::Antenna },
    {"left_antenna", "antenna_xl330_m077_mechanical_estimate", ActuatorRole::Antenna },
}};

}  // namespace reachy::servo::generated

#endif
