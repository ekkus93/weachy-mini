#!/usr/bin/env python3
"""Apply the guarded RMA-051 reusable production-pose wrapper patch."""

from pathlib import Path


RUNTIME_PATH = Path(
    "Assets/ReachyMini/Runtime/Rendering/ReachyProductionAuthoritativeRuntime.cs"
)

OLD = """        private sealed class FaultCapturingPoseSource : IReachyAuthoritativePoseSource
        {
            private readonly IReachyAuthoritativePoseSource inner;

            public FaultCapturingPoseSource(IReachyAuthoritativePoseSource inner)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public string Fault { get; private set; } = string.Empty;

            public bool TryGetLatestPair(
                out ReachyAuthoritativePoseSnapshot older,
                out ReachyAuthoritativePoseSnapshot newer)
            {
                if (!string.IsNullOrEmpty(Fault))
                {
                    older = null!;
                    newer = null!;
                    return false;
                }
                try
                {
                    return inner.TryGetLatestPair(out older, out newer);
                }
                catch (Exception exception)
                {
                    Fault = exception.Message;
                    older = null!;
                    newer = null!;
                    return false;
                }
            }
        }
"""

NEW = """        private sealed class FaultCapturingPoseSource :
            IReachyReusableAuthoritativePoseSource
        {
            private readonly IReachyAuthoritativePoseSource inner;
            private readonly IReachyReusableAuthoritativePoseSource reusableInner;

            public FaultCapturingPoseSource(IReachyAuthoritativePoseSource inner)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
                reusableInner = inner as IReachyReusableAuthoritativePoseSource ??
                    throw new ArgumentException(
                        \"The production pose source must provide reusable pose buffers.\",
                        nameof(inner));
            }

            public string Fault { get; private set; } = string.Empty;

            public int BodyCount => reusableInner.BodyCount;

            public ReachyReusableAuthoritativePoseFrame CreatePoseFrame()
            {
                return reusableInner.CreatePoseFrame();
            }

            public bool TryCopyLatestPair(
                ReachyReusableAuthoritativePoseFrame olderDestination,
                ReachyReusableAuthoritativePoseFrame newerDestination)
            {
                if (!string.IsNullOrEmpty(Fault))
                {
                    return false;
                }
                try
                {
                    return reusableInner.TryCopyLatestPair(
                        olderDestination,
                        newerDestination);
                }
                catch (Exception exception)
                {
                    Fault = exception.Message;
                    return false;
                }
            }

            public bool TryGetLatestPair(
                out ReachyAuthoritativePoseSnapshot older,
                out ReachyAuthoritativePoseSnapshot newer)
            {
                if (!string.IsNullOrEmpty(Fault))
                {
                    older = null!;
                    newer = null!;
                    return false;
                }
                try
                {
                    return inner.TryGetLatestPair(out older, out newer);
                }
                catch (Exception exception)
                {
                    Fault = exception.Message;
                    older = null!;
                    newer = null!;
                    return false;
                }
            }
        }
"""


def main() -> None:
    source = RUNTIME_PATH.read_text(encoding="utf-8")
    occurrences = source.count(OLD)
    if occurrences != 1:
        raise SystemExit(
            f"Expected exactly one guarded runtime wrapper block, found {occurrences}."
        )
    RUNTIME_PATH.write_text(source.replace(OLD, NEW), encoding="utf-8")


if __name__ == "__main__":
    main()
