from __future__ import annotations

from pathlib import Path

path = Path("managed/ReachyMini.Camera.Tests/Rma111AndroidBridgeSourceContracts.cs")
source = path.read_text(encoding="utf-8")
old = 'line.TrimStart().StartsWith("#", StringComparison.Ordinal)'
new = "line.TrimStart().StartsWith('#')"
count = source.count(old)
if count != 1:
    raise SystemExit(f"Expected exactly one CA1865 repair target; found {count}.")
path.write_text(source.replace(old, new), encoding="utf-8")
print("RMA-111 CA1865 source-contract repair applied.")
