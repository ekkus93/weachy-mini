"""Shared validation for exact, clean, pinned Git source checkouts."""

from __future__ import annotations

import os
import subprocess
from pathlib import Path


class SourceCheckoutError(RuntimeError):
    """Raised when a source checkout does not match its provenance lock."""


def validate_commit_sha(commit: object) -> str:
    """Validate and return a lowercase full Git SHA-1 string."""
    if not isinstance(commit, str) or len(commit) != 40 or any(
        character not in "0123456789abcdef" for character in commit
    ):
        raise SourceCheckoutError(
            "Pinned commit must be a lowercase 40-character SHA-1."
        )
    return commit


def run_git(source: Path, *arguments: str) -> str:
    """Run a noninteractive Git command against a source checkout."""
    try:
        completed = subprocess.run(
            ["git", "-C", str(source), *arguments],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
            env={**os.environ, "GIT_TERMINAL_PROMPT": "0"},
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise SourceCheckoutError(f"Cannot inspect source Git checkout: {exc}") from exc
    if completed.returncode != 0:
        detail = (completed.stderr or completed.stdout).strip()
        raise SourceCheckoutError(
            f"Git command failed ({' '.join(arguments)}): {detail}"
        )
    return completed.stdout.strip()


def validate_clean_checkout(source: Path, expected_commit: object) -> str:
    """Require a clean checkout at the exact pinned commit and return that commit."""
    commit = validate_commit_sha(expected_commit)
    if not source.is_dir():
        raise SourceCheckoutError(f"Source checkout is not a directory: {source}")
    actual_commit = run_git(source, "rev-parse", "HEAD")
    if actual_commit != commit:
        raise SourceCheckoutError(
            f"Source revision mismatch: expected {commit}, found {actual_commit}"
        )
    worktree_status = run_git(source, "status", "--porcelain", "--untracked-files=all")
    if worktree_status:
        raise SourceCheckoutError("Source checkout has modified or untracked files.")
    return actual_commit
