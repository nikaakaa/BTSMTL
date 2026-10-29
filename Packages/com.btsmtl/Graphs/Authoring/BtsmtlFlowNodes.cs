using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using FlowCanvas;
using ParadoxNotion.Design;
using UnityEngine;

namespace BTSMTL.Graphs
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BtsmtlGraphOperationAttribute : Attribute
    {
        public BtsmtlGraphOperationAttribute(OrchestrationOperationCode code) => Code = code;
        public OrchestrationOperationCode Code { get; }
    }

    [Serializable]
    public sealed partial class BtsmtlGraphStepPort : IBtsmtlGraphStepPort
    {
        [SerializeField] string m_Id;
        [SerializeField] string m_Name;

        public BtsmtlGraphStepPort() : this(Guid.NewGuid().ToString("N"), "步骤") { }
        public BtsmtlGraphStepPort(string id, string name)
        {
            m_Id = id;
            m_Name = name;
        }
        public string Id => m_Id;
        [GraphAuthoringField("name", GraphAuthoringFieldValueKind.String, DisplayName = "名称")]
        public string Name => m_Name;
        [GraphAuthoringConfigure]
        public void ConfigureName(string name) => m_Name = name;
    }

    public interface IBtsmtlCompositeFlowNode
    {
        int StepCount { get; }
        IBtsmtlGraphStepPort GetStep(int index);
    }

#if UNITY_EDITOR
    public interface IBtsmtlGraphNodeInspector
    {
        void DrawNodeInspector(BtsmtlFlowNode node);
    }
