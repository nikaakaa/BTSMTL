using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationIdentityOrdinalView
    {
        public OrchestrationIdentityOrdinalView(ReadOnlyMemory<int> contentRanks,
            ReadOnlyMemory<int> rosterRanks, ReadOnlyMemory<int> runtimeTextRanks)
        {
            ContentRanks = contentRanks;
            RosterRanks = rosterRanks;
            RuntimeTextRanks = runtimeTextRanks;
        }

        public ReadOnlyMemory<int> ContentRanks { get; }
        public ReadOnlyMemory<int> RosterRanks { get; }
        public ReadOnlyMemory<int> RuntimeTextRanks { get; }

        public int Ordinal(in OrchestrationIdentityValue value) =>
            value.Space switch
            {
                OrchestrationIdentitySpace.Content => ContentRanks.Span[value.Index],
                OrchestrationIdentitySpace.Roster => RosterRanks.Span[value.Index],
                OrchestrationIdentitySpace.RuntimeText => RuntimeTextRanks.Span[value.Index],
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };

        public bool IsEmpty(in OrchestrationIdentityValue value) =>
            value.Space == OrchestrationIdentitySpace.Content && value.Index == 0;

        public bool Equal(in OrchestrationIdentityValue left, in OrchestrationIdentityValue right) =>
            Ordinal(in left) == Ordinal(in right);
    }
}
