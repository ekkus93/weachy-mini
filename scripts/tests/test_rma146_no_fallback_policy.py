import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
POLICY_FILES = [
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderFallbackPolicyContracts.cs",
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderFallbackPolicyEngine.cs",
]
SELECTIONS = (
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyAuthorizedProviderSelectionExtensions.cs"
)
MANAGED = ROOT / "managed/ReachyMini.Core.Tests/Rma146ProviderFallbackPolicyContractTests.cs"


class Rma146NoFallbackPolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = "\n".join(path.read_text(encoding="utf-8") for path in POLICY_FILES)

    def test_default_policy_denies_every_automatic_fallback_mode(self) -> None:
        for token in (
            'DefaultPolicyName = "no-fallback"',
            "allowLocalQualityReduction: false",
            "allowSameProviderRetry: false",
            "allowCrossProviderSwitch: false",
            "allowNetworkProviderSwitch: false",
            "Array.Empty<string>()",
        ):
            self.assertIn(token, self.source)

    def test_asr_tts_llm_vlm_policies_are_independent(self) -> None:
        for workload in ("Asr", "Tts", "Llm", "Vlm"):
            self.assertIn(f"{workload} =", self.source)
        self.assertIn("private readonly ReachyFallbackPolicy[] policies", self.source)
        self.assertIn("policies[(int)workload] = policy", self.source)

    def test_cross_provider_target_must_be_explicitly_authorized(self) -> None:
        self.assertIn("AllowCrossProviderSwitch", self.source)
        self.assertIn("IsTargetAuthorized(request.Target.ProviderId)", self.source)
        self.assertIn('"target-provider-not-authorized"', self.source)
        self.assertIn("MaximumAuthorizedTargets = 32", self.source)

    def test_network_switch_has_separate_gate(self) -> None:
        self.assertIn("AllowNetworkProviderSwitch", self.source)
        self.assertIn("request.TargetsNetworkBoundary", self.source)
        self.assertIn('"network-provider-switch-disabled"', self.source)

    def test_privacy_boundary_change_requires_matching_confirmation(self) -> None:
        self.assertIn("RequiresPrivacyConfirmation", self.source)
        self.assertIn("ConfirmPrivacyBoundaryChange", self.source)
        self.assertIn("MatchesConfirmation(policy, request, confirmation)", self.source)
        self.assertIn('"privacy-boundary-confirmation-required"', self.source)
        self.assertIn("SourceBoundary == request.Source.PrivacyBoundary", self.source)
        self.assertIn("TargetBoundary == request.Target.PrivacyBoundary", self.source)
        self.assertIn("confirmation.ReasonCode", self.source)
        self.assertIn("confirmation.TryConsume()", self.source)

    def test_authorization_is_one_time_and_bound_to_switch(self) -> None:
        self.assertIn("Interlocked.Exchange(ref consumed, 1)", self.source)
        self.assertIn("Provider fallback authorization has already been consumed", self.source)
        self.assertIn("sourceProviderId", self.source)
        self.assertIn("targetProviderId", self.source)

    def test_retry_and_quality_reduction_are_separately_authorized(self) -> None:
        self.assertIn("EvaluateSameProviderRetry", self.source)
        self.assertIn("AllowSameProviderRetry", self.source)
        self.assertIn("EvaluateLocalQualityReduction", self.source)
        self.assertIn("AllowLocalQualityReduction", self.source)
        self.assertIn("ReachyProviderPrivacyBoundary.OnDevice", self.source)

    def test_diagnostics_are_bounded_and_secret_free_by_contract(self) -> None:
        self.assertIn("MaximumDiagnostics = 128", self.source)
        for field in (
            "PolicyName",
            "SourceProviderId",
            "TargetProviderId",
            "ReasonCode",
            "DecisionCode",
        ):
            self.assertIn(field, self.source)
        lowered = self.source.lower()
        self.assertNotIn("credentialreference", lowered)
        self.assertNotIn("secretstore", lowered)
        self.assertNotIn("authorization: bearer", lowered)

    def test_automatic_fallback_selection_requires_one_time_authorization(self) -> None:
        selections = SELECTIONS.read_text(encoding="utf-8")
        self.assertIn("SelectFallback", selections)
        self.assertIn("authorization.Consume", selections)
        self.assertIn("ReachyProviderWorkloadKind.Asr", selections)
        self.assertIn("ReachyProviderWorkloadKind.Tts", selections)
        self.assertIn("ReachyProviderWorkloadKind.Vlm", selections)
        self.assertIn("selection.Current", selections)
        self.assertIn("sourceProvider.InstanceId", selections)
        self.assertNotIn("AutomaticProviderFallbackEnabled = true", self.source)

    def test_managed_mock_failure_never_activates_unauthorized_provider(self) -> None:
        managed = MANAGED.read_text(encoding="utf-8")
        self.assertIn("MockFailureCannotActivateUnauthorizedProvider", managed)
        self.assertIn("ReachyFallbackDecisionStatus.Denied", managed)
        self.assertIn("selection.Current.ProviderInstanceId", managed)
        self.assertIn("AuthorizedSameBoundaryFallbackConsumesOneTimeToken", managed)
        self.assertIn("PrivacyBoundaryChangeRequiresMatchingConfirmation", managed)


if __name__ == "__main__":
    unittest.main()
