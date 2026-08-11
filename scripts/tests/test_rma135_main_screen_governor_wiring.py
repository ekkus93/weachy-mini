from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (
    ROOT / "Assets/ReachyMini/Runtime/Application/ReachyProductionApplicationCompositionProvider.cs"
)


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(f"missing {label}: {needle}")


def main() -> None:
    text = SOURCE.read_text(encoding="utf-8")
    require(
        text, "IReachyProviderGovernorDiagnosticsSource?", "optional provider diagnostics source"
    )
    require(text, "eventArgs.Health.Kind == ReachyServiceKind.Provider", "existing health path")
    require(text, "ReachyProviderGovernorMainScreenProjection.Create", "tested UI projection")
    require(text, "projection.OverrideInteraction", "explicit interaction ownership")
    require(
        text, "providerGovernorDiagnostics.GovernorDiagnostics.DiagnosticLine", "diagnostics output"
    )
    unavailable_start = text.index(
        "internal sealed class ReachyUnavailableProviderApplicationService"
    )
    unavailable_end = text.index(
        "internal sealed class ReachyUnavailablePerceptionApplicationService"
    )
    unavailable = text[unavailable_start:unavailable_end]
    if "IReachyProviderGovernorDiagnosticsSource" in unavailable:
        raise AssertionError("unavailable provider must not masquerade as a live governor source")
    for forbidden in ("LocalLlmGenerationRequest", "LocalLlmChatMessage", "ResponseJson"):
        if forbidden in text:
            raise AssertionError(f"main-screen governor diagnostics must not access {forbidden}")
    print("RMA-135 main-screen governor wiring contracts passed.")


if __name__ == "__main__":
    main()
