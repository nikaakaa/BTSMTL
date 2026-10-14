using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed partial class FPGameplayEffectTarget
    {
        GameplayEffectPreparedSpec<PortableEffectSpecState> DescribeSpec(PortableEffectSpecState spec)
        {
            PortableEffectDefinition definition = spec.Definition;
            return new GameplayEffectPreparedSpec<PortableEffectSpecState>(
                new GameplayEffectControlDescriptor(
                    definition.Id,
                    definition.Revision,
                    definition.DurationPolicy switch
                    {
                        PortableEffectDurationPolicy.Instant => GameplayEffectDurationKind.Instant,
                        PortableEffectDurationPolicy.Duration => GameplayEffectDurationKind.Duration,
                        _ => GameplayEffectDurationKind.Infinite
                    },
                    definition.StackingPolicy switch
                    {
                        PortableEffectStackingPolicy.AggregateBySource => GameplayEffectStackingKind.BySource,
                        PortableEffectStackingPolicy.AggregateByTarget => GameplayEffectStackingKind.ByTarget,
                        _ => GameplayEffectStackingKind.None
                    },
                    definition.MaxStacks,
                    definition.DurationUpdate switch
                    {
                        PortableEffectDurationUpdatePolicy.Refresh => GameplayEffectDurationUpdateKind.Refresh,
                        PortableEffectDurationUpdatePolicy.Extend => GameplayEffectDurationUpdateKind.Extend,
                        _ => GameplayEffectDurationUpdateKind.Keep
                    },
                    definition.PeriodUpdate == PortableEffectPeriodUpdatePolicy.Reset
                        ? GameplayEffectPeriodUpdateKind.Reset
                        : GameplayEffectPeriodUpdateKind.Keep,
                    definition.OverflowPolicy switch
                    {
                        PortableEffectOverflowPolicy.ReplaceOldest => GameplayEffectOverflowKind.ReplaceOldest,
                        PortableEffectOverflowPolicy.ApplyOverflowEffects => GameplayEffectOverflowKind.ApplyOverflowEffects,
                        _ => GameplayEffectOverflowKind.Reject
                    },
                    definition.ExecuteOnApplication),
                ToCommon(spec.Context),
                spec,
                spec.DurationTicks,
                spec.PeriodTicks);
        }

        static GameplayEffectComponentDescriptor DescribeComponent(PortableEffectComponent component)
        {
            return component switch
            {
                PortableModifierComponent modifier => new GameplayEffectComponentDescriptor(
                    GameplayEffectComponentKind.Modifier,
                    modifierPhase: modifier.Application == PortableModifierApplication.BaseValue
                        ? GameplayEffectModifierPhase.BaseValue
                        : GameplayEffectModifierPhase.CurrentValue),
                PortableGrantedTagsComponent => new GameplayEffectComponentDescriptor(GameplayEffectComponentKind.GrantedTags),
                PortableBlockAbilitiesComponent => new GameplayEffectComponentDescriptor(GameplayEffectComponentKind.BlockAbilities),
                PortableReplacementSourceRequirementComponent => new GameplayEffectComponentDescriptor(GameplayEffectComponentKind.ReplacementSourceRequirement),
                PortableTagRequirementsComponent requirement => new GameplayEffectComponentDescriptor(
                    GameplayEffectComponentKind.TagRequirement,
                    requirementPhase: ToCommon(requirement.Phase)),
                PortableAttributeRequirementsComponent requirement => new GameplayEffectComponentDescriptor(
                    GameplayEffectComponentKind.AttributeRequirement,
                    requirementPhase: ToCommon(requirement.Phase)),
                PortableExecutionComponent => new GameplayEffectComponentDescriptor(GameplayEffectComponentKind.Execution),
                PortableAdditionalEffectsComponent => new GameplayEffectComponentDescriptor(GameplayEffectComponentKind.AdditionalEffects),
                PortableCueComponent cue => new GameplayEffectComponentDescriptor(
                    GameplayEffectComponentKind.Cue,
                    cueTrigger: ToCommon(cue.Trigger)),
                _ => throw new InvalidOperationException("Unsupported Gameplay Effect component descriptor.")
            };
        }

        static GameplayEffectRequirementPhase ToCommon(PortableRequirementPhase phase) => phase switch
        {
            PortableRequirementPhase.Application => GameplayEffectRequirementPhase.Application,
            PortableRequirementPhase.Ongoing => GameplayEffectRequirementPhase.Ongoing,
            PortableRequirementPhase.Removal => GameplayEffectRequirementPhase.Removal,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null)
        };

        static GameplayEffectCueTrigger ToCommon(PortableCueTrigger trigger) => trigger switch
        {
            PortableCueTrigger.OnActive => GameplayEffectCueTrigger.OnActive,
            PortableCueTrigger.WhileActive => GameplayEffectCueTrigger.WhileActive,
            PortableCueTrigger.Executed => GameplayEffectCueTrigger.Executed,
            PortableCueTrigger.Removed => GameplayEffectCueTrigger.Removed,
            PortableCueTrigger.Expired => GameplayEffectCueTrigger.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null)
        };

        static GameplayEffectAdditionalTrigger ToCommon(PortableAdditionalEffectTrigger trigger) => trigger switch
        {
            PortableAdditionalEffectTrigger.Applied => GameplayEffectAdditionalTrigger.Applied,
            PortableAdditionalEffectTrigger.Period => GameplayEffectAdditionalTrigger.Period,
            PortableAdditionalEffectTrigger.Removed => GameplayEffectAdditionalTrigger.Removed,
            PortableAdditionalEffectTrigger.Overflow => GameplayEffectAdditionalTrigger.Overflow,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null)
        };

        GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication> ToCommand(SimulationGameplayEffectLifecycleIngress ingress)
        {
            SimulationGameplayEffectApplication authoritative = default;
            if (ingress.Operation == SimulationGameplayEffectLifecycleOperation.Applied)
            {
                authoritative = SimulationGameplayEffectApplication.FromCompiled(
                    ingress.EffectId,
                    ingress.DefinitionRevision,
                    new SimulationGameplayEffectContext(
                        ingress.Context.SourceActorId,
                        ingress.Context.TargetActorId,
                        ingress.Context.SourceActionInstanceId,
                        ingress.Context.PredictionKey,
                        ingress.Context.GameplayResultId,
                        ingress.Context.SourceTick,
                        SimulationGameplayEffectApplicationMode.Confirmed,
                        ingress.Context.Scope, ingress.Context.Holder, ingress.Context.Success, ingress.Context.SuccessTarget),
                    ingress.SetByCallerValues,
                    ingress.SourceAttributeSnapshots,
                    ingress.SourceTagSnapshot);
            }
            return new GameplayEffectLifecycleCommand<SimulationGameplayEffectApplication>(
                ToCommon(ingress.Operation),
                ingress.EffectId,
                ingress.InstanceId,
                ToCommon(ingress.Context),
                ingress.StartTick,
                ingress.EndTick,
                ingress.StackCount,
                ingress.LifecycleRevision,
                authoritative,
                ingress.Scope,
                ingress.Correction);
        }

        static SimulationGameplayEffectContext ToTarget(GameplayEffectContextIdentity context, SimulationActionTargetSnapshot successTarget = default) =>
            new SimulationGameplayEffectContext(context.SourceActor, context.TargetActor,
                context.SourceActionInstanceId, context.PredictionKey, context.GameplayResultId,
                context.SourceTick, context.Predicted
                    ? SimulationGameplayEffectApplicationMode.Predicted
                    : SimulationGameplayEffectApplicationMode.Confirmed, context.Scope, context.Holder, context.Success, successTarget);

        static GameplayEffectContextIdentity ToCommon(SimulationGameplayEffectContext context)
        {
            return context.IsValid
                ? new GameplayEffectContextIdentity(
                    context.SourceActorId,
                    context.TargetActorId,
                    context.SourceActionInstanceId,
                    context.PredictionKey,
                    context.GameplayResultId,
                    context.SourceTick,
                    context.IsPredicted, context.Scope, context.Holder, context.Success)
                : default;
        }

        static GameplayEffectLifecycleKind ToCommon(SimulationGameplayEffectLifecycleOperation operation) => operation switch
        {
            SimulationGameplayEffectLifecycleOperation.Applied => GameplayEffectLifecycleKind.Applied,
            SimulationGameplayEffectLifecycleOperation.Confirmed => GameplayEffectLifecycleKind.Confirmed,
            SimulationGameplayEffectLifecycleOperation.Rejected => GameplayEffectLifecycleKind.Rejected,
            SimulationGameplayEffectLifecycleOperation.StackChanged => GameplayEffectLifecycleKind.StackChanged,
            SimulationGameplayEffectLifecycleOperation.Inhibited => GameplayEffectLifecycleKind.Inhibited,
            SimulationGameplayEffectLifecycleOperation.Resumed => GameplayEffectLifecycleKind.Resumed,
            SimulationGameplayEffectLifecycleOperation.PeriodExecuted => GameplayEffectLifecycleKind.PeriodExecuted,
            SimulationGameplayEffectLifecycleOperation.Removed => GameplayEffectLifecycleKind.Removed,
            SimulationGameplayEffectLifecycleOperation.Expired => GameplayEffectLifecycleKind.Expired,
            SimulationGameplayEffectLifecycleOperation.Corrected => GameplayEffectLifecycleKind.Corrected,
            SimulationGameplayEffectLifecycleOperation.Overflow => GameplayEffectLifecycleKind.Overflow,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

        static SimulationGameplayEffectLifecycleOperation ToTarget(GameplayEffectLifecycleKind lifecycle) => lifecycle switch
        {
            GameplayEffectLifecycleKind.Applied => SimulationGameplayEffectLifecycleOperation.Applied,
            GameplayEffectLifecycleKind.Confirmed => SimulationGameplayEffectLifecycleOperation.Confirmed,
            GameplayEffectLifecycleKind.Rejected => SimulationGameplayEffectLifecycleOperation.Rejected,
            GameplayEffectLifecycleKind.StackChanged => SimulationGameplayEffectLifecycleOperation.StackChanged,
            GameplayEffectLifecycleKind.Inhibited => SimulationGameplayEffectLifecycleOperation.Inhibited,
            GameplayEffectLifecycleKind.Resumed => SimulationGameplayEffectLifecycleOperation.Resumed,
            GameplayEffectLifecycleKind.PeriodExecuted => SimulationGameplayEffectLifecycleOperation.PeriodExecuted,
            GameplayEffectLifecycleKind.Removed => SimulationGameplayEffectLifecycleOperation.Removed,
            GameplayEffectLifecycleKind.Expired => SimulationGameplayEffectLifecycleOperation.Expired,
            GameplayEffectLifecycleKind.Corrected => SimulationGameplayEffectLifecycleOperation.Corrected,
            GameplayEffectLifecycleKind.Overflow => SimulationGameplayEffectLifecycleOperation.Overflow,
            _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null)
        };

        static SimulationGameplayEffectApplyResultCode ToTarget(GameplayEffectApplyResultKind kind) => kind switch
        {
            GameplayEffectApplyResultKind.Applied => SimulationGameplayEffectApplyResultCode.Applied,
            GameplayEffectApplyResultKind.Rejected => SimulationGameplayEffectApplyResultCode.Rejected,
            GameplayEffectApplyResultKind.MissingDefinition => SimulationGameplayEffectApplyResultCode.MissingDefinition,
            GameplayEffectApplyResultKind.InvalidContext => SimulationGameplayEffectApplyResultCode.InvalidContext,
            GameplayEffectApplyResultKind.InvalidPrediction => SimulationGameplayEffectApplyResultCode.InvalidPrediction,
            GameplayEffectApplyResultKind.MissingParameter => SimulationGameplayEffectApplyResultCode.MissingSetByCaller,
            GameplayEffectApplyResultKind.UndeclaredParameter => SimulationGameplayEffectApplyResultCode.UndeclaredSetByCaller,
            GameplayEffectApplyResultKind.RequirementFailed => SimulationGameplayEffectApplyResultCode.RequirementFailed,
            GameplayEffectApplyResultKind.OverflowRejected => SimulationGameplayEffectApplyResultCode.OverflowRejected,
            GameplayEffectApplyResultKind.DefinitionRevisionMismatch => SimulationGameplayEffectApplyResultCode.DefinitionRevisionMismatch,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        void EnsureWorkingState()
        {
            if (m_State == null)
                m_State = m_EffectState.GetGameplayEffectState(m_Scratch);
        }

        bool TrySecondsToTicks(FP seconds, out ulong ticks)
        {
            ticks = 0;
            if (seconds <= FP._0)
                return false;
            ulong wholeSeconds = (ulong)(seconds.RawValue >> FP.Precision);
            ulong fraction = (ulong)(seconds.RawValue & (FP.Raw._1 - 1));
            ticks = checked(wholeSeconds * (ulong)m_TickRate +
                (fraction * (ulong)m_TickRate + (ulong)FP.Raw._1 - 1) / (ulong)FP.Raw._1);
            return ticks > 0;
        }

    }
}
