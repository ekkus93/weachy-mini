#nullable enable

using System;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    [DefaultExecutionOrder(10000)]
    internal sealed class ReachyCameraTextureStageDiagnostics : MonoBehaviour
    {
#if UNITY_ANDROID && !UNITY_EDITOR && DEVELOPMENT_BUILD
        private const float SampleIntervalSeconds = 0.5f;
        private const int MaximumSamplesPerPlane = 4096;

        private static readonly FieldInfo? YTextureField =
            typeof(ReachyAndroidCameraTextureBridge).GetField(
                "yTexture",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? UTextureField =
            typeof(ReachyAndroidCameraTextureBridge).GetField(
                "uTexture",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? VTextureField =
            typeof(ReachyAndroidCameraTextureBridge).GetField(
                "vTexture",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? ConversionMaterialField =
            typeof(ReachyAndroidCameraTextureBridge).GetField(
                "conversionMaterial",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private ReachyAndroidCameraTextureBridge? bridge;
        private float nextSampleTime;
        private ulong sampledSessionId;
        private ulong sampledSequence;
        private bool syntheticProbeAttempted;
        private bool syntheticProbePassed;
        private int syntheticMinimum;
        private int syntheticMaximum;
        private bool syntheticOpaque;
        private PlaneRange yRange;
        private PlaneRange uRange;
        private PlaneRange vRange;
        private string stageMessage = "Waiting for the camera texture bridge.";
        private string markerPath = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForAcceptance()
        {
            if (!ReachyCameraTextureEvidence.IsAcceptanceRequestedFromLaunchIntent())
            {
                return;
            }

            var host = new GameObject("RMA092CameraTextureStageDiagnostics")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            DontDestroyOnLoad(host);
            host.AddComponent<ReachyCameraTextureStageDiagnostics>();
        }

        private void Update()
        {
            if (!syntheticProbeAttempted)
            {
                syntheticProbeAttempted = true;
                RunSyntheticProbe();
            }

            if (Time.unscaledTime < nextSampleTime)
            {
                return;
            }
            nextSampleTime = Time.unscaledTime + SampleIntervalSeconds;

            bridge ??= Object.FindFirstObjectByType<
                ReachyAndroidCameraTextureBridge>();
            ReachyCameraTextureFrameDescriptor? frame = bridge?.Current.Frame;
            if (bridge == null || frame == null ||
                bridge.Current.State != ReachyCameraTextureBridgeState.Ready)
            {
                stageMessage = "Waiting for a ready live camera texture frame.";
                PublishMarker();
                return;
            }
            if (frame.SessionId == sampledSessionId &&
                frame.Sequence == sampledSequence)
            {
                return;
            }

            sampledSessionId = frame.SessionId;
            sampledSequence = frame.Sequence;
            try
            {
                Texture2D yTexture = RequireTexture(YTextureField, "Y");
                Texture2D uTexture = RequireTexture(UTextureField, "U");
                Texture2D vTexture = RequireTexture(VTextureField, "V");
                yRange = SampleTexture(yTexture);
                uRange = SampleTexture(uTexture);
                vRange = SampleTexture(vTexture);
                Material? material =
                    ConversionMaterialField?.GetValue(bridge) as Material;
                RenderTexture? output = bridge.OutputTexture;
                stageMessage =
                    $"live sequence={frame.Sequence}; " +
                    $"Y={yRange.Minimum}-{yRange.Maximum}; " +
                    $"U={uRange.Minimum}-{uRange.Maximum}; " +
                    $"V={vRange.Minimum}-{vRange.Maximum}; " +
                    $"material={(material != null ? "present" : "missing")}; " +
                    $"passes={material?.passCount ?? 0}; " +
                    $"shader_supported={material?.shader.isSupported ?? false}; " +
                    $"output_created={output?.IsCreated() ?? false}; " +
                    $"graphics={SystemInfo.graphicsDeviceType}/{SystemInfo.graphicsDeviceName}";
            }
            catch (Exception exception)
            {
                stageMessage = "stage_diagnostics_failed: " + exception.Message;
            }
            PublishMarker();
        }

        private void OnGUI()
        {
            if (!ReachyCameraTextureEvidence.IsAcceptanceRequestedFromLaunchIntent())
            {
                return;
            }
            GUI.depth = -2000;
            GUI.Box(
                new Rect(12f, 60f, Math.Min(Screen.width - 24f, 1100f), 84f),
                $"RMA-092 stages: synthetic={syntheticProbePassed} " +
                $"rgb={syntheticMinimum}-{syntheticMaximum} " +
                $"opaque={syntheticOpaque}\n{stageMessage}");
        }

        private Texture2D RequireTexture(FieldInfo? field, string planeName)
        {
            Texture2D? texture = field?.GetValue(bridge) as Texture2D;
            return texture ?? throw new InvalidOperationException(
                $"The live {planeName} plane texture is unavailable.");
        }

        private static PlaneRange SampleTexture(Texture2D texture)
        {
            NativeArray<byte> data = texture.GetRawTextureData<byte>();
            if (!data.IsCreated || data.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Texture {texture.name} has no readable CPU plane data.");
            }

            int minimum = 255;
            int maximum = 0;
            int stride = Math.Max(1, data.Length / MaximumSamplesPerPlane);
            for (int index = 0; index < data.Length; index += stride)
            {
                byte value = data[index];
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
            return new PlaneRange(minimum, maximum);
        }

        private void RunSyntheticProbe()
        {
            Shader shader = Shader.Find(ReachyAndroidCameraTextureBridge.ShaderName);
            if (shader == null || !shader.isSupported)
            {
                stageMessage = "The retained YUV conversion shader is unavailable.";
                PublishMarker();
                return;
            }

            Texture2D? yTexture = null;
            Texture2D? uTexture = null;
            Texture2D? vTexture = null;
            RenderTexture? output = null;
            Texture2D? readback = null;
            Material? material = null;
            RenderTexture? previous = RenderTexture.active;
            try
            {
                yTexture = CreatePlaneTexture(
                    4,
                    4,
                    new byte[]
                    {
                        16, 82, 145, 235,
                        16, 82, 145, 235,
                        16, 82, 145, 235,
                        16, 82, 145, 235,
                    });
                uTexture = CreatePlaneTexture(2, 2, new byte[]
                {
                    128, 128,
                    128, 128,
                });
                vTexture = CreatePlaneTexture(2, 2, new byte[]
                {
                    128, 128,
                    128, 128,
                });
                material = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                material.SetTexture("_YTexture", yTexture);
                material.SetTexture("_UTexture", uTexture);
                material.SetTexture("_VTexture", vTexture);
                material.SetVector(
                    "_CropScaleOffset",
                    new Vector4(1f, 1f, 0f, 0f));
                material.SetFloat("_RotationQuarterTurns", 0f);
                material.SetFloat("_MirrorX", 0f);
                material.SetFloat("_ColorStandard", 1f);
                material.SetFloat("_ColorRange", 0f);

                output = new RenderTexture(
                    4,
                    4,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (!output.Create())
                {
                    throw new InvalidOperationException(
                        "The synthetic RGB render texture could not be created.");
                }
                Graphics.Blit(yTexture, output, material);

                readback = new Texture2D(
                    4,
                    4,
                    TextureFormat.RGBA32,
                    false,
                    false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                RenderTexture.active = output;
                readback.ReadPixels(new Rect(0f, 0f, 4f, 4f), 0, 0, false);
                readback.Apply(false, false);
                Color32[] pixels = readback.GetPixels32();
                syntheticMinimum = 255;
                syntheticMaximum = 0;
                syntheticOpaque = true;
                foreach (Color32 pixel in pixels)
                {
                    syntheticMinimum = Math.Min(
                        syntheticMinimum,
                        Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)));
                    syntheticMaximum = Math.Max(
                        syntheticMaximum,
                        Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)));
                    syntheticOpaque &= pixel.a >= 250;
                }
                syntheticProbePassed =
                    syntheticOpaque &&
                    syntheticMaximum - syntheticMinimum >= 128;
            }
            catch (Exception exception)
            {
                stageMessage = "synthetic_probe_failed: " + exception.Message;
            }
            finally
            {
                RenderTexture.active = previous;
                if (output != null && output.IsCreated())
                {
                    output.Release();
                }
                DestroyObject(readback);
                DestroyObject(output);
                DestroyObject(material);
                DestroyObject(yTexture);
                DestroyObject(uTexture);
                DestroyObject(vTexture);
                PublishMarker();
            }
        }

        private static Texture2D CreatePlaneTexture(
            int width,
            int height,
            byte[] data)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.R8,
                false,
                true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.LoadRawTextureData(data);
            texture.Apply(false, false);
            return texture;
        }

        private void PublishMarker()
        {
            try
            {
                string directory = Application.persistentDataPath;
                Directory.CreateDirectory(directory);
                if (!string.IsNullOrEmpty(markerPath) && File.Exists(markerPath))
                {
                    File.Delete(markerPath);
                }

                string graphics = Sanitize(SystemInfo.graphicsDeviceType.ToString());
                string fileName =
                    $"rma092-stage-synth-{(syntheticProbePassed ? 1 : 0)}-" +
                    $"rgb-{syntheticMinimum}-{syntheticMaximum}-" +
                    $"opaque-{(syntheticOpaque ? 1 : 0)}-" +
                    $"y-{yRange.Minimum}-{yRange.Maximum}-" +
                    $"u-{uRange.Minimum}-{uRange.Maximum}-" +
                    $"v-{vRange.Minimum}-{vRange.Maximum}-" +
                    $"api-{graphics}.txt";
                markerPath = Path.Combine(directory, fileName);
                File.WriteAllText(markerPath, stageMessage + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not publish RMA-092 stage diagnostics: " +
                    exception.Message,
                    this);
            }
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }
            return value.Replace(' ', '_');
        }

        private static void DestroyObject(Object? value)
        {
            if (value != null)
            {
                Object.Destroy(value);
            }
        }

        private readonly struct PlaneRange
        {
            public PlaneRange(int minimum, int maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            public int Minimum { get; }

            public int Maximum { get; }
        }
#endif
    }
}