#endif

    public abstract class BtsmtlFlowNode : FlowScriptNode
    {
        public override bool ignoreSelfInstancePortAssignment => true;

        protected static void RejectAuthoringExecution(Flow flow) =>
            throw new InvalidOperationException("BTSMTL 作者图只能运行正式编译内容。");

        protected static T RejectAuthoringValue<T>() =>
            throw new InvalidOperationException("BTSMTL 作者值只能由正式编排求值器读取。");

#if UNITY_EDITOR
        protected override void OnNodeInspectorGUI()
        {
            if (graph is IBtsmtlGraphNodeInspector inspector)
            {
                inspector.DrawNodeInspector(this);
                return;
            }
            if (graph is BtsmtlMacroGraph { Host: { } host } && host.DrawNodeInspector(this))
                return;
            BtsmtlGraphAuthoringGUI.DrawFields(this);
            DrawValueInputsGUI();
        }
#endif
    }

    public abstract partial class BtsmtlCompositeFlowNode<TStep> : BtsmtlFlowNode, IBtsmtlCompositeFlowNode
        where TStep : IBtsmtlGraphStepPort
    {
        protected virtual bool HasExecutionInput => true;
        protected virtual string ExecutionInputId => "Input";

        protected override void RegisterPorts()
        {
            if (HasExecutionInput)
                AddFlowInput("执行", RejectAuthoringExecution, ExecutionInputId);
            for (int i = 0; i < m_Steps.Count; i++)
                AddFlowOutput($"{i + 1}. {m_Steps[i].Name}", m_Steps[i].Id);
        }
    }

    [Name("图入口"), Category("BTSMTL/流程")]
    [BtsmtlGraphOperation(OrchestrationOperationCode.Root)]
    public sealed class BtsmtlEntryFlowNode : BtsmtlFlowNode
    {
        protected override void RegisterPorts() => AddFlowOutput("执行", "Output");
    }

    [Name("顺序执行"), Category("BTSMTL/流程")]
    [BtsmtlGraphOperation(OrchestrationOperationCode.Sequence)]
    public sealed class BtsmtlSequenceFlowNode : BtsmtlCompositeFlowNode<BtsmtlGraphStepPort>
    {
        public BtsmtlSequenceFlowNode() : base(new[]
        {
            new BtsmtlGraphStepPort("0", "步骤 1"), new BtsmtlGraphStepPort("1", "步骤 2")
        }) { }
    }

    [Name("选择执行"), Category("BTSMTL/流程")]
    [BtsmtlGraphOperation(OrchestrationOperationCode.Selector)]
    public sealed class BtsmtlSelectorFlowNode : BtsmtlCompositeFlowNode<BtsmtlGraphStepPort>
    {
        public BtsmtlSelectorFlowNode() : base(new[]
        {
            new BtsmtlGraphStepPort("0", "步骤 1"), new BtsmtlGraphStepPort("1", "步骤 2")
        }) { }
    }

    public enum BtsmtlParallelMode
    {
        JumpComplete,
        UpdateAll,
        FirstChild
    }

    [Name("并行执行"), Category("BTSMTL/流程")]
    [BtsmtlGraphOperation(OrchestrationOperationCode.Parallel)]
    public sealed partial class BtsmtlParallelFlowNode : BtsmtlCompositeFlowNode<BtsmtlGraphStepPort>
    {
        [SerializeField] BtsmtlParallelMode m_Mode;
        public BtsmtlParallelFlowNode() : base(new[]
        {
            new BtsmtlGraphStepPort("0", "步骤 1"), new BtsmtlGraphStepPort("1", "步骤 2")
        }) { }
        [GraphAuthoringField("mode", typeof(BtsmtlParallelMode), DisplayName = "完成方式",
            CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
        public BtsmtlParallelMode Mode => m_Mode;
        [GraphAuthoringConfigure]
        public void ConfigureMode(BtsmtlParallelMode mode) => m_Mode = mode;
    }

    [Name("条件分支"), Category("BTSMTL/流程")]
    [BtsmtlGraphOperation(OrchestrationOperationCode.Branch)]
    public sealed class BtsmtlBranchFlowNode : BtsmtlFlowNode
    {
        protected override void RegisterPorts()
        {
            AddFlowInput("执行", RejectAuthoringExecution, "In");
            AddValueInput<bool>("条件", "Condition");
            AddFlowOutput("满足", "True");
            AddFlowOutput("不满足", "False");
        }
    }

    public interface IBtsmtlVariableFlowNode
    {
        string OwnerId { get; }
        string VariableId { get; }
        Type ValueType { get; }
        bool Writes { get; }
        void ConfigureVariable(string ownerId, string variableId);
    }

    public abstract partial class BtsmtlVariableFlowNode<T> : BtsmtlFlowNode, IBtsmtlVariableFlowNode
    {
        [SerializeField] string m_OwnerId;
        [SerializeField] string m_VariableId;
        [GraphAuthoringField("ownerId", GraphAuthoringFieldValueKind.IdentityReference, AuthoringVisible = false,
            CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
        public string OwnerId => m_OwnerId;
        [GraphAuthoringField("variableId", GraphAuthoringFieldValueKind.IdentityReference,
            DisplayName = "变量", PickerKind = "graph-variable", PickerOwnerField = "ownerId",
            CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
        public string VariableId => m_VariableId;
        public Type ValueType => typeof(T);
        public abstract bool Writes { get; }
        [GraphAuthoringConfigure]
        public void ConfigureVariable(string ownerId, string variableId)
        {
            m_OwnerId = ownerId;
            m_VariableId = variableId;
        }
    }

    [Name("读取图变量"), Category("BTSMTL/变量")]
    public sealed class BtsmtlVariableGetFlowNode<T> : BtsmtlVariableFlowNode<T>
    {
        public override bool Writes => false;
        protected override void RegisterPorts() => AddValueOutput<T>("值", RejectAuthoringValue<T>, "Value");
    }

    [Name("写入图变量"), Category("BTSMTL/变量")]
    public sealed class BtsmtlVariableSetFlowNode<T> : BtsmtlVariableFlowNode<T>
    {
        public override bool Writes => true;
        protected override void RegisterPorts()
        {
            AddFlowInput("执行", RejectAuthoringExecution, "In");
            AddValueInput<T>("值", "Value");
            AddFlowOutput("继续", "Out");
            AddValueOutput<T>("值", RejectAuthoringValue<T>, "Value");
        }
    }

    public interface IBtsmtlHostInputFlowNode
    {
        string InputId { get; }
        Type ValueType { get; }
        void ConfigureInput(string inputId);
    }

    [Name("读取宿主输入"), Category("BTSMTL/输入")]
    public sealed partial class BtsmtlHostInputFlowNode<T> : BtsmtlFlowNode, IBtsmtlHostInputFlowNode
    {
        [SerializeField] string m_InputId = string.Empty;
        [GraphAuthoringField("inputId", GraphAuthoringFieldValueKind.String, DisplayName = "输入身份", NonEmpty = true,
            CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
        public string InputId => m_InputId;
        public Type ValueType => typeof(T);
        [GraphAuthoringConfigure]
        public void ConfigureInput(string inputId) => m_InputId = inputId;
        protected override void RegisterPorts() => AddValueOutput<T>("值", RejectAuthoringValue<T>, "Value");
    }
}
