#nullable enable

using System;

namespace ReachyMini.Speech
{
    public static class SpeechProviderContract
    {
        public static void ValidateProviderForOperation(
            SpeechProviderDescriptor descriptor,
            SpeechOperationContext context)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (descriptor.Kind != context.ProviderKind ||
                !string.Equals(
                    descriptor.InstanceId,
                    context.ProviderInstanceId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The selected speech operation cannot be redirected to another provider instance.");
            }
        }

        public static void ValidateEventOrigin(
            SpeechOperationContext context,
            string providerInstanceId,
            string requestId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!string.Equals(
                    context.ProviderInstanceId,
                    providerInstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    context.RequestId,
                    requestId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Speech output does not belong to the selected provider operation.");
            }
        }
    }
}
