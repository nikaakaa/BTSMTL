using BTSMTL.Orchestration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public enum OperationValuePortConstraint : byte
    {
        Fixed = 1,
        BooleanLike = 2,
        NumericLike = 3,
        Dynamic = 4
    }

    public sealed class OperationValuePortDefinition
    {
        public OperationValuePortDefinition(
            string identity,
            int order,
            OperationValuePortConstraint constraint,
            SemanticValueKind fixedKind = default,
            string constraintGroup = "")
        {
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            if (order < 0)
                throw new ArgumentOutOfRangeException(nameof(order));
            if (!Enum.IsDefined(typeof(OperationValuePortConstraint), constraint))
                throw new ArgumentOutOfRangeException(nameof(constraint));
            if (constraint == OperationValuePortConstraint.Fixed && !Enum.IsDefined(typeof(SemanticValueKind), fixedKind))
                throw new ArgumentOutOfRangeException(nameof(fixedKind));
            Order = order;
            Constraint = constraint;
            FixedKind = fixedKind;
            ConstraintGroup = constraintGroup ?? string.Empty;
        }

        public string Identity { get; }
        public int Order { get; }
        public OperationValuePortConstraint Constraint { get; }
        public SemanticValueKind FixedKind { get; }
        public string ConstraintGroup { get; }

        public bool Accepts(SemanticValueKind kind)
        {
            if (!Enum.IsDefined(typeof(SemanticValueKind), kind))
                return false;
            return Constraint switch
            {
                OperationValuePortConstraint.Fixed => kind == FixedKind,
                OperationValuePortConstraint.BooleanLike => IsBooleanLike(kind),
                OperationValuePortConstraint.NumericLike => IsNumericLike(kind),
                OperationValuePortConstraint.Dynamic => true,
                _ => false
            };
        }

        public SemanticValueKind Resolve(SemanticValueKind actualKind)
        {
            if (!Accepts(actualKind))
                throw new InvalidOperationException($"Value kind '{actualKind}' is not accepted by port '{Identity}' ({Constraint}).");
            return Constraint == OperationValuePortConstraint.Fixed ? FixedKind : actualKind;
        }

        public static bool IsBooleanLike(SemanticValueKind kind) =>
            kind == SemanticValueKind.Boolean ||
            kind == SemanticValueKind.Int32 ||
            kind == SemanticValueKind.UInt64 ||
            kind == SemanticValueKind.Number ||
            kind == SemanticValueKind.Identity;

        public static bool IsNumericLike(SemanticValueKind kind) =>
            kind == SemanticValueKind.Boolean ||
            kind == SemanticValueKind.Int32 ||
            kind == SemanticValueKind.UInt64 ||
            kind == SemanticValueKind.Number;
    }

    public sealed class OperationValuePortContract
    {
        readonly ReadOnlyCollection<OperationValuePortDefinition> m_Inputs;
        readonly ReadOnlyCollection<OperationValuePortDefinition> m_Outputs;
        readonly Dictionary<string, OperationValuePortDefinition> m_InputByIdentity;
        readonly Dictionary<string, OperationValuePortDefinition> m_OutputByIdentity;

        public OperationValuePortContract(
            OrchestrationOperationCode operationCode,
            IEnumerable<OperationValuePortDefinition> inputs,
            IEnumerable<OperationValuePortDefinition> outputs,
            bool outputsRequireExecution = false)
        {
            OperationCode = operationCode;
            m_Inputs = Build(inputs, out m_InputByIdentity, "input");
            m_Outputs = Build(outputs, out m_OutputByIdentity, "output");
            OutputsRequireExecution = outputsRequireExecution;
        }

        public OrchestrationOperationCode OperationCode { get; }
        public IReadOnlyList<OperationValuePortDefinition> Inputs => m_Inputs;
        public IReadOnlyList<OperationValuePortDefinition> Outputs => m_Outputs;
        public bool OutputsRequireExecution { get; }

        public OperationValuePortDefinition RequireInput(string identity) => Require(m_InputByIdentity, identity, "input");
        public OperationValuePortDefinition RequireSelection(string identity) => Require(m_OutputByIdentity, identity, "output");

        static ReadOnlyCollection<OperationValuePortDefinition> Build(
            IEnumerable<OperationValuePortDefinition> source,
            out Dictionary<string, OperationValuePortDefinition> byIdentity,
            string direction)
        {
            var values = new List<OperationValuePortDefinition>(source ?? Array.Empty<OperationValuePortDefinition>());
            values.Sort((left, right) =>
            {
                int byOrder = left.Order.CompareTo(right.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(left.Identity, right.Identity);
            });
            byIdentity = new Dictionary<string, OperationValuePortDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                OperationValuePortDefinition value = values[i] ?? throw new InvalidOperationException($"Value {direction} contract contains null.");
                if (value.Order != i)
                    throw new InvalidOperationException($"Value {direction} contract orders must be contiguous from zero.");
                if (!byIdentity.TryAdd(value.Identity, value))
                    throw new InvalidOperationException($"Value {direction} port '{value.Identity}' is duplicated.");
            }
            return values.AsReadOnly();
        }

        OperationValuePortDefinition Require(
            IReadOnlyDictionary<string, OperationValuePortDefinition> values,
            string identity,
            string direction)
        {
            string key = identity ?? string.Empty;
            if (!values.TryGetValue(key, out OperationValuePortDefinition value))
                throw new InvalidOperationException($"Operation '{OperationCode}' has no Value {direction} port '{key}'.");
            return value;
        }
    }
}
