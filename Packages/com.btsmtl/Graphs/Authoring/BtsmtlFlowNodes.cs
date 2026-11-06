using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
#if UNITY_5_3_OR_NEWER
using FlowCanvas;
using ParadoxNotion.Design;
using UnityEngine;
#endif

namespace BTSMTL.Graphs
{
    [Serializable]
    public sealed partial class BtsmtlGraphStepPort : IBtsmtlGraphStepPort
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        string m_Id;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        string m_Name;

        public BtsmtlGraphStepPort() : this(Guid.NewGuid().ToString("N"), "步骤") { }
        public BtsmtlGraphStepPort(string id, string name)
        {
            m_Id = id;
            m_Name = name;
        }
        [GraphAuthoringField("id", GraphAuthoringFieldValueKind.IdentityReference, ReadOnly = true)]
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

    public abstract class BtsmtlFlowNode
#if UNITY_5_3_OR_NEWER
        : FlowScriptNode, IBtsmtlGraphOperationSource
#else
        : IBtsmtlGraphOperationSource
#endif
    {
        public virtual int OperationVariant => 0;
#if UNITY_5_3_OR_NEWER
        public override bool ignoreSelfInstancePortAssignment => true;

        protected static void RejectAuthoringExecution(Flow flow) =>
            throw new InvalidOperationException("BTSMTL 作者图只能运行正式编译内容。");

        protected static T RejectAuthoringEvaluation<T>() =>
            throw new InvalidOperationException("BTSMTL 作者值只能由正式编排求值器读取。");
#endif

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
#if UNITY_5_3_OR_NEWER
        protected override void RegisterPorts()
        {
            foreach (GraphNodePort port in GraphNodePortShapes.Of(this))
                if (port.IsInput)
                    AddFlowInput(port.DisplayName, RejectAuthoringExecution, port.Id);
                else
                    AddFlowOutput(port.DisplayName, port.Id);
        }
#endif
    }

#if UNITY_5_3_OR_NEWER
    [Name("图入口"), Category("BTSMTL/流程")]
#endif
    [BtsmtlGraphOperation(OrchestrationOperationCode.Root)]
    [FlowOutput("Output", "执行")]
    public sealed class BtsmtlEntryFlowNode : BtsmtlFlowNode
    {
#if UNITY_5_3_OR_NEWER
        protected override void RegisterPorts() => AddFlowOutput("执行", "Output");
#endif
    }

#if UNITY_5_3_OR_NEWER
    [Name("顺序执行"), Category("BTSMTL/流程")]
#endif
    [BtsmtlGraphOperation(OrchestrationOperationCode.Sequence)]
    public sealed class BtsmtlSequenceFlowNode : BtsmtlCompositeFlowNode<BtsmtlGraphStepPort>
    {
        public BtsmtlSequenceFlowNode() : base(new[]
        {
            new BtsmtlGraphStepPort("0", "步骤 1"), new BtsmtlGraphStepPort("1", "步骤 2")
        }) { }
    }

#if UNITY_5_3_OR_NEWER
    [Name("选择执行"), Category("BTSMTL/流程")]
#endif
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

#if UNITY_5_3_OR_NEWER
    [Name("并行执行"), Category("BTSMTL/流程")]
#endif
    [BtsmtlGraphOperation(OrchestrationOperationCode.Parallel)]
    public sealed partial class BtsmtlParallelFlowNode : BtsmtlCompositeFlowNode<BtsmtlGraphStepPort>
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        BtsmtlParallelMode m_Mode;
        public BtsmtlParallelFlowNode() : base(new[]
        {
            new BtsmtlGraphStepPort("0", "步骤 1"), new BtsmtlGraphStepPort("1", "步骤 2")
        }) { }
        [GraphAuthoringField("mode", typeof(BtsmtlParallelMode), DisplayName = "完成方式",
            CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
        public BtsmtlParallelMode Mode => m_Mode;
        public override int OperationVariant => (int)m_Mode;
        [GraphAuthoringConfigure]
        public void ConfigureMode(BtsmtlParallelMode mode) => m_Mode = mode;
    }

#if UNITY_5_3_OR_NEWER
    [Name("条件分支"), Category("BTSMTL/流程")]
#endif
    [BtsmtlGraphOperation(OrchestrationOperationCode.Branch)]
    [FlowInput("In", "执行")]
    [ValueInput("Condition", "条件", typeof(bool))]
    [FlowOutput("True", "满足")]
    [FlowOutput("False", "不满足")]
    public sealed class BtsmtlBranchFlowNode : BtsmtlFlowNode
    {
#if UNITY_5_3_OR_NEWER
        protected override void RegisterPorts()
        {
            AddFlowInput("执行", RejectAuthoringExecution, "In");
            AddValueInput<bool>("条件", "Condition");
            AddFlowOutput("满足", "True");
            AddFlowOutput("不满足", "False");
        }
#endif
    }

    [ValueOutput("Value", "值", TypeFrom = nameof(ValueType))]
    public abstract partial class BtsmtlVariableFlowNode<T> : BtsmtlFlowNode, IBtsmtlVariableFlowNode
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        string m_OwnerId;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        string m_VariableId;
        [GraphAuthoringField("ownerId", GraphAuthoringFieldValueKind.IdentityReference, AuthoringVisible = false,
            CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
        public string OwnerId => m_OwnerId;
        [GraphAuthoringField("variableId", GraphAuthoringFieldValueKind.IdentityReference,
            DisplayName = "变量", PickerKind = "graph-variable", PickerOwnerField = nameof(OwnerId),
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

#if UNITY_5_3_OR_NEWER
    [Name("读取图变量"), Category("BTSMTL/变量")]
#endif
    public sealed class BtsmtlVariableGetFlowNode<T> : BtsmtlVariableFlowNode<T>
    {
        public override bool Writes => false;
#if UNITY_5_3_OR_NEWER
        protected override void RegisterPorts() => AddValueOutput<T>("值", RejectAuthoringEvaluation<T>, "Value");
#endif
    }

#if UNITY_5_3_OR_NEWER
    [Name("写入图变量"), Category("BTSMTL/变量")]
#endif
    [FlowInput("In", "执行")]
    [ValueInput("Value", "值", TypeFrom = nameof(ValueType))]
    [FlowOutput("Out", "继续")]
    public sealed class BtsmtlVariableSetFlowNode<T> : BtsmtlVariableFlowNode<T>
    {
        public override bool Writes => true;
#if UNITY_5_3_OR_NEWER
        protected override void RegisterPorts()
        {
            AddFlowInput("执行", RejectAuthoringExecution, "In");
            AddValueInput<T>("值", "Value");
            AddFlowOutput("继续", "Out");
            AddValueOutput<T>("值", RejectAuthoringEvaluation<T>, "Value");
        }
#endif
    }

#if UNITY_5_3_OR_NEWER
    [Name("读取宿主输入"), Category("BTSMTL/输入")]
#endif
    [ValueOutput("Value", "值", TypeFrom = nameof(ValueType))]
    public sealed partial class BtsmtlHostInputFlowNode<T> : BtsmtlFlowNode, IBtsmtlHostInputFlowNode
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        string m_InputId = string.Empty;
        [GraphAuthoringField("inputId", GraphAuthoringFieldValueKind.String, DisplayName = "输入身份", NonEmpty = true,
            CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
        public string InputId => m_InputId;
        public Type ValueType => typeof(T);
        [GraphAuthoringConfigure]
        public void ConfigureInput(string inputId) => m_InputId = inputId;
#if UNITY_5_3_OR_NEWER
        protected override void RegisterPorts() => AddValueOutput<T>("值", RejectAuthoringEvaluation<T>, "Value");
#endif
    }
}
