# Main Screen Application Shell

## Scope

RMA-081 adds the first production application composition and the fixed-view
main screen on top of the RMA-080 service graph. It deliberately does not
implement CameraX, microphone capture, speech providers, perception, behavior,
or durable settings. Those unavailable capabilities remain visible and
explain why they cannot run.

## Runtime installation

`ReachyMainScreenBootstrap` runs after the generated presentation scene loads.
It requires:

- exactly the active `ReachyPresentationRoot`;
- the production authoritative runtime required by that root;
- the tagged main camera;
- `ReachyPresentationCamera` metadata declaring
  `fixed_front_three_quarter`;
- `AcceptsUserNavigation == false`.

The bootstrap creates one `ReachyApplicationShell` child beneath the generated
presentation root. The shell contains:

- `ReachyMainScreen`;
- `ReachyProductionApplicationCompositionProvider`;
- `ReachyApplicationHostBehaviour`.

A second bootstrap invocation reuses the validated shell. Missing or invalid
presentation dependencies return a diagnostic fault; no alternate camera,
scene, or placeholder application is created.

## Production composition

The first production composition supplies all eight RMA-080 boundaries:

| Boundary | Initial implementation | Initial health |
| --- | --- | --- |
| Simulation | Adapter to `ReachyProductionAuthoritativeRuntime` | Ready |
| Camera | Fixed robot presentation camera | Ready |
| Audio | Explicit speech-audio unavailable service | Unavailable |
| Provider | Explicit unconfigured provider service | Unavailable |
| Perception | Explicit unavailable service | Unavailable |
| Behavior | Explicit unavailable service | Unavailable |
| Persistence | Session-state boundary | Ready |
| UI | Main-screen state and rendering service | Ready |

The expected aggregate application state is therefore `Degraded`, not `Ready`:
the robot view and application shell are operational, but optional interaction
capabilities are not falsely presented as configured.

## Interaction state

`ReachyMainScreenStateStore` is Unity-independent and publishes immutable,
revisioned snapshots. It supports the complete RMA-081 state vocabulary:

- idle;
- listening;
- transcribing;
- thinking;
- speaking;
- interrupted;
- unavailable;
- error.

Each snapshot also includes:

- a human-readable detail;
- the active camera label;
- whether camera selection is available;
- the active provider label;
- provider execution location: unavailable, local, or cloud;
- microphone availability;
- mutually exclusive settings and diagnostics panel state.

Historical snapshots are immutable. Every change increments the revision and
publishes a typed event.

## Main-screen presentation

The application shell is drawn as a safe-area-aware Unity overlay without
changing the generated robot hierarchy or camera transform. It contains:

- Reachy Mini title and concise interaction state;
- state detail text;
- active camera indicator;
- active provider and local/cloud location indicator;
- microphone control;
- camera selector control;
- settings control;
- diagnostics control.

Unavailable controls remain actionable. Selecting them changes the state to
`Unavailable` and explains the missing implementation milestone. Settings and
diagnostics open mutually exclusive panels rather than silently discarding the
request.

## Fixed-camera contract

The main screen has no orbit, pan, zoom, drag, touch-navigation, or free-camera
input path. Bootstrap and camera-service initialization both verify the fixed
camera metadata. Unity tests invoke every main-screen control and assert that
the presentation camera position and rotation remain unchanged.

RMA-082 will replace the settings shell with the complete settings screens.
RMA-090 and later camera tasks may add device-camera selection without changing
the robot presentation camera or introducing observer navigation.
