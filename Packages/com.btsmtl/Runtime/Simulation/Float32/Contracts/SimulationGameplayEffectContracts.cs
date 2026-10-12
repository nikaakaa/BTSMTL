using System;
using System.Collections.Generic;
using System.Linq;

namespace ThirdPersonSimulation
{
    public enum SimulationGameplayEffectApplicationMode : byte
    {
        Confirmed = 0,
        Predicted = 1
    }

    public enum SimulationGameplayEffectLifecycleOperation : byte
    {
        Applied = 0,
        Confirmed = 1,
        Rejected = 2,
        StackChanged = 3,
        Inhibited = 4,
        Resumed = 5,
        PeriodExecuted = 6,
        Removed = 7,
        Expired = 8,
        Corrected = 9,
        Overflow = 10
    }

    public enum SimulationGameplayEffectApplyResultCode : byte
    {
        Applied = 0,
        Rejected = 1,
        MissingDefinition = 2,
        InvalidContext = 3,
        InvalidPrediction = 4,
        MissingSetByCaller = 5,
        UndeclaredSetByCaller = 6,
        RequirementFailed = 7,
        OverflowRejected = 8,
        DefinitionRevisionMismatch = 9
    }

    public readonly struct SimulationSetByCallerValue
    {
        public SimulationSetByCallerValue(CanonicalTextValue parameterId, Float32Scalar value)
        {
            ParameterId = SimulationIdentity.Require(parameterId, nameof(parameterId));
            Value = value;
        }

        public CanonicalTextValue ParameterId { get; }
        public Float32Scalar Value { get; }
    }

    public readonly struct SimulationAttributeCapture
    {
        public SimulationAttributeCapture(CanonicalTextValue attributeId, Float32Scalar value)
        {
            AttributeId = SimulationIdentity.Require(attributeId, nameof(attributeId));
            Value = value;
        }

        public CanonicalTextValue AttributeId { get; }
        public Float32Scalar Value { get; }
    }

    public readonly struct SimulationGameplayEffectContext
    {
        public SimulationGameplayEffectContext(
            ActorId sourceActorId,
            ActorId targetActorId,
            ulong sourceActionInstanceId,
            ulong predictionKey,
            ulong gameplayResultId,
            ulong sourceTick,
            SimulationGameplayEffectApplicationMode applicationMode,
            GameplayEffectScope scope = default,
            GameplayEffectHolderIdentity holder = default,
            GameplayEffectSuccessContext success = default,
            SimulationActionTargetSnapshot successTarget = default)
        {
            if (!sourceActorId.IsValid || !targetActorId.IsValid || sourceTick == 0)
                throw new ArgumentException("Gameplay Effect context identity is incomplete.");
            if (applicationMode == SimulationGameplayEffectApplicationMode.Predicted &&
                (sourceActionInstanceId == 0 || predictionKey == 0))
            {
                throw new ArgumentException("Predicted Gameplay Effect context requires Action and prediction identities.");
            }
            SourceActorId = sourceActorId;
            TargetActorId = targetActorId;
            SourceActionInstanceId = sourceActionInstanceId;
            PredictionKey = predictionKey;
            GameplayResultId = gameplayResultId;
            SourceTick = sourceTick;
            ApplicationMode = applicationMode;
            Scope = scope;
            Holder = holder;
            Success = success;
            SuccessTarget = successTarget;
        }

        public ActorId SourceActorId { get; }
        public ActorId TargetActorId { get; }
        public ulong SourceActionInstanceId { get; }
        public ulong PredictionKey { get; }
        public ulong GameplayResultId { get; }
        public ulong SourceTick { get; }
        public SimulationGameplayEffectApplicationMode ApplicationMode { get; }
        public GameplayEffectScope Scope { get; }
        public GameplayEffectHolderIdentity Holder { get; }
        public GameplayEffectSuccessContext Success { get; }
        public SimulationActionTargetSnapshot SuccessTarget { get; }

        internal void WriteSuccessTarget(CanonicalWriter writer)
        {
            writer.WriteBoolean(SuccessTarget.HasTarget);
            if (!SuccessTarget.HasTarget)
                return;
            writer.WriteText(SuccessTarget.TargetId);
            writer.WriteVector3(SuccessTarget.Position);
            writer.WriteYaw(SuccessTarget.Yaw);
        }

        internal static SimulationActionTargetSnapshot ReadSuccessTarget(CanonicalReader reader) => reader.ReadBoolean()
            ? new SimulationActionTargetSnapshot(reader.ReadText(), reader.ReadVector3(), reader.ReadYaw()) : default;
        public void Retain() { SourceActorId.Retain(); TargetActorId.Retain(); Holder.Retain(); Success.Retain(); SuccessTarget.Retain(); }
        public void Release() { SuccessTarget.Release(); Success.Release(); Holder.Release(); TargetActorId.Release(); SourceActorId.Release(); }
        public bool IsPredicted => ApplicationMode == SimulationGameplayEffectApplicationMode.Predicted;
        public bool IsValid => SourceActorId.IsValid && TargetActorId.IsValid && SourceTick != 0;
    }

