using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationFrameInput<TValue> where TValue : unmanaged
    {
        public OrchestrationFrameInput(ulong tick, ulong instanceGeneration, ReadOnlySpan<TValue> hostInputs,
            ReadOnlySpan<int> requestIdentityIds, bool captureTrace = false,
            bool captureValues = false,
            OrchestrationValueOverrides<TValue> overrides = default,
            OrchestrationTraceObjectIdentity executionObject = default, Guid characterRuntimeId = default)
        {
            Tick = tick;
            InstanceGeneration = instanceGeneration;
            HostInputs = hostInputs;
            RequestIdentityIds = requestIdentityIds;
            CaptureTrace = captureTrace;
            CaptureValues = captureValues;
            Overrides = overrides;
            ExecutionObject = executionObject;
            CharacterRuntimeId = characterRuntimeId;
        }

        public ulong Tick { get; }
        public ulong InstanceGeneration { get; }
        public ReadOnlySpan<TValue> HostInputs { get; }
        public ReadOnlySpan<int> RequestIdentityIds { get; }
        public bool CaptureTrace { get; }
        public bool CaptureValues { get; }
        public OrchestrationValueOverrides<TValue> Overrides { get; }
        public OrchestrationTraceObjectIdentity ExecutionObject { get; }
        public Guid CharacterRuntimeId { get; }
        public bool HasDiagnostics => CaptureTrace || CaptureValues || !Overrides.IsEmpty;
    }

    public interface IOrchestrationValueOverrideStorage<TValue> where TValue : unmanaged
    {
        ref OrchestrationValueOverride<TValue> ValueOverride(int storage, int index);
    }

    public readonly ref struct OrchestrationValueOverrides<TValue> where TValue : unmanaged
    {
        readonly Span<OrchestrationValueOverride<TValue>> m_Values;
        readonly IOrchestrationValueOverrideStorage<TValue> m_Storage;
        readonly int m_StorageIndex;

        public OrchestrationValueOverrides(Span<OrchestrationValueOverride<TValue>> values)
        {
            m_Values = values;
            m_Storage = null;
            m_StorageIndex = 0;
            Length = values.Length;
        }

        public OrchestrationValueOverrides(IOrchestrationValueOverrideStorage<TValue> storage,
            int storageIndex, int length)
        {
            m_Values = default;
            m_Storage = storage;
            m_StorageIndex = storageIndex;
            Length = length;
        }

        public int Length { get; }
        public bool IsEmpty => Length == 0;

        public ref OrchestrationValueOverride<TValue> this[int index]
        {
            get
            {
                if (m_Storage != null)
                    return ref m_Storage.ValueOverride(m_StorageIndex, index);
                return ref m_Values[index];
            }
        }

        public static implicit operator OrchestrationValueOverrides<TValue>(
            Span<OrchestrationValueOverride<TValue>> values) => new OrchestrationValueOverrides<TValue>(values);

        public static implicit operator OrchestrationValueOverrides<TValue>(
            OrchestrationValueOverride<TValue>[] values) => new OrchestrationValueOverrides<TValue>(values);

        public Enumerator GetEnumerator() => new Enumerator(this);

        public ref struct Enumerator
        {
            readonly OrchestrationValueOverrides<TValue> m_Values;
            int m_Index;

            internal Enumerator(OrchestrationValueOverrides<TValue> values)
            {
                m_Values = values;
                m_Index = -1;
            }

            public bool MoveNext() => ++m_Index < m_Values.Length;
            public ref OrchestrationValueOverride<TValue> Current => ref m_Values[m_Index];
        }
    }

    public interface IOrchestrationValueObservationSink<TValue> where TValue : unmanaged
    {
        // context 与 loopScopes 仅在本次同步回调内借用；接收方在返回前取得所需数据的所有权。
        void Observe(in OrchestrationValueOverrideAddress address, in TValue value,
            in OrchestrationTraceContext context, ReadOnlySpan<OrchestrationTraceLoopScope> loopScopes);
    }

    public enum OrchestrationValueOverrideKind : byte
    {
        Blackboard = 1,
        Input = 2,
        Output = 3
    }

    public enum OrchestrationValueOverrideMode : byte
    {
        None,
        Once,
        Locked
    }

    public enum OrchestrationValueMutation : byte
    {
        Set,
        Lock,
        Release
    }

    public enum OrchestrationValueOverrideStatus : byte
    {
        Pending,
        Consumed,
        OwnerRetired
    }

    public struct OrchestrationValueOverrideAddress
    {
        public OrchestrationValueOverrideKind Kind;
        public int Slot;
        public OrchestrationExecutionOwnerToken Owner;

        public bool Equals(in OrchestrationValueOverrideAddress other) =>
            Kind == other.Kind && Slot == other.Slot && Owner.Equals(in other.Owner);
    }

    public struct OrchestrationValueOverride<TValue> where TValue : unmanaged
    {
        public OrchestrationValueOverrideAddress Address;
        public TValue Value;
        public OrchestrationValueOverrideMode Mode;
        public OrchestrationValueOverrideStatus Status;

        public static int Find(OrchestrationValueOverrides<TValue> overrides,
            in OrchestrationValueOverrideAddress address)
        {
            for (int index = 0; index < overrides.Length; index++)
            {
                ref readonly OrchestrationValueOverride<TValue> value = ref overrides[index];
                if (value.Mode != OrchestrationValueOverrideMode.None &&
                    value.Status != OrchestrationValueOverrideStatus.OwnerRetired &&
                    value.Address.Equals(in address))
                    return index;
            }
            return -1;
        }

        public static void Set(OrchestrationValueOverrides<TValue> overrides,
            in OrchestrationValueOverrideAddress address, in TValue value,
            OrchestrationValueOverrideMode mode)
        {
            int index = Find(overrides, in address);
            if (index < 0)
                for (int candidate = 0; candidate < overrides.Length; candidate++)
                {
                    ref readonly OrchestrationValueOverride<TValue> current = ref overrides[candidate];
                    if (current.Mode == OrchestrationValueOverrideMode.None ||
                        current.Status == OrchestrationValueOverrideStatus.OwnerRetired)
                    {
                        index = candidate;
                        break;
                    }
                }
            if (index < 0)
                throw new InvalidOperationException("The prepared scoped value-override capacity is exhausted.");
            overrides[index] = new OrchestrationValueOverride<TValue>
            {
                Address = address,
                Value = value,
                Mode = mode
            };
        }

        public static void Release(OrchestrationValueOverrides<TValue> overrides,
            in OrchestrationValueOverrideAddress address)
        {
            int index = Find(overrides, in address);
            if (index >= 0)
                overrides[index] = default;
        }

        public static void RetireExecutionFrame(OrchestrationValueOverrides<TValue> overrides,
            in OrchestrationExecutionFrameState frame)
        {
            for (int index = 0; index < overrides.Length; index++)
            {
                ref OrchestrationValueOverride<TValue> value = ref overrides[index];
                if (value.Mode != OrchestrationValueOverrideMode.None && value.Address.Owner.MatchesFrame(in frame))
                    value.Retire();
            }
        }

        public void Retire()
        {
            if (Mode != OrchestrationValueOverrideMode.Once || Status != OrchestrationValueOverrideStatus.Consumed)
                Status = OrchestrationValueOverrideStatus.OwnerRetired;
        }

        // 候选成功采用后调用；未被正式求值消费的 Set 继续等待。
        public static void CompleteConsumption(OrchestrationValueOverrides<TValue> overrides)
        {
            for (int index = 0; index < overrides.Length; index++)
            {
                ref OrchestrationValueOverride<TValue> value = ref overrides[index];
                if (value.Status == OrchestrationValueOverrideStatus.OwnerRetired ||
                    value.Mode == OrchestrationValueOverrideMode.Once && value.Status == OrchestrationValueOverrideStatus.Consumed)
                    value = default;
                else
                    value.Status = OrchestrationValueOverrideStatus.Pending;
            }
        }
    }
}
