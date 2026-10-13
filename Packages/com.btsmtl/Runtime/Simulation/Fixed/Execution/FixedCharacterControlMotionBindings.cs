using GameplayAbilityCurveKey = ThirdPersonSimulation.GameplayAbilityCurveKey<ThirdPersonSimulation.Fixed.FixedScalar>;
using GameplayAbilityCurve = ThirdPersonSimulation.GameplayAbilityCurve<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedSimulationMath>;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedCharacterControlMotionBinding
    {
        readonly FixedScalar m_Duration;
        readonly FixedScalar m_SourceStart;
        readonly FixedScalar m_SourceEnd;
        readonly bool m_ForwardOnly;
        readonly GameplayAbilityCurve m_X;
        readonly GameplayAbilityCurve m_Y;
        readonly GameplayAbilityCurve m_Z;
        readonly GameplayAbilityCurve m_Yaw;

        public FixedCharacterControlMotionBinding(CharacterControlMotionBinding source)
        {
            m_Duration = source.Mapping.CurveEndTime - source.Mapping.ClipStartTime;
            m_SourceStart = FixedScalar.FromDouble(source.Mapping.SourceStartTime);
            m_SourceEnd = FixedScalar.FromDouble(source.Mapping.SourceEndTime);
            m_ForwardOnly = source.EvaluationMode == CharacterControlMotionEvaluationMode.ForwardDistanceYaw;
            if (!m_ForwardOnly)
            {
                m_X = Prepare(source.PositionX);
                m_Y = Prepare(source.PositionY);
            }
            m_Z = Prepare(m_ForwardOnly ? source.ForwardDistance : source.PositionZ);
            m_Yaw = Prepare(source.Yaw);
        }

        public void EvaluateDelta(
            FixedScalar previousElapsed, FixedScalar currentElapsed,
            out FixedVector3 displacement, out FixedScalar yaw)
        {
            FixedScalar previous = SourceTime(previousElapsed);
            FixedScalar current = SourceTime(currentElapsed);
            displacement = new FixedVector3(
                m_ForwardOnly ? FixedScalar.Zero : Difference(m_X, previous, current),
                m_ForwardOnly ? FixedScalar.Zero : Difference(m_Y, previous, current),
                Difference(m_Z, previous, current));
            yaw = Difference(m_Yaw, previous, current);
        }

        FixedScalar SourceTime(FixedScalar elapsed) =>
            FixedScalar.Min(m_SourceEnd, m_SourceStart + FixedScalar.Clamp(elapsed, FixedScalar.Zero, m_Duration));

        static FixedScalar Difference(GameplayAbilityCurve curve, FixedScalar previous, FixedScalar current) =>
            curve.Evaluate(current, FixedScalar.Zero) - curve.Evaluate(previous, FixedScalar.Zero);

        static GameplayAbilityCurve Prepare(CharacterControlMotionCurve source)
        {
            var keys = new GameplayAbilityCurveKey[source.Keys.Count];
            for (int index = 0; index < keys.Length; index++)
            {
                CharacterControlMotionCurveKey key = source.Keys[index];
                keys[index] = new GameplayAbilityCurveKey(
                    FixedScalar.FromDouble(key.Time), FixedScalar.FromDouble(key.Value),
                    FixedScalar.FromDouble(key.InTangent), FixedScalar.FromDouble(key.OutTangent),
                    FixedScalar.FromDouble(key.InWeight), FixedScalar.FromDouble(key.OutWeight), key.WeightedMode, false);
            }
            return new GameplayAbilityCurve(source.PreWrapMode, source.PostWrapMode, keys);
        }
    }

    internal sealed class FixedCharacterControlMotionBindingCatalog
    {
        readonly Dictionary<string, FixedCharacterControlMotionBinding> m_Bindings;

        public FixedCharacterControlMotionBindingCatalog(CharacterControlMotionBindingCatalog source)
        {
            m_Bindings = new Dictionary<string, FixedCharacterControlMotionBinding>(source.Bindings.Count, StringComparer.Ordinal);
            for (int index = 0; index < source.Bindings.Count; index++)
            {
                CharacterControlMotionBinding binding = source.Bindings[index];
                m_Bindings.Add(binding.SourceIdentity, new FixedCharacterControlMotionBinding(binding));
            }
        }

        public FixedCharacterControlMotionBinding Require(string sourceIdentity) => m_Bindings[sourceIdentity];
    }
}
