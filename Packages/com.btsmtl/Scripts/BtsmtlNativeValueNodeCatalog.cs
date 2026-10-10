using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FlowCanvas.Nodes;
using BTSMTL.EventGraphs;
using ThirdPersonSimulation;

namespace BTSMTL
{
    public sealed class BtsmtlNativeValueNodeDefinition
    {
        internal BtsmtlNativeValueNodeDefinition(Type nodeType, string kind,
            SimulationOperationCode code, int variant, IReadOnlyDictionary<string, string> inputs)
        {
            NodeType = nodeType;
            Kind = kind;
            Code = code;
            Variant = variant;
            Inputs = inputs;
        }

        public Type NodeType { get; }
        public string Kind { get; }
        public SimulationOperationCode Code { get; }
        public int Variant { get; }
        public IReadOnlyDictionary<string, string> Inputs { get; }

        public string Input(string nativeId) => Inputs[nativeId];

        public string Output(string nativeId) => nativeId == "Value"
            ? Code == SimulationOperationCode.Compare ? "m_Result" : "m_Output"
            : throw new InvalidOperationException($"Native value output '{nativeId}' is not declared for '{Kind}'.");
    }

    public static class BtsmtlNativeValueNodeCatalog
    {
        static readonly IReadOnlyDictionary<Type, BtsmtlNativeValueNodeDefinition> s_Nodes = Create();

        public static IEnumerable<BtsmtlNativeValueNodeDefinition> All => s_Nodes.Values;

        public static bool TryGet(Type nodeType, out BtsmtlNativeValueNodeDefinition definition) =>
            s_Nodes.TryGetValue(nodeType, out definition);

        public static bool TryGetNode(Type nodeType, out BtsmtlNativeValueNodeDefinition definition)
        {
            for (Type type = nodeType; type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SimplexNodeWrapper<>))
                    return TryGet(type.GetGenericArguments()[0], out definition);
            definition = null;
            return false;
        }

        static IReadOnlyDictionary<Type, BtsmtlNativeValueNodeDefinition> Create()
        {
            var nodes = new Dictionary<Type, BtsmtlNativeValueNodeDefinition>();
            var boolean = Ports(("a", "m_Input1"), ("b", "m_Input2"));
            var comparison = Ports(("a", "m_InputValue1"), ("b", "m_InputValue2"));
            Add<AND>("and", SimulationOperationCode.And, boolean);
            Add<OR>("or", SimulationOperationCode.Or, boolean);
            Add<NOT>("not", SimulationOperationCode.Not, Ports(("value", "m_Input")));
            Add<FloatEqual>("compare.number.equal", SimulationOperationCode.Compare, comparison, 0);
            Add<FloatNotEqual>("compare.number.not-equal", SimulationOperationCode.Compare, comparison, 1);
            Add<FloatLessThan>("compare.number.less", SimulationOperationCode.Compare, comparison, 2);
            Add<FloatLessEqualThan>("compare.number.less-equal", SimulationOperationCode.Compare, comparison, 3);
            Add<FloatGreaterEqualThan>("compare.number.greater-equal", SimulationOperationCode.Compare, comparison, 4);
            Add<FloatGreaterThan>("compare.number.greater", SimulationOperationCode.Compare, comparison, 5);
            Add<IntegerEqual>("compare.int32.equal", SimulationOperationCode.Compare, comparison, 0);
            Add<IntegerNotEqual>("compare.int32.not-equal", SimulationOperationCode.Compare, comparison, 1);
            Add<IntegerLessThan>("compare.int32.less", SimulationOperationCode.Compare, comparison, 2);
            Add<IntegerLessEqualThan>("compare.int32.less-equal", SimulationOperationCode.Compare, comparison, 3);
            Add<IntegerGreaterEqualThan>("compare.int32.greater-equal", SimulationOperationCode.Compare, comparison, 4);
            Add<IntegerGreaterThan>("compare.int32.greater", SimulationOperationCode.Compare, comparison, 5);
            AddCalculation<FloatMultiply>(GraphCalculationKind.FloatMultiply);
            AddCalculation<FloatDivide>(GraphCalculationKind.FloatDivide);
            AddCalculation<EventGraphTextEqualNode>(GraphCalculationKind.TextEqual);
            AddCalculation<EventGraphFloatAbsNode>(GraphCalculationKind.FloatAbs);
            AddCalculation<EventGraphFloatClampNode>(GraphCalculationKind.FloatClamp);
            AddCalculation<EventGraphFloatMoveTowardsNode>(GraphCalculationKind.FloatMoveTowards);
            AddCalculation<EventGraphVector3PlanarNode>(GraphCalculationKind.Vector3Planar);
            AddCalculation<EventGraphVector3YNode>(GraphCalculationKind.Vector3Y);
            AddCalculation<EventGraphVector2MagnitudeNode>(GraphCalculationKind.Vector2Magnitude);
            AddCalculation<EventGraphVector2NormalizeNode>(GraphCalculationKind.Vector2Normalize);
            AddCalculation<EventGraphVector2SubtractNode>(GraphCalculationKind.Vector2Subtract);
            AddCalculation<EventGraphVector2SignedAngleNode>(GraphCalculationKind.Vector2SignedAngle);
            AddCalculation<EventGraphQuaternionForwardNode>(GraphCalculationKind.QuaternionForward);
            AddCalculation<EventGraphQuaternionAngleAxisNode>(GraphCalculationKind.QuaternionAngleAxis);
            Add<EventGraphFloatSelectNode>("select.number", SimulationOperationCode.SelectValue,
                Ports(("condition", "condition"), ("whenTrue", "whenTrue"), ("whenFalse", "whenFalse")));
            return new ReadOnlyDictionary<Type, BtsmtlNativeValueNodeDefinition>(nodes);

            void AddCalculation<T>(GraphCalculationKind kind) where T : SimplexNode
            {
                OperationValuePortContract contract = GraphCalculationContracts.Require(kind);
                var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (OperationValuePortDefinition port in contract.Inputs)
                    inputs.Add(port.Identity, port.Identity);
                Add<T>("calculate." + kind, SimulationOperationCode.Calculate,
                    new ReadOnlyDictionary<string, string>(inputs), (int)kind);
            }

            void Add<T>(string kind, SimulationOperationCode code,
                IReadOnlyDictionary<string, string> inputs, int variant = 0) where T : SimplexNode
            {
                Type nodeType = typeof(T);
                nodes.Add(nodeType, new BtsmtlNativeValueNodeDefinition(nodeType, kind, code, variant, inputs));
            }
        }

        static IReadOnlyDictionary<string, string> Ports(params (string Native, string Compiled)[] values)
        {
            var ports = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var value in values)
                ports.Add(value.Native, value.Compiled);
            return new ReadOnlyDictionary<string, string>(ports);
        }
    }
}
