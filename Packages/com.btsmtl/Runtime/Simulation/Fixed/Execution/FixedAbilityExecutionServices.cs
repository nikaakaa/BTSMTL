using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal readonly struct FixedAbilityExecutionServiceSet
    {
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly FixedActionStateStore m_ActionStore;
        readonly FixedEquipmentRuntime m_Equipment;
        readonly FixedValueRuntime m_Values;
        readonly FixedBlackboardRuntime m_Blackboard;
        readonly IReadOnlyList<AbilityTimelineAdvancePending> m_TimelineAdvances;
        readonly IReadOnlyList<AbilityTimelineStopPending> m_TimelineStops;

        public FixedAbilityExecutionServiceSet(
            FixedAbilityExecutionFrame frame,
            FixedAbilityExecutionTarget target,
            FixedActionStateStore actionStore,
            FixedEquipmentRuntime equipment,
            FixedValueRuntime values,
            FixedBlackboardRuntime blackboard,
            IReadOnlyList<AbilityTimelineAdvancePending> timelineAdvances,
            IReadOnlyList<AbilityTimelineStopPending> timelineStops)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Target = target;
            m_ActionStore = actionStore ?? throw new ArgumentNullException(nameof(actionStore));
            m_Equipment = equipment;
            m_Values = values ?? throw new ArgumentNullException(nameof(values));
            m_Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            m_TimelineAdvances = timelineAdvances ?? throw new ArgumentNullException(nameof(timelineAdvances));
            m_TimelineStops = timelineStops ?? throw new ArgumentNullException(nameof(timelineStops));
        }

        public FixedAbilityExecutionTarget Target { get; }
        internal IReadOnlyList<SimulationTraceRecord> TraceRecords => m_Frame.TraceRecords;
        public IReadOnlyList<AbilityTimelineAdvancePending> TimelineAdvances => m_TimelineAdvances;
        public IReadOnlyList<AbilityTimelineStopPending> TimelineStops => m_TimelineStops;

        public void BeginEvaluation(bool diagnosticsEnabled, bool captureMotion, bool captureValues, bool captureControlFlow, bool captureBlackboard)
        {
            m_Frame.Trace.Begin(diagnosticsEnabled, captureMotion, captureValues, captureControlFlow, captureBlackboard);
            m_ActionStore.BeginEvaluation();
            m_Values.BeginEvaluation();
            m_Equipment?.BeginEvaluation();
        }

        public bool EvaluateRuleCondition(OperationControlCursor<FixedAbilityExecutionTarget> cursor,
            OperationHandle condition) => FixedValueRuntime.ToBoolean(m_Values.Evaluate(cursor, condition));

        public void EndEvaluation()
        {
            m_Equipment?.EndEvaluation();
            m_Blackboard.EndFrame();
            m_ActionStore.EndEvaluation();
        }

    }
}
