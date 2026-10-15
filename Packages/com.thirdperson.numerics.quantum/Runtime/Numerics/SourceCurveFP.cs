using Photon.Deterministic;
using GameplayAbilityCurve = ThirdPersonSimulation.GameplayAbilityCurve<Photon.Deterministic.FP>;

namespace ThirdPersonSimulation.QuantumNumerics
{
    public sealed class SourceCurveFP
    {
        readonly FP m_Duration;
        readonly FP m_SourceStart;
        readonly FP m_SourceEnd;
        readonly bool m_ForwardOnly;
        readonly GameplayAbilityCurve m_X;
        readonly GameplayAbilityCurve m_Y;
        readonly GameplayAbilityCurve m_Z;
        readonly GameplayAbilityCurve m_Yaw;

        public SourceCurveFP(FP duration, FP sourceStart, FP sourceEnd, bool forwardOnly,
            GameplayAbilityCurve positionX, GameplayAbilityCurve positionY, GameplayAbilityCurve positionZ,
            GameplayAbilityCurve forwardDistance, GameplayAbilityCurve yaw)
        {
            m_Duration = duration;
            m_SourceStart = sourceStart;
            m_SourceEnd = sourceEnd;
            m_ForwardOnly = forwardOnly;
            if (!m_ForwardOnly)
            {
                m_X = positionX;
                m_Y = positionY;
            }
            m_Z = m_ForwardOnly ? forwardDistance : positionZ;
            m_Yaw = yaw;
        }

        public void EvaluateDelta(FP elapsedTicks, FP timeScale, int tickRate,
            out FPVector3 displacement, out FP yaw)
        {
            FP previous = SourceTime(elapsedTicks / default(QuantumSimulationMath).FromInt64(tickRate));
            FP current = SourceTime((elapsedTicks + timeScale) / default(QuantumSimulationMath).FromInt64(tickRate));
            displacement = new FPVector3(
                m_ForwardOnly ? FP._0 : Difference(m_X, previous, current),
                m_ForwardOnly ? FP._0 : Difference(m_Y, previous, current),
                Difference(m_Z, previous, current));
            yaw = Difference(m_Yaw, previous, current);
        }

        FP SourceTime(FP elapsed) =>
            FPMath.Min(m_SourceEnd, m_SourceStart + FPMath.Clamp(elapsed, FP._0, m_Duration));

        static FP Difference(GameplayAbilityCurve curve, FP previous, FP current) =>
            curve.Evaluate<QuantumSimulationMath>(current, FP._0) - curve.Evaluate<QuantumSimulationMath>(previous, FP._0);
    }
}
