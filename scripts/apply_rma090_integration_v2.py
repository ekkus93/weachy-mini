#!/usr/bin/env python3
"""Run the RMA-090 integration patch with scoped duplicate-anchor handling."""

from __future__ import annotations

from pathlib import Path

import apply_rma090_integration as integration


original_replace_once = integration.replace_once


def scoped_replace_once(path: str, old: str, new: str) -> None:
    if (
        path.endswith("ReachySettingsApplicationCompositionProvider.cs")
        and old.startswith("        private void OnSettingsChanged(")
    ):
        target = Path(path)
        text = target.read_text(encoding="utf-8")
        if new in text:
            return
        index = text.rfind(old)
        if index < 0:
            raise RuntimeError(
                "The main-screen settings event anchor was not found."
            )
        updated = text[:index] + new + text[index + len(old):]
        target.write_text(updated, encoding="utf-8")
        return
    original_replace_once(path, old, new)


integration.replace_once = scoped_replace_once
integration.main()
