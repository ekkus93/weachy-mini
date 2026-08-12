#nullable enable

using System.Runtime.InteropServices;
using ReachyMini.Interop;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private static void TestNativeAbi2Layouts()
    {
        Require(ReachyLlamaNativeContract.AbiVersion == 2U, "Managed runtime ABI is not 2.");
        Require(Marshal.SizeOf<NativeReachyLlamaErrorInfo>() == 392, "error_info ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaModelConfig>() == 16, "model_config ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaGenerationConfig>() == 48, "generation_config ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaGenerationConstraint>() == 48, "constraint ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaChatMessage>() == 16, "chat_message ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaGenerationEvent>() == 24, "generation_event ABI layout drifted.");
        Require(Marshal.SizeOf<NativeReachyLlamaGenerationMetrics>() == 72, "generation_metrics ABI layout drifted.");
    }

    private static void TestRma133BaselineProfile()
    {
        LocalLlmExecutionProfile profile = LocalLlmExecutionProfile.CreateRma133V6Baseline();
        Require(profile.ContextTokens == 2048, "RMA-133 context profile drifted.");
        Require(profile.BatchTokens == 256, "RMA-133 batch profile drifted.");
        Require(profile.MicroBatchTokens == 64, "RMA-133 micro-batch profile drifted.");
        Require(profile.MaximumGeneratedTokens == 128, "RMA-133 output-token profile drifted.");
        Require(profile.Threads == 4 && profile.BatchThreads == 4, "RMA-133 thread profile drifted.");
        Require(profile.Temperature == 0.0F && profile.MinP == 0.0F, "RMA-133 sampling profile drifted.");
        Require(profile.Seed == 133U, "RMA-133 seed drifted.");
        Require(profile.StreamQueueCapacity == 64, "RMA-133 queue profile drifted.");
    }
}
