#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Rendering;
using ReachyMini.Simulation;
using UnityEngine;

namespace ReachyMini.AppState
{
    public readonly struct ReachySettingsResetOutcome
    {
        public ReachySettingsResetOutcome(bool succeeded, string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                throw new ArgumentException(
                    "A simulation reset outcome requires diagnostics.",
                    nameof(detail));
            }
            Succeeded = succeeded;
            Detail = detail;
        }

        public bool Succeeded { get; }

        public string Detail { get; }
    }

    [DisallowMultipleComponent]
    public sealed class ReachySettingsApplicationCompositionProvider :
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
                    "Settings application dependencies cannot change after composition creation.");
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
                    "The settings application composition cannot be created more than once.");
            }
            compositionCreated = true;

            ReachyProductionAuthoritativeRuntime runtime = productionRuntime ??
                throw new InvalidOperationException(
                    "The settings application requires the authoritative runtime.");
            Camera camera = presentationCamera ??
                throw new InvalidOperationException(
                    "The settings application requires the fixed presentation camera.");
            ReachyMainScreen screen = mainScreen ??
                throw new InvalidOperationException(
                    "The settings application requires the main screen.");

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
                        new[] { ReachyServiceKind.Persistence },
                        resolver => new ReachySettingsAudioApplicationService(
                            resolver.GetRequired<ReachySettingsPersistenceApplicationService>(
                                ReachyServiceKind.Persistence).Settings)),
                    new ReachyServiceRegistration(
                        "provider-selection",
                        ReachyServiceKind.Provider,
                        ReachyServiceCriticality.Optional,
                        new[] { ReachyServiceKind.Persistence },
                        resolver => new ReachySettingsProviderApplicationService(
                            resolver.GetRequired<ReachySettingsPersistenceApplicationService>(
                                ReachyServiceKind.Persistence).Settings)),
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
                        "durable-settings",
                        ReachyServiceKind.Persistence,
                        ReachyServiceCriticality.Required,
                        Array.Empty<ReachyServiceKind>(),
                        resolver =>
                            new ReachySettingsPersistenceApplicationService()),
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
                        resolver => new ReachySettingsMainScreenApplicationService(
                            screen,
                            runtime,
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
                            resolver.GetRequired<ReachySettingsPersistenceApplicationService>(
                                ReachyServiceKind.Persistence))),
                });
        }
    }

    internal sealed class ReachySettingsAudioApplicationService :
        ReachyApplicationServiceBase,
        IReachyAudioService
    {
        private readonly ReachySettingsStateStore settings;

        public ReachySettingsAudioApplicationService(
            ReachySettingsStateStore settings)
            : base(
                "speech-audio",
                ReachyServiceKind.Audio,
                ReachyServiceCriticality.Optional)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        protected override void OnInitialize()
        {
            settings.Changed += OnSettingsChanged;
            PublishCurrentHealth();
        }

        protected override void OnDispose()
        {
            settings.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
        {
            PublishCurrentHealth();
        }

        private void PublishCurrentHealth()
        {
            ReachyProviderSelection asr =
                settings.Current.GetProvider(ReachyProviderKind.Asr);
            ReachyProviderSelection tts =
                settings.Current.GetProvider(ReachyProviderKind.Tts);
            SetUnavailable(
                "Speech preferences are stored but audio capture/playback are not " +
                $"installed. ASR={asr.DisplayName} " +
                $"({ReachySettingsStateStore.GetConnectivityLabel(asr.Connectivity)}); " +
                $"TTS={tts.DisplayName} " +
                $"({ReachySettingsStateStore.GetConnectivityLabel(tts.Connectivity)})." );
        }
    }

    internal sealed class ReachySettingsProviderApplicationService :
        ReachyApplicationServiceBase,
        IReachyProviderService
    {
        private readonly ReachySettingsStateStore settings;

        public ReachySettingsProviderApplicationService(
            ReachySettingsStateStore settings)
            : base(
                "provider-selection",
                ReachyServiceKind.Provider,
                ReachyServiceCriticality.Optional)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        protected override void OnInitialize()
        {
            settings.Changed += OnSettingsChanged;
            PublishCurrentHealth();
        }

        protected override void OnDispose()
        {
            settings.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
        {
            PublishCurrentHealth();
        }

        private void PublishCurrentHealth()
        {
            int configured = 0;
            int networkRequired = 0;
            foreach (ReachyProviderSelection provider in
                     settings.Current.ProviderSelections)
            {
                if (provider.Execution != ReachyProviderExecution.Unconfigured)
                {
                    ++configured;
                }
                if (provider.Connectivity ==
                    ReachyConnectivityRequirement.NetworkRequired)
                {
                    ++networkRequired;
                }
            }

            SetUnavailable(
                configured == 0
                    ? "No ASR, TTS, LLM, or VLM provider is configured."
                    : $"{configured} provider preference(s) are stored, including " +
                      $"{networkRequired} network-required selection(s), but provider " +
                      "runtime integrations and credentials are not installed.");
        }
    }

    internal sealed class ReachySettingsMainScreenApplicationService :
        ReachyApplicationServiceBase,
        IReachyUserInterfaceService
    {
        private readonly ReachyMainScreen screen;
        private readonly ReachyProductionAuthoritativeRuntime runtime;
        private readonly IReachyApplicationService[] dependencies;
        private readonly ReachySettingsPersistenceApplicationService persistence;
        private readonly ReachyMainScreenStateStore stateStore =
            new ReachyMainScreenStateStore();

        public ReachySettingsMainScreenApplicationService(
            ReachyMainScreen screen,
            ReachyProductionAuthoritativeRuntime runtime,
            IReachySimulationService simulation,
            IReachyCameraService camera,
            IReachyAudioService audio,
            IReachyProviderService provider,
            IReachyPerceptionService perception,
            IReachyBehaviorService behavior,
            ReachySettingsPersistenceApplicationService persistence)
            : base(
                "main-screen",
                ReachyServiceKind.UserInterface,
                ReachyServiceCriticality.Required)
        {
            this.screen = screen ?? throw new ArgumentNullException(nameof(screen));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.persistence = persistence ??
                throw new ArgumentNullException(nameof(persistence));
            dependencies = new IReachyApplicationService[]
            {
                simulation ?? throw new ArgumentNullException(nameof(simulation)),
                camera ?? throw new ArgumentNullException(nameof(camera)),
                audio ?? throw new ArgumentNullException(nameof(audio)),
                provider ?? throw new ArgumentNullException(nameof(provider)),
                perception ?? throw new ArgumentNullException(nameof(perception)),
                behavior ?? throw new ArgumentNullException(nameof(behavior)),
                persistence,
            };
        }

        protected override void OnInitialize()
        {
            for (int index = 0; index < dependencies.Length; ++index)
            {
                dependencies[index].HealthChanged += OnDependencyHealthChanged;
            }
            persistence.Settings.Changed += OnSettingsChanged;

            UpdateMainScreenCapabilities();
            persistence.Settings.SetSimulationDiagnostics(
                BuildSimulationDiagnostics());
            stateStore.SetInteraction(
                ReachyInteractionState.Idle,
                "Robot view and settings are ready. Selected providers remain unavailable until their runtime integrations are installed.");
            screen.Bind(
                stateStore,
                persistence.Settings,
                BuildDiagnostics,
                ResetSimulation);
            SetReady("Main screen and durable settings are bound to application state.");
        }

        protected override void OnDispose()
        {
            persistence.Settings.Changed -= OnSettingsChanged;
            for (int index = 0; index < dependencies.Length; ++index)
            {
                dependencies[index].HealthChanged -= OnDependencyHealthChanged;
            }
        }

        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
        {
            UpdateMainScreenCapabilities();
        }

        private void OnDependencyHealthChanged(
            object? sender,
            ReachyServiceHealthChangedEventArgs eventArgs)
        {
            if (eventArgs.Health.Criticality == ReachyServiceCriticality.Required &&
                (eventArgs.Health.State == ReachyServiceState.Faulted ||
                 eventArgs.Health.State == ReachyServiceState.Unavailable))
            {
                stateStore.SetInteraction(
                    ReachyInteractionState.Error,
                    $"{eventArgs.Health.ServiceId}: {eventArgs.Health.Message}");
            }
        }

        private void UpdateMainScreenCapabilities()
        {
            int configured = 0;
            bool sendsOffDevice = false;
            foreach (ReachyProviderSelection provider in
                     persistence.Settings.Current.ProviderSelections)
            {
                if (provider.Execution == ReachyProviderExecution.Unconfigured)
                {
                    continue;
                }
                ++configured;
                sendsOffDevice |= provider.SendsDataOffDevice;
            }

            stateStore.SetCapabilities(
                "Fixed front / three-quarter",
                false,
                configured == 0
                    ? "Not configured"
                    : $"{configured}/4 preferences",
                configured == 0
                    ? ReachyProviderLocation.Unavailable
                    : sendsOffDevice
                        ? ReachyProviderLocation.Cloud
                        : ReachyProviderLocation.Local,
                false);
        }

        private ReachySettingsResetOutcome ResetSimulation()
        {
            ReachySimulationControlResult result = runtime.ResetNeutral();
            persistence.Settings.SetSimulationDiagnostics(
                BuildSimulationDiagnostics());
            return result.IsSuccess
                ? new ReachySettingsResetOutcome(
                    true,
                    $"runtime={runtime.Status}; simulation={runtime.SimulationState}")
                : new ReachySettingsResetOutcome(
                    false,
                    $"{result.Error.Code}: {result.Error.Message}");
        }

        private string BuildSimulationDiagnostics()
        {
            return
                $"runtime={runtime.Status}; simulation={runtime.SimulationState}; " +
                $"renderer={runtime.RendererStatus}; model_hash={runtime.ModelHash}; " +
                $"bodies={runtime.BodyCount}; worker_steps={runtime.WorkerStepCount}; " +
                $"fault={runtime.Fault}";
        }

        private string BuildDiagnostics()
        {
            var lines = new List<string>(dependencies.Length + 3)
            {
                "Application shell: active",
                $"Settings file: {persistence.PersistencePath}",
                $"Settings status: {persistence.Settings.Current.StatusMessage}",
            };
            if (!string.IsNullOrEmpty(persistence.LastPersistenceFault))
            {
                lines.Add(
                    $"Settings persistence fault: {persistence.LastPersistenceFault}");
            }
            for (int index = 0; index < dependencies.Length; ++index)
            {
                ReachyServiceHealth health = dependencies[index].Health;
                lines.Add(
                    $"{health.Kind}: {health.State} — {health.Message}");
            }
            return string.Join("\n", lines);
        }
    }
}
