#nullable enable

using ReachyMini.AppState;

namespace ReachyMini.Tests
{
    internal static class ReachyCameraDiscoveryTestExtensions
    {
        public static void RefreshNowForTests(
            this ReachyAndroidCameraDiscovery discovery)
        {
            discovery.RefreshPermissionAndCapabilities();
        }
    }
}
