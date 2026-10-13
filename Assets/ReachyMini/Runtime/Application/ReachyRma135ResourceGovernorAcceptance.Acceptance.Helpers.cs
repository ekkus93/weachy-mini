#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.Interop;
using ReachyMini.LocalModels;
using ReachyMini.Rendering;
using ReachyMini.Simulation;
using UnityEngine;

namespace ReachyMini.Validation
{
    internal sealed partial class ReachyRma135ResourceGovernorAcceptance
    {
        private sealed class Rma135PhysicsStartup
        {
            public Rma135PhysicsStartup(
                ReachyProductionAuthoritativeRuntime runtime,
                ReachySimulationLocalLlmPhysicsBudgetSource budgetSource,
                Rma135PhysicsStartupStabilization stabilization,
                ReachySimulationTimingSnapshot initialTiming)
            {
                Runtime = runtime;
                BudgetSource = budgetSource;
                Stabilization = stabilization;
                InitialTiming = initialTiming;
            }

            public ReachyProductionAuthoritativeRuntime Runtime { get; }

            public ReachySimulationLocalLlmPhysicsBudgetSource BudgetSource { get; }

            public Rma135PhysicsStartupStabilization Stabilization { get; }

            public ReachySimulationTimingSnapshot InitialTiming { get; }
        }

        private static async Task<Rma135PhysicsStartup> StartProductionPhysicsAsync()
        {
            WriteCheckpoint(
                "production_physics_runtime_started",
                "Locating the live ReachyProductionAuthoritativeRuntime; the acceptance will not create a second MuJoCo worker.");
            ReachyProductionAuthoritativeRuntime productionRuntime =
                await WaitForProductionRuntimeAsync().ConfigureAwait(true);
            var realPhysics = new ReachySimulationLocalLlmPhysicsBudgetSource(
                productionRuntime);
            Rma135PhysicsStartupStabilization startupPhysics =
                await WaitForPhysicsBudgetAsync(realPhysics).ConfigureAwait(true);
            ReachySimulationTimingSnapshot workerBeforeAdmission =
                await WaitForTimingProgressAsync(
                    productionRuntime,
                    startupPhysics.MinimumObservedStepCount).ConfigureAwait(true);
            WriteCheckpoint(
                "production_physics_runtime_ready",
                "state=" + startupPhysics.State +
                " observations=" + startupPhysics.Observations.ToString(
                    CultureInfo.InvariantCulture) +
                " exceeded_observations=" + startupPhysics.ExceededObservations.ToString(
                    CultureInfo.InvariantCulture) +
                " steps=" + workerBeforeAdmission.TotalStepCount.ToString(
                    CultureInfo.InvariantCulture));
            return new Rma135PhysicsStartup(
                productionRuntime,
                realPhysics,
                startupPhysics,
                workerBeforeAdmission);
        }

        private static async Task<(string ModelPath, Rma135ArtifactVerification Artifact, uint NativeAbi)>
            VerifyStagedArtifactAsync()
        {
            string modelPath = Path.Combine(
                UnityEngine.Application.persistentDataPath,
                ModelFileName);
            WriteCheckpoint(
                "artifact_verification_started",
                "Verifying exact RMA-133 selected GGUF artifact.");
            Rma135ArtifactVerification artifact = await Task.Run(
                () => VerifyArtifact(modelPath)).ConfigureAwait(true);
            WriteCheckpoint(
                "artifact_verified",
                "bytes=" + artifact.bytes.ToString(CultureInfo.InvariantCulture) +
                " sha256=" + artifact.sha256);

            uint nativeAbi = NativeReachyLlama.AbiVersion();
            if (nativeAbi != 2U)
            {
                throw new InvalidOperationException(
                    "RMA-135 physical acceptance loaded reachy_llama ABI " +
                    nativeAbi + " instead of ABI 2.");
            }
            WriteCheckpoint("abi_verified", "reachy_llama ABI=2.");
            return (modelPath, artifact, nativeAbi);
        }

