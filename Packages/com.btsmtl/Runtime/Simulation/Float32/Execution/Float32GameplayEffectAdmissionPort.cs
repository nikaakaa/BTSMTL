using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal sealed partial class Float32GameplayEffectTarget
    {
        ActorId IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.ActorId => m_ActorId;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.ContextIsValid(SimulationGameplayEffectApplication application) => application.Context.IsValid;
        ActorId IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SourceActorId(SimulationGameplayEffectApplication application) => application.Context.SourceActorId;
        ActorId IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.TargetActorId(SimulationGameplayEffectApplication application) => application.Context.TargetActorId;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.IsPredicted(SimulationGameplayEffectApplication application) => application.Context.IsPredicted;
        ulong IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SourceActionInstanceId(SimulationGameplayEffectApplication application) => application.Context.SourceActionInstanceId;
        ulong IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.PredictionKey(SimulationGameplayEffectApplication application) => application.Context.PredictionKey;
        ulong IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.AuthoritativeInstanceId(SimulationGameplayEffectApplication application) => application.AuthoritativeInstanceId;
        uint IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.ApplicationDefinitionRevision(SimulationGameplayEffectApplication application) => application.DefinitionRevision;

        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.TryCreateSpec(
            SimulationGameplayEffectApplication application,
            out PortableEffectSpecState spec,
            out GameplayEffectApplyResult failure)
        {
            if (!m_State.Catalog.TryGetEffect(application.EffectId, out PortableEffectDefinition definition))
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
            if (application.Context.Scope.Kind != definition.Scope ||
                application.Context.Holder.Lifetime != definition.Lifetime)
            {
                spec = null;
                failure = new GameplayEffectApplyResult(GameplayEffectApplyResultKind.InvalidContext,
                    0, application.AuthoritativeInstanceId, "EffectScopeOrHolderMismatch");
                return false;
            }
            spec = m_Scratch.AcquireSpec(definition, application.Context);
            spec.SourceTagSnapshotProvided = application.HasSourceTagSnapshot;
            failure = default;
            return true;
        }

        uint IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.DefinitionRevision(PortableEffectSpecState spec) => spec.Definition.Revision;
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SuppliedParameterCount(SimulationGameplayEffectApplication application) => application.SetByCallerValues.Count;
        CanonicalTextValue IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SuppliedParameterId(SimulationGameplayEffectApplication application, int index) => application.SetByCallerValues[index].ParameterId;
        Float32Scalar IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SuppliedParameterValue(SimulationGameplayEffectApplication application, int index) => application.SetByCallerValues[index].Value;
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.RequiredParameterCount(PortableEffectSpecState spec) => spec.Definition.SetByCallerParameters.Length;
        string IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.RequiredParameterId(PortableEffectSpecState spec, int index) => spec.Definition.SetByCallerParameters[index];
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.ContainsParameter(PortableEffectSpecState spec, string parameterId) => spec.SetByCaller.ContainsKey(parameterId);
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.AddParameter(PortableEffectSpecState spec, string parameterId, Float32Scalar value) => spec.SetByCaller.Add(parameterId, value);
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SuppliedSourceAttributeCount(SimulationGameplayEffectApplication application) => application.SourceAttributeSnapshots.Count;
        CanonicalTextValue IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SuppliedSourceAttributeId(SimulationGameplayEffectApplication application, int index) => application.SourceAttributeSnapshots[index].AttributeId;
        CanonicalTextValue IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.RequiredSourceAttributeKey(PortableEffectSpecState spec, int index) => spec.Definition.Layout.SourceAttributeKey(index);
        Float32Scalar IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SuppliedSourceAttributeValue(SimulationGameplayEffectApplication application, int index) => application.SourceAttributeSnapshots[index].Value;
        IReadOnlyList<string> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.RequiredSnapshotAttributes(PortableEffectSpecState spec, GameplayEffectAttributeSnapshotKind kind) =>
            kind == GameplayEffectAttributeSnapshotKind.Source
                ? spec.Definition.SourceSnapshotAttributes
                : spec.Definition.TargetSnapshotAttributes;

        Float32Scalar IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.ReadTargetAttribute(string attributeId) =>
            m_State.RequireAttribute(attributeId).CurrentValue;

        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.AddSourceAttribute(PortableEffectSpecState spec, string attributeId, Float32Scalar value) => spec.SourceAttributes.Add(attributeId, value);
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.AddTargetAttribute(PortableEffectSpecState spec, string attributeId, Float32Scalar value) => spec.TargetAttributes.Add(attributeId, value);
        IReadOnlyList<string> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.TargetTags() => m_State.QueryTags;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.HasSourceTagSnapshot(SimulationGameplayEffectApplication application) => application.HasSourceTagSnapshot;
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SourceTagCount(SimulationGameplayEffectApplication application) => application.HasSourceTagSnapshot ? application.SourceTagSnapshot.Count : 0;
        IReadOnlyList<string> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SourceTags(SimulationGameplayEffectApplication application) => application.SourceTagSnapshot;
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SetTargetTags(PortableEffectSpecState spec, IReadOnlyList<string> tags) => spec.SetTargetTags((GameplayTagSnapshot)tags);
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SetSourceTags(PortableEffectSpecState spec, IReadOnlyList<string> tags) => spec.SetSourceTags(m_State.CaptureTags(tags));
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.RequiresDuration(PortableEffectSpecState spec) => spec.Definition.DurationPolicy == PortableEffectDurationPolicy.Duration;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.HasPeriod(PortableEffectSpecState spec) => spec.Definition.HasPeriod;

        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.TryResolveDurationTicks(PortableEffectSpecState spec, out ulong ticks)
        {
            ticks = 0;
            return TryResolveMagnitude(spec, spec.Definition.DurationMagnitude, out Float32Scalar seconds, out _) &&
                   TrySecondsToTicks(seconds, out ticks);
        }

        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.TryResolvePeriodTicks(PortableEffectSpecState spec, out ulong ticks)
        {
            ticks = 0;
            return TryResolveMagnitude(spec, spec.Definition.PeriodMagnitude, out Float32Scalar seconds, out _) &&
                   TrySecondsToTicks(seconds, out ticks);
        }

        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SetDurationTicks(PortableEffectSpecState spec, ulong ticks) => spec.DurationTicks = ticks;
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.SetPeriodTicks(PortableEffectSpecState spec, ulong ticks) => spec.PeriodTicks = ticks;
        GameplayEffectPreparedSpec<PortableEffectSpecState> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, Float32Scalar>.DescribeSpec(PortableEffectSpecState spec) => DescribeSpec(spec);
    }
}
