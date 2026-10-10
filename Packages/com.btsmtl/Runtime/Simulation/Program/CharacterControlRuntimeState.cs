using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation
{
    public sealed class CharacterControlStateSchema
    {
        readonly ReadOnlyCollection<CharacterControlStateFieldDescriptor> m_Fields;
        readonly Dictionary<CharacterControlStateFieldId, int> m_Indexes;
        readonly Dictionary<CharacterControlStateFieldId, CharacterControlStateValueKind> m_Kinds;
        readonly GameplayEffectPublishedIdentityReader m_StateIdentities;
        readonly GameplayEffectPublishedIdentityReader m_TransitionIdentities;

        public CharacterControlStateSchema(CharacterControlModuleContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            var fields = new List<CharacterControlStateFieldDescriptor>(contract.StateFields.Count);
            m_Indexes = new Dictionary<CharacterControlStateFieldId, int>();
            m_Kinds = new Dictionary<CharacterControlStateFieldId, CharacterControlStateValueKind>();
            var hashParts = new List<string>
            {
                "character-control-state-schema/3",
                contract.ModuleId.Value,
                contract.SemanticVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            for (int i = 0; i < contract.StateFields.Count; i++)
            {
                CharacterControlStateFieldDescriptor field = contract.StateFields[i];
                if (!IsSupported(field.ValueKind))
                    throw new InvalidDataException($"Character control state field '{field.Id}' has unsupported kind '{field.ValueKind}'.");
                fields.Add(field);
                if (!m_Indexes.TryAdd(field.Id, i))
                    throw new InvalidDataException($"Character control state field '{field.Id}' is duplicated.");
                m_Kinds.Add(field.Id, field.ValueKind);
                hashParts.Add(field.Id.Value);
                hashParts.Add(((int)field.ValueKind).ToString(System.Globalization.CultureInfo.InvariantCulture));
                hashParts.Add(((int)field.Semantic).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            m_Fields = fields.AsReadOnly();
            var states = new string[contract.States.Count];
            for (int index = 0; index < states.Length; index++)
                states[index] = contract.States[index].Id.Value;
            var transitions = new string[contract.Transitions.Count];
            for (int index = 0; index < transitions.Length; index++)
                transitions[index] = contract.Transitions[index].Id.Value;
            m_StateIdentities = new GameplayEffectPublishedIdentityReader(states);
            m_TransitionIdentities = new GameplayEffectPublishedIdentityReader(transitions);
            ModuleId = contract.ModuleId;
            SemanticVersion = contract.SemanticVersion;
            SchemaHash = StableHash.Compute(hashParts.ToArray());
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
            m_Kinds = new Dictionary<CharacterControlStateFieldId, CharacterControlStateValueKind>();
            var hashParts = new List<string>
            {
                "character-control-state-schema/3",
                moduleId.Value,
                semanticVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            for (int i = 0; i < fields.Count; i++)
            {
                CharacterControlStateFieldDescriptor field = fields[i]
                    ?? throw new ArgumentException("Character control state schema contains a missing field.", nameof(fields));
                if (!IsSupported(field.ValueKind))
                    throw new InvalidDataException($"Character control state field '{field.Id}' has unsupported kind '{field.ValueKind}'.");
                copied.Add(field);
                if (!m_Indexes.TryAdd(field.Id, i))
                    throw new InvalidDataException($"Character control state field '{field.Id}' is duplicated.");
                m_Kinds.Add(field.Id, field.ValueKind);
                hashParts.Add(field.Id.Value);
                hashParts.Add(((int)field.ValueKind).ToString(System.Globalization.CultureInfo.InvariantCulture));
                hashParts.Add(((int)field.Semantic).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            m_Fields = copied.AsReadOnly();
            ModuleId = moduleId;
            SemanticVersion = semanticVersion;
            SchemaHash = StableHash.Compute(hashParts.ToArray());
        }

        public CharacterControlModuleId ModuleId { get; }
        public int SemanticVersion { get; }
        public IReadOnlyList<CharacterControlStateFieldDescriptor> Fields => m_Fields;
        public int FieldCount => m_Fields.Count;
        public StableHash SchemaHash { get; }

        public int RequireIndex(CharacterControlStateFieldId field) =>
            m_Indexes.TryGetValue(field, out int index)
                ? index
                : throw new InvalidOperationException($"Character control state field '{field}' is not declared.");

        public CharacterControlStateValueKind RequireKind(CharacterControlStateFieldId field) =>
            m_Kinds.TryGetValue(field, out CharacterControlStateValueKind kind)
                ? kind
                : throw new InvalidOperationException($"Character control state field '{field}' is not declared.");

        public string ResolvePublishedIdentity(int fieldIndex, in CanonicalTextValue value) =>
            IdentityDirectory(fieldIndex).ResolvePublished(in value);

        internal CanonicalTextValue ReadPublishedIdentity(CanonicalReader reader, int fieldIndex) =>
            CanonicalTextValue.FromString(IdentityDirectory(fieldIndex).ReadPublished(reader));

        GameplayEffectPublishedIdentityReader IdentityDirectory(int fieldIndex) =>
            m_Fields[fieldIndex].Semantic switch
            {
                CharacterControlStateSemantic.ActiveState => m_StateIdentities,
                CharacterControlStateSemantic.Transition => m_TransitionIdentities,
                _ => throw new InvalidOperationException("Control field is an open identity value.")
            };

        static bool IsSupported(CharacterControlStateValueKind kind) =>
            kind == CharacterControlStateValueKind.Identity ||
            kind == CharacterControlStateValueKind.Boolean ||
            kind == CharacterControlStateValueKind.Int32 ||
            kind == CharacterControlStateValueKind.UInt64 ||
            kind == CharacterControlStateValueKind.Number;
    }

    public readonly struct CharacterControlStateValue
    {
        CharacterControlStateValue(
            CharacterControlStateValueKind kind,
            bool boolean,
            int int32,
            ulong uint64,
            CanonicalTextValue identity)
        {
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            UInt64 = uint64;
            Identity = identity.IsNull ? CanonicalTextValue.Empty : identity;
        }

        public CharacterControlStateValueKind Kind { get; }
        public bool Boolean { get; }
        public int Int32 { get; }
        public ulong UInt64 { get; }
        public double Number => BitConverter.Int64BitsToDouble(unchecked((long)UInt64));
        public CanonicalTextValue Identity { get; }

        public bool Equals(CharacterControlStateValue other) =>
            Kind == other.Kind &&
            Boolean == other.Boolean &&
            Int32 == other.Int32 &&
            UInt64 == other.UInt64 &&
            (Kind != CharacterControlStateValueKind.Identity ||
                Identity.Equals(other.Identity));

        public void Retain() => Identity.Retain();
        public void Release() => Identity.Release();

        public static CharacterControlStateValue FromBoolean(bool value) =>
            new CharacterControlStateValue(CharacterControlStateValueKind.Boolean, value, 0, 0, string.Empty);

        public static CharacterControlStateValue FromInt32(int value) =>
            new CharacterControlStateValue(CharacterControlStateValueKind.Int32, false, value, 0, string.Empty);

        public static CharacterControlStateValue FromUInt64(ulong value) =>
            new CharacterControlStateValue(CharacterControlStateValueKind.UInt64, false, 0, value, string.Empty);

        public static CharacterControlStateValue FromIdentity(CanonicalTextValue value) =>
            new CharacterControlStateValue(CharacterControlStateValueKind.Identity, false, 0, 0, value);

        public static CharacterControlStateValue FromNumber(double value) =>
            new CharacterControlStateValue(CharacterControlStateValueKind.Number, false, 0,
                unchecked((ulong)BitConverter.DoubleToInt64Bits(value == 0d ? 0d : value)), string.Empty);

        public static CharacterControlStateValue Default(CharacterControlStateValueKind kind)
        {
            return kind switch
            {
                CharacterControlStateValueKind.Boolean => FromBoolean(false),
                CharacterControlStateValueKind.Int32 => FromInt32(0),
                CharacterControlStateValueKind.UInt64 => FromUInt64(0),
                CharacterControlStateValueKind.Identity => FromIdentity(string.Empty),
                CharacterControlStateValueKind.Number => FromNumber(0d),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }
    }

    public readonly struct CharacterControlRuntimeState
    {
        readonly CharacterControlStateValue[] m_ValueArray;
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
            ModuleId = schema.ModuleId;
            SemanticVersion = schema.SemanticVersion;
            LastCompletedTick = lastCompletedTick;
            m_ValueArray = copied;
            m_Storage = null;
            m_StorageGeneration = 0;
            m_StateHash = ComputeHash(schema, lastCompletedTick, copied, new SimulationCanonicalPayloadHash.Accumulator());
            Retain();
        }

        public CharacterControlStateSchema Schema { get; }
        public CharacterControlModuleId ModuleId { get; }
        public int SemanticVersion { get; }
        public ulong LastCompletedTick { get; }
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
        public bool IsValid => Schema != null && m_ValueArray != null;

        internal static StableHash ComputeHash(
            CharacterControlStateSchema schema,
            ulong lastCompletedTick,
            ReadOnlySpan<CharacterControlStateValue> values,
            SimulationCanonicalPayloadHash.Accumulator hash)
        {
            hash.Begin();
            hash.AppendText("character-control-runtime-state/3");
            hash.AppendAscii(0x1f);
            hash.AppendHash(schema.SchemaHash);
            hash.AppendAscii(0x1f);
            hash.AppendUInt64(lastCompletedTick);
            for (int index = 0; index < values.Length; index++)
            {
                CharacterControlStateValue value = values[index];
                hash.AppendAscii(0x1f);
                hash.AppendInt32((int)value.Kind);
                hash.AppendAscii(0x1f);
                hash.AppendAscii(value.Boolean ? (byte)'1' : (byte)'0');
                hash.AppendAscii(0x1f);
                hash.AppendInt32(value.Int32);
                hash.AppendAscii(0x1f);
                hash.AppendUInt64(value.UInt64);
                hash.AppendAscii(0x1f);
                hash.AppendText(value.Identity);
            }
            return hash.Complete();
        }

        internal CharacterControlRuntimeState(
            CharacterControlStateSchema schema,
            ulong lastCompletedTick,
            CharacterControlStateValue[] values,
            StableHash stateHash)
        {
            Schema = schema;
            ModuleId = schema.ModuleId;
            SemanticVersion = schema.SemanticVersion;
            LastCompletedTick = lastCompletedTick;
            m_ValueArray = values;
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
            ModuleId = schema.ModuleId;
            SemanticVersion = schema.SemanticVersion;
            LastCompletedTick = lastCompletedTick;
            m_ValueArray = storage.Values;
            m_Storage = storage;
            m_StorageGeneration = storage.Generation;
            m_StateHash = default;
            storage.StateHash = stateHash;
        }

        public void Retain()
        {
            CharacterControlStateValue[] values = ValueArray;
            for (int index = 0; index < values.Length; index++)
                values[index].Retain();
        }

        public void Release()
        {
            CharacterControlStateValue[] values = ValueArray;
            for (int index = 0; index < values.Length; index++)
                values[index].Release();
        }

        internal bool OwnsValueArray => m_Storage == null;

        internal CharacterControlStateValue[] ValueArray
        {
            get
            {
                m_Storage?.RequireGeneration(m_StorageGeneration);
                return m_ValueArray;
            }
        }

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
                    : CharacterControlStateValue.Default(schema.Fields[i].ValueKind);
            return new CharacterControlRuntimeState(schema, 0, values);
        }

    }

    public sealed class CharacterControlRuntimeStateStorage
    {
        public CharacterControlRuntimeStateStorage(int fieldCount) =>
            Values = new CharacterControlStateValue[fieldCount];

        internal CharacterControlStateValue[] Values { get; }
        internal SimulationCanonicalPayloadHash.Accumulator Hash { get; } = new SimulationCanonicalPayloadHash.Accumulator();
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
            for (int index = 0; index < Values.Length; index++)
                Values[index].Release();
            Array.Clear(Values, 0, Values.Length);
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
        readonly SimulationCanonicalPayloadHash.Accumulator m_Hash = new SimulationCanonicalPayloadHash.Accumulator();
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
        const int Version = 5;
        public const string CodecIdentity = "character-control-runtime-state/v5";

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
            return Read(bytes, new CanonicalReader(Array.Empty<byte>()), new CanonicalWriter(), resolver);
        }

        public static CharacterControlRuntimeState Read(ArraySegment<byte> bytes,
            CanonicalReader reader, CanonicalWriter writer, ICanonicalTextResolver resolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            CharacterControlStateSchema schema = ReadSchema(reader);
            return ReadCore(bytes, schema, new CharacterControlRuntimeStateStorage(schema.FieldCount), reader, writer,
                resolver, false);
        }

        public static CharacterControlRuntimeState Read(
            ArraySegment<byte> bytes,
            CharacterControlStateSchema schema)
        {
            return Read(bytes, schema, new CanonicalReader(Array.Empty<byte>()), new CanonicalWriter(), null);
        }

        public static CharacterControlRuntimeState Read(ArraySegment<byte> bytes, CharacterControlStateSchema schema,
            CanonicalReader reader, CanonicalWriter writer, ICanonicalTextResolver resolver)
        {
            if (schema == null)
                throw new ArgumentNullException(nameof(schema));
            return Read(bytes, schema, new CharacterControlRuntimeStateStorage(schema.FieldCount), reader, writer, resolver);
        }

        public static CharacterControlRuntimeState Read(ArraySegment<byte> bytes, CharacterControlStateSchema schema,
            CharacterControlRuntimeStateStorage storage, CanonicalReader reader, CanonicalWriter writer,
            ICanonicalTextResolver resolver) => ReadCore(bytes, schema, storage, reader, writer, resolver, true);

        static CharacterControlRuntimeState ReadCore(ArraySegment<byte> bytes, CharacterControlStateSchema schema,
            CharacterControlRuntimeStateStorage storage, CanonicalReader reader, CanonicalWriter writer,
            ICanonicalTextResolver resolver, bool consumePublishedIdentities)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Character control runtime state header is invalid.");
            reader.RequireText(CodecIdentity);
            reader.RequireText(schema.ModuleId.Value);
            int semanticVersion = reader.ReadInt32();
            StableHash schemaHash = reader.ReadHash();
            ulong lastCompletedTick = reader.ReadUInt64();
            int count = reader.ReadInt32();
            if (semanticVersion != schema.SemanticVersion ||
                !schemaHash.Equals(schema.SchemaHash) || count < 0 || count != schema.FieldCount)
                throw new InvalidDataException("Character control runtime state schema binding is stale or mismatched.");
            CharacterControlStateValue[] values = storage.Values;
            for (int i = 0; i < values.Length; i++)
            {
                reader.RequireText(schema.Fields[i].Id.Value);
                CharacterControlStateValueKind kind = ReadValueKind(reader.ReadByte());
                CharacterControlStateSemantic semantic = ReadSemantic(reader.ReadUInt16());
                if (kind != schema.Fields[i].ValueKind || semantic != schema.Fields[i].Semantic)
                    throw new InvalidDataException($"Character control runtime state field '{schema.Fields[i].Id}' does not match schema index '{i}'.");
                CharacterControlStateValue value = consumePublishedIdentities &&
                    kind == CharacterControlStateValueKind.Identity &&
                    (semantic == CharacterControlStateSemantic.ActiveState || semantic == CharacterControlStateSemantic.Transition)
                    ? CharacterControlStateValue.FromIdentity(schema.ReadPublishedIdentity(reader, i))
                    : ReadValue(reader, kind);
                storage.SetValue(i, value);
            }
            StableHash expectedHash = reader.ReadHash();
            reader.RequireComplete();
            StableHash hash = CharacterControlRuntimeState.ComputeHash(schema, lastCompletedTick, values, storage.Hash);
            var result = new CharacterControlRuntimeState(schema, lastCompletedTick, storage, hash);
            if (!result.StateHash.Equals(expectedHash))
                throw new InvalidDataException("Character control runtime state hash is invalid.");
            writer.SetTextCapture(null);
            writer.Reset();
            WriteCanonical(writer, result);
            if (!writer.ContentEquals(bytes.AsSpan()))
                throw new InvalidDataException("Character control runtime state is not canonical.");
            return result;
        }

        static CharacterControlStateSchema ReadSchema(CanonicalReader reader)
        {
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Character control runtime state header is invalid.");
            reader.RequireText(CodecIdentity);
            var moduleId = new CharacterControlModuleId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            int semanticVersion = reader.ReadInt32();
            StableHash schemaHash = reader.ReadHash();
            reader.ReadUInt64();
            int count = reader.ReadInt32();
            if (count < 0 || count > 1024)
                throw new InvalidDataException($"Character control runtime state field count '{count}' is invalid.");
            var fields = new CharacterControlStateFieldDescriptor[count];
            for (int i = 0; i < fields.Length; i++)
            {
                var fieldId = new CharacterControlStateFieldId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                CharacterControlStateValueKind kind = ReadValueKind(reader.ReadByte());
                CharacterControlStateSemantic semantic = ReadSemantic(reader.ReadUInt16());
                if (kind == CharacterControlStateValueKind.Identity)
                    reader.SkipText();
                else
                    ReadValue(reader, kind);
                fields[i] = new CharacterControlStateFieldDescriptor(fieldId, kind, semantic);
            }
            reader.ReadHash();
            reader.RequireComplete();
            var schema = new CharacterControlStateSchema(moduleId, semanticVersion, fields);
            if (!schema.SchemaHash.Equals(schemaHash))
                throw new InvalidDataException("Character control runtime state schema hash is invalid.");
            return schema;
        }

        static void WriteCanonical(CanonicalWriter writer, CharacterControlRuntimeState state)
        {
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteText(CodecIdentity);
            writer.WriteText(state.ModuleId.Value);
            writer.WriteInt32(state.SemanticVersion);
            writer.WriteHash(state.Schema.SchemaHash);
            writer.WriteUInt64(state.LastCompletedTick);
            writer.WriteInt32(state.Values.Count);
            for (int i = 0; i < state.Values.Count; i++)
            {
                CharacterControlStateValue value = state.Values[i];
                writer.WriteText(state.Schema.Fields[i].Id.Value);
                writer.WriteByte((byte)value.Kind);
                writer.WriteUInt16((ushort)state.Schema.Fields[i].Semantic);
                WriteValue(writer, value);
            }
            writer.WriteHash(state.StateHash);
        }

        static void WriteValue(CanonicalWriter writer, CharacterControlStateValue value)
        {
            switch (value.Kind)
            {
                case CharacterControlStateValueKind.Boolean: writer.WriteBoolean(value.Boolean); break;
                case CharacterControlStateValueKind.Int32: writer.WriteInt32(value.Int32); break;
                case CharacterControlStateValueKind.UInt64: writer.WriteUInt64(value.UInt64); break;
                case CharacterControlStateValueKind.Identity: writer.WriteText(value.Identity); break;
                case CharacterControlStateValueKind.Number: writer.WriteUInt64(value.UInt64); break;
                default: throw new InvalidDataException($"Unsupported character control runtime state value kind '{value.Kind}'.");
            }
        }

        static CharacterControlStateValue ReadValue(CanonicalReader reader, CharacterControlStateValueKind kind)
        {
            return kind switch
            {
                CharacterControlStateValueKind.Boolean => CharacterControlStateValue.FromBoolean(reader.ReadBoolean()),
                CharacterControlStateValueKind.Int32 => CharacterControlStateValue.FromInt32(reader.ReadInt32()),
                CharacterControlStateValueKind.UInt64 => CharacterControlStateValue.FromUInt64(reader.ReadUInt64()),
                CharacterControlStateValueKind.Identity => CharacterControlStateValue.FromIdentity(reader.ReadText()),
                CharacterControlStateValueKind.Number => CharacterControlStateValue.FromNumber(reader.ReadDouble()),
                _ => throw new InvalidDataException($"Unsupported character control runtime state value kind '{kind}'.")
            };
        }

        static CharacterControlStateValueKind ReadValueKind(byte value)
        {
            var result = (CharacterControlStateValueKind)value;
            if (result is not (CharacterControlStateValueKind.Boolean or CharacterControlStateValueKind.Int32 or
                CharacterControlStateValueKind.UInt64 or CharacterControlStateValueKind.Identity or CharacterControlStateValueKind.Number))
                throw new InvalidDataException($"Character control runtime state control state value kind '{value}' is invalid.");
            return result;
        }

        static CharacterControlStateSemantic ReadSemantic(ushort value)
        {
            var result = (CharacterControlStateSemantic)value;
            if (result is not (CharacterControlStateSemantic.ActiveState or CharacterControlStateSemantic.EnteredTick or
                CharacterControlStateSemantic.Transition or CharacterControlStateSemantic.StateValue))
                throw new InvalidDataException($"Character control runtime state control state semantic '{value}' is invalid.");
            return result;
        }
    }
}
