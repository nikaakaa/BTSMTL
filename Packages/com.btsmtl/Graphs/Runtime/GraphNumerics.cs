using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs
{
    // 数值方案在此实例化自己的强类型运行时；普通入口只经返回的生命周期接口调用。
    public interface IGraphRuntimeFactory
    {
        IGraphRuntimeCore<TContext> CreateGraphRuntime<TContext>(GraphRuntimeSetup setup) where TContext : struct;
    }

    // 图宿主数值方案：在执行器数值方案之上提供宿主值转换与状态布局。
    public interface IGraphNumerics<TValue> : IOrchestrationNumerics<TValue>, IGraphRuntimeFactory where TValue : unmanaged
    {
        TValue FromIdentity(OrchestrationIdentityValue identity);
        int EnumTypeContentId(in TValue value);
        Type ValueType(in TValue value, OrchestrationIdentityTable identities);
        TValue ToState(in GraphHostValue value, int enumTypeContentId, in OrchestrationIdentityValue identity);
        GraphHostValue FromState(in TValue value, Type enumType, string identity);

        OrchestrationExecutionStateLayout<TValue> CreateStateLayout(StableHash contentIdentity,
            NumericProfileId numericProfile, TargetAbiVersion targetAbi, int blackboardMetadataCapacity,
            int framesCapacity, int operationsCapacity, int rosterCapacity, int savedOutputsCapacity, int valuesCapacity);
    }

    // Encoding 为所选数值方案编排值对应的 IOrchestrationGraphContentEncoding，由集成层提供。
    public sealed class GraphRuntimeSetup
    {
        public GraphRuntimeSetup(BtsmtlGraphRoot source, GraphHostContract contract, object encoding,
            IReadOnlyList<IOrchestrationExtension> extensions)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            Encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            Extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        }

        public BtsmtlGraphRoot Source { get; }
        public GraphHostContract Contract { get; }
        public object Encoding { get; }
        public IReadOnlyList<IOrchestrationExtension> Extensions { get; }

        public IOrchestrationGraphContentEncoding<TValue> RequireEncoding<TValue>() where TValue : unmanaged =>
            Encoding as IOrchestrationGraphContentEncoding<TValue> ??
            throw new ArgumentException($"图内容编码 '{Encoding.GetType()}' 不支持编排值 '{typeof(TValue)}'。");
    }

    // 图级生命周期：不暴露编排值类型；需要直接读写编排值时使用 HostedGraphRuntime。
    public interface IGraphRuntimeCore<TContext> : IDisposable where TContext : struct
    {
        BtsmtlGraphRoot Source { get; }
        GraphHostContract Contract { get; }
        int SourceIdentityId { get; }
        GraphVariableContract VariableContract { get; }
        ulong ResetGeneration { get; }
        bool IsStarted { get; }
        bool IsFaulted { get; }
        bool IsPrepared { get; }
        GraphExecutionFailure LastFailure { get; }

        void Prepare(GraphInvocationContext invocation, IGraphHostContext hostContext);
        void Compute(in TContext context, bool captureValues = false);
        void Discard();
        GraphExecutionResult Adopt();
        GraphExecutionResult Execute(GraphInvocationContext invocation, IGraphHostContext hostContext, in TContext context);
        void Reset();

        event Action<int, int> RecordedStateCapacityRequired;
        void GetRecordedStateCapacity(out int byteCapacity, out int textReferenceCapacity);
        void WriteRecordedFrameState<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter;
        void RestoreRecordedFrameState(ref RecordedRuntimeStateReader reader);
        GraphVariableFrame PublishRecordedFrame();

        IReadOnlyList<GraphRuntimePort> RuntimePorts { get; }
        GraphRuntimePort RequireRuntimePort(string node, string port, bool input);
        void PrepareRuntimeObservation();
        void QueuePortTrigger(string node, string port, bool input);
        void MutateRuntimeVariable(string variableId, GraphHostValue value, GraphVariableMutation operation);
        GraphHostValue ReadRuntimeVariable(string variableId);
        bool IsRuntimeVariableLocked(string variableId);
    }

    // 普通入口：TContext 是扩展调用上下文，图输入另经 IGraphHostContext 提供，两者互不替代。
    public class GraphRuntime<TNumerics, TContext> : IDisposable
        where TNumerics : struct, IGraphRuntimeFactory
        where TContext : struct
    {
        readonly IGraphRuntimeCore<TContext> m_Core;

        public GraphRuntime(GraphRuntimeSetup setup) =>
            m_Core = default(TNumerics).CreateGraphRuntime<TContext>(setup ?? throw new ArgumentNullException(nameof(setup)));

        public BtsmtlGraphRoot Source => m_Core.Source;
        public GraphHostContract Contract => m_Core.Contract;
        public int SourceIdentityId => m_Core.SourceIdentityId;
        public GraphVariableContract VariableContract => m_Core.VariableContract;
        public ulong ResetGeneration => m_Core.ResetGeneration;
        public bool IsStarted => m_Core.IsStarted;
        public bool IsFaulted => m_Core.IsFaulted;
        public bool IsPrepared => m_Core.IsPrepared;
        public GraphExecutionFailure LastFailure => m_Core.LastFailure;

        public void Prepare(GraphInvocationContext invocation, IGraphHostContext hostContext) =>
            m_Core.Prepare(invocation, hostContext);

        public void Compute(in TContext context, bool captureValues = false) => m_Core.Compute(in context, captureValues);
        public void Discard() => m_Core.Discard();
        public GraphExecutionResult Adopt() => m_Core.Adopt();

        public GraphExecutionResult Execute(GraphInvocationContext invocation, IGraphHostContext hostContext,
            in TContext context) => m_Core.Execute(invocation, hostContext, in context);

        public void Reset() => m_Core.Reset();
        public void Dispose() => m_Core.Dispose();

        public event Action<int, int> RecordedStateCapacityRequired
        {
            add => m_Core.RecordedStateCapacityRequired += value;
            remove => m_Core.RecordedStateCapacityRequired -= value;
        }

        public void GetRecordedStateCapacity(out int byteCapacity, out int textReferenceCapacity) =>
            m_Core.GetRecordedStateCapacity(out byteCapacity, out textReferenceCapacity);

        public void WriteRecordedFrameState<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter =>
            m_Core.WriteRecordedFrameState(ref writer);

        public void RestoreRecordedFrameState(ref RecordedRuntimeStateReader reader) =>
            m_Core.RestoreRecordedFrameState(ref reader);

        public GraphVariableFrame PublishRecordedFrame() => m_Core.PublishRecordedFrame();

        public IReadOnlyList<GraphRuntimePort> RuntimePorts => m_Core.RuntimePorts;
        public GraphRuntimePort RequireRuntimePort(string node, string port, bool input) => m_Core.RequireRuntimePort(node, port, input);
        public void PrepareRuntimeObservation() => m_Core.PrepareRuntimeObservation();
        public void QueuePortTrigger(string node, string port, bool input) => m_Core.QueuePortTrigger(node, port, input);

        public void MutateRuntimeVariable(string variableId, GraphHostValue value, GraphVariableMutation operation) =>
            m_Core.MutateRuntimeVariable(variableId, value, operation);

        public GraphHostValue ReadRuntimeVariable(string variableId) => m_Core.ReadRuntimeVariable(variableId);
        public bool IsRuntimeVariableLocked(string variableId) => m_Core.IsRuntimeVariableLocked(variableId);
    }
}
