using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation
{
    internal sealed class Float32AbilityExecutionWorkspace
    {
        SimulationMotionContribution[] m_MotionContributions = Array.Empty<SimulationMotionContribution>();
        int m_MotionContributionCount;
        readonly int m_FactCapacity;
        readonly int m_PresentationCapacity;

        public Float32AbilityExecutionWorkspace(
            Float32GameplayEffectExecutionScratch gameplayEffects,
            List<AbilityTimelineAdvancePending> timelineAdvances,
            List<AbilityTimelineStopPending> timelineStops,
            Float32GraphValueWorkspace values,
            int factCapacity,
            int presentationCapacity,
            int actionInstanceCapacity)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
            m_FactCapacity = factCapacity;
            m_PresentationCapacity = presentationCapacity;
            Facts.Capacity = m_FactCapacity;
            Presentation.Capacity = m_PresentationCapacity;
            ActionTickSnapshot = new List<Float32ActionInstanceState>(actionInstanceCapacity);
            StoppingActionInstances = new HashSet<ulong>(actionInstanceCapacity);
            GameplayEffects = gameplayEffects ?? throw new ArgumentNullException(nameof(gameplayEffects));
            TimelineAdvances = timelineAdvances ?? throw new ArgumentNullException(nameof(timelineAdvances));
            TimelineStops = timelineStops ?? throw new ArgumentNullException(nameof(timelineStops));
        }

        public List<GameplayFact> Facts { get; } = new List<GameplayFact>();
        internal List<Float32ActionInstanceState> ActionTickSnapshot { get; }
        internal HashSet<ulong> StoppingActionInstances { get; }
        public List<PresentationCommand> Presentation { get; } = new List<PresentationCommand>();
        public List<SimulationTraceRecord> Trace { get; } = new List<SimulationTraceRecord>();
        internal SimulationValueTraceStorage ValueTraces;
        internal Float32CharacterEvaluationOutput TraceOutput;
        internal SimulationStructuredTraceStorage StructuredTraces;
        internal int ValueTraceInvocation;
        public List<GameplayCommandBatchAdmission> CommandBatchRejections { get; } = new List<GameplayCommandBatchAdmission>();
        internal Float32GameplayEffectPendingProjection[] GameplayEffectProjections { get; private set; } =
            Array.Empty<Float32GameplayEffectPendingProjection>();
        internal int GameplayEffectProjectionCount { get; set; }
        internal int GameplayEffectCueCount { get; set; }
        internal int GameplayEffectSuccessfulChangeCount { get; set; }
        public Float32GameplayEffectExecutionScratch GameplayEffects { get; }
        public Float32GraphValueWorkspace Values { get; }
        public List<AbilityTimelineLogicMotionWarp> TimelineMotionWarps { get; } =
            new List<AbilityTimelineLogicMotionWarp>();
        public List<AbilityTimelineAdvancePending> TimelineAdvances { get; }
        public List<AbilityTimelineStopPending> TimelineStops { get; }
        public Stack<SimulationTimelineBlackboardContext> TimelineBlackboardContexts { get; } =
            new Stack<SimulationTimelineBlackboardContext>();

        public void Reset()
        {
            for (int i = 0; i < Facts.Count; i++) Facts[i].Release();
            Facts.Clear();
            for (int i = 0; i < Presentation.Count; i++) Presentation[i].Release();
            Presentation.Clear();
            for (int index = 0; index < Trace.Count; index++)
                Trace[index].Release();
            Trace.Clear();
            TruncateGameplayEffectProjections(0);
            CommandBatchRejections.Clear();
            GameplayEffectCueCount = 0;
            GameplayEffectSuccessfulChangeCount = 0;
            TimelineMotionWarps.Clear();
            TimelineBlackboardContexts.Clear();
            ClearMotionContributions();
        }

        public void SubmitMotionContribution(in SimulationMotionContribution contribution)
        {
            if (m_MotionContributionCount == m_MotionContributions.Length)
            {
                int capacity = Math.Max(4, m_MotionContributions.Length * 2);
                var values = new SimulationMotionContribution[capacity];
                Array.Copy(m_MotionContributions, values, m_MotionContributionCount);
                m_MotionContributions = values;
            }

            m_MotionContributions[m_MotionContributionCount++] = contribution;
        }

        internal void PrepareCommandBatchResults(int capacity) => CommandBatchRejections.Capacity = capacity;

        internal void PrepareGameplayEffectOutputs(int changes, int facts, int presentation)
        {
            GameplayEffectProjections = new Float32GameplayEffectPendingProjection[changes];
            Facts.Capacity = checked(m_FactCapacity + facts);
            Presentation.Capacity = checked(m_PresentationCapacity + presentation);
        }

        internal void TruncateGameplayEffectProjections(int count)
        {
            for (int index = count; index < GameplayEffectProjectionCount; index++)
                GameplayEffectProjections[index].Release();
            Array.Clear(GameplayEffectProjections, count, GameplayEffectProjectionCount - count);
            GameplayEffectProjectionCount = count;
        }

        public void CopyMotionContributionsTo(Float32MotionContributionScratch contributions)
        {
            for (int i = 0; i < m_MotionContributionCount; i++)
                contributions.Append(m_MotionContributions[i]);
        }

        public void ClearMotionContributions()
        {
            Array.Clear(m_MotionContributions, 0, m_MotionContributionCount);
            m_MotionContributionCount = 0;
        }
    }

    internal readonly struct Float32GameplayEffectPendingProjection
    {
        internal Float32GameplayEffectPendingProjection(
            in PortableEffectRuntimeChange change,
            SimulationEventHeader factHeader,
            int factIndex,
            SimulationEventHeader presentationHeader,
            int presentationIndex,
            string producerId,
            Float32Scalar sampleTime,
            CanonicalTextValue cause)
        {
            Change = change;
            FactHeader = factHeader;
            FactIndex = factIndex;
            PresentationHeader = presentationHeader;
            PresentationIndex = presentationIndex;
            ProducerId = producerId;
            SampleTime = sampleTime;
            Cause = cause;
        }

        internal readonly PortableEffectRuntimeChange Change;
        internal SimulationEventHeader FactHeader { get; }
        internal int FactIndex { get; }
        internal SimulationEventHeader PresentationHeader { get; }
        internal int PresentationIndex { get; }
        internal string ProducerId { get; }
        internal Float32Scalar SampleTime { get; }
        internal CanonicalTextValue Cause { get; }

        internal void Project(List<GameplayFact> facts, List<PresentationCommand> presentation)
        {
            ref readonly PortableEffectRuntimeChange change = ref Change;
            GameplayFact fact = change.Kind switch
            {
                PortableEffectChangeKind.Lifecycle => new GameplayFact(FactHeader, new GameplayEffectFact(
                    change.Definition.Id, change.InstanceId, change.Operation, change.Context,
                    change.StartTick, change.EndTick, change.StackCount, change.LifecycleRevision,
                    change.Definition.Revision, change.Instant), Cause, change.Cursor),
                PortableEffectChangeKind.Attribute => new GameplayFact(FactHeader, new GameplayAttributeFact(
                    change.Value.AttributeId, change.Value.BeforeBase, change.Value.BaseValue,
                    change.Value.BeforeCurrent, change.Value.CurrentValue, change.Value.Revision,
                    change.CauseEffectId, change.InstanceId, change.Context), Cause, change.Cursor),
                PortableEffectChangeKind.Cue => new GameplayFact(FactHeader, new GameplayCueFact(
                    change.CueId, PortableCueTriggerText.Identity(change.Trigger), change.Definition.Id,
                    change.InstanceId, change.Context), Cause, change.Cursor),
                PortableEffectChangeKind.Failure => new GameplayFact(FactHeader, change.Failure, Cause, change.Cursor),
                PortableEffectChangeKind.Reconciliation => new GameplayFact(FactHeader, change.Reconciliation,
                    change.Context, Cause, change.Cursor),
                PortableEffectChangeKind.Tag => new GameplayFact(FactHeader, change.Tag, Cause, change.Cursor),
                _ => throw new ArgumentOutOfRangeException(nameof(change.Kind))
            };
            fact.Retain();
            facts[FactIndex].Release();
            facts[FactIndex] = fact;
            if (change.Kind == PortableEffectChangeKind.Cue)
            {
                var command = new PresentationCommand(PresentationHeader,
                    PresentationCommandKind.Cue, ProducerId, SampleTime, Float32Scalar.One,
                    PresentationHeader.Activation.Generation);
                command.Retain();
                presentation[PresentationIndex].Release();
                presentation[PresentationIndex] = command;
            }
        }

        internal Float32GameplayEffectPendingProjection Offset(int facts, int presentation) =>
            new Float32GameplayEffectPendingProjection(
                Change, FactHeader, FactIndex + facts, PresentationHeader,
                PresentationIndex + presentation, ProducerId, SampleTime, Cause);
        internal void Retain()
        { Change.Retain(); FactHeader.Retain(); PresentationHeader.Retain(); Cause.Retain(); }
        internal void Release()
        { Cause.Release(); PresentationHeader.Release(); FactHeader.Release(); Change.Release(); }
    }
}
