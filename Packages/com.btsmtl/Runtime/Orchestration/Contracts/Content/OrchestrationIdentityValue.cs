using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationIdentitySpace : byte
    {
        Content = 0,
        Roster = 1,
        RuntimeText = 2
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationIdentityValue : IEquatable<OrchestrationIdentityValue>
    {
        public OrchestrationIdentityValue(OrchestrationIdentitySpace space, int index)
        {
            Space = space;
            Index = index;
        }

        public OrchestrationIdentitySpace Space { get; }
        public int Index { get; }

        [ValueDefault] public static OrchestrationIdentityValue Empty => default;
        public static OrchestrationIdentityValue Content(int sharedIdentity) =>
            new OrchestrationIdentityValue(OrchestrationIdentitySpace.Content, sharedIdentity);
        public static OrchestrationIdentityValue Roster(int rosterIndex) =>
            new OrchestrationIdentityValue(OrchestrationIdentitySpace.Roster, rosterIndex);
        public static OrchestrationIdentityValue RuntimeText(int textIndex) =>
            textIndex == 0 ? Empty : new OrchestrationIdentityValue(OrchestrationIdentitySpace.RuntimeText, textIndex);

        public bool Equals(OrchestrationIdentityValue other) => Space == other.Space && Index == other.Index;
        public override bool Equals(object obj) => obj is OrchestrationIdentityValue other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((byte)Space, Index);
    }
}
