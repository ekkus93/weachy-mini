#!/usr/bin/env python3

from pathlib import Path


def replace_once(text: str, old: str, new: str, contract: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(
            f"{contract}: expected exactly one source pattern, found {count}")
    return text.replace(old, new, 1)


def repair_workflow() -> None:
    path = Path(".github/workflows/local-unity-android-validation.yml")
    text = path.read_text(encoding="utf-8")
    marker = '''      - name: Install and run RMA-090 camera discovery acceptance
'''
    pin_step = '''      - name: Pin one physical Android device
        if: ${{ github.event_name == 'push' || inputs.install_physical_device }}
        shell: bash
        run: |
          set -euo pipefail
          mapfile -t physical_serials < <(
            adb devices \
              | awk 'NR > 1 && $2 == "device" && $1 !~ /^emulator-/ {print $1}'
          )
          accepted=()
          for serial in "${physical_serials[@]}"; do
            state="$(adb -s "${serial}" get-state | tr -d '\\r')"
            abi="$(adb -s "${serial}" shell getprop ro.product.cpu.abi | tr -d '\\r')"
            sdk="$(adb -s "${serial}" shell getprop ro.build.version.sdk | tr -d '\\r')"
            printf 'physical_device=%s state=%s abi=%s sdk=%s\\n' \
              "${serial}" "${state}" "${abi}" "${sdk}"
            if [[ "${state}" == "device" && \
                  "${abi}" == "arm64-v8a" && \
                  "${sdk}" =~ ^[0-9]+$ ]] && (( sdk >= 26 )); then
              accepted+=("${serial}")
            fi
          done
          if (( ${#accepted[@]} != 1 )); then
            printf 'Expected exactly one physical arm64-v8a API-26+ device at suite start; found %s.\\n' \
              "${#accepted[@]}" >&2
            adb devices -l >&2
            exit 1
          fi
          serial="${accepted[0]}"
          adb -s "${serial}" wait-for-device
          if [[ "$(adb -s "${serial}" get-state | tr -d '\\r')" != "device" ]]; then
            printf 'Pinned Android device is not ready: %s\\n' "${serial}" >&2
            exit 1
          fi
          printf 'REACHY_ANDROID_SERIAL=%s\\n' "${serial}" >> "${GITHUB_ENV}"
          printf 'pinned_android_serial=%s\\n' "${serial}"

'''
    text = replace_once(
        text,
        marker,
        pin_step + marker,
        "physical-device pin insertion")
    path.write_text(text, encoding="utf-8")


def repair_java_bridge() -> None:
    path = Path(
        "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/src/main/java/"
        "com/ekkus93/weachy/camera/ReachyMlKitTrackingBridge.java")
    text = path.read_text(encoding="utf-8")
    old_input = '''        InputImage image = InputImage.fromBitmap(bitmap, 0);
        Task<List<Face>> faceTask;
        try {
            faceTask = faceDetector.process(image);
        } catch (Exception error) {
            failImmediately(state, "face_detection_start_failed", error);
            return;
        }'''
    new_input = '''        InputImage image;
        try {
            image = InputImage.fromBitmap(bitmap, 0);
        } catch (Exception error) {
            failImmediately(state, "input_image_failed", error);
            return;
        }

        Task<List<Face>> faceTask;
        try {
            faceTask = faceDetector.process(image);
        } catch (Exception error) {
            failImmediately(state, "face_detection_start_failed", error);
            return;
        }'''
    text = replace_once(
        text,
        old_input,
        new_input,
        "InputImage ownership cleanup")

    old_person = '''    private void completePerson(RequestState state, SegmentationMask mask) {
        synchronized (lock) {
            if (!isCurrent(state)) {
                return;
            }
            state.person = personDetection(mask);
            state.completedParts++;
            finishIfCompleteLocked(state);
        }
    }'''
    new_person = '''    private void completePerson(RequestState state, SegmentationMask mask) {
        PersonDetection person;
        try {
            person = personDetection(mask);
        } catch (Exception error) {
            failPart(state, "person_segmentation_result_failed", error);
            return;
        }
        synchronized (lock) {
            if (!isCurrent(state)) {
                return;
            }
            state.person = person;
            state.completedParts++;
            finishIfCompleteLocked(state);
        }
    }'''
    text = replace_once(
        text,
        old_person,
        new_person,
        "segmentation-result cleanup")
    path.write_text(text, encoding="utf-8")


def repair_acceptance() -> None:
    path = Path(
        "Assets/ReachyMini/Runtime/Application/"
        "ReachyRma111TrackingAcceptance.cs")
    text = path.read_text(encoding="utf-8")
    first_face_block = '''            TrackedObject firstFace = first.Objects
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Classification,
                        "face",
                        StringComparison.Ordinal)) ??
                throw new InvalidOperationException(
                    "Bundled ML Kit did not detect a face in the pinned public-domain fixture.");
'''
    first_with_person = first_face_block + '''            TrackedObject firstPerson = first.Objects
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Classification,
                        "person",
                        StringComparison.Ordinal)) ??
                throw new InvalidOperationException(
                    "Bundled ML Kit did not detect a person in the pinned public-domain fixture.");
'''
    text = replace_once(
        text,
        first_face_block,
        first_with_person,
        "first person acceptance")

    second_face_block = '''            TrackedObject secondFace = second.Objects
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Classification,
                        "face",
                        StringComparison.Ordinal)) ??
                throw new InvalidOperationException(
                    "Bundled ML Kit did not detect the fixture face on the second frame.");
'''
    second_with_person = second_face_block + '''            TrackedObject secondPerson = second.Objects
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Classification,
                        "person",
                        StringComparison.Ordinal)) ??
                throw new InvalidOperationException(
                    "Bundled ML Kit did not detect the fixture person on the second frame.");
'''
    text = replace_once(
        text,
        second_face_block,
        second_with_person,
        "second person acceptance")

    face_stability = '''            if (!string.Equals(
                    firstFace.LocalId,
                    secondFace.LocalId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The managed stable face identifier changed across equivalent frames.");
            }
'''
    both_stability = face_stability + '''            if (!string.Equals(
                    firstPerson.LocalId,
                    secondPerson.LocalId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The managed stable person identifier changed across equivalent frames.");
            }
'''
    text = replace_once(
        text,
        face_stability,
        both_stability,
        "person ID stability acceptance")

    old_success_call = '''                first.Objects.Count(item =>
                    item.Classification == "person"),
                firstFace.LocalId,
                secondFace.LocalId,
                invalidFaceSuppressed);'''
    new_success_call = '''                first.Objects.Count(item =>
                    item.Classification == "person"),
                second.Objects.Count(item =>
                    item.Classification == "person"),
                firstFace.LocalId,
                secondFace.LocalId,
                firstPerson.LocalId,
                secondPerson.LocalId,
                invalidFaceSuppressed);'''
    text = replace_once(
        text,
        old_success_call,
        new_success_call,
        "person report arguments")

    old_fields = '''            public int first_person_count;
            public string first_face_id = string.Empty;
            public string second_face_id = string.Empty;
            public bool stable_face_id;
            public bool invalid_center_suppressed;'''
    new_fields = '''            public int first_person_count;
            public int second_person_count;
            public string first_face_id = string.Empty;
            public string second_face_id = string.Empty;
            public bool stable_face_id;
            public string first_person_id = string.Empty;
            public string second_person_id = string.Empty;
            public bool stable_person_id;
            public bool invalid_center_suppressed;'''
    text = replace_once(text, old_fields, new_fields, "person report fields")

    old_signature = '''                int faceCount,
                int personCount,
                string firstFaceId,
                string secondFaceId,
                bool invalidCenterSuppressed)'''
    new_signature = '''                int faceCount,
                int firstPersonCount,
                int secondPersonCount,
                string firstFaceId,
                string secondFaceId,
                string firstPersonId,
                string secondPersonId,
                bool invalidCenterSuppressed)'''
    text = replace_once(
        text,
        old_signature,
        new_signature,
        "person report success signature")

    old_initializer = '''                    first_face_count = faceCount,
                    first_person_count = personCount,
                    first_face_id = firstFaceId,
                    second_face_id = secondFaceId,
                    stable_face_id = string.Equals(
                        firstFaceId,
                        secondFaceId,
                        StringComparison.Ordinal),
                    invalid_center_suppressed =
                        invalidCenterSuppressed,'''
    new_initializer = '''                    first_face_count = faceCount,
                    first_person_count = firstPersonCount,
                    second_person_count = secondPersonCount,
                    first_face_id = firstFaceId,
                    second_face_id = secondFaceId,
                    stable_face_id = string.Equals(
                        firstFaceId,
                        secondFaceId,
                        StringComparison.Ordinal),
                    first_person_id = firstPersonId,
                    second_person_id = secondPersonId,
                    stable_person_id = string.Equals(
                        firstPersonId,
                        secondPersonId,
                        StringComparison.Ordinal),
                    invalid_center_suppressed =
                        invalidCenterSuppressed,'''
    text = replace_once(
        text,
        old_initializer,
        new_initializer,
        "person report success initializer")
    path.write_text(text, encoding="utf-8")


def repair_acceptance_script() -> None:
    path = Path("scripts/run_rma111_lightweight_tracking_acceptance_android.sh")
    text = path.read_text(encoding="utf-8")
    old_true = '''required_true = (
    "acceptance_enabled",
    "stable_face_id",
    "invalid_center_suppressed",
)'''
    new_true = '''required_true = (
    "acceptance_enabled",
    "stable_face_id",
    "stable_person_id",
    "invalid_center_suppressed",
)'''
    text = replace_once(text, old_true, new_true, "person truth gate")

    old_checks = '''if int(report.get("first_face_count", 0)) < 1:
    raise SystemExit(f"RMA-111 detected no face: {report}")
if report.get("first_face_id") != report.get("second_face_id"):
    raise SystemExit(f"RMA-111 stable ID mismatch: {report}")'''
    new_checks = '''if int(report.get("first_face_count", 0)) < 1:
    raise SystemExit(f"RMA-111 detected no face: {report}")
if int(report.get("first_person_count", 0)) < 1:
    raise SystemExit(f"RMA-111 detected no person on the first frame: {report}")
if int(report.get("second_person_count", 0)) < 1:
    raise SystemExit(f"RMA-111 detected no person on the second frame: {report}")
if report.get("first_face_id") != report.get("second_face_id"):
    raise SystemExit(f"RMA-111 stable face ID mismatch: {report}")
if not str(report.get("first_person_id", "")):
    raise SystemExit(f"RMA-111 first person ID is empty: {report}")
if report.get("first_person_id") != report.get("second_person_id"):
    raise SystemExit(f"RMA-111 stable person ID mismatch: {report}")'''
    text = replace_once(text, old_checks, new_checks, "person result gate")
    path.write_text(text, encoding="utf-8")


def repair_managed_behavior() -> None:
    path = Path(
        "managed/ReachyMini.Camera.Tests/"
        "Rma111LightweightTrackingContracts.cs")
    text = path.read_text(encoding="utf-8")
    old_call = '''            StableIdsSurviveMotionAndProviderIdDrift();
            ExpiryAndOrderingAreDeterministic();'''
    new_call = '''            StableIdsSurviveMotionAndProviderIdDrift();
            PersonIdsSurviveEquivalentFrames();
            ExpiryAndOrderingAreDeterministic();'''
    text = replace_once(text, old_call, new_call, "person behavior call")

    marker = '''        private static void ExpiryAndOrderingAreDeterministic()
'''
    method = '''        private static void PersonIdsSurviveEquivalentFrames()
        {
            var store = new ReachyStableTrackStore();
            IReadOnlyList<TrackedObject> first = store.Update(
                Identity(1UL, 1_000_000_000L),
                new[]
                {
                    Detection(
                        ReachyTrackingDetectionClass.Person,
                        null,
                        0.80,
                        0.10,
                        0.05,
                        0.70,
                        0.90),
                });
            IReadOnlyList<TrackedObject> second = store.Update(
                Identity(2UL, 1_050_000_000L),
                new[]
                {
                    Detection(
                        ReachyTrackingDetectionClass.Person,
                        null,
                        0.82,
                        0.11,
                        0.05,
                        0.70,
                        0.90),
                });

            Equal("person-000001", first[0].LocalId, "deterministic person ID");
            Equal(
                first[0].LocalId,
                second[0].LocalId,
                "stable person ID across equivalent frames");
        }

'''
    text = replace_once(text, marker, method + marker, "person behavior method")
    path.write_text(text, encoding="utf-8")


def replace_source_contract() -> None:
    path = Path(
        "managed/ReachyMini.Camera.Tests/"
        "Rma111AndroidBridgeSourceContracts.cs")
    old = path.read_text(encoding="utf-8")
    required_old = (
        "internal static class Rma111AndroidBridgeSourceContracts",
        "face synchronous-start failure cleanup",
        "person synchronous-start failure drain",
    )
    for expected in required_old:
        if expected not in old:
            raise SystemExit(
                f"Existing source contract lacks expected marker: {expected}")
    if "stable_person_id" in old or "Pin one physical Android device" in old:
        raise SystemExit("Source contract already contains final hardening")

    path.write_text('''#nullable enable

using System;
using System.IO;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma111AndroidBridgeSourceContracts
    {
        private const string BridgePath =
            "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/" +
            "src/main/java/com/ekkus93/weachy/camera/" +
            "ReachyMlKitTrackingBridge.java";
        private const string AcceptancePath =
            "Assets/ReachyMini/Runtime/Application/" +
            "ReachyRma111TrackingAcceptance.cs";
        private const string AcceptanceScriptPath =
            "scripts/run_rma111_lightweight_tracking_acceptance_android.sh";
        private const string WorkflowPath =
            ".github/workflows/local-unity-android-validation.yml";

        internal static void Run()
        {
            string repositoryRoot = FindRepositoryRoot();
            VerifyBridge(Read(repositoryRoot, BridgePath));
            VerifyPhysicalAcceptance(
                Read(repositoryRoot, AcceptancePath),
                Read(repositoryRoot, AcceptanceScriptPath));
            VerifyPinnedDeviceWorkflow(
                repositoryRoot,
                Read(repositoryRoot, WorkflowPath));
        }

        private static void VerifyBridge(string source)
        {
            int inputImage = RequireAfter(
                source,
                "image = InputImage.fromBitmap(bitmap, 0);",
                0,
                "input image creation");
            int faceStart = RequireAfter(
                source,
                "faceTask = faceDetector.process(image);",
                inputImage,
                "face task start");
            int faceListeners = RequireAfter(
                source,
                "faceTask\\n                .addOnSuccessListener",
                faceStart,
                "face listener attachment");
            int personStart = RequireAfter(
                source,
                "personTask = personSegmenter.process(image);",
                faceListeners,
                "person task start");
            _ = RequireAfter(
                source,
                "personTask\\n                .addOnSuccessListener",
                personStart,
                "person listener attachment");

            RequireText(
                source,
                "failImmediately(state, \\"input_image_failed\\", error);",
                "input-image failure cleanup");
            RequireText(
                source,
                "failImmediately(state, \\"face_detection_start_failed\\", error);",
                "face synchronous-start failure cleanup");
            RequireText(
                source,
                "failPart(state, \\"person_segmentation_start_failed\\", error);",
                "person synchronous-start failure drain");
            RequireText(
                source,
                "failPart(state, \\"person_segmentation_result_failed\\", error);",
                "person result failure drain");
            RequireText(
                source,
                "private void failImmediately(",
                "immediate failure cleanup helper");
            RequireText(source, "activeRequest = null;", "active request release");
            RequireText(source, "state.bitmap.recycle();", "bitmap release");
        }

        private static void VerifyPhysicalAcceptance(
            string acceptance,
            string script)
        {
            RequireText(
                acceptance,
                "Bundled ML Kit did not detect a person",
                "physical person detection requirement");
            RequireText(
                acceptance,
                "stable_person_id",
                "stable person report field");
            RequireText(
                acceptance,
                "first_person_id",
                "first person report ID");
            RequireText(
                acceptance,
                "second_person_id",
                "second person report ID");
            RequireText(
                script,
                "\\\"stable_person_id\\\"",
                "stable person shell gate");
            RequireText(
                script,
                "second_person_count",
                "second-frame person shell gate");
            RequireText(
                script,
                "first_person_id",
                "person ID shell gate");
        }

        private static void VerifyPinnedDeviceWorkflow(
            string repositoryRoot,
            string workflow)
        {
            RequireText(
                workflow,
                "- name: Pin one physical Android device",
                "single physical-device pin step");
            RequireText(
                workflow,
                "REACHY_ANDROID_SERIAL=%s",
                "pinned serial environment export");
            RequireText(
                workflow,
                "${#accepted[@]} != 1",
                "ambiguous-device fail-closed gate");

            string[] scripts =
            {
                "scripts/run_rma090_camera_discovery_acceptance_android.sh",
                "scripts/run_rma091_camera_acquisition_acceptance_android.sh",
                "scripts/run_rma092_camera_texture_acceptance_android.sh",
                "scripts/run_rma111_lightweight_tracking_acceptance_android.sh",
                "scripts/run_unity_native_lifecycle_acceptance_android.sh",
                "scripts/run_unity_authoritative_rendering_acceptance_android_impl.sh",
            };
            foreach (string relativePath in scripts)
            {
                RequireText(
                    Read(repositoryRoot, relativePath),
                    "REACHY_ANDROID_SERIAL:-",
                    $"pinned serial consumption in {relativePath}");
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current =
                new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current != null)
            {
                string projectVersion = Path.Combine(
                    current.FullName,
                    "ProjectSettings",
                    "ProjectVersion.txt");
                string bridge = Path.Combine(
                    current.FullName,
                    BridgePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(projectVersion) && File.Exists(bridge))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
            throw new InvalidOperationException(
                "Could not locate the repository root for the RMA-111 source contracts.");
        }

        private static string Read(string root, string relativePath)
        {
            return File.ReadAllText(Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static int RequireAfter(
            string source,
            string expected,
            int startIndex,
            string contract)
        {
            int index = source.IndexOf(
                expected,
                startIndex,
                StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"Managed RMA-111 source contract failed: {contract}.");
            }
            return index;
        }

        private static void RequireText(
            string source,
            string expected,
            string contract)
        {
            if (!source.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed RMA-111 source contract failed: {contract}.");
            }
        }
    }
}
''', encoding="utf-8")


def verify() -> None:
    workflow = Path(
        ".github/workflows/local-unity-android-validation.yml"
    ).read_text(encoding="utf-8")
    if workflow.count("- name: Pin one physical Android device") != 1:
        raise SystemExit("Physical-device pin step is not unique")
    if workflow.count("REACHY_ANDROID_SERIAL=%s") != 1:
        raise SystemExit("Pinned serial export is not unique")

    java = Path(
        "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/src/main/java/"
        "com/ekkus93/weachy/camera/ReachyMlKitTrackingBridge.java"
    ).read_text(encoding="utf-8")
    for expected in (
        'failImmediately(state, "input_image_failed", error);',
        'failPart(state, "person_segmentation_result_failed", error);',
    ):
        if java.count(expected) != 1:
            raise SystemExit(f"Java hardening marker is not unique: {expected}")

    acceptance = Path(
        "Assets/ReachyMini/Runtime/Application/"
        "ReachyRma111TrackingAcceptance.cs"
    ).read_text(encoding="utf-8")
    for expected in (
        "first_person_id",
        "second_person_id",
        "stable_person_id",
        "second_person_count",
    ):
        if expected not in acceptance:
            raise SystemExit(f"Acceptance report marker is missing: {expected}")

    shell = Path(
        "scripts/run_rma111_lightweight_tracking_acceptance_android.sh"
    ).read_text(encoding="utf-8")
    if '"stable_person_id"' not in shell:
        raise SystemExit("Physical shell gate lacks stable_person_id")
    if "second_person_count" not in shell:
        raise SystemExit("Physical shell gate lacks second person count")

    managed = Path(
        "managed/ReachyMini.Camera.Tests/"
        "Rma111LightweightTrackingContracts.cs"
    ).read_text(encoding="utf-8")
    if managed.count("PersonIdsSurviveEquivalentFrames();") != 1:
        raise SystemExit("Managed person stability test is not wired exactly once")


def remove_one_use_files() -> None:
    for path in (
        Path(".github/rma111_final_hardening.py"),
        Path(".github/workflows/rma111-final-hardening.yml"),
    ):
        if not path.is_file():
            raise SystemExit(f"One-use file is missing: {path}")
        path.unlink()


def main() -> None:
    repair_workflow()
    repair_java_bridge()
    repair_acceptance()
    repair_acceptance_script()
    repair_managed_behavior()
    replace_source_contract()
    verify()
    remove_one_use_files()


if __name__ == "__main__":
    main()