    public readonly struct SimulationGameplayEffectApplication
    {
        readonly IReadOnlyList<SimulationSetByCallerValue> m_SetByCallerValues;
        readonly IReadOnlyList<SimulationAttributeCapture> m_SourceAttributeSnapshots;
        readonly IReadOnlyList<string> m_SourceTagSnapshot;

        public SimulationGameplayEffectApplication(
            CanonicalTextValue effectId,
            uint definitionRevision,
            SimulationGameplayEffectContext context,
            IEnumerable<SimulationSetByCallerValue> setByCallerValues = null,
            IEnumerable<SimulationAttributeCapture> sourceAttributeSnapshots = null,
            IEnumerable<string> sourceTagSnapshot = null,
            ulong authoritativeInstanceId = 0,
            ulong authoritativeLifecycleRevision = 0)
        {
            AuthoritativeInstanceId = 0;
            AuthoritativeLifecycleRevision = 0;
            EffectId = SimulationIdentity.Require(effectId, nameof(effectId));
            if (definitionRevision == 0 || !context.IsValid)
                throw new ArgumentException("Gameplay Effect application is incomplete.");
            DefinitionRevision = definitionRevision;
            Context = context;
            AuthoritativeInstanceId = authoritativeInstanceId;
            AuthoritativeLifecycleRevision = authoritativeLifecycleRevision;
            m_SetByCallerValues = CopySetByCaller(setByCallerValues);
            m_SourceAttributeSnapshots = CopyAttributes(sourceAttributeSnapshots);
            m_SourceTagSnapshot = CopyIdentities(sourceTagSnapshot, nameof(sourceTagSnapshot));
            HasSourceTagSnapshot = sourceTagSnapshot != null;
        }

        SimulationGameplayEffectApplication(
            CanonicalTextValue effectId,
            uint definitionRevision,
            SimulationGameplayEffectContext context,
            IReadOnlyList<SimulationSetByCallerValue> compiledSetByCallerValues,
            IReadOnlyList<SimulationAttributeCapture> sourceAttributeSnapshots,
            IReadOnlyList<string> sourceTagSnapshot)
        {
            AuthoritativeInstanceId = 0;
            AuthoritativeLifecycleRevision = 0;
            EffectId = SimulationIdentity.Require(effectId, nameof(effectId));
            if (definitionRevision == 0 || !context.IsValid)
                throw new ArgumentException("Gameplay Effect application is incomplete.");
            DefinitionRevision = definitionRevision;
            Context = context;
            m_SetByCallerValues = compiledSetByCallerValues ?? Array.Empty<SimulationSetByCallerValue>();
            m_SourceAttributeSnapshots = sourceAttributeSnapshots;
            m_SourceTagSnapshot = sourceTagSnapshot;
            HasSourceTagSnapshot = sourceTagSnapshot != null;
        }

        internal static SimulationGameplayEffectApplication FromCompiled(
            CanonicalTextValue effectId,
            uint definitionRevision,
            SimulationGameplayEffectContext context,
            IReadOnlyList<SimulationSetByCallerValue> setByCallerValues,
            IReadOnlyList<SimulationAttributeCapture> sourceAttributeSnapshots,
            IReadOnlyList<string> sourceTagSnapshot) =>
            new SimulationGameplayEffectApplication(effectId, definitionRevision, context, setByCallerValues, sourceAttributeSnapshots, sourceTagSnapshot);

        public CanonicalTextValue EffectId { get; }
        public uint DefinitionRevision { get; }
        public SimulationGameplayEffectContext Context { get; }
        public IReadOnlyList<SimulationSetByCallerValue> SetByCallerValues => m_SetByCallerValues;
        public IReadOnlyList<SimulationAttributeCapture> SourceAttributeSnapshots => m_SourceAttributeSnapshots;
        public IReadOnlyList<string> SourceTagSnapshot => m_SourceTagSnapshot;
        public bool HasSourceTagSnapshot { get; }
        public ulong AuthoritativeInstanceId { get; }
        public ulong AuthoritativeLifecycleRevision { get; }

        static SimulationSetByCallerValue[] CopySetByCaller(IEnumerable<SimulationSetByCallerValue> source)
        {
            var values = source == null ? Array.Empty<SimulationSetByCallerValue>() : source.ToArray();
            Array.Sort(values, (left, right) => CanonicalTextValue.CompareOrdinal(left.ParameterId, right.ParameterId));
            for (int i = 1; i < values.Length; i++)
            {
                if (CanonicalTextValue.Equals(values[i - 1].ParameterId, values[i].ParameterId, StringComparison.Ordinal))
                    throw new ArgumentException($"Duplicate SetByCaller parameter '{values[i].ParameterId}'.", nameof(source));
            }
            return values;
        }

        static SimulationAttributeCapture[] CopyAttributes(IEnumerable<SimulationAttributeCapture> source)
        {
            var values = source == null ? Array.Empty<SimulationAttributeCapture>() : source.ToArray();
            Array.Sort(values, (left, right) => CanonicalTextValue.CompareOrdinal(left.AttributeId, right.AttributeId));
            for (int i = 1; i < values.Length; i++)
            {
                if (CanonicalTextValue.Equals(values[i - 1].AttributeId, values[i].AttributeId, StringComparison.Ordinal))
                    throw new ArgumentException($"Duplicate source Attribute snapshot '{values[i].AttributeId}'.", nameof(source));
            }
            return values;
        }

        static string[] CopyIdentities(IEnumerable<string> source, string parameterName)
        {
            var values = source == null ? Array.Empty<string>() : source.ToArray();
            for (int i = 0; i < values.Length; i++)
                values[i] = SimulationIdentity.Require(values[i], parameterName);
            Array.Sort(values, StringComparer.Ordinal);
            return values;
        }
    }

