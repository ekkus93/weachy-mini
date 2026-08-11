import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROVIDERS = ROOT / "Assets/ReachyMini/Runtime/Core/Providers"
SPEECH = ROOT / "Assets/ReachyMini/Runtime/Core/Speech"
APPLICATION = ROOT / "Assets/ReachyMini/Runtime/Application"


class ProviderSourceSetIntegrityTests(unittest.TestCase):
    def test_secure_provider_consumers_have_their_contract(self) -> None:
        contract = PROVIDERS / "ReachyProviderConfiguration.cs"
        self.assertTrue(contract.is_file())
        source = contract.read_text(encoding="utf-8")
        for symbol in (
            "namespace ReachyMini.Providers",
            "interface IReachyProviderSecretStore",
            "class ReachyProviderProfile",
            "class ReachyProviderHeaderBinding",
            "class ReachyProviderModelBinding",
        ):
            self.assertIn(symbol, source)

        for consumer_name in (
            "ReachyAndroidProviderSecretStore.cs",
            "ReachyProviderProfilePersistence.cs",
        ):
            consumer = APPLICATION / consumer_name
            self.assertTrue(consumer.is_file())
            self.assertIn(
                "using ReachyMini.Providers;",
                consumer.read_text(encoding="utf-8"),
            )

    def test_shared_transport_source_set_is_complete(self) -> None:
        required = {
            "ReachyHttpTransportContracts.cs": "class ReachyHttpTransportRequest",
            "ReachySharedHttpTransport.Core.cs": "partial class ReachySharedHttpTransport",
            "ReachySharedHttpTransport.Read.cs": "CreateRequestMessage",
            "ReachySharedHttpTransport.Helpers.cs": "ZeroingByteArrayContent",
            "ReachyServerSentEventParser.cs": "class ReachyServerSentEventParser",
        }
        for filename, symbol in required.items():
            path = PROVIDERS / filename
            self.assertTrue(path.is_file(), filename)
            self.assertIn(symbol, path.read_text(encoding="utf-8"), filename)

    def test_rma144_asr_source_set_is_complete(self) -> None:
        required = {
            PROVIDERS / "ReachyOpenAiCompatibleAsrOptions.cs":
                "class ReachyOpenAiCompatibleAsrOptions",
            PROVIDERS / "ReachyOpenAiCompatibleAsrProvider.Core.cs":
                "partial class ReachyOpenAiCompatibleAsrProvider",
            PROVIDERS / "ReachyOpenAiCompatibleAsrProvider.Helpers.cs":
                "BuildMultipartBody",
            PROVIDERS / "ReachyAsrTranscriptionProtocolParser.cs":
                "class ReachyAsrTranscriptionProtocolParser",
            PROVIDERS / "ReachyBearerCredentialTransportBinding.cs":
                "class ReachyBearerCredentialTransportBinding",
            SPEECH / "BufferedAsrUtteranceContracts.cs":
                "interface IBufferedAsrUtteranceSource",
        }
        for path, symbol in required.items():
            self.assertTrue(path.is_file(), str(path.relative_to(ROOT)))
            self.assertIn(symbol, path.read_text(encoding="utf-8"), str(path))

    def test_rma145_tts_source_set_is_complete(self) -> None:
        required = {
            PROVIDERS / "ReachyOpenAiCompatibleTtsOptions.cs":
                "class ReachyOpenAiCompatibleTtsOptions",
            PROVIDERS / "ReachyOpenAiCompatibleTtsProvider.Core.cs":
                "partial class ReachyOpenAiCompatibleTtsProvider",
            PROVIDERS / "ReachyOpenAiCompatibleTtsProvider.Helpers.cs":
                "BuildRequestBody",
            SPEECH / "BufferedTtsAudioContracts.cs":
                "interface IBufferedTtsAudioSink",
        }
        for path, symbol in required.items():
            self.assertTrue(path.is_file(), str(path.relative_to(ROOT)))
            self.assertIn(symbol, path.read_text(encoding="utf-8"), str(path))

    def test_rma146_fallback_policy_source_set_is_complete(self) -> None:
        required = {
            PROVIDERS / "ReachyProviderFallbackPolicyContracts.cs":
                "class ReachyFallbackPolicy",
            PROVIDERS / "ReachyProviderFallbackPolicyEngine.cs":
                "class ReachyProviderFallbackPolicyEngine",
            PROVIDERS / "ReachyAuthorizedProviderSelectionExtensions.cs":
                "SelectFallback",
            APPLICATION / "ReachyFallbackPolicyPersistence.cs":
                "class ReachyFallbackPolicyPersistenceStore",
        }
        for path, symbol in required.items():
            self.assertTrue(path.is_file(), str(path.relative_to(ROOT)))
            self.assertIn(symbol, path.read_text(encoding="utf-8"), str(path))

    def test_rma150_conversation_state_machine_source_set_is_complete(self) -> None:
        required = {
            (
                ROOT
                / "Assets/ReachyMini/Runtime/Core/Conversation/ReachyConversationStateContracts.cs"
            ): "enum ReachyConversationState",
            (
                ROOT
                / "Assets/ReachyMini/Runtime/Core/Conversation/ReachyConversationStateMachine.cs"
            ): "class ReachyConversationStateMachine",
            (
                ROOT
                / "managed/ReachyMini.Core.Tests/Rma150ConversationStateMachineContractTests.cs"
            ): "StaleCompletionAfterInterruptIsRejected",
        }
        for path, symbol in required.items():
            self.assertTrue(path.is_file(), str(path.relative_to(ROOT)))
            self.assertIn(symbol, path.read_text(encoding="utf-8"), str(path))


if __name__ == "__main__":
    unittest.main()