        private static async Task<ReachyProductionAuthoritativeRuntime> WaitForProductionRuntimeAsync()
        {
            for (int attempt = 0; attempt < 250; ++attempt)
            {
                ReachyProductionAuthoritativeRuntime? runtime =
                    UnityEngine.Object.FindAnyObjectByType<ReachyProductionAuthoritativeRuntime>();
                if (runtime != null)
                {
                    if (runtime.Status == ReachyProductionRuntimeStatus.Faulted)
                    {
                        throw new InvalidOperationException(
                            "The production authoritative runtime faulted before RMA-135 acceptance: " +
                            runtime.Fault);
                    }
                    if (runtime.Status == ReachyProductionRuntimeStatus.Running &&
                        runtime.SimulationRunState == ReachySimulationRunState.Running)
                    {
                        return runtime;
                    }
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new TimeoutException(
                "Timed out waiting for the app's production authoritative simulation runtime.");
        }

        private static async Task<Rma135PhysicsStartupStabilization> WaitForPhysicsBudgetAsync(
            ReachySimulationLocalLlmPhysicsBudgetSource source)
        {
            const int requiredConsecutiveAdmissible = 3;
            int consecutiveAdmissible = 0;
            int exceededObservations = 0;
            ulong minimumObservedStepCount = 5UL;
            for (int attempt = 1; attempt <= 100; ++attempt)
            {
                LocalLlmPhysicsBudgetState state = source.Capture();
                if (state == LocalLlmPhysicsBudgetState.Exceeded)
                {
                    ++exceededObservations;
                    consecutiveAdmissible = 0;
                }
                else if (state == LocalLlmPhysicsBudgetState.Healthy ||
                    state == LocalLlmPhysicsBudgetState.AtRisk)
                {
                    ++consecutiveAdmissible;
                    if (consecutiveAdmissible >= requiredConsecutiveAdmissible)
                    {
                        return new Rma135PhysicsStartupStabilization(
                            state,
                            attempt,
                            exceededObservations,
                            minimumObservedStepCount);
                    }
                }
                else
                {
                    consecutiveAdmissible = 0;
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new InvalidOperationException(
                "The app's production authoritative simulation did not provide three consecutive admissible timing observations before local LLM acceptance; exceeded_observations=" +
                exceededObservations.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static async Task<ReachySimulationTimingSnapshot> WaitForTimingProgressAsync(
            IReachySimulationTimingSource timingSource,
            ulong minimumStepCount)
        {
            for (int attempt = 0; attempt < 250; ++attempt)
            {
                if (timingSource.SimulationRunState == ReachySimulationRunState.Faulted)
                {
                    throw new InvalidOperationException(
                        "The production authoritative simulation worker faulted during RMA-135 acceptance.");
                }
                if (timingSource.TryGetLatestTimingSnapshot(
                    out ReachySimulationTimingSnapshot timing) &&
                    timing.TotalStepCount >= minimumStepCount)
                {
                    return timing;
                }
                await Task.Delay(20).ConfigureAwait(true);
            }
            throw new TimeoutException(
                "Timed out waiting for production authoritative simulation progress to step " +
                minimumStepCount.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static void ValidateSuccessfulGeneration(
            LocalLlmGovernedGenerationResult result,
            Rma135CollectingSink sink,
            string label)
        {
            if (!result.Succeeded || result.ProviderResult?.Intent == null)
            {
                throw new InvalidOperationException(
                    label + " did not produce a validated intent: governed_status=" + result.Status +
                    " provider_status=" + (result.ProviderResult?.Status.ToString() ?? "none") +
                    " detail=" + result.Detail);
            }
            if (!sink.TerminalValidated || sink.TextEventCount <= 0)
            {
                throw new InvalidOperationException(
                    label + " did not demonstrate ordered streaming followed by validated completion.");
            }
            if (sink.SawTrustedPartialOutput)
            {
                throw new InvalidOperationException(
                    label + " marked partial generated text as trusted/executable.");
            }
        }

        private static Rma135ArtifactVerification VerifyArtifact(string modelPath)
        {
            FileInfo file = new FileInfo(modelPath);
            if (!file.Exists)
            {
                throw new FileNotFoundException(
                    "The exact RMA-133 selected model was not staged for RMA-135 acceptance.",
                    modelPath);
            }
            if (file.Length != LocalLlmBehaviorContract.ArtifactBytes)
            {
                throw new InvalidOperationException(
                    "RMA-135 staged model size mismatch: expected " +
                    LocalLlmBehaviorContract.ArtifactBytes + ", received " + file.Length + ".");
            }
            string hash;
            using (FileStream stream = new FileStream(
                modelPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; ++index)
                {
                    builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                hash = builder.ToString();
            }
            if (!string.Equals(
                hash,
                LocalLlmBehaviorContract.ArtifactSha256,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "RMA-135 staged model SHA-256 mismatch: " + hash + ".");
            }
            return new Rma135ArtifactVerification { bytes = file.Length, sha256 = hash };
        }

        private static LocalLlmGenerationRequest CreateRequest(string requestId, string prompt)
        {
            return new LocalLlmGenerationRequest(
                requestId,
                new[] { new LocalLlmChatMessage(LocalLlmChatRole.User, prompt) });
        }
    }
}
