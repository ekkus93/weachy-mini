# RMA-194 built-APK deferred-feature audit — 2026-10-08

The existing RMA-194 static contracts inspect shipping source, Unity
dependencies, the Bootstrap scene, and fixed-camera admission. The new
`scripts/verify_rma194_release_apk.py` provides an additional bounded
audit of an actual built Android APK, with SHA-256 output.

It rejects known ARCore/OpenXR/AR Foundation payload names, explicit AR/XR
manifest declarations, unsafe ZIP paths, duplicate entries, missing
ARM64/Unity packaging structure, and oversized manifests. Deterministic
positive/negative fixtures are in
`scripts/tests/test_rma194_apk_payload_audit.py`.

Run it on the candidate APK after the build:
`python3 scripts/verify_rma194_release_apk.py Builds/Android/weachy-mini-device-arm64-api26.apk`.

This is a supplementary source-controlled tool; it is not yet wired into
the Android workflows, and no real APK has been audited by it. A passing
scan cannot exclude arbitrarily renamed IL2CPP behavior or substitute for
the complete exact-head release-candidate audit. RMA-194 acceptance
checkboxes remain open.
