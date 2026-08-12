#nullable enable

using System;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private static void ConfigurationRequiresNetworkLocation()
        {
            Throws<ArgumentException>(
                () => Configuration(location: VisionProviderLocation.OnDevice),
                "on-device remote configuration");
        }

        private static void ConfigurationRequiresSemanticCapability()
        {
            Throws<ArgumentException>(
                () => Configuration(
                    supportsVisualQuestions: false,
                    supportsSceneDescription: false),
                "missing semantic capability");
        }

        private static void ConfigurationKeepsModelIdConfigurable()
        {
            OpenAiVisionProviderConfiguration configuration =
                Configuration(modelId: "user-selected-model");
            Equal("user-selected-model", configuration.ModelId, "model id");
        }

        private static void ConfigurationPublishesExactCapabilities()
        {
            OpenAiVisionProviderConfiguration configuration = Configuration(
                maximumConcurrentOperations: 2,
                maximumPromptCharacters: 1234);
            ProviderDescriptor descriptor = configuration.CreateDescriptor();
            VisionLanguageCapabilities capabilities =
                configuration.CreateCapabilities();
            Equal(
                VisionProviderKind.SemanticVisionLanguage,
                descriptor.Kind,
                "descriptor kind");
            Equal(VisionProviderLocation.Cloud, descriptor.Location, "location");
            True(descriptor.RequiresNetworkDisclosure, "network disclosure");
            True(capabilities.SupportsVisualQuestions, "visual questions");
            True(capabilities.SupportsSceneDescription, "scene description");
            True(capabilities.SupportsCancellation, "cancellation");
            Equal(2, capabilities.MaximumConcurrentOperations, "concurrency");
            Equal(1234, capabilities.MaximumPromptCharacters, "prompt bound");
        }

        private static void ResponsesProviderRequiresResponsesConfiguration()
        {
            Throws<ArgumentException>(
                () => new OpenAiResponsesVisionLanguageProvider(
                    Configuration(OpenAiVisionEndpointStyle.ChatCompletions),
                    new FakeTransport(OpenAiVisionEndpointStyle.Responses),
                    new FakeEncoder()),
                "responses configuration mismatch");
        }

        private static void ResponsesProviderRequiresResponsesTransport()
        {
            Throws<ArgumentException>(
                () => new OpenAiResponsesVisionLanguageProvider(
                    Configuration(OpenAiVisionEndpointStyle.Responses),
                    new FakeTransport(OpenAiVisionEndpointStyle.ChatCompletions),
                    new FakeEncoder()),
                "responses transport mismatch");
        }

        private static void ChatProviderRequiresChatConfiguration()
        {
            Throws<ArgumentException>(
                () => new OpenAiChatCompletionsVisionLanguageProvider(
                    Configuration(OpenAiVisionEndpointStyle.Responses),
                    new FakeTransport(OpenAiVisionEndpointStyle.ChatCompletions),
                    new FakeEncoder()),
                "chat configuration mismatch");
        }

        private static void ChatProviderRequiresChatTransport()
        {
            Throws<ArgumentException>(
                () => new OpenAiChatCompletionsVisionLanguageProvider(
                    Configuration(OpenAiVisionEndpointStyle.ChatCompletions),
                    new FakeTransport(OpenAiVisionEndpointStyle.Responses),
                    new FakeEncoder()),
                "chat transport mismatch");
        }
    }
}
