using BTSMTL.Orchestration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
#if UNITY_5_3_OR_NEWER
using FlowCanvas.Nodes;
using BTSMTL.Graphs;
#endif
using ThirdPersonSimulation;

namespace BTSMTL
{
    public sealed class BtsmtlNativeValueNodeDefinition
    {
        internal BtsmtlNativeValueNodeDefinition(string nodeTypeName, GraphNativeValueKind contract)
        {
            NodeTypeName = nodeTypeName;
            Contract = contract;
        }

        public string NodeTypeName { get; }
#if UNITY_5_3_OR_NEWER
        public Type NodeType => BtsmtlNativeValueNodeCatalog.SourceType(NodeTypeName);
#endif
        public GraphNativeValueKind Contract { get; }
        public string Kind => Contract.Kind;
        public OrchestrationOperationCode Code => Contract.Code;
        public int Variant => Contract.Variant;

        public string Input(string nativeId) => Contract.Input(nativeId);

        public string Output(string nativeId) => Contract.Output(nativeId);
    }

    public static class BtsmtlNativeValueNodeCatalog
    {
        static readonly IReadOnlyDictionary<string, BtsmtlNativeValueNodeDefinition> s_Nodes = Create();

        public static IEnumerable<BtsmtlNativeValueNodeDefinition> All => s_Nodes.Values;

        public static bool TryGet(string nodeTypeName, out BtsmtlNativeValueNodeDefinition definition) =>
            s_Nodes.TryGetValue(nodeTypeName, out definition);

#if UNITY_5_3_OR_NEWER
        static readonly IReadOnlyDictionary<string, Type> s_SourceTypes =
            new[] { typeof(SimplexNode).Assembly, typeof(BtsmtlFlowNode).Assembly }.Distinct()
                .SelectMany(assembly => assembly.GetTypes()).Where(type => s_Nodes.ContainsKey(type.Name))
                .ToDictionary(type => type.Name, StringComparer.Ordinal);

        internal static Type SourceType(string name) => s_SourceTypes[name];

        public static bool TryGet(Type nodeType, out BtsmtlNativeValueNodeDefinition definition) =>
            s_Nodes.TryGetValue(nodeType.Name, out definition) && definition.NodeType == nodeType;

        public static bool TryGetNode(Type nodeType, out BtsmtlNativeValueNodeDefinition definition)
        {
            for (Type type = nodeType; type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SimplexNodeWrapper<>))
                    return TryGet(type.GetGenericArguments()[0], out definition);
            definition = null;
            return false;
        }
#endif

        static IReadOnlyDictionary<string, BtsmtlNativeValueNodeDefinition> Create()
        {
            var nodes = new Dictionary<string, BtsmtlNativeValueNodeDefinition>(StringComparer.Ordinal);
            Add("AND", "and");
            Add("OR", "or");
            Add("NOT", "not");
            Add("FloatEqual", "compare.number.equal");
            Add("FloatNotEqual", "compare.number.not-equal");
            Add("FloatLessThan", "compare.number.less");
            Add("FloatLessEqualThan", "compare.number.less-equal");
            Add("FloatGreaterEqualThan", "compare.number.greater-equal");
            Add("FloatGreaterThan", "compare.number.greater");
            Add("IntegerEqual", "compare.int32.equal");
            Add("IntegerNotEqual", "compare.int32.not-equal");
            Add("IntegerLessThan", "compare.int32.less");
            Add("IntegerLessEqualThan", "compare.int32.less-equal");
            Add("IntegerGreaterEqualThan", "compare.int32.greater-equal");
            Add("IntegerGreaterThan", "compare.int32.greater");
            AddCalculation("FloatMultiply", GraphCalculationKind.FloatMultiply);
            AddCalculation("FloatDivide", GraphCalculationKind.FloatDivide);
            AddCalculation("FloatSubtract", GraphCalculationKind.FloatSubtract);
            AddCalculation("GraphTextEqualNode", GraphCalculationKind.TextEqual);
            AddCalculation("GraphFloatAbsNode", GraphCalculationKind.FloatAbs);
            AddCalculation("GraphFloatClampNode", GraphCalculationKind.FloatClamp);
            AddCalculation("GraphFloatMoveTowardsNode", GraphCalculationKind.FloatMoveTowards);
            AddCalculation("GraphVector3PlanarNode", GraphCalculationKind.Vector3Planar);
            AddCalculation("GraphVector3YNode", GraphCalculationKind.Vector3Y);
            AddCalculation("GraphVector2MagnitudeNode", GraphCalculationKind.Vector2Magnitude);
            AddCalculation("GraphVector2NormalizeNode", GraphCalculationKind.Vector2Normalize);
            AddCalculation("GraphVector2SubtractNode", GraphCalculationKind.Vector2Subtract);
            AddCalculation("GraphVector2SignedAngleNode", GraphCalculationKind.Vector2SignedAngle);
            AddCalculation("GraphVector2DotNode", GraphCalculationKind.Vector2Dot);
            AddCalculation("GraphVector2CrossNode", GraphCalculationKind.Vector2Cross);
            AddCalculation("GraphQuaternionForwardNode", GraphCalculationKind.QuaternionForward);
            AddCalculation("GraphQuaternionAngleAxisNode", GraphCalculationKind.QuaternionAngleAxis);
            Add("GraphFloatSelectNode", "select.number");
            return new ReadOnlyDictionary<string, BtsmtlNativeValueNodeDefinition>(nodes);

            void AddCalculation(string nodeTypeName, GraphCalculationKind kind) =>
                Add(nodeTypeName, GraphNativeValueKinds.Calculation(kind));

            void Add(string nodeTypeName, string kind) =>
                nodes.Add(nodeTypeName, new BtsmtlNativeValueNodeDefinition(nodeTypeName, GraphNativeValueKinds.Require(kind)));
        }
    }
}
