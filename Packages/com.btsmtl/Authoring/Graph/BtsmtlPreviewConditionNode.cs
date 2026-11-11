using System;
#if UNITY_EDITOR
using UnityEngine;
#endif

namespace BTSMTL.Authoring.Graph
{
    [FlowInput("Input", "执行"), FlowOutput("Output", "然后")]
    public sealed partial class BtsmtlPreviewConditionNode
    {
        public const string StoredType = "CharacterTimelinePreviewConditionNode";
        public const string Kind = "preview-condition";
        public const string DisplayName = "预览条件";
        public const string CategoryName = "BTSMTL/预览输入";
#if UNITY_EDITOR
        [SerializeField]
#endif
        string m_Condition = string.Empty;
#if UNITY_EDITOR
        [SerializeField]
#endif
        string m_Label = string.Empty;
#if UNITY_EDITOR
        [SerializeField]
#endif
        bool m_Satisfied = true;
#if UNITY_EDITOR
        [SerializeField]
#endif
        bool m_RestoreDefault;

        [GraphAuthoringField(GraphAuthoringFieldValueKind.String, DisplayName = "条件", NonEmpty = true,
            PickerKind = Kind, CompilationImpact = GraphAuthoringFieldCompilationImpact.Editor)]
        public string Condition => m_Condition;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.String, DisplayName = "显示名", CompilationImpact = GraphAuthoringFieldCompilationImpact.Editor)]
        public string Label => m_Label;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Boolean, DisplayName = "成立", CompilationImpact = GraphAuthoringFieldCompilationImpact.Editor)]
        public bool Satisfied => m_Satisfied;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Boolean, DisplayName = "恢复默认值", CompilationImpact = GraphAuthoringFieldCompilationImpact.Editor)]
        public bool RestoreDefault => m_RestoreDefault;

        [GraphAuthoringConfigure]
        public void Configure(string condition, string label, bool satisfied, bool restoreDefault)
        {
            if (string.IsNullOrWhiteSpace(condition)) throw new ArgumentException("预览条件必须指定条件。", nameof(condition));
            m_Condition = condition.Trim();
            m_Label = label;
            m_Satisfied = satisfied;
            m_RestoreDefault = restoreDefault;
        }
    }
}
