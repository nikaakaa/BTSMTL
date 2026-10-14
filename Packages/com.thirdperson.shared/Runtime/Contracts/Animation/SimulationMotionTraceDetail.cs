namespace ThirdPersonSimulation
{
    public enum SimulationMotionTraceDetailKind : byte
    {
        None,
        Contribution,
        Channel,
        Resolved
    }

    public readonly struct SimulationMotionTraceDetail
    {
        public SimulationMotionTraceDetail(
            SimulationMotionTraceDetailKind kind,
            long displacementXRaw,
            long displacementYRaw,
            long displacementZRaw,
            long yawDegreesRaw,
            in CommittedMovementPlaybackClock movementClock,
            int channel = 0,
            int blendMode = 0,
            int priority = 0,
            long weightRaw = 0,
            long planarBasisXRaw = 0,
            long planarBasisYRaw = 0,
            bool claimsLowerChannels = false,
            bool hasMotion = false,
            int participatingSourceCount = 0,
            ulong participatingSourceFingerprint = 0)
        {
            Kind = kind;
            DisplacementXRaw = displacementXRaw;
            DisplacementYRaw = displacementYRaw;
            DisplacementZRaw = displacementZRaw;
            YawDegreesRaw = yawDegreesRaw;
            MovementClock = movementClock;
            Channel = channel;
            BlendMode = blendMode;
            Priority = priority;
            WeightRaw = weightRaw;
            PlanarBasisXRaw = planarBasisXRaw;
            PlanarBasisYRaw = planarBasisYRaw;
            ClaimsLowerChannels = claimsLowerChannels;
            HasMotion = hasMotion;
            ParticipatingSourceCount = participatingSourceCount;
            ParticipatingSourceFingerprint = participatingSourceFingerprint;
        }

        public readonly SimulationMotionTraceDetailKind Kind;
        public readonly bool ClaimsLowerChannels;
        public readonly bool HasMotion;
        public readonly int Channel;
        public readonly int BlendMode;
        public readonly int Priority;
        public readonly int ParticipatingSourceCount;
        public readonly long DisplacementXRaw;
        public readonly long DisplacementYRaw;
        public readonly long DisplacementZRaw;
        public readonly long YawDegreesRaw;
        public readonly long WeightRaw;
        public readonly long PlanarBasisXRaw;
        public readonly long PlanarBasisYRaw;
        public readonly ulong ParticipatingSourceFingerprint;
        public readonly CommittedMovementPlaybackClock MovementClock;

        public bool Equals(in SimulationMotionTraceDetail other) =>
            Kind == other.Kind &&
            DisplacementXRaw == other.DisplacementXRaw &&
            DisplacementYRaw == other.DisplacementYRaw &&
            DisplacementZRaw == other.DisplacementZRaw &&
            YawDegreesRaw == other.YawDegreesRaw &&
            MovementClock.Equals(other.MovementClock) &&
            Channel == other.Channel &&
            BlendMode == other.BlendMode &&
            Priority == other.Priority &&
            WeightRaw == other.WeightRaw &&
            PlanarBasisXRaw == other.PlanarBasisXRaw &&
            PlanarBasisYRaw == other.PlanarBasisYRaw &&
            ClaimsLowerChannels == other.ClaimsLowerChannels &&
            HasMotion == other.HasMotion &&
            ParticipatingSourceCount == other.ParticipatingSourceCount &&
            ParticipatingSourceFingerprint == other.ParticipatingSourceFingerprint;
    }
}
