#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EDITOR_TEST = (
    ROOT
    / "Assets/ReachyMini/Tests/Editor"
    / "ReachyRma190IndependentCloudProviderCombinationsTests.cs"
)
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
SPEC = ROOT / "docs/RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md"


class Rma190IndependentProviderCombinationContracts(unittest.TestCase):
    def test_all_workloads_and_all_cloud_local_masks_are_exercised(self) -> None:
        source = EDITOR_TEST.read_text(encoding="utf-8")
        self.assertIn("for (int mask = 0; mask < 16; ++mask)", source)
        for token in (
            "ReachyProviderKind.Asr",
            "ReachyProviderKind.Tts",
            "ReachyProviderKind.Llm",
            "ReachyProviderKind.Vlm",
            "ReachyProviderWorkloadKind.Asr",
            "ReachyProviderWorkloadKind.Tts",
            "ReachyProviderWorkloadKind.Llm",
            "ReachyProviderWorkloadKind.Vlm",
        ):
            self.assertIn(token, source)

    def test_durable_references_profiles_and_policies_are_independent(self) -> None:
        source = EDITOR_TEST.read_text(encoding="utf-8")
        for token in (
            "ReachySettingsPersistenceApplicationService",
            "ReachySettingsStorageReferences",
            "ReachyProviderProfilePersistenceStore",
            "ReachyFallbackPolicyPersistenceStore",
            "ProfilesAndFallbackPoliciesRemainIndependentAcrossReload",
            "ReachyFallbackPolicy.NoFallback()",
            "AssertIndependentState",
        ):
            self.assertIn(token, source)
        self.assertIn("Assert.That(profileStore.Profiles.Count, Is.EqualTo(4));", source)
        self.assertIn("policy.AuthorizedTargetProviderIds.Count", source)

    def test_cloud_truthfulness_and_roadmap_evidence_stay_fail_closed(self) -> None:
        source = EDITOR_TEST.read_text(encoding="utf-8")
        self.assertIn("selection.SendsDataOffDevice", source)
        self.assertIn("snapshot.PrivacyCloudSummary", source)

        todo = TODO.read_text(encoding="utf-8")
        start = todo.index("## RMA-190")
        end = todo.index("## RMA-191", start)
        self.assertIn(
            "- [ ] Independent cloud provider combinations.",
            todo[start:end],
        )

        spec = SPEC.read_text(encoding="utf-8")
        self.assertIn("Independent cloud-provider combinations", spec)
        self.assertIn("all 16 cloud/local selection masks", spec)


if __name__ == "__main__":
    unittest.main()
