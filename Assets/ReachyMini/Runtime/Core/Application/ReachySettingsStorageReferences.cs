#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed class ReachySettingsStorageReferences
    {
        private const int MaximumIdentifierLength = 128;

        public ReachySettingsStorageReferences(
            string asrProviderProfileId,
            string ttsProviderProfileId,
            string llmProviderProfileId,
            string vlmProviderProfileId,
            string cameraCalibrationProfileId,
            string modelManifestId,
            string modelManifestSha256,
            string deviceProfileId)
        {
            AsrProviderProfileId = ValidateIdentifier(
                asrProviderProfileId,
                nameof(asrProviderProfileId));
            TtsProviderProfileId = ValidateIdentifier(
                ttsProviderProfileId,
                nameof(ttsProviderProfileId));
            LlmProviderProfileId = ValidateIdentifier(
                llmProviderProfileId,
                nameof(llmProviderProfileId));
            VlmProviderProfileId = ValidateIdentifier(
                vlmProviderProfileId,
                nameof(vlmProviderProfileId));
            CameraCalibrationProfileId = ValidateIdentifier(
                cameraCalibrationProfileId,
                nameof(cameraCalibrationProfileId));
            ModelManifestId = ValidateIdentifier(
                modelManifestId,
                nameof(modelManifestId));
            ModelManifestSha256 = ValidateSha256(
                modelManifestSha256,
                nameof(modelManifestSha256));
            DeviceProfileId = ValidateIdentifier(
                deviceProfileId,
                nameof(deviceProfileId));
        }

        public static ReachySettingsStorageReferences Empty { get; } =
            new ReachySettingsStorageReferences(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);

        public string AsrProviderProfileId { get; }

        public string TtsProviderProfileId { get; }

        public string LlmProviderProfileId { get; }

        public string VlmProviderProfileId { get; }

        public string CameraCalibrationProfileId { get; }

        public string ModelManifestId { get; }

        public string ModelManifestSha256 { get; }

        public string DeviceProfileId { get; }

        private static string ValidateIdentifier(string value, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }
            if (value.Length > MaximumIdentifierLength)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value.Length,
                    $"A durable settings identifier cannot exceed {MaximumIdentifierLength} characters.");
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool allowed =
                    char.IsLetterOrDigit(character) ||
                    character == '-' ||
                    character == '_' ||
                    character == '.' ||
                    character == ':';
                if (!allowed)
                {
                    throw new ArgumentException(
                        "Durable settings references must be stable identifiers, not arbitrary values.",
                        parameterName);
                }
            }
            return value;
        }

        private static string ValidateSha256(string value, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }
            if (value.Length == 0)
            {
                return value;
            }
            if (value.Length != 64)
            {
                throw new ArgumentException(
                    "A model-manifest SHA-256 reference must contain exactly 64 hexadecimal characters.",
                    parameterName);
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool hexadecimal =
                    (character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F');
                if (!hexadecimal)
                {
                    throw new ArgumentException(
                        "A model-manifest SHA-256 reference must be hexadecimal.",
                        parameterName);
                }
            }
            return value.ToLowerInvariant();
        }
    }
}
