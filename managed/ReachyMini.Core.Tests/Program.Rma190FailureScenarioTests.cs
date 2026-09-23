#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Providers;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static void TestRma190HttpFailureScenarioMatrix()
        {
            TestRma190HttpFailureScenarioMatrixAsync().GetAwaiter().GetResult();
        }

        private static async Task TestRma190HttpFailureScenarioMatrixAsync()
        {
            ReachyProviderProfile profile = new ReachyProviderProfile(
                "rma190-failure-provider",
                "RMA-190 Failure Provider",
                new Uri("https://rma190.example.test/"),
                ReachyProviderEndpointStyle.ChatCompletions,
                new[]
                {
                    new ReachyProviderModelBinding(
                        ReachyProviderModelRole.Text,
                        "rma190-test-model"),
                },
                Array.Empty<ReachyProviderHeaderBinding>(),
                1000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: null);

            await TestNetworkLossAsync(profile).ConfigureAwait(false);
            await TestRateLimitAsync(profile).ConfigureAwait(false);
            await TestCancellationAsync(profile).ConfigureAwait(false);
        }

        private static async Task TestNetworkLossAsync(
            ReachyProviderProfile profile)
        {
            using var handler = new Rma190HttpHandler((_, _) =>
                throw new HttpRequestException("simulated network loss"));
            using var transport = new ReachySharedHttpTransport(
                profile,
                secretStore: null,
                policy: RetryPolicy(maximumAttempts: 2),
                backoffDelay: new Rma190ImmediateBackoffDelay(),
                handler: handler);
            using ReachyHttpTransportRequest request = BufferedGet();

            ReachyHttpTransportResult result = await transport.SendAsync(
                    request,
                    eventSink: null,
                    CancellationToken.None)
                .ConfigureAwait(false);

            AssertEqual(false, result.Succeeded, "RMA-190 network loss success");
            AssertEqual(2, result.Attempts, "RMA-190 network loss retries");
            AssertEqual(
                ReachyHttpErrorCategory.Network,
                result.Error?.Category ?? ReachyHttpErrorCategory.None,
                "RMA-190 network loss category");
            AssertEqual(
                true,
                result.Error?.Retryable ?? false,
                "RMA-190 network loss retryable");
            AssertEqual(2, handler.CallCount, "RMA-190 network loss call count");
        }

        private static async Task TestRateLimitAsync(
            ReachyProviderProfile profile)
        {
            using var handler = new Rma190HttpHandler((request, _) =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    RequestMessage = request,
                };
                response.Headers.TryAddWithoutValidation(
                    "x-request-id",
                    "rma190-rate-limit");
                return response;
            });
            using var transport = new ReachySharedHttpTransport(
                profile,
                secretStore: null,
                policy: RetryPolicy(maximumAttempts: 1),
                backoffDelay: new Rma190ImmediateBackoffDelay(),
                handler: handler);
            using ReachyHttpTransportRequest request = BufferedGet();

            ReachyHttpTransportResult result = await transport.SendAsync(
                    request,
                    eventSink: null,
                    CancellationToken.None)
                .ConfigureAwait(false);

            AssertEqual(false, result.Succeeded, "RMA-190 rate-limit success");
            AssertEqual(
                ReachyHttpErrorCategory.QuotaOrRateLimited,
                result.Error?.Category ?? ReachyHttpErrorCategory.None,
                "RMA-190 rate-limit category");
            AssertEqual(
                429,
                result.Error?.HttpStatusCode ?? 0,
                "RMA-190 rate-limit status");
            AssertEqual(
                "rma190-rate-limit",
                result.Error?.ProviderRequestId ?? string.Empty,
                "RMA-190 rate-limit request id");
            AssertEqual(
                true,
                result.Error?.Retryable ?? false,
                "RMA-190 rate-limit retryable");
        }

        private static async Task TestCancellationAsync(
            ReachyProviderProfile profile)
        {
            using var handler = new Rma190HttpHandler((request, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent("{}"),
                });
            using var transport = new ReachySharedHttpTransport(
                profile,
                secretStore: null,
                policy: RetryPolicy(maximumAttempts: 1),
                backoffDelay: new Rma190ImmediateBackoffDelay(),
                handler: handler);
            using ReachyHttpTransportRequest request = BufferedGet();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            ReachyHttpTransportResult result = await transport.SendAsync(
                    request,
                    eventSink: null,
                    cancellation.Token)
                .ConfigureAwait(false);

            AssertEqual(false, result.Succeeded, "RMA-190 cancellation success");
            AssertEqual(
                ReachyHttpErrorCategory.Cancelled,
                result.Error?.Category ?? ReachyHttpErrorCategory.None,
                "RMA-190 cancellation category");
            AssertEqual(
                0,
                handler.CallCount,
                "RMA-190 cancellation network calls");
        }

        private static void TestRma190SolverFaultControlledReset()
        {
            byte[] modelBytes = System.Text.Encoding.UTF8.GetBytes(
                "rma190-solver-fault-reset-model");
            ReachySimCreateResult firstCreate = ReachySimSession.Create(modelBytes);
            ReachySimSession firstSession = firstCreate.Session ??
                throw new InvalidOperationException(
                    $"RMA-190 fault worker native create failed: {firstCreate.Error.Code}: {firstCreate.Error.Message}");

            using (var faultedWorker = new ReachySimulationWorker(
                firstSession,
                commandQueueCapacity: 2,
                maximumCommandBytes: 64))
            {
                AssertControlSuccess(
                    faultedWorker.Start(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 fault worker start");
                AssertControlSuccess(
                    faultedWorker.Pause(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 fault worker pause");
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.Accepted,
                    faultedWorker.EnqueueCommandBatch(CreateCommandBatch(1UL)),
                    "RMA-190 first command enqueue");
                AssertControlSuccess(
                    faultedWorker.Resume(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 fault worker resume");
                _ = WaitForSnapshot(
                    faultedWorker,
                    snapshot => snapshot.State.Sequence >= 1UL,
                    TimeSpan.FromSeconds(5.0),
                    "RMA-190 first command application");

                AssertControlSuccess(
                    faultedWorker.Pause(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 fault worker second pause");
                AssertEqual(
                    ReachySimulationCommandEnqueueResult.Accepted,
                    faultedWorker.EnqueueCommandBatch(CreateCommandBatch(1UL)),
                    "RMA-190 stale command enqueue");
                AssertControlSuccess(
                    faultedWorker.Resume(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 stale command resume");

                ReachySimulationFault fault = WaitForWorkerFault(
                    faultedWorker,
                    TimeSpan.FromSeconds(5.0),
                    "RMA-190 solver/command fault");
                AssertEqual(
                    ReachySimulationRunState.Faulted,
                    faultedWorker.State,
                    "RMA-190 faulted worker state");
                AssertEqual(
                    ReachySimErrorCode.CommandFormatError,
                    fault.Error.Code,
                    "RMA-190 retained worker fault code");
                AssertControlSuccess(
                    faultedWorker.Shutdown(TimeSpan.FromSeconds(5.0)),
                    "RMA-190 faulted worker shutdown");
            }

            ReachySimCreateResult recoveryCreate =
                ReachySimSession.Create(modelBytes);
            ReachySimSession recoverySession = recoveryCreate.Session ??
                throw new InvalidOperationException(
                    $"RMA-190 recovery worker native create failed: {recoveryCreate.Error.Code}: {recoveryCreate.Error.Message}");
            using var recoveryWorker = new ReachySimulationWorker(recoverySession);
            AssertControlSuccess(
                recoveryWorker.Start(TimeSpan.FromSeconds(5.0)),
                "RMA-190 recovery worker start");
            AssertControlSuccess(
                recoveryWorker.Pause(TimeSpan.FromSeconds(5.0)),
                "RMA-190 recovery worker pause");
            ReachySimulationControlResult reset = recoveryWorker.Reset(
                (uint)ReachySimResetPose.NeutralAwake,
                TimeSpan.FromSeconds(5.0));
            AssertControlSuccess(reset, "RMA-190 controlled neutral reset");
            ReachyPublishedSimulationSnapshot recovered = WaitForSnapshot(
                recoveryWorker,
                snapshot => snapshot.State.Sequence == 0UL &&
                    snapshot.State.SimulationTime == 0.0,
                TimeSpan.FromSeconds(5.0),
                "RMA-190 controlled reset snapshot");
            AssertEqual(
                0UL,
                recovered.State.Sequence,
                "RMA-190 controlled reset sequence");
            AssertControlSuccess(
                recoveryWorker.Shutdown(TimeSpan.FromSeconds(5.0)),
                "RMA-190 recovery worker shutdown");
        }

        private static ReachyHttpTransportPolicy RetryPolicy(
            int maximumAttempts)
        {
            return new ReachyHttpTransportPolicy(
                connectionTimeoutMilliseconds: 1000,
                readTimeoutMilliseconds: 1000,
                maximumAttempts: maximumAttempts,
                baseBackoffMilliseconds: 0,
                maximumBackoffMilliseconds: 0);
        }

        private static ReachyHttpTransportRequest BufferedGet()
        {
            return new ReachyHttpTransportRequest(
                HttpMethod.Get,
                "v1/rma190",
                body: null,
                contentType: null,
                accept: "application/json",
                ReachyHttpResponseMode.Buffered,
                maximumResponseBytes: 4096,
                maximumSseEventCharacters: 4096,
                explicitlyAuthorizeNonIdempotentRetry: false,
                idempotencyKey: null);
        }

        private sealed class Rma190HttpHandler : HttpMessageHandler
        {
            private readonly Func<
                HttpRequestMessage,
                CancellationToken,
                HttpResponseMessage> respond;

            public Rma190HttpHandler(
                Func<
                    HttpRequestMessage,
                    CancellationToken,
                    HttpResponseMessage> respond)
            {
                this.respond = respond ??
                    throw new ArgumentNullException(nameof(respond));
            }

            public int CallCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                CallCount = checked(CallCount + 1);
                HttpResponseMessage response =
                    respond(request, cancellationToken);
                response.RequestMessage ??= request;
                return Task.FromResult(response);
            }
        }

        private sealed class Rma190ImmediateBackoffDelay :
            IReachyHttpBackoffDelay
        {
            public ValueTask DelayAsync(
                TimeSpan delay,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }
        }
    }
}
