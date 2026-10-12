using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public static class GeneratedBlackboardProtocol
    {
        public const int Version = 1;
        public const int MaximumFieldsPerBlock = 32;
    }

    public readonly struct GeneratedBlackboardField
    {
        public GeneratedBlackboardField(string declarationId, string key, int index,
            ProgramStateValueKind valueKind, ProgramScopeKind scope, ProgramBlackboardLifetime lifetime)
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
        public ProgramStateValueKind ValueKind { get; }
        public ProgramScopeKind Scope { get; }
        public ProgramBlackboardLifetime Lifetime { get; }
    }

    public interface IGeneratedBlackboardValues<TValue> where TValue : struct
    {
        int FieldCount { get; }
        TValue Read(int field);
        void Write(int field, TValue value);
        void CopyTo(IGeneratedBlackboardValues<TValue> destination);
        void Clear();
        void WriteCanonical<TWriter>(TWriter writer) where TWriter : class, ICanonicalValueWriter;
        void ReadCanonical(CanonicalReader reader);
        void RetainValues();
        void ReleaseValues();
    }

    public abstract class GeneratedBlackboardContract<TValue> where TValue : struct
    {
        protected GeneratedBlackboardContract(string ownerId, string identity, string defaultsFile,
            GeneratedBlackboardField[] fields)
        {
            OwnerId = ownerId;
            Identity = identity;
            DefaultsFile = defaultsFile;
            Fields = Array.AsReadOnly(fields);
        }

        public string OwnerId { get; }
        public string Identity { get; }
        public string DefaultsFile { get; }
        public abstract StableHash CanonicalSchemaHash { get; }
        public IReadOnlyList<GeneratedBlackboardField> Fields { get; }
        public abstract IGeneratedBlackboardValues<TValue> Create();
        public abstract IGeneratedBlackboardValues<TValue> ReadDefaults(byte[] bytes);
    }

    [Serializable]
    public abstract class GeneratedBlackboardModule<TValue> where TValue : struct
    {
        public abstract IReadOnlyList<GeneratedBlackboardContract<TValue>> Contracts { get; }
        public abstract GeneratedBlackboardContract<TValue> Require(string identity);
    }

    public sealed class GeneratedBlackboardConfiguration<TValue> where TValue : struct
    {
        readonly Dictionary<string, IGeneratedBlackboardValues<TValue>> m_Defaults = new(StringComparer.Ordinal);

        public GeneratedBlackboardConfiguration(GeneratedBlackboardModule<TValue> module,
            IReadOnlyDictionary<string, byte[]> defaults)
        {
            Module = module;
            DefaultFiles = defaults;
            var names = new List<string>(defaults.Keys);
            names.Sort(StringComparer.Ordinal);
            var writer = new CanonicalWriter();
            foreach (string name in names)
            {
                writer.WriteText(name);
                writer.WriteBytes(defaults[name]);
            }
            ContentHash = writer.ComputeHash();
            for (int index = 0; index < module.Contracts.Count; index++)
            {
                GeneratedBlackboardContract<TValue> contract = module.Contracts[index];
                if (defaults.TryGetValue(contract.DefaultsFile, out byte[] bytes))
                    m_Defaults.Add(contract.Identity, contract.ReadDefaults(bytes));
            }
        }

        public GeneratedBlackboardModule<TValue> Module { get; }
        public IReadOnlyDictionary<string, byte[]> DefaultFiles { get; }
        public StableHash ContentHash { get; }
        internal IGeneratedBlackboardValues<TValue> Defaults(string contract) => m_Defaults[contract];
    }

    public readonly struct GeneratedBlackboardSlot
    {
        public GeneratedBlackboardSlot(int slot, string instance, string contract, int field)
        {
            Slot = slot;
            Instance = instance;
            Contract = contract;
            Field = field;
        }

        public int Slot { get; }
        public string Instance { get; }
        public string Contract { get; }
        public int Field { get; }
    }

    internal sealed class GeneratedBlackboardBlock<TValue> where TValue : struct
    {
        internal GeneratedBlackboardBlock(string instance, GeneratedBlackboardContract<TValue> contract,
            IGeneratedBlackboardValues<TValue> defaults)
        {
            Instance = instance;
            Contract = contract;
            Defaults = defaults;
        }

        internal string Instance { get; }
        internal GeneratedBlackboardContract<TValue> Contract { get; }
        internal IGeneratedBlackboardValues<TValue> Defaults { get; }
    }

    internal readonly struct GeneratedCharacterBlackboardSource<TValue> where TValue : struct
    {
        internal GeneratedCharacterBlackboardSource(CharacterSkillId ability,
            IReadOnlyList<ProgramStateSlot> slots, IReadOnlyList<ProgramScopeLayout> scopes,
            ProgramCatalogEntry[] declarations, GeneratedBlackboardStateBinding<TValue> binding)
        {
            Ability = ability;
            Slots = slots;
            Scopes = scopes;
            Declarations = declarations;
            Binding = binding;
        }

        internal CharacterSkillId Ability { get; }
        internal IReadOnlyList<ProgramStateSlot> Slots { get; }
        internal IReadOnlyList<ProgramScopeLayout> Scopes { get; }
        internal ProgramCatalogEntry[] Declarations { get; }
        internal GeneratedBlackboardStateBinding<TValue> Binding { get; }
    }

    internal sealed class GeneratedCharacterBlackboardLayout<TValue> where TValue : struct
    {
        sealed class Declaration
        {
            internal string Contract;
            internal int Field;
            internal int Offset;
            internal ProgramBlackboardLifetime Lifetime;
        }

        readonly Dictionary<CharacterSkillId, int[]> m_Slots = new();
        internal readonly ProgramStateValueKind[] Kinds;
        internal readonly ProgramBlackboardLifetime[] Lifetimes;
        internal readonly GeneratedBlackboardStateBinding<TValue> Binding;

        internal GeneratedCharacterBlackboardLayout(GeneratedBlackboardConfiguration<TValue> configuration,
            IReadOnlyList<GeneratedCharacterBlackboardSource<TValue>> sources)
        {
            var declarations = new SortedDictionary<string, Declaration>(StringComparer.Ordinal);
            for (int program = 0; program < sources.Count; program++)
            {
                GeneratedCharacterBlackboardSource<TValue> source = sources[program];
                for (int scope = 0; scope < source.Scopes.Count; scope++)
                {
                    if (source.Scopes[scope].Kind != ProgramScopeKind.Character)
                        continue;
                    IReadOnlyList<int> slots = source.Scopes[scope].StateSlots;
                    for (int index = 0; index < slots.Count; index++)
                    {
                        int slot = slots[index];
                        if (source.Slots[slot].Semantic != ProgramStateSemantic.BlackboardValue)
                            continue;
                        string identity = source.Declarations[slot].Identity;
                        GeneratedBlackboardBlock<TValue> block = source.Binding.Blocks[source.Binding.SlotBlocks[slot]];
                        int field = source.Binding.SlotFields[slot];
                        if (declarations.TryGetValue(identity, out Declaration previous))
                        {
                            if (previous.Contract != block.Contract.Identity || previous.Field != field)
                                throw new InvalidOperationException($"Character blackboard '{identity}' has conflicting generated contracts.");
                        }
                        else
                            declarations.Add(identity, new Declaration { Contract = block.Contract.Identity,
                                Field = field, Lifetime = block.Contract.Fields[field].Lifetime });
                    }
                }
            }

            Kinds = new ProgramStateValueKind[declarations.Count * 4];
            Lifetimes = new ProgramBlackboardLifetime[Kinds.Length];
            var generated = new List<GeneratedBlackboardSlot>(declarations.Count);
            int offset = 0;
            foreach (KeyValuePair<string, Declaration> entry in declarations)
            {
                Declaration declaration = entry.Value;
                declaration.Offset = offset;
                GeneratedBlackboardContract<TValue> contract = configuration.Module.Require(declaration.Contract);
                Kinds[offset] = contract.Fields[declaration.Field].ValueKind;
                Kinds[offset + 1] = ProgramStateValueKind.BlackboardOwnerToken;
                Kinds[offset + 2] = ProgramStateValueKind.Int32;
                Kinds[offset + 3] = ProgramStateValueKind.BlackboardWriteStamp;
                for (int part = 0; part < 4; part++)
                    Lifetimes[offset + part] = declaration.Lifetime;
                generated.Add(new GeneratedBlackboardSlot(offset, "scope:character", declaration.Contract, declaration.Field));
                offset += 4;
            }
            Binding = new GeneratedBlackboardStateBinding<TValue>(Kinds.Length, configuration, generated);

            for (int program = 0; program < sources.Count; program++)
            {
                GeneratedCharacterBlackboardSource<TValue> source = sources[program];
                var mapping = new int[source.Slots.Count];
                Array.Fill(mapping, -1);
                var owners = new Dictionary<string, Declaration>(StringComparer.Ordinal);
                for (int scope = 0; scope < source.Scopes.Count; scope++)
                {
                    if (source.Scopes[scope].Kind != ProgramScopeKind.Character)
                        continue;
                    IReadOnlyList<int> slots = source.Scopes[scope].StateSlots;
                    for (int index = 0; index < slots.Count; index++)
                    {
                        int slot = slots[index];
                        if (source.Slots[slot].Semantic == ProgramStateSemantic.BlackboardValue)
                            owners.Add(source.Slots[slot].OwnerIdentity, declarations[source.Declarations[slot].Identity]);
                    }
                    for (int index = 0; index < slots.Count; index++)
                    {
                        int slot = slots[index];
                        mapping[slot] = owners[source.Slots[slot].OwnerIdentity].Offset + Part(source.Slots[slot].Semantic);
                    }
                }
                m_Slots.Add(source.Ability, mapping);
            }
        }

        internal int SlotCount => Kinds.Length;
        internal int[] Slots(CharacterSkillId ability) => m_Slots[ability];

        static int Part(ProgramStateSemantic semantic) => semantic switch
        {
            ProgramStateSemantic.BlackboardValue => 0,
            ProgramStateSemantic.BlackboardOwnerToken => 1,
            ProgramStateSemantic.BlackboardLifetime => 2,
            ProgramStateSemantic.BlackboardWriteStamp => 3,
            _ => throw new InvalidOperationException($"Character blackboard cannot own state semantic '{semantic}'.")
        };
    }

    public sealed class GeneratedBlackboardStateBinding<TValue> where TValue : struct
    {
        internal readonly GeneratedBlackboardBlock<TValue>[] Blocks;
        internal readonly int[] SlotBlocks;
        internal readonly int[] SlotFields;

        public GeneratedBlackboardStateBinding(int slotCount, GeneratedBlackboardConfiguration<TValue> configuration,
            IReadOnlyList<GeneratedBlackboardSlot> slots)
        {
            SlotBlocks = new int[slotCount];
            SlotFields = new int[slotCount];
            Array.Fill(SlotBlocks, -1);
            var blocks = new List<GeneratedBlackboardBlock<TValue>>();
            var indices = new Dictionary<(string Instance, string Contract), int>();
            for (int index = 0; index < slots.Count; index++)
            {
                GeneratedBlackboardSlot slot = slots[index];
                var key = (slot.Instance, slot.Contract);
                if (!indices.TryGetValue(key, out int block))
                {
                    block = blocks.Count;
                    indices.Add(key, block);
                    blocks.Add(new GeneratedBlackboardBlock<TValue>(slot.Instance,
                        configuration.Module.Require(slot.Contract), configuration.Defaults(slot.Contract)));
                }
                SlotBlocks[slot.Slot] = block;
                SlotFields[slot.Slot] = slot.Field;
            }
            Blocks = blocks.ToArray();
            var structure = new List<string> { "generated-blackboard-state/1" };
            for (int index = 0; index < Blocks.Length; index++)
            {
                structure.Add(Blocks[index].Instance);
                structure.Add(Blocks[index].Contract.Identity);
                structure.Add(Blocks[index].Contract.CanonicalSchemaHash.ToString());
            }
            StructureHash = StableHash.Compute(structure.ToArray());
        }

        public StableHash StructureHash { get; }
        public bool Contains(int slot) => SlotBlocks[slot] >= 0;
        public TValue DefaultValue(int slot) => Blocks[SlotBlocks[slot]].Defaults.Read(SlotFields[slot]);

        internal static GeneratedBlackboardStateBinding<TValue> Build(
            GeneratedBlackboardConfiguration<TValue> configuration, string numericTarget,
            IReadOnlyList<ProgramStateSlot> stateSlots, IReadOnlyList<ProgramScopeLayout> scopes,
            ProgramCatalogEntry[] declarations)
        {
            var slots = new List<GeneratedBlackboardSlot>();
            for (int scopeIndex = 0; scopeIndex < scopes.Count; scopeIndex++)
            {
                ProgramScopeLayout scope = scopes[scopeIndex];
                for (int index = 0; index < scope.StateSlots.Count; index++)
                {
                    int slot = scope.StateSlots[index];
                    if (stateSlots[slot].Semantic != ProgramStateSemantic.BlackboardValue)
                        continue;
                    ProgramCatalogEntry declaration = declarations[slot];
                    slots.Add(new GeneratedBlackboardSlot(slot, scope.Identity,
                        RequireField(declaration, "Generated" + numericTarget + "Contract"),
                        int.Parse(RequireField(declaration, "GeneratedField"), System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
            return new GeneratedBlackboardStateBinding<TValue>(stateSlots.Count, configuration, slots);
        }

        static string RequireField(ProgramCatalogEntry declaration, string name)
        {
            for (int index = 0; index < declaration.Fields.Count; index++)
                if (declaration.Fields[index].Name == name)
                    return declaration.Fields[index].Identity;
            throw new System.IO.InvalidDataException($"Blackboard declaration '{declaration.Identity}' lacks '{name}'. Regenerate its runtime data.");
        }
    }
}
