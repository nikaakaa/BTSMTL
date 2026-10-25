namespace ThirdPersonSimulation
{
    public static class AirMotionCalculation<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        public static TVector3 Steer(TVector3 velocity, TVector2 move,
            TScalar maximumSpeed, TScalar acceleration, TScalar delta)
        {
            TMath math = default;
            TScalar strength = math.Min(math.One, math.Magnitude2(move));
            if (math.IsZero(strength))
                return velocity;
            TVector3 wish = math.Normalize3(math.Vector3(math.X2(move), math.Zero, math.Y2(move)));
            TScalar available = math.Subtract(math.Multiply(maximumSpeed, strength), math.Dot3(velocity, wish));
            TScalar step = math.Multiply(math.Multiply(acceleration, strength), delta);
            if (math.GreaterThan(available, math.Zero))
                return math.AddVector3(velocity, math.MultiplyVector3(wish, math.Min(available, step)));
            TScalar speed = math.Magnitude3(velocity);
            TVector3 steered = math.AddVector3(velocity, math.MultiplyVector3(wish, step));
            return math.GreaterThan(math.SqrMagnitude3(steered), math.Zero)
                ? math.MultiplyVector3(math.Normalize3(steered), speed) : velocity;
        }
    }
}
