using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationGraphPreparation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
    {
        OrchestrationGraphPreparation(
            PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>[] graphs,
            OrchestrationProgram<TValue>[] programs,
            OrchestrationPreparedProgramStorage<TValue>[] storages)
        {
            Graphs = graphs;
            Programs = programs;
            PreparedProgramStorages = storages;
        }

        public ReadOnlyMemory<PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>> Graphs { get; }
        public ReadOnlyMemory<OrchestrationProgram<TValue>> Programs { get; }
        public ReadOnlyMemory<OrchestrationPreparedProgramStorage<TValue>> PreparedProgramStorages { get; }

        public static OrchestrationGraphPreparation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues> Prepare(
            FrozenOrchestrationIdentities identities,
            ReadOnlySpan<OrchestrationProgramSource<TValue>> sources,
            ReadOnlySpan<int> programContentIds,
            (OrchestrationBlackboardDefaults<TValue> Content, int IdentityTableIndex)? defaults)
        {
            var projected = new Dictionary<(int Contract, int Field), TValue>();
            if (defaults.HasValue)
            {
                var publication = defaults.Value;
                ReadOnlyMemory<int> mapping = identities.LocalToShared(publication.IdentityTableIndex);
                foreach (ref readonly OrchestrationBlackboardDefault<TValue> value in publication.Content.Values.Span)
                {
                    TValue sourceValue = value.Value;
                    projected.Add((mapping.Span[value.ContractIdentityId], value.Field),
                        PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>.MapValue(in sourceValue, mapping));
                }
            }

            var graphs = new PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>[sources.Length];
            var programs = new OrchestrationProgram<TValue>[sources.Length];
            var storages = new OrchestrationPreparedProgramStorage<TValue>[sources.Length];
            TValues values = default;
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                OrchestrationGraphContent<TValue> content = sources[programIndex].Content;
                ReadOnlySpan<int> mapping = identities.LocalToShared(sources[programIndex].IdentityTableIndex).Span;
                var overrides = new OrchestrationDefaultValue<TValue>[content.GeneratedBlackboards.Length];
                for (int bindingIndex = 0; bindingIndex < overrides.Length; bindingIndex++)
                {
                    ref readonly OrchestrationGraphBlackboardBinding binding = ref content.GeneratedBlackboards.Span[bindingIndex];
                    int contract = mapping[binding.ContractIdentityId];
                    if (!projected.TryGetValue((contract, binding.Field), out TValue value))
                        throw new InvalidOperationException($"当前发布默认值缺少黑板合同 '{identities.Table.Resolve(contract)}' 的字段 '{binding.Field}'。");
                    OrchestrationValueKind declaredKind = content.Program.ValueSlotTypes.Span[binding.ValueSlot].ValueKind;
                    OrchestrationValueKind kind = values.Kind(in value);
                    if (kind != declaredKind)
                        throw new InvalidOperationException($"图 '{content.Identity.GraphId}' 的黑板槽 '{binding.ValueSlot}' 要求 '{declaredKind}'，当前发布默认值为 '{kind}'。");
                    overrides[bindingIndex] = new OrchestrationDefaultValue<TValue>(binding.ValueSlot, value);
                }
                graphs[programIndex] = PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>.Prepare(
                    content, identities, sources[programIndex].IdentityTableIndex, overrides);
                OrchestrationProgramContent<TValue> program = graphs[programIndex].Program;
                programs[programIndex] = new OrchestrationProgram<TValue>(program);
                storages[programIndex] = new OrchestrationPreparedProgramStorage<TValue>(
                    programContentIds[programIndex], program.ExecutionStorage, program.ValueSlotTypes);
            }
            return new OrchestrationGraphPreparation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>(
                graphs, programs, storages);
        }
    }
}
