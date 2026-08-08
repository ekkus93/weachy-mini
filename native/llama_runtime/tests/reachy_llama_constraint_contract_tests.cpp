#include "reachy_llama.h"

#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string>

namespace
{
[[noreturn]] void FailTest(const char * message)
{
    std::cerr << "RMA-133 constrained-runtime contract failure: " << message << '\n';
    std::exit(EXIT_FAILURE);
}

void Require(bool condition, const char * message)
{
    if (!condition)
    {
        FailTest(message);
    }
}

reachy_llama_generation_config DefaultGenerationConfig()
{
    reachy_llama_generation_config config{};
    Require(
        reachy_llama_default_generation_config(&config) == REACHY_LLAMA_STATUS_OK,
        "default generation config failed");
    return config;
}

reachy_llama_error_info FreshError()
{
    reachy_llama_error_info error{};
    error.struct_size = sizeof(error);
    return error;
}

int32_t StartWithConstraint(
    const reachy_llama_generation_constraint & constraint,
    reachy_llama_error_info & error)
{
    reachy_llama_generation_config config = DefaultGenerationConfig();
    reachy_llama_generation_handle generation = 99U;
    const int32_t status = reachy_llama_generation_start_constrained(
        999999U,
        "test prompt",
        &config,
        &constraint,
        &generation,
        &error);
    Require(generation == 0U, "failed constrained start returned a live handle");
    return status;
}

reachy_llama_generation_constraint ValidConstraint(const char * grammar, const char * root)
{
    reachy_llama_generation_constraint constraint{};
    constraint.struct_size = sizeof(constraint);
    constraint.abi_version = REACHY_LLAMA_ABI_VERSION;
    constraint.type = REACHY_LLAMA_CONSTRAINT_GBNF;
    constraint.grammar_utf8 = grammar;
    constraint.grammar_bytes = std::strlen(grammar);
    constraint.root_utf8 = root;
    constraint.root_bytes = std::strlen(root);
    return constraint;
}

void TestAbi2Surface()
{
    Require(REACHY_LLAMA_ABI_VERSION == 2U, "header ABI must be 2");
    Require(reachy_llama_abi_version() == 2U, "runtime ABI must be 2");
    Require(
        std::string(reachy_llama_version_string()) == "rma133-abi2-constrained",
        "runtime version string must identify constrained ABI 2");
    Require(
        std::string(reachy_llama_status_string(REACHY_LLAMA_STATUS_INVALID_CONSTRAINT)) ==
            "invalid_constraint",
        "invalid-constraint status string mismatch");
    Require(
        std::string(reachy_llama_status_string(REACHY_LLAMA_STATUS_CONSTRAINT_INIT_FAILED)) ==
            "constraint_init_failed",
        "constraint-init status string mismatch");
}

void TestConstraintAbiAndTypeValidation()
{
    const char grammar[] = "root ::= \"ok\"";
    const char root[] = "root";

    reachy_llama_generation_constraint constraint = ValidConstraint(grammar, root);
    constraint.struct_size -= 1U;
    reachy_llama_error_info error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_ABI_MISMATCH,
        "wrong constraint struct size must fail ABI validation");

    constraint = ValidConstraint(grammar, root);
    constraint.abi_version = 1U;
    error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_ABI_MISMATCH,
        "ABI-1 constraint must not be accepted as ABI 2");

    constraint = ValidConstraint(grammar, root);
    constraint.type = REACHY_LLAMA_CONSTRAINT_NONE;
    error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
        "constrained entry point must not silently disable constraints");

    constraint = ValidConstraint(grammar, root);
    constraint.type = 999U;
    error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
        "unknown constraint enum must fail explicitly");

    constraint = ValidConstraint(grammar, root);
    constraint.reserved = 1U;
    error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
        "nonzero constraint reserved field must fail explicitly");
}

void TestConstraintContentValidationPrecedesModelLookup()
{
    const char grammar[] = "root ::= \"ok\"";
    const char root[] = "root";

    reachy_llama_generation_constraint constraint = ValidConstraint(grammar, root);
    constraint.grammar_utf8 = nullptr;
    reachy_llama_error_info error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
        "missing grammar must fail explicitly");

    const char embedded_nul[] = {'r', 'o', 'o', 't', ' ', ':', ':', '=', ' ', '\0', 'x'};
    constraint = ValidConstraint(grammar, root);
    constraint.grammar_utf8 = embedded_nul;
    constraint.grammar_bytes = sizeof(embedded_nul);
    error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
        "embedded NUL must be rejected rather than truncated");

    const char invalid_utf8[] = {'r', 'o', 'o', 't', ' ', (char)0xc3, (char)0x28};
    constraint = ValidConstraint(grammar, root);
    constraint.grammar_utf8 = invalid_utf8;
    constraint.grammar_bytes = sizeof(invalid_utf8);
    error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
        "invalid UTF-8 grammar must fail explicitly");

    constraint = ValidConstraint(grammar, "bad root");
    error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
        "unsafe root name must fail explicitly");

    constraint = ValidConstraint(grammar, root);
    constraint.grammar_bytes = (size_t)REACHY_LLAMA_MAX_GRAMMAR_BYTES + 1U;
    error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
        "oversized grammar declaration must fail before reading caller memory");


    constraint = ValidConstraint(grammar, root);
    constraint.root_bytes = (size_t)REACHY_LLAMA_MAX_GRAMMAR_ROOT_BYTES + 1U;
    error = FreshError();
    Require(
        StartWithConstraint(constraint, error) == REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
        "oversized root declaration must fail before reading caller memory");
}

void TestValidConstraintDoesNotBecomeFallback()
{
    const char grammar[] = "root ::= \"ok\"";
    reachy_llama_generation_constraint constraint = ValidConstraint(grammar, "root");
    reachy_llama_error_info error = FreshError();
    const int32_t status = StartWithConstraint(constraint, error);
    Require(status == REACHY_LLAMA_STATUS_NOT_FOUND, "valid constraint should proceed to model lookup");
    Require(error.status == REACHY_LLAMA_STATUS_NOT_FOUND, "model lookup failure must remain visible");
}
} // namespace

int main()
{
    TestAbi2Surface();
    TestConstraintAbiAndTypeValidation();
    TestConstraintContentValidationPrecedesModelLookup();
    TestValidConstraintDoesNotBecomeFallback();
    return EXIT_SUCCESS;
}
