using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using FlowCanvas;
using FlowCanvas.Macros;
using ParadoxNotion;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs.Editor
{
    // 子图的一次出现：参数槽、参数读写、入口骨架与调用发射。技能图与通用图共用；
    // 出现路径、内容哈希与默认值登记由调用方编译器给出，子图体的叶子和连接仍由调用方发射。
    public sealed class BtsmtlMacroCompilation
    {
        readonly BtsmtlGraphSemanticBuilder m_Builder;
        readonly IBtsmtlCanvasCompilationBindings m_Bindings;
        readonly string m_GraphId;
        readonly string m_Route;
        readonly string m_Hash;
        readonly List<ProgramGraphParameterBinding> m_Inputs = new();
        readonly List<ProgramGraphParameterBinding> m_Outputs = new();
        readonly List<int> m_StateSlots = new();

        public BtsmtlMacroCompilation(BtsmtlGraphSemanticBuilder builder, IBtsmtlCanvasCompilationBindings bindings,
            string graphId, string route, string hash, Macro macro, BtsmtlGraphOperationBindings operations)
        {
            m_Builder = builder ?? throw new ArgumentNullException(nameof(builder));
            m_Bindings = bindings;
            m_GraphId = graphId;
            m_Route = route;
            m_Hash = hash;
            BtsmtlMacroInterface.Validate(macro);
            MacroInputNode input = macro.allNodes.OfType<MacroInputNode>().Single();
            MacroOutputNode output = macro.allNodes.OfType<MacroOutputNode>().Single();
            Entry = Declare(input, "entry", OrchestrationOperationCode.Root);
            OperationHandle body = Declare(input, "body-and-results", OrchestrationOperationCode.Sequence);
            OperationHandle results = Declare(output, "results", OrchestrationOperationCode.Sequence);
            operations.AddNode(input, Entry);
            operations.SetFlowSource(input, body);
            operations.AddNode(output, results);
            Child(Entry, body, 0, Source(input, "entry"));
            // 子图体沿入口执行输出的连接排序，结果写回固定排在全部连接之后。
            Child(body, results, input.outConnections.Count + 1, Source(output, "results"));
            foreach (DynamicParameterDefinition parameter in macro.inputDefinitions)
            {
                if (parameter.type == typeof(Flow))
                    continue;
                SimulationSourceLocation source = Source(input, "parameter-read", parameter.ID);
                int slot = DeclareParameter(parameter, ProgramGraphParameterDirection.Input, source);
                OperationHandle reader = m_Builder.DeclareOperation(source, OrchestrationOperationCode.BlackboardGet,
                    Array.Empty<int>(), text0: parameter.ID, phase: "parameter-read:" + parameter.ID);
                BindState(reader, slot, source);
                operations.AddValueOutput(input, parameter.ID, reader, "m_Output");
            }
            int order = 0;
            foreach (DynamicParameterDefinition parameter in macro.outputDefinitions)
            {
                SimulationSourceLocation source = Source(output, "parameter-write", parameter.ID);
                int slot = DeclareParameter(parameter, ProgramGraphParameterDirection.Output, source);
                OperationHandle writer = m_Builder.DeclareOperation(source, OrchestrationOperationCode.BlackboardSet,
                    Array.Empty<int>(), integer0: 1, text0: parameter.ID, phase: "parameter-write:" + parameter.ID);
                BindState(writer, slot, source);
                operations.AddValueInput(output, parameter.ID, writer, "m_Value");
                Child(results, writer, order++, source);
                var port = (ValueInput)output.GetInputPort(parameter.ID);
                if (!port.isConnected)
                    BindDefault(writer, "m_Value", port, source);
            }
            if (m_StateSlots.Count != 0)
                m_Builder.DeclareScope($"{route}/scope:macro-parameters", ProgramScopeKind.Graph,
                    route, Entry, m_StateSlots, Source(input, "parameters"));
        }

        public OperationHandle Entry { get; }

        // callRoute 是本次出现的调用位置身份，与子图出现路径一致；调用操作登记到调用方图的绑定中。
        public OperationHandle EmitCall(MacroNodeWrapper node, string callerGraphId, string callRoute,
            string callerHash, BtsmtlGraphOperationBindings parent)
        {
            var source = new SimulationSourceLocation(node.GetType().FullName, callerGraphId, node.UID,
                string.Empty, string.Empty, string.Empty, callRoute, contentHash: callerHash);
            OperationHandle call = m_Builder.DeclareOperation(source, OrchestrationOperationCode.SubGraph, Array.Empty<int>());
            parent.AddNode(node, call);
            m_Builder.DeclareGraphCallFrame(callRoute, call, Entry, m_GraphId, m_Inputs, m_Outputs, source);
            m_Builder.DeclareControlFlow($"{callRoute}/entry", call, Entry, "Macro", "Entry",
                ProgramControlFlowKind.Enter, 0, 0, OrchestrationAbortPolicy.None, false, OperationHandle.Invalid, source);
            foreach (ValueInput port in node.GetInputValuePorts())
            {
                parent.AddValueInput(node, port.ID, call, port.ID);
                if (!port.isConnected)
                    BindDefault(call, port.ID, port, new SimulationSourceLocation(node.GetType().FullName,
                        callerGraphId, node.UID, string.Empty, string.Empty, string.Empty,
                        $"{callRoute}/port:{port.ID}", portId: port.ID, contentHash: callerHash));
            }
            foreach (ValueOutput port in node.GetOutputValuePorts())
                parent.AddValueOutput(node, port.ID, call, port.ID);
            return call;
        }

        int DeclareParameter(DynamicParameterDefinition parameter, ProgramGraphParameterDirection direction,
            SimulationSourceLocation source)
        {
            string owner = $"{m_Route}/parameter:{direction}:{parameter.ID}";
            SemanticValueKind kind = ValueKind(parameter.type);
            int value = m_Builder.DeclareStandaloneStateSlot(source, OrchestrationValuePortContracts.ToState(kind),
                ProgramStateOwnerKind.Blackboard, ProgramStateSemantic.BlackboardValue, owner,
                BtsmtlGraphValueTypes.AuthoringValue(parameter.type, Default(parameter.type)));
            m_StateSlots.Add(value);
            m_StateSlots.Add(m_Builder.DeclareStandaloneStateSlot(source, ProgramStateValueKind.BlackboardOwnerToken,
                ProgramStateOwnerKind.Blackboard, ProgramStateSemantic.BlackboardOwnerToken, owner));
            m_StateSlots.Add(m_Builder.DeclareStandaloneStateSlot(source, ProgramStateValueKind.Int32,
                ProgramStateOwnerKind.Blackboard, ProgramStateSemantic.BlackboardLifetime, owner, (int)ProgramBlackboardLifetime.GraphInstance));
            m_StateSlots.Add(m_Builder.DeclareStandaloneStateSlot(source, ProgramStateValueKind.BlackboardWriteStamp,
                ProgramStateOwnerKind.Blackboard, ProgramStateSemantic.BlackboardWriteStamp, owner));
            var binding = new ProgramGraphParameterBinding(direction, parameter.name,
                $"macro:{m_GraphId}/parameter:{direction}:{parameter.ID}", value, parameter.ID, kind);
            (direction == ProgramGraphParameterDirection.Input ? m_Inputs : m_Outputs).Add(binding);
            return value;
        }

        void BindDefault(OperationHandle operation, string compiledPort, ValueInput port, SimulationSourceLocation source)
        {
            object value = port.serializedValue;
            if (value == null && port.type == typeof(string))
                value = string.Empty;
            int constant = m_Builder.DeclareConstant(source, "default-value", BtsmtlGraphValueTypes.AuthoringValue(port.type, value));
            if (constant >= 0)
            {
                m_Builder.DeclareConstantInputBinding(operation, compiledPort, constant, ValueKind(port.type), source);
                m_Bindings?.BindInput(source.GraphId, source.NodeId, port.ID, constant);
            }
        }

        void BindState(OperationHandle operation, int slot, SimulationSourceLocation source) =>
            m_Builder.DeclareReference($"{source.Identity}/state", operation, ProgramReferenceKind.StateSlot, slot, source.PortId, source);

        OperationHandle Declare(FlowNode node, string phase, OrchestrationOperationCode code) =>
            m_Builder.DeclareOperation(Source(node, phase), code, Array.Empty<int>(), phase: phase);

        void Child(OperationHandle owner, OperationHandle child, int order, SimulationSourceLocation source) =>
            m_Builder.DeclareControlFlow($"{source.Identity}/child:{order}", owner, child, "Output", "Input",
                ProgramControlFlowKind.Child, order, 0, OrchestrationAbortPolicy.None, false, OperationHandle.Invalid, source);

        SimulationSourceLocation Source(FlowNode node, string phase, string port = "") =>
            new(node.GetType().FullName, m_GraphId, node.UID, string.Empty, string.Empty, string.Empty,
                $"{m_Route}/node:{node.UID}/phase:{phase}/port:{port}", portId: port, contentHash: m_Hash);

        static object Default(Type type) => type == typeof(string) ? string.Empty
            : type == typeof(Quaternion) ? Quaternion.identity : Activator.CreateInstance(type);

        static SemanticValueKind ValueKind(Type type)
        {
            BtsmtlMacroInterface.RequireValueType(type);
            return BtsmtlGraphValueTypes.Kind(type);
        }
    }
}
