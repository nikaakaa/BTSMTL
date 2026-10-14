using System;

namespace ThirdPersonSimulation
{
    public static class GameplayAttributeCalculation<TScalar, TMath>
        where TScalar : struct
        where TMath : struct, IScalarMath<TScalar>
    {
        public static TScalar ApplyModifier(TScalar current, PortableModifierOperation operation,
            TScalar magnitude, PortableClampBound clampBound)
        {
            TMath math = default;
            return operation switch
            {
                PortableModifierOperation.Additive => math.Add(current, magnitude),
                PortableModifierOperation.Multiplicative => math.Multiply(current, magnitude),
                PortableModifierOperation.Override => magnitude,
                PortableModifierOperation.Clamp when clampBound == PortableClampBound.Minimum => math.Max(current, magnitude),
                PortableModifierOperation.Clamp => math.Min(current, magnitude),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }
    }

    public struct GameplayAttributeAggregation<TScalar, TMath>
        where TScalar : struct
        where TMath : struct, IScalarMath<TScalar>
    {
        TScalar m_Additive;
        TScalar m_Multiplicative;
        TScalar m_OverrideValue;
        TScalar m_Minimum;
        TScalar m_Maximum;
        bool m_HasOverride;
        bool m_HasMinimum;
        bool m_HasMaximum;
        int m_OverridePriority;
        ulong m_OverrideSequence;

        public static GameplayAttributeAggregation<TScalar, TMath> Begin()
        {
            TMath math = default;
            return new GameplayAttributeAggregation<TScalar, TMath>
            {
                m_Additive = math.Zero,
                m_Multiplicative = math.One,
                m_OverrideValue = math.Zero,
                m_Minimum = math.Zero,
                m_Maximum = math.Zero,
                m_OverridePriority = int.MinValue
            };
        }

        public void Add(PortableModifierOperation operation, TScalar magnitude,
            PortableClampBound clampBound, int priority, ulong insertionSequence)
        {
            TMath math = default;
            switch (operation)
            {
                case PortableModifierOperation.Additive:
                    m_Additive = math.Add(m_Additive, magnitude);
                    break;
                case PortableModifierOperation.Multiplicative:
                    m_Multiplicative = math.Multiply(m_Multiplicative, magnitude);
                    break;
                case PortableModifierOperation.Override:
                    if (!m_HasOverride || priority > m_OverridePriority ||
                        priority == m_OverridePriority && insertionSequence > m_OverrideSequence)
                    {
                        m_HasOverride = true;
                        m_OverrideValue = magnitude;
                        m_OverridePriority = priority;
                        m_OverrideSequence = insertionSequence;
                    }
                    break;
                case PortableModifierOperation.Clamp:
                    if (clampBound == PortableClampBound.Minimum)
                        IncludeMinimum(magnitude);
                    else
                        IncludeMaximum(magnitude);
                    break;
                default:
                    throw new InvalidOperationException($"Gameplay Attribute modifier operation '{operation}' is invalid.");
            }
        }

        public void IncludeMinimum(TScalar value)
        {
            TMath math = default;
            m_Minimum = m_HasMinimum ? math.Max(m_Minimum, value) : value;
            m_HasMinimum = true;
        }

        public void IncludeMaximum(TScalar value)
        {
            TMath math = default;
            m_Maximum = m_HasMaximum ? math.Min(m_Maximum, value) : value;
            m_HasMaximum = true;
        }

        public TScalar Resolve(TScalar baseValue, string attributeId)
        {
            TMath math = default;
            TScalar value = math.Multiply(math.Add(baseValue, m_Additive), m_Multiplicative);
            if (m_HasOverride)
                value = m_OverrideValue;
            if (m_HasMinimum && m_HasMaximum && math.GreaterThan(m_Minimum, m_Maximum))
                throw new InvalidOperationException($"Gameplay Attribute '{attributeId}' minimum exceeds maximum.");
            if (m_HasMinimum && math.GreaterThan(m_Minimum, value))
                value = m_Minimum;
            if (m_HasMaximum && math.GreaterThan(value, m_Maximum))
                value = m_Maximum;
            return value;
        }
    }
}
