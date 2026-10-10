using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public enum GraphCalculationKind
    {
        FloatMultiply = 1,
        FloatDivide = 2,
        TextEqual = 3,
        FloatAbs = 4,
        FloatClamp = 5,
        FloatMoveTowards = 6,
        Vector3Planar = 7,
        Vector3Y = 8,
        Vector2Magnitude = 9,
        Vector2Normalize = 10,
        Vector2Subtract = 11,
        Vector2SignedAngle = 12,
        QuaternionForward = 13,
        QuaternionAngleAxis = 14
    }

    public static class GraphCalculationContracts
    {
        static readonly IReadOnlyDictionary<GraphCalculationKind, OperationValuePortContract> s_Values = Create();

        public static OperationValuePortContract Require(GraphCalculationKind kind) => s_Values[kind];

        static IReadOnlyDictionary<GraphCalculationKind, OperationValuePortContract> Create()
        {
            var values = new Dictionary<GraphCalculationKind, OperationValuePortContract>();
            Add(GraphCalculationKind.FloatMultiply, SemanticValueKind.Number,
                ("a", SemanticValueKind.Number), ("b", SemanticValueKind.Number));
            Add(GraphCalculationKind.FloatDivide, SemanticValueKind.Number,
                ("a", SemanticValueKind.Number), ("b", SemanticValueKind.Number));
            Add(GraphCalculationKind.TextEqual, SemanticValueKind.Boolean,
                ("a", SemanticValueKind.Identity), ("b", SemanticValueKind.Identity));
            Add(GraphCalculationKind.FloatAbs, SemanticValueKind.Number, ("value", SemanticValueKind.Number));
            Add(GraphCalculationKind.FloatClamp, SemanticValueKind.Number,
                ("value", SemanticValueKind.Number), ("minimum", SemanticValueKind.Number), ("maximum", SemanticValueKind.Number));
            Add(GraphCalculationKind.FloatMoveTowards, SemanticValueKind.Number,
                ("current", SemanticValueKind.Number), ("target", SemanticValueKind.Number), ("maxDelta", SemanticValueKind.Number));
            Add(GraphCalculationKind.Vector3Planar, SemanticValueKind.Vector2, ("value", SemanticValueKind.Vector3));
            Add(GraphCalculationKind.Vector3Y, SemanticValueKind.Number, ("value", SemanticValueKind.Vector3));
            Add(GraphCalculationKind.Vector2Magnitude, SemanticValueKind.Number, ("value", SemanticValueKind.Vector2));
            Add(GraphCalculationKind.Vector2Normalize, SemanticValueKind.Vector2,
                ("value", SemanticValueKind.Vector2), ("minimumSquaredMagnitude", SemanticValueKind.Number));
            Add(GraphCalculationKind.Vector2Subtract, SemanticValueKind.Vector2,
                ("a", SemanticValueKind.Vector2), ("b", SemanticValueKind.Vector2));
            Add(GraphCalculationKind.Vector2SignedAngle, SemanticValueKind.Number,
                ("from", SemanticValueKind.Vector2), ("to", SemanticValueKind.Vector2));
            Add(GraphCalculationKind.QuaternionForward, SemanticValueKind.Vector3, ("value", SemanticValueKind.Quaternion));
            Add(GraphCalculationKind.QuaternionAngleAxis, SemanticValueKind.Quaternion,
                ("angle", SemanticValueKind.Number), ("axis", SemanticValueKind.Vector3));
            return new ReadOnlyDictionary<GraphCalculationKind, OperationValuePortContract>(values);

            void Add(GraphCalculationKind kind, SemanticValueKind output, params (string Name, SemanticValueKind Kind)[] inputs)
            {
                var ports = new OperationValuePortDefinition[inputs.Length];
                for (int i = 0; i < ports.Length; i++)
                    ports[i] = new OperationValuePortDefinition(inputs[i].Name, i, OperationValuePortConstraint.Fixed, inputs[i].Kind);
                values.Add(kind, new OperationValuePortContract(SimulationOperationCode.Calculate, ports,
                    new[] { new OperationValuePortDefinition("m_Output", 0, OperationValuePortConstraint.Fixed, output) }));
            }
        }
    }
}
