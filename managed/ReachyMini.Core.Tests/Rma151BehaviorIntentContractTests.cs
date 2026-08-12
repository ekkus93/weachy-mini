#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReachyMini.Behavior;
using ReachyMini.Providers;

namespace ReachyMini.Core.Tests
{
    internal static class Rma151BehaviorIntentContractTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            ExistingLocalIntentSubsetRemainsValid();
            OptionalFieldsAndTimingAreOrderIndependent();
            UnknownUnsafeActionsFailClosed();
            BoundsAndDuplicateFieldsFailClosed();
            HugeTimingIntegerFailsWithoutOverflow();
            InvalidOutputIsNeverRepairedIntoSuccess();
            RegenerationRequiresExplicitRma146Authorization();
        }

        private static void ExistingLocalIntentSubsetRemainsValid()
        {
            const string json =
                "{\"schema_version\":1,\"speech\":\"Looking.\",\"gaze_target\":" +
                "{\"kind\":\"tracked_entity\",\"entity_id\":\"entity-12\"}," +
                "\"expression\":\"curious\",\"gesture\":\"small_head_tilt\"," +
                "\"urgency\":\"normal\"}";

            ReachyBehaviorIntentValidationResult result =
                ReachyBehaviorIntentJsonParser.Validate(json);
            Equal(true, result.Succeeded, "existing local subset validation");
            ReachyBehaviorIntent intent = result.Intent ??
                throw new InvalidOperationException("Valid RMA-151 intent was null.");
            Equal(1, intent.SchemaVersion, "schema version");
            Equal("Looking.", intent.Speech, "speech");
            Equal("entity-12", intent.GazeTarget?.EntityId, "gaze identity");
            Equal<ReachyBehaviorExpression?>(
                ReachyBehaviorExpression.Curious,
                intent.Expression,
                "expression");
            Equal<ReachyBehaviorGesture?>(
                ReachyBehaviorGesture.SmallHeadTilt,
                intent.Gesture,
                "gesture");
            Equal<ReachyBehaviorUrgency?>(
                ReachyBehaviorUrgency.Normal,
                intent.Urgency,
                "urgency");
            Equal(null, intent.Timing, "timing omitted by RMA-134 subset");
        }

        private static void OptionalFieldsAndTimingAreOrderIndependent()
        {
            const string json =
                "{\"timing\":{\"maximum_duration_ms\":1200,\"start_delay_ms\":25}," +
                "\"gesture\":\"nod\",\"schema_version\":1}";

            ReachyBehaviorIntentValidationResult result =
                ReachyBehaviorIntentJsonParser.Validate(json);
            Equal(true, result.Succeeded, "order-independent optional fields");
            ReachyBehaviorIntent intent = result.Intent ??
                throw new InvalidOperationException("Valid timing intent was null.");
            Equal(null, intent.Speech, "optional speech");
            Equal<ReachyBehaviorGesture?>(
                ReachyBehaviorGesture.Nod,
                intent.Gesture,
                "optional gesture");
            Equal<int?>(25, intent.Timing?.StartDelayMilliseconds, "start delay");
            Equal<int?>(
                1200,
                intent.Timing?.MaximumDurationMilliseconds,
                "maximum duration");

            ReachyBehaviorIntentValidationResult minimal =
                ReachyBehaviorIntentJsonParser.Validate("{\"schema_version\":1}");
            Equal(true, minimal.Succeeded, "safe no-op intent");
        }

        private static void UnknownUnsafeActionsFailClosed()
        {
            string[] unsafeJson =
            {
                "{\"schema_version\":1,\"joint_angle\":1}",
                "{\"schema_version\":1,\"torque\":1}",
                "{\"schema_version\":1,\"velocity\":1}",
                "{\"schema_version\":1,\"position\":[1,2,3]}",
                "{\"schema_version\":1,\"gaze_target\":{" +
                    "\"kind\":\"tracked_entity\",\"entity_id\":\"entity-1\"," +
                    "\"yaw_degrees\":20}}",
            };

            for (int index = 0; index < unsafeJson.Length; ++index)
            {
                ReachyBehaviorIntentValidationResult result =
                    ReachyBehaviorIntentJsonParser.Validate(unsafeJson[index]);
                Equal(false, result.Succeeded, "unsafe fixture " + index);
                Equal(null, result.Intent, "unsafe fixture intent " + index);
                Equal(
                    ReachyBehaviorIntentValidationStatus.UnknownProperty,
                    result.Status,
                    "unsafe fixture status " + index);
            }
        }

        private static void BoundsAndDuplicateFieldsFailClosed()
        {
            string longSpeech = new string('x',
                ReachyBehaviorIntentPolicy.MaximumSpeechCharacters + 1);
            string longEntityId = "entity-" + new string(
                '9',
                ReachyBehaviorIntentPolicy.MaximumEntityIdCharacters);
            string[] invalid =
            {
                "{\"schema_version\":2}",
                "{\"schema_version\":1,\"speech\":\"\"}",
                "{\"schema_version\":1,\"speech\":\"" + longSpeech + "\"}",
                "{\"schema_version\":1,\"speech\":\"ok\",\"speech\":\"again\"}",
                "{\"schema_version\":1,\"gaze_target\":{" +
                    "\"kind\":\"tracked_entity\",\"entity_id\":\"" +
                    longEntityId + "\"}}",
                "{\"schema_version\":1,\"timing\":{}}",
                "{\"schema_version\":1,\"timing\":{\"start_delay_ms\":5001}}",
                "{\"schema_version\":1,\"timing\":{\"maximum_duration_ms\":0}}",
                "{\"schema_version\":1,\"speech\":\"bad\\u0000text\"}",
                "```json\n{\"schema_version\":1}\n```",
                "{\"schema_version\":1} trailing",
            };

            for (int index = 0; index < invalid.Length; ++index)
            {
                ReachyBehaviorIntentValidationResult result =
                    ReachyBehaviorIntentJsonParser.Validate(invalid[index]);
                Equal(false, result.Succeeded, "bounded invalid fixture " + index);
                Equal(null, result.Intent, "bounded invalid intent " + index);
            }
        }

        private static void HugeTimingIntegerFailsWithoutOverflow()
        {
            const string json =
                "{\"schema_version\":1,\"timing\":{" +
                "\"maximum_duration_ms\":999999999999999999999999999999999999}}";
            ReachyBehaviorIntentValidationResult result =
                ReachyBehaviorIntentJsonParser.Validate(json);
            Equal(false, result.Succeeded, "huge timing integer");
            Equal(
                ReachyBehaviorIntentValidationStatus.BoundExceeded,
                result.Status,
                "huge timing integer status");
            Equal(null, result.Intent, "huge timing integer intent");
        }

        private static void InvalidOutputIsNeverRepairedIntoSuccess()
        {
            ReachyBehaviorIntentValidationResult invalid =
                ReachyBehaviorIntentJsonParser.Validate(
                    "{\"schema_version\":1,\"motor_command\":\"move\"}");
            Equal(false, invalid.Succeeded, "invalid output validation");
            Equal(null, invalid.Intent, "invalid output has no fabricated intent");

            ReachyBehaviorIntentRecoveryDecision rejected =
                ReachyBehaviorIntentRecoveryPolicy.Evaluate(
                    invalid,
                    sameProviderRetryAuthorized: false,
                    regenerationAttemptsAlreadyUsed: 0);
            Equal(
                ReachyBehaviorIntentRecoveryAction.Reject,
                rejected.Action,
                "default invalid-output recovery");

            ReachyBehaviorIntentRecoveryDecision allowedOnce =
                ReachyBehaviorIntentRecoveryPolicy.Evaluate(
                    invalid,
                    sameProviderRetryAuthorized: true,
                    regenerationAttemptsAlreadyUsed: 0);
            Equal(
                ReachyBehaviorIntentRecoveryAction.Regenerate,
                allowedOnce.Action,
                "authorized regeneration");

            ReachyBehaviorIntentRecoveryDecision bounded =
                ReachyBehaviorIntentRecoveryPolicy.Evaluate(
                    invalid,
                    sameProviderRetryAuthorized: true,
                    regenerationAttemptsAlreadyUsed: 1);
            Equal(
                ReachyBehaviorIntentRecoveryAction.Reject,
                bounded.Action,
                "regeneration attempt bound");
        }

        private static void RegenerationRequiresExplicitRma146Authorization()
        {
            var endpoint = new ReachyProviderEndpointIdentity(
                ReachyProviderWorkloadKind.Llm,
                "llm-primary",
                ReachyProviderPrivacyBoundary.OnDevice);
            var fallback = new ReachyProviderFallbackPolicyEngine();
            ReachyBehaviorIntentValidationResult invalid =
                ReachyBehaviorIntentJsonParser.Validate("not-json");

            ReachyFallbackDecision denied = fallback.EvaluateSameProviderRetry(
                endpoint,
                "invalid-behavior-intent");
            ReachyBehaviorIntentRecoveryDecision deniedRecovery =
                ReachyBehaviorIntentRecoveryPolicy.Evaluate(
                    invalid,
                    denied.Status == ReachyFallbackDecisionStatus.Authorized,
                    regenerationAttemptsAlreadyUsed: 0);
            Equal(
                ReachyBehaviorIntentRecoveryAction.Reject,
                deniedRecovery.Action,
                "default RMA-146 retry gate");

            fallback.SetPolicy(
                ReachyProviderWorkloadKind.Llm,
                new ReachyFallbackPolicy(
                    "llm-intent-regeneration",
                    allowLocalQualityReduction: false,
                    allowSameProviderRetry: true,
                    allowCrossProviderSwitch: false,
                    allowNetworkProviderSwitch: false,
                    Array.Empty<string>()));
            ReachyFallbackDecision authorized = fallback.EvaluateSameProviderRetry(
                endpoint,
                "invalid-behavior-intent");
            ReachyBehaviorIntentRecoveryDecision authorizedRecovery =
                ReachyBehaviorIntentRecoveryPolicy.Evaluate(
                    invalid,
                    authorized.Status == ReachyFallbackDecisionStatus.Authorized,
                    regenerationAttemptsAlreadyUsed: 0);
            Equal(
                ReachyBehaviorIntentRecoveryAction.Regenerate,
                authorizedRecovery.Action,
                "explicit RMA-146 retry gate");
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-151 contract failed for {description}: " +
                    $"expected={expected}; actual={actual}.");
            }
        }
    }
}
