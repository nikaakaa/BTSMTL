using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace ThirdPersonSimulation
{
    public static class GeneratedBlackboardProtocol
    {
        public const int Version = 3;
        public const int MaximumFieldsPerBlock = 32;
    }

    public readonly struct GeneratedBlackboardField
    {
        public GeneratedBlackboardField(string declarationId, string key, int index,
            OrchestrationValueKind valueKind, OrchestrationScopeKind scope, OrchestrationBlackboardLifetime lifetime)
        {
            DeclarationId = declarationId;
            Key = key;
            Index = index;
            ValueKind = valueKind;
            Scope = scope;
            Lifetime = lifetime;
        }

        public string DeclarationId { get; }
        public string Key { get; }
        public int Index { get; }
        public OrchestrationValueKind ValueKind { get; }
        public OrchestrationScopeKind Scope { get; }
        public OrchestrationBlackboardLifetime Lifetime { get; }
    }

    public interface IGeneratedBlackboardValues<TValue> where TValue : unmanaged
    {
        void CollectIdentityTexts(ICollection<string> identities);
        TValue Read(int field, OrchestrationIdentityTable identities);
    }

    public abstract class GeneratedBlackboardContract<TValue> where TValue : unmanaged
    {
        protected GeneratedBlackboardContract(string ownerId, string identity, string defaultsFile,
            GeneratedBlackboardField[] fields)
        {
            OwnerId = ownerId;
            Identity = identity;
            DefaultsFile = defaultsFile;
            Fields = fields;
        }

        public string OwnerId { get; }
        public string Identity { get; }
        public string DefaultsFile { get; }
        public IReadOnlyList<GeneratedBlackboardField> Fields { get; }
        public abstract IGeneratedBlackboardValues<TValue> ReadDefaults(byte[] bytes);
    }

    [Serializable]
    public abstract class GeneratedBlackboardModule<TValue> where TValue : unmanaged
    {
        public abstract IReadOnlyList<GeneratedBlackboardContract<TValue>> Contracts { get; }
        public abstract GeneratedBlackboardContract<TValue> Require(string identity);
    }

    public sealed class GeneratedBlackboardConfiguration<TValue> where TValue : unmanaged
    {
        public GeneratedBlackboardConfiguration(GeneratedBlackboardModule<TValue> module,
            IReadOnlyDictionary<string, byte[]> files)
        {
            var contracts = new List<GeneratedBlackboardContract<TValue>>();
            var values = new List<IGeneratedBlackboardValues<TValue>>();
            var texts = new List<string>();
            var content = new List<string> { "generated-blackboard-defaults/3" };
            for (int index = 0; index < module.Contracts.Count; index++)
            {
                GeneratedBlackboardContract<TValue> contract = module.Contracts[index];
                if (!files.TryGetValue(contract.DefaultsFile, out byte[] bytes))
                    continue;
                IGeneratedBlackboardValues<TValue> defaults = contract.ReadDefaults(bytes);
                contracts.Add(contract);
                values.Add(defaults);
                texts.Add(contract.Identity);
                defaults.CollectIdentityTexts(texts);
                content.Add(contract.Identity);
                content.Add(StableHash.Compute(bytes).ToString());
            }
            OrchestrationIdentityTable identities = OrchestrationIdentityPublication.Create(texts);
            content.Add(identities.Identity.ToString());
            var published = new List<OrchestrationBlackboardDefault<TValue>>();
            for (int index = 0; index < contracts.Count; index++)
            {
                GeneratedBlackboardContract<TValue> contract = contracts[index];
                int identity = identities.Bind(contract.Identity);
                for (int field = 0; field < contract.Fields.Count; field++)
                    published.Add(new OrchestrationBlackboardDefault<TValue>(identity, field,
                        values[index].Read(field, identities)));
            }
            Defaults = new OrchestrationBlackboardDefaults<TValue>(identities,
                StableHash.Compute(content.ToArray()), published.ToArray());
        }

        public OrchestrationBlackboardDefaults<TValue> Defaults { get; }
    }
}
