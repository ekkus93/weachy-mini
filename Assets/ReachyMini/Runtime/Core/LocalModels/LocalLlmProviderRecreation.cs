#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public enum LocalLlmProviderRecreationStatus
    {
        NotRequired = 0,
        Recreated = 1,
        Suspended = 2,
        CreationFailed = 3,
        SignalFailure = 4,
    }

    public sealed class LocalLlmProviderRecreationResult
    {
        internal LocalLlmProviderRecreationResult(
            LocalLlmProviderRecreationStatus status,
            LocalLlmProvider? provider,
            LocalLlmGovernorDecision? decision,
            string detail)
        {
            Status = status;
            Provider = provider;
            Decision = decision;
            Detail = LocalLlmGenerationResult.BoundDiagnostic(detail);
        }

        public LocalLlmProviderRecreationStatus Status { get; }

        /// <summary>
        /// The provider the caller must use from now on, or null when no usable provider
        /// remains. On <see cref="LocalLlmProviderRecreationStatus.CreationFailed"/> the
        /// previously loaded provider has already been released and must not be reused.
        /// </summary>
        public LocalLlmProvider? Provider { get; }

        public LocalLlmGovernorDecision? Decision { get; }
        public string Detail { get; }

        public bool Succeeded =>
            (Status == LocalLlmProviderRecreationStatus.NotRequired ||
                Status == LocalLlmProviderRecreationStatus.Recreated) &&
            Provider != null;
    }

    /// <summary>
    /// Explicit provider recreation for a shrunken resource envelope.
    ///
    /// RMA-135 keeps the loaded provider execution profile immutable: when a later
    /// observation only permits a smaller envelope, generation is denied rather than
    /// silently downgraded. Without a way to act on that denial the provider is refused
    /// forever, because <c>ProfileFitsWithin</c> compares the loaded profile against an
    /// allowed profile that can no longer contain it. Loading the model artifact can itself
    /// consume enough memory to trigger that state, so a device can admit at Nominal and
    /// then permanently refuse the very provider it just loaded.
    ///
    /// This is the explicit, caller-invoked recreation the specification requires. It is
    /// deliberately NOT wired into the generation path: nothing here retries a cancelled
    /// request, substitutes a model or provider, reaches the network, or mutates a live
    /// provider. It releases the loaded provider and creates a new one from the same
    /// manifest and the same approved artifact at the profile the governor currently allows.
    /// </summary>
    public static class LocalLlmProviderRecreation
    {
        public static async Task<LocalLlmProviderRecreationResult> ForCurrentEnvelopeAsync(
            LocalLlmProvider loadedProvider,
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact,
            LocalLlmExecutionProfile baselineProfile,
            LocalLlmResourceGovernor governor,
            ILocalLlmResourceSignalSource resourceSignals,
            ILocalLlmPhysicsBudgetSource physicsBudget,
            CancellationToken cancellationToken)
        {
            if (loadedProvider == null)
            {
                throw new ArgumentNullException(nameof(loadedProvider));
            }
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            if (baselineProfile == null)
            {
                throw new ArgumentNullException(nameof(baselineProfile));
            }
            if (governor == null)
            {
                throw new ArgumentNullException(nameof(governor));
            }
            if (resourceSignals == null)
            {
                throw new ArgumentNullException(nameof(resourceSignals));
            }
            if (physicsBudget == null)
            {
                throw new ArgumentNullException(nameof(physicsBudget));
            }

            LocalLlmProviderAdmissionResult admission =
                LocalLlmGovernedGenerationCoordinator.EvaluateAdmission(
                    baselineProfile,
                    governor,
                    resourceSignals,
                    physicsBudget);

            if (admission.Status == LocalLlmProviderAdmissionStatus.SignalFailure)
            {
                return new LocalLlmProviderRecreationResult(
                    LocalLlmProviderRecreationStatus.SignalFailure,
                    loadedProvider,
                    admission.Decision,
                    admission.Detail);
            }

            LocalLlmExecutionProfile? allowed = admission.EffectiveProfile;
            if (!admission.Succeeded || allowed == null)
            {
                // Suspended is not a recreation condition. No profile is safe to run, so the
                // loaded provider is left intact for the caller to release or retry later.
                return new LocalLlmProviderRecreationResult(
                    LocalLlmProviderRecreationStatus.Suspended,
                    loadedProvider,
                    admission.Decision,
                    "Local inference is suspended; no execution profile may be recreated. " +
                    admission.Detail);
            }

            if (LocalLlmGovernedGenerationCoordinator.ProfileFitsWithin(
                loadedProvider.ExecutionProfile,
                allowed))
            {
                return new LocalLlmProviderRecreationResult(
                    LocalLlmProviderRecreationStatus.NotRequired,
                    loadedProvider,
                    admission.Decision,
                    "The loaded execution profile still fits within the allowed envelope.");
            }

            // Release before creating. The shrunken envelope is frequently a memory
            // condition caused by this very model, so holding both the outgoing and the
            // incoming provider would require twice the artifact footprint at exactly the
            // moment the device has least to spare. If creation then fails the caller is
            // told through CreationFailed with a null provider rather than being handed a
            // disposed one.
            await loadedProvider.DisposeAsync().ConfigureAwait(false);

            LocalLlmProviderCreationResult creation = await LocalLlmProvider.CreateAsync(
                manifest,
                artifact,
                allowed,
                cancellationToken).ConfigureAwait(false);

            if (!creation.Succeeded || creation.Provider == null)
            {
                return new LocalLlmProviderRecreationResult(
                    LocalLlmProviderRecreationStatus.CreationFailed,
                    null,
                    admission.Decision,
                    "Explicit provider recreation failed: status=" + creation.Status +
                    " native=" + creation.NativeStatus + " detail=" + creation.Detail);
            }

            return new LocalLlmProviderRecreationResult(
                LocalLlmProviderRecreationStatus.Recreated,
                creation.Provider,
                admission.Decision,
                "Recreated the local provider at the currently allowed execution profile.");
        }
    }
}
