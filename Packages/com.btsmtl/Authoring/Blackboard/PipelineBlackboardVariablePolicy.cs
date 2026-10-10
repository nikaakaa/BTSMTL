using System;

namespace BTSMTL.Authoring.Blackboard
{
    public static class PipelineBlackboardVariablePolicy
    {
        public static bool IsValid(PipelineBlackboardVariableScope scope, PipelineBlackboardVariableLifetime lifetime)
        {
            switch (scope)
            {
                case PipelineBlackboardVariableScope.Character:
                    return lifetime == PipelineBlackboardVariableLifetime.Config ||
                           lifetime == PipelineBlackboardVariableLifetime.Spawn ||
                           lifetime == PipelineBlackboardVariableLifetime.ManualClear;
                case PipelineBlackboardVariableScope.Graph:
                    return lifetime == PipelineBlackboardVariableLifetime.Config ||
                           lifetime == PipelineBlackboardVariableLifetime.GraphInstance;
                case PipelineBlackboardVariableScope.State:
                    return lifetime == PipelineBlackboardVariableLifetime.StateEnterToExit;
                case PipelineBlackboardVariableScope.ActionInstance:
                    return lifetime == PipelineBlackboardVariableLifetime.ActionInstance;
                case PipelineBlackboardVariableScope.Frame:
                    return lifetime == PipelineBlackboardVariableLifetime.Frame;
                default:
                    return false;
            }
        }

        public static PipelineBlackboardVariableLifetime DefaultLifetime(PipelineBlackboardVariableScope scope)
        {
            switch (scope)
            {
                case PipelineBlackboardVariableScope.Character:
                    return PipelineBlackboardVariableLifetime.Config;
                case PipelineBlackboardVariableScope.Graph:
                    return PipelineBlackboardVariableLifetime.Config;
                case PipelineBlackboardVariableScope.State:
                    return PipelineBlackboardVariableLifetime.StateEnterToExit;
                case PipelineBlackboardVariableScope.ActionInstance:
                    return PipelineBlackboardVariableLifetime.ActionInstance;
                case PipelineBlackboardVariableScope.Frame:
                    return PipelineBlackboardVariableLifetime.Frame;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
            }
        }
    }
}
