#!/usr/bin/env python3
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
ACCEPTANCE = APP / "ReachyRma190RearCameraVlmEndToEndAcceptance.cs"
RUNNER = ROOT / "scripts/run_rma190_rear_camera_vlm_end_to_end_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma190-rear-camera-vlm-end-to-end.yml"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


class Rma190RearCameraVlmEndToEndContracts(unittest.TestCase):
    def test_android_vlm_fixture_loopback_has_explicit_narrow_network_permission(
        self,
    ) -> None:
        network_dir = ROOT / "Assets/Plugins/Android/ReachyProviderSecurity.androidlib"
        manifest = (network_dir / "AndroidManifest.xml").read_text(encoding="utf-8")
        self.assertIn(
            'android:networkSecurityConfig="@xml/reachy_provider_network_security"',
            manifest,
        )
        android_name = "{http://schemas.android.com/apk/res/android}name"
        requested_permissions = {
            permission.get(android_name)
            for permission in ET.fromstring(manifest).findall("uses-permission")
        }
        self.assertIn("android.permission.INTERNET", requested_permissions)
        policy = ET.parse(network_dir / "res/xml/reachy_provider_network_security.xml").getroot()
        base = policy.find("base-config")
        self.assertIsNotNone(base)
        self.assertEqual("false", base.attrib.get("cleartextTrafficPermitted"))
        allow = policy.find("domain-config")
        self.assertIsNotNone(allow)
        self.assertEqual("true", allow.attrib.get("cleartextTrafficPermitted"))
        self.assertEqual(
            {"localhost", "127.0.0.1"}, {item.text for item in allow.findall("domain")}
        )
        self.assertLess(
            WORKFLOW.read_text(encoding="utf-8").index("Pin one physical Android device"),
            WORKFLOW.read_text(encoding="utf-8").index("Build ARM64 API-26 device APK"),
        )

    def test_acceptance_uses_shipping_rear_camera_and_production_vlm(self) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        for token in (
            "ReachyApplicationHostBehaviour",
            "ReachyAndroidPerceptionApplicationService",
            "ReachyAndroidCameraAcquisition",
            "ReachyAndroidCameraTextureBridge",
            "ReachyProductionAuthoritativeRuntime",
            "ReachyDeviceCameraFacing.Rear",
            "ReachyCameraFacing.Rear",
            "CaptureAcceptanceFrameAsync",
            "VisionFrameOrigin.TransformedReachyEye",
            "ReachyCloudVlmCredentialCoordinator",
            "ReachyProviderEndpointStyle.ChatCompletions",
            "perception.AnalyzeSceneAsync(",
            "production_vlm_transport_and_encoder = true",
            "synthetic_camera_frame_injected = false",
            "raw_camera_payload_persisted = false",
        ):
            self.assertIn(token, source)

    def test_fixture_is_explicitly_non_cloud_and_non_calibration_evidence(self) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        self.assertIn("fixture.Provenance.ToString()", source)
        self.assertIn("deterministic_calibration_fixture = true", source)
        self.assertIn("measured_calibration_claimed = false", source)
        self.assertIn("external_vlm_fixture = true", source)
        self.assertIn("fixture_is_live_cloud_evidence = false", source)
        self.assertIn("not a measured device ", source)
        self.assertIn('"calibration."', source)

    def test_runner_uses_local_fixture_without_persisting_camera_payload(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        for token in (
            'reverse "tcp:${FIXTURE_PORT}" "tcp:${FIXTURE_PORT}"',
            "--ez reachy_rma190_rear_camera_vlm_e2e_acceptance true",
            "--es reachy_rma190_rear_camera_vlm_fixture_base_url",
            "data:image/jpeg;base64,",
            '"raw_request_persisted": False',
            '"authorization_value_persisted": False',
            'report["real_rear_camera"] is True',
            'report["production_vlm_transport_and_encoder"] is True',
            'report["fixture_is_live_cloud_evidence"] is False',
        ):
            self.assertIn(token, runner)
        self.assertNotIn('"request_body":', runner)
        self.assertNotIn('"authorization_value":', runner)

    def test_fixture_health_and_adb_reverse_mapping_are_checked_before_launch(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        self.assertIn("from urllib.request import ProxyHandler, build_opener", runner)
        self.assertIn("build_opener(ProxyHandler({})).open(", runner)
        self.assertIn("/health", runner)
        self.assertIn("response.status != 200 or response.read(16)", runner)
        self.assertIn('grep -Fq "tcp:${FIXTURE_PORT} tcp:${FIXTURE_PORT}"', runner)
        self.assertIn("RMA-190 expected ADB reverse fixture mapping", runner)
        self.assertIn('trap \'kill "${FIXTURE_PID}"', runner)
        self.assertIn("android\\.permission\\.INTERNET: granted=true", runner)
        self.assertLess(runner.index('FIXTURE_PID="$!"'), runner.index("/health"))
        self.assertLess(
            runner.index('"${ADB[@]}" install -r -g'),
            runner.index("android\\.permission\\.INTERNET: granted=true"),
        )
        self.assertLess(
            runner.index("build_opener(ProxyHandler({})).open("),
            runner.index('"${ADB[@]}" reverse "tcp:${FIXTURE_PORT}"'),
        )
        self.assertLess(
            runner.index("RMA-190 expected ADB reverse fixture mapping"),
            runner.index('"${ADB[@]}" install -r -g'),
        )

    def test_workflow_requires_physical_android_runner(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn(
            "name: RMA-190 Rear Camera VLM End-to-End Android Acceptance",
            workflow,
        )
        self.assertIn("weachy-mini-android-device", workflow)
        self.assertIn("build_unity_android.sh device-feasibility", workflow)
        self.assertIn(
            "run_rma190_rear_camera_vlm_end_to_end_android.sh",
            workflow,
        )
        self.assertIn(
            "rma190-rear-camera-vlm-end-to-end-${{ github.sha }}",
            workflow,
        )

    def test_resource_pressure_keeps_production_vlm_fail_closed(self) -> None:
        cloud_path = APP / "ReachyAndroidPerceptionApplicationService.CloudVlm.cs"
        cloud = cloud_path.read_text()
        driver = (APP / "ReachyPriorityDegradationRuntimeDriver.cs").read_text()
        self.assertIn("!decision.VlmAllowed", cloud)
        self.assertIn("priorityRuntime.LastSignals", driver)
        self.assertIn("sampled.AvailableMemoryBytes", driver)
        self.assertIn("sampled.PhysicsBudgetState", driver)

    def test_failed_physical_run_surfaces_bounded_resource_signals(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        self.assertIn("resource-transitions.txt", runner)
        self.assertIn("RMA-181 sampled resource transition:", runner)
        self.assertIn("deque(maxlen=24)", runner)
        self.assertIn("tail -n 24", runner)
        self.assertIn("if (( exit_code != 0 )); then", runner)

    def test_failed_vlm_request_exposes_only_allowlisted_fixture_metadata(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        self.assertIn("Sanitized RMA-190 VLM fixture request metadata:", runner)
        self.assertIn("safe_fields = (", runner)
        for token in (
            '"request_count"',
            '"body_bytes"',
            '"contains_jpeg_data_uri"',
            '"authorization_header_present"',
            '"raw_request_persisted"',
            '"authorization_value_persisted"',
        ):
            self.assertIn(token, runner)
        self.assertIn("No VLM fixture POST was recorded", runner)
        self.assertNotIn("print(metadata", runner)
        self.assertNotIn("print(body", runner)

    def test_acceptance_waits_for_shipping_vlm_admission_before_fresh_capture(self) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        for token in (
            "ReachyPriorityDegradationRuntimeDriver",
            "WaitForStableVlmAdmissionAsync",
            "AdmissionObservationMilliseconds = 100",
            "AdmissionTimeoutMilliseconds = 60_000",
            "RequiredStableAdmissionSamples = 1",
            "driver.CurrentDecision",
            "ReferenceEquals(decision, previousDecision)",
            "decision.VlmAllowed && physicsSignalAvailable",
            "PhysicsSignalUnavailable",
            "stable_vlm_admission_samples",
            "vlm_admission_diagnostic",
        ):
            self.assertIn(token, source)
        wait_stage = source.index('SetStage("wait-production-vlm-admission")')
        capture_stage = source.index('SetStage("capture-real-rear-transformed-frame")')
        vlm_call = source.index("perception.AnalyzeSceneAsync(")
        self.assertLess(wait_stage, capture_stage)
        self.assertLess(capture_stage, vlm_call)
        self.assertNotIn("priorityDegradation.CurrentDecision =", source)

    def test_unity_vlm_encoder_preserves_main_thread_context(self) -> None:
        encoder = (APP / "ReachyUnityRemoteVlmImageEncoder.cs").read_text(encoding="utf-8")
        cloud = (APP / "ReachyAndroidPerceptionApplicationService.CloudVlm.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("Texture2D(", encoder)
        self.assertIn("EncodeToJPG", encoder)
        self.assertIn("ConfigureAwait(true)", encoder)
        self.assertNotIn("ConfigureAwait(false)", encoder)

        current_scene = cloud[
            cloud.index("AnalyzeCurrentSceneAsync") : cloud.index(
                "public async ValueTask<VisionLanguageResult> AnalyzeSceneAsync"
            )
        ]
        capture_path = current_scene[: current_scene.index("finally")]
        analyze_scene_start = cloud.index(
            "public async ValueTask<VisionLanguageResult> AnalyzeSceneAsync"
        )
        analyze_scene_end = cloud.index("private void DisposeCloudVlm")
        analyze_scene = cloud[analyze_scene_start:analyze_scene_end]
        load_path = cloud[
            cloud.index("EnsureCloudVlmLoadedAsync") : cloud.index(
                "private (IVisionLanguageProvider? provider, string detail) LoadCloudVlm"
            )
        ]
        for critical_path in (capture_path, analyze_scene, load_path):
            self.assertIn("ConfigureAwait(true)", critical_path)
            self.assertNotIn("ConfigureAwait(false)", critical_path)

    def test_android_vlm_transport_diagnostics_retain_only_exception_types(self) -> None:
        source = (
            ROOT
            / "Assets/ReachyMini/Runtime/Core/Perception/"
            / "ReachyOpenAiVisionHttpTransport.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("catch (OperationCanceledException)", source)
        self.assertIn(
            "catch (Exception exception) when (exception is not OutOfMemoryException)",
            source,
        )
        self.assertIn('"transport-exception-types"', source)
        self.assertIn("DescribeTransportExceptionTypes(exception)", source)
        self.assertIn("current = current.InnerException", source)
        self.assertIn("depth < 4", source)
        self.assertIn("Math.Min(name.Length, 64)", source)
        self.assertIn("types.Append(safe ? character : '_')", source)
        diagnostic_start = source.index("private static string DescribeTransportExceptionTypes(")
        diagnostic_end = source.index("public void Dispose()", diagnostic_start)
        diagnostic = source[diagnostic_start:diagnostic_end]
        self.assertNotIn(".Message", diagnostic)
        self.assertNotIn("current.ToString()", diagnostic)
        self.assertNotIn("exception.ToString()", diagnostic)
        self.assertNotIn(".StackTrace", diagnostic)

    def test_fixture_policy_preflight_checks_actual_android_policy_before_camera(
        self,
    ) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        self.assertIn("android.security.NetworkSecurityPolicy", source)
        self.assertIn('"isCleartextTrafficPermitted"', source)
        self.assertIn('"127.0.0.1"', source)
        self.assertIn('"localhost"', source)
        self.assertIn("!string.IsNullOrEmpty(fixtureUri.UserInfo)", source)
        self.assertIn("!string.IsNullOrEmpty(fixtureUri.Query)", source)
        self.assertIn("!string.IsNullOrEmpty(fixtureUri.Fragment)", source)
        self.assertIn("Local cleartext has not been authorized.", source)
        self.assertLess(
            source.index('SetStage("verify-android-loopback-cleartext-policy")'),
            source.index('SetStage("wait-shipping-host")'),
        )
        self.assertIn("RequireLoopbackCleartextPolicy(fixtureBaseUrl)", source)

    def test_failed_vlm_transport_preserves_only_validated_physical_frame_evidence(
        self,
    ) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        self.assertIn("private Report? observedFrameEvidence;", source)
        self.assertIn("observedFrameEvidence = Report.ObservedFrame(", source)
        self.assertIn("observedFrameEvidence.production_vlm_transport_and_encoder = true;", source)
        self.assertIn("currentStage,\n                    observedFrameEvidence);", source)
        self.assertIn("Report report = observedEvidence ?? new Report();", source)
        self.assertIn('report.status = "failed";', source)
        observed = source[
            source.index("public static Report ObservedFrame(") : source.index(
                "public static Report Success("
            )
        ]
        self.assertIn("frame.Identity.SourceSequence", observed)
        self.assertIn("frame.Identity.AuthoritativeSequence", observed)
        self.assertIn("descriptor.SessionId", observed)
        self.assertIn("admission.StableSamples", observed)
        self.assertNotIn('status = "passed"', observed)
        self.assertNotIn("EncodedBytes", observed)
        self.assertIn("synthetic_camera_frame_injected = false", observed)

    def test_todo_stays_open_until_physical_run_passes(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        self.assertIn(
            "- [ ] Rear-camera visual question with optional VLM.",
            todo,
        )


if __name__ == "__main__":
    unittest.main()
