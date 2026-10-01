#nullable enable

using System;
using ReachyMini.Performance;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed class ReachyUnityPriorityDegradationTarget :
        IReachyPriorityDegradationTarget
    {
        private bool baselineCaptured;
        private ReachyRuntimeThermalPolicy.FramePacingSnapshot baselineFramePacing;
        private ShadowQuality baselineShadows;
        private int baselineAntiAliasing;
        private bool baselineSoftParticles;

        public void ApplyPriorityDegradation(
            ReachyPriorityDegradationDecision decision)
        {
            if (decision == null)
            {
                throw new ArgumentNullException(nameof(decision));
            }

            CaptureBaselineIfNeeded();
            ReachyRuntimeThermalPolicy.Apply(decision.TargetRenderFps);
            if (decision.ExpensiveVisualEffectsAllowed)
            {
                QualitySettings.shadows = baselineShadows;
                QualitySettings.antiAliasing = baselineAntiAliasing;
                QualitySettings.softParticles = baselineSoftParticles;
                return;
            }

            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.antiAliasing = 0;
            QualitySettings.softParticles = false;
        }

        public void RestoreBaseline()
        {
            if (!baselineCaptured)
            {
                return;
            }

            ReachyRuntimeThermalPolicy.Restore(baselineFramePacing);
            QualitySettings.shadows = baselineShadows;
            QualitySettings.antiAliasing = baselineAntiAliasing;
            QualitySettings.softParticles = baselineSoftParticles;
        }

        private void CaptureBaselineIfNeeded()
        {
            if (baselineCaptured)
            {
                return;
            }

            baselineFramePacing = ReachyRuntimeThermalPolicy.CaptureCurrentPacing();
            baselineShadows = QualitySettings.shadows;
            baselineAntiAliasing = QualitySettings.antiAliasing;
            baselineSoftParticles = QualitySettings.softParticles;
            baselineCaptured = true;
        }
    }
}
