using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal interface IFPAbilityDomainRuntimeFactory
    {
        FPAbilityDomainRuntimeServices Create(
            FPGameplayEffectRuntimeCatalog gameplayEffectCatalog,
            FPGameplayAbilityExecutionAccess access,
            FPAbilityExecutionFrame frame,
            FPActionStateStore actionStore,
            FPHandleAllocator handles,
            EquipmentProgramLayout equipmentLayout,
            FPAbilityExecutionWorkspace workspace);
    }

    internal readonly struct FPAbilityDomainRuntimeServices
    {
        public FPAbilityDomainRuntimeServices(
            FPGameplayEffectOperationRuntime gameplayEffects,
            FPEquipmentRuntime equipment)
        {
            GameplayEffects = gameplayEffects;
            Equipment = equipment;
        }

        public FPGameplayEffectOperationRuntime GameplayEffects { get; }
        public FPEquipmentRuntime Equipment { get; }
    }

    internal sealed class FPAbilityDomainRuntimeFactory : IFPAbilityDomainRuntimeFactory
    {
        public FPAbilityDomainRuntimeServices Create(
            FPGameplayEffectRuntimeCatalog gameplayEffectCatalog,
            FPGameplayAbilityExecutionAccess access,
            FPAbilityExecutionFrame frame,
            FPActionStateStore actionStore,
            FPHandleAllocator handles,
            EquipmentProgramLayout equipmentLayout,
            FPAbilityExecutionWorkspace workspace)
        {
            FPGameplayEffectOperationRuntime gameplayEffects = gameplayEffectCatalog == null
                ? null
                : new FPGameplayEffectOperationRuntime(
                    access,
                    frame,
                    actionStore,
                    handles,
                    frame.Facts,
                    workspace.GameplayEffects);
            FPEquipmentRuntime equipment = null;
            if (equipmentLayout != null)
            {
                equipment = new FPEquipmentRuntime(
                    access,
                    frame,
                    actionStore,
                    handles,
                    gameplayEffects,
                    frame.Facts,
                    frame.Trace,
                    equipmentLayout);
            }
            return new FPAbilityDomainRuntimeServices(gameplayEffects, equipment);
        }
    }

    internal sealed class FPAbilityExecutionServiceFactory : IFPAbilityExecutionServiceFactory
    {
        readonly IAbilityTimelineRuntime m_TimelineRuntime;
        readonly IReadOnlyDictionary<CharacterSkillId, IFPAbilityActivationPort> m_ActionRuntimes;

        public FPAbilityExecutionServiceFactory(
            IAbilityTimelineRuntime timelineRuntime,
            IReadOnlyDictionary<CharacterSkillId, IFPAbilityActivationPort> actionRuntimes)
        {
            m_TimelineRuntime = timelineRuntime;
            m_ActionRuntimes = actionRuntimes;
        }

        public FPAbilityExecutionAssembly Create(
            FPGameplayAbilityExecutionData executionData,
            FPGameplayAbilityExecutionServices executionServices,
            IFPAbilityActionBindingProvider actionBindings,
            IFPAbilityDomainRuntimeFactory domainRuntimeFactory,
            EquipmentProgramLayout equipmentLayout,
            FPAbilityExecutionFrame frame,
            FPAbilityOperationControlRuntime control,
            FPAbilityExecutionWorkspace workspace)
        {
            FPGameplayAbilityExecutionAccess access = executionServices.Access;
            FPStatePort controlState = frame.CreateStatePort(
                "Control",
                executionServices.ControlPolicy);
            FPActionStateStore actionStore = new FPActionStateStore(access, frame);
            FPInputRuntime input = new FPInputRuntime(frame);
            FPHandleAllocator handles = new FPHandleAllocator(frame);
            FPBlackboardRuntime blackboard = new FPBlackboardRuntime(
                access,
                frame.CreateStatePort("Blackboard", executionServices.BlackboardPolicy),
                frame,
                actionStore,
                frame.Trace);
            FPAbilityDomainRuntimeServices domainServices = domainRuntimeFactory.Create(
                executionServices.GameplayEffectCatalog,
                access,
                frame,
                actionStore,
                handles,
                equipmentLayout,
                workspace);
            FPGameplayEffectOperationRuntime gameplayEffects = domainServices.GameplayEffects;
            FPEquipmentRuntime equipment = domainServices.Equipment;

            FPActionRuntime actions = new FPActionRuntime(
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
            FPValueRuntime values = new FPValueRuntime(
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
            FPMotionAccumulator motion = new FPMotionAccumulator(
                access,
                frame,
                workspace,
                workspace.TimelineMotionWarps,
                actionStore);
            FPLocomotionRuntime locomotion = new FPLocomotionRuntime(
                access,
                values,
                motion,
                frame);
            FPCombatOperationRuntime combat = executionServices.CombatCatalog == null ? null :
                new FPCombatOperationRuntime(access, frame, actionStore, workspace.GameplayEffects.Target);
            FPTreeClipInvokerLink treeClipLink = control.TreeClipLink;
            var motionWarp = new FPMotionWarpSubmitRuntime(access, frame, values, actionStore, m_TimelineRuntime, workspace.TimelineMotionWarps);
            FPAbilityExecutionTarget target = new FPAbilityExecutionTarget(
                access,
                controlState,
                frame.CreateOperationStateReset(),
                values,
                blackboard,
                actions,
                gameplayEffects,
                combat,
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
            var services = new FPAbilityExecutionServiceSet(
                frame,
                target,
                actionStore,
                equipment,
                values,
                workspace.TimelineAdvances,
                workspace.TimelineStops);
            control.Bind(services);
            FPAbilityDomainRuntime domain = new FPAbilityDomainRuntime(
                executionData.Binding,
                executionServices,
                actions,
                actionStore,
                control,
                blackboard,
                workspace);
            return new FPAbilityExecutionAssembly(
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
