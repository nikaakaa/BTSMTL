using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    internal enum OrchestrationObservationPhase : byte
    {
        None,
        Evaluating,
        Observing
    }

    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TQuaternion, TMath, TValues, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
        where TContext : struct
    {

        public TValue Evaluate(int operationIndex, int outputPortIndex = -1)
        {
            bool observe = m_ObservationPhase == OrchestrationObservationPhase.None && m_Input.CaptureValues &&
                !IsPredictiveEvaluation && !m_Workspace.ValueObservations.Ports(operationIndex).IsEmpty;
            if (!observe)
                return EvaluateValue(operationIndex, outputPortIndex);
            m_ObservationPhase = OrchestrationObservationPhase.Evaluating;
            try
            {
                TValue value = EvaluateValue(operationIndex, outputPortIndex);
                m_ObservationPhase = OrchestrationObservationPhase.Observing;
                ObserveGraphValues(operationIndex);
                return value;
            }
            finally
            {
                ClearObservedValues();
                m_ObservationPhase = OrchestrationObservationPhase.None;
            }
        }

        void ObserveGraphValues(int operationIndex)
        {
            foreach (ref readonly OrchestrationValueObservationPort port in m_Workspace.ValueObservations.Ports(operationIndex))
            {
                TValue value;
                if (port.IsInput)
                {
                    ref readonly OrchestrationValueInputBinding input = ref m_Program.Content.ValueInputs.Span[port.ValueIndex];
                    value = ReadInput(port.OperationIndex, port.ValueIndex, in input);
                    ObserveInput(port.OperationIndex, port.ValueIndex, in value);
                }
                else
                {
                    value = EvaluateValue(port.OperationIndex, port.OutputPortIndex);
                    ObserveOutput(port.OperationIndex, port.ValueIndex, in value);
                }
            }
        }
    }
}
