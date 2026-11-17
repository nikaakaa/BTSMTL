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
            var ownerOperations = new List<int>[programs.Length][];
            var machineStates = new List<int>[programs.Length][];
            var roots = new Dictionary<(int Program, int Owner),
                (int Playbacks, int Depth, FrameUsage Frames, OwnerCount[] Elements)>();
            var ownerInstanceBuffers = new int[programs.Length][];
            var ownerInstances = new ReadOnlyMemory<int>[programs.Length];
            var sourceDepths = new int[programs.Length];
            var framePools = new List<FramePoolBuilder>();
            var ownerAdmissions = new List<PreparedOwnerAdmission>();
            var ownerPools = new ReadOnlyMemory<int>[programs.Length];
            for (int programIndex = 0; programIndex < programs.Length; programIndex++)
            {
                OrchestrationProgram<TValue> program = programs[programIndex];
                ownerOperations[programIndex] = new List<int>[program.OperationCount];
                machineStates[programIndex] = new List<int>[program.OperationCount];
                for (int operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
                {
                    ref readonly OrchestrationOperation operation = ref program.Operation(operationIndex);
                    if (operation.Code == OrchestrationOperationCode.StateMachine)
                        machineStates[programIndex][operationIndex] = new List<int>();
                    if (operation.Code == OrchestrationOperationCode.Timeline ||
                        operation.Code == OrchestrationOperationCode.StateMachine)
                    {
                        int owner = operation.ExecutionFrameOwnerOperationIndex;
                        List<int> owned = ownerOperations[programIndex][owner];
                        if (owned == null)
                            ownerOperations[programIndex][owner] = owned = new List<int>();
                        owned.Add(operationIndex);
                    }
                }
                ownerInstances[programIndex] = ownerInstanceBuffers[programIndex] = new int[program.OperationCount];
                var poolIndices = new int[program.OperationCount];
                Array.Fill(poolIndices, -1);
                for (int owner = 0; owner < poolIndices.Length; owner++)
                {
                    if (program.Operation(owner).Code == OrchestrationOperationCode.State)
                        machineStates[programIndex][program.Payload<OrchestrationStatePayload>(owner).MachineIndex].Add(owner);
                    if (program.Operation(owner).ExecutionFrameOwnerOperationIndex == owner)
                    {
                        ref readonly OrchestrationExecutionOwnerStorage storage = ref storages[programIndex].Storage.Owners.Span[owner];
                        ref readonly OrchestrationOperationStateStorage states = ref storages[programIndex].OperationStateOwners.Span[owner];
                        int pool = 0;
                        while (pool < framePools.Count && !framePools[pool].Matches(in storage, in states))
                            pool++;
                        if (pool == framePools.Count)
                            framePools.Add(new FramePoolBuilder(in storage, in states));
                        poolIndices[owner] = pool;
                    }
                }
                ownerPools[programIndex] = poolIndices;
            }
            var poolMap = new int[framePools.Count];
            for (int pool = 0; pool < poolMap.Length; pool++)
                poolMap[pool] = pool;
            int playbackCount = 0;
            foreach (ref readonly OrchestrationRootAdmission admission in admissions)
            {
                if (admission.MaximumInstances == 0)
                    continue;
                int playbacks = 0;
                var admissionPools = new List<FramePoolBuilder>();
                var admissionOwners = new Dictionary<(int Program, int Owner), int>();
                var admissionRoots = new OwnerCount[admission.Alternatives.Length][];
                var admissionFrames = new FrameUsage[admission.Alternatives.Length];
                int rootIndex = 0;
                foreach (ref readonly OrchestrationRootAlternative alternative in admission.Alternatives.Span)
                {
                    int alternativePlaybacks = 0;
                    var alternativeFrames = new List<FrameUsage>();
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
                            int depth = AccumulateOwners(programs, programIndex, root.OwnerOperationIndex, 1,
                                ownerOperations, machineStates, callMap, counts, new HashSet<(int Program, int Owner)>(),
                                out FrameUsage frames, ref rootPlaybacks);
                            var elements = new List<OwnerCount>();
                            foreach (var item in counts)
                                elements.Add(new OwnerCount(item.Key.Program, item.Key.Owner, item.Value));
                            roots.Add(key, capacity = (rootPlaybacks, depth, frames, elements.ToArray()));
                        }
                        alternativePlaybacks = checked(alternativePlaybacks +
                            checked(capacity.Playbacks * root.MaximumInstances));
                        alternativeFrames.Add(new FrameGroup(new[] { capacity.Frames },
                            FrameComposition.Together, root.MaximumInstances));
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
                    var liveFrames = new FrameGroup(alternativeFrames.ToArray(), FrameComposition.Together, 1);
                    admissionRoots[rootIndex] = alternativeElements;
                    admissionFrames[rootIndex++] = liveFrames;
                    playbacks = Math.Max(playbacks, alternativePlaybacks);
                    for (int pool = 0; pool < framePools.Count; pool++)
                        Pool(admissionPools, pool).MergeMaximum(framePools[pool],
                            liveFrames.Count(ownerPools, poolMap, pool, pool));
                }
                foreach (var item in admissionOwners)
                    ownerInstanceBuffers[item.Key.Program][item.Key.Owner] = checked(
                        ownerInstanceBuffers[item.Key.Program][item.Key.Owner] +
                        checked(admission.MaximumInstances * item.Value));
                for (int depth = 0; depth < admissionPools.Count; depth++)
                    Pool(framePools, depth).AddAdmission(admissionPools[depth], admission.MaximumInstances);
                playbackCount = checked(playbackCount + checked(admission.MaximumInstances * playbacks));
                ownerAdmissions.Add(new PreparedOwnerAdmission(
                    new OwnerAdmission(admissionRoots, admission.MaximumInstances), admissionFrames));
            }
            MergeFramePools(framePools, ownerPools, poolMap, ownerAdmissions);
            var pools = new OrchestrationExecutionFramePoolCapacity[framePools.Count];
            for (int depth = 0; depth < pools.Length; depth++)
                pools[depth] = framePools[depth].Build();
            var budgets = new OwnerAdmission[ownerAdmissions.Count];
            for (int index = 0; index < budgets.Length; index++)
                budgets[index] = ownerAdmissions[index].Budget;
            var state = new OrchestrationExecutionStateCapacity(pools, ownerPools);
            return new OrchestrationExecutionCapacity(in state, playbackCount, ownerInstances, sourceDepths,
                budgets);
        }

        static void MergeFramePools(List<FramePoolBuilder> pools,
            ReadOnlyMemory<int>[] ownerPools, int[] poolMap, List<PreparedOwnerAdmission> admissions)
        {
            bool merged;
            do
            {
                merged = false;
                for (int first = 0; first < pools.Count && !merged; first++)
                    for (int second = first + 1; second < pools.Count; second++)
                    {
                        int frameCount = 0;
                        foreach (PreparedOwnerAdmission admission in admissions)
                        {
                            int maximum = 0;
                            foreach (FrameUsage root in admission.LiveFrames)
                                maximum = Math.Max(maximum, root.Count(ownerPools, poolMap, first, second));
                            frameCount = checked(frameCount + checked(maximum * admission.Budget.Instances));
                        }
                        if (!pools[first].CanMerge(pools[second], frameCount))
                            continue;
                        pools[first].MergeSharedAdmission(pools[second], frameCount);
                        pools.RemoveAt(second);
                        for (int pool = 0; pool < poolMap.Length; pool++)
                            if (poolMap[pool] == second)
                                poolMap[pool] = first;
                            else if (poolMap[pool] > second)
                                poolMap[pool]--;
                        merged = true;
                        break;
                    }
            } while (merged);
            for (int program = 0; program < ownerPools.Length; program++)
            {
                int[] owners = ownerPools[program].ToArray();
                for (int owner = 0; owner < owners.Length; owner++)
                    if (owners[owner] >= 0)
                        owners[owner] = poolMap[owners[owner]];
                ownerPools[program] = owners;
            }
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

        static int AccumulateOwners<TValue>(ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            int programIndex, int owner, int count,
            List<int>[][] ownerOperations, List<int>[][] machineStates,
            Dictionary<(int Program, int Operation), OrchestrationTimelineCallCapacity> calls,
            Dictionary<(int Program, int Owner), int> instances, HashSet<(int Program, int Owner)> active,
            out FrameUsage usage, ref int playbacks) where TValue : unmanaged
        {
            var key = (programIndex, owner);
            if (!active.Add(key))
                throw new InvalidOperationException($"Program '{programIndex}' 的执行帧 '{owner}' 存在没有有限容量的 Timeline 调用循环。");
            instances.TryGetValue(key, out int previous);
            instances[key] = checked(previous + count);
            List<int> owned = ownerOperations[programIndex][owner];
            var timelineFrames = new Dictionary<int, FrameUsage>();
            int depth = 1;
            if (owned != null)
                foreach (int operationIndex in owned)
                {
                    if (programs[programIndex].Operation(operationIndex).Code != OrchestrationOperationCode.Timeline)
                        continue;
                    if (!calls.TryGetValue((programIndex, operationIndex), out OrchestrationTimelineCallCapacity call))
                        throw new InvalidOperationException($"Program '{programIndex}' Timeline '{operationIndex}' 缺少已完成的生命周期容量生产结果。");
                    playbacks = checked(playbacks + count);
                    var children = new List<FrameUsage>();
                    foreach (ref readonly OrchestrationOccurrenceCapacity occurrence in call.Occurrences.Span)
                        if (occurrence.MaximumInstances != 0)
                        {
                            depth = Math.Max(depth, checked(1 + AccumulateOwners(programs, occurrence.ProgramIndex,
                                occurrence.OwnerOperationIndex, checked(count * occurrence.MaximumInstances),
                                ownerOperations, machineStates, calls, instances, active, out FrameUsage child, ref playbacks)));
                            children.Add(child);
                        }
                    timelineFrames.Add(operationIndex, new FrameGroup(children.ToArray(), FrameComposition.Together, 1));
                }
            FrameUsage scope = owned == null
                ? new FrameGroup(Array.Empty<FrameUsage>(), FrameComposition.Together, 1)
                : ScopeFrames(programs[programIndex], owned, machineStates[programIndex], timelineFrames,
                    programs[programIndex].Operation(owner).ParentStateOperationIndex);
            usage = new FrameGroup(new FrameUsage[] { new OwnerFrames(new OwnerCount(programIndex, owner, count)), scope },
                FrameComposition.Together, 1);
            active.Remove(key);
            return depth;
        }

        static FrameUsage ScopeFrames<TValue>(OrchestrationProgram<TValue> program, List<int> owned,
            List<int>[] machineStates, Dictionary<int, FrameUsage> timelineFrames, int parentState) where TValue : unmanaged
        {
            var children = new List<FrameUsage>();
            foreach (int operation in owned)
            {
                if (program.Operation(operation).ParentStateOperationIndex != parentState)
                    continue;
                if (program.Operation(operation).Code == OrchestrationOperationCode.Timeline)
                    children.Add(timelineFrames[operation]);
                else
                {
                    var states = new List<FrameUsage>();
                    foreach (int state in machineStates[operation])
                        states.Add(ScopeFrames(program, owned, machineStates, timelineFrames, state));
                    children.Add(new FrameGroup(states.ToArray(), FrameComposition.Exclusive, 1));
                }
            }
            return new FrameGroup(children.ToArray(), FrameComposition.Together, 1);
        }

        readonly struct PreparedOwnerAdmission
        {
            internal PreparedOwnerAdmission(OwnerAdmission budget, FrameUsage[] liveFrames)
            {
                Budget = budget;
                LiveFrames = liveFrames;
            }

            internal readonly OwnerAdmission Budget;
            internal readonly FrameUsage[] LiveFrames;
        }

        enum FrameComposition { Together, Exclusive }

        abstract class FrameUsage
        {
            internal abstract int Count(ReadOnlyMemory<int>[] ownerPools, int[] poolMap, int first, int second);
        }

        sealed class OwnerFrames : FrameUsage
        {
            readonly OwnerCount m_Owner;

            internal OwnerFrames(OwnerCount owner) => m_Owner = owner;

            internal override int Count(ReadOnlyMemory<int>[] ownerPools, int[] poolMap, int first, int second)
            {
                int pool = poolMap[ownerPools[m_Owner.Program].Span[m_Owner.Owner]];
                return pool == first || pool == second ? m_Owner.Count : 0;
            }
        }

        sealed class FrameGroup : FrameUsage
        {
            readonly FrameUsage[] m_Children;
            readonly FrameComposition m_Composition;
            readonly int m_Copies;

            internal FrameGroup(FrameUsage[] children, FrameComposition composition, int copies)
            {
                m_Children = children;
                m_Composition = composition;
                m_Copies = copies;
            }

            internal override int Count(ReadOnlyMemory<int>[] ownerPools, int[] poolMap, int first, int second)
            {
                int count = 0;
                foreach (FrameUsage child in m_Children)
                {
                    int childCount = child.Count(ownerPools, poolMap, first, second);
                    count = m_Composition == FrameComposition.Exclusive
                        ? Math.Max(count, childCount) : checked(count + childCount);
                }
                return checked(count * m_Copies);
            }
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
            int m_StopContexts;
            int m_StateMachines;
            int m_Timelines;
            readonly int[] m_Kinds = new int[(int)OrchestrationValueKind.SpatialIdentity + 1];

            internal FramePoolBuilder() { }

            internal FramePoolBuilder(in OrchestrationExecutionOwnerStorage storage,
                in OrchestrationOperationStateStorage states)
            {
                m_Operations = storage.OperationCount;
                m_Values = storage.BlackboardCount;
                m_Outputs = storage.SavedOutputCount;
                m_StopContexts = states.StopContexts;
                m_StateMachines = states.StateMachines;
                m_Timelines = states.Timelines;
                storage.ValueKindCounts.Span.CopyTo(m_Kinds);
            }

            internal bool Matches(in OrchestrationExecutionOwnerStorage storage,
                in OrchestrationOperationStateStorage states) =>
                m_Operations == storage.OperationCount && m_Values == storage.BlackboardCount &&
                m_Outputs == storage.SavedOutputCount && m_StopContexts == states.StopContexts &&
                m_StateMachines == states.StateMachines && m_Timelines == states.Timelines &&
                storage.ValueKindCounts.Span.SequenceEqual(m_Kinds);

            internal bool CanMerge(FramePoolBuilder other, int frameCount)
            {
                if (frameCount >= (long)m_Count + other.m_Count ||
                    !PreservesElementCount(other, frameCount, m_Operations, other.m_Operations) ||
                    !PreservesElementCount(other, frameCount, m_Values, other.m_Values) ||
                    !PreservesElementCount(other, frameCount, m_Outputs, other.m_Outputs) ||
                    !PreservesElementCount(other, frameCount, m_StopContexts, other.m_StopContexts) ||
                    !PreservesElementCount(other, frameCount, m_StateMachines, other.m_StateMachines) ||
                    !PreservesElementCount(other, frameCount, m_Timelines, other.m_Timelines))
                    return false;
                for (int kind = 0; kind < m_Kinds.Length; kind++)
                {
                    int first = m_Kinds[kind];
                    int second = other.m_Kinds[kind];
                    if (kind == (int)OrchestrationValueKind.Boolean)
                    {
                        first = checked((first + 63) / 64);
                        second = checked((second + 63) / 64);
                    }
                    if (!PreservesElementCount(other, frameCount, first, second))
                        return false;
                }
                return true;
            }

            bool PreservesElementCount(FramePoolBuilder other, int frameCount, int first, int second) =>
                (long)frameCount * Math.Max(first, second) <=
                (long)m_Count * first + (long)other.m_Count * second;

            internal void MergeSharedAdmission(FramePoolBuilder other, int frameCount)
            {
                m_Count = frameCount;
                MergeWidths(other);
            }

            internal void MergeMaximum(FramePoolBuilder other, int count)
            {
                m_Count = Math.Max(m_Count, count);
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
                m_StopContexts = Math.Max(m_StopContexts, other.m_StopContexts);
                m_StateMachines = Math.Max(m_StateMachines, other.m_StateMachines);
                m_Timelines = Math.Max(m_Timelines, other.m_Timelines);
                for (int kind = 0; kind < m_Kinds.Length; kind++)
                    m_Kinds[kind] = Math.Max(m_Kinds[kind], other.m_Kinds[kind]);
            }

            internal OrchestrationExecutionFramePoolCapacity Build() =>
                new OrchestrationExecutionFramePoolCapacity(m_Count, m_Operations, m_Values, m_Outputs, m_Kinds,
                    new OrchestrationOperationStateStorage(m_StopContexts, m_StateMachines, m_Timelines));
        }
    }
}
