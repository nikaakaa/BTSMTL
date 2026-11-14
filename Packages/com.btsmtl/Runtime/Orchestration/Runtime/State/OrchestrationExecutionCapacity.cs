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
        readonly OwnerAdmission[] m_OwnerAdmissions;

        OrchestrationExecutionCapacity(in OrchestrationExecutionStateCapacity state,
            int playbackCount, ReadOnlyMemory<int>[] ownerInstances,
            int[] sourceDepths, OwnerAdmission[] ownerAdmissions)
        {
            State = state;
            PlaybackCount = playbackCount;
            m_OwnerInstances = ownerInstances;
            m_SourceDepths = sourceDepths;
            m_OwnerAdmissions = ownerAdmissions;
        }

        public OrchestrationExecutionStateCapacity State { get; }
        public int PlaybackCount { get; }
        public int ProgramCount => m_OwnerInstances.Length;
        public ReadOnlyMemory<int> OwnerInstances(int programIndex) => m_OwnerInstances[programIndex];
        public int SourceDepth(int programIndex) => m_SourceDepths[programIndex];

        public int CountOwnerElements(ReadOnlySpan<ReadOnlyMemory<int>> elements)
        {
            int total = 0;
            foreach (OwnerAdmission admission in m_OwnerAdmissions)
            {
                int maximum = 0;
                foreach (OwnerCount[] root in admission.Roots)
                {
                    int count = 0;
                    foreach (OwnerCount owner in root)
                        count = checked(count + checked(owner.Count * elements[owner.Program].Span[owner.Owner]));
                    maximum = Math.Max(maximum, count);
                }
                total = checked(total + checked(maximum * admission.Instances));
            }
            return total;
        }

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
                (int Playbacks, int Depth, List<FramePoolBuilder> Pools, OwnerCount[] Elements)>();
            var ownerInstanceBuffers = new int[programs.Length][];
            var ownerInstances = new ReadOnlyMemory<int>[programs.Length];
            var sourceDepths = new int[programs.Length];
            var framePools = new List<FramePoolBuilder>();
            var ownerAdmissions = new List<OwnerAdmission>();
            var ownerPools = new ReadOnlyMemory<int>[programs.Length];
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
                var poolIndices = new int[program.OperationCount];
                Array.Fill(poolIndices, -1);
                for (int owner = 0; owner < poolIndices.Length; owner++)
                    if (program.Operation(owner).ExecutionFrameOwnerOperationIndex == owner)
                    {
                        ref readonly OrchestrationExecutionOwnerStorage storage = ref program.ExecutionStorage.Owners.Span[owner];
                        int pool = 0;
                        while (pool < framePools.Count && !framePools[pool].Matches(in storage))
                            pool++;
                        if (pool == framePools.Count)
                        {
                            var prepared = new FramePoolBuilder();
                            prepared.AddOwner(0, in storage);
                            framePools.Add(prepared);
                        }
                        poolIndices[owner] = pool;
                    }
                ownerPools[programIndex] = poolIndices;
            }
            int playbackCount = 0;
            foreach (ref readonly OrchestrationRootAdmission admission in admissions)
            {
                if (admission.MaximumInstances == 0)
                    continue;
                int playbacks = 0;
                var admissionPools = new List<FramePoolBuilder>();
                var admissionOwners = new Dictionary<(int Program, int Owner), int>();
                var admissionRoots = new OwnerCount[admission.Roots.Length][];
                int rootIndex = 0;
                foreach (ref readonly OrchestrationExecutionRoot root in admission.Roots.Span)
                {
                    int programIndex = root.ProgramIndex;
                    OrchestrationProgram<TValue> program = programs[programIndex];
                    var key = (programIndex, root.OwnerOperationIndex);
                    if (!roots.TryGetValue(key, out var capacity))
                    {
                        if (program.Operation(root.OwnerOperationIndex).ExecutionFrameOwnerOperationIndex != root.OwnerOperationIndex)
                            throw new InvalidOperationException($"Program '{programIndex}' admission '{root.OwnerOperationIndex}' is not an execution frame owner.");
                        var counts = new Dictionary<(int Program, int Owner), int>();
                        int rootPlaybacks = 0;
                        var rootPools = new List<FramePoolBuilder>();
                        int depth = AccumulateOwners(programs, programIndex, root.OwnerOperationIndex, 1,
                            ownerCalls, callMap, counts, new HashSet<(int Program, int Owner)>(),
                            rootPools, ownerPools, ref rootPlaybacks);
                        var elements = new List<OwnerCount>();
                        foreach (var item in counts)
                            elements.Add(new OwnerCount(item.Key.Program, item.Key.Owner, item.Value));
                        roots.Add(key, capacity = (rootPlaybacks, depth, rootPools, elements.ToArray()));
                    }
                    admissionRoots[rootIndex++] = capacity.Elements;
                    playbacks = Math.Max(playbacks, capacity.Playbacks);
                    for (int depth = 0; depth < capacity.Pools.Count; depth++)
                        Pool(admissionPools, depth).MergeMaximum(capacity.Pools[depth]);
                    foreach (OwnerCount element in capacity.Elements)
                    {
                        sourceDepths[element.Program] = Math.Max(sourceDepths[element.Program], capacity.Depth);
                        var owner = (element.Program, element.Owner);
                        admissionOwners.TryGetValue(owner, out int previous);
                        admissionOwners[owner] = Math.Max(previous, element.Count);
                    }
                }
                foreach (var item in admissionOwners)
                    ownerInstanceBuffers[item.Key.Program][item.Key.Owner] = checked(
                        ownerInstanceBuffers[item.Key.Program][item.Key.Owner] +
                        checked(admission.MaximumInstances * item.Value));
                for (int depth = 0; depth < admissionPools.Count; depth++)
                    Pool(framePools, depth).AddAdmission(admissionPools[depth], admission.MaximumInstances);
                playbackCount = checked(playbackCount + checked(admission.MaximumInstances * playbacks));
                ownerAdmissions.Add(new OwnerAdmission(admissionRoots, admission.MaximumInstances));
            }
            var pools = new OrchestrationExecutionFramePoolCapacity[framePools.Count];
            for (int depth = 0; depth < pools.Length; depth++)
                pools[depth] = framePools[depth].Build();
            var state = new OrchestrationExecutionStateCapacity(pools, ownerPools);
            return new OrchestrationExecutionCapacity(in state, playbackCount, ownerInstances, sourceDepths,
                ownerAdmissions.ToArray());
        }

        readonly struct OwnerCount
        {
            internal OwnerCount(int program, int owner, int count)
            {
                Program = program;
                Owner = owner;
                Count = count;
            }

            internal readonly int Program;
            internal readonly int Owner;
            internal readonly int Count;
        }

        readonly struct OwnerAdmission
        {
            internal OwnerAdmission(OwnerCount[][] roots, int instances)
            {
                Roots = roots;
                Instances = instances;
            }

            internal readonly OwnerCount[][] Roots;
            internal readonly int Instances;
        }

        static int AccumulateOwners<TValue>(ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            int programIndex, int owner, int count,
            List<int>[][] ownerCalls,
            Dictionary<(int Program, int Operation), OrchestrationTimelineCallCapacity> calls,
            Dictionary<(int Program, int Owner), int> instances, HashSet<(int Program, int Owner)> active,
            List<FramePoolBuilder> pools, ReadOnlyMemory<int>[] ownerPools, ref int playbacks) where TValue : unmanaged
        {
            var key = (programIndex, owner);
            if (!active.Add(key))
                throw new InvalidOperationException($"Program '{programIndex}' 的执行帧 '{owner}' 存在没有有限容量的 Timeline 调用循环。");
            instances.TryGetValue(key, out int previous);
            instances[key] = checked(previous + count);
            Pool(pools, ownerPools[programIndex].Span[owner]).AddOwner(count,
                programs[programIndex].ExecutionStorage.Owners.Span[owner]);
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
                                ownerCalls, calls, instances, active, pools, ownerPools, ref playbacks)));
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

            internal bool Matches(in OrchestrationExecutionOwnerStorage storage) =>
                m_Operations == storage.OperationCount && m_Values == storage.BlackboardCount &&
                m_Outputs == storage.SavedOutputCount && storage.ValueKindCounts.Span.SequenceEqual(m_Kinds);

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
