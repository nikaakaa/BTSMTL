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
            var roots = new Dictionary<(int Program, int Owner),
                (int Playbacks, int[][] Owners, int Depth, List<FramePoolBuilder> Pools)>();
            var ownerInstanceBuffers = new int[programs.Length][];
            var ownerInstances = new ReadOnlyMemory<int>[programs.Length];
            var sourceDepths = new int[programs.Length];
            var framePools = new List<FramePoolBuilder>();
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
            int playbackCount = 0;
            foreach (ref readonly OrchestrationRootAdmission admission in admissions)
            {
                if (admission.MaximumInstances == 0)
                    continue;
                int playbacks = 0;
                var admissionPools = new List<FramePoolBuilder>();
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
                        var rootPools = new List<FramePoolBuilder>();
                        int depth = AccumulateOwners(programs, programIndex, root.OwnerOperationIndex, 1,
                            ownerCalls, callMap, counts, new HashSet<(int Program, int Owner)>(),
                            rootPools, 0, ref rootPlaybacks);
                        roots.Add(key, capacity = (rootPlaybacks, counts, depth, rootPools));
                    }
                    playbacks = Math.Max(playbacks, capacity.Playbacks);
                    for (int depth = 0; depth < capacity.Pools.Count; depth++)
                        Pool(admissionPools, depth).MergeMaximum(capacity.Pools[depth]);
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
                for (int depth = 0; depth < admissionPools.Count; depth++)
                    Pool(framePools, depth).AddAdmission(admissionPools[depth], admission.MaximumInstances);
                playbackCount = checked(playbackCount + checked(admission.MaximumInstances * playbacks));
            }
            var pools = new OrchestrationExecutionFramePoolCapacity[framePools.Count];
            for (int depth = 0; depth < pools.Length; depth++)
                pools[depth] = framePools[depth].Build();
            var state = new OrchestrationExecutionStateCapacity(pools);
            return new OrchestrationExecutionCapacity(in state, playbackCount, ownerInstances, sourceDepths);
        }

        static int AccumulateOwners<TValue>(ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            int programIndex, int owner, int count,
            List<int>[][] ownerCalls,
            Dictionary<(int Program, int Operation), OrchestrationTimelineCallCapacity> calls,
            int[][] instances, HashSet<(int Program, int Owner)> active,
            List<FramePoolBuilder> pools, int level, ref int playbacks) where TValue : unmanaged
        {
            var key = (programIndex, owner);
            if (!active.Add(key))
                throw new InvalidOperationException($"Program '{programIndex}' 的执行帧 '{owner}' 存在没有有限容量的 Timeline 调用循环。");
            instances[programIndex][owner] = checked(instances[programIndex][owner] + count);
            Pool(pools, level).AddOwner(count, programs[programIndex].ExecutionStorage.Owners.Span[owner]);
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
                            depth = Math.Max(depth, checked(1 + AccumulateOwners(programs, occurrence.ProgramIndex,
                                occurrence.OwnerOperationIndex, checked(count * occurrence.MaximumInstances),
                                ownerCalls, calls, instances, active, pools, level + 1, ref playbacks)));
                }
            active.Remove(key);
            return depth;
        }

        static FramePoolBuilder Pool(List<FramePoolBuilder> pools, int depth)
        {
            while (pools.Count <= depth)
                pools.Add(new FramePoolBuilder());
            return pools[depth];
        }

        sealed class FramePoolBuilder
        {
            int m_Count;
            int m_Operations;
            int m_Values;
            int m_Outputs;
            readonly int[] m_Kinds = new int[(int)OrchestrationValueKind.SpatialIdentity + 1];

            internal void AddOwner(int count, in OrchestrationExecutionOwnerStorage storage)
            {
                m_Count = checked(m_Count + count);
                m_Operations = Math.Max(m_Operations, storage.OperationCount);
                m_Values = Math.Max(m_Values, storage.BlackboardCount);
                m_Outputs = Math.Max(m_Outputs, storage.SavedOutputCount);
                for (int kind = 0; kind < m_Kinds.Length; kind++)
                    m_Kinds[kind] = Math.Max(m_Kinds[kind], storage.ValueKindCounts.Span[kind]);
            }

            internal void MergeMaximum(FramePoolBuilder other)
            {
                m_Count = Math.Max(m_Count, other.m_Count);
                MergeWidths(other);
            }

            internal void AddAdmission(FramePoolBuilder other, int instances)
            {
                m_Count = checked(m_Count + other.m_Count * instances);
                MergeWidths(other);
            }

            void MergeWidths(FramePoolBuilder other)
            {
                m_Operations = Math.Max(m_Operations, other.m_Operations);
                m_Values = Math.Max(m_Values, other.m_Values);
                m_Outputs = Math.Max(m_Outputs, other.m_Outputs);
                for (int kind = 0; kind < m_Kinds.Length; kind++)
                    m_Kinds[kind] = Math.Max(m_Kinds[kind], other.m_Kinds[kind]);
            }

            internal OrchestrationExecutionFramePoolCapacity Build() =>
                new OrchestrationExecutionFramePoolCapacity(m_Count, m_Operations, m_Values, m_Outputs, m_Kinds);
        }
    }
}
