#if UNITY_EDITOR
namespace ThirdPersonSimulation
{
    public sealed class Float32GraphPortValues : IProgramValuePortInputs<AbilityStateValue>, IProgramValuePortEdits<AbilityStateValue>
    {
        readonly Float32GraphValueLayout m_Layout;
        readonly AbilityStateValue[] m_Inputs;
        readonly AbilityStateValue[] m_Observed;
        readonly byte[] m_Flags;

        public Float32GraphPortValues(Float32GraphValueLayout layout)
        {
            m_Layout = layout;
            m_Inputs = new AbilityStateValue[layout.PortSlotCount];
            m_Observed = new AbilityStateValue[layout.PortSlotCount];
            m_Flags = new byte[layout.PortSlotCount];
        }

        public AbilityStateValue Read(int slot) => m_Observed[slot];
        public AbilityStateValue ReadInput(int slot) => m_Flags[slot] != 0 ? m_Inputs[slot] : default;
        public bool IsLocked(int slot) => m_Flags[slot] == 3;
        bool IProgramValuePortInputs<AbilityStateValue>.TryRead(OperationHandle operation, int portIndex, bool input, out AbilityStateValue value) =>
            TryRead(PortSlot(operation, portIndex, input), out value);

        public bool TryReadInput(OperationHandle operation, int portIndex, bool input, out AbilityStateValue value) =>
            TryRead(PortSlot(operation, portIndex, input), out value);

        public bool IsLocked(OperationHandle operation, int portIndex, bool input) =>
            IsLocked(PortSlot(operation, portIndex, input));

        public void Set(OperationHandle operation, int portIndex, bool input, AbilityStateValue value, bool locked) =>
            Set(PortSlot(operation, portIndex, input), value, locked);

        public void Release(OperationHandle operation, int portIndex, bool input) =>
            Release(PortSlot(operation, portIndex, input));

        int PortSlot(OperationHandle operation, int portIndex, bool input) => input
            ? m_Layout.InputPortSlot(operation, portIndex) : m_Layout.OutputPortSlot(operation, portIndex);

        public bool TryRead(int slot, out AbilityStateValue value)
        {
            value = m_Inputs[slot];
            return m_Flags[slot] != 0;
        }

        public void Set(int slot, AbilityStateValue value, bool locked)
        {
            value.Retain();
            Release(slot);
            m_Inputs[slot] = value;
            m_Flags[slot] = locked ? (byte)3 : (byte)1;
        }

        public void Release(int slot)
        {
            m_Inputs[slot].Release();
            m_Inputs[slot] = default;
            m_Flags[slot] = 0;
        }

        public void Observe(int slot, AbilityStateValue value)
        {
            value.Retain();
            m_Observed[slot].Release();
            m_Observed[slot] = value;
        }

        public void CompleteEvaluation()
        {
            for (int i = 0; i < m_Flags.Length; i++)
                if (m_Flags[i] == 1)
                    Release(i);
        }

        public void Reset()
        {
            for (int i = 0; i < m_Flags.Length; i++)
            {
                Release(i);
                m_Observed[i].Release();
                m_Observed[i] = default;
            }
        }
    }
}
#endif
