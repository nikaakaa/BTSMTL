namespace ThirdPersonSimulation
{
    public readonly struct Float32ProgramNumericValueAccess : IProgramNumericValueAccess<AbilityStateValue, Float32Scalar>
    {
        public ProgramStateValueKind Kind(in AbilityStateValue value) => value.Kind;
        public bool Boolean(in AbilityStateValue value) => value.Boolean;
        public int Int32(in AbilityStateValue value) => value.Int32;
        public ulong UInt64(in AbilityStateValue value) => value.UInt64;
        public Float32Scalar Scalar(in AbilityStateValue value) => value.Scalar;
        public CanonicalTextValue Identity(in AbilityStateValue value) => value.Identity;
    }
}
