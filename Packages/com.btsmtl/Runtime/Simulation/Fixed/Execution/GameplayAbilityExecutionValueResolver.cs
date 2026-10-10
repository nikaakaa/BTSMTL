using System;
using System.Collections.Generic;

using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public static class GameplayAbilityExecutionValueResolver
    {
        public static SemanticValueKind ResolveLinkedSourceKind(
            IReadOnlyList<SimulationOperation> operations,
            IReadOnlyList<ProgramGraphCallFrame> graphCallFrames,
            IReadOnlyList<ProgramConstant> constants,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            ProgramControlFlowEdge edge)
        {
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));
            if (edge == null || edge.Kind != ProgramControlFlowKind.Value)
                throw new ArgumentException("A Value control-flow edge is required.", nameof(edge));
            if (edge.Source.Value < 0 || edge.Source.Value >= operations.Count ||
                edge.Target.Value < 0 || edge.Target.Value >= operations.Count)
            {
                throw new InvalidOperationException($"Value edge '{edge.Identity}' references an operation outside the Ability execution data.");
            }
            SimulationOperation source = operations[edge.Source.Value];
            OperationValuePortDefinition sourcePort = GameplayAbilityValuePortContracts
                .Require(source.Code, source.Handle, graphCallFrames)
                .RequireSelection(edge.SourcePort);
            return ResolveOutputKind(source, sourcePort, constants, references, stateSlots);
        }

        public static SemanticValueKind ResolveOutputKind(
            SimulationOperation operation,
            OperationValuePortDefinition port,
            IReadOnlyList<ProgramConstant> constants,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramStateSlot> stateSlots)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            if (port == null)
                throw new ArgumentNullException(nameof(port));
            if (port.Constraint == OperationValuePortConstraint.Fixed)
                return port.FixedKind;
            if (operation.Code == SimulationOperationCode.BlackboardGet)
            {
                return GameplayAbilityValuePortContracts.FromState(
                    stateSlots[RequireStateReference(references, operation)].ValueKind);
            }
            if (operation.Code == SimulationOperationCode.CharacterStateRead)
                return CharacterStateProviderFields.ValueKind(operation.Text0);
            if (operation.Code == SimulationOperationCode.Constant && operation.ConstantReferences.Count > 0)
                return FromConstant(constants[operation.ConstantReferences[0]].Kind);
            throw new InvalidOperationException($"Operation '{operation.Handle}' output '{port.Identity}' has no concrete Value kind.");
        }

        public static void RequireInputKind(
            SimulationOperation operation,
            OperationValuePortDefinition port,
            SemanticValueKind kind,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramStateSlot> stateSlots)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            if (port == null)
                throw new ArgumentNullException(nameof(port));
            SemanticValueKind expected = port.Constraint == OperationValuePortConstraint.Dynamic
                ? operation.Code == SimulationOperationCode.CharacterStateRead
                    ? CharacterStateProviderFields.ValueKind(operation.Text0)
                    : GameplayAbilityValuePortContracts.FromState(
                        stateSlots[RequireStateReference(references, operation)].ValueKind)
                : port.Resolve(kind);
            if (!port.Accepts(kind) ||
                port.Constraint == OperationValuePortConstraint.Dynamic && expected != kind)
            {
                throw new InvalidOperationException($"Operation '{operation.Handle}' input '{port.Identity}' cannot accept Value kind '{kind}'.");
            }
        }

        static int RequireStateReference(
            IReadOnlyList<ProgramReference> references,
            SimulationOperation operation)
        {
            if (references == null)
                throw new ArgumentNullException(nameof(references));
            int found = -1;
            for (int i = 0; i < references.Count; i++)
            {
                ProgramReference reference = references[i];
                if (reference.Kind != ProgramReferenceKind.StateSlot ||
                    !reference.SourceOperation.Equals(operation.Handle))
                    continue;
                if (found >= 0)
                    throw new InvalidOperationException($"Operation '{operation.Handle}' has multiple state-slot references for Value resolution.");
                found = reference.TargetIndex;
            }
            if (found < 0)
                throw new InvalidOperationException($"Operation '{operation.Handle}' has no state-slot reference for Value resolution.");
            return found;
        }

        static SemanticValueKind FromConstant(ProgramConstantKind kind)
        {
            return kind switch
            {
                ProgramConstantKind.Boolean => SemanticValueKind.Boolean,
                ProgramConstantKind.Int32 => SemanticValueKind.Int32,
                ProgramConstantKind.UInt64 => SemanticValueKind.UInt64,
                ProgramConstantKind.Scalar => SemanticValueKind.Number,
                ProgramConstantKind.Vector2 => SemanticValueKind.Vector2,
                ProgramConstantKind.Vector3 => SemanticValueKind.Vector3,
                ProgramConstantKind.Yaw => SemanticValueKind.Yaw,
                ProgramConstantKind.String => SemanticValueKind.Identity,
                _ => throw new InvalidOperationException($"Program constant kind '{kind}' cannot enter the Value graph.")
            };
        }
    }
}

