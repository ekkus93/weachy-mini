#nullable enable

namespace ReachyMini.AppState
{
    public sealed partial class ReachyAndroidCameraTextureBridge
    {
        public void ReleaseForMemoryPressure()
        {
            if (disposed)
            {
                return;
            }

            InvalidateOutput(
                "Camera texture cache was released because Android reported low memory.");
        }
    }
}
