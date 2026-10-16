using System;
using BTSMTL.Timeline;
using UnityEngine;
using UnityEngine.UI;

namespace BTSMTL.Timeline.Unity
{
    [DisallowMultipleComponent]
    public sealed class ScenePresentationPanelTarget : MonoBehaviour, ITimelineScenePresentationSink
    {
        [SerializeField]
        string m_TargetIdentity = string.Empty;

        [SerializeField]
        Graphic m_Graphic;

        float m_OpenAmount;
        bool m_Visible = true;

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

        void Validate(TimelineScenePresentationSample sample, string parameterId)
        {
            if (string.IsNullOrWhiteSpace(TargetIdentity) || !m_Graphic)
                throw new InvalidOperationException($"Scene presentation target '{name}' is not configured.");
            if (!string.Equals(sample.TargetIdentity, TargetIdentity, StringComparison.Ordinal) ||
                !string.Equals(sample.ParameterId, parameterId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Scene presentation sample does not belong to target '{TargetIdentity}'.");
        }

        void ApplyGraphic()
        {
            Color color = m_Graphic.color;
            color.a = m_Visible ? m_OpenAmount : 0f;
            m_Graphic.color = color;
        }
    }
}
