using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationIdentityOrdinalView
    {
        public OrchestrationIdentityOrdinalView(ReadOnlySpan<int> contentRanks,
            ReadOnlySpan<int> rosterRanks, ReadOnlySpan<int> runtimeTextRanks)
        {
            ContentRanks = contentRanks;
            RosterRanks = rosterRanks;
            RuntimeTextRanks = runtimeTextRanks;
        }

        public ReadOnlySpan<int> ContentRanks { get; }
        public ReadOnlySpan<int> RosterRanks { get; }
        public ReadOnlySpan<int> RuntimeTextRanks { get; }

        public int Ordinal(in OrchestrationIdentityValue value) =>
            value.Space switch
            {
                OrchestrationIdentitySpace.Null => -1,
                OrchestrationIdentitySpace.Content => ContentRanks[value.Index],
                OrchestrationIdentitySpace.Roster => RosterRanks[value.Index],
                OrchestrationIdentitySpace.RuntimeText => RuntimeTextRanks[value.Index],
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };

        public bool IsEmpty(in OrchestrationIdentityValue value) =>
            value.Space == OrchestrationIdentitySpace.Null ||
            value.Space == OrchestrationIdentitySpace.Content && value.Index == 0;

        public bool Equal(in OrchestrationIdentityValue left, in OrchestrationIdentityValue right) =>
            Ordinal(in left) == Ordinal(in right);
    }
}
