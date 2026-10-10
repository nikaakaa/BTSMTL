using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class Float32GraphValueLayout
    {
        readonly OperationValueInputRange[] m_ValueInputRanges;
        readonly CompiledValueInputBinding[] m_ValueInputs;
        readonly int[] m_ValueEvaluationSlotOffsets;
        readonly int[] m_GraphOutputStateSlots;
        readonly int[] m_InputPortOffsets;

        internal Float32GraphValueLayout(Float32GraphExecutionData data)
        {
            BuildValueInputs(data.Operations, data.Constants, data.ConstantInputBindings, data.ControlFlow,
                data.References, data.StateSlots, data.GraphCallFrames, out m_ValueInputRanges, out m_ValueInputs,
                out m_ValueEvaluationSlotOffsets, out m_GraphOutputStateSlots);
            m_InputPortOffsets = new int[data.Operations.Count + 1];
            for (int i = 0; i < data.Operations.Count; i++)
            {
                SimulationOperation operation = data.Operations[i];
                int count = GameplayAbilityValuePortContracts.Require(operation.Code, operation.Handle,
                    data.GraphCallFrames, operation.Integer0).Inputs.Count;
                m_InputPortOffsets[i + 1] = m_InputPortOffsets[i] + count;
            }
        }

        public int PortSlotCount => m_GraphOutputStateSlots.Length + m_InputPortOffsets[m_InputPortOffsets.Length - 1];
        public int InputPortSlot(OperationHandle operation, int portIndex) =>
            m_GraphOutputStateSlots.Length + m_InputPortOffsets[operation.Value] + portIndex;
        public int OutputPortSlot(OperationHandle operation, int portIndex) =>
            m_ValueEvaluationSlotOffsets[operation.Value] + portIndex + 1;

        public ReadOnlySpan<CompiledValueInputBinding> ValueInputs(OperationHandle operation)
        {
            OperationValueInputRange range = m_ValueInputRanges[operation.Value];
            return new ReadOnlySpan<CompiledValueInputBinding>(m_ValueInputs, range.Offset, range.Count);
        }

        internal int ValueEvaluationSlotCount => m_GraphOutputStateSlots.Length;

        internal int ValueEvaluationSlot(OperationHandle operation, int outputPortIndex) =>
            m_ValueEvaluationSlotOffsets[operation.Value] + outputPortIndex + 1;

        internal int GraphOutputStateSlot(int evaluationSlot) => m_GraphOutputStateSlots[evaluationSlot];

        static void BuildValueInputs(
            IReadOnlyList<SimulationOperation> operations,
            IReadOnlyList<ProgramConstant> constants,
            IReadOnlyList<ProgramConstantInputBinding> constantInputBindings,
            IReadOnlyList<ProgramControlFlowEdge> controlFlow,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            IReadOnlyList<ProgramGraphCallFrame> graphCallFrames,
            out OperationValueInputRange[] ranges,
            out CompiledValueInputBinding[] bindings,
            out int[] evaluationSlotOffsets,
            out int[] graphOutputStateSlots)
        {
            int operationCount = operations.Count;
            var grouped = new CompiledValueInputBinding?[operationCount][];
            evaluationSlotOffsets = new int[operationCount + 1];
            for (int i = 0; i < operationCount; i++)
            {
                OperationValuePortContract contract = GameplayAbilityValuePortContracts.Require(
                    operations[i].Code,
                    operations[i].Handle,
                    graphCallFrames, operations[i].Integer0);
                grouped[i] = new CompiledValueInputBinding?[contract.Inputs.Count];
                evaluationSlotOffsets[i + 1] = checked(evaluationSlotOffsets[i] + contract.Outputs.Count + 1);
            }
            graphOutputStateSlots = new int[evaluationSlotOffsets[operationCount]];
            for (int i = 0; i < graphOutputStateSlots.Length; i++)
                graphOutputStateSlots[i] = -1;
            for (int i = 0; i < graphCallFrames.Count; i++)
            {
                ProgramGraphCallFrame frame = graphCallFrames[i];
                int offset = evaluationSlotOffsets[frame.OwnerOperation.Value] + 1;
                for (int output = 0; output < frame.Outputs.Count; output++)
                    graphOutputStateSlots[offset + output] = frame.Outputs[output].StateSlot;
            }
            for (int i = 0; i < controlFlow.Count; i++)
            {
                ProgramControlFlowEdge edge = controlFlow[i];
                if (edge.Kind != ProgramControlFlowKind.Value)
                    continue;
                SimulationOperation source = operations[edge.Source.Value];
                SimulationOperation target = operations[edge.Target.Value];
                OperationValuePortDefinition sourcePort = GameplayAbilityValuePortContracts
                    .Require(source.Code, source.Handle, graphCallFrames, source.Integer0)
                    .RequireSelection(edge.SourcePort);
                OperationValuePortDefinition targetPort = GameplayAbilityValuePortContracts
                    .Require(target.Code, target.Handle, graphCallFrames, target.Integer0)
                    .RequireInput(edge.TargetPort);
                SemanticValueKind kind = GameplayAbilityExecutionValueResolver.ResolveOutputKind(
                    source,
                    sourcePort,
                    constants,
                    references,
                    stateSlots);
                GameplayAbilityExecutionValueResolver.RequireInputKind(
                    target,
                    targetPort,
                    kind,
                    references,
                    stateSlots);
                AddValueInput(
                    grouped[target.Handle.Value],
                    targetPort.Order,
                    new CompiledValueInputBinding(
                        targetPort.Order,
                        kind,
                        CompiledValueInputSourceKind.Operation,
                        source.Handle,
                        sourcePort.Order,
                        sourcePort.Identity,
                        -1),
                    target);
            }
            for (int i = 0; i < constantInputBindings.Count; i++)
            {
                ProgramConstantInputBinding input = constantInputBindings[i];
                SimulationOperation target = operations[input.TargetOperation.Value];
                OperationValuePortDefinition targetPort = GameplayAbilityValuePortContracts
                    .Require(target.Code, target.Handle, graphCallFrames, target.Integer0)
                    .RequireInput(input.TargetPort);
                GameplayAbilityExecutionValueResolver.RequireInputKind(
                    target,
                    targetPort,
                    input.ResolvedValueKind,
                    references,
                    stateSlots);
                AddValueInput(
                    grouped[target.Handle.Value],
                    targetPort.Order,
                    new CompiledValueInputBinding(
                        targetPort.Order,
                        input.ResolvedValueKind,
                        CompiledValueInputSourceKind.Constant,
                        OperationHandle.Invalid,
                        -1,
                        null,
                        input.ConstantIndex),
                    target);
            }
            ranges = new OperationValueInputRange[operationCount];
            var flattened = new List<CompiledValueInputBinding>();
            for (int i = 0; i < operationCount; i++)
            {
                int offset = flattened.Count;
                for (int port = 0; port < grouped[i].Length; port++)
                {
                    if (grouped[i][port].HasValue)
                        flattened.Add(grouped[i][port].Value);
                }
                ranges[i] = new OperationValueInputRange(offset, flattened.Count - offset);
            }
            bindings = flattened.ToArray();
        }

        static void AddValueInput(
            CompiledValueInputBinding?[] inputs,
            int portIndex,
            CompiledValueInputBinding binding,
            SimulationOperation target)
        {
            if (portIndex < 0 || portIndex >= inputs.Length)
                throw new InvalidOperationException($"Operation '{target.Handle}' Value input port index '{portIndex}' is invalid.");
            if (inputs[portIndex].HasValue)
                throw new InvalidOperationException($"Operation '{target.Handle}' Value input port index '{portIndex}' has multiple sources.");
            inputs[portIndex] = binding;
        }

    }
}
