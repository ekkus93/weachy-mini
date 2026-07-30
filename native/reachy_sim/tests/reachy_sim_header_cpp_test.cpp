#include "reachy_sim.h"
#include "reachy_sim_state.h"

#include <cstdint>
#include <type_traits>

static_assert(sizeof(ReachySimHandle) == sizeof(std::uint64_t));
static_assert(std::is_standard_layout_v<ReachySimConfig>);
static_assert(std::is_standard_layout_v<ReachySimStateHeader>);
static_assert(std::is_standard_layout_v<ReachySimStateRequest>);
static_assert(std::is_standard_layout_v<ReachySimStatePayloadHeader>);
static_assert(std::is_standard_layout_v<ReachySimActuatorObservation>);
static_assert(std::is_standard_layout_v<ReachySimBodyPose>);
static_assert(std::is_standard_layout_v<ReachySimErrorInfo>);

int main()
{
    const ReachySimConfig config = reachy_sim_default_config();
    return config.abi_version == REACHY_SIM_ABI_VERSION ? 0 : 1;
}
