#nullable enable

using System;
using ReachyMini.AppState;
using ReachyMini.Interop;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalLlmProvider : IReachyMemoryPressureParticipant
    {
        private readonly IDisposable memoryPressureRegistration;

        public ReachyMemoryPressureReleaseResult ReleaseForMemoryPressure()
        {
            lock (sync)
            {
                if (state == LocalLlmProviderState.Disposed)
                {
                    return new ReachyMemoryPressureReleaseResult(
                        ReachyMemoryPressureReleaseStatus.NothingToRelease,
                        "The local LLM provider is disposed.");
                }
                if (state == LocalLlmProviderState.Loading ||
                    state == LocalLlmProviderState.Generating ||
                    activeGenerationTask != null)
                {
                    return new ReachyMemoryPressureReleaseResult(
                        ReachyMemoryPressureReleaseStatus.RetainedActiveState,
                        "An active local LLM load/reload or generation was retained to avoid corrupting in-flight state.");
                }
                if (modelHandle == 0UL)
                {
                    return new ReachyMemoryPressureReleaseResult(
                        ReachyMemoryPressureReleaseStatus.NothingToRelease,
                        "No idle local LLM model is loaded.");
                }

                state = LocalLlmProviderState.Loading;
                LocalLlmRuntimeCallResult unload;
                try
                {
                    unload = runtime.UnloadModel(modelHandle);
                }
                catch (Exception exception)
                {
                    state = LocalLlmProviderState.Faulted;
                    return new ReachyMemoryPressureReleaseResult(
                        ReachyMemoryPressureReleaseStatus.Failed,
                        "Local LLM memory-pressure unload failed (" +
                        exception.GetType().Name + ").");
                }

                if (!unload.Succeeded)
                {
                    state = LocalLlmProviderState.Faulted;
                    return new ReachyMemoryPressureReleaseResult(
                        ReachyMemoryPressureReleaseStatus.Failed,
                        "The local LLM runtime rejected memory-pressure unload with status " +
                        unload.Status + ".");
                }

                modelHandle = 0UL;
                state = LocalLlmProviderState.Unavailable;
                return new ReachyMemoryPressureReleaseResult(
                    ReachyMemoryPressureReleaseStatus.Released,
                    "The idle local LLM model was unloaded; explicit reload restores it.");
            }
        }
    }
}
