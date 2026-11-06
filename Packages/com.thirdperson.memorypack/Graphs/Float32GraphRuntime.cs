using System;
using System.Collections.Generic;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;

namespace ThirdPersonSimulation.Serialization
{
    // 正式 Float32 图入口：接收已发布内容，无扩展、无扩展上下文。
    public sealed class Float32GraphRuntime : GraphRuntime<Float32GraphNumerics, ValueTuple>
    {
        public Float32GraphRuntime(OrchestrationGraphContent<Float32OrchestrationValue> content, GraphHostContract contract)
            : base(new GraphRuntimeSetup(contract, content,
                Array.Empty<IOrchestrationExtension>())) { }

        public void Compute(bool captureValues = false) => Compute(default, captureValues);

        public GraphExecutionResult Execute(GraphInvocationContext invocation, IGraphHostContext hostContext) =>
            Execute(invocation, hostContext, default);

        public static OrchestrationGraphContent<Float32OrchestrationValue> ReadPublishedContent(
            IOrchestrationGraphContentSource source, string numericTarget)
        {
            if (source.Identity.Source.NumericTarget != numericTarget)
                throw new InvalidOperationException($"已发布图 '{source.Identity.Source.GraphId}' 的数值目标与宿主 '{numericTarget}' 不匹配。");
            return source.Load(MemoryPackGraphContentPublisher.Instance);
        }

        public static GraphHostContract CreatePublishedHostContract(OrchestrationGraphContent<Float32OrchestrationValue> content, string contractId,
            string revision, string updateEventId, IReadOnlyList<PipelineBlackboardVariableScope> blackboardScopes,
            IReadOnlyList<string> domains, string numericTarget) =>
            GraphHostContract.FromPublishedGraph<Float32OrchestrationValue, Float32GraphNumerics>(
                content, contractId, revision, updateEventId, blackboardScopes,
                domains, numericTarget);
    }

    // 带扩展的 Float32 图入口：TContext 为扩展调用上下文。
    public sealed class Float32GraphRuntime<TContext> : GraphRuntime<Float32GraphNumerics, TContext>
        where TContext : struct
    {
        public Float32GraphRuntime(OrchestrationGraphContent<Float32OrchestrationValue> content, GraphHostContract contract,
            IReadOnlyList<IOrchestrationExtension> extensions)
            : base(new GraphRuntimeSetup(contract, content, extensions)) { }
    }
}
