using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
namespace ThirdPersonSimulation.FPGameplay
{
    public readonly struct FPProgramNumericValueAccess : IProgramNumericValueAccess<AbilityStateValue, FP>
    {
        public ProgramStateValueKind Kind(in AbilityStateValue value) => value.Kind;
        public bool Boolean(in AbilityStateValue value) => value.Boolean;
        public int Int32(in AbilityStateValue value) => value.Int32;
        public ulong UInt64(in AbilityStateValue value) => value.UInt64;
        public FP Scalar(in AbilityStateValue value) => value.Scalar;
        public CanonicalTextValue Identity(in AbilityStateValue value) => value.Identity;
    }
}
