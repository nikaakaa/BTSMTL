using Photon.Deterministic;

namespace ThirdPersonSimulation.QuantumNumerics
{
    public static class QuantumSimulationNumericProfile
    {
        public const int AbiVersion = 1;
        public const int FractionalBits = FP.Precision;

        public static SimulationNumericProfile Value { get; } = new SimulationNumericProfile(
            new NumericProfileId("quantum-fp-q48.16"),
            new TargetAbiVersion(AbiVersion),
            FP.SIZE * 8,
            SimulationNumericRoundingMode.OperationDefined,
            SimulationNumericOverflowMode.UncheckedIntegerArithmetic,
            true);
    }
}
