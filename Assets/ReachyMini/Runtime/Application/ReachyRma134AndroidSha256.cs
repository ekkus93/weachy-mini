#nullable enable

#if UNITY_ANDROID
using System;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace ReachyMini.Validation
{
    /// <summary>
    /// Android-only SHA-256 implementation for the RMA-134 physical acceptance harness.
    /// The full file read/hash loop stays inside Java. Only the final 64-character
    /// digest crosses JNI, avoiding repeated large byte-array marshalling on API 26.
    /// </summary>
    internal sealed class SHA256 : IDisposable
    {
        private const string BridgeClassName =
            "com.ekkus93.weachy.rma134.ReachyRma134Sha256Bridge";
        private const string ProgressFileName = "rma134-local-llm-hash-progress.txt";
        private bool disposed;

        private SHA256()
        {
        }

        public static SHA256 Create()
        {
            return new SHA256();
        }

        public byte[] ComputeHash(Stream inputStream)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SHA256));
            }
            if (inputStream == null)
            {
                throw new ArgumentNullException(nameof(inputStream));
            }
            if (!(inputStream is FileStream fileStream))
            {
                throw new ArgumentException(
                    "RMA-134 Android SHA verification requires a FileStream.",
                    nameof(inputStream));
            }

            int attachStatus = AndroidJNI.AttachCurrentThread();
            if (attachStatus < 0)
            {
                throw new InvalidOperationException(
                    "RMA-134 could not attach its artifact-verification worker to the Android JVM: " +
                    attachStatus + ".");
            }

            string? digestHex = null;
            Exception? failure = null;
            try
            {
                string directory = Path.GetDirectoryName(fileStream.Name) ??
                    throw new InvalidOperationException(
                        "RMA-134 staged model path has no parent directory.");
                string progressPath = Path.Combine(directory, ProgressFileName);
                using AndroidJavaClass bridge = new AndroidJavaClass(BridgeClassName);
                digestHex = bridge.CallStatic<string>("sha256", fileStream.Name, progressPath);
                ValidateDigestHex(digestHex);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                int detachStatus = AndroidJNI.DetachCurrentThread();
                if (detachStatus < 0)
                {
                    Exception detachFailure = new InvalidOperationException(
                        "RMA-134 could not detach its artifact-verification worker from the Android JVM: " +
                        detachStatus + ".");
                    failure = failure == null
                        ? detachFailure
                        : new AggregateException(
                            "RMA-134 SHA verification failed and JVM detach also failed.",
                            failure,
                            detachFailure);
                }
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return DecodeHexDigest(digestHex ?? throw new InvalidOperationException(
                "RMA-134 Android SHA verification completed without a digest."));
        }

        public void Dispose()
        {
            disposed = true;
        }

        private static void ValidateDigestHex(string? value)
        {
            if (value == null || value.Length != 64)
            {
                throw new InvalidOperationException(
                    "RMA-134 Android SHA helper returned an invalid SHA-256 string length.");
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool hexadecimal =
                    (character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f');
                if (!hexadecimal)
                {
                    throw new InvalidOperationException(
                        "RMA-134 Android SHA helper returned non-lowercase-hexadecimal output.");
                }
            }
        }

        private static byte[] DecodeHexDigest(string value)
        {
            byte[] digest = new byte[32];
            for (int index = 0; index < digest.Length; ++index)
            {
                if (!byte.TryParse(
                    value.Substring(index * 2, 2),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out digest[index]))
                {
                    throw new InvalidOperationException(
                        "RMA-134 Android SHA helper returned an invalid SHA-256 digest.");
                }
            }
            return digest;
        }
    }
}
#endif
