from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DIAGNOSTICS = (
    ROOT / "Assets/ReachyMini/Runtime/Core/LocalModels/LocalLlmGovernorDiagnostics.cs"
)


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(f"missing {label}: {needle}")


def forbid(text: str, needle: str, label: str) -> None:
    if needle in text:
        raise AssertionError(f"forbidden {label}: {needle}")


def main() -> None:
    text = DIAGNOSTICS.read_text(encoding="utf-8")
    require(text, "LocalLlmGovernorPresentationState.Throttled", "throttled UI state")
    require(text, "LocalLlmGovernorPresentationState.Suspended", "suspended UI state")
    require(text, "thermal telemetry unavailable", "thermal-unavailable wording")
    require(text, "physics timing budget exceeded", "physics-budget wording")
    require(text, "ctx={0},batch={1},ubatch={2},threads={3}/{4}", "effective profile")
    forbid(text, "LocalLlmGenerationRequest", "request access in diagnostics")
    forbid(text, "LocalLlmChatMessage", "conversation access in diagnostics")
    forbid(text, "BehaviorIntent", "generated intent access in diagnostics")
    forbid(text, "ResponseJson", "generated response access in diagnostics")
    print("RMA-135 diagnostic privacy/static contracts passed.")


if __name__ == "__main__":
    main()
