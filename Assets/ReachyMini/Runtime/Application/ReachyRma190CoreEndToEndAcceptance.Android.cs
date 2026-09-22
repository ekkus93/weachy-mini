#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;
using ReachyMini.Behavior;
using UnityEngine;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma190CoreEndToEndAcceptance
    {
        private static string ComputePlanSha256(
            ReachyBehaviorTrajectoryPlan plan)
        {
            var text = new StringBuilder();
            text.Append(plan.Purpose.ToString());
            text.Append('|');
            for (int frameIndex = 0; frameIndex < plan.Frames.Count; ++frameIndex)
            {
                ReachyBehaviorTrajectoryFrame frame = plan.Frames[frameIndex];
                text.Append(frame.OffsetMilliseconds.ToString(
                    CultureInfo.InvariantCulture));
                for (int targetIndex = 0;
                    targetIndex < frame.TargetPositionsRadians.Count;
                    ++targetIndex)
                {
                    text.Append(',');
                    text.Append(frame.TargetPositionsRadians[targetIndex].ToString(
                        "R",
                        CultureInfo.InvariantCulture));
                }
                text.Append(';');
            }
            byte[] payload = Encoding.UTF8.GetBytes(text.ToString());
            using System.Security.Cryptography.SHA256 sha256 =
                System.Security.Cryptography.SHA256.Create();
            byte[] digest = sha256.ComputeHash(payload);
            var hex = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; ++index)
            {
                hex.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
            }
            return hex.ToString();
        }

        private static NetworkSnapshot CaptureNetworkSnapshot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass(
                "com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject connectivity =
                activity.Call<AndroidJavaObject>(
                    "getSystemService",
                    "connectivity");
            AndroidJavaObject? activeNetwork =
                connectivity.Call<AndroidJavaObject>("getActiveNetwork");
            if (activeNetwork == null)
            {
                return new NetworkSnapshot(false, false, false);
            }
            using (activeNetwork)
            {
                AndroidJavaObject? capabilities =
                    connectivity.Call<AndroidJavaObject>(
                        "getNetworkCapabilities",
                        activeNetwork);
                if (capabilities == null)
                {
                    return new NetworkSnapshot(true, false, false);
                }
                using (capabilities)
                {
                    const int netCapabilityInternet = 12;
                    const int netCapabilityValidated = 16;
                    bool internet = capabilities.Call<bool>(
                        "hasCapability",
                        netCapabilityInternet);
                    bool validated = capabilities.Call<bool>(
                        "hasCapability",
                        netCapabilityValidated);
                    return new NetworkSnapshot(true, internet, validated);
                }
            }
#else
            throw new PlatformNotSupportedException(
                "RMA-190 physical network validation requires an Android player.");
#endif
        }

        private static bool ReadBooleanLaunchExtra(
            string name,
            bool defaultValue)
        {
