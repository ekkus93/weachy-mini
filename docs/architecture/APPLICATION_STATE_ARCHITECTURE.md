# Application State Architecture

## Scope

RMA-080 defines the application-level service graph and lifecycle used by the
Unity Android Reachy Mini emulator. It does not implement the main screen,
settings, CameraX, providers, perception, or behavior. Those capabilities plug
into the contracts defined here in later tasks.

## Service boundaries

A complete composition contains exactly one service for each boundary:

- simulation;
- camera;
- audio;
- provider selection and execution;
- perception;
- behavior planning and execution;
- persistence;
- user interface.

Each boundary has a distinct marker interface derived from
`IReachyApplicationService`. A factory must return the interface matching its
registered `ReachyServiceKind`; identity, criticality, and kind mismatches fail
startup. This prevents a generic provider or placeholder from silently standing
in for another capability.

## Dependency construction

`ReachyApplicationComposition.CreateComplete` validates the complete graph
before any service is constructed. It rejects:

- missing or duplicate service kinds;
- duplicate service identifiers;
- missing, duplicate, or self dependencies;
- dependency cycles.

Construction follows a deterministic topological order. A factory receives an
`IReachyServiceResolver` restricted to the dependencies declared by that
registration. Requesting an undeclared or not-yet-constructed dependency is a
startup fault, not a service-locator fallback. A factory result that violates
its registration is disposed before startup fails.

## Lifecycle

`ReachyApplicationHost.Start` performs two separate phases:

1. construct all services in dependency order;
2. initialize all services in the same order.

Initialization must finish in `Ready`, `Degraded`, `Unavailable`, or `Faulted`.
A required unavailable service or any initialization fault aborts startup. The
host attempts reverse-order disposal of every constructed service and preserves
both the startup error and any rollback errors.

`ReachyApplicationHost.Dispose` is explicit, idempotent, and reverse ordered.
It attempts every disposal even when one service throws, and retains all
failures in the final application health message.

`ReachyApplicationServiceBase` provides one-shot initialization, idempotent
disposal, immutable health records, monotonic service revisions, and visible
fault/degraded/unavailable transitions. Services may implement the interface
directly when they need a different internal lifecycle, but the host applies
the same post-initialization state contract.

## Health model

Every service publishes immutable `ReachyServiceHealth` records. The host
publishes immutable `ReachyApplicationHealthSnapshot` records containing a
defensive copy of every service record and a monotonic application revision.

Aggregation is fail closed:

- any required service that is faulted, unavailable, or disposed makes the
  application `Faulted`;
- an optional faulted/unavailable service, or any degraded service, makes the
  application `Degraded`;
- only a graph whose services are all ready is `Ready`.

This model allows later UI work to distinguish an optional unavailable camera
or cloud provider from loss of authoritative simulation, persistence, or the UI
itself. No state is relabeled as ready to keep the application moving.

## Unity ownership

The graph, contracts, host, and health model live under
`Assets/ReachyMini/Runtime/Core` in namespace `ReachyMini.AppState` and contain
no `UnityEngine` dependency. The namespace deliberately avoids shadowing
`UnityEngine.Application`. The shared code is compiled by both Unity and the
existing .NET shared-core project.

`ReachyApplicationHostBehaviour` is the narrow Unity lifecycle bridge. It
requires an explicit `IReachyApplicationCompositionProvider`, exposes health
and retained startup diagnostics, delegates Unity `Start` and `OnDestroy` to
explicit `StartApplication` and `ShutdownApplication` methods, and has no
default or placeholder composition. The explicit methods make the same
lifecycle path deterministic in edit-mode tests without attempting to fake
Unity callbacks through `SendMessage`.

RMA-081 will supply the first production composition and UI service while
preserving these boundaries.
