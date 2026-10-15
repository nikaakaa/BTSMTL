using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationFrameInput<TValue> where TValue : unmanaged
    {
        public OrchestrationFrameInput(ulong tick, ulong instanceGeneration, ReadOnlySpan<TValue> hostInputs,
            ReadOnlySpan<int> requestIdentityIds, bool captureTrace = false,
            bool captureValues = false, bool useOverrides = false,
            ReadOnlySpan<TValue> valueOverrides = default,
            ReadOnlySpan<TValue> inputOverrides = default,
            ReadOnlySpan<TValue> outputOverrides = default)
        {
            Tick = tick;
            InstanceGeneration = instanceGeneration;
            HostInputs = hostInputs;
            RequestIdentityIds = requestIdentityIds;
            CaptureTrace = captureTrace;
            CaptureValues = captureValues;
            UseOverrides = useOverrides;
            ValueOverrides = valueOverrides;
            InputOverrides = inputOverrides;
            OutputOverrides = outputOverrides;
        }

        public ulong Tick { get; }
        public ulong InstanceGeneration { get; }
        public ReadOnlySpan<TValue> HostInputs { get; }
        public ReadOnlySpan<int> RequestIdentityIds { get; }
        public bool CaptureTrace { get; }
        public bool CaptureValues { get; }
        public bool UseOverrides { get; }
        public ReadOnlySpan<TValue> ValueOverrides { get; }
        public ReadOnlySpan<TValue> InputOverrides { get; }
        public ReadOnlySpan<TValue> OutputOverrides { get; }
    }
}
