using NUnit.Framework;
using ReachyMini.Core;

namespace ReachyMini.Tests
{
    public sealed class ProjectMetadataTests
    {
        [Test]
        public void InitialFidelityIsUnavailableUntilSimulatorLoads()
        {
            Assert.That(ProjectMetadata.InitialFidelity, Is.EqualTo(SimulationFidelity.Unavailable));
        }

        [TestCase(0.002, true)]
        [TestCase(0.01, true)]
        [TestCase(0.0, false)]
        [TestCase(-0.002, false)]
        [TestCase(0.02, false)]
        public void PhysicsTimestepValidationIsBounded(double timestep, bool expected)
        {
            Assert.That(ProjectMetadata.IsSupportedPhysicsTimestep(timestep), Is.EqualTo(expected));
        }
    }
}
