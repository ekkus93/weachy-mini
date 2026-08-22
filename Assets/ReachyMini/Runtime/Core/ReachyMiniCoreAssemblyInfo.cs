using System.Runtime.CompilerServices;

// RMA-142/143: OpenAI-compatible LLM adapter mock-server contract tests need to
// construct ReachyOpenAiCompatibleLlmProviderBase-derived providers against a fake
// HttpMessageHandler via their internal transportOverride constructors, mirroring the
// (previously unreachable) internal seam already present on the RMA-144/145 ASR/TTS
// adapters. This file authorizes exactly the test project that uses it.
[assembly: InternalsVisibleTo("ReachyMini.Core.Tests")]
