namespace ReachyMini.Core
{
    public enum SimulationFidelity
    {
        Unavailable = 0,
        GeometricBaseline = 1,
        DynamicBaseline = 2,
        ServoFidelity = 3,
        UnitCalibratedTwin = 4,
        PopulationModel = 5,
    }

    public static class ProjectMetadata
    {
        public const string ProductName = "Weachy Mini";
        public const uint NativeAbiVersion = 1;
        public const uint NativeSnapshotFormatVersion = 1;
        public const ulong UncalibratedCalibrationProfileId = 0UL;
        public const double InitialPhysicsTimestepSeconds = 0.002;
        public const SimulationFidelity InitialFidelity = SimulationFidelity.Unavailable;

        public static bool IsSupportedPhysicsTimestep(double timestepSeconds)
        {
            return timestepSeconds > 0.0 && timestepSeconds <= 0.01;
        }
    }
}
