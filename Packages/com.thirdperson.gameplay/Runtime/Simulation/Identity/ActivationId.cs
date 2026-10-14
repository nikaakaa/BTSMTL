using System;

namespace ThirdPersonSimulation
{
    public readonly partial struct ActivationId : IEquatable<ActivationId>
    {
        public ActivationId(SimulationExecutionSource source, ulong generation)
        {
            Source = source;
            Generation = generation;
            RequireRecord();
        }
        public SimulationExecutionSource Source { get; }
        public ulong Generation { get; }
        public bool IsValid => Source.IsValid && Generation != 0;
        public bool Equals(ActivationId other) => Source.Equals(other.Source) && Generation == other.Generation;
        public override bool Equals(object obj) => obj is ActivationId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Source, Generation);
        public override string ToString() => $"{Source}/{Generation}";

        [ValueValidation]
        void RequireRecord()
        {
            if (!Source.IsValid || Generation == 0)
                throw new ArgumentException("Activation identity is incomplete.");
        }
    }

}
