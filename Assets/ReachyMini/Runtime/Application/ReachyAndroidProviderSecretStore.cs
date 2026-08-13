#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Providers;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed class ReachyAndroidProviderSecretStore : IReachyProviderSecretStore
    {
        private const string BridgeClassName =
            "com.ekkus93.weachy.providers.ReachyProviderSecretBridge";
        private const int MaximumSecretBytes = 16 * 1024;

        public void Put(string reference, byte[] secretUtf8)
        {
            ReachyProviderSecretReference.Validate(reference);
            if (secretUtf8 == null)
            {
                throw new ArgumentNullException(nameof(secretUtf8));
            }
            if (secretUtf8.Length == 0 || secretUtf8.Length > MaximumSecretBytes)
            {
                throw new ArgumentException(
                    "Provider secret bytes are empty or exceed the allowed size.",
                    nameof(secretUtf8));
            }

            using AndroidJavaObject activity = GetCurrentActivity();
            using AndroidJavaClass bridge = new AndroidJavaClass(BridgeClassName);
            bridge.CallStatic("put", activity, reference, secretUtf8);
        }

        public byte[] GetSecret(string reference)
        {
            ReachyProviderSecretReference.Validate(reference);
            using AndroidJavaObject activity = GetCurrentActivity();
            using AndroidJavaClass bridge = new AndroidJavaClass(BridgeClassName);
            byte[]? secret = bridge.CallStatic<byte[]>("get", activity, reference);
            return secret ?? throw new KeyNotFoundException(
                "The requested provider secret reference is not configured.");
        }

        public bool Contains(string reference)
        {
            ReachyProviderSecretReference.Validate(reference);
            using AndroidJavaObject activity = GetCurrentActivity();
            using AndroidJavaClass bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<bool>("contains", activity, reference);
        }

        public bool Delete(string reference)
        {
            ReachyProviderSecretReference.Validate(reference);
            using AndroidJavaObject activity = GetCurrentActivity();
            using AndroidJavaClass bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<bool>("delete", activity, reference);
        }

        internal static bool IsKeyguardLockedForAcceptance()
        {
            using AndroidJavaObject activity = GetCurrentActivity();
            using AndroidJavaClass bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<bool>("isKeyguardLocked", activity);
        }

        internal static bool IsDeviceSecureForAcceptance()
        {
            using AndroidJavaObject activity = GetCurrentActivity();
            using AndroidJavaClass bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<bool>("isDeviceSecure", activity);
        }

        internal static bool HasEncryptionKeyForAcceptance()
        {
            using AndroidJavaObject activity = GetCurrentActivity();
            using AndroidJavaClass bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<bool>("hasEncryptionKey", activity);
        }

        internal static bool InvalidateEncryptionKeyForAcceptance()
        {
            using AndroidJavaObject activity = GetCurrentActivity();
            using AndroidJavaClass bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<bool>(
                "invalidateEncryptionKeyForTesting",
                activity);
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using AndroidJavaClass unityPlayer = new AndroidJavaClass(
                "com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity") ??
                throw new InvalidOperationException(
                    "Unity currentActivity is unavailable for provider secret storage.");
        }
    }
}
