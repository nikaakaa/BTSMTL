using System;
using BTSMTL.Timeline;
using UnityEngine;
using UnityEngine.UI;

namespace BTSMTL.Timeline.Unity
{
    [DisallowMultipleComponent]
    public sealed class ScenePresentationPanelTarget : MonoBehaviour, ITimelineScenePresentationSink
    {
        public const string PanelDomain = "ui.panel";

        [SerializeField]
        string m_TargetIdentity = string.Empty;

        [SerializeField]
        Graphic m_Graphic;

        float m_OpenAmount;
        bool m_Visible = true;

        public string TargetIdentity => m_TargetIdentity;
        public float OpenAmount => m_OpenAmount;
        public bool Visible => m_Visible;

        public void Prepare()
        {
            if (string.IsNullOrWhiteSpace(m_TargetIdentity))
                throw new InvalidOperationException($"场景面板 '{name}' 未配置 TargetIdentity。");
            if (!m_Graphic)
                throw new InvalidOperationException($"场景面板 '{name}' 未配置 Graphic。");
        }

        public void WriteScalar(TimelineScenePresentationSample sample)
        {
            m_OpenAmount = Mathf.Clamp01(sample.Value);
            ApplyGraphic();
        }

        public void WriteBoolean(TimelineScenePresentationSample sample)
        {
            m_Visible = sample.Value >= 0.5f;
            ApplyGraphic();
        }

        void ApplyGraphic()
        {
            Color color = m_Graphic.color;
            color.a = m_Visible ? m_OpenAmount : 0f;
            m_Graphic.color = color;
        }
    }
}
