using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationRootAlternative
    {
        public OrchestrationRootAlternative(int programIndex, int ownerOperationIndex)
            : this(new[] { new OrchestrationOccurrenceCapacity(programIndex, ownerOperationIndex, 1) })
        {
        }

        public OrchestrationRootAlternative(ReadOnlyMemory<OrchestrationOccurrenceCapacity> occurrences) =>
            Occurrences = occurrences;

        public ReadOnlyMemory<OrchestrationOccurrenceCapacity> Occurrences { get; }
    }

    public readonly struct OrchestrationRootAdmission
    {
        public OrchestrationRootAdmission(ReadOnlyMemory<OrchestrationRootAlternative> alternatives, int maximumInstances)
        {
            Alternatives = alternatives;
            MaximumInstances = maximumInstances;
        }

        public ReadOnlyMemory<OrchestrationRootAlternative> Alternatives { get; }
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

        public OrchestrationRootAdmission[] CreateTimelineRootAdmissions<TValue>(
            ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<OrchestrationTimelineCallCapacity> calls,
            int maximumCopies) where TValue : unmanaged
        {
            var occurrencesByOwner = new Dictionary<(int Program, int Owner), List<OrchestrationOccurrenceCapacity>>();
            foreach (ref readonly OrchestrationTimelineCallCapacity call in calls)
            {
                int owner = programs[call.ProgramIndex].Operation(call.OperationIndex).ExecutionFrameOwnerOperationIndex;
                var key = (call.ProgramIndex, owner);
                if (!occurrencesByOwner.TryGetValue(key, out var occurrences))
                    occurrencesByOwner.Add(key, occurrences = new List<OrchestrationOccurrenceCapacity>());
                foreach (ref readonly OrchestrationOccurrenceCapacity occurrence in call.Occurrences.Span)
                    occurrences.Add(occurrence);
            }
            var admissions = new OrchestrationRootAdmission[m_OwnerAdmissions.Length];
            for (int index = 0; index < admissions.Length; index++)
            {
                OwnerAdmission admission = m_OwnerAdmissions[index];
                var alternatives = new OrchestrationRootAlternative[admission.Alternatives.Length];
                for (int alternative = 0; alternative < alternatives.Length; alternative++)
                {
                    var projected = new List<OrchestrationOccurrenceCapacity>();
                    foreach (OwnerCount owner in admission.Alternatives[alternative])
                    {
                        if (!occurrencesByOwner.TryGetValue((owner.Program, owner.Owner), out var occurrences))
                            continue;
                        foreach (OrchestrationOccurrenceCapacity occurrence in occurrences)
                            projected.Add(new OrchestrationOccurrenceCapacity(occurrence.ProgramIndex,
                                occurrence.OwnerOperationIndex, checked(owner.Count * occurrence.MaximumInstances)));
                    }
                    alternatives[alternative] = new OrchestrationRootAlternative(projected.ToArray());
                }
                admissions[index] = new OrchestrationRootAdmission(alternatives,
                    checked(admission.Instances * maximumCopies));
            }
            return admissions;
        }

        public int CountOwnerElements(ReadOnlySpan<ReadOnlyMemory<int>> elements)
        {
            int total = 0;
            foreach (OwnerAdmission admission in m_OwnerAdmissions)
            {
                int maximum = 0;
                foreach (OwnerCount[] root in admission.Alternatives)
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
            ReadOnlySpan<OrchestrationPreparedProgramStorage<TValue>> storages,
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
                        ref readonly OrchestrationExecutionOwnerStorage storage = ref storages[programIndex].Storage.Owners.Span[owner];
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
                var admissionRoots = new OwnerCount[admission.Alternatives.Length][];
                int rootIndex = 0;
                foreach (ref readonly OrchestrationRootAlternative alternative in admission.Alternatives.Span)
                {
                    int alternativePlaybacks = 0;
                    var alternativePools = new List<FramePoolBuilder>();
                    var alternativeOwners = new Dictionary<(int Program, int Owner), int>();
                    foreach (ref readonly OrchestrationOccurrenceCapacity root in alternative.Occurrences.Span)
                    {
                        if (root.MaximumInstances == 0)
                            continue;
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
                            int depth = AccumulateOwners(storages, programIndex, root.OwnerOperationIndex, 1,
                                ownerCalls, callMap, counts, new HashSet<(int Program, int Owner)>(),
                                rootPools, ownerPools, ref rootPlaybacks);
                            var elements = new List<OwnerCount>();
                            foreach (var item in counts)
                                elements.Add(new OwnerCount(item.Key.Program, item.Key.Owner, item.Value));
                            roots.Add(key, capacity = (rootPlaybacks, depth, rootPools, elements.ToArray()));
                        }
                        alternativePlaybacks = checked(alternativePlaybacks +
                            checked(capacity.Playbacks * root.MaximumInstances));
                        for (int pool = 0; pool < capacity.Pools.Count; pool++)
                            Pool(alternativePools, pool).AddAdmission(capacity.Pools[pool], root.MaximumInstances);
                        foreach (OwnerCount element in capacity.Elements)
                        {
                            sourceDepths[element.Program] = Math.Max(sourceDepths[element.Program], capacity.Depth);
                            var owner = (element.Program, element.Owner);
                            alternativeOwners.TryGetValue(owner, out int previous);
                            alternativeOwners[owner] = checked(previous + checked(element.Count * root.MaximumInstances));
                        }
                    }
                    var alternativeElements = new OwnerCount[alternativeOwners.Count];
                    int elementIndex = 0;
                    foreach (var item in alternativeOwners)
                    {
                        alternativeElements[elementIndex++] = new OwnerCount(item.Key.Program, item.Key.Owner, item.Value);
                        admissionOwners.TryGetValue(item.Key, out int previous);
                        admissionOwners[item.Key] = Math.Max(previous, item.Value);
                    }
                    admissionRoots[rootIndex++] = alternativeElements;
                    playbacks = Math.Max(playbacks, alternativePlaybacks);
                    for (int pool = 0; pool < alternativePools.Count; pool++)
                        Pool(admissionPools, pool).MergeMaximum(alternativePools[pool]);
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
                Alternatives = roots;
                Instances = instances;
            }

            internal readonly OwnerCount[][] Alternatives;
            internal readonly int Instances;
        }

        static int AccumulateOwners<TValue>(ReadOnlySpan<OrchestrationPreparedProgramStorage<TValue>> storages,
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
                storages[programIndex].Storage.Owners.Span[owner]);
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
                            depth = Math.Max(depth, checked(1 + AccumulateOwners(storages, occurrence.ProgramIndex,
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
