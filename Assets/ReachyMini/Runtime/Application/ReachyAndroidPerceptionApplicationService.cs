#nullable enable

using System;
using ReachyMini.Perception;
using ReachyMini.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    // RMA-195 phase C: replaces the permanently-Unavailable "perception"
    // stub with real camera-to-world-model tracking, gated behind
    // Application.platform == Android the same way Phase B's local-LLM
    // provider service gates its own Android-only construction -- this keeps
    // both the constructor and OnInitialize() fully safe to run in EditMode
    // tests, where Application.platform is never Android.
    //
    // The actual per-frame pipeline lives in ReachyAndroidPerceptionDriver, a
    // MonoBehaviour this service creates on a hidden, DontDestroyOnLoad
    // GameObject (mirroring how RMA-111/135's acceptance bootstraps spawn
    // their own MonoBehaviour), because the pipeline's GPU work
    // (ReachyCameraHomographyWarpPipeline.Execute, GPU texture readback)
    // must run from Unity's Update() loop, not a background Task.Run loop
    // like phase A's behavior service uses -- see the driver's own header
    // comment for the full threading rationale.
    //
    // RMA-195 phase D (VLM half, 2026-08-22): also implements
    // ICloudVlmProviderCapability as an optional as-castable facet (mirroring
    // ILocalLlmProviderCapability/ICloudLlmProviderCapability on the provider-
    // selection service) -- the cloud-VLM loading/analysis logic itself lives in
    // the sibling ReachyAndroidPerceptionApplicationService.CloudVlm.cs partial
    // file to keep this file's original scope readable.
    public sealed partial class ReachyAndroidPerceptionApplicationService :
        ReachyApplicationServiceBase,
        IReachyPerceptionService,
        ICloudVlmProviderCapability,
        IReachyApplicationInterruptionParticipant
    {
        private readonly ReachyProductionAuthoritativeRuntime runtime;
        private readonly ReachyCameraCalibrationPersistenceStore calibrations;

        private GameObject? driverObject;
        private ReachyAndroidPerceptionDriver? driver;

        private ReachyPerceptionServiceSnapshot fallbackSnapshot =
            new ReachyPerceptionServiceSnapshot(
                ReachyPerceptionServiceExecutionState.NoCameraFrame,
                null,
                "Perception has not started.",
                0UL);

        public ReachyAndroidPerceptionApplicationService(
            ReachyProductionAuthoritativeRuntime runtime,
            ReachyCameraCalibrationPersistenceStore calibrations)
            : base(
                "perception",
                ReachyServiceKind.Perception,
                ReachyServiceCriticality.Optional)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.calibrations = calibrations ??
                throw new ArgumentNullException(nameof(calibrations));
        }

        public ReachyPerceptionServiceSnapshot PerceptionSnapshot =>
            driver != null ? driver.Snapshot : fallbackSnapshot;

        public event EventHandler<ReachyPerceptionServiceSnapshotChangedEventArgs>?
            PerceptionSnapshotChanged;

        protected override void OnInitialize()
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                SetUnavailable(
                    "Live perception tracking requires an Android device.");
                return;
            }

            var newDriverObject = new GameObject("ReachyAndroidPerceptionDriver");
            Object.DontDestroyOnLoad(newDriverObject);
            ReachyAndroidPerceptionDriver newDriver =
                newDriverObject.AddComponent<ReachyAndroidPerceptionDriver>();
            newDriver.SnapshotChanged += OnDriverSnapshotChanged;
            newDriver.Configure(runtime, calibrations);
            driverObject = newDriverObject;
            driver = newDriver;
            SetReady("Perception tracking driver started.");
        }

        protected override void OnDispose()
        {
            if (driver != null)
            {
                driver.SnapshotChanged -= OnDriverSnapshotChanged;
                driver = null;
            }
            if (driverObject != null)
            {
                Object.Destroy(driverObject);
                driverObject = null;
            }
            DisposeCloudVlm();
        }

        public void PauseForApplicationInterruption()
        {
            driver?.PauseForApplicationInterruption();
        }

        public void ResumeAfterApplicationInterruption()
        {
            driver?.ResumeAfterApplicationInterruption();
        }

        private void OnDriverSnapshotChanged(
            object? sender,
            ReachyPerceptionServiceSnapshotChangedEventArgs eventArgs)
        {
            PerceptionSnapshotChanged?.Invoke(this, eventArgs);
        }
    }
}
