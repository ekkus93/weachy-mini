#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"


def read(name: str) -> str:
    return (APP / name).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> None:
    orchestrator = read("ReachyConversationTurnOrchestrator.cs")
    asr = read("ReachyConversationTurnOrchestrator.Asr.cs")
    tts = read("ReachyConversationTurnOrchestrator.Tts.cs")
    stack = read("ReachyAndroidSpeechAudioStack.cs")

    require(
        "ReachyAndroidSpeechAudioStackFactory.CreateOfflineDefaultAsync" in orchestrator,
        "shipping conversation must create the RMA-125 offline audio stack",
    )
    require(
        "RunAsrAsync(\n                    speechStack.Asr" in orchestrator,
        "shipping ASR must use the coordinated stack leg",
    )
    require(
        "RunTtsAsync(\n                    speechStack.Tts" in orchestrator,
        "shipping TTS must use the same coordinated stack",
    )
    require(
        "new SpeechAudioFocusCoordinator(" in stack
        and "new AudioCoordinatedAsrProvider(" in stack
        and "new AudioCoordinatedTtsProvider(" in stack,
        "offline stack must share one focus coordinator across ASR/TTS",
    )
    require(
        "ReachyAndroidOnDeviceAsrProviderFactory.Create" in stack
        and "ReachyAndroidOfflineTtsProviderFactory.Create" in stack,
        "offline stack must retain explicit on-device ASR and offline TTS",
    )
    require(
        "ReachyAndroidSystemAsrProviderFactory.Create" not in stack
        and "ReachyAndroidSystemTtsProviderFactory.Create" not in stack,
        "shipping offline stack must not substitute network-capable speech providers",
    )
    require(
        "ReachyAndroidOnDeviceAsrProviderFactory.Create" not in asr
        and "ReachyAndroidOfflineTtsProviderFactory.Create" not in tts,
        "orchestrator legs must not bypass the shared focus stack",
    )
    require(
        "SelectExactOfflineVoice" in tts,
        "shipping TTS must retain exact installed offline voice selection",
    )
    composition = read("ReachySettingsApplicationCompositionProvider.cs")
    require(
        "screen.ConfigureConversationTurn(conversationTurnOrchestrator.StartTurnAsync);\n"
        "            // The audio service may have completed" in composition
        and "ReportSpeechSetupIfUnavailable();" in composition,
        "initial audio-service setup failures must remain visible after screen binding",
    )
    require(
        "if (eventArgs.Health.Kind == ReachyServiceKind.Audio)" in composition
        and "UpdateMainScreenCapabilities();\n"
        "                ReportSpeechSetupIfUnavailable();" in composition,
        "subsequent audio-health failures must surface setup guidance",
    )
    require(
        'stateStore.ReportUnavailableAction("Speech setup", health.Message);' in composition
        and "ReachyServiceState.Unavailable" in composition
        and "ReachyServiceState.Degraded" in composition
        and "ReachyServiceState.Faulted" in composition,
        "speech setup guidance must surface all failed local-capability states",
    )
    require(
        '"Speech setup unavailable:", StringComparison.Ordinal' in composition
        and "stateStore.Current.InteractionState == ReachyInteractionState.Unavailable" in composition
        and "ReachyInteractionState.Idle" in composition,
        "speech recovery must clear only its own stale setup notice",
    )
    print("RMA-125 shipping audio-focus integration contract passed")


if __name__ == "__main__":
    main()
