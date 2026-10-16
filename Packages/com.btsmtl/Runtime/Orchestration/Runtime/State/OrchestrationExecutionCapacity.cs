using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationRootAdmission
    {
        public OrchestrationRootAdmission(ReadOnlyMemory<int> programIndices, int maximumInstances)
        {
            ProgramIndices = programIndices;
            MaximumInstances = maximumInstances;
        }

        public ReadOnlyMemory<int> ProgramIndices { get; }
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

        OrchestrationExecutionCapacity(in OrchestrationExecutionStateCapacity state,
            int playbackCount, ReadOnlyMemory<int>[] ownerInstances,
            OrchestrationProgramOutputCapacity[] outputs)
        {
            State = state;
            PlaybackCount = playbackCount;
            m_OwnerInstances = ownerInstances;
            m_Outputs = outputs;
        }

        public OrchestrationExecutionStateCapacity State { get; }
        public int PlaybackCount { get; }
        public int ProgramCount => m_Outputs.Length;
        public ReadOnlyMemory<int> OwnerInstances(int programIndex) => m_OwnerInstances[programIndex];
        public OrchestrationProgramOutputCapacity Outputs(int programIndex) => m_Outputs[programIndex];

        internal static OrchestrationExecutionCapacity Prepare<TValue>(
            ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<OrchestrationRootAdmission> admissions,
            ReadOnlySpan<OrchestrationTimelineCallCapacity> calls) where TValue : unmanaged
        {
            var callMap = new Dictionary<(int Program, int Operation), OrchestrationTimelineCallCapacity>();
            foreach (ref readonly OrchestrationTimelineCallCapacity call in calls)
                callMap.Add((call.ProgramIndex, call.OperationIndex), call);
            var ownerCalls = new List<OrchestrationTimelineCallCapacity>[programs.Length][];
            var framesPerRoot = new int[programs.Length];
            var playbacksPerRoot = new int[programs.Length];
            var ownerInstancesPerRoot = new int[programs.Length][];
            var ownerInstanceBuffers = new int[programs.Length][];
            var ownerInstances = new ReadOnlyMemory<int>[programs.Length];
            var outputs = new OrchestrationProgramOutputCapacity[programs.Length];
            int operations = 0;
            int values = 0;
            for (int programIndex = 0; programIndex < programs.Length; programIndex++)
            {
                OrchestrationProgram<TValue> program = programs[programIndex];
                operations = Math.Max(operations, program.OperationCount);
                values = Math.Max(values, program.ValueSlotCount);
                ownerCalls[programIndex] = new List<OrchestrationTimelineCallCapacity>[program.OperationCount];
                int outputOperations = 0;
                int maximumOutputPorts = 0;
                for (int operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
                {
                    ref readonly OrchestrationOperation operation = ref program.Operation(operationIndex);
                    if (operation.Code == OrchestrationOperationCode.Timeline)
                    {
                        if (!callMap.TryGetValue((programIndex, operationIndex), out OrchestrationTimelineCallCapacity call))
                            throw new InvalidOperationException($"Program '{programIndex}' Timeline '{operationIndex}' 缺少已完成的生命周期容量生产结果。");
                        int owner = operation.ExecutionFrameOwnerOperationIndex;
                        List<OrchestrationTimelineCallCapacity> owned = ownerCalls[programIndex][owner];
                        if (owned == null)
                            ownerCalls[programIndex][owner] = owned = new List<OrchestrationTimelineCallCapacity>();
                        owned.Add(call);
                    }
                    else if (operation.Code == OrchestrationOperationCode.EmitOutput)
                    {
                        outputOperations++;
                        maximumOutputPorts = Math.Max(maximumOutputPorts,
                            program.OutputTemplate(operation.Integer0).Ports.Length);
                    }
                }
                outputs[programIndex] = new OrchestrationProgramOutputCapacity(outputOperations,
                    outputOperations == 0 ? 0 : program.MaximumOperationEvaluations,
                    checked(program.MaximumOperationEvaluations * maximumOutputPorts));
                CountOwner(program.RootOperationIndex, ownerCalls[programIndex],
                    out framesPerRoot[programIndex], out playbacksPerRoot[programIndex]);
                ownerInstancesPerRoot[programIndex] = new int[program.OperationCount];
                AccumulateOwners(program.RootOperationIndex, 1, ownerCalls[programIndex],
                    ownerInstancesPerRoot[programIndex]);
                ownerInstances[programIndex] = ownerInstanceBuffers[programIndex] = new int[program.OperationCount];
            }
            int frameCount = 0;
            int playbackCount = 0;
            foreach (ref readonly OrchestrationRootAdmission admission in admissions)
            {
                int frames = 0;
                int playbacks = 0;
                foreach (int programIndex in admission.ProgramIndices.Span)
                {
                    frames = Math.Max(frames, framesPerRoot[programIndex]);
                    playbacks = Math.Max(playbacks, playbacksPerRoot[programIndex]);
                    Span<int> instances = ownerInstanceBuffers[programIndex];
                    ReadOnlySpan<int> perRoot = ownerInstancesPerRoot[programIndex];
                    for (int owner = 0; owner < instances.Length; owner++)
                        instances[owner] = checked(instances[owner] +
                            checked(admission.MaximumInstances * perRoot[owner]));
                }
                frameCount = checked(frameCount + checked(admission.MaximumInstances * frames));
                playbackCount = checked(playbackCount + checked(admission.MaximumInstances * playbacks));
            }
            for (int programIndex = 0; programIndex < programs.Length; programIndex++)
                if (ownerInstanceBuffers[programIndex][programs[programIndex].RootOperationIndex] == 0)
                    throw new InvalidOperationException($"Program '{programIndex}' 缺少实际根实例准入。");
            var state = new OrchestrationExecutionStateCapacity(frameCount, operations, values);
            return new OrchestrationExecutionCapacity(in state, playbackCount, ownerInstances, outputs);
        }

        static void CountOwner(int owner, List<OrchestrationTimelineCallCapacity>[] calls,
            out int frames, out int playbacks)
        {
            frames = 1;
            playbacks = 0;
            List<OrchestrationTimelineCallCapacity> owned = calls[owner];
            if (owned == null)
                return;
            foreach (OrchestrationTimelineCallCapacity call in owned)
            {
                playbacks = checked(playbacks + 1);
                foreach (ref readonly OrchestrationOccurrenceCapacity occurrence in call.Occurrences.Span)
                {
                    CountOwner(occurrence.OwnerOperationIndex, calls, out int childFrames, out int childPlaybacks);
                    frames = checked(frames + checked(occurrence.MaximumInstances * childFrames));
                    playbacks = checked(playbacks + checked(occurrence.MaximumInstances * childPlaybacks));
                }
            }
        }

        static void AccumulateOwners(int owner, int count,
            List<OrchestrationTimelineCallCapacity>[] calls, Span<int> instances)
        {
            instances[owner] = checked(instances[owner] + count);
            List<OrchestrationTimelineCallCapacity> owned = calls[owner];
            if (owned == null)
                return;
            foreach (OrchestrationTimelineCallCapacity call in owned)
                foreach (ref readonly OrchestrationOccurrenceCapacity occurrence in call.Occurrences.Span)
                    AccumulateOwners(occurrence.OwnerOperationIndex,
                        checked(count * occurrence.MaximumInstances), calls, instances);
        }
    }
}
