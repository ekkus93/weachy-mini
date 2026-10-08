# RMA-194 deferred-feature shipping-scope regression — 2026-10-08

## Purpose

The initial Android digital twin is limited to Level 1 camera reprojection,
fixed presentation camera, and the authoritative production MuJoCo runtime.
Level 2 depth reprojection, Level 3 reconstruction, AR placement, movable
observer camera, and VLA remain separate future milestones.

## Shipping-source guard

The permanent `scripts/tests/test_rma194_release_acceptance.py` suite now
checks three concrete, executable boundaries:

1. The Unity package manifest does not introduce known AR Foundation, AR
   Subsystems, ARCore, ARKit, OpenXR, or XR Interaction packages.
2. The shipping `ReachyMainScreenBootstrap` explicitly rejects a camera whose
   framing is not `fixed_front_three_quarter` or whose
   `AcceptsUserNavigation` is true; the production application composition
   retains the fixed presentation-camera requirement.
3. A repository-wide scan of shipping Unity runtime C# rejects known AR/XR
   namespace imports and AR/depth/scene component creation/activation paths.

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
