using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using GameplayAbilityCurveKey = ThirdPersonSimulation.GameplayAbilityCurveKey<Photon.Deterministic.FP>;
using GameplayAbilityCurve = ThirdPersonSimulation.GameplayAbilityCurve<Photon.Deterministic.FP>;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPCharacterControlMotionBinding
    {
        readonly FP m_Duration;
        readonly FP m_SourceStart;
        readonly FP m_SourceEnd;
        readonly bool m_ForwardOnly;
        readonly GameplayAbilityCurve m_X;
        readonly GameplayAbilityCurve m_Y;
        readonly GameplayAbilityCurve m_Z;
        readonly GameplayAbilityCurve m_Yaw;

        public FPCharacterControlMotionBinding(CharacterControlMotionBinding source)
        {
            m_Duration = source.Mapping.CurveEndTime - source.Mapping.ClipStartTime;
            m_SourceStart = QuantumScalarBoundary.ConvertExternal(source.Mapping.SourceStartTime, source.SourceIdentity);
            m_SourceEnd = QuantumScalarBoundary.ConvertExternal(source.Mapping.SourceEndTime, source.SourceIdentity);
            m_ForwardOnly = source.EvaluationMode == CharacterControlMotionEvaluationMode.ForwardDistanceYaw;
            if (!m_ForwardOnly)
            {
                m_X = Prepare(source.PositionX, source.SourceIdentity);
                m_Y = Prepare(source.PositionY, source.SourceIdentity);
            }
            m_Z = Prepare(m_ForwardOnly ? source.ForwardDistance : source.PositionZ, source.SourceIdentity);
            m_Yaw = Prepare(source.Yaw, source.SourceIdentity);
        }

        public void EvaluateDelta(
            FP previousElapsed, FP currentElapsed,
            out FPVector3 displacement, out FP yaw)
        {
            FP previous = SourceTime(previousElapsed);
            FP current = SourceTime(currentElapsed);
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

        static GameplayAbilityCurve Prepare(CharacterControlMotionCurve source, string sourceIdentity)
        {
            var keys = new GameplayAbilityCurveKey[source.Keys.Count];
            for (int index = 0; index < keys.Length; index++)
            {
                CharacterControlMotionCurveKey key = source.Keys[index];
                keys[index] = new GameplayAbilityCurveKey(
                    QuantumScalarBoundary.ConvertExternal(key.Time, sourceIdentity), QuantumScalarBoundary.ConvertExternal(key.Value, sourceIdentity),
                    QuantumScalarBoundary.ConvertExternal(key.InTangent, sourceIdentity), QuantumScalarBoundary.ConvertExternal(key.OutTangent, sourceIdentity),
                    QuantumScalarBoundary.ConvertExternal(key.InWeight, sourceIdentity), QuantumScalarBoundary.ConvertExternal(key.OutWeight, sourceIdentity), key.WeightedMode, false);
            }
            return new GameplayAbilityCurve(source.PreWrapMode, source.PostWrapMode, keys);
        }
    }

    internal sealed class FPCharacterControlMotionBindingCatalog
    {
        readonly Dictionary<string, FPCharacterControlMotionBinding> m_Bindings;

        public FPCharacterControlMotionBindingCatalog(CharacterControlMotionBindingCatalog source)
        {
            m_Bindings = new Dictionary<string, FPCharacterControlMotionBinding>(source.Bindings.Count, StringComparer.Ordinal);
            for (int index = 0; index < source.Bindings.Count; index++)
            {
                CharacterControlMotionBinding binding = source.Bindings[index];
                m_Bindings.Add(binding.SourceIdentity, new FPCharacterControlMotionBinding(binding));
            }
        }

        public FPCharacterControlMotionBinding Require(string sourceIdentity) => m_Bindings[sourceIdentity];
    }
}
