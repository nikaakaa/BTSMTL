using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation
{
    public sealed class CharacterControlStateSchema
    {
        readonly Dictionary<CharacterControlStateFieldId, int> m_Indexes;
        readonly CanonicalDeclaredIdentityReader m_StateIdentities;
        readonly CanonicalDeclaredIdentityReader m_TransitionIdentities;

        public CharacterControlStateSchema(CharacterControlModuleContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            var fields = new List<CharacterControlStateFieldDescriptor>(contract.StateFields.Count);
            m_Indexes = new Dictionary<CharacterControlStateFieldId, int>();
            for (int i = 0; i < contract.StateFields.Count; i++)
            {
                CharacterControlStateFieldDescriptor field = contract.StateFields[i];
                fields.Add(field);
                if (!m_Indexes.TryAdd(field.Id, i))
                    throw new InvalidDataException($"Character control state field '{field.Id}' is duplicated.");
            }
            var states = new string[contract.States.Count];
            for (int index = 0; index < states.Length; index++)
                states[index] = contract.States[index].Id.Value;
            var transitions = new string[contract.Transitions.Count];
            for (int index = 0; index < transitions.Length; index++)
                transitions[index] = contract.Transitions[index].Id.Value;
            m_StateIdentities = new CanonicalDeclaredIdentityReader(states);
            m_TransitionIdentities = new CanonicalDeclaredIdentityReader(transitions);
            Declaration = new CharacterControlSchemaDeclaration(contract.ModuleId, contract.SemanticVersion, fields.AsReadOnly());
            SchemaHash = ComputeSchemaHash(Declaration);
        }

        internal CharacterControlStateSchema(
            CharacterControlModuleId moduleId,
            int semanticVersion,
            IReadOnlyList<CharacterControlStateFieldDescriptor> fields)
        {
            if (!moduleId.IsValid || semanticVersion <= 0 || fields == null)
                throw new ArgumentException("Character control state schema identity is incomplete.");
            var copied = new List<CharacterControlStateFieldDescriptor>(fields.Count);
            m_Indexes = new Dictionary<CharacterControlStateFieldId, int>();
            for (int i = 0; i < fields.Count; i++)
            {
                CharacterControlStateFieldDescriptor field = fields[i]
                    ?? throw new ArgumentException("Character control state schema contains a missing field.", nameof(fields));
                copied.Add(field);
                if (!m_Indexes.TryAdd(field.Id, i))
                    throw new InvalidDataException($"Character control state field '{field.Id}' is duplicated.");
            }
            Declaration = new CharacterControlSchemaDeclaration(moduleId, semanticVersion, copied.AsReadOnly());
            SchemaHash = ComputeSchemaHash(Declaration);
        }

        public CharacterControlSchemaDeclaration Declaration { get; }
        public CharacterControlModuleId ModuleId => Declaration.ModuleId;
        public int SemanticVersion => Declaration.SemanticVersion;
        public IReadOnlyList<CharacterControlStateFieldDescriptor> Fields => Declaration.Fields;
        public int FieldCount => Declaration.Fields.Count;
        public StableHash SchemaHash { get; }

        public int RequireIndex(CharacterControlStateFieldId field) =>
            m_Indexes.TryGetValue(field, out int index)
                ? index
                : throw new InvalidOperationException($"Character control state field '{field}' is not declared.");

        public CharacterControlStateValueKind RequireKind(CharacterControlStateFieldId field) =>
            Fields[RequireIndex(field)].ValueKind;

        public string ResolvePublishedIdentity(int fieldIndex, in CanonicalTextValue value) =>
            IdentityDirectory(fieldIndex).ResolvePublished(in value);

        CanonicalDeclaredIdentityReader IdentityDirectory(int fieldIndex) =>
            Fields[fieldIndex].Semantic switch
            {
                CharacterControlStateSemantic.ActiveState => m_StateIdentities,
                CharacterControlStateSemantic.Transition => m_TransitionIdentities,
                _ => throw new InvalidOperationException("Control field is an open identity value.")
            };

        static StableHash ComputeSchemaHash(CharacterControlSchemaDeclaration declaration)
        {
            var hash = new CanonicalHashWriter();
            hash.WriteHash(CharacterControlSchemaDeclaration.CanonicalSchemaHash);
            hash.WriteHash(CharacterControlStateValue.CanonicalSchemaHash);
            CharacterControlSchemaDeclaration.CanonicalWrite(hash, declaration);
            return hash.ComputeHash();
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class CharacterControlSchemaDeclaration
    {
        public CharacterControlSchemaDeclaration(CharacterControlModuleId moduleId, int semanticVersion,
            ReadOnlyCollection<CharacterControlStateFieldDescriptor> fields)
        {
            ModuleId = moduleId;
            SemanticVersion = semanticVersion;
            Fields = fields;
        }

        public CharacterControlModuleId ModuleId { get; }
        public int SemanticVersion { get; }
        public ReadOnlyCollection<CharacterControlStateFieldDescriptor> Fields { get; }
    }

    [CanonicalContract]
    public readonly partial struct CharacterControlStateValue
    {
        [CanonicalUnionCase]
        public bool Boolean { get; }
        [CanonicalUnionCase]
        public int Int32 { get; }
        [CanonicalUnionCase]
        public ulong UInt64 { get; }
        [CanonicalUnionCase(NormalizeNull = true)]
        public CanonicalTextValue Identity { get; }
        [CanonicalUnionCase(NormalizeZero = true)]
        public double Number { get; }

        [CanonicalValidation]
        void RequireValid()
        {
            if (Kind == CharacterControlStateValueKind.Number && (double.IsNaN(Number) || double.IsInfinity(Number)))
                throw new ArgumentOutOfRangeException(nameof(Number));
        }

        public bool Equals(CharacterControlStateValue other) =>
            Kind == other.Kind && Boolean == other.Boolean && Int32 == other.Int32 &&
            UInt64 == other.UInt64 && Number.Equals(other.Number) && Identity.Equals(other.Identity);

        public void Retain() => CanonicalRetain(this);
        public void Release() => CanonicalRelease(this);
    }

    [CanonicalContract(PreparedStorage = typeof(CharacterControlRuntimeStateStorage))]
    public readonly partial struct CharacterControlStateFrame
    {
        public CharacterControlStateFrame(ulong lastCompletedTick, CanonicalArraySlice<CharacterControlStateValue> items)
        {
            LastCompletedTick = lastCompletedTick;
            Items = items;
        }

        public ulong LastCompletedTick { get; }
        public CanonicalArraySlice<CharacterControlStateValue> Items { get; }
    }

    public readonly struct CharacterControlRuntimeState
    {
        readonly CharacterControlStateFrame m_Frame;
        readonly CharacterControlRuntimeStateStorage m_Storage;
        readonly uint m_StorageGeneration;
        readonly StableHash m_StateHash;

        public CharacterControlRuntimeState(
            CharacterControlStateSchema schema,
            ulong lastCompletedTick,
            IEnumerable<CharacterControlStateValue> values)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            var copied = values == null
                ? new CharacterControlStateValue[0]
                : values is CharacterControlStateValue[] valueArray
                    ? (CharacterControlStateValue[])valueArray.Clone()
                    : new List<CharacterControlStateValue>(values).ToArray();
            if (copied.Length != schema.FieldCount)
                throw new ArgumentException("Character control runtime state values do not match its schema.", nameof(values));
            for (int i = 0; i < copied.Length; i++)
            {
                if (copied[i].Kind != schema.Fields[i].ValueKind)
                    throw new ArgumentException($"Character control runtime state field '{schema.Fields[i].Id}' kind does not match its schema.", nameof(values));
            }
            m_Frame = new CharacterControlStateFrame(lastCompletedTick, new CanonicalArraySlice<CharacterControlStateValue>(copied));
            m_Storage = null;
            m_StorageGeneration = 0;
            m_StateHash = ComputeHash(schema, lastCompletedTick, copied, new CanonicalHashWriter());
            Retain();
        }

        public CharacterControlStateSchema Schema { get; }
        public CharacterControlModuleId ModuleId => Schema.ModuleId;
        public int SemanticVersion => Schema.SemanticVersion;
        public ulong LastCompletedTick => m_Frame.LastCompletedTick;
        public IReadOnlyList<CharacterControlStateValue> Values => ValueArray;
        public StableHash StateHash
        {
            get
            {
                if (m_Storage == null)
                    return m_StateHash;
                m_Storage.RequireGeneration(m_StorageGeneration);
                return m_Storage.GetStateHash(Schema, LastCompletedTick);
            }
        }
        internal StableHash StoredHash => m_Storage == null ? m_StateHash : m_Storage.StateHash;
        public bool IsValid => Schema != null && m_Frame.Items.Array != null;

        internal CharacterControlStateFrame Frame
        {
            get
            {
                m_Storage?.RequireGeneration(m_StorageGeneration);
                return m_Frame;
            }
        }

        internal static StableHash ComputeHash(
            CharacterControlStateSchema schema,
            ulong lastCompletedTick,
            CharacterControlStateValue[] values,
            CanonicalHashWriter hash)
        {
            hash.Reset();
            hash.WriteHash(CharacterControlStateFrame.CanonicalSchemaHash);
            hash.WriteHash(schema.SchemaHash);
            CharacterControlStateFrame.CanonicalWrite(hash,
                new CharacterControlStateFrame(lastCompletedTick, new CanonicalArraySlice<CharacterControlStateValue>(values)));
            return hash.ComputeHash();
        }

        internal CharacterControlRuntimeState(
            CharacterControlStateSchema schema,
            ulong lastCompletedTick,
            CharacterControlStateValue[] values,
            StableHash stateHash)
        {
            Schema = schema;
            m_Frame = new CharacterControlStateFrame(lastCompletedTick, new CanonicalArraySlice<CharacterControlStateValue>(values));
            m_Storage = null;
            m_StorageGeneration = 0;
            m_StateHash = stateHash;
            Retain();
        }

        internal CharacterControlRuntimeState(
            CharacterControlStateSchema schema,
            ulong lastCompletedTick,
            CharacterControlRuntimeStateStorage storage,
            StableHash stateHash)
        {
            Schema = schema;
            m_Frame = new CharacterControlStateFrame(lastCompletedTick, new CanonicalArraySlice<CharacterControlStateValue>(storage.Values));
            m_Storage = storage;
            m_StorageGeneration = storage.Generation;
            m_StateHash = default;
            storage.StateHash = stateHash;
        }

        public void Retain() => CharacterControlStateFrame.CanonicalRetain(Frame);

        public void Release() => CharacterControlStateFrame.CanonicalRelease(Frame);

        internal bool OwnsValueArray => m_Storage == null;

        internal CharacterControlStateValue[] ValueArray => Frame.Items.Array;

        public CharacterControlStateValue Get(CharacterControlStateFieldId field) =>
            ValueArray[Schema.RequireIndex(field)];

        public void RequireContract(CharacterControlModuleContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (contract.ModuleId != ModuleId || contract.SemanticVersion != SemanticVersion)
                throw new InvalidOperationException($"Character control runtime state '{ModuleId}/{SemanticVersion}' does not match module contract '{contract.ModuleId}/{contract.SemanticVersion}'.");
            if (!contract.StateSchema.SchemaHash.Equals(Schema.SchemaHash))
                throw new InvalidOperationException($"Character control runtime state schema does not match module '{ModuleId}'.");
        }

        public void RequireBinding(CharacterControlRuntimeBinding binding)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (binding.ModuleId != ModuleId || binding.SemanticVersion != SemanticVersion)
                throw new InvalidOperationException($"Character control runtime state '{ModuleId}/{SemanticVersion}' does not match binding '{binding.ModuleId}/{binding.SemanticVersion}'.");
        }

        public static CharacterControlRuntimeState CreateInitial(
            CharacterControlRuntimeBinding binding,
            CharacterControlModuleContract contract)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            binding.RequireContract(contract);
            CharacterControlStateSchema schema = contract.StateSchema;
            var values = new CharacterControlStateValue[schema.FieldCount];
            for (int i = 0; i < values.Length; i++)
                values[i] = schema.Fields[i].Semantic == CharacterControlStateSemantic.ActiveState
                    ? CharacterControlStateValue.FromIdentity(contract.InitialState.Value)
                    : CharacterControlStateValue.CanonicalDefault(schema.Fields[i].ValueKind);
            return new CharacterControlRuntimeState(schema, 0, values);
        }

    }

    public sealed class CharacterControlRuntimeStateStorage
    {
        public CharacterControlRuntimeStateStorage(int fieldCount) =>
            Items = new CanonicalBuffer<CharacterControlStateValue>(fieldCount);

        internal CharacterControlStateValue[] Values => Items.OwnedArray;
        internal CanonicalBuffer<CharacterControlStateValue> Items { get; }
        internal CanonicalHashWriter Hash { get; } = new CanonicalHashWriter();
        internal StableHash StateHash { get; set; }
        internal uint Generation { get; private set; }

        public void BeginWrite()
        {
            Generation++;
            StateHash = default;
        }

        internal StableHash GetStateHash(CharacterControlStateSchema schema, ulong lastCompletedTick)
        {
            if (!StateHash.IsValid)
                StateHash = CharacterControlRuntimeState.ComputeHash(schema, lastCompletedTick, Values, Hash);
            return StateHash;
        }

        public void Clear()
        {
            CharacterControlStateFrame.CanonicalRelease(
                new CharacterControlStateFrame(0, new CanonicalArraySlice<CharacterControlStateValue>(Values)));
            Array.Clear(Values, 0, Values.Length);
            Items.PrepareRead(0);
            StateHash = default;
        }

        internal void SetValue(int index, CharacterControlStateValue value)
        {
            value.Retain();
            Values[index].Release();
            Values[index] = value;
        }

        internal void CopyValuesFrom(ReadOnlySpan<CharacterControlStateValue> values)
        {
            for (int index = 0; index < values.Length; index++)
                SetValue(index, values[index]);
        }

        public CharacterControlRuntimeState CopyFrom(CharacterControlRuntimeState state)
        {
            StableHash stateHash = state.StoredHash;
            CopyValuesFrom(state.ValueArray);
            return new CharacterControlRuntimeState(
                state.Schema,
                state.LastCompletedTick,
                this,
                stateHash);
        }

        internal void RequireGeneration(uint generation)
        {
            if (generation != Generation)
                throw new InvalidOperationException("Character Control state storage has already been reused.");
        }
    }

    public enum CharacterControlRuntimeStateTransactionStatus : byte
    {
        Active = 1,
        Aborted = 2
    }

    public sealed class CharacterControlRuntimeStateTransaction : IDisposable
    {
        CharacterControlStateSchema m_Schema;
        SimulationTick m_Tick;
        CharacterControlStateValue[] m_Values;
        CharacterControlStateValue[] m_MutableValues = Array.Empty<CharacterControlStateValue>();
        readonly CanonicalHashWriter m_Hash = new CanonicalHashWriter();
        CharacterControlRuntimeStateTransactionStatus m_Status;
        bool m_ValuesChanged;

        public CharacterControlRuntimeStateTransaction()
        {
            m_Status = CharacterControlRuntimeStateTransactionStatus.Aborted;
        }

        internal void PrepareValueCapacity(int fieldCount)
        {
            ClearMutableValues();
            m_MutableValues = new CharacterControlStateValue[fieldCount];
        }

        public CharacterControlRuntimeStateTransaction Restart(
            CharacterControlRuntimeState state,
            CharacterControlStateSchema schema,
            SimulationTick tick)
        {
            if (!state.IsValid)
                throw new ArgumentException("Character control runtime state is invalid.", nameof(state));
            if (schema == null)
                throw new ArgumentNullException(nameof(schema));
            if (!state.Schema.SchemaHash.Equals(schema.SchemaHash))
                throw new InvalidOperationException("Character control runtime state schema is stale.");
            if (state.ModuleId != schema.ModuleId || state.SemanticVersion != schema.SemanticVersion)
                throw new InvalidOperationException("Character control runtime state identity does not match its schema.");
            if (!tick.IsValid || tick.Value != checked(state.LastCompletedTick + 1))
                throw new ArgumentException("Character control runtime state transaction Tick is not the next Tick.", nameof(tick));
            ClearMutableValues();
            m_Schema = schema;
            m_Tick = tick;
            BaseState = state;
            m_Values = state.ValueArray;
            m_ValuesChanged = false;
            m_Status = CharacterControlRuntimeStateTransactionStatus.Active;
            return this;
        }

        public CharacterControlRuntimeStateTransactionStatus Status => m_Status;
        public CharacterControlRuntimeState BaseState { get; private set; }
        public SimulationTick Tick => m_Tick;

        public CharacterControlStateValue Get(CharacterControlStateFieldId field)
        {
            RequireActive();
            return m_Values[m_Schema.RequireIndex(field)];
        }

        public void Set(CharacterControlStateFieldId field, CharacterControlStateValue value)
        {
            RequireActive();
            int index = m_Schema.RequireIndex(field);
            if (value.Kind != m_Schema.Fields[index].ValueKind)
                throw new ArgumentException($"Character control state field '{field}' kind does not match its schema.", nameof(value));
            if (m_Values[index].Equals(value))
                return;
            MakeValuesMutable();
            value.Retain();
            m_Values[index].Release();
            m_Values[index] = value;
            m_ValuesChanged = true;
        }

        public CharacterControlRuntimeState Capture()
        {
            RequireActive();
            CharacterControlStateValue[] values;
            if (m_ValuesChanged || !BaseState.OwnsValueArray)
            {
                values = new CharacterControlStateValue[m_Values.Length];
                Array.Copy(m_Values, values, values.Length);
            }
            else
            {
                values = BaseState.ValueArray;
            }
            StableHash stateHash = CharacterControlRuntimeState.ComputeHash(m_Schema, m_Tick.Value, m_Values, m_Hash);
            return new CharacterControlRuntimeState(
                m_Schema,
                m_Tick.Value,
                values,
                stateHash);
        }

        public CharacterControlRuntimeState Capture(CharacterControlRuntimeStateStorage storage)
        {
            RequireActive();
            storage.CopyValuesFrom(m_Values);
            return new CharacterControlRuntimeState(
                m_Schema,
                m_Tick.Value,
                storage,
                default);
        }

        public void Restore(CharacterControlRuntimeState state)
        {
            RequireActive();
            if (!state.IsValid)
                throw new ArgumentException("Character control runtime state is invalid.", nameof(state));
            if (!state.Schema.SchemaHash.Equals(m_Schema.SchemaHash) ||
                state.LastCompletedTick != m_Tick.Value)
                throw new InvalidOperationException("Character control runtime state restore identity does not match the active transaction.");
            if (MatchesBaseValues(state.Values))
            {
                ClearMutableValues();
                m_Values = BaseState.ValueArray;
                m_ValuesChanged = false;
                return;
            }
            CopyMutableValues(state.ValueArray);
            m_Values = m_MutableValues;
            m_ValuesChanged = true;
        }

        public void Abort()
        {
            if (m_Status == CharacterControlRuntimeStateTransactionStatus.Active)
            {
                ClearMutableValues();
                m_Values = null;
                BaseState = default;
                m_Status = CharacterControlRuntimeStateTransactionStatus.Aborted;
            }
        }

        public void Dispose() => Abort();

        void RequireActive()
        {
            if (m_Status != CharacterControlRuntimeStateTransactionStatus.Active)
                throw new InvalidOperationException("Character control runtime state transaction is not active.");
        }

        bool MatchesBaseValues(IReadOnlyList<CharacterControlStateValue> values)
        {
            if (values.Count != BaseState.Values.Count)
                return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (!values[i].Equals(BaseState.Values[i]))
                    return false;
            }
            return true;
        }

        void CopyMutableValues(ReadOnlySpan<CharacterControlStateValue> values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                CharacterControlStateValue value = values[index];
                value.Retain();
                m_MutableValues[index].Release();
                m_MutableValues[index] = value;
            }
        }

        void ClearMutableValues()
        {
            for (int index = 0; index < m_MutableValues.Length; index++)
                m_MutableValues[index].Release();
            Array.Clear(m_MutableValues, 0, m_MutableValues.Length);
        }

        void MakeValuesMutable()
        {
            if (m_ValuesChanged)
                return;
            CopyMutableValues(BaseState.ValueArray);
            m_Values = m_MutableValues;
        }

    }

    public static class CharacterControlRuntimeStateCodec
    {
        const uint Magic = 0x54535243;
        const int Version = 8;
        public const string CodecIdentity = "character-control-runtime-state/v8";

        public static void WriteLengthPrefixed(CanonicalWriter writer, CharacterControlRuntimeState state)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (!state.IsValid)
                throw new ArgumentException("Character control runtime state is invalid.", nameof(state));
            long prefixPosition = writer.BeginLengthPrefixedBlock();
            WriteCanonical(writer, state);
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        public static CharacterControlRuntimeState Read(ArraySegment<byte> bytes)
        {
            return Read(bytes, (ICanonicalTextResolver)null);
        }

        public static CharacterControlRuntimeState Read(ArraySegment<byte> bytes, ICanonicalTextResolver resolver)
        {
            return Read(bytes, new CanonicalReader(Array.Empty<byte>()), resolver);
        }

        public static CharacterControlRuntimeState Read(ArraySegment<byte> bytes,
            CanonicalReader reader, ICanonicalTextResolver resolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            ReadHeader(reader);
            CharacterControlStateSchema schema = ReadSchema(reader);
            return ReadFrame(schema, new CharacterControlRuntimeStateStorage(schema.FieldCount), reader, false);
        }

        public static CharacterControlRuntimeState Read(
            ArraySegment<byte> bytes,
            CharacterControlStateSchema schema)
        {
            return Read(bytes, schema, new CanonicalReader(Array.Empty<byte>()), null);
        }

        public static CharacterControlRuntimeState Read(ArraySegment<byte> bytes, CharacterControlStateSchema schema,
            CanonicalReader reader, ICanonicalTextResolver resolver)
        {
            if (schema == null)
                throw new ArgumentNullException(nameof(schema));
            return Read(bytes, schema, new CharacterControlRuntimeStateStorage(schema.FieldCount), reader, resolver);
        }

        public static CharacterControlRuntimeState Read(ArraySegment<byte> bytes, CharacterControlStateSchema schema,
            CharacterControlRuntimeStateStorage storage, CanonicalReader reader,
            ICanonicalTextResolver resolver) => ReadCore(bytes, schema, storage, reader, resolver, true);

        static CharacterControlRuntimeState ReadCore(ArraySegment<byte> bytes, CharacterControlStateSchema schema,
            CharacterControlRuntimeStateStorage storage, CanonicalReader reader,
            ICanonicalTextResolver resolver, bool consumePublishedIdentities)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            ReadHeader(reader);
            CharacterControlSchemaDeclaration.CanonicalWrite(reader.MatchWriter, schema.Declaration);
            return ReadFrame(schema, storage, reader, consumePublishedIdentities);
        }

        static CharacterControlRuntimeState ReadFrame(CharacterControlStateSchema schema,
            CharacterControlRuntimeStateStorage storage, CanonicalReader reader, bool consumePublishedIdentities)
        {
            storage.Clear();
            CharacterControlStateFrame frame = CharacterControlStateFrame.CanonicalRead(reader, storage);
            if (frame.Items.Count != schema.FieldCount)
                throw new InvalidDataException("Character control runtime state values do not match its schema.");
            CharacterControlStateValue[] values = storage.Values;
            for (int i = 0; i < values.Length; i++)
            {
                CharacterControlStateFieldDescriptor field = schema.Fields[i];
                CharacterControlStateValue value = values[i];
                if (value.Kind != field.ValueKind)
                    throw new InvalidDataException("Character control runtime state field kind does not match its schema.");
                if (consumePublishedIdentities && field.ValueKind == CharacterControlStateValueKind.Identity &&
                    (field.Semantic == CharacterControlStateSemantic.ActiveState || field.Semantic == CharacterControlStateSemantic.Transition))
                    storage.SetValue(i, CharacterControlStateValue.FromIdentity(schema.ResolvePublishedIdentity(i, value.Identity)));
            }
            StableHash expectedHash = reader.ReadHash();
            reader.RequireComplete();
            StableHash hash = CharacterControlRuntimeState.ComputeHash(schema, frame.LastCompletedTick, values, storage.Hash);
            if (!hash.Equals(expectedHash))
                throw new InvalidDataException("Character control runtime state hash is invalid.");
            return new CharacterControlRuntimeState(schema, frame.LastCompletedTick, storage, hash);
        }

        static CharacterControlStateSchema ReadSchema(CanonicalReader reader)
        {
            CharacterControlSchemaDeclaration declaration = CharacterControlSchemaDeclaration.CanonicalReadMaterialized(reader);
            return new CharacterControlStateSchema(declaration.ModuleId, declaration.SemanticVersion, declaration.Fields);
        }

        static void ReadHeader(CanonicalReader reader)
        {
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Character control runtime state header is invalid.");
            CharacterControlSchemaDeclaration.CanonicalRequireSchema(reader);
            CharacterControlStateFrame.CanonicalRequireSchema(reader);
        }

        static void WriteCanonical(CanonicalWriter writer, CharacterControlRuntimeState state)
        {
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteHash(CharacterControlSchemaDeclaration.CanonicalSchemaHash);
            writer.WriteHash(CharacterControlStateFrame.CanonicalSchemaHash);
            CharacterControlSchemaDeclaration.CanonicalWrite(writer, state.Schema.Declaration);
            CharacterControlStateFrame.CanonicalWrite(writer, state.Frame);
            writer.WriteHash(state.StateHash);
        }

    }
}
