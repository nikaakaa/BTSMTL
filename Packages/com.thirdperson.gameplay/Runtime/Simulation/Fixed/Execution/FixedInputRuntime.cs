using ThirdPersonSimulation;
using System;

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

        public void ApplyBlackboardInputBindings(IFixedBlackboardPort blackboard
#if UNITY_EDITOR
            , FixedCharacterEvaluationOutput output
#endif
            )
        {
            for (int i = 0; i < m_Frame.Services.BlackboardInputBindings.Count; i++)
            {
                BlackboardInputStateBinding binding = m_Frame.Services.BlackboardInputBindings[i];
#if UNITY_EDITOR
                long readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                SimulationInputValue value = ReadValue(binding.InputId, binding.InputKind);
#if UNITY_EDITOR
                long projectStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                output.EvaluationInputBindingReadElapsedStopwatchTicks += projectStarted - readStarted;
#endif
                AbilityStateValue projected = Project(in value, binding.Projection);
#if UNITY_EDITOR
                output.EvaluationInputProjectionValueElapsedStopwatchTicks +=
                    System.Diagnostics.Stopwatch.GetTimestamp() - projectStarted;
#endif
                blackboard.WriteInputProjection(binding.StateAddress.SlotIndex, projected);
#if UNITY_EDITOR
                output.EvaluationInputBindingProjectElapsedStopwatchTicks +=
                    System.Diagnostics.Stopwatch.GetTimestamp() - projectStarted;
                output.EvaluationInputBindingCalls++;
#endif
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

        public SimulationInputValue ReadValue(string inputId, ProgramInputValueKind kind) =>
            m_Frame.ReadInputValue(inputId, kind);

    }
}

