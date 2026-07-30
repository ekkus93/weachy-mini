#!/usr/bin/env python3
"""Apply small deterministic fixes after the main RMA-050 patch."""

from pathlib import Path

path = Path("Assets/ReachyMini/Tests/Editor/ReachyPresentationAssetTests.cs")
text = path.read_text(encoding="utf-8")
old = "using UnityEngine;\nusing UnityEngine.SceneManagement;\n"
new = "using UnityEngine;\nusing UnityEngine.Rendering;\nusing UnityEngine.SceneManagement;\n"
if text.count(old) != 1:
    raise SystemExit("Could not add UnityEngine.Rendering import exactly once")
text = text.replace(old, new)
old = '''                Assert.That(
                    roots.SelectMany(root => root.GetComponentsInChildren<AudioListener>(true)),
                    Has.Exactly(1).Items);
'''
new = '''                Assert.That(
                    roots.SelectMany(root => root.GetComponentsInChildren<AudioListener>(true))
                        .ToArray(),
                    Has.Length.EqualTo(1));
'''
if text.count(old) != 1:
    raise SystemExit("Could not harden AudioListener assertion exactly once")
path.write_text(text.replace(old, new), encoding="utf-8", newline="\n")
