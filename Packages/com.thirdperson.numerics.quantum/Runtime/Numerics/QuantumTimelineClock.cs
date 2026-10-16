using Photon.Deterministic;

namespace ThirdPersonSimulation.QuantumNumerics
{
    public readonly struct QuantumTimelineTimeProjection : IOrchestrationTimelineTimeProjection<FP, FP>
    {
        public FP Project(FP time) => time;
    }

    public readonly struct QuantumFloat32TimelineTimeProjection : IOrchestrationTimelineTimeProjection<FP, Float32Scalar>
    {
        public Float32Scalar Project(FP time) => Float32Scalar.FromSingle(time.AsFloat);
    }
}
