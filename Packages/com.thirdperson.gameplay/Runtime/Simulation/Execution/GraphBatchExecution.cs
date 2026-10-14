using System;
using ThirdPersonPerformance.Instrumentation;

namespace ThirdPersonSimulation
{
    public enum GraphBatchExecutionMode : byte
    {
        Serial,
        Parallel,
        Auto
    }

    public readonly struct GraphBatchExecutionOptions
    {
        public GraphBatchExecutionOptions(GraphBatchExecutionMode mode, int parallelThreshold = 0)
        {
            if (mode > GraphBatchExecutionMode.Auto)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (mode == GraphBatchExecutionMode.Auto && parallelThreshold < 2)
                throw new ArgumentOutOfRangeException(nameof(parallelThreshold));
            Mode = mode;
            ParallelThreshold = parallelThreshold;
        }

        public GraphBatchExecutionMode Mode { get; }
        public int ParallelThreshold { get; }
    }

    public interface IGraphBatchWork
    {
        PerformanceInstrumentationContext GetInstrumentationContext(int instanceIndex);
        void Compute(int instanceIndex);
    }

    public interface IGraphBatchExecutionResource
    {
        int ExecutionUnits { get; }
        int FailureInstanceIndex { get; }
        void Prepare();
        // 宿主准备实际执行线程的采集存储与调用身份，不依赖调用线程的 ThreadStatic 传播。
        void Dispatch(IGraphBatchWork work, int instanceCount);
        // 包括派发部分失败：返回或抛出前，全部已派发调用结束，结果对调用线程可见。
        void Complete();
    }

    public sealed class GraphBatchExecution
    {
        readonly IGraphBatchExecutionResource m_Resource;

        public GraphBatchExecution(GraphBatchExecutionOptions options, int independentInstanceCount,
            IGraphBatchExecutionResource resource = null)
        {
            RequestedMode = options.Mode;
            IndependentInstanceCount = independentInstanceCount;
            ParallelThreshold = options.ParallelThreshold;
            if (options.Mode == GraphBatchExecutionMode.Serial)
            {
                ResolvedMode = GraphBatchExecutionMode.Serial;
                return;
            }
            ExecutionUnits = resource == null ? 0 : resource.ExecutionUnits;
            if (options.Mode == GraphBatchExecutionMode.Parallel && ExecutionUnits < 2)
                throw new InvalidOperationException("Parallel 图批次缺少宿主提供的实际并行执行资源。");
            ResolvedMode = options.Mode == GraphBatchExecutionMode.Parallel ||
                independentInstanceCount >= options.ParallelThreshold && ExecutionUnits >= 2
                ? GraphBatchExecutionMode.Parallel : GraphBatchExecutionMode.Serial;
            if (ResolvedMode == GraphBatchExecutionMode.Parallel)
            {
                resource.Prepare();
                m_Resource = resource;
            }
        }

        public GraphBatchExecutionMode RequestedMode { get; }
        public GraphBatchExecutionMode ResolvedMode { get; }
        public int IndependentInstanceCount { get; }
        public int ParallelThreshold { get; }
        public int ExecutionUnits { get; }
        public bool IsInFlight { get; private set; }
        public Exception LastFailure { get; private set; }
        public int FailureInstanceIndex { get; private set; } = -1;

        public void RequireIdle()
        {
            if (IsInFlight)
                throw new InvalidOperationException("批次 owner 必须等待已派发计算完成后才能改变实例范围或生命周期。");
        }

        public void ReportFailure(int instanceIndex, Exception failure)
        {
            FailureInstanceIndex = instanceIndex;
            LastFailure = failure;
        }

        public void Execute(IGraphBatchWork work, int instanceCount)
        {
            RequireIdle();
            LastFailure = null;
            FailureInstanceIndex = -1;
            IsInFlight = true;
            int serialInstanceIndex = -1;
            try
            {
                if (ResolvedMode == GraphBatchExecutionMode.Serial)
                    for (serialInstanceIndex = 0; serialInstanceIndex < instanceCount; serialInstanceIndex++)
                        work.Compute(serialInstanceIndex);
                else
                {
                    try { m_Resource.Dispatch(work, instanceCount); }
                    finally { m_Resource.Complete(); }
                }
            }
            catch (Exception exception)
            {
                FailureInstanceIndex = ResolvedMode == GraphBatchExecutionMode.Parallel
                    ? m_Resource.FailureInstanceIndex : serialInstanceIndex;
                LastFailure = exception;
                throw;
            }
            finally { IsInFlight = false; }
        }
    }
}
