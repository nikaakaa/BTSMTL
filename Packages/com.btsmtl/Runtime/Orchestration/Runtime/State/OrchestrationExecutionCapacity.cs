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
        public OrchestrationOccurrenceCapacity(int programIndex, int ownerOperationIndex, int maximumInstances)
        {
            ProgramIndex = programIndex;
            OwnerOperationIndex = ownerOperationIndex;
            MaximumInstances = maximumInstances;
        }

        public int ProgramIndex { get; }
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
        internal OrchestrationProgramOutputCapacity(int requestCount, int valueCount)
        {
            RequestCount = requestCount;
            ValueCount = valueCount;
        }

        public int RequestCount { get; }
        public int ValueCount { get; }
    }

    public sealed class OrchestrationExecutionCapacity
    {
        readonly ReadOnlyMemory<int>[] m_OwnerInstances;
        readonly int[] m_SourceDepths;

        OrchestrationExecutionCapacity(in OrchestrationExecutionStateCapacity state,
            int playbackCount, ReadOnlyMemory<int>[] ownerInstances,
            int[] sourceDepths)
        {
            State = state;
            PlaybackCount = playbackCount;
            m_OwnerInstances = ownerInstances;
            m_SourceDepths = sourceDepths;
        }

        public OrchestrationExecutionStateCapacity State { get; }
        public int PlaybackCount { get; }
        public int ProgramCount => m_OwnerInstances.Length;
        public ReadOnlyMemory<int> OwnerInstances(int programIndex) => m_OwnerInstances[programIndex];
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
            var roots = new Dictionary<(int Program, int Owner), (int Frames, int Playbacks, int[][] Owners, int Depth)>();
            var ownerInstanceBuffers = new int[programs.Length][];
            var ownerInstances = new ReadOnlyMemory<int>[programs.Length];
            var sourceDepths = new int[programs.Length];
            int operations = 0;
            int values = 0;
            int savedOutputs = 0;
            var valueKindCounts = new int[(int)OrchestrationValueKind.SpatialIdentity + 1];
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
                        var counts = new int[programs.Length][];
                        for (int target = 0; target < counts.Length; target++)
                            counts[target] = new int[programs[target].OperationCount];
                        int rootPlaybacks = 0;
                        int depth = AccumulateOwners(programIndex, root.OwnerOperationIndex, 1,
                            ownerCalls, callMap, counts, new HashSet<(int Program, int Owner)>(), ref rootPlaybacks);
                        int rootFrames = 0;
                        foreach (int[] owners in counts)
                            foreach (int count in owners)
                                rootFrames = checked(rootFrames + count);
                        roots.Add(key, capacity = (rootFrames, rootPlaybacks, counts, depth));
                    }
                    frames = Math.Max(frames, capacity.Frames);
                    playbacks = Math.Max(playbacks, capacity.Playbacks);
                    for (int target = 0; target < capacity.Owners.Length; target++)
                    {
                        if (!admissionOwners.TryGetValue(target, out int[] perAdmission))
                            admissionOwners.Add(target, perAdmission = new int[programs[target].OperationCount]);
                        for (int owner = 0; owner < perAdmission.Length; owner++)
                        {
                            int count = capacity.Owners[target][owner];
                            if (count != 0)
                                sourceDepths[target] = Math.Max(sourceDepths[target], capacity.Depth);
                            perAdmission[owner] = Math.Max(perAdmission[owner], count);
                        }
                    }
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
                ReadOnlySpan<int> instances = ownerInstances[programIndex].Span;
                ReadOnlySpan<OrchestrationExecutionOwnerStorage> storages = programs[programIndex].ExecutionStorage.Owners.Span;
                for (int owner = 0; owner < instances.Length; owner++)
                {
                    if (instances[owner] == 0)
                        continue;
                    ref readonly OrchestrationExecutionOwnerStorage storage = ref storages[owner];
                    operations = Math.Max(operations, storage.OperationCount);
                    values = Math.Max(values, storage.BlackboardCount);
                    savedOutputs = Math.Max(savedOutputs, storage.SavedOutputCount);
                    for (int kind = 0; kind < valueKindCounts.Length; kind++)
                        valueKindCounts[kind] = Math.Max(valueKindCounts[kind], storage.ValueKindCounts.Span[kind]);
                }
            }
            var state = new OrchestrationExecutionStateCapacity(frameCount, operations, values,
                savedOutputs, valueKindCounts);
            return new OrchestrationExecutionCapacity(in state, playbackCount, ownerInstances, sourceDepths);
        }

        static int AccumulateOwners(int programIndex, int owner, int count,
            List<int>[][] ownerCalls,
            Dictionary<(int Program, int Operation), OrchestrationTimelineCallCapacity> calls,
            int[][] instances, HashSet<(int Program, int Owner)> active, ref int playbacks)
        {
            var key = (programIndex, owner);
            if (!active.Add(key))
                throw new InvalidOperationException($"Program '{programIndex}' 的执行帧 '{owner}' 存在没有有限容量的 Timeline 调用循环。");
            instances[programIndex][owner] = checked(instances[programIndex][owner] + count);
            List<int> owned = ownerCalls[programIndex][owner];
            int depth = 1;
            if (owned != null)
                foreach (int operationIndex in owned)
                {
                    if (!calls.TryGetValue((programIndex, operationIndex), out OrchestrationTimelineCallCapacity call))
                        throw new InvalidOperationException($"Program '{programIndex}' Timeline '{operationIndex}' 缺少已完成的生命周期容量生产结果。");
                    playbacks = checked(playbacks + count);
                    foreach (ref readonly OrchestrationOccurrenceCapacity occurrence in call.Occurrences.Span)
                        if (occurrence.MaximumInstances != 0)
                            depth = Math.Max(depth, checked(1 + AccumulateOwners(occurrence.ProgramIndex,
                                occurrence.OwnerOperationIndex, checked(count * occurrence.MaximumInstances),
                                ownerCalls, calls, instances, active, ref playbacks)));
                }
            active.Remove(key);
            return depth;
        }
    }
}
