using System;

namespace ThirdPersonSimulation
{
    [CanonicalContract]
    public readonly partial struct SimulationPipelineHash : IEquatable<SimulationPipelineHash>, ICanonicalHashValue
    {
        public SimulationPipelineHash(StableHash value) { Value = value; }
        public StableHash Value { get; }
        public bool IsValid => Value.IsValid;
        public bool Equals(SimulationPipelineHash other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is SimulationPipelineHash other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
    }

}
