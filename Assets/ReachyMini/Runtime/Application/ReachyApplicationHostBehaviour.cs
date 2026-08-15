#nullable enable

using System;
using UnityEngine;
using ReachyMini.Diagnostics;
using ReachyMini.RuntimeDiagnostics;

namespace ReachyMini.AppState
{
    public interface IReachyApplicationCompositionProvider
    {
        ReachyApplicationComposition CreateApplicationComposition();
    }

    [DisallowMultipleComponent]
    public sealed class ReachyApplicationHostBehaviour : MonoBehaviour
    {
        [SerializeField]
        private MonoBehaviour? compositionProvider;

        private ReachyApplicationHost? host;
        private ReachyApplicationInterruptionCoordinator? interruptionCoordinator;
        private bool lowMemorySubscribed;
        private bool startupEntered;

        public ReachyApplicationHost? Host => host;

        public ReachyApplicationHealthSnapshot? Health => host?.Health;

        public string Fault { get; private set; } = string.Empty;

        public void ConfigureCompositionProvider(MonoBehaviour provider)
        {
            if (startupEntered)
            {
                throw new InvalidOperationException(
                    "The composition provider cannot change after startup begins.");
            }
            compositionProvider = provider ??
                throw new ArgumentNullException(nameof(provider));
        }

        public void StartApplication()
        {
            if (startupEntered)
            {
                throw new InvalidOperationException(
                    "The Reachy application host cannot be started more than once.");
            }
            startupEntered = true;

            if (compositionProvider is not IReachyApplicationCompositionProvider provider)
            {
                EnterFault(
                    "Reachy application startup requires an explicit composition provider.");
                return;
            }

            try
            {
                ReachyApplicationComposition composition =
                    provider.CreateApplicationComposition() ??
                    throw new InvalidOperationException(
                        "The application composition provider returned null.");
                host = new ReachyApplicationHost(composition);
                host.HealthChanged += OnHealthChanged;
                host.Start();
                interruptionCoordinator =
                    new ReachyApplicationInterruptionCoordinator(host);
                Application.lowMemory += OnLowMemory;
                lowMemorySubscribed = true;
            }
            catch (Exception exception)
            {
                ShutdownApplication();
                ReachyRuntimeDiagnostics.Emit(
                    "application",
                    ReachyDiagnosticEventIds.ApplicationStartupFailed,
                    ReachyDiagnosticSeverity.Error,
                    ReachyDiagnosticErrorCategory.Lifecycle,
                    new ReachyDiagnosticField(
                        "exception_type",
                        exception.GetType().Name,
                        ReachyDiagnosticDataClass.Identifier));
                EnterFault(
                    "Application lifecycle operation failed (" +
                    exception.GetType().Name + ").");
            }
        }

        public void ShutdownApplication()
        {
            if (lowMemorySubscribed)
            {
                Application.lowMemory -= OnLowMemory;
                lowMemorySubscribed = false;
            }

            ReachyApplicationInterruptionCoordinator? coordinator =
                interruptionCoordinator;
            interruptionCoordinator = null;
            coordinator?.Dispose();

            ReachyApplicationHost? activeHost = host;
            host = null;
            if (activeHost == null)
            {
                return;
            }

            activeHost.HealthChanged -= OnHealthChanged;
            try
            {
                activeHost.Dispose();
            }
            catch (Exception exception)
            {
                ReachyRuntimeDiagnostics.Emit(
                    "application",
                    ReachyDiagnosticEventIds.ApplicationDisposalFailed,
                    ReachyDiagnosticSeverity.Error,
                    ReachyDiagnosticErrorCategory.Lifecycle,
                    new ReachyDiagnosticField(
                        "exception_type",
                        exception.GetType().Name,
                        ReachyDiagnosticDataClass.Identifier));
            }
        }

        private void Start()
        {
            StartApplication();
        }

