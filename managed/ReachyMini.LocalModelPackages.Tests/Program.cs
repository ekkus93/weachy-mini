#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

namespace ReachyMini.LocalModelPackages.Tests
{
    internal static class Program
    {
        private static readonly byte[] ModelBytes =
            Encoding.UTF8.GetBytes("synthetic-rma132-model-payload-v1");

        public static async Task<int> Main()
        {
            var tests = new (string Name, Func<Task> Run)[]
            {
                ("import and resolve", TestImportAndResolveAsync),
                ("wrong hash", TestWrongHashAsync),
                ("storage preflight", TestStorageAsync),
                ("fresh download", TestFreshDownloadAsync),
                ("exact resume", TestResumeAsync),
                ("clean restart", TestRestartAsync),
                ("wrong resume offset", TestWrongOffsetAsync),
                ("extra import bytes", TestExtraBytesAsync),
                ("tampered installed artifact", TestTamperAsync),
                ("exact delete", TestDeleteAsync),
                ("partial import recovery", TestRecoveryAsync),
                ("orphan cleanup", TestCleanupAsync),
                ("store ownership", TestOwnershipAsync),
                ("provenance origin", TestOriginAsync),
                ("source change restarts", TestSourceChangeAsync),
            };

            for (int index = 0; index < tests.Length; ++index)
            {
                await tests[index].Run().ConfigureAwait(false);
                Console.WriteLine("PASS: " + tests[index].Name);
            }

            Console.WriteLine("RMA-132 managed package contracts passed: 15");
            return 0;
        }

        private static async Task TestImportAndResolveAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest();
                LocalModelPackageResult imported = await ImportAsync(context, manifest, ModelBytes)
                    .ConfigureAwait(false);
                Require(imported.Succeeded, imported.Detail);
                Require(imported.Outcome == LocalModelPackageOutcome.Imported, "wrong import outcome");
                LocalModelApprovedArtifact approved = imported.Artifact ??
                    throw new InvalidOperationException("import did not issue approved artifact");
                Require(
                    approved.FullPath.StartsWith(
                        context.Root + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal),
                    "approved path escaped store");

