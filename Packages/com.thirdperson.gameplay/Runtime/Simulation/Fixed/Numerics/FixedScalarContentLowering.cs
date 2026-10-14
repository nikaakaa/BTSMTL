namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedScalarContentLowering : IScalarContentLowering<FixedScalar>
    {
        public FixedScalar FromSingle(float value) => FixedScalar.FromDouble(value);
    }
}
