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

    public interface IBtsmtlGraphStepPort
    {
        string Id { get; }
        string Name { get; }
    }

    [Serializable]
    [GraphAuthoringField("name", GraphAuthoringFieldValueKind.String,
        ReadMember = nameof(Name), WriteMember = nameof(ConfigureName), WriteParameter = "name",
        DefaultMember = nameof(Name), DisplayName = "名称")]
    public sealed class BtsmtlGraphStepPort : IBtsmtlGraphStepPort
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
        public string Name => m_Name;
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
            BtsmtlGraphAuthoringGUI.DrawFields(this);
            DrawValueInputsGUI();
        }
#endif
    }

    [GraphAuthoringField("steps", GraphAuthoringFieldValueKind.Object,
        ReadMember = nameof(Steps), WriteMember = nameof(SetSteps), WriteParameter = "steps",
        CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
    public abstract class BtsmtlCompositeFlowNode<TStep> : BtsmtlFlowNode, IBtsmtlCompositeFlowNode
        where TStep : IBtsmtlGraphStepPort
    {
        [SerializeField, GatherPortsCallback] List<TStep> m_Steps = new();
        protected BtsmtlCompositeFlowNode() { }
        protected BtsmtlCompositeFlowNode(IEnumerable<TStep> defaultSteps) => m_Steps = defaultSteps.ToList();
        public IReadOnlyList<TStep> Steps => m_Steps;
        public int StepCount => m_Steps.Count;
        public IBtsmtlGraphStepPort GetStep(int index) => m_Steps[index];
        protected virtual bool HasExecutionInput => true;
        protected virtual string ExecutionInputId => "Input";

        public void SetSteps(IEnumerable<TStep> steps)
        {
            List<TStep> next = steps.ToList();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TStep step in next)
                if (string.IsNullOrWhiteSpace(step.Id) || !ids.Add(step.Id))
                    throw new ArgumentException("组合步骤必须具有唯一的稳定端口身份。", nameof(steps));
            foreach (BinderConnection connection in outConnections.OfType<BinderConnection>())
                if (!ids.Contains(connection.sourcePortID))
                    throw new InvalidOperationException("删除步骤前必须先断开其端口。");
            m_Steps = next;
            GatherPorts();
        }

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
    [GraphAuthoringField("mode", typeof(BtsmtlParallelMode),
        ReadMember = nameof(Mode), WriteMember = nameof(ConfigureMode), WriteParameter = "mode",
        DefaultMember = nameof(Mode), DisplayName = "完成方式",
        CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
    public sealed class BtsmtlParallelFlowNode : BtsmtlCompositeFlowNode<BtsmtlGraphStepPort>
    {
        [SerializeField] BtsmtlParallelMode m_Mode;
        public BtsmtlParallelFlowNode() : base(new[]
        {
            new BtsmtlGraphStepPort("0", "步骤 1"), new BtsmtlGraphStepPort("1", "步骤 2")
        }) { }
        public BtsmtlParallelMode Mode => m_Mode;
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

    [GraphAuthoringField("ownerId", GraphAuthoringFieldValueKind.IdentityReference,
        ReadMember = nameof(OwnerId), WriteMember = nameof(ConfigureVariable), WriteParameter = "ownerId",
        DefaultMember = nameof(OwnerId), AuthoringVisible = false,
        CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
    [GraphAuthoringField("variableId", GraphAuthoringFieldValueKind.IdentityReference,
        ReadMember = nameof(VariableId), WriteMember = nameof(ConfigureVariable), WriteParameter = "variableId",
        DefaultMember = nameof(VariableId), DisplayName = "变量", PickerKind = "graph-variable",
        PickerOwnerField = "ownerId", CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
    public abstract class BtsmtlVariableFlowNode<T> : BtsmtlFlowNode, IBtsmtlVariableFlowNode
    {
        [SerializeField] string m_OwnerId;
        [SerializeField] string m_VariableId;
        public string OwnerId => m_OwnerId;
        public string VariableId => m_VariableId;
        public Type ValueType => typeof(T);
        public abstract bool Writes { get; }
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
    [GraphAuthoringField("inputId", GraphAuthoringFieldValueKind.String,
        ReadMember = nameof(InputId), WriteMember = nameof(ConfigureInput), WriteParameter = "inputId",
        DefaultMember = nameof(InputId), DisplayName = "输入身份", NonEmpty = true,
        CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
    public sealed class BtsmtlHostInputFlowNode<T> : BtsmtlFlowNode, IBtsmtlHostInputFlowNode
    {
        [SerializeField] string m_InputId = string.Empty;
        public string InputId => m_InputId;
        public Type ValueType => typeof(T);
        public void ConfigureInput(string inputId) => m_InputId = inputId;
        protected override void RegisterPorts() => AddValueOutput<T>("值", RejectAuthoringValue<T>, "Value");
    }
}