        private void OnLowMemory()
        {
            if (host == null)
            {
                return;
            }

            int directFailureCount = 0;
            ReachyAndroidCameraTextureBridge? textureBridge =
                UnityEngine.Object.FindAnyObjectByType<
                    ReachyAndroidCameraTextureBridge>();
            try
            {
                textureBridge?.ReleaseForMemoryPressure();
            }
            catch (Exception)
            {
                directFailureCount = checked(directFailureCount + 1);
            }

            ReachyMemoryPressureSweepResult sweep =
                ReachyMemoryPressureRegistry.ReleaseRegisteredResources();
            try
            {
                _ = Resources.UnloadUnusedAssets();
            }
            catch (Exception)
            {
                directFailureCount = checked(directFailureCount + 1);
            }

            int totalFailureCount = checked(
                sweep.FailureCount + directFailureCount);
            ReachyRuntimeDiagnostics.Emit(
                "application",
                ReachyDiagnosticEventIds.ApplicationLowMemoryHandled,
                totalFailureCount == 0
                    ? ReachyDiagnosticSeverity.Warning
                    : ReachyDiagnosticSeverity.Error,
                ReachyDiagnosticErrorCategory.Resource,
                new ReachyDiagnosticField(
                    "participants",
                    sweep.ParticipantCount.ToString(),
                    ReachyDiagnosticDataClass.Identifier),
                new ReachyDiagnosticField(
                    "released",
                    sweep.ReleasedCount.ToString(),
                    ReachyDiagnosticDataClass.Identifier),
                new ReachyDiagnosticField(
                    "retained_active",
                    sweep.RetainedActiveCount.ToString(),
                    ReachyDiagnosticDataClass.Identifier),
                new ReachyDiagnosticField(
                    "failures",
                    totalFailureCount.ToString(),
                    ReachyDiagnosticDataClass.Identifier));
        }

        private void OnApplicationPause(bool paused)
        {
            ReachyApplicationInterruptionCoordinator? coordinator =
                interruptionCoordinator;
            if (coordinator == null || host == null)
            {
                return;
            }

            ReachyAndroidCameraAcquisition? acquisition =
                UnityEngine.Object.FindAnyObjectByType<
                    ReachyAndroidCameraAcquisition>();
            try
            {
                ReachyApplicationInterruptionResult result;
                if (paused)
                {
                    acquisition?.PauseForApplicationInterruption();
                    result = coordinator.Pause();
                }
                else
                {
                    result = coordinator.Resume();
                    if (result.Succeeded)
                    {
                        acquisition?.ResumeAfterApplicationInterruption();
                    }
                }

                if (!result.Succeeded)
                {
                    EnterFault(
                        "Application interruption transition failed: " +
                        result.Diagnostic);
                }
            }
            catch (Exception exception)
            {
                EnterFault(
                    "Application interruption transition failed (" +
                    exception.GetType().Name + ").");
            }
        }

        private void OnDestroy()
        {
            ShutdownApplication();
        }

        private void OnHealthChanged(
            object? sender,
            ReachyApplicationHealthChangedEventArgs eventArgs)
        {
            if (eventArgs.Snapshot.State == ReachyApplicationState.Faulted)
            {
                Fault = eventArgs.Snapshot.Message;
                ReachyRuntimeDiagnostics.Emit(
                    "application",
                    ReachyDiagnosticEventIds.ApplicationFaulted,
                    ReachyDiagnosticSeverity.Error,
                    ReachyDiagnosticErrorCategory.Lifecycle,
                    new ReachyDiagnosticField(
                        "state",
                        eventArgs.Snapshot.State.ToString(),
                        ReachyDiagnosticDataClass.Identifier));
            }
        }

        private void EnterFault(string message)
        {
            Fault = string.IsNullOrWhiteSpace(message)
                ? "Reachy application startup failed without diagnostics."
                : message;
            ReachyRuntimeDiagnostics.Emit(
                "application",
                ReachyDiagnosticEventIds.ApplicationStartupFailed,
                ReachyDiagnosticSeverity.Error,
                ReachyDiagnosticErrorCategory.Lifecycle,
                new ReachyDiagnosticField(
                    "state",
                    "faulted",
                    ReachyDiagnosticDataClass.Identifier));
        }
    }
}
