# RMA-192 license and attribution review — 2026-09-25

## Scope

This review reconciles the release-facing third-party inventory, generated Reachy asset attribution, offline in-app notices, and the benchmark-selected local-model metadata. It verifies repository and shipping-policy state; it is not a legal opinion and does not claim rights beyond the recorded upstream licenses.

## Packaged dependency and asset inventory

`third_party/inventory.json` now distinguishes packaged runtime dependencies, deterministic build sources, generated Reachy derivatives, test-only fixtures, developer tools, and the selected-but-not-bundled local model. Packaged entries retain exact source revisions. The RMA-012 offline Licenses screen continues to expose one notice for every inventory entry.

MuJoCo and llama.cpp are packaged runtime inputs built from pinned upstream sources. CameraX and both bundled ML Kit dependencies are packaged Android dependencies. The selected Qwen GGUF is not committed or bundled. Reachy source inputs are fetched at the pinned revision and the presentation assets are generated deterministically.

## Reachy attribution, ShareAlike, and modification treatment

The pinned Reachy source contract records the hardware/model asset license as `CC-BY-NC-SA` and software license as `Apache-2.0`. `scripts/import_reachy_assets.py` copies source MJCF/mesh bytes without content modification, retains the upstream license, and emits `ATTRIBUTION.md` plus `PROVENANCE.json`.

The Unity presentation is not falsely described as unmodified. `scripts/prepare_reachy_unity_assets.py` performs scale baking, MuJoCo-to-Unity basis conversion, winding reversal, and STL-to-OBJ conversion. RMA-192 now requires the generated presentation output to carry both `ATTRIBUTION.md` and `UPSTREAM_LICENSE`; the attribution explicitly identifies the presentation as a modified derivative and preserves the attribution, noncommercial, ShareAlike, and modification-notice requirements. The generated `UNITY_RENDER_MAP.json` records the licensing notice hashes and transformation identity.

The offline application notice independently identifies the Reachy presentation meshes as coordinate-converted derivatives and states the same attribution/noncommercial/ShareAlike/modification obligations.

## No-endorsement disclosure

Both `third_party/THIRD_PARTY_NOTICES.md` and the installed offline Licenses screen state that Weachy Mini is an unofficial community project and is not endorsed by Pollen Robotics, Hugging Face, Google DeepMind, Unity, Google, or OpenAI. The project remains described as intended to be free and noncommercial.

## Selected local model

RMA-133 selected **Qwen3 0.6B Q4_K_M** from `Qwen/Qwen3-0.6B-GGUF`, revision `8e42d41f70cb6c571f58c3f31bd9287b372d97cc`, under the manifest-recorded Apache-2.0 license. The exact selected artifact is 396,704,416 bytes with SHA-256 `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`.

The GGUF remains excluded from the repository and is not bundled in the application. The current shipping Local Model screen deliberately reports INSTALL/IMPORT/SELECT/DELETE as unavailable. Any future acquisition path remains subject to the RMA-132 verified-package contract: source/manifest identity, expected size, SHA-256 verification, storage checks, and atomic installation. The inventory, third-party notice, and offline Licenses screen now identify the selected model and its license instead of the obsolete “candidate only” state.

## Regression contract

`scripts/tests/test_rma192_license_attribution_review.py` ties the following surfaces together so later changes fail visibly:

- packaged dependency status and exact source revisions;
- Reachy license/source-lock identity and derivative modification notice;
- generated-presentation attribution and upstream-license propagation;
- inventory-to-offline-license-screen coverage;
- no-endorsement disclosure;
- selected Qwen manifest identity, license, size, and SHA-256;
- the no-model-binary repository policy and currently unavailable shipping acquisition UI.

No physical-device evidence is required for this repository/license reconciliation.
