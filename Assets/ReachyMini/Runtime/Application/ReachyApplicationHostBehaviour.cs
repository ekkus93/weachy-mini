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
