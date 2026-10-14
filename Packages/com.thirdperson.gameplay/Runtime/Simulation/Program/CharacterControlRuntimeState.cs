using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    [ValueOperations(Resources = false)]
    public sealed partial class CharacterControlStateSchema : IEquatable<CharacterControlStateSchema>
    {
        [ValueEqualityIgnore]
        readonly Dictionary<CharacterControlStateFieldId, int> m_Indexes;
        [ValueEqualityIgnore]
        readonly CanonicalDeclaredIdentityReader m_StateIdentities;
        [ValueEqualityIgnore]
        readonly CanonicalDeclaredIdentityReader m_TransitionIdentities;

        public CharacterControlStateSchema(CharacterControlModuleContract contract)
        {
            m_Indexes = new Dictionary<CharacterControlStateFieldId, int>();
            for (int i = 0; i < contract.StateFields.Count; i++)
                m_Indexes.Add(contract.StateFields[i].Id, i);
            var states = new string[contract.States.Count];
            for (int index = 0; index < states.Length; index++)
                states[index] = contract.States[index].Id.Value;
            var transitions = new string[contract.Transitions.Count];
            for (int index = 0; index < transitions.Length; index++)
                transitions[index] = contract.Transitions[index].Id.Value;
            m_StateIdentities = new CanonicalDeclaredIdentityReader(states);
            m_TransitionIdentities = new CanonicalDeclaredIdentityReader(transitions);
            ModuleId = contract.ModuleId;
            SemanticVersion = contract.SemanticVersion;
            Fields = contract.StateFields;
        }


        public CharacterControlModuleId ModuleId { get; }
        public int SemanticVersion { get; }
        public IReadOnlyList<CharacterControlStateFieldDescriptor> Fields { get; }
        public int FieldCount => Fields.Count;

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

    }

    [ValueOperations]
    public readonly partial struct CharacterControlStateValue : IEquatable<CharacterControlStateValue>
    {
        [ValueUnionCase]
        public bool Boolean { get; }
        [ValueUnionCase]
        public int Int32 { get; }
        [ValueUnionCase]
        public ulong UInt64 { get; }
        [ValueUnionCase(NormalizeNull = true)]
        public CanonicalTextValue Identity { get; }
        [ValueUnionCase(NormalizeZero = true)]
        public double Number { get; }

        [ValueValidation]
        void RequireValid()
        {
            if (Kind == CharacterControlStateValueKind.Number && (double.IsNaN(Number) || double.IsInfinity(Number)))
                throw new ArgumentOutOfRangeException(nameof(Number));
        }


    }

    [ValueOperations]
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
            Retain();
        }

        public CharacterControlStateSchema Schema { get; }
        public CharacterControlModuleId ModuleId => Schema.ModuleId;
        public int SemanticVersion => Schema.SemanticVersion;
        public ulong LastCompletedTick => m_Frame.LastCompletedTick;
        public IReadOnlyList<CharacterControlStateValue> Values => ValueArray;
        public bool IsValid => Schema != null && m_Frame.Items.Array != null;

        internal CharacterControlStateFrame Frame
        {
            get
            {
                m_Storage?.RequireGeneration(m_StorageGeneration);
                return m_Frame;
            }
        }

        internal CharacterControlRuntimeState(
            CharacterControlStateSchema schema,
            ulong lastCompletedTick,
            CharacterControlStateValue[] values)
        {
            Schema = schema;
            m_Frame = new CharacterControlStateFrame(lastCompletedTick, new CanonicalArraySlice<CharacterControlStateValue>(values));
            m_Storage = null;
            m_StorageGeneration = 0;
            Retain();
        }

        internal CharacterControlRuntimeState(
            CharacterControlStateSchema schema,
            ulong lastCompletedTick,
            CharacterControlRuntimeStateStorage storage)
        {
            Schema = schema;
            m_Frame = new CharacterControlStateFrame(lastCompletedTick, new CanonicalArraySlice<CharacterControlStateValue>(storage.Values));
            m_Storage = storage;
            m_StorageGeneration = storage.Generation;
        }

        public void Retain() => Frame.Retain();

        public void Release() => Frame.Release();

        internal bool OwnsValueArray => m_Storage == null;

        internal CharacterControlStateValue[] ValueArray => Frame.Items.Array;

        public CharacterControlStateValue Get(CharacterControlStateFieldId field) =>
            ValueArray[Schema.RequireIndex(field)];

        public void RequireContract(CharacterControlModuleContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (!contract.StateSchema.Equals(Schema))
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
            Items = new CanonicalBuffer<CharacterControlStateValue>(fieldCount);

        internal CharacterControlStateValue[] Values => Items.OwnedArray;
        internal CanonicalBuffer<CharacterControlStateValue> Items { get; }
        internal uint Generation { get; private set; }

        public void BeginWrite()
        {
            Generation++;
        }

        public void Clear()
        {
            new CharacterControlStateFrame(0, new CanonicalArraySlice<CharacterControlStateValue>(Values)).Release();
            Array.Clear(Values, 0, Values.Length);
            Items.PrepareRead(0);
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
            CopyValuesFrom(state.ValueArray);
            return new CharacterControlRuntimeState(
                state.Schema,
                state.LastCompletedTick,
                this);
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
            if (!state.Schema.Equals(schema))
                throw new InvalidOperationException("Character control runtime state schema is stale.");
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
            return new CharacterControlRuntimeState(
                m_Schema,
                m_Tick.Value,
                values);
        }

        public CharacterControlRuntimeState Capture(CharacterControlRuntimeStateStorage storage)
        {
            RequireActive();
            storage.CopyValuesFrom(m_Values);
            return new CharacterControlRuntimeState(
                m_Schema,
                m_Tick.Value,
                storage);
        }

        public void Restore(CharacterControlRuntimeState state)
        {
            RequireActive();
            if (!state.IsValid)
                throw new ArgumentException("Character control runtime state is invalid.", nameof(state));
            if (!state.Schema.Equals(m_Schema) ||
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

}
