#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Presentation;
using ReachyMini.Rendering;
using ReachyMini.Simulation;
using UnityEngine;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    public sealed class ReachyProductionApplicationCompositionProvider :
        MonoBehaviour,
        IReachyApplicationCompositionProvider
    {
        [SerializeField]
        private ReachyProductionAuthoritativeRuntime? productionRuntime;

        [SerializeField]
        private Camera? presentationCamera;

        [SerializeField]
        private ReachyMainScreen? mainScreen;

        private bool compositionCreated;

        public void Configure(
            ReachyProductionAuthoritativeRuntime runtime,
            Camera camera,
            ReachyMainScreen screen)
        {
            if (compositionCreated)
            {
                throw new InvalidOperationException(
                    "Production application dependencies cannot change after composition creation.");
            }
            productionRuntime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            presentationCamera = camera ?? throw new ArgumentNullException(nameof(camera));
            mainScreen = screen ?? throw new ArgumentNullException(nameof(screen));
        }

        public ReachyApplicationComposition CreateApplicationComposition()
        {
            if (compositionCreated)
            {
                throw new InvalidOperationException(
                    "The production application composition cannot be created more than once.");
            }
            compositionCreated = true;

            ReachyProductionAuthoritativeRuntime runtime = productionRuntime ??
                throw new InvalidOperationException(
                    "The production application requires the authoritative runtime.");
            Camera camera = presentationCamera ??
                throw new InvalidOperationException(
                    "The production application requires the fixed presentation camera.");
            ReachyMainScreen screen = mainScreen ??
                throw new InvalidOperationException(
                    "The production application requires the main screen.");

            return ReachyApplicationComposition.CreateComplete(
                new[]
                {
                    new ReachyServiceRegistration(
                        "production-simulation",
                        ReachyServiceKind.Simulation,
                        ReachyServiceCriticality.Required,
                        Array.Empty<ReachyServiceKind>(),
                        resolver =>
                            new ReachyProductionSimulationApplicationService(runtime)),
                    new ReachyServiceRegistration(
                        "fixed-presentation-camera",
                        ReachyServiceKind.Camera,
                        ReachyServiceCriticality.Optional,
                        Array.Empty<ReachyServiceKind>(),
                        resolver =>
                            new ReachyFixedCameraApplicationService(camera)),
                    new ReachyServiceRegistration(
                        "speech-audio",
                        ReachyServiceKind.Audio,
                        ReachyServiceCriticality.Optional,
                        Array.Empty<ReachyServiceKind>(),
                        resolver => new ReachyUnavailableAudioApplicationService()),
                    new ReachyServiceRegistration(
                        "provider-selection",
                        ReachyServiceKind.Provider,
                        ReachyServiceCriticality.Optional,
                        new[] { ReachyServiceKind.Persistence },
                        resolver =>
                        {
                            resolver.GetRequired<IReachyPersistenceService>(
                                ReachyServiceKind.Persistence);
                            return new ReachyUnavailableProviderApplicationService();
                        }),
                    new ReachyServiceRegistration(
                        "perception",
                        ReachyServiceKind.Perception,
                        ReachyServiceCriticality.Optional,
                        new[]
                        {
                            ReachyServiceKind.Camera,
                            ReachyServiceKind.Provider,
                        },
                        resolver =>
                        {
                            resolver.GetRequired<IReachyCameraService>(
                                ReachyServiceKind.Camera);
                            resolver.GetRequired<IReachyProviderService>(
                                ReachyServiceKind.Provider);
                            return new ReachyUnavailablePerceptionApplicationService();
                        }),
                    new ReachyServiceRegistration(
                        "behavior",
                        ReachyServiceKind.Behavior,
                        ReachyServiceCriticality.Optional,
                        new[]
                        {
                            ReachyServiceKind.Simulation,
                            ReachyServiceKind.Provider,
                            ReachyServiceKind.Perception,
                        },
                        resolver =>
                        {
                            resolver.GetRequired<IReachySimulationService>(
                                ReachyServiceKind.Simulation);
                            resolver.GetRequired<IReachyProviderService>(
                                ReachyServiceKind.Provider);
                            resolver.GetRequired<IReachyPerceptionService>(
                                ReachyServiceKind.Perception);
                            return new ReachyUnavailableBehaviorApplicationService();
                        }),
                    new ReachyServiceRegistration(
                        "session-persistence",
                        ReachyServiceKind.Persistence,
                        ReachyServiceCriticality.Required,
                        Array.Empty<ReachyServiceKind>(),
                        resolver => new ReachySessionPersistenceApplicationService()),
                    new ReachyServiceRegistration(
                        "main-screen",
                        ReachyServiceKind.UserInterface,
                        ReachyServiceCriticality.Required,
                        new[]
                        {
                            ReachyServiceKind.Simulation,
                            ReachyServiceKind.Camera,
                            ReachyServiceKind.Audio,
                            ReachyServiceKind.Provider,
                            ReachyServiceKind.Perception,
                            ReachyServiceKind.Behavior,
                            ReachyServiceKind.Persistence,
                        },
                        resolver => new ReachyMainScreenApplicationService(
                            screen,
                            resolver.GetRequired<IReachySimulationService>(
                                ReachyServiceKind.Simulation),
                            resolver.GetRequired<IReachyCameraService>(
                                ReachyServiceKind.Camera),
                            resolver.GetRequired<IReachyAudioService>(
                                ReachyServiceKind.Audio),
                            resolver.GetRequired<IReachyProviderService>(
                                ReachyServiceKind.Provider),
                            resolver.GetRequired<IReachyPerceptionService>(
                                ReachyServiceKind.Perception),
                            resolver.GetRequired<IReachyBehaviorService>(
                                ReachyServiceKind.Behavior),
                            resolver.GetRequired<IReachyPersistenceService>(
                                ReachyServiceKind.Persistence))),
                });
        }
    }

    internal sealed class ReachyProductionSimulationApplicationService :
        ReachyApplicationServiceBase,
        IReachySimulationService,
        IReachyApplicationInterruptionParticipant
    {
        private readonly ReachyProductionAuthoritativeRuntime runtime;

        public ReachyProductionSimulationApplicationService(
            ReachyProductionAuthoritativeRuntime runtime)
            : base(
                "production-simulation",
                ReachyServiceKind.Simulation,
                ReachyServiceCriticality.Required)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        protected override void OnInitialize()
        {
            SetReady(
                $"Authoritative simulation adapter is present; runtime status is {runtime.Status}.");
        }

        public void PauseForApplicationInterruption()
        {
            ReachySimulationControlResult result =
                runtime.PauseForApplicationInterruption();
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Authoritative simulation pause failed with " +
                    result.Error.Code + ".");
            }
        }

        public void ResumeAfterApplicationInterruption()
        {
            ReachySimulationControlResult result =
                runtime.ResumeAfterApplicationInterruption();
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Authoritative simulation resume failed with " +
                    result.Error.Code + ".");
            }
        }
    }

    internal sealed class ReachyFixedCameraApplicationService :
        ReachyApplicationServiceBase,
        IReachyCameraService
    {
        private readonly Camera camera;

        public ReachyFixedCameraApplicationService(Camera camera)
            : base(
                "fixed-presentation-camera",
                ReachyServiceKind.Camera,
                ReachyServiceCriticality.Optional)
        {
            this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        public string ActiveCameraLabel => "Fixed front / three-quarter";

        public bool SelectionAvailable => false;

        protected override void OnInitialize()
        {
            ReachyPresentationCamera metadata =
                camera.GetComponent<ReachyPresentationCamera>();
            if (metadata == null ||
                !string.Equals(
                    metadata.Framing,
                    "fixed_front_three_quarter",
                    StringComparison.Ordinal) ||
                metadata.AcceptsUserNavigation)
            {
                throw new InvalidOperationException(
                    "The main screen camera is not the fixed non-navigable presentation camera.");
            }
            if (!camera.CompareTag("MainCamera") || !camera.enabled)
            {
                throw new InvalidOperationException(
                    "The fixed presentation camera is not the active main camera.");
            }
            SetReady(
                "Fixed front / three-quarter robot presentation camera is active; device camera selection is not installed.");
        }
    }

    internal sealed class ReachyUnavailableAudioApplicationService :
        ReachyApplicationServiceBase,
        IReachyAudioService
    {
        public ReachyUnavailableAudioApplicationService()
            : base(
                "speech-audio",
                ReachyServiceKind.Audio,
                ReachyServiceCriticality.Optional)
        {
        }

        protected override void OnInitialize()
        {
            SetUnavailable(
                "Microphone capture and speech playback are not implemented until the speech phase.");
        }
    }

    internal sealed class ReachyUnavailableProviderApplicationService :
        ReachyApplicationServiceBase,
        IReachyProviderService
    {
        public ReachyUnavailableProviderApplicationService()
            : base(
                "provider-selection",
                ReachyServiceKind.Provider,
                ReachyServiceCriticality.Optional)
        {
        }

        protected override void OnInitialize()
        {
            SetUnavailable(
                "No local or cloud provider is configured; provider selection begins in RMA-082.");
        }
    }

    internal sealed class ReachyUnavailablePerceptionApplicationService :
        ReachyApplicationServiceBase,
        IReachyPerceptionService
    {
        public ReachyUnavailablePerceptionApplicationService()
            : base(
                "perception",
                ReachyServiceKind.Perception,
                ReachyServiceCriticality.Optional)
        {
        }

        protected override void OnInitialize()
        {
            SetUnavailable(
                "Perception is unavailable until camera and provider capabilities are configured.");
        }
    }

    internal sealed class ReachyUnavailableBehaviorApplicationService :
        ReachyApplicationServiceBase,
        IReachyBehaviorService
    {
        public ReachyUnavailableBehaviorApplicationService()
            : base(
                "behavior",
                ReachyServiceKind.Behavior,
                ReachyServiceCriticality.Optional)
        {
        }

        protected override void OnInitialize()
        {
            SetUnavailable(
                "Interactive behavior is unavailable until provider and perception services are configured.");
        }
    }

    internal sealed class ReachySessionPersistenceApplicationService :
        ReachyApplicationServiceBase,
        IReachyPersistenceService
    {
        public ReachySessionPersistenceApplicationService()
            : base(
                "session-persistence",
                ReachyServiceKind.Persistence,
                ReachyServiceCriticality.Required)
        {
        }

        protected override void OnInitialize()
        {
            SetReady(
                "Session state is available; durable user settings are introduced in RMA-082.");
        }
    }

    internal sealed class ReachyMainScreenApplicationService :
        ReachyApplicationServiceBase,
        IReachyUserInterfaceService,
        IReachyApplicationInterruptionParticipant
    {
        private readonly ReachyMainScreen screen;
        private readonly IReachyApplicationService[] dependencies;
        private readonly IReachyProviderGovernorDiagnosticsSource? providerGovernorDiagnostics;
        private readonly ReachyMainScreenStateStore stateStore =
            new ReachyMainScreenStateStore();
        private bool governorOwnsInteractionState;

        public ReachyMainScreenApplicationService(
            ReachyMainScreen screen,
            IReachySimulationService simulation,
            IReachyCameraService camera,
            IReachyAudioService audio,
            IReachyProviderService provider,
            IReachyPerceptionService perception,
            IReachyBehaviorService behavior,
            IReachyPersistenceService persistence)
            : base(
                "main-screen",
                ReachyServiceKind.UserInterface,
                ReachyServiceCriticality.Required)
        {
            this.screen = screen ?? throw new ArgumentNullException(nameof(screen));
            IReachyProviderService resolvedProvider = provider ??
                throw new ArgumentNullException(nameof(provider));
            providerGovernorDiagnostics =
                resolvedProvider as IReachyProviderGovernorDiagnosticsSource;
            dependencies = new IReachyApplicationService[]
            {
                simulation ?? throw new ArgumentNullException(nameof(simulation)),
                camera ?? throw new ArgumentNullException(nameof(camera)),
                audio ?? throw new ArgumentNullException(nameof(audio)),
                resolvedProvider,
                perception ?? throw new ArgumentNullException(nameof(perception)),
                behavior ?? throw new ArgumentNullException(nameof(behavior)),
                persistence ?? throw new ArgumentNullException(nameof(persistence)),
            };
        }

        protected override void OnInitialize()
        {
            for (int index = 0; index < dependencies.Length; ++index)
            {
                dependencies[index].HealthChanged += OnDependencyHealthChanged;
            }

            stateStore.SetCapabilities(
                "Fixed front / three-quarter",
                false,
                "Not configured",
                ReachyProviderLocation.Unavailable,
                false);
            stateStore.SetInteraction(
                ReachyInteractionState.Idle,
                "Robot view is ready. Voice, providers, perception, and behavior are not configured.");
            ApplyProviderGovernorDiagnostics();
            screen.Bind(stateStore, BuildDiagnostics);
            SetReady("Main screen is bound to application state.");
        }

        protected override void OnDispose()
        {
            for (int index = 0; index < dependencies.Length; ++index)
            {
                dependencies[index].HealthChanged -= OnDependencyHealthChanged;
            }
        }

        public void PauseForApplicationInterruption()
        {
            _ = stateStore.PauseForApplicationInterruption();
        }

        public void ResumeAfterApplicationInterruption()
        {
            _ = stateStore.ResumeAfterApplicationInterruption();
        }

        private void OnDependencyHealthChanged(
            object? sender,
            ReachyServiceHealthChangedEventArgs eventArgs)
        {
            if (eventArgs.Health.Kind == ReachyServiceKind.Provider &&
                providerGovernorDiagnostics != null)
            {
                ApplyProviderGovernorDiagnostics();
            }

            if (eventArgs.Health.Criticality == ReachyServiceCriticality.Required &&
                (eventArgs.Health.State == ReachyServiceState.Faulted ||
                 eventArgs.Health.State == ReachyServiceState.Unavailable))
            {
                stateStore.SetInteraction(
                    ReachyInteractionState.Error,
                    $"{eventArgs.Health.ServiceId}: {eventArgs.Health.Message}");
            }
        }

        private void ApplyProviderGovernorDiagnostics()
        {
            if (providerGovernorDiagnostics == null)
            {
                return;
            }

            ReachyMainScreenSnapshot current = stateStore.Current;
            ReachyProviderGovernorMainScreenProjection projection =
                ReachyProviderGovernorMainScreenProjection.Create(
                    providerGovernorDiagnostics.GovernorDiagnostics,
                    current.InteractionState,
                    governorOwnsInteractionState);
            stateStore.SetCapabilities(
                current.ActiveCamera,
                current.CameraSelectionAvailable,
                projection.ActiveProvider,
                projection.ProviderLocation,
                current.MicrophoneAvailable);
            if (projection.OverrideInteraction)
            {
                stateStore.SetInteraction(
                    projection.InteractionState,
                    projection.Detail);
            }
            governorOwnsInteractionState = projection.GovernorOwnsInteractionState;
        }

        private string BuildDiagnostics()
        {
            var lines = new List<string>(dependencies.Length + 2)
            {
                "Application shell: active",
            };
            for (int index = 0; index < dependencies.Length; ++index)
            {
                ReachyServiceHealth health = dependencies[index].Health;
                lines.Add(
                    $"{health.Kind}: {health.State} — {health.Message}");
            }
            if (providerGovernorDiagnostics != null)
            {
                lines.Add(providerGovernorDiagnostics.GovernorDiagnostics.DiagnosticLine);
            }
            return string.Join("\n", lines);
        }
    }
}
