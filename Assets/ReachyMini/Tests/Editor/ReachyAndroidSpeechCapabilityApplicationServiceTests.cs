#nullable enable

using NUnit.Framework;
using ReachyMini.AppState;

namespace ReachyMini.Tests
{
    // ReachyProductionApplicationCompositionProvider and
    // ReachySettingsApplicationCompositionProvider previously stubbed the
    // "speech-audio" service permanently Unavailable with a message dating to
    // 2026-07-31, before speech (ASR/TTS) was actually built. This locks in that the
    // replacement, real service compiles, initializes, and disposes cleanly off
    // Android (the Editor test platform), reporting Unavailable rather than the
    // stale "not implemented" message or an exception. The real ASR/TTS probe path
    // itself only runs on an Android player and is covered by physical acceptance.
    public sealed class ReachyAndroidSpeechCapabilityApplicationServiceTests
    {
        [Test]
        public void OffAndroidReportsUnavailableWithoutThrowing()
        {
            var service = new ReachyAndroidSpeechCapabilityApplicationService();
            try
            {
                service.Initialize();
                Assert.That(
                    service.Health.State,
                    Is.EqualTo(ReachyServiceState.Unavailable));
                Assert.That(
                    service.Health.Message,
                    Does.Not.Contain("not implemented until the speech phase"));
                Assert.That(
                    service.Health.Message,
                    Does.Not.Contain("not installed"));
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void ServiceIdentifiesAsOptionalAudioService()
        {
            var service = new ReachyAndroidSpeechCapabilityApplicationService();
            try
            {
                Assert.That(service.ServiceId, Is.EqualTo("speech-audio"));
                Assert.That(service.Kind, Is.EqualTo(ReachyServiceKind.Audio));
                Assert.That(
                    service.Criticality,
                    Is.EqualTo(ReachyServiceCriticality.Optional));
            }
            finally
            {
                service.Dispose();
            }
        }
    }
}
