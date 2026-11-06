using BTSMTL.Orchestration;
using BTSMTL.Authoring.Graph;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
        public GraphNativeValueKind Contract { get; }
        public string Kind => Contract.Kind;
        public OrchestrationOperationCode Code => Contract.Code;
        public int Variant => Contract.Variant;

        public string Input(string nativeId) => Contract.Input(nativeId);

        public string Output(string nativeId) => Contract.Output(nativeId);

        // 原生数值节点的端口由其运行合同决定。
        public IReadOnlyList<GraphNodePort> Ports
        {
            get
            {
                var ports = new List<GraphNodePort>();
                foreach (KeyValuePair<string, string> input in Contract.Inputs)
                    ports.Add(new GraphNodePort(GraphAuthoringPortDirection.Input, false, input.Key, input.Key,
                        ValueType(Contract.ValuePorts.RequireInput(input.Value).FixedKind), ports.Count));
                ports.Add(new GraphNodePort(GraphAuthoringPortDirection.Output, false, "Value", "Value",
                    ValueType(Contract.ValuePorts.RequireSelection(Output("Value")).FixedKind), ports.Count));
                return ports;
            }
        }

        static string ValueType(SemanticValueKind kind) => kind switch
        {
            SemanticValueKind.Boolean => "bool", SemanticValueKind.Int32 => "int",
            SemanticValueKind.UInt64 => "ulong", SemanticValueKind.Number => "float",
            SemanticValueKind.ObjectHandle => "object-handle",
            SemanticValueKind.Identity => "string", SemanticValueKind.Vector2 => "vector2",
            SemanticValueKind.Vector3 => "vector3", SemanticValueKind.Quaternion => "quaternion",
            _ => throw new ArgumentException($"原生端口没有作者值类型：{kind}。", nameof(kind))
        };
    }

    public static class BtsmtlNativeValueNodeCatalog
    {
        static readonly IReadOnlyDictionary<string, BtsmtlNativeValueNodeDefinition> s_Nodes = Create();

        public static IEnumerable<BtsmtlNativeValueNodeDefinition> All => s_Nodes.Values;

        public static bool TryGet(string nodeTypeName, out BtsmtlNativeValueNodeDefinition definition) =>
            s_Nodes.TryGetValue(nodeTypeName, out definition);

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
            AddCalculation("GraphVector3ScaleNode", GraphCalculationKind.Vector3Scale);
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
