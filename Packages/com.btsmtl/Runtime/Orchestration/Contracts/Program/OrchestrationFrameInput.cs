using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationFrameInput<TValue> where TValue : unmanaged
    {
        public OrchestrationFrameInput(ulong tick, ulong instanceGeneration, ReadOnlySpan<TValue> hostInputs,
            ReadOnlySpan<int> requestIdentityIds, bool captureTrace = false,
            bool captureValues = false,
            Span<OrchestrationValueOverride<TValue>> overrides = default,
            IOrchestrationValueObservationSink<TValue> observations = null)
        {
            Tick = tick;
            InstanceGeneration = instanceGeneration;
            HostInputs = hostInputs;
            RequestIdentityIds = requestIdentityIds;
            CaptureTrace = captureTrace;
            CaptureValues = captureValues;
            Overrides = overrides;
            Observations = observations;
        }

        public ulong Tick { get; }
        public ulong InstanceGeneration { get; }
        public ReadOnlySpan<TValue> HostInputs { get; }
        public ReadOnlySpan<int> RequestIdentityIds { get; }
        public bool CaptureTrace { get; }
        public bool CaptureValues { get; }
        public Span<OrchestrationValueOverride<TValue>> Overrides { get; }
        public IOrchestrationValueObservationSink<TValue> Observations { get; }
    }

    public interface IOrchestrationValueObservationSink<TValue> where TValue : unmanaged
    {
        void Observe(in OrchestrationValueOverrideAddress address, in TValue value);
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

        public static int Find(ReadOnlySpan<OrchestrationValueOverride<TValue>> overrides,
            in OrchestrationValueOverrideAddress address)
        {
            for (int index = 0; index < overrides.Length; index++)
                if (overrides[index].Mode != OrchestrationValueOverrideMode.None &&
                    overrides[index].Status != OrchestrationValueOverrideStatus.OwnerRetired &&
                    overrides[index].Address.Equals(in address))
                    return index;
            return -1;
        }

        public static void Set(Span<OrchestrationValueOverride<TValue>> overrides,
            in OrchestrationValueOverrideAddress address, in TValue value,
            OrchestrationValueOverrideMode mode)
        {
            int index = Find(overrides, in address);
            if (index < 0)
                for (int candidate = 0; candidate < overrides.Length; candidate++)
                    if (overrides[candidate].Mode == OrchestrationValueOverrideMode.None ||
                        overrides[candidate].Status == OrchestrationValueOverrideStatus.OwnerRetired)
                    {
                        index = candidate;
                        break;
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

        public static void Release(Span<OrchestrationValueOverride<TValue>> overrides,
            in OrchestrationValueOverrideAddress address)
        {
            int index = Find(overrides, in address);
            if (index >= 0)
                overrides[index] = default;
        }

        public static void RetireExecutionFrame(Span<OrchestrationValueOverride<TValue>> overrides,
            in OrchestrationExecutionFrameState frame)
        {
            for (int index = 0; index < overrides.Length; index++)
                if (overrides[index].Mode != OrchestrationValueOverrideMode.None &&
                    overrides[index].Address.Owner.MatchesFrame(in frame))
                    overrides[index].Retire();
        }

        public void Retire()
        {
            if (Mode != OrchestrationValueOverrideMode.Once || Status != OrchestrationValueOverrideStatus.Consumed)
                Status = OrchestrationValueOverrideStatus.OwnerRetired;
        }

        // 候选成功采用后调用；未被正式求值消费的 Set 继续等待。
        public static void CompleteConsumption(Span<OrchestrationValueOverride<TValue>> overrides)
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
