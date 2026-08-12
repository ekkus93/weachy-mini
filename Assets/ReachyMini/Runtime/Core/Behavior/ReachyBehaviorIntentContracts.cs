#nullable enable

using System;

namespace ReachyMini.Behavior
{
    public static class ReachyBehaviorIntentPolicy
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaximumSpeechCharacters = 160;
        public const int MaximumEntityIdCharacters = 64;
        public const int MaximumDiagnosticCharacters = 384;
        public const int MaximumTopLevelProperties = 7;
        public const int MaximumGazeTargetProperties = 2;
        public const int MaximumTimingProperties = 2;
        public const int MaximumStartDelayMilliseconds = 5_000;
        public const int MinimumMaximumDurationMilliseconds = 1;
        public const int MaximumDurationMilliseconds = 30_000;
        public const int MaximumRegenerationAttempts = 1;
    }

    public enum ReachyBehaviorExpression
    {
        Neutral = 0,
        Attentive = 1,
        Curious = 2,
        Pleased = 3,
        Concerned = 4,
        Surprised = 5,
    }

    public enum ReachyBehaviorGesture
    {
        None = 0,
        Nod = 1,
        SmallHeadTilt = 2,
        Recoil = 3,
    }

    public enum ReachyBehaviorUrgency
    {
        Low = 0,
        Normal = 1,
        High = 2,
    }

    public sealed class ReachyBehaviorGazeTarget
    {
        internal ReachyBehaviorGazeTarget(string entityId)
        {
            Kind = "tracked_entity";
            EntityId = entityId;
        }

        public string Kind { get; }

        public string EntityId { get; }
    }

    public sealed class ReachyBehaviorTimingConstraints
    {
        internal ReachyBehaviorTimingConstraints(
            int? startDelayMilliseconds,
            int? maximumDurationMilliseconds)
        {
            StartDelayMilliseconds = startDelayMilliseconds;
            MaximumDurationMilliseconds = maximumDurationMilliseconds;
        }

        public int? StartDelayMilliseconds { get; }

        public int? MaximumDurationMilliseconds { get; }
    }

    public sealed class ReachyBehaviorIntent
    {
        internal ReachyBehaviorIntent(
            string? speech,
            ReachyBehaviorGazeTarget? gazeTarget,
            ReachyBehaviorExpression? expression,
            ReachyBehaviorGesture? gesture,
            ReachyBehaviorUrgency? urgency,
            ReachyBehaviorTimingConstraints? timing)
        {
            SchemaVersion = ReachyBehaviorIntentPolicy.CurrentSchemaVersion;
            Speech = speech;
            GazeTarget = gazeTarget;
            Expression = expression;
            Gesture = gesture;
            Urgency = urgency;
            Timing = timing;
        }

        public int SchemaVersion { get; }

        public string? Speech { get; }

        public ReachyBehaviorGazeTarget? GazeTarget { get; }

        public ReachyBehaviorExpression? Expression { get; }

        public ReachyBehaviorGesture? Gesture { get; }

        public ReachyBehaviorUrgency? Urgency { get; }

        public ReachyBehaviorTimingConstraints? Timing { get; }
    }

    public enum ReachyBehaviorIntentValidationStatus
    {
        Valid = 0,
        InvalidJson = 1,
        UnsupportedSchemaVersion = 2,
        UnknownProperty = 3,
        InvalidValue = 4,
        BoundExceeded = 5,
    }

    public sealed class ReachyBehaviorIntentValidationResult
    {
        internal ReachyBehaviorIntentValidationResult(
            ReachyBehaviorIntentValidationStatus status,
            string diagnosticCode,
            string detail,
            ReachyBehaviorIntent? intent)
        {
            Status = status;
            DiagnosticCode = diagnosticCode;
            Detail = BoundDiagnostic(detail);
            Intent = intent;
        }

        public ReachyBehaviorIntentValidationStatus Status { get; }

        public bool Succeeded =>
            Status == ReachyBehaviorIntentValidationStatus.Valid && Intent != null;

        public string DiagnosticCode { get; }

        public string Detail { get; }

        public ReachyBehaviorIntent? Intent { get; }

        private static string BoundDiagnostic(string? detail)
        {
            string text = detail ?? string.Empty;
            if (text.Length <= ReachyBehaviorIntentPolicy.MaximumDiagnosticCharacters)
            {
                return text;
            }
            return text.Substring(
                0,
                ReachyBehaviorIntentPolicy.MaximumDiagnosticCharacters);
        }
    }

    public enum ReachyBehaviorIntentRecoveryAction
    {
        Reject = 0,
        Regenerate = 1,
    }

    public sealed class ReachyBehaviorIntentRecoveryDecision
    {
        internal ReachyBehaviorIntentRecoveryDecision(
            ReachyBehaviorIntentRecoveryAction action,
            string diagnosticCode)
        {
            Action = action;
            DiagnosticCode = diagnosticCode;
        }

        public ReachyBehaviorIntentRecoveryAction Action { get; }

        public string DiagnosticCode { get; }
    }

    public static class ReachyBehaviorIntentRecoveryPolicy
    {
        public static ReachyBehaviorIntentRecoveryDecision Evaluate(
            ReachyBehaviorIntentValidationResult validation,
            bool sameProviderRetryAuthorized,
            int regenerationAttemptsAlreadyUsed)
        {
            if (validation == null)
            {
                throw new ArgumentNullException(nameof(validation));
            }
            if (validation.Succeeded)
            {
                throw new ArgumentException(
                    "Recovery policy only applies after behavior-intent validation fails.",
                    nameof(validation));
            }
            if (regenerationAttemptsAlreadyUsed < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(regenerationAttemptsAlreadyUsed));
            }

            if (sameProviderRetryAuthorized &&
                regenerationAttemptsAlreadyUsed <
                    ReachyBehaviorIntentPolicy.MaximumRegenerationAttempts)
            {
                return new ReachyBehaviorIntentRecoveryDecision(
                    ReachyBehaviorIntentRecoveryAction.Regenerate,
                    "invalid-intent-regeneration-authorized");
            }

            return new ReachyBehaviorIntentRecoveryDecision(
                ReachyBehaviorIntentRecoveryAction.Reject,
                sameProviderRetryAuthorized
                    ? "invalid-intent-regeneration-limit-reached"
                    : "invalid-intent-retry-not-authorized");
        }
    }
}
