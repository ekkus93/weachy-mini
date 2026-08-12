#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalLlmProvider : IAsyncDisposable
    {
        private static readonly TimeSpan CancellationDrainTimeout = TimeSpan.FromSeconds(30);

        private readonly object sync = new object();
        private readonly ILocalLlmRuntime runtime;
        private readonly LocalModelManifest manifest;
        private readonly LocalModelApprovedArtifact artifact;
        private readonly LocalLlmExecutionProfile profile;
        private readonly bool ownsRuntime;

        private LocalLlmProviderState state;
        private ulong modelHandle;
        private ulong conversationEpoch = 1UL;
        private CancellationTokenSource? activeCancellation;
        private Task<LocalLlmGenerationResult>? activeGenerationTask;

        private LocalLlmProvider(
            ILocalLlmRuntime runtime,
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile profile,
            ulong modelHandle,
            bool ownsRuntime)
        {
            this.runtime = runtime;
            this.manifest = manifest;
            this.artifact = artifact;
            this.profile = profile;
            this.modelHandle = modelHandle;
            this.ownsRuntime = ownsRuntime;
            state = LocalLlmProviderState.Ready;
        }

        public LocalLlmProviderState State
        {
            get
            {
                lock (sync)
                {
                    return state;
                }
            }
        }

        public ulong ConversationEpoch
        {
            get
            {
                lock (sync)
                {
                    return conversationEpoch;
                }
            }
        }

        public LocalLlmExecutionProfile ExecutionProfile => profile;

        public string ModelId => manifest.Identity.ModelId;

        public static async Task<LocalLlmProviderCreationResult> CreateAsync(
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile profile,
            CancellationToken cancellationToken)
        {
            ReachyLlamaLocalLlmRuntime? ownedRuntime = new ReachyLlamaLocalLlmRuntime();
            try
            {
                LocalLlmProviderCreationResult result = await CreateCoreAsync(
                    manifest,
                    artifact,
                    profile,
                    ownedRuntime,
                    ownsRuntime: true,
                    cancellationToken).ConfigureAwait(false);
                if (result.Provider != null)
                {
                    ownedRuntime = null;
                }
                return result;
            }
            finally
            {
                ownedRuntime?.Dispose();
            }
        }

        internal static Task<LocalLlmProviderCreationResult> CreateForTestingAsync(
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile profile,
            ILocalLlmRuntime runtime,
            CancellationToken cancellationToken)
        {
            return CreateCoreAsync(
                manifest,
                artifact,
                profile,
                runtime,
                ownsRuntime: false,
                cancellationToken);
        }

        public async Task<LocalLlmReloadResult> ReloadAsync(CancellationToken cancellationToken)
        {
            ulong handleToUnload;
            lock (sync)
            {
                if (state == LocalLlmProviderState.Disposed)
                {
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Disposed,
                        "The local LLM provider is disposed.",
                        ReachyLlamaNativeContract.StatusNotFound);
                }
                if (state == LocalLlmProviderState.Generating || activeGenerationTask != null)
                {
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Busy,
                        "The local LLM provider cannot reload during generation.",
                        ReachyLlamaNativeContract.StatusBusy);
                }
                handleToUnload = modelHandle;
                state = LocalLlmProviderState.Loading;
            }

            try
            {
                ValidateConfiguration();
                if (runtime.GetAbiVersion() != ReachyLlamaNativeContract.AbiVersion)
                {
                    SetState(LocalLlmProviderState.Faulted);
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Unavailable,
                        "The installed reachy_llama runtime does not expose ABI 2.",
                        ReachyLlamaNativeContract.StatusAbiMismatch);
                }

                if (handleToUnload != 0UL)
                {
                    LocalLlmRuntimeCallResult unload = await Task.Run(
                        () => runtime.UnloadModel(handleToUnload),
                        CancellationToken.None).ConfigureAwait(false);
                    if (!unload.Succeeded)
                    {
                        SetState(LocalLlmProviderState.Faulted);
                        return new LocalLlmReloadResult(
                            LocalLlmReloadStatus.RuntimeFailure,
                            "Failed to unload the current local LLM before reload: " + unload.Detail,
                            unload.Status);
                    }
                    SetModelHandle(0UL, LocalLlmProviderState.Loading);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    SetState(LocalLlmProviderState.Unavailable);
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Cancelled,
                        "Local LLM reload was cancelled after the prior model was unloaded.",
                        ReachyLlamaNativeContract.StatusCancelled);
                }

                LocalLlmRuntimeLoadResult load = await Task.Run(
                    () => runtime.LoadModel(artifact.FullPath, checkTensors: true),
                    CancellationToken.None).ConfigureAwait(false);
                if (!load.Succeeded || load.ModelHandle == 0UL)
                {
                    SetState(LocalLlmProviderState.Faulted);
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.RuntimeFailure,
                        load.Detail.Length == 0
                            ? "reachy_llama did not return a model handle during reload."
                            : load.Detail,
                        load.Status);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    LocalLlmRuntimeCallResult cancelledUnload = await Task.Run(
                        () => runtime.UnloadModel(load.ModelHandle),
                        CancellationToken.None).ConfigureAwait(false);
                    if (!cancelledUnload.Succeeded)
                    {
                        SetState(LocalLlmProviderState.Faulted);
                        return new LocalLlmReloadResult(
                            LocalLlmReloadStatus.RuntimeFailure,
                            "Reload cancellation could not clean up the newly loaded model: " +
                                cancelledUnload.Detail,
                            cancelledUnload.Status);
                    }
                    SetModelHandle(0UL, LocalLlmProviderState.Unavailable);
                    return new LocalLlmReloadResult(
                        LocalLlmReloadStatus.Cancelled,
                        "Local LLM reload was cancelled and the newly loaded model was unloaded.",
                        ReachyLlamaNativeContract.StatusCancelled);
                }

                SetModelHandle(load.ModelHandle, LocalLlmProviderState.Ready);
                return new LocalLlmReloadResult(
                    LocalLlmReloadStatus.Reloaded,
                    string.Empty,
                    ReachyLlamaNativeContract.StatusOk);
            }
            catch (ArgumentException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return new LocalLlmReloadResult(
                    LocalLlmReloadStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }
            catch (InvalidOperationException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return new LocalLlmReloadResult(
                    LocalLlmReloadStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }
            catch (DllNotFoundException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(exception.Message, ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (EntryPointNotFoundException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(exception.Message, ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (BadImageFormatException exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(exception.Message, ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (Exception exception)
            {
                SetState(LocalLlmProviderState.Faulted);
                return RuntimeReloadFailure(
                    "Unexpected local LLM reload failure: " + exception.Message,
                    ReachyLlamaNativeContract.StatusInternalError);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task<LocalLlmGenerationResult>? activeTask;
            CancellationTokenSource? cancellation;
            lock (sync)
            {
                if (state == LocalLlmProviderState.Disposed)
                {
                    return;
                }
                state = LocalLlmProviderState.Disposed;
                conversationEpoch = NextEpoch(conversationEpoch);
                cancellation = activeCancellation;
                activeTask = activeGenerationTask;
                cancellation?.Cancel();
            }

            if (activeTask != null)
            {
                await activeTask.ConfigureAwait(false);
            }

            ulong handle;
            lock (sync)
            {
                handle = modelHandle;
                modelHandle = 0UL;
            }

            try
            {
                if (handle != 0UL)
                {
                    LocalLlmRuntimeCallResult unload = await Task.Run(
                        () => runtime.UnloadModel(handle),
                        CancellationToken.None).ConfigureAwait(false);
                    if (!unload.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "Failed to unload the local LLM during provider disposal: " + unload.Detail);
                    }
                }
            }
            finally
            {
                if (ownsRuntime)
                {
                    runtime.Dispose();
                }
                cancellation?.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private static async Task<LocalLlmProviderCreationResult> CreateCoreAsync(
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile profile,
            ILocalLlmRuntime runtime,
            bool ownsRuntime,
            CancellationToken cancellationToken)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (manifest == null || artifact == null || profile == null)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.InvalidConfiguration,
                    "Local LLM creation requires a manifest, approved artifact, and execution profile.",
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }

            try
            {
                LocalLlmBehaviorContract.ValidateFrozenBytes();
                LocalLlmBehaviorContract.ValidateSelectedInputs(manifest, artifact);
                profile.ValidateAgainst(manifest);
                if (string.IsNullOrWhiteSpace(artifact.FullPath) ||
                    artifact.FullPath.Contains('\0') ||
                    !Path.IsPathRooted(artifact.FullPath))
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.InvalidConfiguration,
                        "The approved local-model artifact path is not an absolute safe path.",
                        ReachyLlamaNativeContract.StatusInvalidArgument);
                }
                if (runtime.GetAbiVersion() != ReachyLlamaNativeContract.AbiVersion)
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.Unavailable,
                        "The installed reachy_llama runtime does not expose ABI 2.",
                        ReachyLlamaNativeContract.StatusAbiMismatch);
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.Cancelled,
                        "Local LLM provider creation was cancelled before model load.",
                        ReachyLlamaNativeContract.StatusCancelled);
                }

                LocalLlmRuntimeLoadResult load = await Task.Run(
                    () => runtime.LoadModel(artifact.FullPath, checkTensors: true),
                    CancellationToken.None).ConfigureAwait(false);
                if (!load.Succeeded || load.ModelHandle == 0UL)
                {
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.RuntimeFailure,
                        load.Detail.Length == 0
                            ? "reachy_llama did not return a model handle."
                            : load.Detail,
                        load.Status);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    LocalLlmRuntimeCallResult unload = await Task.Run(
                        () => runtime.UnloadModel(load.ModelHandle),
                        CancellationToken.None).ConfigureAwait(false);
                    if (!unload.Succeeded)
                    {
                        return CreationFailure(
                            LocalLlmProviderCreationStatus.RuntimeFailure,
                            "Creation cancellation could not clean up the loaded model: " + unload.Detail,
                            unload.Status);
                    }
                    return CreationFailure(
                        LocalLlmProviderCreationStatus.Cancelled,
                        "Local LLM provider creation was cancelled and the loaded model was unloaded.",
                        ReachyLlamaNativeContract.StatusCancelled);
                }

                LocalLlmProvider? provider = null;
                try
                {
                    provider = new LocalLlmProvider(
                        runtime,
                        manifest,
                        artifact,
                        profile,
                        load.ModelHandle,
                        ownsRuntime);
                    LocalLlmProviderCreationResult result = new LocalLlmProviderCreationResult(
                        LocalLlmProviderCreationStatus.Created,
                        string.Empty,
                        ReachyLlamaNativeContract.StatusOk,
                        provider);
                    provider = null;
                    return result;
                }
                finally
                {
                    if (provider != null)
                    {
                        await provider.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (ArgumentException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }
            catch (InvalidOperationException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.InvalidConfiguration,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusInvalidArgument);
            }
            catch (DllNotFoundException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.Unavailable,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (EntryPointNotFoundException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.Unavailable,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (BadImageFormatException exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.Unavailable,
                    exception.Message,
                    ReachyLlamaNativeContract.StatusNotFound);
            }
            catch (Exception exception)
            {
                return CreationFailure(
                    LocalLlmProviderCreationStatus.RuntimeFailure,
                    "Unexpected local LLM creation failure: " + exception.Message,
                    ReachyLlamaNativeContract.StatusInternalError);
            }
        }

        private void ValidateConfiguration()
        {
            LocalLlmBehaviorContract.ValidateFrozenBytes();
            LocalLlmBehaviorContract.ValidateSelectedInputs(manifest, artifact);
            profile.ValidateAgainst(manifest);
            if (string.IsNullOrWhiteSpace(artifact.FullPath) ||
                artifact.FullPath.Contains('\0') ||
                !Path.IsPathRooted(artifact.FullPath))
            {
                throw new ArgumentException(
                    "The approved local-model artifact path is not an absolute safe path.",
                    nameof(artifact));
            }
        }

        private static LocalLlmProviderCreationResult CreationFailure(
            LocalLlmProviderCreationStatus status,
            string detail,
            int nativeStatus)
        {
            return new LocalLlmProviderCreationResult(
                status,
                detail,
                nativeStatus,
                null);
        }

        private static LocalLlmReloadResult RuntimeReloadFailure(
            string detail,
            int nativeStatus)
        {
            return new LocalLlmReloadResult(
                LocalLlmReloadStatus.RuntimeFailure,
                detail,
                nativeStatus);
        }

    }
}
