#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    internal sealed class ReachyAndroidJavaCameraTextureFrameLease :
        IReachyCameraTextureFrameLease
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject? lease;
        private AndroidJavaObject? yBuffer;
        private AndroidJavaObject? uBuffer;
        private AndroidJavaObject? vBuffer;
        private bool disposed;
#endif

        public ReachyAndroidJavaCameraTextureFrameLease(
            AndroidJavaObject javaLease)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            lease = javaLease ?? throw new ArgumentNullException(nameof(javaLease));
            try
            {
                long sessionId = lease.Call<long>("getSessionId");
                long sequence = lease.Call<long>("getSequence");
                long timestamp = lease.Call<long>("getTimestampNanoseconds");
                string cameraId =
                    lease.Call<string>("getCameraId") ?? string.Empty;
                string facing =
                    lease.Call<string>("getFacing") ?? string.Empty;
                int sensorOrientation =
                    lease.Call<int>("getSensorOrientationDegrees");
                int rotation = lease.Call<int>("getRotationDegrees");
                int width = lease.Call<int>("getWidth");
                int height = lease.Call<int>("getHeight");
                int chromaWidth = lease.Call<int>("getChromaWidth");
                int chromaHeight = lease.Call<int>("getChromaHeight");
                int cropLeft = lease.Call<int>("getCropLeft");
                int cropTop = lease.Call<int>("getCropTop");
                int cropRight = lease.Call<int>("getCropRight");
                int cropBottom = lease.Call<int>("getCropBottom");
                bool mirrored = lease.Call<bool>("isMirrored");
                string colorStandard =
                    lease.Call<string>("getColorStandard") ?? string.Empty;
                string colorRange =
                    lease.Call<string>("getColorRange") ?? string.Empty;

                Descriptor = new ReachyCameraTextureFrameDescriptor(
                    checked((ulong)sessionId),
                    checked((ulong)sequence),
                    timestamp,
                    cameraId,
                    ParseFacing(facing),
                    sensorOrientation,
                    rotation,
                    width,
                    height,
                    chromaWidth,
                    chromaHeight,
                    new ReachyCameraFrameCrop(
                        cropLeft,
                        cropTop,
                        cropRight,
                        cropBottom),
                    mirrored,
                    ParseColorStandard(colorStandard),
                    ParseColorRange(colorRange));

                YLength = lease.Call<int>("getYLength");
                ULength = lease.Call<int>("getULength");
                VLength = lease.Call<int>("getVLength");
                yBuffer = lease.Call<AndroidJavaObject>("getYBuffer");
                uBuffer = lease.Call<AndroidJavaObject>("getUBuffer");
                vBuffer = lease.Call<AndroidJavaObject>("getVBuffer");
                YBuffer = GetDirectAddress(yBuffer, "Y");
                UBuffer = GetDirectAddress(uBuffer, "U");
                VBuffer = GetDirectAddress(vBuffer, "V");
            }
            catch
            {
                Dispose();
                throw;
            }
#else
            _ = javaLease;
            throw new PlatformNotSupportedException(
                "Android direct camera texture leases require an Android player.");
#endif
        }

        public ReachyCameraTextureFrameDescriptor Descriptor { get; } = null!;

        public IntPtr YBuffer { get; }

        public int YLength { get; }

        public IntPtr UBuffer { get; }

        public int ULength { get; }

        public IntPtr VBuffer { get; }

        public int VLength { get; }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (disposed)
            {
                return;
            }
            disposed = true;
            try
            {
                lease?.Call("close");
            }
            finally
            {
                yBuffer?.Dispose();
                uBuffer?.Dispose();
                vBuffer?.Dispose();
                lease?.Dispose();
                yBuffer = null;
                uBuffer = null;
                vBuffer = null;
                lease = null;
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static unsafe IntPtr GetDirectAddress(
            AndroidJavaObject? buffer,
            string planeName)
        {
            if (buffer == null)
            {
                throw new InvalidOperationException(
                    $"The Android texture lease returned no {planeName} direct buffer.");
            }
            IntPtr address = (IntPtr)AndroidJNI.GetDirectBufferAddress(
                buffer.GetRawObject());
            if (address == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"The Android {planeName} plane is not backed by a direct buffer.");
            }
            return address;
        }

        private static ReachyDeviceCameraFacing ParseFacing(string value)
        {
            return value switch
            {
                "front" => ReachyDeviceCameraFacing.Front,
                "rear" => ReachyDeviceCameraFacing.Rear,
                "external" => ReachyDeviceCameraFacing.External,
                _ => ReachyDeviceCameraFacing.Unknown,
            };
        }

        private static ReachyCameraYuvColorStandard ParseColorStandard(
            string value)
        {
            return value switch
            {
                "bt601" => ReachyCameraYuvColorStandard.Bt601,
                "bt709" => ReachyCameraYuvColorStandard.Bt709,
                _ => ReachyCameraYuvColorStandard.Unknown,
            };
        }

        private static ReachyCameraYuvColorRange ParseColorRange(
            string value)
        {
            return value switch
            {
                "limited" => ReachyCameraYuvColorRange.Limited,
                "full" => ReachyCameraYuvColorRange.Full,
                _ => ReachyCameraYuvColorRange.Unknown,
            };
        }
#endif
    }
}
