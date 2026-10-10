using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal interface IFloat32AbilityDomainRuntimeFactory
    {
        Float32AbilityDomainRuntimeServices Create(
            Float32GameplayEffectRuntimeCatalog gameplayEffectCatalog,
            Float32GameplayAbilityExecutionAccess access,
            Float32AbilityExecutionFrame frame,
            Float32ActionStateStore actionStore,
            Float32HandleAllocator handles,
            EquipmentProgramLayout equipmentLayout,
            Float32AbilityExecutionWorkspace workspace);
    }

    internal readonly struct Float32AbilityDomainRuntimeServices
    {
        public Float32AbilityDomainRuntimeServices(
            Float32GameplayEffectOperationRuntime gameplayEffects,
            Float32EquipmentRuntime equipment)
        {
            GameplayEffects = gameplayEffects;
            Equipment = equipment;
        }

        public Float32GameplayEffectOperationRuntime GameplayEffects { get; }
        public Float32EquipmentRuntime Equipment { get; }
    }

    internal sealed class Float32AbilityDomainRuntimeFactory : IFloat32AbilityDomainRuntimeFactory
    {
        public Float32AbilityDomainRuntimeServices Create(
            Float32GameplayEffectRuntimeCatalog gameplayEffectCatalog,
            Float32GameplayAbilityExecutionAccess access,
            Float32AbilityExecutionFrame frame,
            Float32ActionStateStore actionStore,
            Float32HandleAllocator handles,
            EquipmentProgramLayout equipmentLayout,
            Float32AbilityExecutionWorkspace workspace)
        {
            Float32GameplayEffectOperationRuntime gameplayEffects = gameplayEffectCatalog == null
                ? null
                : new Float32GameplayEffectOperationRuntime(
                    access,
                    frame,
                    actionStore,
                    handles,
                    frame.Facts,
                    workspace.GameplayEffects);
            Float32EquipmentRuntime equipment = null;
            if (equipmentLayout != null)
            {
                equipment = new Float32EquipmentRuntime(
                    access,
                    frame,
                    actionStore,
                    handles,
                    gameplayEffects,
                    frame.Facts,
                    frame.Trace,
                    equipmentLayout);
            }
            return new Float32AbilityDomainRuntimeServices(gameplayEffects, equipment);
        }
    }

    internal sealed class Float32AbilityExecutionServiceFactory : IFloat32AbilityExecutionServiceFactory
    {
        readonly IAbilityTimelineRuntime m_TimelineRuntime;
        readonly IReadOnlyDictionary<CharacterSkillId, IFloat32AbilityActivationPort> m_ActionRuntimes;
        public Float32AbilityExecutionServiceFactory(
            IAbilityTimelineRuntime timelineRuntime,
            IReadOnlyDictionary<CharacterSkillId, IFloat32AbilityActivationPort> actionRuntimes)
        {
            m_TimelineRuntime = timelineRuntime;
            m_ActionRuntimes = actionRuntimes;
        }
        public Float32AbilityExecutionAssembly Create(
            Float32GameplayAbilityExecutionData executionData,
            Float32GameplayAbilityExecutionServices executionServices,
            IFloat32AbilityActionBindingProvider actionBindings,
            IFloat32AbilityDomainRuntimeFactory domainRuntimeFactory,
            EquipmentProgramLayout equipmentLayout,
            Float32AbilityExecutionFrame frame,
            Float32AbilityOperationControlRuntime control,
            Float32AbilityExecutionWorkspace workspace)
        {
            domainRuntimeFactory = domainRuntimeFactory ?? throw new ArgumentNullException(nameof(domainRuntimeFactory));
            Float32GameplayAbilityExecutionAccess access = executionServices.Access;
            Float32StatePort controlState = frame.CreateStatePort(
                "Control",
                executionServices.ControlPolicy);
            Float32ActionStateStore actionStore = new Float32ActionStateStore(access, frame);
            Float32InputRuntime input = new Float32InputRuntime(frame);
            Float32HandleAllocator handles = new Float32HandleAllocator(frame);
            Float32BlackboardRuntime blackboard = new Float32BlackboardRuntime(
                access,
                frame.CreateStatePort("Blackboard", executionServices.BlackboardPolicy),
                frame,
                actionStore,
                frame.Facts,
                frame.Trace,
                workspace);
            Float32AbilityDomainRuntimeServices domainServices = domainRuntimeFactory.Create(
                executionServices.GameplayEffectCatalog,
                access,
                frame,
                actionStore,
                handles,
                equipmentLayout,
                workspace);
            Float32GameplayEffectOperationRuntime gameplayEffects = domainServices.GameplayEffects;
            Float32EquipmentRuntime equipment = domainServices.Equipment;

            Float32ActionRuntime actions = new Float32ActionRuntime(
                access,
                actionBindings,
                m_ActionRuntimes,
                frame,
                input,
                actionStore,
                blackboard,
                gameplayEffects,
                gameplayEffects,
                handles,
                frame.Facts,
                frame.Trace,
                equipment,
                operation => !control.IsActive(operation) && !control.IsStopping(operation));
            Float32ValueRuntime values = new Float32ValueRuntime(
                access,
                input,
                actionStore,
                actions,
                gameplayEffects,
                equipment,
                blackboard,
                frame,
                controlState,
                workspace);
            Float32MotionAccumulator motion = new Float32MotionAccumulator(
                access,
                frame,
                workspace,
                workspace.TimelineMotionWarps,
                actionStore);
            Float32LocomotionRuntime locomotion = new Float32LocomotionRuntime(
                access,
                values,
                motion,
                frame);
            Float32TreeClipInvokerLink treeClipLink = control.TreeClipLink;
            var motionWarp = new Float32MotionWarpSubmitRuntime(access, frame, values, actionStore, m_TimelineRuntime, workspace.TimelineMotionWarps);
            Float32AbilityExecutionTarget target = new Float32AbilityExecutionTarget(
                access,
                controlState,
                frame.CreateOperationStateReset(),
                values,
                blackboard,
                actions,
                gameplayEffects,
                equipment,
                locomotion,
                motionWarp,
                frame.Facts,
                frame.Presentation,
                frame.Trace,
                m_TimelineRuntime,
                actionStore,
                workspace.TimelineAdvances,
                workspace.TimelineStops,
                frame,
                treeClipLink);
            var services = new Float32AbilityExecutionServiceSet(
                frame,
                target,
                actionStore,
                equipment,
                values,
                blackboard,
                workspace.TimelineAdvances,
                workspace.TimelineStops);
            control.Bind(services);
            Float32AbilityDomainRuntime domain = new Float32AbilityDomainRuntime(
                executionData.Binding,
                executionServices,
                actions,
                actionStore,
                control,
                workspace);
            return new Float32AbilityExecutionAssembly(
                input,
                actions,
                gameplayEffects,
                equipment,
                blackboard,
                motion,
                control,
                domain);
        }
    }
}
