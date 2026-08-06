# RMA-113 VLM Scheduling Policy Validation

**Task:** RMA-113 — Implement VLM scheduling policy
**Date:** 2026-08-06
**Status:** Complete

## Scope

RMA-113 introduces a Unity-independent managed scheduler that controls when an
existing RMA-110 vision-language provider may be invoked. The scheduler is an
admission and lifecycle policy only. Provider execution, transformed-frame
ownership, timeouts, provider epochs, structured provider errors, and model or
network interaction remain owned by the established provider executor.

Permanent implementation and validation surfaces:

- `Assets/ReachyMini/Runtime/Core/Perception/ReachyVlmSchedulingPolicy.cs`
- `managed/ReachyMini.VlmScheduling.Tests/Program.cs`
- `managed/ReachyMini.VlmScheduling.Tests/ReachyMini.VlmScheduling.Tests.csproj`
- `managed/ReachyMini.VlmScheduling.Tests/README.md`
- `.github/workflows/rma113-vlm-scheduling-policy.yml`

## Admission policy

The scheduler recognizes only these explicit trigger classes:

1. user visual question;
2. explicit planner request;
3. significant scene change;
4. new entity;
5. manual request;
6. configured slow interval.

There is no camera-frame trigger. `ExplicitTriggersOnly` is the default options
profile, and slow-interval admission requires an explicit positive interval and
bounded configured prompt.

Every admission names one exact provider instance. Unknown providers are
rejected visibly; the scheduler never probes or substitutes another provider.
Provider policy state is bounded to sixteen configured instances.

## Rate, concurrency, and ordering

Each provider owns independent:

- sliding-window request history;
- maximum request count per configured interval;
- active-request concurrency count;
- last accepted trigger sequence;
- last slow-interval timestamp;
- immutable diagnostic snapshot.

Trigger sequences cannot repeat or regress. Timestamps cannot regress. A rate or
concurrency rejection does not consume another provider's budget and cannot
silently overwrite an active lease.

## Obsolescence and cancellation

Scene and question revisions are monotonic. A scene revision change marks every
active request obsolete. A question revision change additionally cancels active
visual-question work. Cancellation requests remain visible and keep their
concurrency slot until completion releases the lease.

### Rejected initially green candidate

Commit `269168a4a033924d983f21b54cb7935bdca45e12` passed the original 24
contracts, but a post-green source audit found a scheduler-lock/cancellation-lock
inversion:

1. cancellation dispatch held the lease cancellation monitor while synchronous
   user callbacks executed;
2. concurrent completion held the scheduler monitor while waiting for that
   cancellation monitor;
3. a cancellation callback reentering `GetSnapshot` waited for the scheduler
   monitor.

That cycle could deadlock even though the original contract suite was green. The
candidate and its validation runs were rejected rather than documented as
completion.

### Accepted cancellation hardening

Commit `b29db6abbd41c6e1c3dee0ea5f5b2a2bbc90aa09` dispatches callbacks outside
the lease monitor. `Complete` removes the active lease and updates scheduler
accounting under the scheduler monitor, releases it, and only then waits for
cancellation disposal. Reentrant completion on the cancellation-dispatch thread
defers disposal until callback unwinding. The lease caches its cancellation
token so completed state remains readable without touching a disposed token
source.

One-use hardening run `31091801023`, job `92584315862`, built the production
managed core with zero warnings and passed all 25 contracts, including a
thread-coordinated regression that drives cancellation dispatch and concurrent
completion through the previous inversion boundary.

## Cloud disclosure contract

On-device providers require neither network nor cost acknowledgement. Cloud
providers require a non-empty network disclosure and explicit acknowledgement.
Providers marked potentially billable additionally require a non-empty cost
disclosure and separate cost acknowledgement. Missing disclosure or either
acknowledgement fails closed without consuming rate or concurrency capacity.

## Accepted exact-SHA validation

### Dedicated RMA-113 gate

Run `31091982708`, job `92584899909`, passed on exact implementation SHA
`b29db6abbd41c6e1c3dee0ea5f5b2a2bbc90aa09`:

