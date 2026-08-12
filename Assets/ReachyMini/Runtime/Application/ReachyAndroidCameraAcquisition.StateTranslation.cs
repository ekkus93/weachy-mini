#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyAndroidCameraAcquisition
    {
        private void ApplyPlatformSnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                state.MarkFaulted(
                    "The Android CameraX bridge returned no state JSON.");
                desiredActive = false;
                return;
            }

            ReachyCameraAcquisitionEnvelope? envelope;
            try
            {
                envelope = JsonUtility.FromJson<ReachyCameraAcquisitionEnvelope>(json);
            }
            catch (Exception exception)
            {
                state.MarkFaulted(
                    $"camera_acquisition_json_failed: {exception.Message}");
                desiredActive = false;
                return;
            }
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.state))
            {
                state.MarkFaulted(
                    "The Android CameraX bridge returned an invalid state object.");
                desiredActive = false;
                return;
            }

            string detail = string.IsNullOrWhiteSpace(envelope.message)
                ? "The Android CameraX bridge changed state without diagnostics."
                : envelope.message;
            switch (envelope.state)
            {
                case "Starting":
                    break;
                case "Running":
                    if (state.Current.State == ReachyCameraAcquisitionState.Starting ||
                        state.Current.State == ReachyCameraAcquisitionState.Paused)
                    {
                        state.MarkRunning(detail);
                    }
                    PublishFrame(envelope.latestFrame);
                    break;
                case "Paused":
                    if (state.Current.State == ReachyCameraAcquisitionState.Starting ||
                        state.Current.State == ReachyCameraAcquisitionState.Running)
                    {
                        state.MarkPaused(detail);
                    }
                    break;
                case "Stopping":
                    if (state.Current.IsActive &&
                        state.Current.State != ReachyCameraAcquisitionState.Stopping)
                    {
                        state.BeginStop(detail);
                    }
                    break;
                case "Stopped":
                    // restartAfterClose captures the pending-switch intent before
                    // MarkStopped clears the active session, then triggers the
                    // re-entrant StartPreferred call (anchor file) only after the
                    // state store has observed the CameraX CLOSED snapshot.
                    bool restartAfterClose =
                        desiredActive && pendingStartAfterStop;
                    state.MarkStopped(detail);
                    if (restartAfterClose)
                    {
                        pendingStartAfterStop = false;
                        StartPreferred(preferredFacing);
                    }
                    break;
                case "PermissionRevoked":
                    desiredActive = false;
                    state.MarkPermissionRevoked(detail);
                    break;
                case "Unavailable":
                    desiredActive = false;
                    state.MarkUnavailable(
                        PrefixError(envelope.errorCode, detail));
                    break;
                case "Faulted":
                    desiredActive = false;
                    state.MarkFaulted(
                        PrefixError(envelope.errorCode, detail));
                    break;
                default:
                    desiredActive = false;
                    state.MarkFaulted(
                        $"camera_acquisition_state_unknown: {envelope.state}");
                    break;
            }
        }

        private void PublishFrame(ReachyCameraFrameDto? dto)
        {
            if (dto == null || dto.sequence <= 0L)
            {
                return;
            }
            if (dto.crop == null || dto.intrinsics == null)
            {
                state.MarkFaulted(
                    "CameraX frame metadata omitted crop or intrinsics.");
                desiredActive = false;
                RequirePlatform().Stop();
                return;
            }

            ReachyCameraFrameMetadata frame;
            try
            {
                frame = new ReachyCameraFrameMetadata(
                    checked((ulong)dto.sessionId),
                    checked((ulong)dto.sequence),
                    dto.timestampNanoseconds,
                    dto.cameraId,
                    ParseFacing(dto.facing),
                    dto.sensorOrientationDegrees,
                    dto.rotationDegrees,
                    dto.width,
                    dto.height,
                    new ReachyCameraFrameCrop(
                        dto.crop.left,
                        dto.crop.top,
                        dto.crop.right,
                        dto.crop.bottom),
                    ParsePixelFormat(dto.pixelFormat),
                    new ReachyCameraFrameIntrinsics(
                        ParseIntrinsicsSource(dto.intrinsics.source),
                        dto.intrinsics.fx,
                        dto.intrinsics.fy,
                        dto.intrinsics.cx,
                        dto.intrinsics.cy,
                        dto.intrinsics.skew,
                        dto.intrinsics.provenance));
                state.PublishFrame(frame);
            }
            catch (Exception exception)
            {
                desiredActive = false;
                RequirePlatform().Stop();
                state.MarkFaulted(
                    $"camera_frame_contract_failed: {exception.Message}");
            }
        }
    }
}
