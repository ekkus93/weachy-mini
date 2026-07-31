#ifndef REACHY_POWER_THERMAL_BASELINE_GENERATED_HPP
#define REACHY_POWER_THERMAL_BASELINE_GENERATED_HPP

#include "reachy_power_thermal_model.hpp"

namespace reachy::servo::generated {

inline constexpr SharedPowerSupplyParameters kSharedPowerSupply{
    "reachy_mini_shared_servo_bus_estimate",
    PowerThermalEvidenceClass::EngineeringEstimate,
    "rma064_internal_servo_bus_not_documented_engineering_baseline",
    PowerThermalScalar{5.0, PowerThermalEvidenceClass::EngineeringEstimate, "robotis_recommended_operating_voltage_5_volts_not_robot_input_voltage"},
    PowerThermalScalar{0.12, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_shared_wiring_regulator_battery_impedance_estimate"},
    PowerThermalScalar{5.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_conservative_shared_servo_current_budget_not_hardware_rating"},
    PowerThermalScalar{3.7, PowerThermalEvidenceClass::ManufacturerDerived, "robotis_documented_minimum_servo_input_voltage"},
};

inline constexpr std::array<ServoThermalParameters, 3> kServoThermalBaselines{{
    ServoThermalParameters{
        "body_yaw_xc330_m288_pg_thermal_estimate",
        ActuatorRole::BodyYaw,
        PowerThermalEvidenceClass::EngineeringEstimate,
        "rma064_body_yaw_thermal_estimate_from_xc330_proxy",
        PowerThermalScalar{2.7906976744186047, PowerThermalEvidenceClass::ManufacturerDerived, "six_volts_divided_by_xc330_proxy_2_15_amp_stall_current"},
        PowerThermalScalar{18.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_body_yaw_lumped_thermal_resistance_estimate"},
        PowerThermalScalar{12.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_body_yaw_lumped_thermal_capacitance_estimate"},
        PowerThermalScalar{65.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma062_five_degree_margin_below_robotis_limit"},
        PowerThermalScalar{70.0, PowerThermalEvidenceClass::ManufacturerDerived, "robotis_xc330_proxy_temperature_limit"},
        PowerThermalScalar{55.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_body_yaw_explicit_thermal_recovery_hysteresis"},
    },
    ServoThermalParameters{
        "stewart_xl330_m288_thermal_estimate",
        ActuatorRole::Stewart,
        PowerThermalEvidenceClass::EngineeringEstimate,
        "rma064_stewart_thermal_estimate_from_xl330_m288",
        PowerThermalScalar{3.4482758620689657, PowerThermalEvidenceClass::ManufacturerDerived, "six_volts_divided_by_xl330_m288_1_74_amp_stall_current"},
        PowerThermalScalar{22.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_stewart_lumped_thermal_resistance_estimate"},
        PowerThermalScalar{8.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_stewart_lumped_thermal_capacitance_estimate"},
        PowerThermalScalar{65.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma062_five_degree_margin_below_robotis_limit"},
        PowerThermalScalar{70.0, PowerThermalEvidenceClass::ManufacturerDerived, "robotis_xl330_m288_temperature_limit"},
        PowerThermalScalar{55.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_stewart_explicit_thermal_recovery_hysteresis"},
    },
    ServoThermalParameters{
        "antenna_xl330_m077_thermal_estimate",
        ActuatorRole::Antenna,
        PowerThermalEvidenceClass::EngineeringEstimate,
        "rma064_antenna_thermal_estimate_from_xl330_m077",
        PowerThermalScalar{3.4482758620689657, PowerThermalEvidenceClass::ManufacturerDerived, "six_volts_divided_by_xl330_m077_1_74_amp_stall_current"},
        PowerThermalScalar{28.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_antenna_lumped_thermal_resistance_estimate"},
        PowerThermalScalar{4.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_antenna_lumped_thermal_capacitance_estimate"},
        PowerThermalScalar{60.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_antenna_early_derating_engineering_margin"},
        PowerThermalScalar{70.0, PowerThermalEvidenceClass::ManufacturerDerived, "robotis_xl330_m077_temperature_limit"},
        PowerThermalScalar{50.0, PowerThermalEvidenceClass::EngineeringEstimate, "rma064_antenna_explicit_thermal_recovery_hysteresis"},
    },
}};

inline constexpr std::array<ServoActuatorBinding, kReachyPowerThermalActuatorCount> kPowerThermalBindings{{
    ServoActuatorBinding{"yaw_body", "body_yaw_xc330_m288_pg_thermal_estimate", ActuatorRole::BodyYaw},
    ServoActuatorBinding{"stewart_1", "stewart_xl330_m288_thermal_estimate", ActuatorRole::Stewart},
    ServoActuatorBinding{"stewart_2", "stewart_xl330_m288_thermal_estimate", ActuatorRole::Stewart},
    ServoActuatorBinding{"stewart_3", "stewart_xl330_m288_thermal_estimate", ActuatorRole::Stewart},
    ServoActuatorBinding{"stewart_4", "stewart_xl330_m288_thermal_estimate", ActuatorRole::Stewart},
    ServoActuatorBinding{"stewart_5", "stewart_xl330_m288_thermal_estimate", ActuatorRole::Stewart},
    ServoActuatorBinding{"stewart_6", "stewart_xl330_m288_thermal_estimate", ActuatorRole::Stewart},
    ServoActuatorBinding{"right_antenna", "antenna_xl330_m077_thermal_estimate", ActuatorRole::Antenna},
    ServoActuatorBinding{"left_antenna", "antenna_xl330_m077_thermal_estimate", ActuatorRole::Antenna},
}};

}  // namespace reachy::servo::generated

#endif
