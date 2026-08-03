#!/usr/bin/env python3
"""Run the RMA-090 integration patch with scoped anchor handling."""

from __future__ import annotations

from pathlib import Path

import apply_rma090_integration as integration


original_replace_once = integration.replace_once


def preserve_csharp_newline_literals(value: str) -> str:
    return (
        value.replace('"CAMERA\nFIXED VIEW"', '"CAMERA\\nFIXED VIEW"')
        .replace('"CAMERA\nACCESS"', '"CAMERA\\nACCESS"')
        .replace(
            'camera.Message + "\n" + current.ReprojectionStatus',
            'camera.Message + "\\n" + current.ReprojectionStatus',
        )
    )


def scoped_replace_once(path: str, old: str, new: str) -> None:
    old = preserve_csharp_newline_literals(old)
    new = preserve_csharp_newline_literals(new)
    if (
        path.endswith("ReachySettingsApplicationCompositionProvider.cs")
        and old.startswith("        private void OnSettingsChanged(")
    ):
        target = Path(path)
        text = target.read_text(encoding="utf-8")
        if new in text:
            return
        index = text.rfind(old)
        if index < 0:
            raise RuntimeError(
                "The main-screen settings event anchor was not found."
            )
        updated = text[:index] + new + text[index + len(old):]
        target.write_text(updated, encoding="utf-8")
        return
    original_replace_once(path, old, new)


def add_resolution_operators() -> None:
    path = Path(
        "Assets/ReachyMini/Runtime/Core/Application/ReachyCameraCapabilities.cs"
    )
    text = path.read_text(encoding="utf-8")
    operators = """        public static bool operator ==(
            ReachyCameraResolution left,
            ReachyCameraResolution right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            ReachyCameraResolution left,
            ReachyCameraResolution right)
        {
            return !left.Equals(right);
        }

"""
    if operators in text:
        return
    anchor = """        public override int GetHashCode()
        {
            return HashCode.Combine(Width, Height);
        }

"""
    if text.count(anchor) != 1:
        raise RuntimeError("Resolution equality operator anchor is not unique.")
    path.write_text(
        text.replace(anchor, anchor + operators, 1),
        encoding="utf-8",
    )


def preserve_camera_selection_actionability() -> None:
    path = Path(
        "Assets/ReachyMini/Runtime/Application/ReachyMainScreen.cs"
    )
    text = path.read_text(encoding="utf-8")
    old = """            if (camera.Permission == ReachyCameraPermissionState.Unsupported ||
                camera.Permission == ReachyCameraPermissionState.Faulted)
            {
                store.ReportUnavailableAction(
                    "Camera discovery",
                    camera.Message);
                return;
            }

            (requestCameraAccess ?? throw new InvalidOperationException(
                "The camera access operation is not bound."))();
            camera = RequireCameraCapabilities();
            store.SetInteraction(
                camera.Permission == ReachyCameraPermissionState.Faulted
                    ? ReachyInteractionState.Error
                    : ReachyInteractionState.Unavailable,
                camera.Message);
"""
    new = """            if (camera.Permission == ReachyCameraPermissionState.Unsupported ||
                camera.Permission == ReachyCameraPermissionState.Faulted)
            {
                store.ReportUnavailableAction(
                    "Camera selection",
                    camera.Message);
                return;
            }

            (requestCameraAccess ?? throw new InvalidOperationException(
                "The camera access operation is not bound."))();
            camera = RequireCameraCapabilities();
            if (camera.Permission == ReachyCameraPermissionState.Unsupported ||
                camera.Permission == ReachyCameraPermissionState.Faulted)
            {
                store.ReportUnavailableAction(
                    "Camera selection",
                    camera.Message);
                return;
            }
            store.SetInteraction(
                ReachyInteractionState.Unavailable,
                camera.Message);
"""
    if new in text:
        return
    if text.count(old) != 1:
        raise RuntimeError(
            "The camera selection diagnostic anchor is not unique."
        )
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


integration.replace_once = scoped_replace_once
integration.main()
add_resolution_operators()
preserve_camera_selection_actionability()
