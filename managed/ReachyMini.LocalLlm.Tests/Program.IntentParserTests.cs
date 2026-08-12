#nullable enable

using System;
using ReachyMini.Behavior;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private static void TestStrictIntentParser()
    {
        Require(
            LocalLlmBehaviorContract.TryParseIntent(
                ValidIntent,
                out ReachyBehaviorIntent? parsed,
                out string validDetail),
            "Valid behavior intent was rejected: " + validDetail);
        ReachyBehaviorIntent valid = parsed ??
            throw new InvalidOperationException("Valid behavior intent returned null.");
        Require(valid.SchemaVersion == 1, "Behavior schema version changed.");
        Require(valid.Expression == ReachyBehaviorExpression.Attentive, "Expression parse changed.");
        Require(valid.Gesture == ReachyBehaviorGesture.Nod, "Gesture parse changed.");
        Require(valid.Urgency == ReachyBehaviorUrgency.Normal, "Urgency parse changed.");

        const string withGaze =
            "{\"schema_version\":1,\"speech\":\"Looking.\",\"gaze_target\":{\"kind\":\"tracked_entity\",\"entity_id\":\"entity-12\"},\"expression\":\"curious\",\"gesture\":\"small_head_tilt\",\"urgency\":\"low\"}";
        Require(
            LocalLlmBehaviorContract.TryParseIntent(
                withGaze,
                out ReachyBehaviorIntent? gaze,
                out string gazeDetail),
            "Valid gaze intent was rejected: " + gazeDetail);
        Require(gaze?.GazeTarget?.EntityId == "entity-12", "Tracked gaze identity changed.");

        string[] invalid =
        {
            "```json\n" + ValidIntent + "\n```",
            "<think>hidden</think>" + ValidIntent,
            ValidIntent + " trailing",
            ValidIntent + ValidIntent,
            "{\"schema_version\":\"1\",\"speech\":\"Hello.\",\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\"}",
            "{\"schema_version\":1,\"speech\":\"Hello.\",\"joint_angle\":1,\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\"}",
            "{\"schema_version\":1,\"speech\":\"Hello.\",\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\",\"torque\":1}",
            "{\"schema_version\":1,\"speech\":\"Hello.\",\"gaze_target\":{\"kind\":\"tracked_entity\",\"entity_id\":\"thing-12\"},\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\"}",
        };
        for (int index = 0; index < invalid.Length; ++index)
        {
            Require(
                !LocalLlmBehaviorContract.TryParseIntent(invalid[index], out _, out _),
                "Unsafe or repaired behavior intent was accepted at fixture " + index + ".");
        }
    }
}
