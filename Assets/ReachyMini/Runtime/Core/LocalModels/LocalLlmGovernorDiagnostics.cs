#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReachyMini.LocalModels
{
    public enum LocalLlmGovernorPresentationState
    {
        Unavailable = 0,
        Ready = 1,
        Throttled = 2,
        Suspended = 3,
    }

    public sealed class LocalLlmGovernorDiagnosticsSnapshot
    {
        private static readonly KeyValuePair<LocalLlmGovernorReason, string>[] ReasonLabels =
        {
            Pair(LocalLlmGovernorReason.DeviceProfileLimit, "device-profile limit"),
            Pair(LocalLlmGovernorReason.ThermalSignalUnavailable, "thermal telemetry unavailable"),
            Pair(LocalLlmGovernorReason.MemorySignalUnavailable, "memory telemetry unavailable"),
            Pair(LocalLlmGovernorReason.PhysicsSignalUnavailable, "physics timing unavailable"),
            Pair(LocalLlmGovernorReason.ThermalLight, "light thermal pressure"),
            Pair(LocalLlmGovernorReason.ThermalModerate, "moderate thermal pressure"),
            Pair(LocalLlmGovernorReason.ThermalSevereOrWorse, "severe-or-worse thermal pressure"),
            Pair(LocalLlmGovernorReason.MemoryPressure, "memory pressure"),
            Pair(LocalLlmGovernorReason.MemoryCritical, "critical memory pressure"),
            Pair(LocalLlmGovernorReason.AndroidLowMemory, "Android low-memory signal"),
            Pair(LocalLlmGovernorReason.PhysicsAtRisk, "physics timing at risk"),
            Pair(LocalLlmGovernorReason.PhysicsBudgetExceeded, "physics timing budget exceeded"),
            Pair(LocalLlmGovernorReason.RecoveryHold, "recovery hold"),
            Pair(LocalLlmGovernorReason.RecentOutOfMemory, "recent local inference out-of-memory"),
            Pair(LocalLlmGovernorReason.ProfileIncompatible, "execution profile incompatible"),
        };

        private LocalLlmGovernorDiagnosticsSnapshot(
            LocalLlmGovernorPresentationState presentationState,
            LocalLlmGovernorMode? mode,
            LocalLlmDeviceProfileKind? deviceProfile,
            LocalLlmGovernorReason reasons,
            string effectiveProfile,
            string diagnosticLine,
            string userMessage)
        {
            PresentationState = presentationState;
            Mode = mode;
            DeviceProfile = deviceProfile;
            Reasons = reasons;
            EffectiveProfile = effectiveProfile;
            DiagnosticLine = LocalLlmGenerationResult.BoundDiagnostic(diagnosticLine);
            UserMessage = LocalLlmGenerationResult.BoundDiagnostic(userMessage);
        }

        public LocalLlmGovernorPresentationState PresentationState { get; }
        public LocalLlmGovernorMode? Mode { get; }
        public LocalLlmDeviceProfileKind? DeviceProfile { get; }
        public LocalLlmGovernorReason Reasons { get; }
        public string EffectiveProfile { get; }
        public string DiagnosticLine { get; }
        public string UserMessage { get; }
        public bool ThermalTelemetryAvailable =>
            (Reasons & LocalLlmGovernorReason.ThermalSignalUnavailable) == 0;
        public bool InferenceAvailable =>
            PresentationState == LocalLlmGovernorPresentationState.Ready ||
            PresentationState == LocalLlmGovernorPresentationState.Throttled;

        public static LocalLlmGovernorDiagnosticsSnapshot Create(
            LocalLlmGovernorDecision? decision)
        {
            if (decision == null)
            {
                return new LocalLlmGovernorDiagnosticsSnapshot(
                    LocalLlmGovernorPresentationState.Unavailable,
                    null,
                    null,
                    LocalLlmGovernorReason.None,
                    "unavailable",
                    "Local LLM governor: decision unavailable.",
                    "Local LLM resource status is unavailable.");
            }

            LocalLlmGovernorPresentationState presentation = decision.Mode switch
            {
                LocalLlmGovernorMode.Nominal => LocalLlmGovernorPresentationState.Ready,
                LocalLlmGovernorMode.Reduced => LocalLlmGovernorPresentationState.Throttled,
                LocalLlmGovernorMode.Minimal => LocalLlmGovernorPresentationState.Throttled,
                LocalLlmGovernorMode.Suspended => LocalLlmGovernorPresentationState.Suspended,
                _ => throw new ArgumentOutOfRangeException(nameof(decision)),
            };
            string reasons = FormatReasons(decision.Reasons);
            string effectiveProfile = FormatEffectiveProfile(decision.EffectiveProfile);
            string diagnostic = string.Format(
                CultureInfo.InvariantCulture,
                "Local LLM governor: state={0}; mode={1}; device={2}; reasons={3}; effective={4}.",
                presentation,
                decision.Mode,
                decision.DeviceProfile.Kind,
                reasons,
                effectiveProfile);
            string userMessage = presentation switch
            {
                LocalLlmGovernorPresentationState.Ready =>
                    "Local LLM resources are within the current device and physics budget.",
                LocalLlmGovernorPresentationState.Throttled =>
                    "Local LLM is resource-throttled: " + reasons + ".",
                LocalLlmGovernorPresentationState.Suspended =>
                    "Local LLM is unavailable because the resource governor suspended inference: " +
                    reasons + ".",
                _ => throw new ArgumentOutOfRangeException(nameof(decision)),
            };
            return new LocalLlmGovernorDiagnosticsSnapshot(
                presentation,
                decision.Mode,
                decision.DeviceProfile.Kind,
                decision.Reasons,
                effectiveProfile,
                diagnostic,
                userMessage);
        }

        private static string FormatEffectiveProfile(LocalLlmExecutionProfile? profile)
        {
            if (profile == null)
            {
                return "suspended";
            }
            return string.Format(
                CultureInfo.InvariantCulture,
                "ctx={0},batch={1},ubatch={2},threads={3}/{4}",
                profile.ContextTokens,
                profile.BatchTokens,
                profile.MicroBatchTokens,
                profile.Threads,
                profile.BatchThreads);
        }

        private static string FormatReasons(LocalLlmGovernorReason reasons)
        {
            if (reasons == LocalLlmGovernorReason.None)
            {
                return "none";
            }

            var labels = new List<string>();
            LocalLlmGovernorReason known = LocalLlmGovernorReason.None;
            for (int index = 0; index < ReasonLabels.Length; ++index)
            {
                KeyValuePair<LocalLlmGovernorReason, string> entry = ReasonLabels[index];
                known |= entry.Key;
                if ((reasons & entry.Key) != 0)
                {
                    labels.Add(entry.Value);
                }
            }

            LocalLlmGovernorReason unknown = reasons & ~known;
            if (unknown != LocalLlmGovernorReason.None)
            {
                labels.Add("unknown-flags=" + ((int)unknown).ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(", ", labels);
        }

        private static KeyValuePair<LocalLlmGovernorReason, string> Pair(
            LocalLlmGovernorReason reason,
            string label)
        {
            return new KeyValuePair<LocalLlmGovernorReason, string>(reason, label);
        }
    }
}
