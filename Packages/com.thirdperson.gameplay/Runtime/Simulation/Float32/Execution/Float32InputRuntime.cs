using System;

namespace ThirdPersonSimulation
{
    internal readonly struct Float32InputRuntime
    {
        readonly Float32AbilityExecutionFrame m_Frame;

        public Float32InputRuntime(
            Float32AbilityExecutionFrame frame)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public void ApplyBlackboardInputBindings(IFloat32BlackboardPort blackboard)
        {
            for (int i = 0; i < m_Frame.Services.BlackboardInputBindings.Count; i++)
            {
                BlackboardInputStateBinding binding = m_Frame.Services.BlackboardInputBindings[i];
                SimulationInputValue value = ReadValue(binding.InputId, binding.InputKind);
                blackboard.WriteInputProjection(binding.StateAddress.SlotIndex, Project(value, binding.Projection));
            }
        }

        static AbilityStateValue Project(in SimulationInputValue value, ProgramBlackboardInputProjection projection)
        {
            switch (projection)
            {
                case ProgramBlackboardInputProjection.ActionTargetIdentity:
                    return AbilityStateValue.FromIdentity(value.ActionTargetSnapshot.TargetId);
                case ProgramBlackboardInputProjection.ActionTargetPosition:
                    return AbilityStateValue.FromVector3(value.ActionTargetSnapshot.Position);
                case ProgramBlackboardInputProjection.ActionTargetYaw:
                    return AbilityStateValue.FromScalar(value.ActionTargetSnapshot.Yaw.Degrees);
            }
            return value.Kind switch
            {
                ProgramInputValueKind.Boolean => AbilityStateValue.FromBoolean(value.Boolean),
                ProgramInputValueKind.Scalar => AbilityStateValue.FromScalar(value.Scalar),
                ProgramInputValueKind.Vector2 => AbilityStateValue.FromVector2(value.Vector2),
                ProgramInputValueKind.Vector3 => AbilityStateValue.FromVector3(value.Vector3),
                _ => throw new InvalidOperationException($"Blackboard Input Binding value kind '{value.Kind}' is unsupported.")
            };
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

        public SimulationInputValue ReadValue(CanonicalTextValue inputId, ProgramInputValueKind kind) =>
            m_Frame.ReadInputValue(inputId, kind);

    }
}
