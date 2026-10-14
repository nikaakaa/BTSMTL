using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using FPValueInputBuffer = ThirdPersonSimulation.ProgramValueInputBuffer<ThirdPersonSimulation.FPGameplay.AbilityStateValue>;
using MotionContributionBuffer = ThirdPersonSimulation.MotionContributionBuffer<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using SimulationMotionContribution = ThirdPersonSimulation.SimulationMotionContribution<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPAbilityExecutionWorkspace
    {
        readonly MotionContributionBuffer m_MotionContributions = new MotionContributionBuffer();
        readonly int m_FactCapacity;
        readonly int m_PresentationCapacity;

        public FPAbilityExecutionWorkspace(
            FPGameplayEffectExecutionScratch gameplayEffects,
            FPGameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            List<AbilityTimelineAdvancePending> timelineAdvances,
            List<AbilityTimelineStopPending> timelineStops)
        {
            m_MotionContributions.PrepareCapacity(layout.Topology.MotionContributionCapacity);
            GameplayEffects = gameplayEffects ?? throw new ArgumentNullException(nameof(gameplayEffects));
            TimelineAdvances = timelineAdvances ?? throw new ArgumentNullException(nameof(timelineAdvances));
            TimelineStops = timelineStops ?? throw new ArgumentNullException(nameof(timelineStops));
            int depthCapacity = checked(data.Operations.Count + 1);
            m_FactCapacity = layout.Topology.FactRecordCapacity;
            m_PresentationCapacity = layout.Topology.PresentationRecordCapacity;
            Facts.Capacity = m_FactCapacity;
            Presentation.Capacity = m_PresentationCapacity;
            ActionTickSnapshot = new List<FPActionInstanceState>(layout.ActionInstanceCapacity);
            StoppingActionInstances = new HashSet<ulong>(layout.ActionInstanceCapacity);
            int inputCapacity = 0;
            for (int index = 0; index < data.Operations.Count; index++)
                inputCapacity = Math.Max(inputCapacity, layout.ValueInputs(data.Operations[index].Handle).Length);
            ActiveValueSlots = new bool[layout.ValueEvaluationSlotCount];
            ObservationValues = new BorrowedValueObservationScratch<AbilityStateValue>(layout.ValueEvaluationSlotCount);
            while (ValueBuffers.Count < depthCapacity)
                ValueBuffers.Add(new FPValueInputBuffer(inputCapacity));
        }

        public List<GameplayFact> Facts { get; } = new List<GameplayFact>();
        internal List<FPActionInstanceState> ActionTickSnapshot { get; }
        internal HashSet<ulong> StoppingActionInstances { get; }
        public List<PresentationCommand> Presentation { get; } = new List<PresentationCommand>();
        public List<SimulationTraceRecord> Trace { get; } = new List<SimulationTraceRecord>();
        internal SimulationValueTraceStorage ValueTraces;
        internal FPCharacterEvaluationOutput TraceOutput;
        internal int ValueTraceInvocation;
        public List<GameplayCommandBatchAdmission> CommandBatchRejections { get; } = new List<GameplayCommandBatchAdmission>();
        internal FPGameplayEffectPendingProjection[] GameplayEffectProjections { get; private set; } =
            Array.Empty<FPGameplayEffectPendingProjection>();
        internal int GameplayEffectProjectionCount { get; set; }
        internal int GameplayEffectCueCount { get; set; }
        internal int GameplayEffectSuccessfulChangeCount { get; set; }
        public FPGameplayEffectExecutionScratch GameplayEffects { get; }
        internal bool[] ActiveValueSlots { get; }
        internal BorrowedValueObservationScratch<AbilityStateValue> ObservationValues { get; }
        public List<FPValueInputBuffer> ValueBuffers { get; } = new List<FPValueInputBuffer>();
        public List<AbilityTimelineLogicMotionWarp> TimelineMotionWarps { get; } =
            new List<AbilityTimelineLogicMotionWarp>();
        public List<AbilityTimelineAdvancePending> TimelineAdvances { get; }
        public List<AbilityTimelineStopPending> TimelineStops { get; }

        public void Reset()
        {
            Facts.Clear();
            Presentation.Clear();
            for (int index = 0; index < Trace.Count; index++)
                Trace[index].Release();
            Trace.Clear();
            TruncateGameplayEffectProjections(0);
            CommandBatchRejections.Clear();
            GameplayEffectCueCount = 0;
            GameplayEffectSuccessfulChangeCount = 0;
            TimelineMotionWarps.Clear();
            m_MotionContributions.Clear();
        }

        public void SubmitMotionContribution(in SimulationMotionContribution contribution) =>
            m_MotionContributions.Append(in contribution);

        internal void PrepareCommandBatchResults(int capacity) => CommandBatchRejections.Capacity = capacity;

        internal void PrepareGameplayEffectOutputs(int changes, int facts, int presentation)
        {
            GameplayEffectProjections = new FPGameplayEffectPendingProjection[changes];
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

        public void TransferMotionContributionsTo(MotionContributionBuffer contributions) =>
            m_MotionContributions.TransferTo(contributions);

    }

    internal readonly struct FPGameplayEffectPendingProjection
    {
        internal FPGameplayEffectPendingProjection(
            in PortableEffectRuntimeChange change,
            SimulationEventHeader factHeader,
            int factIndex,
            SimulationEventHeader presentationHeader,
            int presentationIndex,
            string producerId,
            FP sampleTime,
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
        internal FP SampleTime { get; }
        internal CanonicalTextValue Cause { get; }

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
                    PresentationCommandKind.Cue, ProducerId, SampleTime, FP._1,
                    PresentationHeader.Activation.Generation);
                command.Retain();
                presentation[PresentationIndex] = command;
            }
        }

        internal FPGameplayEffectPendingProjection Offset(int facts, int presentation) =>
            new FPGameplayEffectPendingProjection(
                Change, FactHeader, FactIndex + facts, PresentationHeader,
                PresentationIndex + presentation, ProducerId, SampleTime, Cause);
    }
}
