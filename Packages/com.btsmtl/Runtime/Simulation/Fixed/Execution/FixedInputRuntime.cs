using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal readonly struct FixedInputRuntime
    {
        readonly FixedAbilityExecutionFrame m_Frame;

        public FixedInputRuntime(
            FixedAbilityExecutionFrame frame)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public void ApplyBlackboardInputBindings(IFixedBlackboardPort blackboard)
        {
            if (blackboard == null)
                throw new ArgumentNullException(nameof(blackboard));
            for (int i = 0; i < m_Frame.Layout.BlackboardInputBindings.Count; i++)
            {
                BlackboardInputStateBinding binding = m_Frame.Layout.BlackboardInputBindings[i];
                blackboard.ProjectBlackboardInput(binding, ReadValue(binding.InputId, binding.InputKind));
            }
        }

        public bool HasRequest(CanonicalTextValue requestId, out SimulationInputRequestState state)
        {
            state = m_Frame.ReadInputRequest(requestId);
            return state.IsValid && !state.Consumed && state.ExpireTick >= m_Frame.Tick.Value;
        }

        public void ClearRequest(CanonicalTextValue requestId)
        {
            SimulationInputRequestState state = m_Frame.ReadInputRequest(requestId);
            if (state.IsValid && !state.Consumed)
                m_Frame.InputRequests.SetInputRequest(requestId, state.Consume());
        }

        public SimulationInputValue ReadValue(string inputId, ProgramInputValueKind kind) =>
            m_Frame.ReadInputValue(inputId, kind);

    }
}

