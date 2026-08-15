# RMA-163 — Imported Content and URL Security

## Scope

RMA-163 hardens every persisted/imported JSON boundary used by camera calibration,
durable settings, provider profiles, fallback policy state, and local-model metadata.
It also centralizes URL host policy for provider endpoints and local model/VLM download
sources.

## Requirements

- Decode imported files with strict UTF-8 and reject malformed byte sequences.
- Apply per-document byte ceilings before JSON parsing and to serialized output.
- Bound profile collections, calibration text, image dimensions, and crop arithmetic.
- Preserve managed model-store path containment and reject traversal/overwrite escapes.
- Require public HTTPS for remote model/VLM sources and redirects.
- Permit cleartext provider endpoints only for explicit trusted local-development hosts.
- Reject credentials/fragments in remote model URLs and user-info/query/fragment data in
  provider base URLs.
- Keep diagnostic-bundle admission default-deny for secrets and private media.

## Failure behavior

Invalid or oversized imported state fails visibly through the owning persistence service;
existing quarantine/recovery behavior remains fail-closed. Model-store ownership markers
are bounded before read and must match exactly. Endpoint validation rejects unsafe sources
rather than rewriting or silently falling back.

## Verification

Permanent static coverage lives in `scripts/tests/test_rma163_import_security.py` and is
included by the repository static gate.
