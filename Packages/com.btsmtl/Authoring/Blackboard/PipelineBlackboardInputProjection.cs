using System;
using System.Reflection;
using BTSMTL.Orchestration;

namespace BTSMTL.Authoring.Blackboard
{
    public enum PipelineBlackboardInputProjection
    {
        Direct,
        ActionTargetIdentity,
        ActionTargetPosition,
        ActionTargetYaw,
        [PipelineBlackboardProjectedObjectKind("gameplay.object/actor")]
        ActionTargetObjectHandle
    }

    // 句柄投影产出的对象种类；身份由领域种类声明解析，作者期读取。
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PipelineBlackboardProjectedObjectKindAttribute : Attribute
    {
        public PipelineBlackboardProjectedObjectKindAttribute(string objectKindId) => ObjectKindId = objectKindId;

        public string ObjectKindId { get; }
    }

    public static class PipelineBlackboardInputProjections
    {
        // 投影固定的句柄种类；不产出句柄的投影为 null。
        public static OrchestrationObjectKind ObjectKind(PipelineBlackboardInputProjection projection) =>
            typeof(PipelineBlackboardInputProjection).GetField(projection.ToString())?
                .GetCustomAttribute<PipelineBlackboardProjectedObjectKindAttribute>() is { } declared
                ? OrchestrationObjectKinds.Require(declared.ObjectKindId) : null;
    }
}
