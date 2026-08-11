import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTRACTS = ROOT / "Assets/ReachyMini/Runtime/Core/Conversation/ReachyConversationStateContracts.cs"
MACHINE = ROOT / "Assets/ReachyMini/Runtime/Core/Conversation/ReachyConversationStateMachine.cs"
MANAGED = ROOT / "managed/ReachyMini.Core.Tests/Rma150ConversationStateMachineContractTests.cs"


class Rma150ConversationStateMachineTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.contracts = CONTRACTS.read_text(encoding="utf-8")
        cls.machine = MACHINE.read_text(encoding="utf-8")
        cls.source = cls.contracts + "\n" + cls.machine

    def test_all_required_states_are_explicit(self) -> None:
        for state in (
            "Idle",
            "Listening",
            "Transcribing",
            "Thinking",
            "PreparingSpeech",
            "Speaking",
            "Interrupted",
            "Unavailable",
            "Error",
        ):
            self.assertIn(f"{state} =", self.contracts)

    def test_every_async_stage_has_explicit_operation_identity(self) -> None:
        for field in ("SessionId", "TurnId", "OperationEpoch", "ExpectedState", "Kind"):
            self.assertIn(field, self.contracts)
        self.assertIn("RequireCurrent", self.machine)
        self.assertIn("ReachyStaleConversationCompletionException", self.machine)

    def test_asr_and_tts_stage_overlap_is_forbidden(self) -> None:
        self.assertIn("ValidateNoConflictingAudioSessions", self.machine)
        self.assertIn("ASR and TTS sessions simultaneously", self.machine)
        self.assertIn("operationCancellation != null", self.machine)
        self.assertIn("attempted to overlap active operations", self.machine)

    def test_interruption_cancels_active_operation(self) -> None:
        self.assertIn("Interrupt(string reasonCode)", self.machine)
        self.assertIn("DetachOperation()", self.machine)
        self.assertIn("CancelAndDispose(cancellation)", self.machine)
        self.assertIn("ReachyConversationState.Interrupted", self.machine)
        self.assertIn("cancellation.Cancel()", self.machine)
        self.assertIn("Conversation reason codes cannot exceed 64 characters", self.machine)

    def test_barge_in_policy_is_explicit_and_fail_closed(self) -> None:
        for policy in ("Disabled", "SpeakingOnly", "PreparingOrSpeaking"):
            self.assertIn(f"{policy} =", self.contracts)
        self.assertIn("RequestBargeIn", self.machine)
        self.assertIn('throw InvalidTransition("barge-in")', self.machine)

    def test_error_reset_changes_session_identity(self) -> None:
        self.assertIn("ResetAfterError", self.machine)
        self.assertIn("sessionId = checked(sessionId + 1UL)", self.machine)
        self.assertIn("turnId = 0UL", self.machine)

    def test_no_blocking_or_fire_and_forget_waits(self) -> None:
        self.assertNotIn(".Wait()", self.source)
        self.assertNotIn(".Result", self.source)
        self.assertNotIn("Thread.Sleep", self.source)
        self.assertNotIn("Task.Run", self.source)

    def test_managed_transition_matrix_covers_stale_and_barge_in_paths(self) -> None:
        managed = MANAGED.read_text(encoding="utf-8")
        for token in (
            "HappyPathIsDeterministic",
            "NoMatchReturnsToIdle",
            "StaleCompletionAfterInterruptIsRejected",
            "SpeakingBargeInCancelsPlaybackBeforeNextTurn",
            "ConflictingSessionStartIsRejected",
            "ErrorResetChangesSessionIdentity",
        ):
            self.assertIn(token, managed)
        self.assertIn("CancellationToken.IsCancellationRequested", managed)
        self.assertIn("ReachyStaleConversationCompletionException", managed)


if __name__ == "__main__":
    unittest.main()
