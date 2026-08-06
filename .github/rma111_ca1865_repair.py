from __future__ import annotations

from pathlib import Path

path = Path("managed/ReachyMini.Camera.Tests/Rma111AndroidBridgeSourceContracts.cs")
source = path.read_text(encoding="utf-8")
repairs = (
    (
        'line.TrimStart().StartsWith("#", StringComparison.Ordinal)',
        "line.TrimStart().StartsWith('#')",
        "CA1865 char overload",
    ),
    (
        '"installed-apk-final-sha256.txt",',
        '"installed-apk-${label}-sha256.txt",',
        "dynamic installed APK digest evidence",
    ),
    (
        '"installed_final_sha256 != \\"${candidate_sha256}\\"",',
        '"\\"${installed_final_sha256}\\" != \\"${candidate_sha256}\\"",',
        "quoted final installed APK digest equality gate",
    ),
)
for old, new, label in repairs:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one {label} target; found {count}.")
    source = source.replace(old, new)
path.write_text(source, encoding="utf-8")
print("RMA-111 analyzer and authoritative source-contract repairs applied.")
