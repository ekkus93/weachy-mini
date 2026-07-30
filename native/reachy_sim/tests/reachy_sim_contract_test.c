int reachy_sim_contract_base_main(void);
#define main reachy_sim_contract_base_main
#include "reachy_sim_contract_test_base.inc"
#undef main

int reachy_sim_rma030_main(void);
#define g_failures g_rma030_failures
#define check_true rma030_check_true
#define check_status rma030_check_status
#define initialized_error rma030_initialized_error
#define main reachy_sim_rma030_main
#include "reachy_sim_concurrency_test.inc"
#undef main
#undef initialized_error
#undef check_status
#undef check_true
#undef g_failures

int main(void)
{
    const int base_result = reachy_sim_contract_base_main();
    const int hardening_result = reachy_sim_rma030_main();
    return base_result != 0 || hardening_result != 0 ? 1 : 0;
}
