using System;

namespace BTSMTL.Replay.Input
{
    public sealed class CharacterInputRequestEvaluator
    {
        readonly CharacterInputKeyLayout m_Keys;
        readonly bool[] m_Armed;
        readonly bool[] m_Requested;
        readonly int[] m_ValueRequests;
        readonly int[] m_RequestedIndices;
        readonly double[] m_PressStartedAt;
        double m_SampleTime;
        int m_RequestCount;

        public CharacterInputRequestEvaluator(CharacterInputKeyLayout keys)
        {
            m_Keys = keys;
            m_Armed = new bool[keys.RequestCount];
            m_Requested = new bool[keys.RequestCount];
            m_RequestedIndices = new int[keys.RequestCount];
            m_ValueRequests = new int[keys.ValueCount];
            Array.Fill(m_ValueRequests, -1);
            m_PressStartedAt = new double[keys.Count];
            Array.Fill(m_PressStartedAt, double.NegativeInfinity);
            for (int request = 0; request < keys.RequestCount; request++)
            {
                int value = keys.RequestDefinition(request).ReleaseValueSlot;
                if (value >= 0)
                    m_ValueRequests[value] = request;
            }
        }

        public ReadOnlySpan<int> RequestedSourceIndices => m_RequestedIndices.AsSpan(0, m_RequestCount);
        public bool Requested(int request) => m_Requested[request];

        public void Capture(ReadOnlySpan<CharacterInputKeyState> states, double timeSeconds, bool acceptRequests = true)
        {
            m_SampleTime = timeSeconds;
            for (int key = 0; key < states.Length; key++)
            {
                if (!states[key].Pressed && !states[key].PressedThisFrame && !states[key].ReleasedThisFrame)
                    m_PressStartedAt[key] = double.NegativeInfinity;
                else if (states[key].PressedThisFrame || double.IsNegativeInfinity(m_PressStartedAt[key]))
                    m_PressStartedAt[key] = timeSeconds;
            }
            m_RequestCount = 0;
            for (int request = 0; request < m_Keys.RequestCount; request++)
            {
                CharacterInputKeyRequest definition = m_Keys.RequestDefinition(request);
                ref readonly CharacterInputKeyState key = ref states[m_Keys.RequestKey(request)];
                bool requested;
                if (definition.ReleaseValueSlot < 0)
                    requested = definition.RepeatWhileHeld
                        ? key.Pressed && timeSeconds >= m_PressStartedAt[m_Keys.RequestKey(request)] + definition.MinimumHoldSeconds
                        : key.PressedThisFrame;
                else
                {
                    requested = false;
                    if (key.PressedThisFrame)
                    {
                        m_Armed[request] = true;
                    }
                    bool cancelled = !acceptRequests;
                    if (m_Armed[request])
                        for (int other = 0; other < m_Keys.RequestCount; other++)
                            cancelled |= m_Keys.Cancels(request, other) &&
                                states[m_Keys.RequestKey(other)].PressedThisFrame;
                    if (cancelled)
                    {
                        m_Armed[request] = false;
                        requested = false;
                    }
                    if (key.ReleasedThisFrame)
                    {
                        requested |= m_Armed[request];
                        m_Armed[request] = false;
                    }
                }
                m_Requested[request] = acceptRequests && requested;
                if (m_Requested[request])
                    m_RequestedIndices[m_RequestCount++] = request;
            }
        }

        public CharacterInputValue Value(int value, in CharacterInputKeyState state)
        {
            CharacterInputKeyValue definition = m_Keys.ValueDefinition(value);
            switch (definition.ValueType)
            {
                case CharacterInputValueType.Bool:
                    int request = m_ValueRequests[value];
                    bool held = state.Pressed && HoldThresholdReached(value);
                    return CharacterInputValue.Bool(definition.InputValueId, held && (request < 0 || m_Armed[request]));
                case CharacterInputValueType.Float:
                    return CharacterInputValue.Float(definition.InputValueId, state.Scalar);
                case CharacterInputValueType.Vector2:
                    return CharacterInputValue.Vector2(definition.InputValueId, state.Vector2);
                default:
                    throw new InvalidOperationException($"Input value '{definition.InputValueId}' has unsupported type '{definition.ValueType}'.");
            }
        }

        public bool IsTargetingHoldSatisfied(int request)
        {
            for (int value = 0; value < m_ValueRequests.Length; value++)
                if (m_ValueRequests[value] == request)
                    return (m_Armed[request] || m_Requested[request]) && HoldThresholdReached(value);
            return true;
        }

        bool HoldThresholdReached(int value) =>
            m_SampleTime >= m_PressStartedAt[m_Keys.ValueKey(value)] + m_Keys.ValueDefinition(value).MinimumHoldSeconds;

        public double NextChangeTime
        {
            get
            {
                double next = double.PositiveInfinity;
                for (int value = 0; value < m_Keys.ValueCount; value++)
                {
                    double at = m_PressStartedAt[m_Keys.ValueKey(value)] + m_Keys.ValueDefinition(value).MinimumHoldSeconds;
                    if (at > m_SampleTime && at < next)
                        next = at;
                }
                for (int request = 0; request < m_Keys.RequestCount; request++)
                {
                    double at = m_PressStartedAt[m_Keys.RequestKey(request)] + m_Keys.RequestDefinition(request).MinimumHoldSeconds;
                    if (at > m_SampleTime && at < next)
                        next = at;
                }
                return next;
            }
        }
    }
}
