# RMA-194 deferred-feature shipping-scope regression — 2026-10-08

## Purpose

The initial Android digital twin is limited to Level 1 camera reprojection,
fixed presentation camera, and the authoritative production MuJoCo runtime.
Level 2 depth reprojection, Level 3 reconstruction, AR placement, movable
observer camera, and VLA remain separate future milestones.

## Shipping-source guard

The permanent `scripts/tests/test_rma194_release_acceptance.py` suite now
checks four concrete, executable boundaries:

1. The Unity package manifest does not introduce known AR Foundation, AR
   Subsystems, ARCore, ARKit, OpenXR, or XR Interaction packages.
2. The shipping `ReachyMainScreenBootstrap` explicitly rejects a camera whose
   framing is not `fixed_front_three_quarter` or whose
   `AcceptsUserNavigation` is true; the production application composition
   retains the fixed presentation-camera requirement. Existing shell reuse is
   checked only *after* both authoritative runtime and fixed-camera validation;
   the permanent contract asserts that ordering so a preexisting main-screen
   component cannot bypass the safety boundary. Reused shells must also retain
   their original validated camera binding and required host/composition
   components; incomplete or differently bound shells fail closed.
3. A repository-wide scan of shipping Unity runtime C# rejects known AR/XR
   namespace imports and AR/depth/scene component creation/activation paths.

4. The checked-in Unity build settings enable only the reviewed Bootstrap scene.
   Its serialized YAML contains none of the known AR/XR/depth/scene/VLA
   activation names, and the pinned presentation-generation command still
   produces the named Reachy prefab and scene. This guards accidental
   shipping-scene additions; generated build output still needs an actual
   release-candidate inspection before final checkbox closure.

The test runs in the standard static Python suite, including hosted CI and the
RMA release-review contract workflow. It is a regression tripwire against
accidental deferred-feature activation, not a substitute for a complete
release-candidate source/scene/package audit.

## Acceptance boundary

The authoritative RMA-194 checkbox for no half-enabled deferred features
remains **open** pending complete release-candidate audit and exact-head
qualification. This contract does not claim that all possible future
feature implementations are detectable by the static patterns, nor does it
claim physical acceptance, calibration, or a final release label.

RMA-125 spoken offline interaction, RMA-184 representative-device
measurements, and the remaining RMA-190 scenarios are unaffected.
