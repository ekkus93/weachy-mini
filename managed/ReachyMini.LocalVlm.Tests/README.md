# RMA-114 Local VLM Contract Harness

This executable project validates the optional local VLM extension point without
loading a model, invoking a runtime, reading an image, or opening a network
connection.

Run from the repository root:

```bash
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj \
  --configuration Release --warnaserror

dotnet run \
  --project managed/ReachyMini.LocalVlm.Tests/ReachyMini.LocalVlm.Tests.csproj \
  --configuration Release
```

The 45-case harness covers manifest identity, provenance, limits, capability and
artifact integrity fields; safe relative paths; optional first-release policy;
no automatic download or fallback; exact on-device provider creation; honest
unavailable-stub behavior; cancellation/disposal; schema/source parity; and the
absence of model payloads from `models/manifests`.

Local artifact roots are fail-closed: only absolute local filesystem paths,
hostless file URIs, and authority-bearing Android content URIs are accepted.
Relative paths, UNC/network shares, remote-host file URIs, and network schemes
are rejected before an adapter can create a provider.
