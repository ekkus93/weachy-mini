#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidOnDeviceAsrTests
{
    private static AndroidOnDeviceAsrProvider CreateProvider(
        FakeAndroidOnDeviceAsrPlatform platform,
        TimeSpan? maximumUtteranceDuration = null)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return new AndroidOnDeviceAsrProvider(
            platform,
            "android-on-device-test-instance",
            "en-US",
            maximumUtteranceDuration ?? TimeSpan.FromSeconds(30));
    }

    private static AsrOptions Options()
    {
        return new AsrOptions("en-US", true);
    }

    private static AsrRequest Request(
        AndroidOnDeviceAsrProvider provider,
        string requestId = "request-1",
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var selection = new SpeechProviderSelection(provider.Descriptor);
        return new AsrRequest(
            new SpeechOperationContext(
                requestId,
                selection.Current,
                timeout ?? TimeSpan.FromSeconds(1)),
            Options());
    }

    private static AndroidOnDeviceAsrSupportResult Support(
        AndroidOnDeviceAsrSupportState state,
        string diagnostic)
    {
        return new AndroidOnDeviceAsrSupportResult(state, diagnostic);
    }

    private static AndroidOnDeviceAsrPlatformEvent Event(
        string requestId,
        AndroidOnDeviceAsrPlatformEventKind kind,
        string? transcript = null)
    {
        return new AndroidOnDeviceAsrPlatformEvent(
            requestId,
            kind,
            transcript);
    }

    private static AndroidOnDeviceAsrPlatformEvent FailedPlatform(
        string requestId,
        AndroidOnDeviceAsrFailureKind kind,
        string code,
        string diagnostic)
    {
        return new AndroidOnDeviceAsrPlatformEvent(
            requestId,
            AndroidOnDeviceAsrPlatformEventKind.Failed,
            failure: new AndroidOnDeviceAsrPlatformFailure(
                kind,
                code,
                diagnostic));
    }

    private static async Task<IReadOnlyList<AsrEvent>> CollectAsync(
        IAsyncEnumerable<AsrEvent> stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var events = new List<AsrEvent>();
        await foreach (AsrEvent value in stream.ConfigureAwait(false))
        {
            events.Add(value);
        }
        return events;
    }

    private static void AssertSingleFailure(
        IReadOnlyList<AsrEvent> events,
        SpeechErrorCategory category,
        string code)
    {
        AssertEqual(1, events.Count, "failure event count");
        AssertFailure(events[0], category, code);
    }

    private static void AssertFailure(
        AsrEvent value,
        SpeechErrorCategory category,
        string code)
    {
        AssertEqual(AsrEventKind.Failed, value.Kind, "failure kind");
        SpeechProviderError error = value.Error ??
            throw new InvalidOperationException("Expected a structured provider error.");
        AssertEqual(category, error.Category, "error category");
        AssertEqual(code, error.Code, "error code");
    }

    private static async Task ExpectThrowsAsync<TException>(
        Func<Task> action)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            "Expected exception " + typeof(TException).Name + ".");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(
        T expected,
        T actual,
        string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                message + ": expected=" + expected + ", actual=" + actual + ".");
        }
    }
}
