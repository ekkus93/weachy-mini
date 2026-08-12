#nullable enable

using System;

namespace ReachyMini.Perception
{
    public sealed class VlmScheduleSignal
    {
        public VlmScheduleSignal(
            string providerInstanceId,
            VlmScheduleTrigger trigger,
            VlmSemanticOperation operation,
            ulong triggerSequence,
            ulong sceneRevision,
            ulong questionRevision,
            string prompt,
            bool networkDisclosureAcknowledged,
            bool costDisclosureAcknowledged)
        {
            if (!Enum.IsDefined(typeof(VlmScheduleTrigger), trigger))
            {
                throw new ArgumentOutOfRangeException(nameof(trigger));
            }
            if (!Enum.IsDefined(typeof(VlmSemanticOperation), operation))
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }
            ProviderInstanceId = ProviderDescriptor.RequireText(
                providerInstanceId,
                nameof(providerInstanceId));
            if (triggerSequence == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(triggerSequence));
            }
            if (sceneRevision == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(sceneRevision));
            }
            if (trigger == VlmScheduleTrigger.UserVisualQuestion &&
                (operation != VlmSemanticOperation.VisualQuestion || questionRevision == 0UL))
            {
                throw new ArgumentException(
                    "User visual questions require a nonzero question revision and visual-question operation.",
                    nameof(trigger));
            }
            if ((trigger == VlmScheduleTrigger.SignificantSceneChange ||
                    trigger == VlmScheduleTrigger.NewEntity ||
                    trigger == VlmScheduleTrigger.SlowInterval) &&
                operation != VlmSemanticOperation.SceneDescription)
            {
                throw new ArgumentException(
                    "Autonomous scene triggers require a scene-description operation.",
                    nameof(operation));
            }

            Trigger = trigger;
            Operation = operation;
            TriggerSequence = triggerSequence;
            SceneRevision = sceneRevision;
            QuestionRevision = questionRevision;
            Prompt = ProviderDescriptor.RequireText(prompt, nameof(prompt));
            NetworkDisclosureAcknowledged = networkDisclosureAcknowledged;
            CostDisclosureAcknowledged = costDisclosureAcknowledged;
        }

        public string ProviderInstanceId { get; }

        public VlmScheduleTrigger Trigger { get; }

        public VlmSemanticOperation Operation { get; }

        public ulong TriggerSequence { get; }

        public ulong SceneRevision { get; }

        public ulong QuestionRevision { get; }

        public string Prompt { get; }

        public bool NetworkDisclosureAcknowledged { get; }

        public bool CostDisclosureAcknowledged { get; }
    }
}
