namespace ThirdPersonSimulation
{
    public static class GrappleMotionCalculation<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        public static TScalar GravityScale(TVector3 pull, TVector3 look, TVector2 move,
            TScalar minimum, TScalar maximum)
        {
            TMath math = default;
            if (math.GreaterThan(math.Divide(math.FromInt32(-4), math.FromInt32(5)), math.Y3(pull)))
                return math.One;
            TVector2 horizontalLook = math.Normalize2(math.Vector2(math.X3(look), math.Z3(look)));
            TScalar forwardInput = math.Dot2(move, horizontalLook);
            TVector2 horizontalPull = math.Normalize2(math.Vector2(math.X3(pull), math.Z3(pull)));
            TScalar awayInput = math.Negate(math.Dot2(move, horizontalPull));
            TVector3 up = math.Vector3(math.Zero, math.One, math.Zero);
            TVector3 ropeUp = math.Normalize3(math.SubtractVector3(up, math.MultiplyVector3(pull, math.Y3(pull))));
            TScalar underInput = math.Multiply(
                math.Multiply(math.Multiply(math.Negate(forwardInput), math.Dot3(ropeUp, look)), math.FromInt32(2)),
                math.Clamp(math.Multiply(math.Multiply(math.Negate(forwardInput), math.Y3(look)), math.FromInt32(10)),
                    math.Zero, math.One));
            TScalar blend = math.Clamp(math.Max(awayInput, underInput), math.Zero, math.One);
            return math.Add(minimum, math.Multiply(math.Subtract(maximum, minimum), blend));
        }

        public static TVector3 PullVelocity(TVector3 velocity, TVector3 pull, TVector3 look,
            TScalar startSpeed, TScalar maximumSpeed, TScalar rampProgress,
            TScalar acceleration, TScalar tangentialDeceleration, TScalar delta)
        {
            TMath math = default;
            TScalar vertical = math.Y3(velocity);
            TScalar along = math.Dot3(velocity, pull);
            TVector3 tangent = math.SubtractVector3(velocity, math.MultiplyVector3(pull, along));
            TVector3 preserved = math.Vector3(math.Zero, math.Min(math.Y3(tangent), math.Zero), math.Zero);
            tangent = math.SubtractVector3(tangent, preserved);
            TVector3 viewVelocity = math.MultiplyVector3(look, math.Max(math.Zero, math.Dot3(tangent, look)));
            preserved = math.AddVector3(preserved, viewVelocity);
            tangent = math.SubtractVector3(tangent, viewVelocity);
            TScalar tangentSpeed = math.Magnitude3(tangent);
            if (math.GreaterThan(tangentSpeed, math.Zero))
                tangent = math.MultiplyVector3(tangent, math.Divide(
                    math.Max(math.Zero, math.Subtract(tangentSpeed, math.Multiply(tangentialDeceleration, delta))), tangentSpeed));
            TScalar limit = math.Add(startSpeed, math.Multiply(math.Subtract(maximumSpeed, startSpeed), rampProgress));
            if (math.GreaterThan(limit, along))
                along = math.Min(limit, math.Add(along, math.Multiply(acceleration, delta)));
            velocity = math.AddVector3(math.AddVector3(math.MultiplyVector3(pull, along), tangent), preserved);
            return math.Vector3(math.X3(velocity), math.Max(vertical, math.Y3(velocity)), math.Z3(velocity));
        }
    }
}
