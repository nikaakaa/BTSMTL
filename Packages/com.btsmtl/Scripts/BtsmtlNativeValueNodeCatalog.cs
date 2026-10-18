using BTSMTL.Orchestration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FlowCanvas.Nodes;
using BTSMTL.Graphs;
using ThirdPersonSimulation;

namespace BTSMTL
{
    public sealed class BtsmtlNativeValueNodeDefinition
    {
        internal BtsmtlNativeValueNodeDefinition(Type nodeType, string kind,
            OrchestrationOperationCode code, int variant, IReadOnlyDictionary<string, string> inputs)
        {
            NodeType = nodeType;
            Kind = kind;
            Code = code;
            Variant = variant;
            Inputs = inputs;
        }

        public Type NodeType { get; }
        public string Kind { get; }
        public OrchestrationOperationCode Code { get; }
        public int Variant { get; }
        public IReadOnlyDictionary<string, string> Inputs { get; }

        public string Input(string nativeId) => Inputs[nativeId];

        public string Output(string nativeId) => nativeId == "Value"
            ? Code == OrchestrationOperationCode.Compare ? "m_Result" : "m_Output"
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
            Add<AND>("and", OrchestrationOperationCode.And, boolean);
            Add<OR>("or", OrchestrationOperationCode.Or, boolean);
            Add<NOT>("not", OrchestrationOperationCode.Not, Ports(("value", "m_Input")));
            Add<FloatEqual>("compare.number.equal", OrchestrationOperationCode.Compare, comparison, 0);
            Add<FloatNotEqual>("compare.number.not-equal", OrchestrationOperationCode.Compare, comparison, 1);
            Add<FloatLessThan>("compare.number.less", OrchestrationOperationCode.Compare, comparison, 2);
            Add<FloatLessEqualThan>("compare.number.less-equal", OrchestrationOperationCode.Compare, comparison, 3);
            Add<FloatGreaterEqualThan>("compare.number.greater-equal", OrchestrationOperationCode.Compare, comparison, 4);
            Add<FloatGreaterThan>("compare.number.greater", OrchestrationOperationCode.Compare, comparison, 5);
            Add<IntegerEqual>("compare.int32.equal", OrchestrationOperationCode.Compare, comparison, 0);
            Add<IntegerNotEqual>("compare.int32.not-equal", OrchestrationOperationCode.Compare, comparison, 1);
            Add<IntegerLessThan>("compare.int32.less", OrchestrationOperationCode.Compare, comparison, 2);
            Add<IntegerLessEqualThan>("compare.int32.less-equal", OrchestrationOperationCode.Compare, comparison, 3);
            Add<IntegerGreaterEqualThan>("compare.int32.greater-equal", OrchestrationOperationCode.Compare, comparison, 4);
            Add<IntegerGreaterThan>("compare.int32.greater", OrchestrationOperationCode.Compare, comparison, 5);
            AddCalculation<FloatMultiply>(GraphCalculationKind.FloatMultiply);
            AddCalculation<FloatDivide>(GraphCalculationKind.FloatDivide);
            AddCalculation<GraphTextEqualNode>(GraphCalculationKind.TextEqual);
            AddCalculation<GraphFloatAbsNode>(GraphCalculationKind.FloatAbs);
            AddCalculation<GraphFloatClampNode>(GraphCalculationKind.FloatClamp);
            AddCalculation<GraphFloatMoveTowardsNode>(GraphCalculationKind.FloatMoveTowards);
            AddCalculation<GraphVector3PlanarNode>(GraphCalculationKind.Vector3Planar);
            AddCalculation<GraphVector3YNode>(GraphCalculationKind.Vector3Y);
            AddCalculation<GraphVector2MagnitudeNode>(GraphCalculationKind.Vector2Magnitude);
            AddCalculation<GraphVector2NormalizeNode>(GraphCalculationKind.Vector2Normalize);
            AddCalculation<GraphVector2SubtractNode>(GraphCalculationKind.Vector2Subtract);
            AddCalculation<GraphVector2SignedAngleNode>(GraphCalculationKind.Vector2SignedAngle);
            AddCalculation<GraphQuaternionForwardNode>(GraphCalculationKind.QuaternionForward);
            AddCalculation<GraphQuaternionAngleAxisNode>(GraphCalculationKind.QuaternionAngleAxis);
            Add<GraphFloatSelectNode>("select.number", OrchestrationOperationCode.SelectValue,
                Ports(("condition", "condition"), ("whenTrue", "whenTrue"), ("whenFalse", "whenFalse")));
            return new ReadOnlyDictionary<Type, BtsmtlNativeValueNodeDefinition>(nodes);

            void AddCalculation<T>(GraphCalculationKind kind) where T : SimplexNode
            {
                OperationValuePortContract contract = GraphCalculationContracts.Require(kind);
                var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (OperationValuePortDefinition port in contract.Inputs)
                    inputs.Add(port.Identity, port.Identity);
                Add<T>("calculate." + kind, OrchestrationOperationCode.Calculate,
                    new ReadOnlyDictionary<string, string>(inputs), (int)kind);
            }

            void Add<T>(string kind, OrchestrationOperationCode code,
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