- managed core build with warnings as errors and zero warnings;
- all 25 deterministic behavioral and source contracts;
- exact-SHA report generation and source hashing;
- evidence upload and final `RMA-113 VLM Scheduling Policy` status publication.

Artifact `8963824901`,
`rma113-vlm-scheduling-policy-evidence-b29db6abbd41c6e1c3dee0ea5f5b2a2bbc90aa09`,
has digest
`sha256:4967e21791c153a5512120d2807c02181e3da20bf08e5f0ed0a7a417fb5c5ab3`.
The report records explicit-trigger-only defaults, no camera-frame trigger,
slow interval disabled by default, per-provider rate and concurrency limits,
scene/question cancellation, cancellation lock-inversion hardening, mandatory
cloud network/cost disclosure, bounded provider state, and no provider fallback.
The accepted scheduler source SHA-256 is
`2037cbb358050f82fc6645de9a2caaf755810c3508692f59cfc113facd8c3d2f`.

### Hosted CI

Hosted CI run `31091981878` passed static workflow/repository policy, managed
warnings-as-errors, native warnings-as-errors and sanitizer tests, Android lint
and tests, and pinned Reachy/MuJoCo model validation on the same exact SHA.

### Physical Unity and Android validation

Self-hosted run `31091982334`, job `92584898645`, passed the complete acceptance
sequence on an LGE LG-H872 running Android 8.0.0/API 26/arm64-v8a, serial
`LGH87250967ab9`:

- generated Reachy presentation and production MuJoCo staging;
- `129/129` Unity EditMode tests and `1/1` PlayMode test;
- ARM64/API-26 IL2CPP APK build and architecture verification;
- physical RMA-090 discovery, RMA-091 acquisition, and RMA-092 Vulkan texture
  acceptance;
- RMA-111 bundled face/person tracking with stable `face-000001` and
  `person-000001`, invalid-center suppression, zero VLM invocations, and no
  runtime model download;
- RMA-022 pause/resume, controlled initialization failure, destruction, and
  no-hidden-native-fallback lifecycle acceptance;
- authoritative MuJoCo rendering with 17 moved bodies, all six Stewart links,
  yaw, head, and both antennas moving, valid renderer structure, and no hidden
  kinematic fallback;
- every evidence upload, APK upload, and final status publication.

Physical artifact digests:

- Unity tests `8963899372`:
  `sha256:a00ed1b6bd66835e0faf415ee9b34ac95fbed04990c26932f3aa6106643bfc03`;
- RMA-090 `8963969514`:
  `sha256:4d93ba51d2263bf9a3c147e5bad47653294ecf9f08895a6ee4b42d6cebe5a3e0`;
- RMA-091 `8964008450`:
  `sha256:dfece375a37f73437045dad653f71021b2ebc5e9d87c27500f4dd3b831b3b63f`;
- RMA-092 `8964041132`:
  `sha256:61992cbace8d8cd19099e00a6f9299d48e42a17f33d0c0e1cb45197d8350fd2a`;
- RMA-111 `8964058253`:
  `sha256:dd6a04fb093a6a528af839f97d7f6ac8dba3db25e93ef519be6b1348b9fe928e`;
- lifecycle `8964087814`:
  `sha256:ebf3fe25ec49f22e07d6c72dae032aa608ab0b38f339f1a34439ff342aee41c5`;
- authoritative rendering `8964103224`:
  `sha256:150565d34ae6a82ef75338d0ae13688210be6708baa24f834ca42eecddb272f5`;
- APK `8964139939`:
  `sha256:e39dc9baba9b362d2765b9865e2bbcabb0de14f8aff29d58680ac0260a716750`.

The authoritative gate reused the installed APK only after candidate,
pre-install, and final installed SHA-256 all matched
`d7a609d59247bc72434171338b26200be97ae408d842e4a9ba75657d36903402`.
Install and launch status were zero and
`installed_apk_matches_candidate=true`.

## Repository cleanup

Cleanup run `31092896861` deleted the one-use `rma113-validation` and
`rma113-hardening` branches. Their compressed payloads, applicators, Python
patcher, and disposable workflows are absent from `master`. The closeout branch
is deleted by its own publication workflow. Only permanent scheduler source,
tests, harness README, workflow, TODO evidence, and this validation record
remain.
