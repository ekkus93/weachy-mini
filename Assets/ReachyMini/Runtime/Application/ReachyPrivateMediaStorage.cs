#nullable enable

using UnityEngine;

namespace ReachyMini.AppState
{
    public static class ReachyPrivateMediaStorage
    {
        public static ReachyPrivateMediaTemporaryFileStore CreateTemporaryStore()
        {
            var store = new ReachyPrivateMediaTemporaryFileStore(
                Application.temporaryCachePath);
            _ = store.PurgeAbandonedFiles();
            return store;
        }
    }
}
