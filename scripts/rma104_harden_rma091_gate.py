from pathlib import Path

path = Path(".github/workflows/rma091-camera-acquisition.yml")
text = path.read_text(encoding="utf-8")

close_test_anchor = (
    "      - 'Assets/ReachyMini/Tests/Editor/"
    "ReachyCameraTextureBridgeTests.cs'\n"
)
close_test_paths = close_test_anchor + (
    "      - 'Assets/ReachyMini/Tests/Editor/"
    "ReachyCameraCloseBarrierTests.cs'\n"
    "      - 'Assets/ReachyMini/Tests/Editor/"
    "ReachyCameraCloseBarrierTests.cs.meta'\n"
)
if text.count(close_test_anchor) != 2:
    raise SystemExit("Expected two RMA-091 camera test path anchors.")
text = text.replace(close_test_anchor, close_test_paths)

validation_anchor = (
    "      - 'docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md'\n"
)
validation_paths = validation_anchor + (
    "      - 'docs/validation/"
    "RMA_104_REPROJECTION_TEST_SUITE_VALIDATION_2026-08-05.md'\n"
)
if text.count(validation_anchor) != 2:
    raise SystemExit("Expected two RMA-091 documentation path anchors.")
text = text.replace(validation_anchor, validation_paths)

variable_anchor = (
    "          texture_tests = (root / "
    "'Assets/ReachyMini/Tests/Editor/"
    "ReachyCameraTextureBridgeTests.cs').read_text(encoding='utf-8')\n"
)
variable_block = variable_anchor + (
    "          close_barrier_tests = (root / "
    "'Assets/ReachyMini/Tests/Editor/"
    "ReachyCameraCloseBarrierTests.cs').read_text(encoding='utf-8')\n"
)
if text.count(variable_anchor) != 1:
    raise SystemExit("RMA-091 close-barrier test variable anchor drifted.")
text = text.replace(variable_anchor, variable_block)

java_anchor = (
    "          if '++generation' not in java or "
    "'expectedGeneration != generation' not in java:\n"
    "              raise SystemExit('Async CameraX callbacks are not "
    "session-invalidated.')\n\n"
)
java_block = java_anchor + (
    "          for token in [\n"
    "              '\"Stopping\".equals(state)',\n"
    "              'beginGracefulStopLocked()',\n"
    "              'handleStoppingCameraStateLocked',\n"
    "              'CameraState.Type.CLOSED',\n"
    "              'completeGracefulStopLocked()',\n"
    "              'camera_stop_failed',\n"
    "          ]:\n"
    "              if token not in java:\n"
    "                  raise SystemExit(\n"
    "                      f'Missing CameraX close-barrier behavior: {token}'\n"
    "                  )\n\n"
)
if text.count(java_anchor) != 1:
    raise SystemExit("RMA-091 Java contract anchor drifted.")
text = text.replace(java_anchor, java_block)

camerax_anchor = "          camerax = next(\n"
close_test_block = (
    "          for token in [\n"
    "              'ExplicitStopRemainsStoppingUntilClosedSnapshot',\n"
    "              'CameraSwitchStartsOnlyAfterClosedSnapshot',\n"
    "              'ReachyCameraAcquisitionState.Stopping',\n"
    "              'platform.CompleteStop()',\n"
    "              '\"CLOSED\"',\n"
    "          ]:\n"
    "              if token not in close_barrier_tests:\n"
    "                  raise SystemExit(\n"
    "                      f'Missing CameraX close-barrier regression: {token}'\n"
    "                  )\n\n"
)
if text.count(camerax_anchor) != 1:
    raise SystemExit("RMA-091 inventory anchor drifted.")
text = text.replace(camerax_anchor, close_test_block + camerax_anchor)

path.write_text(text, encoding="utf-8")
Path(__file__).unlink()
