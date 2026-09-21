from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROVIDER = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyLocalLlmProviderApplicationService.cs"
SETTINGS_COMPOSITION = (
    ROOT / "Assets/ReachyMini/Runtime/Application/ReachySettingsApplicationCompositionProvider.cs"
)
DEAD_COMPOSITION = (
    ROOT / "Assets/ReachyMini/Runtime/Application/ReachyProductionApplicationCompositionProvider.cs"
)


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(f"missing {label}: {needle}")


def main() -> None:
    if DEAD_COMPOSITION.exists():
        raise AssertionError(
            "RMA-195 closure requires the dead production composition provider to stay removed"
        )

    provider = PROVIDER.read_text(encoding="utf-8")
    settings = SETTINGS_COMPOSITION.read_text(encoding="utf-8")

    require(
        provider,
        "IReachyProviderGovernorDiagnosticsSource",
        "live provider governor diagnostics capability",
    )
    require(
        provider,
        "LocalLlmGovernorDiagnosticsSnapshot.Create",
        "privacy-safe governor diagnostic snapshot",
    )
    require(
        settings,
        "new ReachyLocalLlmProviderApplicationService",
        "shipping provider composition",
    )
    require(
        settings,
        "new ReachyDiagnosticsScreenSource",
        "shipping diagnostics source",
    )
    for forbidden in ("LocalLlmGenerationRequest", "LocalLlmChatMessage", "ResponseJson"):
        if forbidden in settings:
            raise AssertionError(
                f"main-screen composition diagnostics must not access {forbidden}"
            )
    print("RMA-135 shipping governor-wiring contracts passed.")


if __name__ == "__main__":
    main()
