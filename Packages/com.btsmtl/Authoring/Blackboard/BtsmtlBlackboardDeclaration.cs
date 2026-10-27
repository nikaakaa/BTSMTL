using System;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Authoring.Graph;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs
{
    [Serializable]
    public sealed partial class BtsmtlBlackboardDeclaration
    {
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        string m_VariableId;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        PipelineBlackboardVariableScope m_Scope;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        PipelineBlackboardVariableLifetime m_Lifetime;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        string m_Category;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        PipelineBlackboardInputBinding m_InputBinding;

        public BtsmtlBlackboardDeclaration(string variableId, PipelineBlackboardVariableScope scope,
            PipelineBlackboardVariableLifetime lifetime, string category = "", PipelineBlackboardInputBinding inputBinding = null)
        {
            if (string.IsNullOrWhiteSpace(variableId))
                throw new ArgumentException("黑板声明必须引用稳定变量ID。", nameof(variableId));
            m_VariableId = variableId; m_Scope = scope; m_Lifetime = lifetime;
            m_Category = category ?? string.Empty; m_InputBinding = inputBinding;
        }

        [GraphAuthoringField(nameof(m_VariableId), GraphAuthoringFieldValueKind.IdentityReference, ReadOnly = true)]
        public string VariableId => m_VariableId;
        [GraphAuthoringField(nameof(m_Scope), typeof(PipelineBlackboardVariableScope), ReadOnly = true)]
        public PipelineBlackboardVariableScope Scope => m_Scope;
        [GraphAuthoringField(nameof(m_Lifetime), typeof(PipelineBlackboardVariableLifetime), ReadOnly = true)]
        public PipelineBlackboardVariableLifetime Lifetime => m_Lifetime;
        [GraphAuthoringField(nameof(m_Category), GraphAuthoringFieldValueKind.String, ReadOnly = true)]
        public string Category => m_Category;
        [GraphAuthoringField(nameof(m_InputBinding), GraphAuthoringFieldValueKind.Object, ReadOnly = true, Optional = true)]
        public PipelineBlackboardInputBinding InputBinding => m_InputBinding != null && m_InputBinding.IsDefined ? m_InputBinding : null;

        public static void ValidateDefinition(SemanticValueKind kind, TypeCode typeCode,
            PipelineBlackboardVariableScope scope, PipelineBlackboardVariableLifetime lifetime,
            PipelineBlackboardInputBinding inputBinding)
        {
            if (!PipelineBlackboardVariablePolicy.IsValid(scope, lifetime))
                throw new InvalidOperationException("黑板作用域与生命周期不匹配。");
            if (inputBinding == null) return;
            bool typeMatches = inputBinding.Projection == PipelineBlackboardInputProjection.Direct ||
                (kind, typeCode) == ProjectionType(inputBinding.Projection);
            if (!inputBinding.IsDefined || scope != PipelineBlackboardVariableScope.Character ||
                lifetime != PipelineBlackboardVariableLifetime.Spawn || !typeMatches)
                throw new InvalidOperationException("黑板输入投影必须使用对应基础类型和 Character／Spawn 作用域。");
        }

        static (SemanticValueKind, TypeCode) ProjectionType(PipelineBlackboardInputProjection projection) => projection switch
        {
            PipelineBlackboardInputProjection.ActionTargetIdentity => (SemanticValueKind.Identity, TypeCode.String),
            PipelineBlackboardInputProjection.ActionTargetPosition => (SemanticValueKind.Vector3, TypeCode.Object),
            PipelineBlackboardInputProjection.ActionTargetYaw => (SemanticValueKind.Number, TypeCode.Single),
            _ => throw new InvalidOperationException("角色目标输入必须声明其基础投影。")
        };
    }
}
