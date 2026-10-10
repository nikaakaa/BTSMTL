using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public interface ISimulationValueTraceInterest
    {
        bool IsValueCaptureRequested(ActorId actorId);
    }

    public interface ISimulationBlackboardTraceInterest
    {
        bool IsBlackboardCaptureRequested(ActorId actorId);
    }

    public static class SimulationValueTraceLimits
    {
        public const int MaxSamplesPerEvaluation = 4096;
    }

    public enum SemanticValueKind : byte
    {
        Boolean = 1,
        Int32 = 2,
        UInt64 = 3,
        Number = 4,
        Vector2 = 5,
        Vector3 = 6,
        Yaw = 7,
        Identity = 8,
        Quaternion = 9,
        Enum = 10
    }

    public enum OperationValuePortConstraint : byte
    {
        Fixed = 1,
        BooleanLike = 2,
        NumericLike = 3,
        Dynamic = 4
    }

    public enum CompiledValueInputSourceKind : byte
    {
        Operation = 1,
        Constant = 2
    }

    public readonly struct OperationValueInputRange
    {
        public OperationValueInputRange(int offset, int count)
        {
            if (offset < 0 || count < 0)
                throw new ArgumentOutOfRangeException();
            Offset = offset;
            Count = count;
        }

        public int Offset { get; }
        public int Count { get; }
    }

    public readonly struct CompiledValueInputBinding
    {
        public CompiledValueInputBinding(
            int targetPortIndex,
            SemanticValueKind resolvedValueKind,
            CompiledValueInputSourceKind sourceKind,
            OperationHandle sourceOperation,
            int sourceOutputPortIndex,
            string sourceOutputPortIdentity,
            int constantIndex)
        {
            if (targetPortIndex < 0 || sourceOutputPortIndex < -1 || constantIndex < -1)
                throw new ArgumentOutOfRangeException();
            if (!Enum.IsDefined(typeof(SemanticValueKind), resolvedValueKind) ||
                !Enum.IsDefined(typeof(CompiledValueInputSourceKind), sourceKind))
                throw new ArgumentOutOfRangeException();
            if (sourceKind == CompiledValueInputSourceKind.Operation && (!sourceOperation.IsValid || sourceOutputPortIndex < 0 || constantIndex >= 0))
                throw new ArgumentException("Operation Value source is incomplete.");
            if (sourceKind == CompiledValueInputSourceKind.Operation)
                SimulationIdentity.Require(sourceOutputPortIdentity, nameof(sourceOutputPortIdentity));
            if (sourceKind == CompiledValueInputSourceKind.Constant && (sourceOperation.IsValid || sourceOutputPortIndex >= 0 || constantIndex < 0))
                throw new ArgumentException("Constant Value source is incomplete.");
            TargetPortIndex = targetPortIndex;
            ResolvedValueKind = resolvedValueKind;
            SourceKind = sourceKind;
            SourceOperation = sourceOperation;
            SourceOutputPortIndex = sourceOutputPortIndex;
            SourceOutputPortIdentity = sourceOutputPortIdentity ?? string.Empty;
            ConstantIndex = constantIndex;
        }

        public int TargetPortIndex { get; }
        public SemanticValueKind ResolvedValueKind { get; }
        public CompiledValueInputSourceKind SourceKind { get; }
        public OperationHandle SourceOperation { get; }
        public int SourceOutputPortIndex { get; }
        public string SourceOutputPortIdentity { get; }
        public int ConstantIndex { get; }
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
            SimulationOperationCode operationCode,
            IEnumerable<OperationValuePortDefinition> inputs,
            IEnumerable<OperationValuePortDefinition> outputs)
        {
            OperationCode = operationCode;
            m_Inputs = Build(inputs, out m_InputByIdentity, "input");
            m_Outputs = Build(outputs, out m_OutputByIdentity, "output");
        }

        public SimulationOperationCode OperationCode { get; }
        public IReadOnlyList<OperationValuePortDefinition> Inputs => m_Inputs;
        public IReadOnlyList<OperationValuePortDefinition> Outputs => m_Outputs;

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

    public static class GameplayAbilityValuePortContracts
    {
        static readonly ReadOnlyDictionary<SimulationOperationCode, OperationValuePortContract> s_Contracts = Build();
        static readonly IReadOnlyDictionary<int, OperationValuePortContract> s_HostInputs = BuildHostInputs();
        static readonly IReadOnlyDictionary<int, OperationValuePortContract> s_Identities = BuildIdentities();

        static IReadOnlyDictionary<int, OperationValuePortContract> BuildIdentities()
        {
            var values = new Dictionary<int, OperationValuePortContract>();
            foreach (SemanticValueKind kind in Enum.GetValues(typeof(SemanticValueKind)))
                values.Add((int)kind, Both(SimulationOperationCode.IdentityValue,
                    new[] { Fixed("m_Value", 0, kind) }, new[] { Fixed("m_Output", 0, kind) }));
            return values;
        }

        static IReadOnlyDictionary<int, OperationValuePortContract> BuildHostInputs()
        {
            var values = new Dictionary<int, OperationValuePortContract>();
            foreach (SemanticValueKind kind in Enum.GetValues(typeof(SemanticValueKind)))
                values.Add((int)kind, Output(SimulationOperationCode.HostInput, Fixed("m_Output", 0, kind)));
            return values;
        }

        public static OperationValuePortContract Require(SimulationOperationCode code, int variant = 0)
        {
            if (code == SimulationOperationCode.Calculate)
                return GraphCalculationContracts.Require((GraphCalculationKind)variant);
            if (code == SimulationOperationCode.HostInput)
                return s_HostInputs[variant];
            if (code == SimulationOperationCode.IdentityValue)
                return s_Identities[variant];
            if (!s_Contracts.TryGetValue(code, out OperationValuePortContract contract))
                throw new InvalidOperationException($"Operation '{code}' has no Value port contract in '{GameplayAbilityOperationSet.Version.Value}'.");
            return contract;
        }

        public static OperationValuePortContract Require(
            SimulationOperationCode code,
            OperationHandle operation,
            IReadOnlyList<ProgramGraphCallFrame> graphCallFrames,
            int variant = 0)
        {
            if (code != SimulationOperationCode.SubGraph)
                return Require(code, variant);
            if (!operation.IsValid)
                throw new ArgumentException("A valid SubGraph operation is required.", nameof(operation));

            ProgramGraphCallFrame frame = null;
            for (int i = 0; i < (graphCallFrames?.Count ?? 0); i++)
            {
                ProgramGraphCallFrame candidate = graphCallFrames[i];
                if (!candidate.OwnerOperation.Equals(operation))
                    continue;
                if (frame != null)
                    throw new InvalidOperationException($"SubGraph operation '{operation}' has multiple graph call frames.");
                frame = candidate;
            }
            if (frame == null)
                throw new InvalidOperationException($"SubGraph operation '{operation}' has no graph call frame.");
            return new OperationValuePortContract(
                code,
                BuildGraphPorts(frame.Inputs),
                BuildGraphPorts(frame.Outputs));
        }

        static OperationValuePortDefinition[] BuildGraphPorts(
            IReadOnlyList<ProgramGraphParameterBinding> bindings)
        {
            var result = new OperationValuePortDefinition[bindings?.Count ?? 0];
            for (int i = 0; i < result.Length; i++)
            {
                ProgramGraphParameterBinding binding = bindings[i];
                result[i] = new OperationValuePortDefinition(
                    binding.PortId,
                    i,
                    OperationValuePortConstraint.Fixed,
                    binding.ValueKind);
            }
            return result;
        }

        public static SemanticValueKind FromLiteral(SemanticLiteralKind kind)
        {
            return kind switch
            {
                SemanticLiteralKind.Boolean => SemanticValueKind.Boolean,
                SemanticLiteralKind.Int32 => SemanticValueKind.Int32,
                SemanticLiteralKind.UInt64 => SemanticValueKind.UInt64,
                SemanticLiteralKind.Number => SemanticValueKind.Number,
                SemanticLiteralKind.Vector2 => SemanticValueKind.Vector2,
                SemanticLiteralKind.Vector3 => SemanticValueKind.Vector3,
                SemanticLiteralKind.Yaw => SemanticValueKind.Yaw,
                SemanticLiteralKind.String => SemanticValueKind.Identity,
                SemanticLiteralKind.Quaternion => SemanticValueKind.Quaternion,
                SemanticLiteralKind.Enum => SemanticValueKind.Enum,
                _ => throw new InvalidOperationException($"Semantic literal kind '{kind}' cannot enter the Value graph.")
            };
        }

        public static SemanticValueKind FromState(ProgramStateValueKind kind)
        {
            return kind switch
            {
                ProgramStateValueKind.Boolean => SemanticValueKind.Boolean,
                ProgramStateValueKind.Int32 => SemanticValueKind.Int32,
                ProgramStateValueKind.UInt64 => SemanticValueKind.UInt64,
                ProgramStateValueKind.Scalar => SemanticValueKind.Number,
                ProgramStateValueKind.Vector2 => SemanticValueKind.Vector2,
                ProgramStateValueKind.Vector3 => SemanticValueKind.Vector3,
                ProgramStateValueKind.Yaw => SemanticValueKind.Yaw,
                ProgramStateValueKind.Identity => SemanticValueKind.Identity,
                ProgramStateValueKind.Quaternion => SemanticValueKind.Quaternion,
                ProgramStateValueKind.Enum => SemanticValueKind.Enum,
                _ => throw new InvalidOperationException($"Program state kind '{kind}' cannot enter the Value graph.")
            };
        }

		static ReadOnlyDictionary<SimulationOperationCode, OperationValuePortContract> Build()
		{
			var values = new Dictionary<SimulationOperationCode, OperationValuePortContract>();
			for (int i = 0; i < GameplayAbilityOperationSet.Operations.Count; i++)
			{
				SimulationOperationCode code = GameplayAbilityOperationSet.Operations[i];
				values.Add(code, Empty(code));
			}

			Set(values, Output(SimulationOperationCode.StateRootCompleted, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
            values.Add(SimulationOperationCode.Branch, Input(SimulationOperationCode.Branch, Fixed("Condition", 0, SemanticValueKind.Boolean)));
            values.Add(SimulationOperationCode.Cycle, Output(SimulationOperationCode.Cycle, Fixed("Current", 0, SemanticValueKind.Int32)));
            values.Add(SimulationOperationCode.SelectValue, Both(SimulationOperationCode.SelectValue,
                new[] { Fixed("condition", 0, SemanticValueKind.Boolean), Fixed("whenTrue", 1, SemanticValueKind.Number),
                    Fixed("whenFalse", 2, SemanticValueKind.Number) },
                new[] { Fixed("m_Output", 0, SemanticValueKind.Number) }));
			Set(values, Empty(SimulationOperationCode.SubGraph));
			Set(values, Output(SimulationOperationCode.StateExitCause, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Input(SimulationOperationCode.TimelineClipExitRequest, Fixed("m_When", 0, SemanticValueKind.Boolean)));
            Set(values, Output(SimulationOperationCode.ActivationEntry, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
            Set(values, Output(SimulationOperationCode.TimelineTime, Fixed("m_Output", 0, SemanticValueKind.Number)));
            Set(values, Output(SimulationOperationCode.ActionTargetRead,
                Fixed("m_HasTarget", 0, SemanticValueKind.Boolean),
                Fixed("m_Position", 1, SemanticValueKind.Vector3),
                Fixed("m_Yaw", 2, SemanticValueKind.Number),
                Fixed("m_PlanarDistance", 3, SemanticValueKind.Number)));
            Set(values, Both(SimulationOperationCode.MotionWarp,
                new[] {
                    Fixed("m_Enabled", 0, SemanticValueKind.Boolean),
                    Fixed("m_HasTarget", 1, SemanticValueKind.Boolean),
                    Fixed("m_TargetPosition", 2, SemanticValueKind.Vector3),
                    Fixed("m_TargetYaw", 3, SemanticValueKind.Number),
                    Fixed("m_InputDirection", 4, SemanticValueKind.Vector2)
                },
                new[] { Fixed("m_WindowEndTime", 0, SemanticValueKind.Number) }));
            Set(values, Input(SimulationOperationCode.Timeline,
                Fixed("m_PlaybackRate", 0, SemanticValueKind.Number),
                Fixed("m_Paused", 1, SemanticValueKind.Boolean),
                Fixed("m_StartTime", 2, SemanticValueKind.Number)));
			Set(values, Output(SimulationOperationCode.BlackboardGet, Dynamic("m_Output", 0)));
			Set(values, Input(SimulationOperationCode.BlackboardSet, Dynamic("m_Value", 0)));
			Set(values, Output(SimulationOperationCode.InputBoolean, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.InputScalar, Fixed("m_Output", 0, SemanticValueKind.Number)));
			Set(values, Output(SimulationOperationCode.InputVector2, Fixed("m_Output", 0, SemanticValueKind.Vector2)));
			Set(values, Output(SimulationOperationCode.InputVector2Magnitude, Fixed("m_Output", 0, SemanticValueKind.Number)));
			Set(values, Output(SimulationOperationCode.InputRequest, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Both(SimulationOperationCode.MoveFacingAngle,
				new[] { Fixed("m_MoveInput", 0, SemanticValueKind.Vector2) },
				new[] { Fixed("m_Output", 0, SemanticValueKind.Number) }));
			Set(values, Output(SimulationOperationCode.CharacterStateRead, Dynamic("m_Output", 0)));
			Set(values, Output(SimulationOperationCode.ActivateActionInstance, Fixed("m_Activated", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.ActionContextActive, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.SubmitActionLifecycle, Fixed("m_Submitted", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.ActionWindowActive, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.AbilityActive, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.SelectedSourceWindowActive, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.SelectedSourcePhase, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.LocomotionDecision, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.CanActivateAction, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.ActionEventReceived, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Input(SimulationOperationCode.LocomotionInputMotion, Fixed("m_MoveInput", 0, SemanticValueKind.Vector2)));
			Set(values, Input(SimulationOperationCode.ConditionResult, BooleanLike("m_Result", 0)));
			Set(values, Both(SimulationOperationCode.Compare,
				new[] { NumericLike("m_InputValue1", 0, "compare"), NumericLike("m_InputValue2", 1, "compare") },
				new[] { Fixed("m_Result", 0, SemanticValueKind.Boolean) }));
			Set(values, Both(SimulationOperationCode.And,
				new[] { BooleanLike("m_Input1", 0), BooleanLike("m_Input2", 1) },
				new[] { Fixed("m_Output", 0, SemanticValueKind.Boolean) }));
			Set(values, Both(SimulationOperationCode.Or,
				new[] { BooleanLike("m_Input1", 0), BooleanLike("m_Input2", 1) },
				new[] { Fixed("m_Output", 0, SemanticValueKind.Boolean) }));
			Set(values, Both(SimulationOperationCode.Not,
				new[] { BooleanLike("m_Input", 0) },
				new[] { Fixed("m_Output", 0, SemanticValueKind.Boolean) }));
			Set(values, Output(SimulationOperationCode.Constant, Dynamic("m_Output", 0)));
			Set(values, Output(SimulationOperationCode.GameplayEffectHasTag, Fixed("m_Result", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.GameplayEffectMatchTags, Fixed("m_Result", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.GameplayAttributeRead,
				Fixed("m_Valid", 0, SemanticValueKind.Boolean),
				Fixed("m_BaseValue", 1, SemanticValueKind.Number),
				Fixed("m_CurrentValue", 2, SemanticValueKind.Number)));
			Set(values, Output(SimulationOperationCode.GameplayEffectApply, Fixed("m_Applied", 0, SemanticValueKind.Boolean), Fixed("m_Handle", 1, SemanticValueKind.UInt64)));
			Set(values, Output(SimulationOperationCode.AttackQuery, Fixed("m_Submitted", 0, SemanticValueKind.Boolean)));
			Set(values, Output(SimulationOperationCode.AttackHitQuery, Fixed("m_Hit", 0, SemanticValueKind.Boolean)));
			Set(values, Both(SimulationOperationCode.GameplayEffectRemove, new[] { Fixed("m_Handle", 0, SemanticValueKind.UInt64) }, new[] { Fixed("m_Removed", 0, SemanticValueKind.Boolean) }));
			Set(values, Output(SimulationOperationCode.CameraBasisRead,
				Fixed(CameraProgramOperationSchema.BasisValidPortId, 0, SemanticValueKind.Boolean),
				Fixed(CameraProgramOperationSchema.BasisPlanarForwardPortId, 1, SemanticValueKind.Vector3),
				Fixed(CameraProgramOperationSchema.BasisPlanarRightPortId, 2, SemanticValueKind.Vector3),
				Fixed(CameraProgramOperationSchema.BasisLookDirectionPortId, 3, SemanticValueKind.Vector3),
				Fixed(CameraProgramOperationSchema.BasisAimPointPortId, 4, SemanticValueKind.Vector3),
				Fixed(CameraProgramOperationSchema.BasisYawPortId, 5, SemanticValueKind.Yaw),
				Fixed(CameraProgramOperationSchema.BasisPitchPortId, 6, SemanticValueKind.Number)));
			Set(values, Output(
				SimulationOperationCode.ReadEquipmentIdentity,
				Fixed("m_Equipment", 0, SemanticValueKind.Identity),
				Fixed("m_Feature", 1, SemanticValueKind.Identity),
				Fixed("m_Revision", 2, SemanticValueKind.UInt64),
				Fixed("m_Equipped", 3, SemanticValueKind.Boolean)));
			Set(values, Both(SimulationOperationCode.ReadEquipmentParameter, new[] { Fixed("m_ExpectedRevision", 0, SemanticValueKind.UInt64) }, new[] { Dynamic("m_Output", 0) }));
			Set(values, Both(SimulationOperationCode.RequestEquipmentChange, new[] { Fixed("m_ExpectedRevision", 0, SemanticValueKind.UInt64) }, new[] { Fixed("m_Accepted", 0, SemanticValueKind.Boolean), Fixed("m_Failure", 1, SemanticValueKind.Int32) }));
			Set(values, Both(SimulationOperationCode.BeginEquipmentChange, new[] { Fixed("m_ExpectedRevision", 0, SemanticValueKind.UInt64) }, new[] { Fixed("m_Begun", 0, SemanticValueKind.Boolean), Fixed("m_ChangeId", 1, SemanticValueKind.UInt64), Fixed("m_Failure", 2, SemanticValueKind.Int32) }));
			Set(values, Both(SimulationOperationCode.CommitEquipmentChange, new[] { Fixed("m_ChangeId", 0, SemanticValueKind.UInt64) }, new[] { Fixed("m_Committed", 0, SemanticValueKind.Boolean), Fixed("m_Failure", 1, SemanticValueKind.Int32) }));
			Set(values, Both(SimulationOperationCode.CancelEquipmentChange, new[] { Fixed("m_ChangeId", 0, SemanticValueKind.UInt64) }, new[] { Fixed("m_Cancelled", 0, SemanticValueKind.Boolean), Fixed("m_Failure", 1, SemanticValueKind.Int32) }));

			return new ReadOnlyDictionary<SimulationOperationCode, OperationValuePortContract>(values);
		}

        static void Set(Dictionary<SimulationOperationCode, OperationValuePortContract> values, OperationValuePortContract contract)
        {
            if (!values.ContainsKey(contract.OperationCode))
                throw new InvalidOperationException($"Value port contract targets unknown operation '{contract.OperationCode}'.");
            values[contract.OperationCode] = contract;
        }

        static OperationValuePortContract Empty(SimulationOperationCode code) =>
            new OperationValuePortContract(code, Array.Empty<OperationValuePortDefinition>(), Array.Empty<OperationValuePortDefinition>());

        static OperationValuePortContract Input(SimulationOperationCode code, params OperationValuePortDefinition[] inputs) =>
            new OperationValuePortContract(code, inputs, Array.Empty<OperationValuePortDefinition>());

        static OperationValuePortContract Output(SimulationOperationCode code, params OperationValuePortDefinition[] outputs) =>
            new OperationValuePortContract(code, Array.Empty<OperationValuePortDefinition>(), outputs);

        static OperationValuePortContract Both(SimulationOperationCode code, OperationValuePortDefinition[] inputs, OperationValuePortDefinition[] outputs) =>
            new OperationValuePortContract(code, inputs, outputs);

        static OperationValuePortDefinition Fixed(string identity, int order, SemanticValueKind kind) =>
            new OperationValuePortDefinition(identity, order, OperationValuePortConstraint.Fixed, kind);

        static OperationValuePortDefinition BooleanLike(string identity, int order) =>
            new OperationValuePortDefinition(identity, order, OperationValuePortConstraint.BooleanLike);

        static OperationValuePortDefinition NumericLike(string identity, int order, string group) =>
            new OperationValuePortDefinition(identity, order, OperationValuePortConstraint.NumericLike, constraintGroup: group);

        static OperationValuePortDefinition Dynamic(string identity, int order) =>
            new OperationValuePortDefinition(identity, order, OperationValuePortConstraint.Dynamic);
    }
}
                                                                                                                                                                                                                                                                                                                                                                                         