    public readonly struct SimulationGameplayResultIngress
    {
        public SimulationGameplayResultIngress(SimulationGameplayEffectApplication application)
        {
            Application = application;
        }

        public SimulationGameplayEffectApplication Application { get; }
        public bool IsValid => Application.Context.IsValid;
    }

    public readonly struct SimulationGameplayEffectSpecSnapshot
    {
        public SimulationGameplayEffectSpecSnapshot(
            CanonicalTextValue effectId,
            uint definitionRevision,
            SimulationGameplayEffectContext context,
            IReadOnlyList<SimulationSetByCallerValue> setByCallerValues,
            IReadOnlyList<SimulationAttributeCapture> sourceAttributes,
            IReadOnlyList<SimulationAttributeCapture> targetAttributes,
            IReadOnlyList<string> sourceTags,
            IReadOnlyList<string> targetTags,
            ulong durationTicks,
            ulong periodTicks,
            bool sourceTagSnapshotProvided)
        {
            EffectId = effectId;
            DefinitionRevision = definitionRevision;
            Context = context;
            SetByCallerValues = setByCallerValues;
            SourceAttributes = sourceAttributes;
            TargetAttributes = targetAttributes;
            SourceTags = sourceTags;
            TargetTags = targetTags;
            DurationTicks = durationTicks;
            PeriodTicks = periodTicks;
            SourceTagSnapshotProvided = sourceTagSnapshotProvided;
        }

        public CanonicalTextValue EffectId { get; }
        public uint DefinitionRevision { get; }
        public SimulationGameplayEffectContext Context { get; }
        public IReadOnlyList<SimulationSetByCallerValue> SetByCallerValues { get; }
        public IReadOnlyList<SimulationAttributeCapture> SourceAttributes { get; }
        public IReadOnlyList<SimulationAttributeCapture> TargetAttributes { get; }
        public IReadOnlyList<string> SourceTags { get; }
        public IReadOnlyList<string> TargetTags { get; }
        public ulong DurationTicks { get; }
        public ulong PeriodTicks { get; }
        public bool SourceTagSnapshotProvided { get; }
        public bool IsComplete => !CanonicalTextValue.IsNullOrEmpty(EffectId) && DefinitionRevision != 0 &&
            Context.IsValid && SetByCallerValues != null && SourceAttributes != null &&
            TargetAttributes != null && SourceTags != null && TargetTags != null;
    }

