# First-party warning policy

Every warning or lint error in code owned by this repository is a defect. It must be corrected at its source before the related task is complete.

## Required behavior

- C and C++ first-party targets compile with strict warnings and warnings-as-errors.
- Java first-party targets use `-Xlint:all -Werror` and Android Lint uses warnings-as-errors.
- Managed first-party projects enable .NET analyzers and warnings-as-errors; Unity code also follows `.editorconfig` and Unity test coverage.
- Python scripts are checked by Ruff and formatting checks.
- Shell scripts are checked by ShellCheck.
- CI must fail on a first-party warning or lint error.

## Prohibited behavior

- Do not add warning-disable pragmas, blanket ignore lists, analyzer suppressions, lint baselines, or fake generated-code labels merely to make checks pass.
- Do not weaken the global warning level because one file is noisy.
- Do not catch and discard an error that should fail a task.
- Do not edit third-party code to satisfy first-party style or warning rules.

## Third-party isolation

Third-party targets must be built in separate targets or external projects. First-party warning flags must not leak into third-party compilation. Existing third-party warnings are recorded as upstream information only when they affect integration; they are not hidden by modifying vendored source.

A narrowly scoped compatibility flag for a third-party target may be considered later only when required to compile an unmodified dependency and when its rationale is documented. It must never apply to first-party code.
