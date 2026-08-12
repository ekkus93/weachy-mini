#nullable enable

using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyAndroidCameraTextureBridge
    {
        private void InvalidateOutput(string message)
        {
            lastUploadedSessionId = 0UL;
            lastUploadedSequence = 0UL;
            DestroyFrameResources();
            if (current.State != ReachyCameraTextureBridgeState.Faulted &&
                current.State != ReachyCameraTextureBridgeState.Unsupported)
            {
                PublishWaiting(message);
            }
        }

        private void PublishWaiting(string message)
        {
            Publish(
                ReachyCameraTextureBridgeState.Waiting,
                message,
                null,
                current.UploadedFrameCount,
                current.StaleFrameCount);
        }

        private void Publish(
            ReachyCameraTextureBridgeState state,
            string message,
            ReachyCameraTextureFrameDescriptor? frame,
            ulong uploadedFrameCount,
            ulong staleFrameCount)
        {
            current = new ReachyCameraTextureBridgeSnapshot(
                state,
                message,
                frame,
                uploadedFrameCount,
                staleFrameCount,
                checked(current.Revision + 1UL));
            Changed?.Invoke(
                this,
                new ReachyCameraTextureBridgeChangedEventArgs(current));
        }

        // Disposal of yTexture/uTexture/vTexture/outputTexture. Creation of
        // these same fields lives in TextureResources.cs
        // (EnsurePlaneTextures/EnsurePlaneTexture/EnsureOutputTexture),
        // called only from PumpOnce (Pump.cs).
        private void DestroyFrameResources()
        {
            DestroyUnityObject(yTexture);
            DestroyUnityObject(uTexture);
            DestroyUnityObject(vTexture);
            yTexture = null;
            uTexture = null;
            vTexture = null;
            DestroyOutputTexture();
        }

        // conversionMaterial is created in Configure() (the anchor file) but
        // only ever destroyed here — a pre-existing asymmetry, not an
        // oversight introduced by this split.
        private void DestroyAllResources()
        {
            DestroyFrameResources();
            DestroyUnityObject(conversionMaterial);
            conversionMaterial = null;
        }

        // Disposal of outputTexture; creation lives in
        // TextureResources.cs's EnsureOutputTexture.
        private void DestroyOutputTexture()
        {
            if (outputTexture == null)
            {
                return;
            }
            if (outputTexture.IsCreated())
            {
                outputTexture.Release();
            }
            DestroyUnityObject(outputTexture);
            outputTexture = null;
        }

        private RenderTexture RequireOutputTexture()
        {
            return outputTexture ?? throw new InvalidOperationException(
                "The camera RGB render texture is unavailable.");
        }

        private static Texture2D RequireTexture(
            Texture2D? texture,
            string planeName)
        {
            return texture ?? throw new InvalidOperationException(
                $"The camera {planeName} plane texture is unavailable.");
        }

        private static void DestroyUnityObject(Object? value)
        {
            if (value == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(value);
            }
            else
            {
                Object.DestroyImmediate(value);
            }
        }
    }
}