    public sealed class SimulationGameplayEffectCorrection : IGameplayEffectAuthoritativeCorrection
    {
        public SimulationGameplayEffectCorrection(
            GameplayEffectCorrectionCoverage coverage,
            bool hasActiveEffect,
            uint definitionRevision,
            GameplayEffectActiveControlSnapshot active,
            SimulationGameplayEffectSpecSnapshot spec,
            IReadOnlyList<SimulationAttributeValueIngress> attributes,
            IReadOnlyList<GameplayCueFact> cues)
        {
            Coverage = coverage;
            HasActiveEffect = hasActiveEffect;
            DefinitionRevision = definitionRevision;
            Active = active;
            Spec = spec;
            Attributes = attributes;
            Cues = cues;
        }

        public GameplayEffectCorrectionCoverage Coverage { get; }
        public bool HasActiveEffect { get; }
        public uint DefinitionRevision { get; }
        public GameplayEffectActiveControlSnapshot Active { get; }
        public SimulationGameplayEffectSpecSnapshot Spec { get; }
        public IReadOnlyList<SimulationAttributeValueIngress> Attributes { get; }
        public IReadOnlyList<GameplayCueFact> Cues { get; }

        public bool IsComplete => Coverage != 0 &&
            (Coverage & ~(GameplayEffectCorrectionCoverage.ActiveEffect |
                GameplayEffectCorrectionCoverage.Attributes | GameplayEffectCorrectionCoverage.Cues)) == 0 &&
            (!HasActiveEffect || (Coverage & GameplayEffectCorrectionCoverage.ActiveEffect) != 0 && Spec.IsComplete) &&
            ((Coverage & GameplayEffectCorrectionCoverage.Attributes) != 0
                ? Attributes != null : Attributes == null || Attributes.Count == 0) &&
            ((Coverage & GameplayEffectCorrectionCoverage.Cues) != 0
                ? Cues != null : Cues == null || Cues.Count == 0);

