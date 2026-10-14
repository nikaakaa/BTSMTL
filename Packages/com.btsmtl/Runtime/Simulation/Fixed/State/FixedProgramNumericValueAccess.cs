namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedProgramNumericValueAccess : IProgramNumericValueAccess<AbilityStateValue, FixedScalar>
    {
        public ProgramStateValueKind Kind(in AbilityStateValue value) => value.Kind;
        public bool Boolean(in AbilityStateValue value) => value.Boolean;
        public int Int32(in AbilityStateValue value) => value.Int32;
        public ulong UInt64(in AbilityStateValue value) => value.UInt64;
        public FixedScalar Scalar(in AbilityStateValue value) => value.Scalar;
        public CanonicalTextValue Identity(in AbilityStateValue value) => value.Identity;
    }
}