                LocalModelPackageResult resolved =
                    await context.Manager.ResolveInstalledAsync(manifest, CancellationToken.None)
                        .ConfigureAwait(false);
                Require(resolved.Succeeded, resolved.Detail);
                LocalModelApprovedArtifact resolvedArtifact = resolved.Artifact ??
                    throw new InvalidOperationException("resolve did not issue approved artifact");
                Require(
                    string.Equals(
                        resolvedArtifact.Sha256,
                        manifest.Artifact.Sha256,
                        StringComparison.Ordinal),
                    "resolved hash identity changed");
            }).ConfigureAwait(false);
        }

        private static async Task TestWrongHashAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest(new string('0', 64));
                LocalModelPackageResult result = await ImportAsync(context, manifest, ModelBytes)
                    .ConfigureAwait(false);
                RequireFailure(result, LocalModelPackageFailure.Sha256Mismatch);
            }).ConfigureAwait(false);
        }

        private static async Task TestStorageAsync()
        {
            await WithStoreAsync(
                async context =>
                {
                    LocalModelPackageResult result =
                        await ImportAsync(context, CreateManifest(), ModelBytes)
                            .ConfigureAwait(false);
                    RequireFailure(result, LocalModelPackageFailure.InsufficientStorage);
                },
                0L).ConfigureAwait(false);
        }

        private static async Task TestFreshDownloadAsync()
        {
            await WithStoreAsync(async context =>
            {
                var transport = new ScriptedTransport(
                    (uri, offset) =>
                    {
                        Require(offset == 0L, "fresh download did not start at zero");
                        return Content(offset);
                    });
                LocalModelPackageResult result = await context.Manager.DownloadAsync(
                        CreateManifest(),
                        ArtifactUri("model.gguf"),
                        transport,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Require(result.Succeeded, result.Detail);
                Require(result.Outcome == LocalModelPackageOutcome.Installed, "wrong download outcome");
                Require(!result.Resumed && !result.Restarted, "fresh download reported resume/restart");
                Require(transport.CallCount == 1, "fresh download reopened source");
            }).ConfigureAwait(false);
        }

        private static async Task TestResumeAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest();
                int split = ModelBytes.Length / 2;
                var first = new ScriptedTransport(
                    (uri, offset) => Response(Slice(0, split), 0L));
                LocalModelPackageResult incomplete = await context.Manager.DownloadAsync(
                        manifest,
                        ArtifactUri("model.gguf"),
                        first,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                RequireFailure(incomplete, LocalModelPackageFailure.SizeMismatch);

                var second = new ScriptedTransport(
                    (uri, offset) =>
                    {
                        Require(offset == split, "resume offset was not exact");
                        return Content(offset);
                    });
                LocalModelPackageResult resumed = await context.Manager.DownloadAsync(
                        manifest,
                        ArtifactUri("model.gguf"),
                        second,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Require(resumed.Succeeded, resumed.Detail);
                Require(resumed.Resumed && !resumed.Restarted, "resume flags incorrect");
            }).ConfigureAwait(false);
        }

        private static async Task TestRestartAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest();
                int split = ModelBytes.Length / 2;
                await context.Manager.DownloadAsync(
                        manifest,
                        ArtifactUri("model.gguf"),
                        new ScriptedTransport((uri, offset) => Response(Slice(0, split), 0L)),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                int calls = 0;
                var restart = new ScriptedTransport(
                    (uri, offset) =>
                    {
                        ++calls;
                        if (calls == 1)
                        {
                            Require(offset == split, "restart did not first attempt resume");
                            return LocalModelDownloadResponse.CreateRestartRequired("range unavailable");
                        }
                        Require(offset == 0L, "clean restart did not start at zero");
                        return Content(0L);
                    });
                LocalModelPackageResult result = await context.Manager.DownloadAsync(
                        manifest,
                        ArtifactUri("model.gguf"),
                        restart,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Require(result.Succeeded, result.Detail);
                Require(result.Restarted && !result.Resumed, "restart flags incorrect");
                Require(calls == 2, "restart did not make two source opens");
            }).ConfigureAwait(false);
        }

        private static async Task TestWrongOffsetAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest();
                int split = ModelBytes.Length / 2;
                await context.Manager.DownloadAsync(
                        manifest,
                        ArtifactUri("model.gguf"),
                        new ScriptedTransport((uri, offset) => Response(Slice(0, split), 0L)),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                var wrong = new ScriptedTransport(
                    (uri, offset) => Response(Slice(split, ModelBytes.Length - split), offset + 1L));
                LocalModelPackageResult result = await context.Manager.DownloadAsync(
                        manifest,
                        ArtifactUri("model.gguf"),
                        wrong,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                RequireFailure(result, LocalModelPackageFailure.ResumeProtocolViolation);
            }).ConfigureAwait(false);
        }

        private static async Task TestExtraBytesAsync()
        {
            await WithStoreAsync(async context =>
            {
                var extra = new byte[ModelBytes.Length + 1];
                Buffer.BlockCopy(ModelBytes, 0, extra, 0, ModelBytes.Length);
                extra[extra.Length - 1] = 42;
                LocalModelPackageResult result =
                    await ImportAsync(context, CreateManifest(), extra).ConfigureAwait(false);
                RequireFailure(result, LocalModelPackageFailure.SizeMismatch);
            }).ConfigureAwait(false);
        }

        private static async Task TestTamperAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest();
                LocalModelPackageResult installed =
                    await ImportAsync(context, manifest, ModelBytes).ConfigureAwait(false);
                LocalModelApprovedArtifact approved = installed.Artifact ??
                    throw new InvalidOperationException("test setup did not install model");
                File.WriteAllText(approved.FullPath, "tampered", Encoding.UTF8);

                LocalModelPackageResult result =
                    await context.Manager.ResolveInstalledAsync(manifest, CancellationToken.None)
                        .ConfigureAwait(false);
                RequireFailure(result, LocalModelPackageFailure.InstalledArtifactCorrupt);
                Require(result.Artifact == null, "corrupt model exposed approved path");
            }).ConfigureAwait(false);
        }

        private static async Task TestDeleteAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest();
                LocalModelPackageResult installed =
                    await ImportAsync(context, manifest, ModelBytes).ConfigureAwait(false);
                LocalModelApprovedArtifact approved = installed.Artifact ??
                    throw new InvalidOperationException("test setup did not install model");
                string path = approved.FullPath;

                LocalModelPackageResult deleted =
                    await context.Manager.DeleteAsync(manifest, CancellationToken.None)
                        .ConfigureAwait(false);
                Require(deleted.Succeeded, deleted.Detail);
                Require(deleted.Outcome == LocalModelPackageOutcome.Deleted, "wrong delete outcome");
                Require(!File.Exists(path), "delete left installed file");

                LocalModelPackageResult again =
                    await context.Manager.DeleteAsync(manifest, CancellationToken.None)
                        .ConfigureAwait(false);
                Require(again.Succeeded, again.Detail);
                Require(again.Outcome == LocalModelPackageOutcome.NotInstalled, "repeat delete not explicit");
            }).ConfigureAwait(false);
        }

        private static async Task TestRecoveryAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest();
                await context.Manager.ResolveInstalledAsync(manifest, CancellationToken.None)
                    .ConfigureAwait(false);
                string staging = StagingDirectory(context.Root, manifest);
                Directory.CreateDirectory(staging);
                string part = Path.Combine(staging, "artifact.import.part");
                File.WriteAllBytes(part, Slice(0, 4));

                LocalModelRecoveryReport report = await context.Manager.RecoverAsync(
                        new[] { manifest },
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Require(report.Succeeded, report.Detail);
                Require(report.RemovedImportPartials == 1, "recovery did not remove import partial");
                Require(!File.Exists(part), "import partial survived recovery");
            }).ConfigureAwait(false);
        }

        private static async Task TestCleanupAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest();
                await context.Manager.ResolveInstalledAsync(manifest, CancellationToken.None)
                    .ConfigureAwait(false);

                string installed = Path.Combine(
                    context.Root, "installed", "orphan", new string('1', 64), "model.gguf");
                string? installedParent = Path.GetDirectoryName(installed);
                if (installedParent == null)
                {
                    throw new InvalidOperationException("orphan installed path has no parent");
                }
                Directory.CreateDirectory(installedParent);
                File.WriteAllBytes(installed, ModelBytes);

                string staging = Path.Combine(
                    context.Root, "staging", "orphan", new string('2', 64));
                Directory.CreateDirectory(staging);
                File.WriteAllText(Path.Combine(staging, "junk"), "junk", Encoding.UTF8);
                string quarantine = Path.Combine(context.Root, "quarantine", "old");
                Directory.CreateDirectory(quarantine);
                File.WriteAllText(Path.Combine(quarantine, "junk"), "junk", Encoding.UTF8);

                LocalModelCleanupReport report = await context.Manager.CleanupOrphansAsync(
                        new[] { manifest },
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Require(report.Succeeded, report.Detail);
                Require(report.RemovedInstalledOrphans >= 1, "installed orphan not removed");
                Require(report.RemovedStagingEntries >= 1, "staging orphan not removed");
                Require(report.RemovedQuarantineEntries >= 1, "quarantine not cleaned");
                Require(!File.Exists(installed), "installed orphan remains");
            }).ConfigureAwait(false);
        }

        private static async Task TestOwnershipAsync()
        {
            string root = TemporaryRoot();
            Directory.CreateDirectory(root);
            string unrelated = Path.Combine(root, "unrelated.txt");
            File.WriteAllText(unrelated, "keep", Encoding.UTF8);
            try
            {
                using var manager = Manager(root, long.MaxValue);
                LocalModelPackageResult result = await manager.ResolveInstalledAsync(
                        CreateManifest(),
                        CancellationToken.None)
                    .ConfigureAwait(false);
                RequireFailure(result, LocalModelPackageFailure.StoreOwnershipMismatch);
                Require(File.Exists(unrelated), "ownership failure modified unrelated file");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static async Task TestOriginAsync()
        {
            await WithStoreAsync(async context =>
            {
                var transport = new ScriptedTransport(
                    (uri, offset) => throw new InvalidOperationException("transport must not run"));
                LocalModelPackageResult result = await context.Manager.DownloadAsync(
                        CreateManifest(),
                        new Uri("https://other.example.invalid/model.gguf"),
                        transport,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                RequireFailure(result, LocalModelPackageFailure.DownloadUriRejected);
                Require(transport.CallCount == 0, "rejected URI reached transport");
            }).ConfigureAwait(false);
        }

        private static async Task TestSourceChangeAsync()
        {
            await WithStoreAsync(async context =>
            {
                LocalModelManifest manifest = CreateManifest();
                int split = ModelBytes.Length / 2;
                await context.Manager.DownloadAsync(
                        manifest,
                        ArtifactUri("first.gguf"),
                        new ScriptedTransport((uri, offset) => Response(Slice(0, split), 0L)),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                var changed = new ScriptedTransport(
                    (uri, offset) =>
                    {
                        Require(offset == 0L, "changed source reused another source partial");
                        return Content(0L);
                    });
                LocalModelPackageResult result = await context.Manager.DownloadAsync(
                        manifest,
                        ArtifactUri("second.gguf"),
                        changed,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Require(result.Succeeded, result.Detail);
                Require(!result.Resumed, "changed source reported resume");
            }).ConfigureAwait(false);
        }

        private static async Task<LocalModelPackageResult> ImportAsync(
            TestContext context,
            LocalModelManifest manifest,
            byte[] bytes)
        {
            using var source = new MemoryStream(bytes, writable: false);
            return await context.Manager.ImportAsync(manifest, source, CancellationToken.None)
                .ConfigureAwait(false);
        }

        private static LocalModelDownloadResponse Content(long offset)
        {
            int start = checked((int)offset);
            return Response(Slice(start, ModelBytes.Length - start), offset);
        }

        private static LocalModelDownloadResponse Response(byte[] bytes, long offset)
        {
            var stream = new MemoryStream(bytes, writable: false);
            try
            {
                LocalModelDownloadResponse response =
                    LocalModelDownloadResponse.CreateContent(stream, offset, ModelBytes.Length);
                stream = null!;
                return response;
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }
        }

        private static byte[] Slice(int offset, int count)
        {
            var bytes = new byte[count];
            Buffer.BlockCopy(ModelBytes, offset, bytes, 0, count);
            return bytes;
        }

        private static LocalModelManifest CreateManifest(string? shaOverride = null)
        {
            return new LocalModelManifest(
                LocalModelManifestPolicy.CurrentSchemaVersion,
                new LocalModelIdentity(
                    "rma132.synthetic",
                    "rma132.synthetic-model",
                    "RMA-132 Synthetic Model",
                    "1",
                    new Uri("https://models.example.invalid/repository"),
                    "synthetic-revision",
                    "NOASSERTION",
                    experimental: true,
                    "Synthetic package-manager test fixture only."),
                new LocalModelRuntimeRequirement(
                    "reachy_llama",
                    LocalModelManifestPolicy.ReachyLlamaAbiVersion,
                    requiresNetworkAccess: false),
                new LocalModelArtifact(
                    "model.gguf",
                    ModelBytes.Length,
                    shaOverride ?? Sha256(ModelBytes)),
                new LocalModelGgufMetadata(
                    3,
                    "synthetic",
                    "synthetic-q",
                    1L,
                    "synthetic-tokenizer",
                    "synthetic-pre"),
                new LocalModelInferenceProfile(
                    1024,
                    "{{ synthetic }}",
                    Array.Empty<string>(),
                    new LocalModelMemoryEstimate(1024L * 1024L, 1024, 128),
                    1),
                new LocalModelDeviceCompatibility(
                    new[] { "arm64-v8a" },
                    26,
                    Array.Empty<string>(),
                    1024L * 1024L,
                    LocalModelManifestPolicy.ReachyLlamaAbiVersion));
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            byte[] digest = hash.ComputeHash(bytes);
            var builder = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; ++index)
            {
                builder.Append(digest[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static Uri ArtifactUri(string fileName)
        {
            return new Uri("https://models.example.invalid/" + fileName);
        }

        private static string StagingDirectory(string root, LocalModelManifest manifest)
        {
            return Path.Combine(
                root,
                "staging",
                manifest.Identity.ManifestId,
                manifest.Artifact.Sha256);
        }

        private static async Task WithStoreAsync(
            Func<TestContext, Task> body,
            long availableBytes = long.MaxValue)
        {
            string root = TemporaryRoot();
            try
            {
                using var context = new TestContext(root, Manager(root, availableBytes));
                await body(context).ConfigureAwait(false);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static LocalModelPackageManager Manager(string root, long availableBytes)
        {
            return new LocalModelPackageManager(
                root,
                new ConstantStorageProbe(availableBytes),
                new LocalModelPackageOptions(
                    maximumArtifactBytes: 1024L * 1024L,
                    safetyReserveBytes: 0L));
        }

        private static string TemporaryRoot()
        {
            return Path.Combine(Path.GetTempPath(), "reachy-rma132-" + Guid.NewGuid().ToString("N"));
        }

        private static void RequireFailure(
            LocalModelPackageResult result,
            LocalModelPackageFailure expected)
        {
            Require(!result.Succeeded, "operation unexpectedly succeeded");
            Require(result.Failure == expected, "operation returned wrong failure");
            Require(result.Artifact == null, "failed operation exposed approved artifact");
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition)
            {
                throw new InvalidOperationException(detail);
            }
        }

        private sealed class TestContext : IDisposable
        {
            public TestContext(string root, LocalModelPackageManager manager)
            {
                Root = root;
                Manager = manager;
            }

            public string Root { get; }

            public LocalModelPackageManager Manager { get; }

            public void Dispose()
            {
                Manager.Dispose();
            }
        }

        private sealed class ConstantStorageProbe : ILocalModelStorageProbe
        {
            private readonly long availableBytes;

            public ConstantStorageProbe(long availableBytes)
            {
                this.availableBytes = availableBytes;
            }

            public long GetAvailableBytes(string managedStoreRoot)
            {
                Require(Path.IsPathRooted(managedStoreRoot), "storage probe path not rooted");
                return availableBytes;
            }
        }

        private sealed class ScriptedTransport : ILocalModelDownloadTransport
        {
            private readonly Func<Uri, long, LocalModelDownloadResponse> open;

            public ScriptedTransport(Func<Uri, long, LocalModelDownloadResponse> open)
            {
                this.open = open ?? throw new ArgumentNullException(nameof(open));
            }

            public int CallCount { get; private set; }

            public Task<LocalModelDownloadResponse> OpenAsync(
                Uri sourceUri,
                long requestedOffset,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ++CallCount;
                return Task.FromResult(open(sourceUri, requestedOffset));
            }
        }
    }
}
