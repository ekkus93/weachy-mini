#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTRACTS = ROOT / "Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalModelPackageContracts.cs"
MANAGER = ROOT / "Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalModelPackageManager.cs"
HTTP = ROOT / "Assets/ReachyMini/Runtime/Core/LocalModels/HttpLocalModelDownloadTransport.cs"
SETTINGS = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyMainScreen.cs"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> None:
    contracts = CONTRACTS.read_text(encoding="utf-8")
    manager = MANAGER.read_text(encoding="utf-8")
    http = HTTP.read_text(encoding="utf-8")
    settings = SETTINGS.read_text(encoding="utf-8")

    require("ILocalModelStorageProbe" in contracts, "storage probe contract missing")
    require("ILocalModelDownloadTransport" in contracts, "download transport contract missing")
    require("LocalModelApprovedArtifact" in contracts, "approved artifact type missing")
    require(
        "internal LocalModelApprovedArtifact(" in contracts,
        "approved artifact constructor must not be public",
    )

    require("CheckStorage(" in manager, "storage preflight missing")
    require("artifact.download.part" in manager, "download staging file missing")
    require("artifact.download.meta" in manager, "resume metadata missing")
    require("source_sha256=" in manager, "resume source fingerprint binding missing")
    require("expected_size=" in manager, "resume size binding missing")
    require("artifact_sha256=" in manager, "resume hash binding missing")
    require("File.Move(stagingPath, finalPath)" in manager, "atomic staged publish missing")
    require("ComputeSha256Async" in manager, "SHA-256 verification missing")
    require("ResolveInstalledCoreAsync" in manager, "approved-path resolver missing")
    require("InstalledArtifactCorrupt" in manager, "tampered installed artifact must fail visibly")
    require("RecoverAsync(" in manager, "termination recovery entry point missing")
    require("CleanupOrphansAsync(" in manager, "orphan cleanup entry point missing")
    require("DeleteAsync(" in manager, "exact model deletion missing")
    require(
        "ImportAsync(\n            LocalModelManifest manifest,\n            Stream source" in manager,
        "imports must consume a caller-opened stream rather than arbitrary paths",
    )
    require(
        "string sourcePath" not in manager and "string importPath" not in manager,
        "package manager must not accept arbitrary import paths",
    )
    require(
        "provenance.Host" in manager and "artifactUri.Host" in manager,
        "initial download must stay bound to manifest provenance origin",
    )
    require(
        "Uri.UriSchemeHttps" in manager and "Uri.UriSchemeHttps" in http,
        "download and redirect URI HTTPS checks missing",
    )
    require(
        "RequestedRangeNotSatisfiable" in http,
        "HTTP transport must support clean restart when range resume is rejected",
    )
    require(
        "ContentRange" in http and "RangeHeaderValue" in http,
        "HTTP byte-range resume contract missing",
    )
    require(
        "HttpCompletionOption.ResponseHeadersRead" in http,
        "HTTP transport must stream rather than buffer the complete model",
    )
    require(
        "AcceptEncoding.Add" in http and '"identity"' in http,
        "HTTP transport must request byte-identity content",
    )

    forbidden_fallbacks = [
        "fallback model",
        "default model",
        "nearest model",
        "prefix match",
        "try another",
        "cloud fallback",
    ]
    package_text = (contracts + manager + http).lower()
    for phrase in forbidden_fallbacks:
        require(phrase not in package_text, f"forbidden fallback phrase present: {phrase}")

    candidate_ids = ["qwen", "gemma", "llama-3", "smollm", "phi-"]
    settings_lower = settings.lower()
    for candidate_id in candidate_ids:
        require(
            candidate_id not in settings_lower,
            f"candidate model ID leaked into settings/UI: {candidate_id}",
        )

    print("RMA-132 static package-management contracts passed")


if __name__ == "__main__":
    main()
