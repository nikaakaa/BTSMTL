using ProgramValues = ThirdPersonSimulation.ProgramValueCalculation<ThirdPersonSimulation.AbilityStateValue, ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32SimulationMath, ThirdPersonSimulation.Float32ProgramNumericValueAccess>;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal readonly struct Float32AbilityExecutionServiceSet
    {
        readonly Float32AbilityExecutionFrame m_Frame;
        readonly Float32ActionStateStore m_ActionStore;
        readonly Float32EquipmentRuntime m_Equipment;
        readonly Float32ValueRuntime m_Values;
        readonly IReadOnlyList<AbilityTimelineAdvancePending> m_TimelineAdvances;
        readonly IReadOnlyList<AbilityTimelineStopPending> m_TimelineStops;

        public Float32AbilityExecutionServiceSet(
            Float32AbilityExecutionFrame frame,
            Float32AbilityExecutionTarget target,
            Float32ActionStateStore actionStore,
            Float32EquipmentRuntime equipment,
            Float32ValueRuntime values,
            IReadOnlyList<AbilityTimelineAdvancePending> timelineAdvances,
            IReadOnlyList<AbilityTimelineStopPending> timelineStops)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Target = target;
            m_ActionStore = actionStore ?? throw new ArgumentNullException(nameof(actionStore));
            m_Equipment = equipment;
            m_Values = values ?? throw new ArgumentNullException(nameof(values));
            m_TimelineAdvances = timelineAdvances ?? throw new ArgumentNullException(nameof(timelineAdvances));
            m_TimelineStops = timelineStops ?? throw new ArgumentNullException(nameof(timelineStops));
        }

        public Float32AbilityExecutionTarget Target { get; }
        public IReadOnlyList<AbilityTimelineAdvancePending> TimelineAdvances => m_TimelineAdvances;
        public IReadOnlyList<AbilityTimelineStopPending> TimelineStops => m_TimelineStops;

        public void BeginEvaluation(bool diagnosticsEnabled, bool captureValues, bool captureControlFlow, bool captureBlackboard)
        {
            m_Frame.Trace.Begin(diagnosticsEnabled, captureValues, captureControlFlow, captureBlackboard);
            m_ActionStore.BeginEvaluation();
            m_Values.BeginEvaluation();
            m_Equipment?.BeginEvaluation();
        }

        public bool EvaluateRuleCondition(OperationControlCursor<Float32AbilityExecutionTarget> cursor,
            OperationHandle condition) => ProgramValues.ToBoolean(m_Values.Evaluate(cursor, condition));

        public void EndEvaluation()
        {
            m_Equipment?.EndEvaluation();
            m_ActionStore.EndEvaluation();
        }

    }
}
