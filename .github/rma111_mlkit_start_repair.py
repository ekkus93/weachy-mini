#!/usr/bin/env python3

from pathlib import Path


def replace_once(text: str, old: str, new: str, contract: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(
            f"{contract}: expected exactly one source pattern, found {count}")
    return text.replace(old, new, 1)


def repair_unity_test() -> None:
    path = Path(
        "Assets/ReachyMini/Tests/Editor/ReachyLightweightTrackingTests.cs")
    text = path.read_text(encoding="utf-8")
    block_start = text.index('"RMA-111 source validity"')
    block_end = text.index("});", block_start)
    block = text[block_start:block_end]
    count = block.count("Color.white,")
    if count != 4:
        raise SystemExit(
            f"Unity validity pixels: expected four Color.white values, found {count}")
    fixed = block.replace(
        "Color.white,",
        "new Color32(255, 255, 255, 255),")
    path.write_text(
        text[:block_start] + fixed + text[block_end:],
        encoding="utf-8")


def repair_android_bridge() -> None:
    path = Path(
        "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/src/main/java/"
        "com/ekkus93/weachy/camera/ReachyMlKitTrackingBridge.java")
    text = path.read_text(encoding="utf-8")
    old_start = '''        InputImage image = InputImage.fromBitmap(bitmap, 0);
        Task<List<Face>> faceTask = faceDetector.process(image);
        Task<SegmentationMask> personTask = personSegmenter.process(image);
        faceTask
                .addOnSuccessListener(faces -> completeFaces(state, faces))
                .addOnFailureListener(error -> failPart(state, "face_detection_failed", error));
        personTask
                .addOnSuccessListener(mask -> completePerson(state, mask))
                .addOnFailureListener(error -> failPart(state, "person_segmentation_failed", error));'''
    new_start = '''        InputImage image = InputImage.fromBitmap(bitmap, 0);
        Task<List<Face>> faceTask;
        try {
            faceTask = faceDetector.process(image);
        } catch (Exception error) {
            failImmediately(state, "face_detection_start_failed", error);
            return;
        }
        faceTask
                .addOnSuccessListener(faces -> completeFaces(state, faces))
                .addOnFailureListener(error -> failPart(state, "face_detection_failed", error));

        Task<SegmentationMask> personTask;
        try {
            personTask = personSegmenter.process(image);
        } catch (Exception error) {
            failPart(state, "person_segmentation_start_failed", error);
            return;
        }
        personTask
                .addOnSuccessListener(mask -> completePerson(state, mask))
                .addOnFailureListener(error -> failPart(state, "person_segmentation_failed", error));'''
    text = replace_once(
        text,
        old_start,
        new_start,
        "ML Kit task start block")

    marker = '''    private void failPart(RequestState state, String code, Exception error) {
        synchronized (lock) {'''
    helper = '''    private void failImmediately(
            RequestState state,
            String code,
            Exception error) {
        synchronized (lock) {
            if (!isCurrent(state)) {
                return;
            }
            activeRequest = null;
            try {
                if (!state.cancelled && !closed) {
                    state.callback.onFailure(
                            state.requestId,
                            code,
                            safeMessage(error));
                }
            } finally {
                state.bitmap.recycle();
            }
        }
    }

    private void failPart(RequestState state, String code, Exception error) {
        synchronized (lock) {'''
    text = replace_once(
        text,
        marker,
        helper,
        "ML Kit immediate-failure insertion point")
    path.write_text(text, encoding="utf-8")


def create_source_contract() -> None:
    path = Path(
        "managed/ReachyMini.Camera.Tests/"
        "Rma111AndroidBridgeSourceContracts.cs")
    if path.exists():
        raise SystemExit(f"Permanent source contract already exists: {path}")
    path.write_text('''#nullable enable

using System;
using System.IO;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma111AndroidBridgeSourceContracts
    {
        private const string RelativeBridgePath =
            "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/" +
            "src/main/java/com/ekkus93/weachy/camera/" +
            "ReachyMlKitTrackingBridge.java";

        internal static void Run()
        {
            string repositoryRoot = FindRepositoryRoot();
            string sourcePath = Path.Combine(
                repositoryRoot,
                RelativeBridgePath.Replace('/', Path.DirectorySeparatorChar));
            string source = File.ReadAllText(sourcePath);

            int faceStart = RequireAfter(
                source,
                "faceTask = faceDetector.process(image);",
                0,
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
                "failImmediately(state, \\"face_detection_start_failed\\", error);",
                "face synchronous-start failure cleanup");
            RequireText(
                source,
                "failPart(state, \\"person_segmentation_start_failed\\", error);",
                "person synchronous-start failure drain");
            RequireText(
                source,
                "private void failImmediately(",
                "immediate failure cleanup helper");
            RequireText(
                source,
                "activeRequest = null;",
                "active request release");
            RequireText(
                source,
                "state.bitmap.recycle();",
                "bitmap release");
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
                    RelativeBridgePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
                if (File.Exists(projectVersion) && File.Exists(bridge))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
            throw new InvalidOperationException(
                "Could not locate the repository root for the RMA-111 Android bridge source contract.");
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


def wire_source_contract() -> None:
    path = Path(
        "managed/ReachyMini.Camera.Tests/"
        "Rma111LightweightTrackingContracts.cs")
    text = path.read_text(encoding="utf-8")
    old = '''            TrackingPixelsRequireExactColorAndValidityLengths();
            StableIdsSurviveMotionAndProviderIdDrift();'''
    new = '''            TrackingPixelsRequireExactColorAndValidityLengths();
            Rma111AndroidBridgeSourceContracts.Run();
            StableIdsSurviveMotionAndProviderIdDrift();'''
    path.write_text(
        replace_once(text, old, new, "RMA-111 contract call site"),
        encoding="utf-8")


def verify() -> None:
    test = Path(
        "Assets/ReachyMini/Tests/Editor/ReachyLightweightTrackingTests.cs"
    ).read_text(encoding="utf-8")
    start = test.index('"RMA-111 source validity"')
    end = test.index("});", start)
    block = test[start:end]
    if block.count("new Color32(255, 255, 255, 255)") != 4:
        raise SystemExit("Validity test lacks four Color32 pixels")
    if "Color.white" in block:
        raise SystemExit("Color[] inference remains in validity test")

    java = Path(
        "Assets/Plugins/Android/ReachyCameraDiscovery.androidlib/src/main/java/"
        "com/ekkus93/weachy/camera/ReachyMlKitTrackingBridge.java"
    ).read_text(encoding="utf-8")
    required_once = (
        'failImmediately(state, "face_detection_start_failed", error);',
        'failPart(state, "person_segmentation_start_failed", error);',
        'private void failImmediately(',
    )
    for expected in required_once:
        count = java.count(expected)
        if count != 1:
            raise SystemExit(
                f"Expected one {expected!r} in bridge, found {count}")
    face_start = java.index("faceTask = faceDetector.process(image);")
    face_listeners = java.index(
        "faceTask\n                .addOnSuccessListener",
        face_start)
    person_start = java.index(
        "personTask = personSegmenter.process(image);",
        face_listeners)
    person_listeners = java.index(
        "personTask\n                .addOnSuccessListener",
        person_start)
    if not face_start < face_listeners < person_start < person_listeners:
        raise SystemExit("ML Kit task/listener ordering contract is broken")

    if not Path(
        "managed/ReachyMini.Camera.Tests/"
        "Rma111AndroidBridgeSourceContracts.cs").is_file():
        raise SystemExit("Permanent source contract is missing")


def remove_one_use_files() -> None:
    for path in (
        Path(".github/workflows/rma111-mlkit-start-repair.yml"),
        Path(".github/rma111_mlkit_start_repair.py"),
    ):
        if not path.is_file():
            raise SystemExit(f"One-use file is missing: {path}")
        path.unlink()


def main() -> None:
    repair_unity_test()
    repair_android_bridge()
    create_source_contract()
    wire_source_contract()
    verify()
    remove_one_use_files()


if __name__ == "__main__":
    main()