        public bool CoversAttribute(CanonicalTextValue attributeId)
        {
            if (Attributes == null)
                return false;
            for (int i = 0; i < Attributes.Count; i++)
            {
                if (CanonicalTextValue.Equals(Attributes[i].AttributeId, attributeId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public bool CoversCue(CanonicalTextValue cueId)
        {
            if (Cues == null)
                return false;
            for (int i = 0; i < Cues.Count; i++)
            {
                if (CanonicalTextValue.Equals(Cues[i].CueId, cueId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    public sealed class SimulationGameplayEffectLifecycleIngress
    {
        public SimulationGameplayEffectLifecycleIngress(
            SimulationGameplayEffectLifecycleOperation operation,
            CanonicalTextValue effectId,
            ulong instanceId,
            SimulationGameplayEffectContext context,
            ulong startTick,
            ulong endTick,
            int stackCount,
            ulong lifecycleRevision,
            uint definitionRevision,
            GameplayEffectReconciliationScope scope,
            SimulationGameplayEffectCorrection correction = null,
            IReadOnlyList<SimulationSetByCallerValue> setByCallerValues = null,
            IReadOnlyList<SimulationAttributeCapture> sourceAttributeSnapshots = null,
            IReadOnlyList<string> sourceTagSnapshot = null)
        {
            Operation = operation;
            EffectId = effectId.IsNull ? CanonicalTextValue.Empty : effectId;
            InstanceId = instanceId;
            Context = context;
            StartTick = startTick;
            EndTick = endTick;
            StackCount = stackCount;
            LifecycleRevision = lifecycleRevision;
            DefinitionRevision = definitionRevision;
            Scope = scope;
            Correction = correction;
            SetByCallerValues = setByCallerValues ?? Array.Empty<SimulationSetByCallerValue>();
            SourceAttributeSnapshots = sourceAttributeSnapshots ?? Array.Empty<SimulationAttributeCapture>();
            SourceTagSnapshot = sourceTagSnapshot;
            if (!IsValid)
                throw new ArgumentException("Gameplay Effect lifecycle ingress is incomplete.");
        }

        public SimulationGameplayEffectLifecycleOperation Operation { get; }
        public CanonicalTextValue EffectId { get; }
        public ulong InstanceId { get; }
        public SimulationGameplayEffectContext Context { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        public int StackCount { get; }
        public ulong LifecycleRevision { get; }
        public uint DefinitionRevision { get; }
        public GameplayEffectReconciliationScope Scope { get; }
        public SimulationGameplayEffectCorrection Correction { get; }
        public IReadOnlyList<SimulationSetByCallerValue> SetByCallerValues { get; }
        public IReadOnlyList<SimulationAttributeCapture> SourceAttributeSnapshots { get; }
        public IReadOnlyList<string> SourceTagSnapshot { get; }
        public bool HasSourceTagSnapshot => SourceTagSnapshot != null;

        public bool IsValid
        {
            get
            {
                if (LifecycleRevision == 0)
                    return false;
                if (Operation == SimulationGameplayEffectLifecycleOperation.Confirmed ||
                    Operation == SimulationGameplayEffectLifecycleOperation.Rejected ||
                    Operation == SimulationGameplayEffectLifecycleOperation.Corrected)
                {
                    if (!Context.IsValid || Context.PredictionKey == 0 || Scope.PredictionKey != Context.PredictionKey)
                        return false;
                    bool selected = Scope.Kind == GameplayEffectReconciliationScopeKind.PredictionKey ||
                        !CanonicalTextValue.IsNullOrEmpty(Scope.EffectId) &&
                        (Scope.Kind == GameplayEffectReconciliationScopeKind.Effect ||
                         Scope.Kind == GameplayEffectReconciliationScopeKind.Instance && Scope.PredictedInstanceId != 0);
                    if (!selected)
                        return false;
                    if (Scope.Kind != GameplayEffectReconciliationScopeKind.PredictionKey &&
                        !CanonicalTextValue.Equals(EffectId, Scope.EffectId, StringComparison.Ordinal))
                        return false;
                    if (Operation == SimulationGameplayEffectLifecycleOperation.Corrected)
                        return Scope.Kind != GameplayEffectReconciliationScopeKind.PredictionKey &&
                            !CanonicalTextValue.IsNullOrEmpty(EffectId) && DefinitionRevision != 0;
                    return true;
                }
                if (Operation == SimulationGameplayEffectLifecycleOperation.Applied)
                    return !CanonicalTextValue.IsNullOrEmpty(EffectId) && Context.IsValid && DefinitionRevision != 0 && InstanceId != 0;
                return !CanonicalTextValue.IsNullOrEmpty(EffectId) && InstanceId != 0;
            }
        }
    }

    public readonly struct SimulationAttributeValueIngress
    {
        public SimulationAttributeValueIngress(
            CanonicalTextValue attributeId,
            Float32Scalar baseValue,
            Float32Scalar currentValue,
            ulong valueRevision,
            CanonicalTextValue causeEffectId,
            ulong causeEffectInstanceId,
            SimulationGameplayEffectContext causeContext)
        {
            AttributeId = SimulationIdentity.Require(attributeId, nameof(attributeId));
            if (valueRevision == 0)
                throw new ArgumentOutOfRangeException(nameof(valueRevision));
            BaseValue = baseValue;
            CurrentValue = currentValue;
            ValueRevision = valueRevision;
            CauseEffectId = causeEffectId.IsNull ? CanonicalTextValue.Empty : causeEffectId;
            CauseEffectInstanceId = causeEffectInstanceId;
            CauseContext = causeContext;
        }

        public CanonicalTextValue AttributeId { get; }
        public Float32Scalar BaseValue { get; }
        public Float32Scalar CurrentValue { get; }
        public ulong ValueRevision { get; }
        public CanonicalTextValue CauseEffectId { get; }
        public ulong CauseEffectInstanceId { get; }
        public SimulationGameplayEffectContext CauseContext { get; }
        public bool IsValid => !CanonicalTextValue.IsNullOrEmpty(AttributeId) && ValueRevision != 0;
    }

    public readonly struct GameplayEffectFact
    {
        public GameplayEffectFact(
            CanonicalTextValue effectId,
            ulong instanceId,
            SimulationGameplayEffectLifecycleOperation operation,
            SimulationGameplayEffectContext context,
            ulong startTick,
            ulong endTick,
            int stackCount,
            ulong lifecycleRevision,
            uint definitionRevision,
            bool instant)
        {
            EffectId = SimulationIdentity.Require(effectId, nameof(effectId));
            if (instanceId == 0 || lifecycleRevision == 0 || definitionRevision == 0 || !context.IsValid)
                throw new ArgumentException("Gameplay Effect fact identity is incomplete.");
            InstanceId = instanceId;
            Operation = operation;
            Context = context;
            StartTick = startTick;
            EndTick = endTick;
            StackCount = stackCount;
            LifecycleRevision = lifecycleRevision;
            DefinitionRevision = definitionRevision;
            Instant = instant;
        }

        public CanonicalTextValue EffectId { get; }
        public ulong InstanceId { get; }
        public SimulationGameplayEffectLifecycleOperation Operation { get; }
        public SimulationGameplayEffectContext Context { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        public int StackCount { get; }
        public ulong LifecycleRevision { get; }
        public uint DefinitionRevision { get; }
        public bool Instant { get; }
        public bool IsValid => !CanonicalTextValue.IsNullOrEmpty(EffectId) && InstanceId != 0 && LifecycleRevision != 0;
    }

    public readonly struct GameplayAttributeFact
    {
        public GameplayAttributeFact(
            CanonicalTextValue attributeId,
            Float32Scalar beforeBase,
            Float32Scalar baseValue,
            Float32Scalar beforeCurrent,
            Float32Scalar currentValue,
            ulong valueRevision,
            CanonicalTextValue causeEffectId,
            ulong causeEffectInstanceId,
            SimulationGameplayEffectContext causeContext)
        {
            AttributeId = SimulationIdentity.Require(attributeId, nameof(attributeId));
            if (valueRevision == 0)
                throw new ArgumentOutOfRangeException(nameof(valueRevision));
            BeforeBase = beforeBase;
            BaseValue = baseValue;
            BeforeCurrent = beforeCurrent;
            CurrentValue = currentValue;
            ValueRevision = valueRevision;
            CauseEffectId = causeEffectId.IsNull ? CanonicalTextValue.Empty : causeEffectId;
            CauseEffectInstanceId = causeEffectInstanceId;
            CauseContext = causeContext;
        }

        public CanonicalTextValue AttributeId { get; }
        public Float32Scalar BeforeBase { get; }
        public Float32Scalar BaseValue { get; }
        public Float32Scalar BeforeCurrent { get; }
        public Float32Scalar CurrentValue { get; }
        public ulong ValueRevision { get; }
        public CanonicalTextValue CauseEffectId { get; }
        public ulong CauseEffectInstanceId { get; }
        public SimulationGameplayEffectContext CauseContext { get; }
        public bool IsValid => !CanonicalTextValue.IsNullOrEmpty(AttributeId) && ValueRevision != 0;
    }

    public readonly struct GameplayCueFact
    {
        public GameplayCueFact(
            CanonicalTextValue cueId,
            CanonicalTextValue triggerId,
            CanonicalTextValue sourceId,
            ulong sourceInstanceId,
            SimulationGameplayEffectContext effectContext = default)
        {
            CueId = SimulationIdentity.Require(cueId, nameof(cueId));
            TriggerId = SimulationIdentity.Require(triggerId, nameof(triggerId));
            SourceId = SimulationIdentity.Require(sourceId, nameof(sourceId));
            SourceInstanceId = sourceInstanceId;
            EffectContext = effectContext;
        }

        public CanonicalTextValue CueId { get; }
        public CanonicalTextValue TriggerId { get; }
        public CanonicalTextValue SourceId { get; }
        public ulong SourceInstanceId { get; }
        public SimulationGameplayEffectContext EffectContext { get; }
        public bool IsValid => !CanonicalTextValue.IsNullOrEmpty(CueId) && !CanonicalTextValue.IsNullOrEmpty(TriggerId) && !CanonicalTextValue.IsNullOrEmpty(SourceId);
    }
}
