#nullable enable

namespace ReachyMini.AppState
{
    public enum ReachySettingsSection
    {
        Providers = 0,
        Camera = 1,
        Speech = 2,
        LocalModel = 3,
        Simulation = 4,
        Privacy = 5,
        Licenses = 6,
        CloudLlm = 7,
        CloudVlm = 8,
    }

    public enum ReachyProviderKind
    {
        Asr = 0,
        Tts = 1,
        Llm = 2,
        Vlm = 3,
    }

    public enum ReachyProviderExecution
    {
        Unconfigured = 0,
        OnDevice = 1,
        AndroidService = 2,
        Cloud = 3,
    }

    public enum ReachyConnectivityRequirement
    {
        Unavailable = 0,
        OfflineCapable = 1,
        NetworkRequired = 2,
    }

    public enum ReachyCameraFacing
    {
        Unconfigured = 0,
        Front = 1,
        Rear = 2,
    }

    public enum ReachySimulationFidelity
    {
        Standard = 0,
        HighFidelity = 1,
    }
}
