using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedAbilityExecutionWorkspace
    {
        SimulationMotionContribution[] m_MotionContributions = Array.Empty<SimulationMotionContribution>();
        int m_MotionContributionCount;
        readonly int m_OutputCapacity;

        public FixedAbilityExecutionWorkspace(
            FixedGameplayEffectExecutionScratch gameplayEffects,
            FixedGameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            List<AbilityTimelineAdvancePending> timelineAdvances,
            List<AbilityTimelineStopPending> timelineStops)
        {
            GameplayEffects = gameplayEffects ?? throw new ArgumentNullException(nameof(gameplayEffects));
            TimelineAdvances = timelineAdvances ?? throw new ArgumentNullException(nameof(timelineAdvances));
            TimelineStops = timelineStops ?? throw new ArgumentNullException(nameof(timelineStops));
            int depthCapacity = checked(data.Operations.Count + 1);
            m_OutputCapacity = checked(data.Operations.Count * 2);
            Facts.Capacity = m_OutputCapacity;
            Presentation.Capacity = m_OutputCapacity;
            ActionTickSnapshot = new List<FixedActionInstanceState>(layout.ActionInstanceCapacity);
            StoppingActionInstances = new HashSet<ulong>(layout.ActionInstanceCapacity);
            int inputCapacity = 0;
            for (int index = 0; index < data.Operations.Count; index++)
                inputCapacity = Math.Max(inputCapacity, layout.ValueInputs(data.Operations[index].Handle).Length);
            ActiveValueSlots = new bool[layout.ValueEvaluationSlotCount];
            while (ValueBuffers.Count < depthCapacity)
                ValueBuffers.Add(new FixedValueInputBuffer());
            for (int index = 0; index < ValueBuffers.Count; index++)
                if (ValueBuffers[index].Values.Capacity < inputCapacity)
                    ValueBuffers[index].Values.Capacity = inputCapacity;
        }

        public List<GameplayFact> Facts { get; } = new List<GameplayFact>();
        internal List<FixedActionInstanceState> ActionTickSnapshot { get; }
        internal HashSet<ulong> StoppingActionInstances { get; }
        public List<PresentationCommand> Presentation { get; } = new List<PresentationCommand>();
        public List<SimulationTraceRecord> Trace { get; } = new List<SimulationTraceRecord>();
        internal SimulationValueTraceStorage ValueTraces;
        internal FixedCharacterEvaluationOutput TraceOutput;
        internal int ValueTraceInvocation;
        public List<GameplayCommandBatchAdmission> CommandBatchRejections { get; } = new List<GameplayCommandBatchAdmission>();
        public List<FixedGameplayEffectPendingProjection> GameplayEffectProjections { get; } =
            new List<FixedGameplayEffectPendingProjection>();
        internal int GameplayEffectCueCount { get; set; }
        internal int GameplayEffectSuccessfulChangeCount { get; set; }
        public FixedGameplayEffectExecutionScratch GameplayEffects { get; }
        internal bool[] ActiveValueSlots { get; }
        public List<FixedValueInputBuffer> ValueBuffers { get; } = new List<FixedValueInputBuffer>();
        public List<AbilityTimelineLogicMotionWarp> TimelineMotionWarps { get; } =
            new List<AbilityTimelineLogicMotionWarp>();
        public List<SimulationActionWindowProjectionCandidate> ActionWindowProjections { get; } =
            new List<SimulationActionWindowProjectionCandidate>();
        public HashSet<SimulationActionWindowProjectionKey> ActionWindowProjectionKeys { get; } = new();
        public List<AbilityTimelineAdvancePending> TimelineAdvances { get; }
        public List<AbilityTimelineStopPending> TimelineStops { get; }
        public Stack<SimulationTimelineBlackboardContext> TimelineBlackboardContexts { get; } =
            new Stack<SimulationTimelineBlackboardContext>();

        public void Reset()
        {
            Facts.Clear();
            Presentation.Clear();
            for (int index = 0; index < Trace.Count; index++)
                Trace[index].Release();
            Trace.Clear();
            for (int index = 0; index < GameplayEffectProjections.Count; index++)
                GameplayEffectProjections[index].Release();
            GameplayEffectProjections.Clear();
            CommandBatchRejections.Clear();
            GameplayEffectCueCount = 0;
            GameplayEffectSuccessfulChangeCount = 0;
            TimelineMotionWarps.Clear();
            ActionWindowProjections.Clear();
            ActionWindowProjectionKeys.Clear();
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

            contribution.Retain();
            m_MotionContributions[m_MotionContributionCount++] = contribution;
        }

        internal void PrepareCommandBatchResults(int capacity) => CommandBatchRejections.Capacity = capacity;

        internal void PrepareGameplayEffectOutputs(int changes, int facts, int presentation)
        {
            GameplayEffectProjections.Capacity = changes;
            Facts.Capacity = checked(m_OutputCapacity + facts);
            Presentation.Capacity = checked(m_OutputCapacity + presentation);
        }

        public void CopyMotionContributionsTo(FixedMotionContributionScratch contributions)
        {
            for (int i = 0; i < m_MotionContributionCount; i++)
                contributions.Append(m_MotionContributions[i]);
        }

        public void ClearMotionContributions()
        {
            for (int index = 0; index < m_MotionContributionCount; index++)
                m_MotionContributions[index].Release();
            Array.Clear(m_MotionContributions, 0, m_MotionContributionCount);
            m_MotionContributionCount = 0;
        }
    }

    internal readonly struct FixedGameplayEffectPendingProjection
    {
        internal FixedGameplayEffectPendingProjection(
            in PortableEffectRuntimeChange change,
            SimulationEventHeader factHeader,
            int factIndex,
            SimulationEventHeader presentationHeader,
            int presentationIndex,
            string producerId,
            FixedScalar sampleTime,
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
        internal FixedScalar SampleTime { get; }
        internal CanonicalTextValue Cause { get; }

        internal const int MaximumTextReferences = 29;
        internal void Retain()
        {
            Change.Retain(); FactHeader.Retain(); PresentationHeader.Retain(); Cause.Retain();
        }
        internal void Release()
        {
            Change.Release(); FactHeader.Release(); PresentationHeader.Release(); Cause.Release();
        }

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
            facts[FactIndex] = fact;
            if (change.Kind == PortableEffectChangeKind.Cue)
            {
                var command = new PresentationCommand(PresentationHeader,
                    PresentationCommandKind.Cue, ProducerId, SampleTime, FixedScalar.One,
                    PresentationHeader.Activation.Generation);
                command.Retain();
                presentation[PresentationIndex] = command;
            }
        }

        internal FixedGameplayEffectPendingProjection Offset(int facts, int presentation) =>
            new FixedGameplayEffectPendingProjection(
                Change, FactHeader, FactIndex + facts, PresentationHeader,
                PresentationIndex + presentation, ProducerId, SampleTime, Cause);
    }
}
