#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ReachyMini.Providers;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed class ReachyProviderProfilePersistenceStore
    {
        public const string ProfileFileName = "reachy-provider-profiles-v1.json";

        private const int SchemaVersion = 1;
        private readonly string persistencePath;
        private readonly Dictionary<string, ReachyProviderProfile> profiles =
            new Dictionary<string, ReachyProviderProfile>(StringComparer.Ordinal);

        public ReachyProviderProfilePersistenceStore()
            : this(Path.Combine(Application.persistentDataPath, ProfileFileName))
        {
        }

        public ReachyProviderProfilePersistenceStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Provider profile persistence requires a file path.",
                    nameof(path));
            }
            persistencePath = Path.GetFullPath(path);
        }

        public string PersistencePath => persistencePath;

        public bool IsDegraded { get; private set; }

        public string StatusMessage { get; private set; } =
            "Provider profile persistence has not been initialized.";

        public IReadOnlyList<ReachyProviderProfile> Profiles
        {
            get
            {
                var result = new List<ReachyProviderProfile>(profiles.Values);
                result.Sort((left, right) => string.CompareOrdinal(
                    left.ProviderId,
                    right.ProviderId));
                return result.AsReadOnly();
            }
        }

        public void Initialize()
        {
            profiles.Clear();
            if (!File.Exists(persistencePath))
            {
                Persist();
                IsDegraded = false;
                StatusMessage =
                    $"Provider profile storage initialized at {persistencePath}.";
                return;
            }

            try
            {
                string json = File.ReadAllText(persistencePath);
                ProviderProfilesEnvelope envelope =
                    JsonUtility.FromJson<ProviderProfilesEnvelope>(json) ??
                    throw new InvalidDataException(
                        "The provider profile file did not contain a JSON object.");
                if (envelope.schemaVersion != SchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Unsupported provider profile schema {envelope.schemaVersion}; expected {SchemaVersion}.");
                }
                ProviderProfileEnvelope[] stored =
                    envelope.profiles ?? Array.Empty<ProviderProfileEnvelope>();
                for (int index = 0; index < stored.Length; ++index)
                {
                    ReachyProviderProfile profile = stored[index].ToProfile();
                    if (!profiles.TryAdd(profile.ProviderId, profile))
                    {
                        throw new InvalidDataException(
                            "Provider profile identifiers must be unique.");
                    }
                }
                IsDegraded = false;
                StatusMessage =
                    $"Provider profiles restored from {persistencePath}.";
            }
            catch (Exception exception)
            {
                string quarantinePath = QuarantineInvalidFile();
                profiles.Clear();
                Persist();
                IsDegraded = true;
                StatusMessage =
                    "Invalid provider profile metadata was quarantined and no provider profile was activated. " +
                    $"source={persistencePath}; quarantine={quarantinePath}; " +
                    $"error={exception.GetType().Name}";
            }
        }

        public bool TryGet(string providerId, out ReachyProviderProfile? profile)
        {
            if (providerId == null)
            {
                throw new ArgumentNullException(nameof(providerId));
            }
            bool found = profiles.TryGetValue(providerId, out ReachyProviderProfile? value);
            profile = value;
            return found;
        }

        public void Upsert(ReachyProviderProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            bool replaced = profiles.TryGetValue(
                profile.ProviderId,
                out ReachyProviderProfile? previous);
            profiles[profile.ProviderId] = profile;
            try
            {
                Persist();
            }
            catch
            {
                if (replaced && previous != null)
                {
                    profiles[profile.ProviderId] = previous;
                }
                else
                {
                    profiles.Remove(profile.ProviderId);
                }
                throw;
            }
            IsDegraded = false;
            StatusMessage = profile.UsesCleartextLocalDevelopmentTransport
                ? profile.SecurityWarning
                : $"Provider profile '{profile.ProviderId}' persisted securely without credential values.";
        }

        public bool Remove(string providerId)
        {
            if (providerId == null)
            {
                throw new ArgumentNullException(nameof(providerId));
            }
            if (!profiles.TryGetValue(providerId, out ReachyProviderProfile? removed))
            {
                return false;
            }
            profiles.Remove(providerId);
            try
            {
                Persist();
            }
            catch
            {
                profiles[providerId] = removed;
                throw;
            }
            StatusMessage = $"Provider profile '{providerId}' removed.";
            return true;
        }

        public string ExportRedactedJson()
        {
            var exported = new RedactedProviderProfileEnvelope[profiles.Count];
            int index = 0;
            foreach (ReachyProviderProfile profile in Profiles)
            {
                exported[index++] = RedactedProviderProfileEnvelope.FromProfile(profile);
            }
            return JsonUtility.ToJson(
                new RedactedProviderProfilesEnvelope
                {
                    schemaVersion = SchemaVersion,
                    profiles = exported,
                },
                prettyPrint: true);
        }

        private void Persist()
        {
            string? directory = Path.GetDirectoryName(persistencePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            ProviderProfileEnvelope[] stored = new ProviderProfileEnvelope[profiles.Count];
            int index = 0;
            foreach (ReachyProviderProfile profile in Profiles)
            {
                stored[index++] = ProviderProfileEnvelope.FromProfile(profile);
            }
            string json = JsonUtility.ToJson(
                new ProviderProfilesEnvelope
                {
                    schemaVersion = SchemaVersion,
                    profiles = stored,
                },
                prettyPrint: true);

            string temporaryPath = persistencePath + ".tmp";
            string backupPath = persistencePath + ".bak";
            File.WriteAllText(temporaryPath, json);
            try
            {
                if (File.Exists(persistencePath))
                {
                    File.Copy(persistencePath, backupPath, overwrite: true);
                    File.Delete(persistencePath);
                }
                File.Move(temporaryPath, persistencePath);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch
            {
                if (!File.Exists(persistencePath) && File.Exists(backupPath))
                {
                    File.Copy(backupPath, persistencePath, overwrite: true);
                }
                throw;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private string QuarantineInvalidFile()
        {
            string stamp = DateTime.UtcNow.ToString(
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture);
            string quarantinePath = persistencePath + $".corrupt-{stamp}";
            File.Move(persistencePath, quarantinePath);
            return quarantinePath;
        }

        [Serializable]
        private sealed class ProviderProfilesEnvelope
        {
            public int schemaVersion = SchemaVersion;
            public ProviderProfileEnvelope[] profiles =
                Array.Empty<ProviderProfileEnvelope>();
        }

        [Serializable]
        private sealed class ProviderProfileEnvelope
        {
            public string providerId = string.Empty;
            public string displayName = string.Empty;
            public string baseUrl = string.Empty;
            public int endpointStyle;
            public ProviderModelEnvelope[] models = Array.Empty<ProviderModelEnvelope>();
            public ProviderHeaderEnvelope[] headers = Array.Empty<ProviderHeaderEnvelope>();
            public int timeoutMilliseconds;
            public bool streamingEnabled;
            public int tlsMode;
            public string credentialReference = string.Empty;

            public ReachyProviderProfile ToProfile()
            {
                var modelBindings = new ReachyProviderModelBinding[models.Length];
                for (int index = 0; index < models.Length; ++index)
                {
                    modelBindings[index] = new ReachyProviderModelBinding(
                        (ReachyProviderModelRole)models[index].role,
                        models[index].modelId);
                }
                var headerBindings = new ReachyProviderHeaderBinding[headers.Length];
                for (int index = 0; index < headers.Length; ++index)
                {
                    headerBindings[index] = new ReachyProviderHeaderBinding(
                        headers[index].name,
                        (ReachyProviderHeaderValueKind)headers[index].valueKind,
                        headers[index].valueOrSecretReference);
                }
                return new ReachyProviderProfile(
                    providerId,
                    displayName,
                    new Uri(baseUrl, UriKind.Absolute),
                    (ReachyProviderEndpointStyle)endpointStyle,
                    modelBindings,
                    headerBindings,
                    timeoutMilliseconds,
                    streamingEnabled,
                    (ReachyProviderTlsMode)tlsMode,
                    string.IsNullOrEmpty(credentialReference)
                        ? null
                        : credentialReference);
            }

            public static ProviderProfileEnvelope FromProfile(
                ReachyProviderProfile profile)
            {
                var models = new ProviderModelEnvelope[profile.ModelBindings.Count];
                for (int index = 0; index < models.Length; ++index)
                {
                    ReachyProviderModelBinding binding = profile.ModelBindings[index];
                    models[index] = new ProviderModelEnvelope
                    {
                        role = (int)binding.Role,
                        modelId = binding.ModelId,
                    };
                }
                var headers = new ProviderHeaderEnvelope[profile.Headers.Count];
                for (int index = 0; index < headers.Length; ++index)
                {
                    ReachyProviderHeaderBinding binding = profile.Headers[index];
                    headers[index] = new ProviderHeaderEnvelope
                    {
                        name = binding.Name,
                        valueKind = (int)binding.ValueKind,
                        valueOrSecretReference = binding.ValueOrSecretReference,
                    };
                }
                return new ProviderProfileEnvelope
                {
                    providerId = profile.ProviderId,
                    displayName = profile.DisplayName,
                    baseUrl = profile.BaseUri.AbsoluteUri,
                    endpointStyle = (int)profile.EndpointStyle,
                    models = models,
                    headers = headers,
                    timeoutMilliseconds = profile.TimeoutMilliseconds,
                    streamingEnabled = profile.StreamingEnabled,
                    tlsMode = (int)profile.TlsMode,
                    credentialReference = profile.CredentialReference ?? string.Empty,
                };
            }
        }

        [Serializable]
        private sealed class ProviderModelEnvelope
        {
            public int role;
            public string modelId = string.Empty;
        }

        [Serializable]
        private sealed class ProviderHeaderEnvelope
        {
            public string name = string.Empty;
            public int valueKind;
            public string valueOrSecretReference = string.Empty;
        }

        [Serializable]
        private sealed class RedactedProviderProfilesEnvelope
        {
            public int schemaVersion = SchemaVersion;
            public RedactedProviderProfileEnvelope[] profiles =
                Array.Empty<RedactedProviderProfileEnvelope>();
        }

        [Serializable]
        private sealed class RedactedProviderProfileEnvelope
        {
            public string providerId = string.Empty;
            public string displayName = string.Empty;
            public string baseUrl = string.Empty;
            public int endpointStyle;
            public string[] modelRoles = Array.Empty<string>();
            public string[] headerNames = Array.Empty<string>();
            public int timeoutMilliseconds;
            public bool streamingEnabled;
            public int tlsMode;
            public bool credentialConfigured;
            public string securityWarning = string.Empty;

            public static RedactedProviderProfileEnvelope FromProfile(
                ReachyProviderProfile profile)
            {
                ReachyProviderProfileDiagnostic diagnostic =
                    profile.CreateRedactedDiagnostic();
                var modelRoles = new string[diagnostic.ModelRoles.Count];
                for (int index = 0; index < modelRoles.Length; ++index)
                {
                    modelRoles[index] = diagnostic.ModelRoles[index];
                }
                var headerNames = new string[diagnostic.HeaderNames.Count];
                for (int index = 0; index < headerNames.Length; ++index)
                {
                    headerNames[index] = diagnostic.HeaderNames[index];
                }
                return new RedactedProviderProfileEnvelope
                {
                    providerId = diagnostic.ProviderId,
                    displayName = diagnostic.DisplayName,
                    baseUrl = diagnostic.BaseUri.AbsoluteUri,
                    endpointStyle = (int)diagnostic.EndpointStyle,
                    modelRoles = modelRoles,
                    headerNames = headerNames,
                    timeoutMilliseconds = diagnostic.TimeoutMilliseconds,
                    streamingEnabled = diagnostic.StreamingEnabled,
                    tlsMode = (int)diagnostic.TlsMode,
                    credentialConfigured = diagnostic.CredentialConfigured,
                    securityWarning = diagnostic.SecurityWarning,
                };
            }
        }
    }
}
