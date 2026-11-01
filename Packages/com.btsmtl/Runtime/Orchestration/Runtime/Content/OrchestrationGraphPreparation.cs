using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationGraphPreparation<TValue, TNumerics>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
    {
        OrchestrationGraphPreparation(
            PreparedOrchestrationGraph<TValue, TNumerics>[] graphs,
            OrchestrationProgram<TValue>[] programs,
            OrchestrationPreparedProgramStorage<TValue>[] storages)
        {
            Graphs = graphs;
            Programs = programs;
            PreparedProgramStorages = storages;
        }

        public ReadOnlyMemory<PreparedOrchestrationGraph<TValue, TNumerics>> Graphs { get; }
        public ReadOnlyMemory<OrchestrationProgram<TValue>> Programs { get; }
        public ReadOnlyMemory<OrchestrationPreparedProgramStorage<TValue>> PreparedProgramStorages { get; }

        public static OrchestrationGraphPreparation<TValue, TNumerics> Prepare(
            FrozenOrchestrationIdentities identities,
            ReadOnlySpan<OrchestrationProgramSource<TValue>> sources,
            ReadOnlySpan<int> programContentIds)
        {
            var graphs = new PreparedOrchestrationGraph<TValue, TNumerics>[sources.Length];
            var programs = new OrchestrationProgram<TValue>[sources.Length];
            var storages = new OrchestrationPreparedProgramStorage<TValue>[sources.Length];
            var actionKinds = new SortedDictionary<(int Graph, int Declaration), OrchestrationValueKind>();
            foreach (ref readonly OrchestrationProgramSource<TValue> source in sources)
            {
                ReadOnlySpan<int> mapping = identities.LocalToShared(source.IdentityTableIndex).Span;
                foreach (ref readonly OrchestrationGraphVariable variable in source.Content.Structure.Variables.Span)
                    if (source.Content.Structure.Program.BlackboardSlots.Span[variable.ValueSlot].ScopeKind ==
                        OrchestrationScopeKind.ActionInstance)
                        actionKinds.TryAdd((mapping[variable.GraphIdentityId], mapping[variable.DeclarationIdentityId]),
                            source.Content.Structure.Program.ValueSlotTypes.Span[variable.ValueSlot].ValueKind);
            }
            var actionSlots = new Dictionary<(int Graph, int Declaration), (int Slot, int TypeIndex)>();
            var actionKindCounts = new int[OrchestrationStateLayoutView.KindCount];
            foreach (var entry in actionKinds)
                actionSlots.Add(entry.Key, (actionSlots.Count, actionKindCounts[(int)entry.Value]++));
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                OrchestrationGraphContent<TValue> content = sources[programIndex].Content;
                ReadOnlySpan<int> mapping = identities.LocalToShared(sources[programIndex].IdentityTableIndex).Span;
                graphs[programIndex] = PreparedOrchestrationGraph<TValue, TNumerics>.Prepare(
                    content, identities, sources[programIndex].IdentityTableIndex,
                    ReadOnlySpan<OrchestrationDefaultValue<TValue>>.Empty,
                    PrepareStorage(content.Structure, mapping, actionSlots, actionKindCounts));
                OrchestrationProgramContent program = graphs[programIndex].Program;
                programs[programIndex] = new OrchestrationProgram<TValue>(program, graphs[programIndex].Parameters);
                storages[programIndex] = new OrchestrationPreparedProgramStorage<TValue>(
                    programContentIds[programIndex], program.ExecutionStorage, program.ValueSlotTypes);
            }
            return new OrchestrationGraphPreparation<TValue, TNumerics>(
                graphs, programs, storages);
        }

        static OrchestrationProgramStorageLayout PrepareStorage(OrchestrationGraphStructure graph,
            ReadOnlySpan<int> mapping,
            Dictionary<(int Graph, int Declaration), (int Slot, int TypeIndex)> actionSlots,
            int[] actionKindCounts)
        {
            OrchestrationProgramContent program = graph.Program;
            OrchestrationProgramStorageLayout original = program.ExecutionStorage;
            var values = original.Values.ToArray();
            var counts = new int[original.Owners.Length];
            var kinds = new int[original.Owners.Length][];
            for (int owner = 0; owner < kinds.Length; owner++)
                if (program.Operations.Span[owner].ExecutionFrameOwnerOperationIndex == owner)
                {
                    counts[owner] = actionSlots.Count;
                    kinds[owner] = (int[])actionKindCounts.Clone();
                }
            foreach (ref readonly OrchestrationGraphVariable variable in graph.Variables.Span)
            {
                int slot = variable.ValueSlot;
                if (program.BlackboardSlots.Span[slot].ScopeKind != OrchestrationScopeKind.ActionInstance)
                    continue;
                var shared = actionSlots[(mapping[variable.GraphIdentityId], mapping[variable.DeclarationIdentityId])];
                values[slot] = new OrchestrationValueStorageSlot(-1, shared.Slot, shared.TypeIndex,
                    values[slot].SavedOutputIndex, shared.Slot);
            }
            for (int slot = 0; slot < values.Length; slot++)
            {
                OrchestrationValueStorageSlot value = values[slot];
                if (value.ValueIndex < 0 || program.BlackboardSlots.Span[slot].ScopeKind == OrchestrationScopeKind.ActionInstance)
                    continue;
                int index = counts[value.OwnerOperationIndex]++;
                int kind = (int)program.ValueSlotTypes.Span[slot].ValueKind;
                values[slot] = new OrchestrationValueStorageSlot(value.OwnerOperationIndex,
                    index, kinds[value.OwnerOperationIndex][kind]++, value.SavedOutputIndex, index);
            }
            var owners = new OrchestrationExecutionOwnerStorage[original.Owners.Length];
            for (int owner = 0; owner < owners.Length; owner++)
                if (kinds[owner] != null)
                {
                    OrchestrationExecutionOwnerStorage previous = original.Owners.Span[owner];
                    owners[owner] = new OrchestrationExecutionOwnerStorage(previous.OperationCount,
                        previous.SavedOutputCount, counts[owner], kinds[owner]);
                }
            return new OrchestrationProgramStorageLayout(original.OperationIndices, values, owners);
        }
    }
}
