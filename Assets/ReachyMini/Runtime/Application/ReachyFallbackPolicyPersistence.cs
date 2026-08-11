#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ReachyMini.Providers;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed class ReachyFallbackPolicyPersistenceStore
    {
        public const string PolicyFileName = "reachy-fallback-policies-v1.json";

        private const int SchemaVersion = 1;
        private readonly string persistencePath;
        private readonly ReachyProviderFallbackPolicyEngine engine;

        public ReachyFallbackPolicyPersistenceStore(
            ReachyProviderFallbackPolicyEngine engine)
            : this(
                engine,
                Path.Combine(Application.persistentDataPath, PolicyFileName))
        {
        }

        public ReachyFallbackPolicyPersistenceStore(
            ReachyProviderFallbackPolicyEngine engine,
            string path)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Fallback policy persistence requires a file path.",
                    nameof(path));
            }
            persistencePath = Path.GetFullPath(path);
        }

        public string PersistencePath => persistencePath;

        public bool IsDegraded { get; private set; }

        public string StatusMessage { get; private set; } =
            "Fallback policy persistence has not been initialized.";

        public ReachyFallbackPolicy GetPolicy(ReachyProviderWorkloadKind workload)
        {
            return engine.GetPolicy(workload);
        }

        public void Initialize()
        {
            if (!File.Exists(persistencePath))
            {
                ResetToNoFallback();
                Persist();
                IsDegraded = false;
                StatusMessage =
                    $"Fallback policy storage initialized at {persistencePath}.";
                return;
            }

            try
            {
                string json = File.ReadAllText(persistencePath);
                FallbackPoliciesEnvelope envelope =
                    JsonUtility.FromJson<FallbackPoliciesEnvelope>(json) ??
                    throw new InvalidDataException(
                        "The fallback policy file did not contain a JSON object.");
                if (envelope.schemaVersion != SchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Unsupported fallback policy schema {envelope.schemaVersion}; expected {SchemaVersion}.");
                }
                ReachyFallbackPolicy[] loaded = ParseCompletePolicySet(
                    envelope.policies ?? Array.Empty<FallbackPolicyEnvelope>());
                ApplyPolicySet(loaded);
                IsDegraded = false;
                StatusMessage =
                    $"Fallback policies restored from {persistencePath}.";
            }
            catch (Exception exception)
            {
                string quarantinePath = QuarantineInvalidFile();
                ResetToNoFallback();
                Persist();
                IsDegraded = true;
                StatusMessage =
                    "Invalid fallback policy settings were quarantined and the fail-closed no-fallback defaults were restored. " +
                    $"source={persistencePath}; quarantine={quarantinePath}; error={exception.GetType().Name}";
            }
        }

        public void SetPolicy(
            ReachyProviderWorkloadKind workload,
            ReachyFallbackPolicy policy)
        {
            ReachyFallbackPolicy previous = engine.GetPolicy(workload);
            engine.SetPolicy(
                workload,
                policy ?? throw new ArgumentNullException(nameof(policy)));
            try
            {
                Persist();
            }
            catch
            {
                engine.SetPolicy(workload, previous);
                throw;
            }
            IsDegraded = false;
            StatusMessage =
                $"{workload} fallback policy set to '{policy.Name}'.";
        }

        public string ExportRedactedJson()
        {
            return JsonUtility.ToJson(CaptureEnvelope(), prettyPrint: true);
        }

        private void ResetToNoFallback()
        {
            foreach (ReachyProviderWorkloadKind workload in Workloads())
            {
                engine.SetPolicy(workload, ReachyFallbackPolicy.NoFallback());
            }
        }

        private void ApplyPolicySet(ReachyFallbackPolicy[] policies)
        {
            if (policies == null || policies.Length != 4)
            {
                throw new InvalidDataException(
                    "Fallback policy settings require exactly one policy per workload.");
            }
            for (int index = 0; index < policies.Length; ++index)
            {
                engine.SetPolicy((ReachyProviderWorkloadKind)index, policies[index]);
            }
        }

        private static ReachyFallbackPolicy[] ParseCompletePolicySet(
            FallbackPolicyEnvelope[] stored)
        {
            if (stored.Length != 4)
            {
                throw new InvalidDataException(
                    "Fallback policy settings require exactly four workload policies.");
            }

            var result = new ReachyFallbackPolicy[4];
            var seen = new bool[4];
            for (int index = 0; index < stored.Length; ++index)
            {
                FallbackPolicyEnvelope envelope = stored[index] ??
                    throw new InvalidDataException(
                        "Fallback policy settings cannot contain null entries.");
                if (!Enum.IsDefined(typeof(ReachyProviderWorkloadKind), envelope.workload))
                {
                    throw new InvalidDataException(
                        "Fallback policy settings contain an unknown workload kind.");
                }
                int workloadIndex = envelope.workload;
                if (seen[workloadIndex])
                {
                    throw new InvalidDataException(
                        "Fallback policy settings contain duplicate workload entries.");
                }
                seen[workloadIndex] = true;
                result[workloadIndex] = envelope.ToPolicy();
            }
            for (int index = 0; index < seen.Length; ++index)
            {
                if (!seen[index] || result[index] == null)
                {
                    throw new InvalidDataException(
                        "Fallback policy settings omitted a workload policy.");
                }
            }
            return result;
        }

        private void Persist()
        {
            string? directory = Path.GetDirectoryName(persistencePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string json = JsonUtility.ToJson(CaptureEnvelope(), prettyPrint: true);
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

        private FallbackPoliciesEnvelope CaptureEnvelope()
        {
            var policies = new FallbackPolicyEnvelope[4];
            foreach (ReachyProviderWorkloadKind workload in Workloads())
            {
                policies[(int)workload] = FallbackPolicyEnvelope.FromPolicy(
                    workload,
                    engine.GetPolicy(workload));
            }
            return new FallbackPoliciesEnvelope
            {
                schemaVersion = SchemaVersion,
                policies = policies,
            };
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

        private static IEnumerable<ReachyProviderWorkloadKind> Workloads()
        {
            yield return ReachyProviderWorkloadKind.Asr;
            yield return ReachyProviderWorkloadKind.Tts;
            yield return ReachyProviderWorkloadKind.Llm;
            yield return ReachyProviderWorkloadKind.Vlm;
        }

        [Serializable]
        private sealed class FallbackPoliciesEnvelope
        {
            public int schemaVersion = SchemaVersion;
            public FallbackPolicyEnvelope[] policies =
                Array.Empty<FallbackPolicyEnvelope>();
        }

        [Serializable]
        private sealed class FallbackPolicyEnvelope
        {
            public int workload;
            public string name = ReachyFallbackPolicy.DefaultPolicyName;
            public bool allowLocalQualityReduction;
            public bool allowSameProviderRetry;
            public bool allowCrossProviderSwitch;
            public bool allowNetworkProviderSwitch;
            public string[] authorizedTargetProviderIds = Array.Empty<string>();

            public ReachyFallbackPolicy ToPolicy()
            {
                return new ReachyFallbackPolicy(
                    name,
                    allowLocalQualityReduction,
                    allowSameProviderRetry,
                    allowCrossProviderSwitch,
                    allowNetworkProviderSwitch,
                    authorizedTargetProviderIds ?? Array.Empty<string>());
            }

            public static FallbackPolicyEnvelope FromPolicy(
                ReachyProviderWorkloadKind workload,
                ReachyFallbackPolicy policy)
            {
                if (policy == null)
                {
                    throw new ArgumentNullException(nameof(policy));
                }
                var targets = new string[policy.AuthorizedTargetProviderIds.Count];
                for (int index = 0; index < targets.Length; ++index)
                {
                    targets[index] = policy.AuthorizedTargetProviderIds[index];
                }
                return new FallbackPolicyEnvelope
                {
                    workload = (int)workload,
                    name = policy.Name,
                    allowLocalQualityReduction = policy.AllowLocalQualityReduction,
                    allowSameProviderRetry = policy.AllowSameProviderRetry,
                    allowCrossProviderSwitch = policy.AllowCrossProviderSwitch,
                    allowNetworkProviderSwitch = policy.AllowNetworkProviderSwitch,
                    authorizedTargetProviderIds = targets,
                };
            }
        }
    }
}
