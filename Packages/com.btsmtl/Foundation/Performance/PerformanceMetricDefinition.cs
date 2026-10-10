using System;
using System.Collections.Generic;

namespace ThirdPersonPerformance
{
    public enum PerformanceMetricDomain : byte
    {
        Gameplay = 1,
        Session = 2,
        Simulation = 3,
        Presentation = 4,
        Unity = 5
    }

    public enum PerformanceSampleScope : byte
    {
        RenderFrame = 1,
        LogicTick = 2,
        Invocation = 3,
        Counter = 4
    }

    public enum PerformanceMetricUnit : byte
    {
        Nanoseconds = 1,
        Bytes = 2,
        Count = 3
    }

    public enum PerformanceMetricAggregation : byte
    {
        InclusiveDuration = 1,
        Sum = 2,
        LastValue = 3
    }

    [Serializable]
    public sealed class PerformanceMetricDefinition
    {
        public PerformanceMetricDefinition(
            string metricId,
            string profilerName,
            PerformanceMetricDomain domain,
            string parentId,
            PerformanceSampleScope sampleScope,
            PerformanceMetricUnit unit,
            PerformanceMetricAggregation aggregation)
        {
            if (string.IsNullOrWhiteSpace(metricId) || string.IsNullOrWhiteSpace(profilerName) ||
                !Enum.IsDefined(typeof(PerformanceMetricDomain), domain) ||
                !Enum.IsDefined(typeof(PerformanceSampleScope), sampleScope) ||
                !Enum.IsDefined(typeof(PerformanceMetricUnit), unit) ||
                !Enum.IsDefined(typeof(PerformanceMetricAggregation), aggregation))
            {
                throw new ArgumentException("Performance metric definition is incomplete.");
            }
            MetricId = metricId.Trim();
            ProfilerName = profilerName.Trim();
            Domain = domain;
            ParentId = parentId?.Trim() ?? string.Empty;
            SampleScope = sampleScope;
            Unit = unit;
            Aggregation = aggregation;
        }

        public string MetricId { get; }
        public string ProfilerName { get; }
        public PerformanceMetricDomain Domain { get; }
        public string ParentId { get; }
        public PerformanceSampleScope SampleScope { get; }
        public PerformanceMetricUnit Unit { get; }
        public PerformanceMetricAggregation Aggregation { get; }
    }

    public static class PerformanceMetricCatalogValidator
    {
        public static PerformanceMetricDefinition[] Validate(params IReadOnlyList<PerformanceMetricDefinition>[] catalogs)
        {
            if (catalogs == null)
                throw new ArgumentNullException(nameof(catalogs));
            var values = new List<PerformanceMetricDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int catalogIndex = 0; catalogIndex < catalogs.Length; catalogIndex++)
            {
                IReadOnlyList<PerformanceMetricDefinition> catalog = catalogs[catalogIndex] ??
                    throw new ArgumentException("Performance metric catalog is missing.", nameof(catalogs));
                for (int metricIndex = 0; metricIndex < catalog.Count; metricIndex++)
                {
                    PerformanceMetricDefinition metric = catalog[metricIndex] ??
                        throw new ArgumentException("Performance metric catalog contains a null metric.", nameof(catalogs));
                    if (!ids.Add(metric.MetricId) || !names.Add(metric.ProfilerName))
                        throw new InvalidOperationException($"Performance metric '{metric.MetricId}' or profiler name '{metric.ProfilerName}' is duplicated.");
                    values.Add(metric);
                }
            }
            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrEmpty(values[i].ParentId) && !ids.Contains(values[i].ParentId))
                    throw new InvalidOperationException($"Performance metric '{values[i].MetricId}' has an unknown parent '{values[i].ParentId}'.");
            }
            return values.ToArray();
        }
    }
}
