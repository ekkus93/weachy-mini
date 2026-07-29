#!/usr/bin/env python3
"""Check local Markdown links without requiring network access."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LINK_PATTERN = re.compile(r"(?<!!)\[[^]]+]\(([^)]+)\)")


def markdown_files() -> list[Path]:
    """Return first-party Markdown files while excluding generated trees."""
    excluded_parts = {"Library", "Temp", "build", "Build", ".git", "third_party_src"}
    return sorted(
        path
        for path in ROOT.rglob("*.md")
        if not excluded_parts.intersection(path.relative_to(ROOT).parts)
    )


def validate_link(document: Path, target: str) -> str | None:
    """Return an error message for an invalid local target."""
    normalized = target.strip()
    if not normalized or normalized.startswith(("http://", "https://", "mailto:", "#")):
        return None
    without_anchor = normalized.split("#", maxsplit=1)[0]
    if not without_anchor:
        return None
    candidate = (document.parent / without_anchor).resolve()
    try:
        candidate.relative_to(ROOT)
    except ValueError:
        return f"{document.relative_to(ROOT)}: link escapes repository: {target}"
    if not candidate.exists():
        return f"{document.relative_to(ROOT)}: missing local link target: {target}"
    return None


def main() -> int:
    """Check all local Markdown links."""
    errors: list[str] = []
    for document in markdown_files():
        text = document.read_text(encoding="utf-8")
        for match in LINK_PATTERN.finditer(text):
            error = validate_link(document, match.group(1))
            if error is not None:
                errors.append(error)

    if errors:
        print("documentation link validation failed:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print("documentation link validation passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
