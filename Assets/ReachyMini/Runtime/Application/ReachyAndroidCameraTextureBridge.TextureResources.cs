#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyAndroidCameraTextureBridge
    {
        // Creation lives here; the only caller is PumpOnce (Pump.cs). Disposal of
        // these same plane/output textures lives in Diagnostics.cs
        // (DestroyFrameResources/DestroyOutputTexture/DestroyAllResources).
        private void EnsurePlaneTextures(
            ReachyCameraTextureFrameDescriptor descriptor)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.R8))
            {
                throw new InvalidOperationException(
                    "This device does not support the required R8 plane texture format.");
            }
            yTexture = EnsurePlaneTexture(
                yTexture,
                descriptor.Width,
                descriptor.Height,
                "ReachyCameraY");
            uTexture = EnsurePlaneTexture(
                uTexture,
                descriptor.ChromaWidth,
                descriptor.ChromaHeight,
                "ReachyCameraU");
            vTexture = EnsurePlaneTexture(
                vTexture,
                descriptor.ChromaWidth,
                descriptor.ChromaHeight,
                "ReachyCameraV");
        }

        private static Texture2D EnsurePlaneTexture(
            Texture2D? existing,
            int width,
            int height,
            string name)
        {
            if (existing != null &&
                existing.width == width && existing.height == height)
            {
                return existing;
            }
            DestroyUnityObject(existing);
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.R8,
                mipChain: false,
                linear: true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            return texture;
        }

        // Creation lives here; disposal (DestroyOutputTexture) lives in
        // Diagnostics.cs — see the cross-reference comment there.
        private void EnsureOutputTexture(
            ReachyCameraTextureFrameDescriptor descriptor)
        {
            if (outputTexture != null &&
                outputTexture.width == descriptor.OutputWidth &&
                outputTexture.height == descriptor.OutputHeight)
            {
                return;
            }
            DestroyOutputTexture();
            outputTexture = new RenderTexture(
                descriptor.OutputWidth,
                descriptor.OutputHeight,
                depth: 0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "ReachyCameraRgb",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!outputTexture.Create())
            {
                DestroyOutputTexture();
                throw new InvalidOperationException(
                    "Unity could not create the camera RGB render texture.");
            }
        }

        private void ConfigureMaterial(
            ReachyCameraTextureFrameDescriptor descriptor)
        {
            Material material = conversionMaterial ??
                throw new InvalidOperationException(
                    "The camera conversion material is unavailable.");
            material.SetTexture(YTextureId, yTexture);
            material.SetTexture(UTextureId, uTexture);
            material.SetTexture(VTextureId, vTexture);
            material.SetVector(
                CropScaleOffsetId,
                new Vector4(
                    (float)descriptor.Crop.Width / descriptor.Width,
                    (float)descriptor.Crop.Height / descriptor.Height,
                    (float)descriptor.Crop.Left / descriptor.Width,
                    (float)descriptor.Crop.Top / descriptor.Height));
            material.SetFloat(
                RotationQuarterTurnsId,
                descriptor.RotationDegrees / 90f);
            material.SetFloat(MirrorXId, descriptor.Mirrored ? 1f : 0f);
            material.SetFloat(
                ColorStandardId,
                descriptor.ColorStandard == ReachyCameraYuvColorStandard.Bt709
                    ? 1f
                    : 0f);
            material.SetFloat(
                ColorRangeId,
                descriptor.ColorRange == ReachyCameraYuvColorRange.Full
                    ? 1f
                    : 0f);
        }

        private static void UploadPlane(
            Texture2D texture,
            IntPtr source,
            int length,
            string planeName)
        {
            int expected = checked(texture.width * texture.height);
            if (length != expected)
            {
                throw new InvalidOperationException(
                    $"{planeName} plane length {length} does not match texture size {expected}.");
            }
            texture.LoadRawTextureData(source, length);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }
    }
}
