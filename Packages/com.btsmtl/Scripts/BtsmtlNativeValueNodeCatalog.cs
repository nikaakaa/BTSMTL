using BTSMTL.Orchestration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FlowCanvas.Nodes;
using BTSMTL.Graphs;
using ThirdPersonSimulation;

namespace BTSMTL
{
    // FlowCanvas 原生节点类型与其 kind 合同的对应；编译规则由 GraphNativeValueKinds 按 kind 给出。
    public sealed class BtsmtlNativeValueNodeDefinition
    {
        internal BtsmtlNativeValueNodeDefinition(Type nodeType, GraphNativeValueKind contract)
        {
            NodeType = nodeType;
            Contract = contract;
        }

        public Type NodeType { get; }
        public GraphNativeValueKind Contract { get; }
        public string Kind => Contract.Kind;
        public OrchestrationOperationCode Code => Contract.Code;
        public int Variant => Contract.Variant;

        public string Input(string nativeId) => Contract.Input(nativeId);

        public string Output(string nativeId) => Contract.Output(nativeId);
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
            Add<AND>("and");
            Add<OR>("or");
            Add<NOT>("not");
            Add<FloatEqual>("compare.number.equal");
            Add<FloatNotEqual>("compare.number.not-equal");
            Add<FloatLessThan>("compare.number.less");
            Add<FloatLessEqualThan>("compare.number.less-equal");
            Add<FloatGreaterEqualThan>("compare.number.greater-equal");
            Add<FloatGreaterThan>("compare.number.greater");
            Add<IntegerEqual>("compare.int32.equal");
            Add<IntegerNotEqual>("compare.int32.not-equal");
            Add<IntegerLessThan>("compare.int32.less");
            Add<IntegerLessEqualThan>("compare.int32.less-equal");
            Add<IntegerGreaterEqualThan>("compare.int32.greater-equal");
            Add<IntegerGreaterThan>("compare.int32.greater");
            AddCalculation<FloatMultiply>(GraphCalculationKind.FloatMultiply);
            AddCalculation<FloatDivide>(GraphCalculationKind.FloatDivide);
            AddCalculation<FloatSubtract>(GraphCalculationKind.FloatSubtract);
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
            AddCalculation<GraphVector2DotNode>(GraphCalculationKind.Vector2Dot);
            AddCalculation<GraphVector2CrossNode>(GraphCalculationKind.Vector2Cross);
            AddCalculation<GraphQuaternionForwardNode>(GraphCalculationKind.QuaternionForward);
            AddCalculation<GraphQuaternionAngleAxisNode>(GraphCalculationKind.QuaternionAngleAxis);
            Add<GraphFloatSelectNode>("select.number");
            return new ReadOnlyDictionary<Type, BtsmtlNativeValueNodeDefinition>(nodes);

            void AddCalculation<T>(GraphCalculationKind kind) where T : SimplexNode =>
                Add<T>(GraphNativeValueKinds.Calculation(kind));

            void Add<T>(string kind) where T : SimplexNode =>
                nodes.Add(typeof(T), new BtsmtlNativeValueNodeDefinition(typeof(T), GraphNativeValueKinds.Require(kind)));
        }
    }
}
