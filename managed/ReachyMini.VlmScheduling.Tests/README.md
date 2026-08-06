# RMA-113 VLM Scheduling Contract Harness

This executable test project validates the bounded VLM admission policy in
`ReachyVlmSchedulingPolicy.cs` without invoking a model or network service.

Run the permanent contract locally with:

```bash
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj \
  --configuration Release --warnaserror

dotnet run \
  --project managed/ReachyMini.VlmScheduling.Tests/ReachyMini.VlmScheduling.Tests.csproj \
  --configuration Release
```

The 25-case harness requires explicit trigger admission, per-provider rate and
concurrency limits, bounded provider state, obsolete scene/question
cancellation, mandatory cloud network and cost acknowledgement, immutable
snapshots, and exact provider selection with no fallback. Camera-frame-rate VLM
execution is not an available trigger. The configured slow interval is disabled
unless explicitly enabled with a positive interval and bounded prompt.

Cancellation dispatch runs callbacks without holding the lease monitor.
Concurrent completion removes the active request while holding the scheduler
monitor, then releases that monitor before waiting for cancellation disposal.
The regression suite coordinates those threads and rejects the previous
scheduler-lock/cancellation-lock inversion.

The permanent CI workflow is
`.github/workflows/rma113-vlm-scheduling-policy.yml`.
