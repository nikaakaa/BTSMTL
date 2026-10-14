using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal static class BlackboardScopeOwnerCalculation
    {
        internal static bool IsActiveGraphCallInput<TTarget>(OperationControlCursor<TTarget> cursor,
            IReadOnlyList<ProgramGraphCallFrame> frames, int valueSlot)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            for (int i = 0; i < frames.Count; i++)
            {
                ProgramGraphCallFrame frame = frames[i];
                if (!cursor.IsActive(frame.OwnerOperation))
                    continue;
                for (int binding = 0; binding < frame.Inputs.Count; binding++)
                    if (frame.Inputs[binding].StateSlot == valueSlot)
                        return true;
            }
            return false;
        }

        internal static BlackboardOwnerToken Resolve<TTarget>(OperationControlCursor<TTarget> cursor,
            OperationHandle operation, in SimulationBlackboardSlotGroup group,
            IGameplayAbilityExecutionServices services, ulong frameTick, bool actionActive, ulong actionInstance)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ProgramScopeLayout scope = group.Scope;
            ulong generation;
            switch (scope.Kind)
            {
                case ProgramScopeKind.Character:
                    generation = 1;
                    break;
                case ProgramScopeKind.Graph:
                    if (group.LifetimeKind == ProgramBlackboardLifetime.Config)
                        generation = 1;
                    else
                    {
                        if (!cursor.IsActive(scope.OwnerOperation))
                            throw new InvalidOperationException($"Blackboard access '{services.SourcePath(operation)}' is outside Graph scope '{scope.Identity}'.");
                        generation = cursor.ReadGeneration(scope.OwnerOperation);
                    }
                    break;
                case ProgramScopeKind.State:
                    if (!cursor.IsCurrentStateExecution(scope.OwnerOperation))
                        throw new InvalidOperationException($"Blackboard access '{services.SourcePath(operation)}' is outside State scope '{scope.Identity}'.");
                    generation = cursor.ReadGeneration(scope.OwnerOperation);
                    break;
                case ProgramScopeKind.ActionInstance:
                    if (!actionActive)
                        throw new InvalidOperationException($"Blackboard access '{services.SourcePath(operation)}' requires an explicit active Action Context.");
                    generation = actionInstance;
                    break;
                case ProgramScopeKind.Frame:
                    generation = frameTick;
                    break;
                default:
                    throw new InvalidOperationException($"Blackboard scope '{scope.Kind}' is unsupported.");
            }
            return new BlackboardOwnerToken(scope.Kind, group.CompiledOwnerIndex, generation);
        }
    }
}
