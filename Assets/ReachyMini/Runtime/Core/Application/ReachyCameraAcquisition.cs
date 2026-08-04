#nullable enable

using System;

namespace ReachyMini.AppState
{
    public enum ReachyCameraAcquisitionState
    {
        Stopped = 0,
        Starting = 1,
        Running = 2,
        Paused = 3,
        Stopping = 4,
        PermissionRevoked = 5,
        Unavailable = 6,
        Faulted = 7,
    }

    public enum ReachyCameraPixelFormat
    {
        Unknown = 0,
        Yuv420888 = 1,
    }

    public enum ReachyCameraFrameIntrinsicsSource
    {
        AndroidCalibration = 0,
        UncalibratedPinholeEstimate = 1,
    }

    public readonly struct ReachyCameraFrameCrop :
        IEquatable<ReachyCameraFrameCrop>
    {
        public ReachyCameraFrameCrop(
            int left,
            int top,
            int right,
            int bottom)
        {
            if (left < 0 || top < 0 || right <= left || bottom <= top)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(left),
                    "A frame crop must have nonnegative origin and positive extent.");
            }

            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; }

        public int Top { get; }

        public int Right { get; }

        public int Bottom { get; }

        public int Width => checked(Right - Left);

        public int Height => checked(Bottom - Top);

        public bool Equals(ReachyCameraFrameCrop other)
        {
            return Left == other.Left &&
                Top == other.Top &&
                Right == other.Right &&
                Bottom == other.Bottom;
        }

        public override bool Equals(object? obj)
        {
            return obj is ReachyCameraFrameCrop other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Left, Top, Right, Bottom);
        }

        public static bool operator ==(
            ReachyCameraFrameCrop left,
            ReachyCameraFrameCrop right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            ReachyCameraFrameCrop left,
            ReachyCameraFrameCrop right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"[{Left},{Top}..{Right},{Bottom}]";
        }
    }

    public sealed class ReachyCameraFrameIntrinsics
    {
        public ReachyCameraFrameIntrinsics(
            ReachyCameraFrameIntrinsicsSource source,
            float focalLengthX,
            float focalLengthY,
            float principalPointX,
            float principalPointY,
            float skew,
            string provenance)
        {
            if (string.IsNullOrWhiteSpace(provenance))
            {
                throw new ArgumentException(
                    "Frame intrinsics require provenance.",
                    nameof(provenance));
            }
            RequireFinitePositive(focalLengthX, nameof(focalLengthX));
            RequireFinitePositive(focalLengthY, nameof(focalLengthY));
            RequireFinite(principalPointX, nameof(principalPointX));
            RequireFinite(principalPointY, nameof(principalPointY));
            RequireFinite(skew, nameof(skew));

            Source = source;
            FocalLengthX = focalLengthX;
            FocalLengthY = focalLengthY;
            PrincipalPointX = principalPointX;
            PrincipalPointY = principalPointY;
            Skew = skew;
            Provenance = provenance;
        }

        public ReachyCameraFrameIntrinsicsSource Source { get; }

        public bool IsCalibrated =>
            Source == ReachyCameraFrameIntrinsicsSource.AndroidCalibration;

        public float FocalLengthX { get; }

        public float FocalLengthY { get; }

        public float PrincipalPointX { get; }

        public float PrincipalPointY { get; }

        public float Skew { get; }

        public string Provenance { get; }

        private static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Frame intrinsic values must be finite.");
            }
        }

        private static void RequireFinitePositive(float value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Frame focal lengths must be positive.");
            }
        }
    }

    public sealed class ReachyCameraFrameMetadata
    {
        public ReachyCameraFrameMetadata(
            ulong sessionId,
            ulong sequence,
            long timestampNanoseconds,
            string cameraId,
            ReachyDeviceCameraFacing lensFacing,
            int sensorOrientationDegrees,
            int rotationDegrees,
            int width,
            int height,
            ReachyCameraFrameCrop crop,
            ReachyCameraPixelFormat pixelFormat,
            ReachyCameraFrameIntrinsics intrinsics)
        {
            if (sessionId == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sessionId),
                    sessionId,
                    "A camera frame requires a nonzero acquisition session identifier.");
            }
            if (sequence == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence),
                    sequence,
                    "A camera frame sequence starts at one.");
            }
            if (timestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestampNanoseconds),
                    timestampNanoseconds,
                    "A camera frame timestamp must be positive.");
            }
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                throw new ArgumentException(
                    "A camera frame requires a stable camera identifier.",
                    nameof(cameraId));
            }
            if (lensFacing != ReachyDeviceCameraFacing.Front &&
                lensFacing != ReachyDeviceCameraFacing.Rear &&
                lensFacing != ReachyDeviceCameraFacing.External)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lensFacing),
                    lensFacing,
                    "A frame must identify a front, rear, or external lens.");
            }
            ValidateRightAngle(
                sensorOrientationDegrees,
                nameof(sensorOrientationDegrees));
            ValidateRightAngle(rotationDegrees, nameof(rotationDegrees));
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "Frame dimensions must be positive.");
            }
            if (crop.Right > width || crop.Bottom > height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(crop),
                    crop,
                    "The frame crop must remain inside the image buffer.");
            }
            if (pixelFormat == ReachyCameraPixelFormat.Unknown)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pixelFormat),
                    pixelFormat,
                    "A camera frame must expose its pixel format.");
            }

            SessionId = sessionId;
            Sequence = sequence;
            TimestampNanoseconds = timestampNanoseconds;
            CameraId = cameraId;
            LensFacing = lensFacing;
            SensorOrientationDegrees = sensorOrientationDegrees;
            RotationDegrees = rotationDegrees;
            Width = width;
            Height = height;
            Crop = crop;
            PixelFormat = pixelFormat;
            Intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
        }

        public ulong SessionId { get; }

        public ulong Sequence { get; }

        public long TimestampNanoseconds { get; }

        public string CameraId { get; }

        public ReachyDeviceCameraFacing LensFacing { get; }

        public int SensorOrientationDegrees { get; }

        public int RotationDegrees { get; }

        public int Width { get; }

        public int Height { get; }

        public ReachyCameraFrameCrop Crop { get; }

        public ReachyCameraPixelFormat PixelFormat { get; }

        public ReachyCameraFrameIntrinsics Intrinsics { get; }

        public string Summary =>
            $"session={SessionId}; sequence={Sequence}; timestamp_ns={TimestampNanoseconds}; " +
            $"camera={CameraId}; facing={LensFacing}; sensor_orientation={SensorOrientationDegrees}; " +
            $"rotation={RotationDegrees}; size={Width}x{Height}; crop={Crop}; " +
            $"format={PixelFormat}; intrinsics={Intrinsics.Source}";

        private static void ValidateRightAngle(int value, string name)
        {
            if (value != 0 && value != 90 && value != 180 && value != 270)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Camera orientation must be 0, 90, 180, or 270 degrees.");
            }
        }
    }

    public sealed class ReachyCameraAcquisitionSnapshot
    {
        public ReachyCameraAcquisitionSnapshot(
            ReachyCameraAcquisitionState state,
            ulong sessionId,
            ReachyDeviceCameraFacing requestedFacing,
            string cameraId,
            string message,
            ulong acceptedFrameCount,
            ulong staleFrameCount,
            ReachyCameraFrameMetadata? latestFrame,
            ulong revision)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Camera acquisition state requires diagnostics.",
                    nameof(message));
            }
            bool activeState =
                state == ReachyCameraAcquisitionState.Starting ||
                state == ReachyCameraAcquisitionState.Running ||
                state == ReachyCameraAcquisitionState.Paused ||
                state == ReachyCameraAcquisitionState.Stopping;
            if (activeState)
            {
                if (sessionId == 0UL)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(sessionId),
                        sessionId,
                        "An active acquisition state requires a session identifier.");
                }
                if (string.IsNullOrWhiteSpace(cameraId))
                {
                    throw new ArgumentException(
                        "An active acquisition state requires a selected camera.",
                        nameof(cameraId));
                }
            }
            else if (sessionId != 0UL || !string.IsNullOrEmpty(cameraId))
            {
                throw new ArgumentException(
                    "Inactive acquisition states cannot retain an active camera session.");
            }
            if (latestFrame != null)
            {
                if (!activeState ||
                    latestFrame.SessionId != sessionId ||
                    !string.Equals(
                        latestFrame.CameraId,
                        cameraId,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "The latest frame must belong to the active acquisition session.",
                        nameof(latestFrame));
                }
            }

            State = state;
            SessionId = sessionId;
            RequestedFacing = requestedFacing;
            CameraId = cameraId;
            Message = message;
            AcceptedFrameCount = acceptedFrameCount;
            StaleFrameCount = staleFrameCount;
            LatestFrame = latestFrame;
            Revision = revision;
        }

        public ReachyCameraAcquisitionState State { get; }

        public ulong SessionId { get; }

        public ReachyDeviceCameraFacing RequestedFacing { get; }

        public string CameraId { get; }

        public string Message { get; }

        public ulong AcceptedFrameCount { get; }

        public ulong StaleFrameCount { get; }

        public ReachyCameraFrameMetadata? LatestFrame { get; }

        public ulong Revision { get; }

        public bool IsActive =>
            State == ReachyCameraAcquisitionState.Starting ||
            State == ReachyCameraAcquisitionState.Running ||
            State == ReachyCameraAcquisitionState.Paused ||
            State == ReachyCameraAcquisitionState.Stopping;

        public bool HasFrame => LatestFrame != null;

        public string Summary =>
            $"state={State}; session={SessionId}; camera={CameraId}; facing={RequestedFacing}; " +
            $"accepted={AcceptedFrameCount}; stale={StaleFrameCount}; " +
            $"latest_sequence={(LatestFrame == null ? 0UL : LatestFrame.Sequence)}; " +
            $"detail={Message}";
    }

    public sealed class ReachyCameraAcquisitionChangedEventArgs : EventArgs
    {
        public ReachyCameraAcquisitionChangedEventArgs(
            ReachyCameraAcquisitionSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyCameraAcquisitionSnapshot Snapshot { get; }
    }

    public sealed class ReachyCameraAcquisitionStateStore
    {
        private ReachyCameraAcquisitionSnapshot current =
            new ReachyCameraAcquisitionSnapshot(
                ReachyCameraAcquisitionState.Stopped,
                0UL,
                ReachyDeviceCameraFacing.Unknown,
                string.Empty,
                "Camera frame acquisition is stopped.",
                0UL,
                0UL,
                null,
                0UL);

        public ReachyCameraAcquisitionSnapshot Current => current;

        public event EventHandler<ReachyCameraAcquisitionChangedEventArgs>? Changed;

        public void BeginStart(
            ulong sessionId,
            ReachyDeviceCameraFacing requestedFacing,
            string cameraId,
            string message)
        {
            if (requestedFacing != ReachyDeviceCameraFacing.Front &&
                requestedFacing != ReachyDeviceCameraFacing.Rear &&
                requestedFacing != ReachyDeviceCameraFacing.External)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedFacing),
                    requestedFacing,
                    "Camera acquisition requires an explicit lens facing.");
            }
            Publish(
                ReachyCameraAcquisitionState.Starting,
                sessionId,
                requestedFacing,
                cameraId,
                message,
                0UL,
                0UL,
                null);
        }

        public void MarkRunning(string message)
        {
            RequireState(
                ReachyCameraAcquisitionState.Starting,
                ReachyCameraAcquisitionState.Paused);
            PublishActive(
                ReachyCameraAcquisitionState.Running,
                message,
                current.LatestFrame);
        }

        public void MarkPaused(string message)
        {
            RequireState(
                ReachyCameraAcquisitionState.Starting,
                ReachyCameraAcquisitionState.Running);
            PublishActive(
                ReachyCameraAcquisitionState.Paused,
                message,
                current.LatestFrame);
        }

        public void BeginStop(string message)
        {
            if (!current.IsActive)
            {
                throw new InvalidOperationException(
                    $"Camera acquisition cannot stop from {current.State}.");
            }
            PublishActive(
                ReachyCameraAcquisitionState.Stopping,
                message,
                current.LatestFrame);
        }

        public void MarkStopped(string message)
        {
            PublishInactive(ReachyCameraAcquisitionState.Stopped, message);
        }

        public void MarkPermissionRevoked(string message)
        {
            PublishInactive(
                ReachyCameraAcquisitionState.PermissionRevoked,
                message);
        }

        public void MarkUnavailable(string message)
        {
            PublishInactive(ReachyCameraAcquisitionState.Unavailable, message);
        }

        public void MarkFaulted(string message)
        {
            PublishInactive(ReachyCameraAcquisitionState.Faulted, message);
        }

        public bool PublishFrame(ReachyCameraFrameMetadata frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }
            if (current.State != ReachyCameraAcquisitionState.Running)
            {
                throw new InvalidOperationException(
                    $"Camera frames cannot be published while acquisition is {current.State}.");
            }
            if (frame.SessionId != current.SessionId ||
                !string.Equals(
                    frame.CameraId,
                    current.CameraId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The camera frame belongs to a different acquisition session.");
            }

            ReachyCameraFrameMetadata? previous = current.LatestFrame;
            if (previous != null)
            {
                if (frame.Sequence == previous.Sequence)
                {
                    return false;
                }
                if (frame.Sequence < previous.Sequence ||
                    frame.TimestampNanoseconds <= previous.TimestampNanoseconds)
                {
                    Publish(
                        current.State,
                        current.SessionId,
                        current.RequestedFacing,
                        current.CameraId,
                        "Rejected stale or non-monotonic camera frame metadata.",
                        current.AcceptedFrameCount,
                        checked(current.StaleFrameCount + 1UL),
                        previous);
                    return false;
                }
            }

            Publish(
                current.State,
                current.SessionId,
                current.RequestedFacing,
                current.CameraId,
                $"Camera frame {frame.Sequence} acquired without a CPU pixel copy.",
                checked(current.AcceptedFrameCount + 1UL),
                current.StaleFrameCount,
                frame);
            return true;
        }

        private void PublishActive(
            ReachyCameraAcquisitionState state,
            string message,
            ReachyCameraFrameMetadata? latestFrame)
        {
            Publish(
                state,
                current.SessionId,
                current.RequestedFacing,
                current.CameraId,
                message,
                current.AcceptedFrameCount,
                current.StaleFrameCount,
                latestFrame);
        }

        private void PublishInactive(
            ReachyCameraAcquisitionState state,
            string message)
        {
            Publish(
                state,
                0UL,
                ReachyDeviceCameraFacing.Unknown,
                string.Empty,
                message,
                current.AcceptedFrameCount,
                current.StaleFrameCount,
                null);
        }

        private void Publish(
            ReachyCameraAcquisitionState state,
            ulong sessionId,
            ReachyDeviceCameraFacing requestedFacing,
            string cameraId,
            string message,
            ulong acceptedFrameCount,
            ulong staleFrameCount,
            ReachyCameraFrameMetadata? latestFrame)
        {
            ReachyCameraAcquisitionSnapshot next =
                new ReachyCameraAcquisitionSnapshot(
                    state,
                    sessionId,
                    requestedFacing,
                    cameraId,
                    message,
                    acceptedFrameCount,
                    staleFrameCount,
                    latestFrame,
                    checked(current.Revision + 1UL));
            current = next;
            Changed?.Invoke(
                this,
                new ReachyCameraAcquisitionChangedEventArgs(next));
        }

        private void RequireState(
            ReachyCameraAcquisitionState first,
            ReachyCameraAcquisitionState second)
        {
            if (current.State != first && current.State != second)
            {
                throw new InvalidOperationException(
                    $"Camera acquisition state {current.State} is invalid for this transition.");
            }
        }
    }
}
