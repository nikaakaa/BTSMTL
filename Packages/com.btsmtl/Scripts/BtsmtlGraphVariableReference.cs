using System;

namespace BTSMTL
{
    public readonly struct BtsmtlGraphVariableReference : IEquatable<BtsmtlGraphVariableReference>
    {
        public BtsmtlGraphVariableReference(string graphId, string variableId)
        {
            GraphId = RequireIdentity(graphId, nameof(graphId));
            VariableId = RequireIdentity(variableId, nameof(variableId));
        }

        public static string CompileIdentity(string graphId, string variableId) =>
            $"blackboard:{graphId}:{variableId}";

        public string GraphId { get; }
        public string VariableId { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(GraphId) && !string.IsNullOrWhiteSpace(VariableId);

        public bool Equals(BtsmtlGraphVariableReference other) =>
            string.Equals(GraphId, other.GraphId, StringComparison.Ordinal) &&
            string.Equals(VariableId, other.VariableId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is BtsmtlGraphVariableReference other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(GraphId, VariableId);
        public override string ToString() => $"{GraphId}/{VariableId}";

        static string RequireIdentity(string value, string parameterName) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("图变量身份缺失。", parameterName)
                : value.Trim();
    }

}
