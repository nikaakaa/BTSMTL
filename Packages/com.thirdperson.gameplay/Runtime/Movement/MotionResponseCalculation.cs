namespace ThirdPersonSimulation
{
    public static class MotionResponseCalculation<TScalar, TMath>
        where TScalar : struct
        where TMath : struct, IScalarMath<TScalar>
    {
        public static TScalar Evaluate(TScalar remaining, TScalar response, TScalar delta)
        {
            TMath math = default;
            TScalar amount = math.Clamp(math.Multiply(response, delta), math.Zero, math.One);
            return math.Multiply(remaining, amount);
        }
    }
}
