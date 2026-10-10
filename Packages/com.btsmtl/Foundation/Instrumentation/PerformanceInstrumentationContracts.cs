using System;
using System.Diagnostics;

namespace ThirdPersonPerformance.Instrumentation
{
    [Conditional(PerformanceInstrumentationIdentity.Define)]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class PerformanceProbeAttribute : Attribute
    {
        public PerformanceProbeAttribute(string metricId)
        {
            MetricId = metricId ?? string.Empty;
        }

        public string MetricId { get; }
    }

    public enum PerformanceInstrumentationMode : byte
    {
        Disabled = 0,
        MarkerOnly = 1,
        Span = 2
    }

    public enum PerformanceProbeEndState : byte
    {
        Completed = 1,
        Exception = 2
    }

    public enum PerformanceRecordKind : byte
    {
        Duration = 0,
        Counter = 1
    }

    [Flags]
    public enum PerformanceInstrumentationContextFlags : byte
    {
        None = 0,
        RenderFrame = 1,
        LogicTick = 2,
        Actor = 4,
        Program = 8,
        Pipeline = 16
    }

    public readonly struct PerformanceInstrumentationContext
    {
        public PerformanceInstrumentationContext(
            ulong renderFrame,
            ulong logicTick,
            ulong actorId,
            ulong programIdentity,
            ulong pipelineIdentity,
            PerformanceInstrumentationContextFlags flags)
        {
            RenderFrame = renderFrame;
            LogicTick = logicTick;
            ActorId = actorId;
            ProgramIdentity = programIdentity;
            PipelineIdentity = pipelineIdentity;
            Flags = flags;
        }

        public ulong RenderFrame { get; }
        public ulong LogicTick { get; }
        public ulong ActorId { get; }
        public ulong ProgramIdentity { get; }
        public ulong PipelineIdentity { get; }
        public PerformanceInstrumentationContextFlags Flags { get; }
        public bool HasRenderFrame => (Flags & PerformanceInstrumentationContextFlags.RenderFrame) != 0;
        public bool HasLogicTick => (Flags & PerformanceInstrumentationContextFlags.LogicTick) != 0;
        public bool HasActor => (Flags & PerformanceInstrumentationContextFlags.Actor) != 0;
    }

    public static class PerformanceInstrumentationIdentity
    {
        public const string Define = "THIRDPERSON_PERFORMANCE_INSTRUMENTATION";
        public const string ContractsAssembly = "ThirdPerson.Performance.Instrumentation.Contracts";
        public const string RuntimeAssembly = "ThirdPerson.Performance.Instrumentation.Runtime";
        public const string ManifestSchema = "third-person-performance-instrumentation/1";
        public const string SpanFileSchema = "third-person-performance-instrumentation-spans/3";
        public const string WeaverVersion = "1.0.3";
        public const int SpanLayoutRevision = 3;
        public const ulong Unavailable = 0;

        public static ulong Hash64(string value)
        {
            if (value == null)
                return 0UL;
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
