using System;
using System.Collections.Generic;
using ReachyMini.Core;

namespace ReachyMini.Core.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            AssertEqual(SimulationFidelity.Unavailable, ProjectMetadata.InitialFidelity, "initial fidelity");
            AssertEqual(true, ProjectMetadata.IsSupportedPhysicsTimestep(0.002), "500 Hz timestep");
            AssertEqual(false, ProjectMetadata.IsSupportedPhysicsTimestep(0.0), "zero timestep");
            AssertEqual(false, ProjectMetadata.IsSupportedPhysicsTimestep(0.02), "oversized timestep");
            return 0;
        }

        private static void AssertEqual<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {description}: expected {expected}, actual {actual}.");
            }
        }
    }
}
