#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.Application
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

        public ReachyApplicationHost? Host => host;

        public ReachyApplicationHealthSnapshot? Health => host?.Health;

        public string Fault { get; private set; } = string.Empty;

        public void ConfigureCompositionProvider(MonoBehaviour provider)
        {
            if (host != null)
            {
                throw new InvalidOperationException(
                    "The composition provider cannot change after startup.");
            }
            compositionProvider = provider ??
                throw new ArgumentNullException(nameof(provider));
        }

        private void Start()
        {
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
                EnterFault(exception.Message);
            }
        }

        private void OnDestroy()
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
                Debug.LogError(
                    $"Reachy application disposal failed: {exception.Message}",
                    this);
            }
        }

        private void OnHealthChanged(
            object? sender,
            ReachyApplicationHealthChangedEventArgs eventArgs)
        {
            if (eventArgs.Snapshot.State == ReachyApplicationState.Faulted)
            {
                Fault = eventArgs.Snapshot.Message;
                Debug.LogError(
                    $"Reachy application fault: {Fault}",
                    this);
            }
        }

        private void EnterFault(string message)
        {
            Fault = string.IsNullOrWhiteSpace(message)
                ? "Reachy application startup failed without diagnostics."
                : message;
            Debug.LogError($"Reachy application startup failed: {Fault}", this);
        }
    }
}
