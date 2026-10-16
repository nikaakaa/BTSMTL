using System;

namespace ThirdPersonSimulation
{
    [Flags]
    public enum WorldCapability : ulong
    {
        None = 0,
        BodyMotion = 1UL << 0,
        Grounding = 1UL << 1,
        Collision = 1UL << 2,
        Reconstructible = 1UL << 3,
        Snapshotable = 1UL << 4,
        DeterministicReplay = 1UL << 5,
        AirborneVerticalMotion = 1UL << 6,
        FrameOwned = 1UL << 7
    }

    public enum ActionTargetRequirement : byte
    {
        None = 0,
        SnapshotRequired = 1,
        OptionalSnapshot = 2
    }

}
