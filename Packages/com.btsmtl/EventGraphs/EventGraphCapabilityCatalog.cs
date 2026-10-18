using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BTSMTL.Authoring;
using BTSMTL.Orchestration;
using FlowCanvas;
using FlowCanvas.Macros;
using FlowCanvas.Nodes;
using ParadoxNotion;

namespace BTSMTL.EventGraphs
{
    public readonly struct EventGraphNodeOperation
    {
        public EventGraphNodeOperation(OrchestrationOperationCode code, int integer0 = 0, int integer1 = 0,
            string text0 = "")
        {
            Code = code;
            Integer0 = integer0;
            Integer1 = integer1;
            Text0 = text0;
        }

        public OrchestrationOperationCode Code { get; }
        public int Integer0 { get; }
        public int Integer1 { get; }
        public string Text0 { get; }
    }

    // BTSMTL 支持的 EventGraph 节点只在此登记：原版 FlowCanvas 节点在外部登记，BTSMTL 自有节点在自身类型上声明。
    public static class EventGraphCapabilityCatalog
    {
        static readonly IReadOnlyDictionary<Type, EventGraphCapabilityKind> s_StockNodes =
            new Dictionary<Type, EventGraphCapabilityKind>
            {
                [typeof(StartEvent)] = EventGraphCapabilityKind.InitializationEvent,
                [typeof(UpdateEvent)] = EventGraphCapabilityKind.UpdateEvent,
                [typeof(GetVariable<>)] = EventGraphCapabilityKind.VariableGet,
                [typeof(SetVariable<>)] = EventGraphCapabilityKind.VariableSet,
                [typeof(SwitchBool)] = EventGraphCapabilityKind.Switch,
                [typeof(Split)] = EventGraphCapabilityKind.InstantSplit,
                [typeof(Sequence)] = EventGraphCapabilityKind.Sequence,
                [typeof(MacroNodeWrapper)] = EventGraphCapabilityKind.Macro,
                [typeof(MacroInputNode)] = EventGraphCapabilityKind.Macro,
                [typeof(MacroOutputNode)] = EventGraphCapabilityKind.Macro
            };

        public static bool TryGet(Type nodeType, out EventGraphCapabilityKind kind)
        {
            for (Type type = nodeType; type != null; type = type.BaseType)
            {
                if (s_StockNodes.TryGetValue(type.IsGenericType ? type.GetGenericTypeDefinition() : type, out kind))
                    return true;
                EventGraphCapabilityAttribute capability = type.GetCustomAttribute<EventGraphCapabilityAttribute>(false);
                if (capability != null)
                {
                    kind = capability.Kind;
                    return true;
                }
            }
            if (BtsmtlNativeValueNodeCatalog.TryGetNode(nodeType, out BtsmtlNativeValueNodeDefinition value))
            {
                kind = value.EventGraphCapability;
                return true;
            }
            kind = default;
            return false;
        }

        public static bool Allows(Type nodeType) => TryGet(nodeType, out _);

        public static EventGraphCapabilityKind Require(Type nodeType) =>
            TryGet(nodeType, out EventGraphCapabilityKind kind)
                ? kind
                : throw new InvalidOperationException($"EventGraph 节点类型 '{nodeType.FullName}' 没有登记。");

        public static bool TryGetPureValue(Type nodeType, out BtsmtlNativeValueNodeDefinition definition) =>
            BtsmtlNativeValueNodeCatalog.TryGetNode(nodeType, out definition);

        public static EventGraphNodeOperation Describe(FlowNode node)
        {
            EventGraphCapabilityKind kind = Require(node.GetType());
            switch (kind)
            {
                case EventGraphCapabilityKind.InitializationEvent:
                case EventGraphCapabilityKind.UpdateEvent:
                case EventGraphCapabilityKind.InstantSplit:
                    return new EventGraphNodeOperation(OrchestrationOperationCode.Sequence);
                case EventGraphCapabilityKind.Switch:
                    return new EventGraphNodeOperation(OrchestrationOperationCode.Branch);
                case EventGraphCapabilityKind.Sequence:
                {
                    var cycle = (Sequence)node;
                    int count = cycle.GetOutputFlowPorts().Count();
                    return new EventGraphNodeOperation(OrchestrationOperationCode.Cycle,
                        Math.Min(Math.Max(cycle.current, 0), count - 1), count);
                }
                case EventGraphCapabilityKind.HostInput:
                    return new EventGraphNodeOperation(OrchestrationOperationCode.HostInput);
                case EventGraphCapabilityKind.VariableGet:
                    return new EventGraphNodeOperation(OrchestrationOperationCode.BlackboardGet, 1, 0,
                        VariableId(node));
                case EventGraphCapabilityKind.VariableSet:
                    return new EventGraphNodeOperation(OrchestrationOperationCode.BlackboardSet, 1,
                        (int)(AssignOp)node.GetType().GetField("operation").GetValue(node), VariableId(node));
                case EventGraphCapabilityKind.PureValue:
                {
                    BtsmtlNativeValueNodeCatalog.TryGetNode(node.GetType(), out BtsmtlNativeValueNodeDefinition value);
                    return new EventGraphNodeOperation(value.Code, value.Variant);
                }
                default:
                    throw new InvalidOperationException($"节点 '{node.UID}' 的能力 '{kind}' 不发射独立指令。");
            }
        }

        public static string VariableId(FlowNode node) => ((ParameterVariableNode)node).parameter.targetVariableID;

        static readonly BtsmtlRuntimeEditableAttribute s_VariableEditable =
            new BtsmtlRuntimeEditableAttribute(BtsmtlRuntimeValueKind.Variable, "parameter.targetVariableID");

        // 原版节点不带 BTSMTL 特性，其运行改值声明同样在此登记。
        public static BtsmtlRuntimeEditableAttribute RuntimeEditable(Type nodeType) =>
            TryGet(nodeType, out EventGraphCapabilityKind kind) &&
            kind is EventGraphCapabilityKind.VariableGet or EventGraphCapabilityKind.VariableSet
                ? s_VariableEditable
                : null;
    }
}
