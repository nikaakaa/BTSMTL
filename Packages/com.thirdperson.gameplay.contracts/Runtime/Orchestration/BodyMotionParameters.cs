namespace ThirdPersonSimulation
{
    public readonly struct BodyMotionParameters<TScalar> where TScalar : struct
    {
        public BodyMotionParameters(TScalar gravityAcceleration, TScalar maximumFallSpeed)
        {
            GravityAcceleration = gravityAcceleration;
            MaximumFallSpeed = maximumFallSpeed;
        }

        public TScalar GravityAcceleration { get; }
        public TScalar MaximumFallSpeed { get; }
    }
}
