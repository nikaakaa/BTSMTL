using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed partial class FPGameplayEffectTarget
    {
        ActorId IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.ActorId => m_ActorId;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.ContextIsValid(SimulationGameplayEffectApplication application) => application.Context.IsValid;
        ActorId IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SourceActorId(SimulationGameplayEffectApplication application) => application.Context.SourceActorId;
        ActorId IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.TargetActorId(SimulationGameplayEffectApplication application) => application.Context.TargetActorId;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.IsPredicted(SimulationGameplayEffectApplication application) => application.Context.IsPredicted;
        ulong IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SourceActionInstanceId(SimulationGameplayEffectApplication application) => application.Context.SourceActionInstanceId;
        ulong IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.PredictionKey(SimulationGameplayEffectApplication application) => application.Context.PredictionKey;
        ulong IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.AuthoritativeInstanceId(SimulationGameplayEffectApplication application) => application.AuthoritativeInstanceId;
        uint IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.ApplicationDefinitionRevision(SimulationGameplayEffectApplication application) => application.DefinitionRevision;

        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.TryCreateSpec(
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

        uint IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.DefinitionRevision(PortableEffectSpecState spec) => spec.Definition.Revision;
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SuppliedParameterCount(SimulationGameplayEffectApplication application) => application.SetByCallerValues.Count;
        CanonicalTextValue IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SuppliedParameterId(SimulationGameplayEffectApplication application, int index) => application.SetByCallerValues[index].ParameterId;
        FP IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SuppliedParameterValue(SimulationGameplayEffectApplication application, int index) => application.SetByCallerValues[index].Value;
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.RequiredParameterCount(PortableEffectSpecState spec) => spec.Definition.SetByCallerParameters.Length;
        string IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.RequiredParameterId(PortableEffectSpecState spec, int index) => spec.Definition.SetByCallerParameters[index];
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.ContainsParameter(PortableEffectSpecState spec, string parameterId) => spec.SetByCaller.ContainsKey(parameterId);
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.AddParameter(PortableEffectSpecState spec, string parameterId, FP value) => spec.SetByCaller.Add(parameterId, value);
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SuppliedSourceAttributeCount(SimulationGameplayEffectApplication application) => application.SourceAttributeSnapshots.Count;
        CanonicalTextValue IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SuppliedSourceAttributeId(SimulationGameplayEffectApplication application, int index) => application.SourceAttributeSnapshots[index].AttributeId;
        CanonicalTextValue IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.RequiredSourceAttributeKey(PortableEffectSpecState spec, int index) => spec.Definition.Layout.SourceAttributeKey(index);
        FP IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SuppliedSourceAttributeValue(SimulationGameplayEffectApplication application, int index) => application.SourceAttributeSnapshots[index].Value;
        IReadOnlyList<string> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.RequiredSnapshotAttributes(PortableEffectSpecState spec, GameplayEffectAttributeSnapshotKind kind) =>
            kind == GameplayEffectAttributeSnapshotKind.Source
                ? spec.Definition.SourceSnapshotAttributes
                : spec.Definition.TargetSnapshotAttributes;

        FP IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.ReadTargetAttribute(string attributeId) =>
            m_State.RequireAttribute(attributeId).CurrentValue;

        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.AddSourceAttribute(PortableEffectSpecState spec, string attributeId, FP value) => spec.SourceAttributes.Add(attributeId, value);
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.AddTargetAttribute(PortableEffectSpecState spec, string attributeId, FP value) => spec.TargetAttributes.Add(attributeId, value);
        IReadOnlyList<string> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.TargetTags() => m_State.QueryTags;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.HasSourceTagSnapshot(SimulationGameplayEffectApplication application) => application.HasSourceTagSnapshot;
        int IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SourceTagCount(SimulationGameplayEffectApplication application) => application.HasSourceTagSnapshot ? application.SourceTagSnapshot.Count : 0;
        IReadOnlyList<string> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SourceTags(SimulationGameplayEffectApplication application) => application.SourceTagSnapshot;
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SetTargetTags(PortableEffectSpecState spec, IReadOnlyList<string> tags) => spec.TargetTags = (GameplayTagSnapshot)tags;
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SetSourceTags(PortableEffectSpecState spec, IReadOnlyList<string> tags) => spec.SourceTags = m_State.CaptureTags(tags);
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.RequiresDuration(PortableEffectSpecState spec) => spec.Definition.DurationPolicy == PortableEffectDurationPolicy.Duration;
        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.HasPeriod(PortableEffectSpecState spec) => spec.Definition.HasPeriod;

        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.TryResolveDurationTicks(PortableEffectSpecState spec, out ulong ticks)
        {
            ticks = 0;
            return TryResolveMagnitude(spec, spec.Definition.DurationMagnitude, out FP seconds, out _) &&
                   TrySecondsToTicks(seconds, out ticks);
        }

        bool IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.TryResolvePeriodTicks(PortableEffectSpecState spec, out ulong ticks)
        {
            ticks = 0;
            return TryResolveMagnitude(spec, spec.Definition.PeriodMagnitude, out FP seconds, out _) &&
                   TrySecondsToTicks(seconds, out ticks);
        }

        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SetDurationTicks(PortableEffectSpecState spec, ulong ticks) => spec.DurationTicks = ticks;
        void IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.SetPeriodTicks(PortableEffectSpecState spec, ulong ticks) => spec.PeriodTicks = ticks;
        GameplayEffectPreparedSpec<PortableEffectSpecState> IGameplayEffectApplicationAdmissionPort<SimulationGameplayEffectApplication, PortableEffectSpecState, FP>.DescribeSpec(PortableEffectSpecState spec) => DescribeSpec(spec);
    }
}
