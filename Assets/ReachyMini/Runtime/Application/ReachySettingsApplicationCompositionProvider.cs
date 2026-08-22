#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using ReachyMini.Diagnostics;
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

        [SerializeField]
        private ReachyAndroidCameraDiscovery? cameraDiscovery;

        private bool compositionCreated;

        public void Configure(
            ReachyProductionAuthoritativeRuntime runtime,
            Camera camera,
            ReachyMainScreen screen,
            ReachyAndroidCameraDiscovery discovery)
        {
            if (compositionCreated)
            {
                throw new InvalidOperationException(
                    "Settings application dependencies cannot change after composition creation.");
            }
            productionRuntime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            presentationCamera = camera ?? throw new ArgumentNullException(nameof(camera));
            mainScreen = screen ?? throw new ArgumentNullException(nameof(screen));
            cameraDiscovery = discovery ??
                throw new ArgumentNullException(nameof(discovery));
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
            ReachyAndroidCameraDiscovery discovery = cameraDiscovery ??
                throw new InvalidOperationException(
                    "The settings application requires Android camera discovery.");

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
                            new ReachyDiscoveredCameraApplicationService(
                                camera,
                                discovery)),
                    new ReachyServiceRegistration(
                        "speech-audio",
                        ReachyServiceKind.Audio,
                        ReachyServiceCriticality.Optional,
                        Array.Empty<ReachyServiceKind>(),
                        resolver => new ReachyAndroidSpeechCapabilityApplicationService()),
                    new ReachyServiceRegistration(
                        "provider-selection",
                        ReachyServiceKind.Provider,
                        ReachyServiceCriticality.Optional,
                        new[] { ReachyServiceKind.Persistence },
                        resolver => new ReachyLocalLlmProviderApplicationService(
                            resolver.GetRequired<ReachySettingsPersistenceApplicationService>(
                                ReachyServiceKind.Persistence).Settings,
                            runtime)),
                    new ReachyServiceRegistration(
                        "perception",
                        ReachyServiceKind.Perception,
                        ReachyServiceCriticality.Optional,
                        new[]
                        {
                            ReachyServiceKind.Camera,
                            ReachyServiceKind.Provider,
                            ReachyServiceKind.Persistence,
                        },
                        resolver =>
                        {
                            // RMA-195 phase C: perception only needs the
                            // authoritative runtime (captured via `runtime`
                            // below) and the durable camera-calibration
                            // store. Camera/Provider stay declared
                            // dependencies for composition ordering -- the
                            // real service discovers the camera texture
                            // bridge itself (ReachyMainScreen.CameraPreview.cs's
                            // FindAnyObjectByType pattern), not through the
                            // Camera service boundary -- but are not
                            // otherwise consumed yet.
                            resolver.GetRequired<IReachyCameraService>(
                                ReachyServiceKind.Camera);
                            resolver.GetRequired<IReachyProviderService>(
                                ReachyServiceKind.Provider);
                            ReachyCameraCalibrationPersistenceStore calibrations =
                                resolver
                                    .GetRequired<ReachySettingsPersistenceApplicationService>(
                                        ReachyServiceKind.Persistence)
                                    .CameraCalibrations;
                            return new ReachyAndroidPerceptionApplicationService(
                                runtime,
                                calibrations);
                        }),
                    new ReachyServiceRegistration(
                        "baseline-behavior",
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
                            // RMA-195 phase A/C: baseline behavior needs the
                            // authoritative runtime (captured via `runtime`
                            // below, not through the Simulation service
                            // boundary) and, as of phase C, the real
                            // perception service for gaze-target world
                            // snapshots. Provider stays a declared dependency
                            // for composition ordering only -- still not
                            // consumed (phase D).
                            resolver.GetRequired<IReachySimulationService>(
                                ReachyServiceKind.Simulation);
                            resolver.GetRequired<IReachyProviderService>(
                                ReachyServiceKind.Provider);
                            IReachyPerceptionService perceptionService =
                                resolver.GetRequired<IReachyPerceptionService>(
                                    ReachyServiceKind.Perception);
                            return new ReachyBaselineBehaviorApplicationService(
                                runtime,
                                perceptionService);
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
                                ReachyServiceKind.Persistence),
                            discovery)),
                });
        }
    }

    internal sealed class ReachySettingsMainScreenApplicationService :
        ReachyApplicationServiceBase,
        IReachyUserInterfaceService,
        IReachyApplicationInterruptionParticipant
    {
        private readonly ReachyMainScreen screen;
        private readonly ReachyProductionAuthoritativeRuntime runtime;
        private readonly IReachyAudioService audio;
        private readonly IReachyApplicationService[] dependencies;
        private readonly ReachySettingsPersistenceApplicationService persistence;
        private readonly ReachyAndroidCameraDiscovery cameraDiscovery;
        private readonly ReachyDiagnosticsScreenSource diagnosticsSource;
        private readonly ReachyDiagnosticBundleExportCoordinator diagnosticBundleExporter;
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
            ReachySettingsPersistenceApplicationService persistence,
            ReachyAndroidCameraDiscovery cameraDiscovery)
            : base(
                "main-screen",
                ReachyServiceKind.UserInterface,
                ReachyServiceCriticality.Required)
        {
            this.screen = screen ?? throw new ArgumentNullException(nameof(screen));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.persistence = persistence ??
                throw new ArgumentNullException(nameof(persistence));
            this.cameraDiscovery = cameraDiscovery ??
                throw new ArgumentNullException(nameof(cameraDiscovery));
            this.audio = audio ?? throw new ArgumentNullException(nameof(audio));
            dependencies = new IReachyApplicationService[]
            {
                simulation ?? throw new ArgumentNullException(nameof(simulation)),
                camera ?? throw new ArgumentNullException(nameof(camera)),
                this.audio,
                provider ?? throw new ArgumentNullException(nameof(provider)),
                perception ?? throw new ArgumentNullException(nameof(perception)),
                behavior ?? throw new ArgumentNullException(nameof(behavior)),
                persistence,
            };
            diagnosticsSource = new ReachyDiagnosticsScreenSource(
                this.runtime,
                this.persistence.Settings,
                this.cameraDiscovery,
                dependencies);
            diagnosticBundleExporter = new ReachyDiagnosticBundleExportCoordinator(
                diagnosticsSource,
                Path.Combine(
                    Application.persistentDataPath,
                    "diagnostics"));
        }

        protected override void OnInitialize()
        {
            for (int index = 0; index < dependencies.Length; ++index)
            {
                dependencies[index].HealthChanged += OnDependencyHealthChanged;
            }
            persistence.Settings.Changed += OnSettingsChanged;
            cameraDiscovery.State.Changed += OnCameraCapabilitiesChanged;

            UpdateMainScreenCapabilities();
            persistence.Settings.SetSimulationDiagnostics(
                BuildSimulationDiagnostics());
            stateStore.SetInteraction(
                ReachyInteractionState.Idle,
                "Robot view and settings are ready. Selected providers remain unavailable until their runtime integrations are installed.");
            screen.Bind(
                stateStore,
                persistence.Settings,
                BuildDiagnosticsSnapshot,
                ResetSimulation,
                cameraDiscovery.State,
                cameraDiscovery.RequestAccessOrRefresh);
            screen.ConfigureDiagnosticBundleExport(
                diagnosticBundleExporter.ExportRedactedBundle);
            SetReady("Main screen and durable settings are bound to application state.");
        }

        protected override void OnDispose()
        {
            persistence.Settings.Changed -= OnSettingsChanged;
            cameraDiscovery.State.Changed -= OnCameraCapabilitiesChanged;
            diagnosticsSource.Dispose();
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

        private void OnCameraCapabilitiesChanged(
            object? sender,
            ReachyCameraCapabilityChangedEventArgs eventArgs)
        {
            UpdateMainScreenCapabilities();
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
            if (eventArgs.Health.Kind == ReachyServiceKind.Audio)
            {
                UpdateMainScreenCapabilities();
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
                cameraDiscovery.State.Current.SelectionAvailable,
                configured == 0
                    ? "Not configured"
                    : $"{configured}/4 preferences",
                configured == 0
                    ? ReachyProviderLocation.Unavailable
                    : sendsOffDevice
                        ? ReachyProviderLocation.Cloud
                        : ReachyProviderLocation.Local,
                audio.Health.State == ReachyServiceState.Ready);
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

        private ReachyDiagnosticsScreenSnapshot BuildDiagnosticsSnapshot()
        {
            return diagnosticsSource.Capture();
        }
    }
}
