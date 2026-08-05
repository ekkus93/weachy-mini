#!/usr/bin/env python3
from pathlib import Path

PATH = Path("managed/ReachyMini.Camera.Tests/Rma110VisionProviderContracts.cs")


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"unexpected {label} count: {count}")
    return source.replace(old, new)


def main() -> None:
    source = PATH.read_text(encoding="utf-8")
    old = """            ProviderKindsAndCapabilitiesRemainExplicit();
            await TransformedFramesRequireOwnedColorValidityAndCoverageAsync()
                .ConfigureAwait(false);
            await FrameSourceRejectsRawFallbackAndStaleSequenceAsync()
                .ConfigureAwait(false);
            await CallerCancellationReturnsTypedFailureAsync()
                .ConfigureAwait(false);
            await TimeoutQuarantinesProviderAsync().ConfigureAwait(false);
            await ProviderFaultRemainsVisibleAsync().ConfigureAwait(false);
            await ProviderSwitchSupersedesLateResultsAsync()
                .ConfigureAwait(false);
            await ResultIdentityMismatchFailsClosedAsync()
                .ConfigureAwait(false);
            await CloudDisclosureIsRequiredBeforeInvocationAsync()
                .ConfigureAwait(false);
            await FrameResourcesDisposeExactlyOnceAsync()
                .ConfigureAwait(false);
"""
    new = """            Console.WriteLine("RMA-110 provider capability contract starting.");
            ProviderKindsAndCapabilitiesRemainExplicit();
            Console.WriteLine("RMA-110 transformed frame ownership contract starting.");
            await TransformedFramesRequireOwnedColorValidityAndCoverageAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-110 frame-source rejection contract starting.");
            await FrameSourceRejectsRawFallbackAndStaleSequenceAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-110 caller cancellation contract starting.");
            await CallerCancellationReturnsTypedFailureAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-110 timeout quarantine contract starting.");
            await TimeoutQuarantinesProviderAsync().ConfigureAwait(false);
            Console.WriteLine("RMA-110 provider fault contract starting.");
            await ProviderFaultRemainsVisibleAsync().ConfigureAwait(false);
            Console.WriteLine("RMA-110 provider switch contract starting.");
            await ProviderSwitchSupersedesLateResultsAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-110 result identity contract starting.");
            await ResultIdentityMismatchFailsClosedAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-110 cloud disclosure contract starting.");
            await CloudDisclosureIsRequiredBeforeInvocationAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-110 exactly-once disposal contract starting.");
            await FrameResourcesDisposeExactlyOnceAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-110 managed contracts completed.");
"""
    source = replace_once(source, old, new, "RMA-110 run sequence")

    bounded = """            cancellation.Cancel();
            Task completed = await Task.WhenAny(
                pending,
                Task.Delay(
                    TimeSpan.FromSeconds(1.0),
                    CancellationToken.None)).ConfigureAwait(false);
            if (completed != pending)
            {
                throw new InvalidOperationException(
                    "Managed test failed: caller cancellation did not complete within one second.");
            }
            TrackingResult result = await pending.ConfigureAwait(false);
"""
    traced = """            Console.WriteLine("RMA-110 cancellation pending task created.");
            cancellation.Cancel();
            Console.WriteLine("RMA-110 cancellation source returned from Cancel().");
            Task completed = await Task.WhenAny(
                pending,
                Task.Delay(
                    TimeSpan.FromSeconds(1.0),
                    CancellationToken.None)).ConfigureAwait(false);
            Console.WriteLine(
                completed == pending
                    ? "RMA-110 cancellation pending task won."
                    : "RMA-110 cancellation safety timeout won.");
            if (completed != pending)
            {
                throw new InvalidOperationException(
                    "Managed test failed: caller cancellation did not complete within one second.");
            }
            TrackingResult result = await pending.ConfigureAwait(false);
            Console.WriteLine("RMA-110 cancellation result awaited.");
"""
    source = replace_once(source, bounded, traced, "caller cancellation trace")

    PATH.write_text(source, encoding="utf-8")
    Path(__file__).unlink()


if __name__ == "__main__":
    main()
