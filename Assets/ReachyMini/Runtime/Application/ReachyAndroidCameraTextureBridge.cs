#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    public sealed partial class ReachyAndroidCameraTextureBridge : MonoBehaviour
    {
        public const string ShaderName =
            "Hidden/ReachyMini/CameraYuv420ToRgb";

        private static readonly int YTextureId =
            Shader.PropertyToID("_YTexture");
        private static readonly int UTextureId =
            Shader.PropertyToID("_UTexture");
        private static readonly int VTextureId =
            Shader.PropertyToID("_VTexture");
        private static readonly int CropScaleOffsetId =
            Shader.PropertyToID("_CropScaleOffset");
        private static readonly int RotationQuarterTurnsId =
            Shader.PropertyToID("_RotationQuarterTurns");
        private static readonly int MirrorXId =
            Shader.PropertyToID("_MirrorX");
        private static readonly int ColorStandardId =
            Shader.PropertyToID("_ColorStandard");
        private static readonly int ColorRangeId =
            Shader.PropertyToID("_ColorRange");

        private ReachyAndroidCameraAcquisition? acquisition;
        private Material? conversionMaterial;
        private Texture2D? yTexture;
        private Texture2D? uTexture;
        private Texture2D? vTexture;
        private RenderTexture? outputTexture;
        private ulong lastUploadedSessionId;
        private ulong lastUploadedSequence;
        private bool disposed;
        private ReachyCameraTextureBridgeSnapshot current =
            new ReachyCameraTextureBridgeSnapshot(
                ReachyCameraTextureBridgeState.Waiting,
                "Camera texture bridge is waiting for a running acquisition session.",
                null,
                0UL,
                0UL,
                0UL);

        public ReachyCameraTextureBridgeSnapshot Current => current;

        public RenderTexture? OutputTexture =>
            current.HasTexture ? outputTexture : null;

        public Texture? PreviewTexture => OutputTexture;

        public Texture? AnalysisTexture => OutputTexture;

        public event EventHandler<ReachyCameraTextureBridgeChangedEventArgs>?
            Changed;

        public void Configure(
            ReachyAndroidCameraAcquisition cameraAcquisition,
            Shader? conversionShader = null)
        {
            if (cameraAcquisition == null)
            {
                throw new ArgumentNullException(nameof(cameraAcquisition));
            }
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyAndroidCameraTextureBridge));
            }
            if (acquisition != null && acquisition != cameraAcquisition)
            {
                throw new InvalidOperationException(
                    "The texture bridge acquisition service cannot change after configuration.");
            }

            Shader shader = conversionShader ?? Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                Publish(
                    ReachyCameraTextureBridgeState.Unsupported,
                    $"Required camera YUV conversion shader '{ShaderName}' is unavailable or unsupported.",
                    null,
                    current.UploadedFrameCount,
                    current.StaleFrameCount);
                throw new InvalidOperationException(current.Message);
            }

            acquisition = cameraAcquisition;
            acquisition.State.Changed -= OnAcquisitionChanged;
            acquisition.State.Changed += OnAcquisitionChanged;
            // conversionMaterial is created here; it is destroyed only in
            // DestroyAllResources() (Diagnostics.cs) — a pre-existing
            // asymmetry, not something introduced by this split.
            if (conversionMaterial == null)
            {
                conversionMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            PublishWaiting(
                "Camera texture bridge is configured and waiting for a frame.");
        }
    }
}
