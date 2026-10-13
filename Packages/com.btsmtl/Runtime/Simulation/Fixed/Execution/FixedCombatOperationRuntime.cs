using System;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedCombatOperationRuntime : FixedOperationModule
    {
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly IFixedActionContextReader m_Actions;
        readonly FixedGameplayEffectTarget m_GameplayEffects;
        readonly FixedFactSink m_Facts;

        public FixedCombatOperationRuntime(FixedGameplayAbilityExecutionAccess access,
            FixedAbilityExecutionFrame frame, IFixedActionContextReader actions,
            FixedGameplayEffectTarget gameplayEffects) : base(access)
        {
            m_Frame = frame;
            m_Actions = actions;
            m_GameplayEffects = gameplayEffects;
            m_Facts = frame.Facts;
        }

        public bool SubmitAttack(SimulationOperation operation)
        {
            ProgramCatalogEntry catalog = RequireCatalog(operation, ProgramCatalogEntryKind.CombatAttack);
            PortableAttackDefinition definition = Access.Services.CombatCatalog.RequireAttack(catalog.Identity);
            FixedActionInstanceState action;
            if (!m_Actions.TryGetCurrentSkillExecution(out action) || !action.IsActive)
                throw new InvalidOperationException("Attack query has no owning Action instance.");
            AbilityTreeClipInvocation clip = m_Frame.TreeClipInvocation;
            GameplayEffectScope scope = GameplayEffectScope.Action(action.SourceContext);
            GameplayEffectHolderIdentity holder = ClipHolder(clip, action.SkillExecutionGeneration);
            if (!m_GameplayEffects.TryFindEffect(GetStringConstant(operation, OperationNamedConstant.AttackIntervalEffect, string.Empty),
                    scope, out ulong intervalHandle, out _, holder))
                return false;
            FixedCombatQuery query = m_Frame.CombatQueries.Append();
            query.IntervalEffectHandle = intervalHandle;
            query.IntervalScope = scope;
            query.IntervalHolder = holder;
            query.Identity = new CombatAttackIdentity(action.InstanceId, action.SegmentGeneration,
                action.SkillId, operation.Handle, clip.ClipAuthoringId,
                clip.PlaybackGeneration, clip.BranchRevision, clip.Cycle, clip.TimelineRuntimeHandle);
            query.Action = new TimelineActionContextIdentity(action.ActionId, action.ContextId,
                action.InstanceId, action.PredictionKey, action.TargetSnapshot.TargetId, action.SkillId,
                action.SkillEntryOperation, action.SkillExecutionGeneration);
            query.Definition = definition;
            query.NodeId = clip.NodeAuthoringId;
            query.AttackEventId = GetStringConstant(operation, OperationNamedConstant.AttackEventId, string.Empty);
            query.Origin = m_Frame.BodyFacts.Position;
            query.Yaw = m_Frame.BodyFacts.Yaw;
            query.PreviousTime = clip.PreviousTime;
            query.Time = clip.Time;
            query.WindowStartTime = clip.WindowStartTime;
            query.BodyStartFraction = clip.BodyStartFraction;
            query.BodyEndFraction = clip.BodyEndFraction;
            query.Header = m_Facts.Next(operation);
            query.RequestId = query.Header.Sequence;
            return true;
        }

        static GameplayEffectHolderIdentity ClipHolder(in AbilityTreeClipInvocation invocation, ulong executionGeneration) =>
            GameplayEffectHolderIdentity.Invocation(invocation.ActionInstanceId, executionGeneration,
                invocation.TimelineRuntimeHandle, invocation.ClipAuthoringId, invocation.PlaybackGeneration,
                invocation.BranchRevision, invocation.Cycle);
    }
}
