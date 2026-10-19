using BTSMTL.Orchestration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public static class OrchestrationValuePortContracts
    {
        static readonly ReadOnlyDictionary<OrchestrationOperationCode, OperationValuePortContract> s_Contracts = Build();
        static readonly IReadOnlyDictionary<int, OperationValuePortContract> s_Identities = BuildIdentities();

        static IReadOnlyDictionary<int, OperationValuePortContract> BuildIdentities()
        {
            var values = new Dictionary<int, OperationValuePortContract>();
            foreach (SemanticValueKind kind in Enum.GetValues(typeof(SemanticValueKind)))
                values.Add((int)kind, Both(OrchestrationOperationCode.IdentityValue,
                    new[] { Fixed("m_Value", 0, kind) }, new[] { Fixed("m_Output", 0, kind) }));
            return values;
        }

        public static OperationValuePortContract Require(OrchestrationOperationCode code, int variant = 0)
        {
            if (code == OrchestrationOperationCode.Calculate)
                return GraphCalculationContracts.Require((GraphCalculationKind)variant);
            if (code == OrchestrationOperationCode.IdentityValue)
                return s_Identities[variant];
            if (!s_Contracts.TryGetValue(code, out OperationValuePortContract contract))
                throw new InvalidOperationException($"Operation '{code}' has no Value port contract.");
            return contract;
        }

        public static OperationValuePortContract Require(SemanticOperation operation,
            IReadOnlyList<ProgramGraphCallFrame> graphCallFrames)
        {
            if (operation.Code == OrchestrationOperationCode.Extension)
                return ForExtension(operation.Extension.Value, operation.ConstantOutputs);
            if (operation.Code == OrchestrationOperationCode.EmitOutput)
                return ForOutput(operation.Output.Value);
            if (operation.Code == OrchestrationOperationCode.HostInput)
                return ForHostInputs(operation.HostInputs);
            return Require(operation.Code, operation.Handle, graphCallFrames, operation.Integer0);
        }

        public static OperationValuePortContract ForExtension(OrchestrationExtensionReference reference,
            IReadOnlyList<SemanticConstantOutputBinding> constantOutputs)
        {
            var outputs = new OperationValuePortDefinition[constantOutputs.Count];
            for (int i = 0; i < outputs.Length; i++)
                outputs[i] = Fixed(constantOutputs[i].OutputPortId, i, constantOutputs[i].ValueKind);
            return ForExtension(reference.Kind, reference.Ports.Span, outputs);
        }

        public static OperationValuePortContract ForExtension(OrchestrationExtensionKind kind,
            ReadOnlySpan<OrchestrationExtensionPort> ports, IReadOnlyList<OperationValuePortDefinition> constantOutputs)
        {
            var inputs = new List<OperationValuePortDefinition>();
            var outputs = new List<OperationValuePortDefinition>();
            foreach (OrchestrationExtensionPort port in ports)
            {
                List<OperationValuePortDefinition> target = port.Direction == OrchestrationExtensionPortDirection.Input
                    ? inputs : outputs;
                target.Add(Fixed(port.Id, port.Index, FromOrchestrationKind(port.ValueKind)));
            }
            for (int i = 0; i < constantOutputs.Count; i++)
                outputs.Add(Fixed(constantOutputs[i].Identity, outputs.Count, constantOutputs[i].FixedKind));
            return new OperationValuePortContract(OrchestrationOperationCode.Extension, inputs, outputs,
                kind == OrchestrationExtensionKind.Action);
        }

        public static OperationValuePortContract ForOutput(OrchestrationOutputTemplate output)
        {
            var inputs = new OperationValuePortDefinition[output.Ports.Length];
            for (int i = 0; i < inputs.Length; i++)
                inputs[i] = Fixed(output.Ports.Span[i].Id, i, FromOrchestrationKind(output.Ports.Span[i].ValueKind));
            return Input(OrchestrationOperationCode.EmitOutput, inputs);
        }

        public static OperationValuePortContract ForHostInputs(IReadOnlyList<SemanticHostInputPort> hostInputs)
        {
            var outputs = new OperationValuePortDefinition[hostInputs.Count];
            for (int i = 0; i < outputs.Length; i++)
                outputs[i] = Fixed(hostInputs[i].OutputPortId, i, hostInputs[i].ValueKind);
            return Output(OrchestrationOperationCode.HostInput, outputs);
        }

        public static SemanticValueKind FromOrchestrationKind(OrchestrationValueKind kind) => kind switch
        {
            OrchestrationValueKind.Boolean => SemanticValueKind.Boolean,
            OrchestrationValueKind.Int32 => SemanticValueKind.Int32,
            OrchestrationValueKind.UInt64 => SemanticValueKind.UInt64,
            OrchestrationValueKind.Scalar => SemanticValueKind.Number,
            OrchestrationValueKind.Vector2 => SemanticValueKind.Vector2,
            OrchestrationValueKind.Vector3 => SemanticValueKind.Vector3,
            OrchestrationValueKind.Yaw => SemanticValueKind.Yaw,
            OrchestrationValueKind.Identity => SemanticValueKind.Identity,
            OrchestrationValueKind.Quaternion => SemanticValueKind.Quaternion,
            OrchestrationValueKind.Enum => SemanticValueKind.Enum,
            _ => throw new InvalidOperationException($"Value kind '{kind}' is not an author graph port kind.")
        };

        public static OperationValuePortContract Require(
            OrchestrationOperationCode code,
            OperationHandle operation,
            IReadOnlyList<ProgramGraphCallFrame> graphCallFrames,
            int variant = 0)
        {
            if (code != OrchestrationOperationCode.SubGraph)
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

        public static ProgramStateValueKind ToState(SemanticValueKind kind) => kind switch
        {
            SemanticValueKind.Boolean => ProgramStateValueKind.Boolean,
            SemanticValueKind.Int32 => ProgramStateValueKind.Int32,
            SemanticValueKind.UInt64 => ProgramStateValueKind.UInt64,
            SemanticValueKind.Number => ProgramStateValueKind.Scalar,
            SemanticValueKind.Vector2 => ProgramStateValueKind.Vector2,
            SemanticValueKind.Vector3 => ProgramStateValueKind.Vector3,
            SemanticValueKind.Yaw => ProgramStateValueKind.Yaw,
            SemanticValueKind.Identity => ProgramStateValueKind.Identity,
            SemanticValueKind.Quaternion => ProgramStateValueKind.Quaternion,
            SemanticValueKind.Enum => ProgramStateValueKind.Enum,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

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

		static ReadOnlyDictionary<OrchestrationOperationCode, OperationValuePortContract> Build()
		{
			var values = new Dictionary<OrchestrationOperationCode, OperationValuePortContract>();
			for (int i = 0; i < OrchestrationOperationSet.Operations.Count; i++)
			{
				OrchestrationOperationCode code = OrchestrationOperationSet.Operations[i];
				values.Add(code, Empty(code));
			}

			Set(values, Output(OrchestrationOperationCode.StateRootCompleted, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
            Set(values, Input(OrchestrationOperationCode.Branch, Fixed("Condition", 0, SemanticValueKind.Boolean)));
            Set(values, Output(OrchestrationOperationCode.Cycle, Fixed("Current", 0, SemanticValueKind.Int32)));
            Set(values, Both(OrchestrationOperationCode.SelectValue,
                new[] { Fixed("condition", 0, SemanticValueKind.Boolean), Fixed("whenTrue", 1, SemanticValueKind.Number),
                    Fixed("whenFalse", 2, SemanticValueKind.Number) },
                new[] { Fixed("m_Output", 0, SemanticValueKind.Number) }));
			Set(values, Empty(OrchestrationOperationCode.SubGraph));
			Set(values, Output(OrchestrationOperationCode.StateExitCause, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Input(OrchestrationOperationCode.TimelineClipExitRequest, Fixed("m_When", 0, SemanticValueKind.Boolean)));
			Set(values, Input(OrchestrationOperationCode.TimelineSectionContinueRequest, Fixed("m_When", 0, SemanticValueKind.Boolean)));
            Set(values, Output(OrchestrationOperationCode.TimelineTime, Fixed("m_Output", 0, SemanticValueKind.Number)));
            Set(values, Output(OrchestrationOperationCode.TimelineSection, Fixed("m_Output", 0, SemanticValueKind.Identity)));
            Set(values, Input(OrchestrationOperationCode.Timeline,
                Fixed("m_PlaybackRate", 0, SemanticValueKind.Number),
                Fixed("m_Paused", 1, SemanticValueKind.Boolean),
                Fixed("m_StartTime", 2, SemanticValueKind.Number)));
			Set(values, Output(OrchestrationOperationCode.BlackboardGet, Dynamic("m_Output", 0)));
			Set(values, Input(OrchestrationOperationCode.BlackboardSet, Dynamic("m_Value", 0)));
			Set(values, Output(OrchestrationOperationCode.InputBoolean, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Output(OrchestrationOperationCode.InputScalar, Fixed("m_Output", 0, SemanticValueKind.Number)));
			Set(values, Output(OrchestrationOperationCode.InputVector2, Fixed("m_Output", 0, SemanticValueKind.Vector2)));
			Set(values, Output(OrchestrationOperationCode.InputVector2Magnitude, Fixed("m_Output", 0, SemanticValueKind.Number)));
			Set(values, Output(OrchestrationOperationCode.InputRequest, Fixed("m_Output", 0, SemanticValueKind.Boolean)));
			Set(values, Input(OrchestrationOperationCode.ConditionResult, BooleanLike("m_Result", 0)));
			Set(values, Both(OrchestrationOperationCode.Compare,
				new[] { NumericLike("m_InputValue1", 0, "compare"), NumericLike("m_InputValue2", 1, "compare") },
				new[] { Fixed("m_Result", 0, SemanticValueKind.Boolean) }));
			Set(values, Both(OrchestrationOperationCode.And,
				new[] { BooleanLike("m_Input1", 0), BooleanLike("m_Input2", 1) },
				new[] { Fixed("m_Output", 0, SemanticValueKind.Boolean) }));
			Set(values, Both(OrchestrationOperationCode.Or,
				new[] { BooleanLike("m_Input1", 0), BooleanLike("m_Input2", 1) },
				new[] { Fixed("m_Output", 0, SemanticValueKind.Boolean) }));
			Set(values, Both(OrchestrationOperationCode.Not,
				new[] { BooleanLike("m_Input", 0) },
				new[] { Fixed("m_Output", 0, SemanticValueKind.Boolean) }));
			Set(values, Output(OrchestrationOperationCode.Constant, Dynamic("m_Output", 0)));

			return new ReadOnlyDictionary<OrchestrationOperationCode, OperationValuePortContract>(values);
		}

        static void Set(Dictionary<OrchestrationOperationCode, OperationValuePortContract> values, OperationValuePortContract contract)
        {
            if (!values.ContainsKey(contract.OperationCode))
                throw new InvalidOperationException($"Value port contract targets unknown operation '{contract.OperationCode}'.");
            values[contract.OperationCode] = contract;
        }

        static OperationValuePortContract Empty(OrchestrationOperationCode code) =>
            new OperationValuePortContract(code, Array.Empty<OperationValuePortDefinition>(), Array.Empty<OperationValuePortDefinition>());

        static OperationValuePortContract Input(OrchestrationOperationCode code, params OperationValuePortDefinition[] inputs) =>
            new OperationValuePortContract(code, inputs, Array.Empty<OperationValuePortDefinition>());

        static OperationValuePortContract Output(OrchestrationOperationCode code, params OperationValuePortDefinition[] outputs) =>
            new OperationValuePortContract(code, Array.Empty<OperationValuePortDefinition>(), outputs);

        static OperationValuePortContract Both(OrchestrationOperationCode code, OperationValuePortDefinition[] inputs, OperationValuePortDefinition[] outputs) =>
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
