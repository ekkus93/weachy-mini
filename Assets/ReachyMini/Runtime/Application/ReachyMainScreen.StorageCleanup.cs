#nullable enable

using System.IO;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyMainScreen
    {
        private string storageCleanupStatus =
            "Storage cleanup preserves installed models, settings, credentials, and active user state.";

        public string StorageCleanupStatus => storageCleanupStatus;

        public ReachyStorageCleanupOutcome CleanupRecoverableStorage()
        {
            string diagnosticDirectory = Path.Combine(
                Application.persistentDataPath,
                "diagnostics");
            var cleanup = new ReachyStorageCleanupCoordinator(diagnosticDirectory);
            ReachyStorageCleanupOutcome outcome = cleanup.CleanupRecoverableStorage();
            storageCleanupStatus = outcome.Detail;
            return outcome;
        }
    }
}
