# RMA-102 GPU Homography Warp Validation

**Milestone:** RMA-102  
**Implementation SHA:** `b5aabd8f4e937867ec72e75539a96a6182ecd89b`  
**Final repair validation date:** 2026-08-05  
**Status:** Complete

## Scope

RMA-102 implements the rotation-only inverse homography:

```text
H_phone_to_reachy =
    K_reachy
    * R_currentReachy_from_currentPhone
    * inverse(K_phone)
```

The shader consumes the exact inverse mapping, emits independent GPU-resident
color and validity render textures, rejects rays behind the phone camera and
out-of-bounds source coordinates before sampling, supports independent output
resolution, and performs no runtime CPU image readback.

## Root-cause correction

The initial self-hosted Unity evidence was invalid because
`scripts/run_unity_tests.sh` always supplied `-nographics`. Unity therefore
initialized `NullGfxDevice`, and the failing identity-shader value
`0.80392158` was not authoritative evidence about the production shader.

The repair on the implementation SHA:

- removed the unconditional `-nographics` path;
- requires a real graphics mode and a usable display;
- defaults test execution to OpenGL Core;
- rejects Unity logs containing `NullGfxDevice` or `Renderer: Null Device`;
- adds a test-level `GraphicsDeviceType.Null` guard;
- releases an active render target only after clearing `RenderTexture.active`;
- leaves the shader math and the `> 0.9` identity assertion unchanged; and
- removes the Ruff hook's `2>/dev/null || true` fail-open behavior.

## Hosted validation

Hosted CI run `31008738003` passed on the exact implementation SHA:

- Actionlint;
- Ruff lint and format;
- ShellCheck;
- repository static policy;
- native warnings-as-errors tests;
- ASan/UBSan tests;
- managed warnings-as-errors tests;
- pinned Reachy model validation; and
- Android lint, Java warnings, and tests.

## Authoritative self-hosted validation

Local Unity Android Validation run `31009555103` completed successfully on
the `kawa` self-hosted runner against the exact implementation SHA.

The uploaded Unity logs prove:

- Unity initialized OpenGL Core with Mesa llvmpipe;
- `NullGfxDevice` and `Renderer: Null Device` were absent;
- EditMode passed `110/110`;
- PlayMode passed `1/1`;
- `IdentityGpuWarpEmitsColorAndValidityMask` passed without weakening its
  threshold; and
- the previous active-render-texture release warning was absent.

The same run also passed:

- ARM64 API-26 IL2CPP APK build and verification;
- RMA-090 camera discovery acceptance;
- RMA-091 camera acquisition acceptance;
- RMA-092 camera texture acceptance;
- RMA-022 lifecycle acceptance;
- authoritative rendering acceptance;
- all evidence uploads; and
- final exact-SHA commit-status publication.

## Artifacts

Run `31009555103` published:

- `local-unity-test-results-b5aabd8f4e937867ec72e75539a96a6182ecd89b`;
- `rma090-camera-device-report-b5aabd8f4e937867ec72e75539a96a6182ecd89b`;
- `rma091-camera-device-report-b5aabd8f4e937867ec72e75539a96a6182ecd89b`;
- `rma092-camera-texture-report-b5aabd8f4e937867ec72e75539a96a6182ecd89b`;
- `unity-lifecycle-device-report-b5aabd8f4e937867ec72e75539a96a6182ecd89b`;
- `unity-authoritative-device-report-b5aabd8f4e937867ec72e75539a96a6182ecd89b`; and
- `local-unity-device-apk-b5aabd8f4e937867ec72e75539a96a6182ecd89b`.

The exact commit carries successful `RMA-102 GPU Homography Warp` and
`Local Unity Android Validation` evidence. The implementation is accepted as
the RMA-102 baseline for RMA-103 and RMA-104.
