#nullable enable

using System;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed class ReachyAndroidSpeechAudioStack : IAsyncDisposable
    {
        internal ReachyAndroidSpeechAudioStack(
            IAsrProvider asr,
            ITtsProvider tts,
            SpeechAudioFocusCoordinator audio)
        {
            Asr = asr ?? throw new ArgumentNullException(nameof(asr));
            Tts = tts ?? throw new ArgumentNullException(nameof(tts));
            Audio = audio ?? throw new ArgumentNullException(nameof(audio));
        }

        public IAsrProvider Asr { get; }
        public ITtsProvider Tts { get; }
        public SpeechAudioFocusCoordinator Audio { get; }

        public async ValueTask DisposeAsync()
        {
            Exception? firstFailure = null;
            try
            {
                await Asr.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstFailure = exception;
            }

            try
            {
                await Tts.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
            }

            try
            {
                await Audio.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
            }

            GC.SuppressFinalize(this);
            if (firstFailure != null)
            {
                throw new InvalidOperationException(
                    "Disposing the Android speech audio stack failed; all owned components were still given a teardown attempt.",
                    firstFailure);
            }
        }
    }

    public static class ReachyAndroidSpeechAudioStackFactory
    {
        public static async ValueTask<ReachyAndroidSpeechAudioStack>
            CreateOfflineDefaultAsync(
                string asrInstanceId,
                string ttsInstanceId,
                string languageTag,
                TimeSpan maximumUtteranceDuration)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var audio = new SpeechAudioFocusCoordinator(
                new ReachyAndroidSpeechAudioFocusPlatform());
            IAsrProvider? rawAsr = null;
            ITtsProvider? rawTts = null;
            try
            {
                rawAsr = ReachyAndroidOnDeviceAsrProviderFactory.Create(
                    asrInstanceId,
                    languageTag,
                    maximumUtteranceDuration);
                rawTts = ReachyAndroidOfflineTtsProviderFactory.Create(
                    ttsInstanceId,
                    languageTag);
                var coordinatedAsr = new AudioCoordinatedAsrProvider(
                    rawAsr,
                    audio,
                    ownsInner: true);
                var coordinatedTts = new AudioCoordinatedTtsProvider(
                    rawTts,
                    audio,
                    ownsInner: true);
                rawAsr = null;
                rawTts = null;
                return new ReachyAndroidSpeechAudioStack(
                    coordinatedAsr,
                    coordinatedTts,
                    audio);
            }
            catch (Exception creationFailure)
            {
                Exception? cleanupFailure = null;
                if (rawAsr != null)
                {
                    try
                    {
                        await rawAsr.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        cleanupFailure = exception;
                    }
                }
                if (rawTts != null)
                {
                    try
                    {
                        await rawTts.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        cleanupFailure ??= exception;
                    }
                }
                try
                {
                    await audio.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupFailure ??= exception;
                }

                if (cleanupFailure != null)
                {
                    throw new AggregateException(
                        "Creating the Android offline speech stack failed and cleanup also reported an error.",
                        creationFailure,
                        cleanupFailure);
                }
                throw;
            }
#else
            await Task.Yield();
            throw new PlatformNotSupportedException(
                "The RMA-125 offline Android speech stack requires an Android player build.");
#endif
        }
    }
}
