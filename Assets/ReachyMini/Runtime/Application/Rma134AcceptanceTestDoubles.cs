#nullable enable

using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

namespace ReachyMini.Validation
{
    internal sealed class Rma134CollectingSink : ILocalLlmStreamSink
    {
        public int TextEventCount { get; private set; }
        public int TextUtf8Bytes { get; private set; }
        public bool TerminalValidated { get; private set; }
        public bool SawTrustedPartialOutput { get; private set; }

        public ValueTask OnEventAsync(LocalLlmStreamEvent streamEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (streamEvent.Type == LocalLlmStreamEventType.Text)
            {
                ++TextEventCount;
                TextUtf8Bytes = checked(TextUtf8Bytes + Encoding.UTF8.GetByteCount(streamEvent.Text));
                SawTrustedPartialOutput |= streamEvent.IsTrustedExecutableOutput;
            }
            else if (streamEvent.Type == LocalLlmStreamEventType.Completed)
            {
                TerminalValidated = true;
            }
            return default;
        }
    }

    internal sealed class Rma134CancelOnFirstTextSink : ILocalLlmStreamSink
    {
        private readonly CancellationTokenSource cancellation;

        internal Rma134CancelOnFirstTextSink(CancellationTokenSource cancellation)
        {
            this.cancellation = cancellation;
        }

        public int TextEventCount { get; private set; }

        public ValueTask OnEventAsync(LocalLlmStreamEvent streamEvent, CancellationToken cancellationToken)
        {
            if (streamEvent.Type == LocalLlmStreamEventType.Text)
            {
                ++TextEventCount;
                cancellation.Cancel();
            }
            return default;
        }
    }
}
