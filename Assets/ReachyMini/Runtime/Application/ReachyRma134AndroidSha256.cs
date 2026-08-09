#nullable enable

#if UNITY_ANDROID
using System;
using System.IO;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace ReachyMini.Validation
{
    /// <summary>
    /// Android-only SHA-256 implementation for the RMA-134 physical acceptance harness.
    ///
    /// This type is deliberately named SHA256 so that the existing acceptance-only
    /// VerifyArtifact method binds to Android's platform MessageDigest on Android,
    /// rather than System.Security.Cryptography.SHA256. The Unity/.NET SHA-256 path
    /// was physically observed to remain inside ComputeHash for more than 600 seconds
    /// on the LG-H872 for the frozen 396,704,416-byte GGUF. Production model approval
    /// remains owned by RMA-132; this class exists only to independently verify the
    /// exact staged artifact during RMA-134 physical acceptance.
    /// </summary>
    internal sealed class SHA256 : IDisposable
    {
        private const int BufferBytes = 4 * 1024 * 1024;
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

            AndroidJavaObject? digest = null;
            AndroidJavaObject? fileInput = null;
            AndroidJavaObject? digestInput = null;
            byte[]? result = null;
            Exception? failure = null;
            try
            {
                using AndroidJavaClass digestClass = new AndroidJavaClass("java.security.MessageDigest");
                digest = digestClass.CallStatic<AndroidJavaObject>("getInstance", "SHA-256");
                if (digest == null)
                {
                    throw new InvalidOperationException(
                        "Android java.security.MessageDigest returned null for SHA-256.");
                }

                fileInput = new AndroidJavaObject("java.io.FileInputStream", fileStream.Name);
                digestInput = new AndroidJavaObject(
                    "java.security.DigestInputStream",
                    fileInput,
                    digest);

                byte[] buffer = new byte[BufferBytes];
                while (true)
                {
                    int read = digestInput.Call<int>("read", buffer, 0, buffer.Length);
                    if (read < 0)
                    {
                        break;
                    }
                    if (read == 0)
                    {
                        continue;
                    }
                }

                result = digest.Call<byte[]>("digest");
                if (result == null || result.Length != 32)
                {
                    throw new InvalidOperationException(
                        "Android SHA-256 returned an invalid digest length: " +
                        (result == null ? "null" : result.Length.ToString()) + ".");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (digestInput != null)
                {
                    try
                    {
                        digestInput.Call("close");
                    }
                    catch (Exception closeException)
                    {
                        failure = CombineFailures(failure, closeException, "closing Android digest stream");
                    }
                }
                else if (fileInput != null)
                {
                    try
                    {
                        fileInput.Call("close");
                    }
                    catch (Exception closeException)
                    {
                        failure = CombineFailures(failure, closeException, "closing Android file stream");
                    }
                }

                digestInput?.Dispose();
                fileInput?.Dispose();
                digest?.Dispose();

                int detachStatus = AndroidJNI.DetachCurrentThread();
                if (detachStatus < 0)
                {
                    failure = CombineFailures(
                        failure,
                        new InvalidOperationException(
                            "RMA-134 could not detach its artifact-verification worker from the Android JVM: " +
                            detachStatus + "."),
                        "detaching Android SHA worker");
                }
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return result ?? throw new InvalidOperationException(
                "RMA-134 Android SHA verification completed without a digest.");
        }

        public void Dispose()
        {
            disposed = true;
        }

        private static Exception CombineFailures(Exception? primary, Exception secondary, string operation)
        {
            if (primary == null)
            {
                return new InvalidOperationException(
                    "RMA-134 failed while " + operation + ".",
                    secondary);
            }
            return new AggregateException(
                "RMA-134 encountered a primary verification failure and also failed while " + operation + ".",
                primary,
                secondary);
        }
    }
}
#endif
