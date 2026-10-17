using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExecutionRoot
    {
        public OrchestrationExecutionRoot(int programIndex, int ownerOperationIndex)
        {
            ProgramIndex = programIndex;
            OwnerOperationIndex = ownerOperationIndex;
        }

        public int ProgramIndex { get; }
        public int OwnerOperationIndex { get; }
    }

    public readonly struct OrchestrationRootAdmission
    {
        public OrchestrationRootAdmission(ReadOnlyMemory<OrchestrationExecutionRoot> roots, int maximumInstances)
        {
            Roots = roots;
            MaximumInstances = maximumInstances;
        }

        public ReadOnlyMemory<OrchestrationExecutionRoot> Roots { get; }
        public int MaximumInstances { get; }
    }

    public readonly struct OrchestrationOccurrenceCapacity
    {
        public OrchestrationOccurrenceCapacity(int ownerOperationIndex, int maximumInstances)
        {
            OwnerOperationIndex = ownerOperationIndex;
            MaximumInstances = maximumInstances;
        }

        public int OwnerOperationIndex { get; }
        public int MaximumInstances { get; }
    }

    public readonly struct OrchestrationTimelineCallCapacity
    {
        public OrchestrationTimelineCallCapacity(int programIndex, int operationIndex,
            ReadOnlyMemory<OrchestrationOccurrenceCapacity> occurrences)
        {
            ProgramIndex = programIndex;
            OperationIndex = operationIndex;
            Occurrences = occurrences;
        }

        public int ProgramIndex { get; }
        public int OperationIndex { get; }
        public ReadOnlyMemory<OrchestrationOccurrenceCapacity> Occurrences { get; }
    }

    public readonly struct OrchestrationProgramOutputCapacity
    {
        internal OrchestrationProgramOutputCapacity(int graphOutputOperations, int requestCount, int valueCount)
        {
            GraphOutputOperations = graphOutputOperations;
            RequestCount = requestCount;
            ValueCount = valueCount;
        }

        public int GraphOutputOperations { get; }
        public int RequestCount { get; }
        public int ValueCount { get; }
    }

    public sealed class OrchestrationExecutionCapacity
    {
        readonly ReadOnlyMemory<int>[] m_OwnerInstances;
        readonly OrchestrationProgramOutputCapacity[] m_Outputs;
        readonly int[] m_SourceDepths;

        OrchestrationExecutionCapacity(in OrchestrationExecutionStateCapacity state,
            int playbackCount, ReadOnlyMemory<int>[] ownerInstances,
            OrchestrationProgramOutputCapacity[] outputs, int[] sourceDepths)
        {
            State = state;
            PlaybackCount = playbackCount;
            m_OwnerInstances = ownerInstances;
            m_Outputs = outputs;
            m_SourceDepths = sourceDepths;
        }

        public OrchestrationExecutionStateCapacity State { get; }
        public int PlaybackCount { get; }
        public int ProgramCount => m_Outputs.Length;
        public ReadOnlyMemory<int> OwnerInstances(int programIndex) => m_OwnerInstances[programIndex];
        public OrchestrationProgramOutputCapacity Outputs(int programIndex) => m_Outputs[programIndex];
        public int SourceDepth(int programIndex) => m_SourceDepths[programIndex];

        internal static OrchestrationExecutionCapacity Prepare<TValue>(
            ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<OrchestrationRootAdmission> admissions,
            ReadOnlySpan<OrchestrationTimelineCallCapacity> calls) where TValue : unmanaged
        {
            var callMap = new Dictionary<(int Program, int Operation), OrchestrationTimelineCallCapacity>();
            foreach (ref readonly OrchestrationTimelineCallCapacity call in calls)
                callMap.Add((call.ProgramIndex, call.OperationIndex), call);
            var ownerCalls = new List<int>[programs.Length][];
            var roots = new Dictionary<(int Program, int Owner), (int Frames, int Playbacks, int[] Owners, int Depth)>();
            var ownerInstanceBuffers = new int[programs.Length][];
            var ownerInstances = new ReadOnlyMemory<int>[programs.Length];
            var outputs = new OrchestrationProgramOutputCapacity[programs.Length];
            var sourceDepths = new int[programs.Length];
            int operations = 0;
            int values = 0;
            for (int programIndex = 0; programIndex < programs.Length; programIndex++)
            {
                OrchestrationProgram<TValue> program = programs[programIndex];
                ownerCalls[programIndex] = new List<int>[program.OperationCount];
                for (int operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
                {
                    ref readonly OrchestrationOperation operation = ref program.Operation(operationIndex);
                    if (operation.Code == OrchestrationOperationCode.Timeline)
                    {
                        int owner = operation.ExecutionFrameOwnerOperationIndex;
                        List<int> owned = ownerCalls[programIndex][owner];
                        if (owned == null)
                            ownerCalls[programIndex][owner] = owned = new List<int>();
                        owned.Add(operationIndex);
                    }
                }
                ownerInstances[programIndex] = ownerInstanceBuffers[programIndex] = new int[program.OperationCount];
            }
            int frameCount = 0;
            int playbackCount = 0;
            foreach (ref readonly OrchestrationRootAdmission admission in admissions)
            {
                if (admission.MaximumInstances == 0)
                    continue;
                int frames = 0;
                int playbacks = 0;
                var admissionOwners = new Dictionary<int, int[]>();
                foreach (ref readonly OrchestrationExecutionRoot root in admission.Roots.Span)
                {
                    int programIndex = root.ProgramIndex;
                    OrchestrationProgram<TValue> program = programs[programIndex];
                    var key = (programIndex, root.OwnerOperationIndex);
                    if (!roots.TryGetValue(key, out var capacity))
                    {
                        if (program.Operation(root.OwnerOperationIndex).ExecutionFrameOwnerOperationIndex != root.OwnerOperationIndex)
                            throw new InvalidOperationException($"Program '{programIndex}' admission '{root.OwnerOperationIndex}' is not an execution frame owner.");
                        var counts = new int[program.OperationCount];
                        int rootPlaybacks = 0;
                        int depth = AccumulateOwners(programIndex, root.OwnerOperationIndex, 1,
                            ownerCalls[programIndex], callMap, counts, ref rootPlaybacks);
                        int rootFrames = 0;
                        foreach (int count in counts)
                            rootFrames = checked(rootFrames + count);
                        roots.Add(key, capacity = (rootFrames, rootPlaybacks, counts, depth));
                    }
                    sourceDepths[programIndex] = Math.Max(sourceDepths[programIndex], capacity.Depth);
                    frames = Math.Max(frames, capacity.Frames);
                    playbacks = Math.Max(playbacks, capacity.Playbacks);
                    if (!admissionOwners.TryGetValue(programIndex, out int[] perAdmission))
                        admissionOwners.Add(programIndex, perAdmission = new int[program.OperationCount]);
                    for (int owner = 0; owner < perAdmission.Length; owner++)
                        perAdmission[owner] = Math.Max(perAdmission[owner], capacity.Owners[owner]);
                    operations = Math.Max(operations, program.OperationCount);
                    values = Math.Max(values, program.ValueSlotCount);
                }
                foreach (KeyValuePair<int, int[]> item in admissionOwners)
                {
                    Span<int> instances = ownerInstanceBuffers[item.Key];
                    for (int owner = 0; owner < instances.Length; owner++)
                        instances[owner] = checked(instances[owner] +
                            checked(admission.MaximumInstances * item.Value[owner]));
                }
                frameCount = checked(frameCount + checked(admission.MaximumInstances * frames));
                playbackCount = checked(playbackCount + checked(admission.MaximumInstances * playbacks));
            }
            for (int programIndex = 0; programIndex < programs.Length; programIndex++)
            {
                OrchestrationProgram<TValue> program = programs[programIndex];
                int outputOperations = 0;
                int maximumOutputPorts = 0;
                for (int operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
                {
                    ref readonly OrchestrationOperation operation = ref program.Operation(operationIndex);
                    if (operation.Code == OrchestrationOperationCode.EmitOutput &&
                        ownerInstanceBuffers[programIndex][operation.ExecutionFrameOwnerOperationIndex] != 0)
                    {
                        outputOperations++;
                        maximumOutputPorts = Math.Max(maximumOutputPorts,
                            program.OutputTemplate(operation.Integer0).Ports.Length);
                    }
                }
                outputs[programIndex] = new OrchestrationProgramOutputCapacity(outputOperations,
                    outputOperations == 0 ? 0 : program.MaximumOperationEvaluations,
                    checked(program.MaximumOperationEvaluations * maximumOutputPorts));
            }
            var state = new OrchestrationExecutionStateCapacity(frameCount, operations, values);
            return new OrchestrationExecutionCapacity(in state, playbackCount, ownerInstances, outputs, sourceDepths);
        }

        static int AccumulateOwners(int programIndex, int owner, int count,
            List<int>[] ownerCalls,
            Dictionary<(int Program, int Operation), OrchestrationTimelineCallCapacity> calls,
            Span<int> instances, ref int playbacks)
        {
            instances[owner] = checked(instances[owner] + count);
            List<int> owned = ownerCalls[owner];
            if (owned == null)
                return 1;
            int depth = 1;
            foreach (int operationIndex in owned)
            {
                if (!calls.TryGetValue((programIndex, operationIndex), out OrchestrationTimelineCallCapacity call))
                    throw new InvalidOperationException($"Program '{programIndex}' Timeline '{operationIndex}' 缺少已完成的生命周期容量生产结果。");
                playbacks = checked(playbacks + count);
                foreach (ref readonly OrchestrationOccurrenceCapacity occurrence in call.Occurrences.Span)
                    if (occurrence.MaximumInstances != 0)
                        depth = Math.Max(depth, checked(1 + AccumulateOwners(programIndex,
                            occurrence.OwnerOperationIndex, checked(count * occurrence.MaximumInstances),
                            ownerCalls, calls, instances, ref playbacks)));
            }
            return depth;
        }
    }
}
