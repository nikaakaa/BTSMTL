using System;
using System.Collections.Generic;
using BTSMTL.Timeline;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation;
using UnityEngine;
using UnityEngine.UI;

namespace BTSMTL.Timeline.Unity
{
    [DisallowMultipleComponent]
    public sealed class ScenePresentationPanelTarget : MonoBehaviour, ITimelineScenePresentationSink, ITimelineRuntimeEvaluationSink
    {
        [SerializeField]
        string m_TargetIdentity = string.Empty;

        [SerializeField]
        Graphic m_Graphic;

        float m_OpenAmount;
        bool m_Visible = true;
        readonly Dictionary<ulong, PendingRuntimeValues> m_PendingRuntimeValues =
            new Dictionary<ulong, PendingRuntimeValues>();

        public string TargetIdentity => m_TargetIdentity ?? string.Empty;
        public float OpenAmount => m_OpenAmount;
        public bool Visible => m_Visible;

        public void WriteScalar(TimelineScenePresentationSample sample)
        {
            Validate(sample, "openAmount");
            m_OpenAmount = Mathf.Clamp01(sample.Value);
            ApplyGraphic();
        }

        public void WriteBoolean(TimelineScenePresentationSample sample)
        {
            Validate(sample, "visible");
            m_Visible = sample.Value >= 0.5f;
            ApplyGraphic();
        }

        public bool Consume(TimelineRuntimeStepContext context)
        {
            PendingRuntimeValues pending = new PendingRuntimeValues();
            for (int index = 0; index < context.Advance.Evaluation.ScenePresentation.Count; index++)
            {
                TimelineRuntimeScenePresentationSample sample =
                    context.Advance.Evaluation.ScenePresentation[index];
                if (!context.Playback.TryResolveTargetIdentity(sample.TargetBindingId, out string targetIdentity))
                    return false;
                if (!string.Equals(targetIdentity, TargetIdentity, StringComparison.Ordinal))
                    continue;
                if (sample.ValueKind == TimelineBindingValueKind.Scalar &&
                    string.Equals(sample.ParameterBindingId, "openAmount", StringComparison.Ordinal))
                {
                    if (pending.HasBoolean || pending.HasScalar)
                        return false;
                    pending.HasScalar = true;
                    pending.Scalar = sample.Value;
                }
                else if (sample.ValueKind == TimelineBindingValueKind.Boolean &&
                         string.Equals(sample.ParameterBindingId, "visible", StringComparison.Ordinal))
                {
                    if (pending.HasScalar || pending.HasBoolean)
                        return false;
                    pending.HasBoolean = true;
                    pending.Boolean = sample.Value >= 0.5f;
                }
                else
                {
                    return false;
                }
            }
            m_PendingRuntimeValues[context.Playback.Handle.Value] = pending;
            return true;
        }

        public void Commit(TimelineRuntimeStepContext context)
        {
            if (!m_PendingRuntimeValues.TryGetValue(context.Playback.Handle.Value, out PendingRuntimeValues pending))
                return;
            if (pending.HasScalar)
                m_OpenAmount = Mathf.Clamp01(pending.Scalar);
            if (pending.HasBoolean)
                m_Visible = pending.Boolean;
            m_PendingRuntimeValues.Remove(context.Playback.Handle.Value);
            ApplyGraphic();
        }

        public void Discard(TimelineRuntimeStepContext context)
        {
            m_PendingRuntimeValues.Remove(context.Playback.Handle.Value);
        }

        public bool ConsumeStop(TimelineRuntimeStopRequest request) => true;

        public void CommitStop(TimelineRuntimeStopRequest request)
        {
            m_PendingRuntimeValues.Remove(request.Handle.Value);
        }

        public void DiscardStop(TimelineRuntimeStopRequest request)
        {
            m_PendingRuntimeValues.Remove(request.Handle.Value);
        }

        void Validate(TimelineScenePresentationSample sample, string parameterId)
        {
            if (string.IsNullOrWhiteSpace(TargetIdentity) || !m_Graphic)
                throw new InvalidOperationException($"Scene presentation target '{name}' is not configured.");
            if (!string.Equals(sample.TargetIdentity, TargetIdentity, StringComparison.Ordinal) ||
                !string.Equals(sample.ParameterId, parameterId, StringComparison.Ordinal) ||
                !sample.ExecutionIdentity.IsValid)
                throw new InvalidOperationException($"Scene presentation sample does not belong to target '{TargetIdentity}'.");
        }

        void ApplyGraphic()
        {
            Color color = m_Graphic.color;
            color.a = m_Visible ? m_OpenAmount : 0f;
            m_Graphic.color = color;
        }

        sealed class PendingRuntimeValues
        {
            public bool HasScalar;
            public bool HasBoolean;
            public float Scalar;
            public bool Boolean;
        }
    }
}
