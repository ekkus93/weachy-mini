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
        private int baselineTargetFrameRate;
        private int baselineVSyncCount;
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
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = decision.TargetRenderFps;
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

            Application.targetFrameRate = baselineTargetFrameRate;
            QualitySettings.vSyncCount = baselineVSyncCount;
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

            baselineTargetFrameRate = Application.targetFrameRate;
            baselineVSyncCount = QualitySettings.vSyncCount;
            baselineShadows = QualitySettings.shadows;
            baselineAntiAliasing = QualitySettings.antiAliasing;
            baselineSoftParticles = QualitySettings.softParticles;
            baselineCaptured = true;
        }
    }
}
