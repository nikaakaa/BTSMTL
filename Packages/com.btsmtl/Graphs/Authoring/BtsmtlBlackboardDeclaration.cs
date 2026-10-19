using System;
using BTSMTL.Authoring.Blackboard;
using NodeCanvas.Framework;
using UnityEngine;

namespace BTSMTL.Graphs
{
    // 一个图变量的黑板语义：作用域、生命周期、分类与输入投影。值类型、名称与默认值由对应的原生变量持有。
    [Serializable]
    public sealed class BtsmtlBlackboardDeclaration
    {
        [SerializeField] string m_VariableId;
        [SerializeField] PipelineBlackboardVariableScope m_Scope;
        [SerializeField] PipelineBlackboardVariableLifetime m_Lifetime;
        [SerializeField] string m_Category;
        [SerializeField] PipelineBlackboardInputBinding m_InputBinding;

        public BtsmtlBlackboardDeclaration(string variableId, PipelineBlackboardVariableScope scope,
            PipelineBlackboardVariableLifetime lifetime, string category = "", PipelineBlackboardInputBinding inputBinding = null)
        {
            if (string.IsNullOrWhiteSpace(variableId))
                throw new ArgumentException("黑板声明必须引用稳定变量ID。", nameof(variableId));
            m_VariableId = variableId;
            m_Scope = scope;
            m_Lifetime = lifetime;
            m_Category = category ?? string.Empty;
            m_InputBinding = inputBinding;
        }

        public string VariableId => m_VariableId;
        public PipelineBlackboardVariableScope Scope => m_Scope;
        public PipelineBlackboardVariableLifetime Lifetime => m_Lifetime;
        public string Category => m_Category;
        public PipelineBlackboardInputBinding InputBinding => m_InputBinding != null && m_InputBinding.IsDefined ? m_InputBinding : null;

        public void Validate(Variable variable)
        {
            if (variable == null || !variable.hasStableIdentity || variable.ID != m_VariableId ||
                variable.isDataBound || variable.isPropertyBound || variable is not ISerializedVariableValue)
                throw new InvalidOperationException("黑板变量必须有稳定身份，且不能绑定原生运行getter或对象属性。");
            ValidateDefinition(variable.varType, m_Scope, m_Lifetime, InputBinding);
        }

        public static void ValidateDefinition(
            Type type,
            PipelineBlackboardVariableScope scope,
            PipelineBlackboardVariableLifetime lifetime,
            PipelineBlackboardInputBinding inputBinding)
        {
            BtsmtlGraphValueTypes.Kind(type);
            if (!PipelineBlackboardVariablePolicy.IsValid(scope, lifetime))
                throw new InvalidOperationException("黑板作用域与生命周期不匹配。");
            if (inputBinding != null && (!inputBinding.IsDefined ||
                scope != PipelineBlackboardVariableScope.Character ||
                lifetime != PipelineBlackboardVariableLifetime.Spawn ||
                inputBinding.Projection != PipelineBlackboardInputProjection.Direct &&
                type != ProjectionType(inputBinding.Projection)))
                throw new InvalidOperationException("黑板输入投影必须使用对应基础类型和 Character／Spawn 作用域。");
        }

        static Type ProjectionType(PipelineBlackboardInputProjection projection) =>
            projection switch
            {
                PipelineBlackboardInputProjection.ActionTargetIdentity => typeof(string),
                PipelineBlackboardInputProjection.ActionTargetPosition => typeof(Vector3),
                PipelineBlackboardInputProjection.ActionTargetYaw => typeof(float),
                _ => throw new InvalidOperationException("角色目标输入必须声明其基础投影。")
            };
    }
}
