using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed partial class FixedGameplayEffectTarget
    {
        ActorId IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.ActorId => m_ActorId;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.ContextIsValid(SimulationGameplayEffectApplication application) => application.Context.IsValid;
        ActorId IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SourceActorId(SimulationGameplayEffectApplication application) => application.Context.SourceActorId;
        ActorId IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.TargetActorId(SimulationGameplayEffectApplication application) => application.Context.TargetActorId;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.IsPredicted(SimulationGameplayEffectApplication application) => application.Context.IsPredicted;
        ulong IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SourceActionInstanceId(SimulationGameplayEffectApplication application) => application.Context.SourceActionInstanceId;
        ulong IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.PredictionKey(SimulationGameplayEffectApplication application) => application.Context.PredictionKey;
        ulong IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.AuthoritativeInstanceId(SimulationGameplayEffectApplication application) => application.AuthoritativeInstanceId;
        uint IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.ApplicationDefinitionRevision(SimulationGameplayEffectApplication application) => application.DefinitionRevision;

        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.TryCreateSpec(
            SimulationGameplayEffectApplication application,
            out PortableEffectSpecState spec,
            out GameplayEffectApplyResult failure)
        {
            if (!m_State.Catalog.TryResolveEffect(application.EffectId, out PortableEffectDefinition definition))
            {
                spec = null;
                failure = new GameplayEffectApplyResult(GameplayEffectApplyResultKind.MissingDefinition,
                    0, application.AuthoritativeInstanceId, "EffectDefinitionMissing");
                return false;
            }
            if (!TryAdmitSpec(application, definition, out failure))
            {
                spec = null;
                return false;
            }
            spec = m_Scratch.AcquireSpec(definition, application.Context);
            spec.SourceTagSnapshotProvided = application.HasSourceTagSnapshot;
            failure = default;
            return true;
        }

        uint IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.DefinitionRevision(PortableEffectSpecState spec) => spec.Definition.Revision;
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SuppliedParameterCount(SimulationGameplayEffectApplication application) => application.SetByCallerValues.Count;
        CanonicalTextValue IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SuppliedParameterId(SimulationGameplayEffectApplication application, int index) => application.SetByCallerValues[index].ParameterId;
        FixedScalar IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SuppliedParameterValue(SimulationGameplayEffectApplication application, int index) => application.SetByCallerValues[index].Value;
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.RequiredParameterCount(PortableEffectSpecState spec) => spec.Definition.SetByCallerParameters.Length;
        string IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.RequiredParameterId(PortableEffectSpecState spec, int index) => spec.Definition.SetByCallerParameters[index];
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.ContainsParameter(PortableEffectSpecState spec, string parameterId) => spec.SetByCaller.ContainsKey(parameterId);
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.AddParameter(PortableEffectSpecState spec, string parameterId, FixedScalar value) => spec.SetByCaller.Add(parameterId, value);
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SuppliedSourceAttributeCount(SimulationGameplayEffectApplication application) => application.SourceAttributeSnapshots.Count;
        CanonicalTextValue IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SuppliedSourceAttributeId(SimulationGameplayEffectApplication application, int index) => application.SourceAttributeSnapshots[index].AttributeId;
        CanonicalTextValue IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.RequiredSourceAttributeKey(PortableEffectSpecState spec, int index) => spec.Definition.Layout.SourceAttributeKey(index);
        FixedScalar IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SuppliedSourceAttributeValue(SimulationGameplayEffectApplication application, int index) => application.SourceAttributeSnapshots[index].Value;
        IReadOnlyList<string> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.RequiredSnapshotAttributes(PortableEffectSpecState spec, GameplayEffectAttributeSnapshotKind kind) =>
            kind == GameplayEffectAttributeSnapshotKind.Source
                ? spec.Definition.SourceSnapshotAttributes
                : spec.Definition.TargetSnapshotAttributes;

        FixedScalar IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.ReadTargetAttribute(string attributeId) =>
            m_State.RequireAttribute(attributeId).CurrentValue;

        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.AddSourceAttribute(PortableEffectSpecState spec, string attributeId, FixedScalar value) => spec.SourceAttributes.Add(attributeId, value);
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.AddTargetAttribute(PortableEffectSpecState spec, string attributeId, FixedScalar value) => spec.TargetAttributes.Add(attributeId, value);
        IReadOnlyList<string> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.TargetTags() => m_State.QueryTags;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.HasSourceTagSnapshot(SimulationGameplayEffectApplication application) => application.HasSourceTagSnapshot;
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SourceTagCount(SimulationGameplayEffectApplication application) => application.HasSourceTagSnapshot ? application.SourceTagSnapshot.Count : 0;
        IReadOnlyList<string> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SourceTags(SimulationGameplayEffectApplication application) => application.SourceTagSnapshot;
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SetTargetTags(PortableEffectSpecState spec, IReadOnlyList<string> tags) => spec.SetTargetTags((GameplayTagSnapshot)tags);
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SetSourceTags(PortableEffectSpecState spec, IReadOnlyList<string> tags) => spec.SetSourceTags(m_State.CaptureTags(tags));
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.RequiresDuration(PortableEffectSpecState spec) => spec.Definition.DurationPolicy == PortableEffectDurationPolicy.Duration;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.HasPeriod(PortableEffectSpecState spec) => spec.Definition.HasPeriod;

        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.TryResolveDurationTicks(PortableEffectSpecState spec, out ulong ticks)
        {
            ticks = 0;
            return TryResolveMagnitude(spec, spec.Definition.DurationMagnitude, out FixedScalar seconds, out _) &&
                   TrySecondsToTicks(seconds, out ticks);
        }

        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.TryResolvePeriodTicks(PortableEffectSpecState spec, out ulong ticks)
        {
            ticks = 0;
            return TryResolveMagnitude(spec, spec.Definition.PeriodMagnitude, out FixedScalar seconds, out _) &&
                   TrySecondsToTicks(seconds, out ticks);
        }

        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SetDurationTicks(PortableEffectSpecState spec, ulong ticks) => spec.DurationTicks = ticks;
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.SetPeriodTicks(PortableEffectSpecState spec, ulong ticks) => spec.PeriodTicks = ticks;
        GameplayEffectPreparedSpec<PortableEffectSpecState> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FixedScalar>.DescribeSpec(PortableEffectSpecState spec) => DescribeSpec(spec);
    }
}
