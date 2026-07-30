#nullable enable

using ReachyMini.Interop;

namespace ReachyMini.Simulation
{
    public interface IReachyPublishedAuthoritativeStateSource
    {
        ReachySimAuthoritativeStateLayout AuthoritativeStateLayout { get; }

        ReachySimAuthoritativeStateFrame CreateAuthoritativeStateFrame();

        bool TryCaptureLatestAuthoritativeState(
            ReachySimAuthoritativeStateFrame destination);
    }
}
