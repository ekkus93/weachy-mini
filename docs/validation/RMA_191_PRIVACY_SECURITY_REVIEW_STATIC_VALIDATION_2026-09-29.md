# RMA-191 Privacy and Security Review Static Validation — 2026-09-29

## Scope

This document records the first permanent source-level regression gate for the RMA-191 privacy and security review.

The gate is intentionally conservative: it does not claim that the full RMA-191 release review is closed, and it does not replace physical Android credential testing or live cloud-provider disclosure testing. It creates repository-wide contracts that make the current security and privacy assumptions fail visibly if later code drifts.

## Added permanent gate

New test file:

- `scripts/tests/test_rma191_privacy_security_review.py`

The file is automatically included by the existing static validation path because `scripts/ci.sh --static-only` runs:

```bash
python3 -m unittest discover -s scripts/tests -v
```

## Coverage

The new static gate covers these RMA-191 review areas:

| RMA-191 area | Permanent source-level coverage |
| --- | --- |
| No embedded keys | Searches runtime/application, Android plugin, managed, Android-plugin, and script sources for common OpenAI-style, Google-style, and GitHub PAT-shaped credential literals. |
| Keystore usage | Requires the C# provider secret store to use `IReachyProviderSecretStore` through `ReachyProviderSecretBridge`, and requires exactly one Java bridge containing `AndroidKeyStore`, AES/GCM, key-invalidation handling, and delete support. |
| Network security configuration | Requires provider profile validation to default to HTTPS, restrict cleartext to explicit trusted-local-network mode, reject user-info/query/fragment base URLs, and avoid process-wide certificate bypass hooks. |
| Provider disclosure before cloud-bound data | Requires cloud LLM and cloud VLM authorization paths to require saved API keys, disclose conversation-text/camera-frame transfer, and expose revoke/fail-closed behavior. |
| Logs/diagnostics and private-media retention | Requires private media retention to remain default-denied, temporary media to be disposable/deleted, and diagnostic-bundle sources to retain redacted-only bundle semantics with manifest/log output and credential/raw-media references. |
| Native buffer and lifetime safety | Binds the review to existing native handle/buffer lifetime contracts for stale handles, post-close operations, invalid copy output preservation, busy state-copy output preservation, and busy snapshot-copy output preservation. |

## Current evidence boundary

This commit adds the review gate and this validation record. It does not by itself mark RMA-191 complete.

RMA-191 should remain open until an exact-head validation result confirms that the static unittest discovery path passed with this file present, and until any remaining physical or release-review obligations in the authoritative TODO are either evidenced or explicitly moved to a later milestone with rationale.

## Related exact-head physical evidence

Immediately before this review gate was added, exact-head A54 no-artifacts validation passed on `bff9c7789331ed72320260821d4d024f303d3a8b`:

- Workflow: `Local Unity Android Device Validation No Artifacts`
- Run: `36630438538`
- Job: `109617988864`
- Result: success

That physical run covers the Android device path, RMA-190 core E2E, RMA-090/091/092 camera stages, RMA-111 tracking, RMA-154 visual-servo, RMA-022 lifecycle, RMA-195 composition, and authoritative rendering for the source head immediately preceding this static review gate.

The new static RMA-191 gate still needs exact-head static qualification after this commit.
