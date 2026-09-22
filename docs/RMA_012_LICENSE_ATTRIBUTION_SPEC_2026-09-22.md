# RMA-012 In-App Licenses and Attribution Closure

**Date:** 2026-09-22  
**Roadmap item:** `RMA-012`  
**Authoritative roadmap:** `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`

## Purpose

RMA-012 closes the release-notice gap left after the RMA-082 settings shell
introduced a basic Licenses section. The installed application must present its
project status and the repository's complete third-party inventory without
requiring network access.

## Contract

The shipping `ReachyMainScreen` Licenses section consumes only
`ReachySettingsStateStore.GetLicenseNotices()`. That method contains:

1. an explicit statement that Weachy Mini is an unofficial community project
   and is not endorsed by Pollen Robotics, Hugging Face, Google DeepMind,
   Unity, Google, or OpenAI;
2. an explicit statement that the app is intended to be free and
   noncommercial; and
3. one notice whose `Component` exactly matches every `name` in
   `third_party/inventory.json`.

The inventory entries include both packaged dependencies and intentionally
non-packaged/candidate records. Their UI text must describe those states rather
than implying that a developer tool or unselected model is redistributed.

MuJoCo is identified as Google DeepMind software under Apache-2.0. Reachy Mini
software and Pollen Robotics hardware/model assets are separately identified so
the Apache software notice cannot obscure the CC BY-NC-SA attribution,
noncommercial, ShareAlike, and modification obligations applicable to derived
assets.

## Offline UI

The settings panel is fixed-size and does not use a scrolling content surface.
The Licenses section therefore paginates four notices at a time with explicit
`PREVIOUS` and `NEXT` controls. All text is compiled into the application; the
screen performs no HTTP request and does not depend on an external browser or
repository checkout.

## Drift prevention

`scripts/tests/test_rma012_licenses.py` loads the repository inventory and
requires every inventory `name` to appear as an in-app notice component. It
also requires the unofficial/non-endorsement and free/noncommercial statements
and the fixed-panel pagination contract.

The existing `managed/ReachyMini.Application.Tests` suite additionally compiles
and exercises `GetLicenseNotices()`, checking the project-status text, MuJoCo
Apache-2.0 notice, and Reachy/Pollen attribution.

`.github/workflows/rma082-settings.yml` runs both the managed settings suite and
the RMA-012 inventory-parity test whenever the settings notice source,
repository inventory/notices, or RMA-012 test changes.

## Completion rule

RMA-012 roadmap checkboxes may be marked complete only after the exact branch
head passes normal CI and the dedicated RMA-082 settings workflow. The runtime
notice list and `third_party/inventory.json` must remain in parity at that
qualified SHA.
