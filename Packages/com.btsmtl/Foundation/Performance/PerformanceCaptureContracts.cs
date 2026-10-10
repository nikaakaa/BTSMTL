using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ThirdPersonPerformance.Instrumentation;

namespace ThirdPersonPerformance
{
    public static class PerformanceCaptureSchemas
    {
        public const string FixedInputTrace = ThirdPersonSimulation.FixedInputTraceDocument.CurrentSchema;
        public const string Toolchain = "third-person-performance-toolchain/2";
        public const string Scenario = "third-person-performance-scenario/3";
        public const string CameraTrace = "third-person-performance-camera-trace/1";
        public const string Budget = "third-person-performance-budget/1";
        public const string Profile = "third-person-performance-profile/1";
        public const string Player = "third-person-performance-player/4";
        public const string BuildInputs = "third-person-performance-build-inputs/2";
        public const string Request = "third-person-performance-run-request/4";
        public const string Status = "third-person-performance-run-status/2";
        public const string RuntimeResult = "third-person-performance-runtime-result/5";
        public const string Gate = "third-person-performance-gate/1";
        public const string Manifest = "third-person-performance-capture/4";
        public const string Summary = "third-person-performance-summary/6";
        public const string Comparison = "third-person-performance-comparison/2";
        public const string AnalysisRequest = "third-person-performance-analysis-request/2";
        public const string Analysis = "third-person-performance-analysis/2";
        public const string InstrumentationManifest = PerformanceInstrumentationIdentity.ManifestSchema;
        public const string CollectorId = "windows-wpr-cpu/1";
        public const string TransportId = "loopback-tcp/1";
    }

    public static class PerformanceOperationKinds
    {
        public const string Smoke = "smoke";
        public const string Replay = "replay";
        public const string Capture = "capture";
    }

    public enum PerformanceCaptureStatus : byte
    {
        Pending = 1,
        WaitingForRuntime = 2,
        WarmingUp = 3,
        Recording = 4,
        Finalizing = 5,
        Completed = 6,
        Faulted = 7,
        Cancelled = 8,
        Replaying = 9
    }

    [Serializable]
    public sealed class PerformanceToolchainDocument
    {
        public string schema = PerformanceCaptureSchemas.Toolchain;
        public string collector_id = PerformanceCaptureSchemas.CollectorId;
        public string wpr_path = string.Empty;
        public string xperf_path = string.Empty;
        public string wpa_exporter_path = string.Empty;
        public string wpa_path = string.Empty;
        public string unity_profiler_path = string.Empty;
        public string unity_profiler_open_mode = string.Empty;
        public string symbol_cache_path = string.Empty;
        public string wpr_profile_path = string.Empty;
        public string wpa_profile_path = string.Empty;
        public string wpr_profile_hash = string.Empty;
        public string wpa_profile_hash = string.Empty;
        public string wpr_version = string.Empty;
        public string xperf_version = string.Empty;
        public string wpa_exporter_version = string.Empty;
        public string wpa_version = string.Empty;
        public string unity_version = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceCameraTraceSegmentDocument
    {
        public int first_frame;
        public int frame_count;
        public float x;
        public float y;
    }

    [Serializable]
    public sealed class PerformanceCameraTraceDocument
    {
        public string schema = PerformanceCaptureSchemas.CameraTrace;
        public string trace_id = string.Empty;
        public int revision;
        public string input_id = string.Empty;
        public int tick_rate;
        public int frame_count;
        public PerformanceCameraTraceSegmentDocument[] segments = Array.Empty<PerformanceCameraTraceSegmentDocument>();
        public string content_hash = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceScenarioDocument
    {
        public string schema = PerformanceCaptureSchemas.Scenario;
        public string scenario_id = string.Empty;
        public int revision;
        public string scene_path = string.Empty;
        public string runtime_id = string.Empty;
        public string actor_id = string.Empty;
        public string roster_identity = string.Empty;
        public string ready_condition = string.Empty;
        public string capture_start_boundary = string.Empty;
        public string capture_end_boundary = string.Empty;
        public string input_trace_path = string.Empty;
        public string input_trace_hash = string.Empty;
        public string camera_trace_path = string.Empty;
        public string camera_trace_hash = string.Empty;
        public int warmup_logic_ticks;
        public int capture_logic_ticks;
        public int width;
        public int height;
        public int quality_level;
        public int v_sync_count;
        public int target_frame_rate;
        public string content_hash = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceMetricBudgetDocument
    {
        public string metric_id = string.Empty;
        public double p95_limit;
        public double p99_limit;
        public string unit = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceBudgetDocument
    {
        public string schema = PerformanceCaptureSchemas.Budget;
        public string budget_id = string.Empty;
        public int revision;
        public double target_fps;
        public int maximum_dropped_logic_ticks;
        public PerformanceMetricBudgetDocument[] metrics = Array.Empty<PerformanceMetricBudgetDocument>();
        public string content_hash = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceCaptureProfileDocument
    {
        public string schema = PerformanceCaptureSchemas.Profile;
        public string profile_id = string.Empty;
        public int revision;
        public int maximum_presentation_fps;
        public int logic_tick_rate;
        public int sample_capacity_margin_percent;
        public int instrumentation_span_capacity;
        public int runtime_ready_timeout_seconds;
        public int capture_timeout_seconds;
        public string content_hash = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceFileDocument
    {
        public string role = string.Empty;
        public string path = string.Empty;
        public long size;
        public string sha256 = string.Empty;
    }

    [Serializable]
    public sealed class PerformancePlayerManifestDocument
    {
        public string schema = PerformanceCaptureSchemas.Player;
        public string build_inputs_hash = string.Empty;
        public string build_id = string.Empty;
        public string unity_version = string.Empty;
        public string build_target = string.Empty;
        public string scripting_backend = string.Empty;
        public string build_mode = string.Empty;
        public string scene_path = string.Empty;
        public string executable_path = string.Empty;
        public string scenario_catalog_path = string.Empty;
        public string content_identity = string.Empty;
        public string pipeline_identity = string.Empty;
        public string pose_graph_revision = string.Empty;
        public string solver_identity = string.Empty;
        public string instrumentation_identity = string.Empty;
        public string instrumentation_mode = string.Empty;
        public int instrumentation_span_layout_revision;
        public string instrumentation_manifest_path = string.Empty;
        public PerformanceFileDocument[] files = Array.Empty<PerformanceFileDocument>();
    }

    [Serializable]
    public sealed class PerformanceRunRequestDocument
    {
        public string schema = PerformanceCaptureSchemas.Request;
        public string operation = string.Empty;
        public string run_id = string.Empty;
        public string request_document_path = string.Empty;
        public string transport = PerformanceCaptureSchemas.TransportId;
        public string staging_root = string.Empty;
        public string result_root = string.Empty;
        public string player_manifest_path = string.Empty;
        public string scenario_path = string.Empty;
        public string scenario_hash = string.Empty;
        public string budget_path = string.Empty;
        public string budget_hash = string.Empty;
        public string profile_path = string.Empty;
        public string profile_hash = string.Empty;
        public string toolchain_path = string.Empty;
        public string smoke_manifest_path = string.Empty;
        public string smoke_manifest_hash = string.Empty;
        public string replay_manifest_path = string.Empty;
        public string replay_manifest_hash = string.Empty;
        public string baseline_manifest_path = string.Empty;
        public string instrumentation_identity = string.Empty;
        public string instrumentation_mode = string.Empty;
        public bool record_allocation_callstacks;
        public string cancel_path = string.Empty;
        public string status_path = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceRunStatusDocument
    {
        public string schema = PerformanceCaptureSchemas.Status;
        public string operation = string.Empty;
        public string run_id = string.Empty;
        public string status = string.Empty;
        public string stage = string.Empty;
        public string message = string.Empty;
        public string updated_utc = string.Empty;
        public int controller_process_id;
        public int player_process_id;
    }

    [Serializable]
    public sealed class PerformanceProcessDocument
    {
        public int player_process_id;
        public int player_exit_code;
        public int controller_process_id;
        public int controller_exit_code;
        public string wpr_instance = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceRuntimeResultDocument
    {
        public string schema = PerformanceCaptureSchemas.RuntimeResult;
        public string operation = string.Empty;
        public string run_id = string.Empty;
        public string status = string.Empty;
        public string stage = string.Empty;
        public string message = string.Empty;
        public string started_utc = string.Empty;
        public string completed_utc = string.Empty;
        public int presentation_frames;
        public int logic_ticks;
        public int dropped_logic_ticks;
        public double capture_seconds;
        public int main_thread_native_id;
        public int main_thread_managed_id;
        public string metric_catalog_revision = string.Empty;
        public string operating_system = string.Empty;
        public string processor = string.Empty;
        public string graphics_device = string.Empty;
        public string graphics_api = string.Empty;
        public string instrumentation_identity = string.Empty;
        public string instrumentation_mode = string.Empty;
        public int instrumentation_span_layout_revision;
        public int instrumentation_record_count;
        public int instrumentation_record_capacity;
        public string instrumentation_span_path = string.Empty;
        public string instrumentation_span_hash = string.Empty;
        public bool allocation_callstacks_recorded;
        public string start_body_hash = string.Empty;
        public string input_sequence_hash = string.Empty;
        public string body_trajectory_hash = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceGateManifestDocument
    {
        public string schema = PerformanceCaptureSchemas.Gate;
        public string operation = string.Empty;
        public string run_id = string.Empty;
        public string status = string.Empty;
        public string stage = string.Empty;
        public string message = string.Empty;
        public string created_utc = string.Empty;
        public string completed_utc = string.Empty;
        public string scenario_id = string.Empty;
        public string scenario_hash = string.Empty;
        public string build_id = string.Empty;
        public string instrumentation_identity = string.Empty;
        public string instrumentation_mode = string.Empty;
        public int instrumentation_span_layout_revision;
        public string player_manifest_hash = string.Empty;
        public string profile_hash = string.Empty;
        public string predecessor_gate_hash = string.Empty;
        public string input_trace_hash = string.Empty;
        public string camera_trace_hash = string.Empty;
        public int warmup_logic_ticks;
        public int capture_logic_ticks;
        public string start_body_hash = string.Empty;
        public string input_sequence_hash = string.Empty;
        public string body_trajectory_hash = string.Empty;
        public PerformanceProcessDocument process = new PerformanceProcessDocument();
        public PerformanceFileDocument[] files = Array.Empty<PerformanceFileDocument>();
    }

    [Serializable]
    public sealed class PerformanceMetricDefinitionDocument
    {
        public string metric_id = string.Empty;
        public string profiler_name = string.Empty;
        public string domain = string.Empty;
        public string parent_id = string.Empty;
        public string sample_scope = string.Empty;
        public string unit = string.Empty;
        public string aggregation = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceMetricCatalogDocument
    {
        public string schema = "third-person-performance-metric-catalog/1";
        public string revision = string.Empty;
        public PerformanceMetricDefinitionDocument[] metrics = Array.Empty<PerformanceMetricDefinitionDocument>();
    }

    [Serializable]
    public sealed class PerformanceDistributionDocument
    {
        public int sample_count;
        public long invocation_count;
        public double mean;
        public double p50;
        public double p95;
        public double p99;
        public double max;
    }

    [Serializable]
    public sealed class PerformanceMetricSummaryDocument
    {
        public string metric_id = string.Empty;
        public string profiler_name = string.Empty;
        public string parent_id = string.Empty;
        public string sample_scope = string.Empty;
        public string unit = string.Empty;
        public PerformanceDistributionDocument distribution = new PerformanceDistributionDocument();
        public double mean_per_invocation;
        public double total;
        public double parent_inclusive_total_ratio;
        public bool budget_exceeded;
    }

    [Serializable]
    public sealed class PerformanceInstrumentationSpanSampleDocument
    {
        public ulong sequence;
        public string actor_id = string.Empty;
        public ulong render_frame;
        public ulong logic_tick;
        public long started_at_ticks;
        public long duration_ticks;
        public int managed_thread_id;
        public double duration_milliseconds;
        public string end_state = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceInstrumentationActorSummaryDocument
    {
        public string actor_id = string.Empty;
        public long invocation_count;
        public long exception_count;
        public PerformanceDistributionDocument distribution = new PerformanceDistributionDocument();
    }

    [Serializable]
    public sealed class PerformanceInstrumentationPointSummaryDocument
    {
        public string point_id = string.Empty;
        public string metric_id = string.Empty;
        public string assembly = string.Empty;
        public string declaring_type = string.Empty;
        public string method = string.Empty;
        public string source_file = string.Empty;
        public int source_line;
        public PerformanceDistributionDocument distribution = new PerformanceDistributionDocument();
        public long exception_count;
        public PerformanceInstrumentationActorSummaryDocument[] actors = Array.Empty<PerformanceInstrumentationActorSummaryDocument>();
        public PerformanceInstrumentationSpanSampleDocument[] slowest_samples = Array.Empty<PerformanceInstrumentationSpanSampleDocument>();
    }

    [Serializable]
    public sealed class PerformanceWorkloadSummaryDocument
    {
        public string metric_id = string.Empty;
        public string status = string.Empty;
        public PerformanceDistributionDocument per_render_frame = new PerformanceDistributionDocument();
        public PerformanceInstrumentationActorSummaryDocument[] actors = Array.Empty<PerformanceInstrumentationActorSummaryDocument>();
    }

    [Serializable]
    public sealed class PerformanceFunctionHotspotDocument
    {
        public string process = string.Empty;
        public string thread = string.Empty;
        public string module = string.Empty;
        public string function = string.Empty;
        public double inclusive_samples;
        public double exclusive_samples;
    }

    [Serializable]
    public sealed class PerformanceThreadHotspotDocument
    {
        public string process = string.Empty;
        public string thread = string.Empty;
        public double exclusive_samples;
    }

    [Serializable]
    public sealed class PerformanceWaitEvidenceDocument
    {
        public string process = string.Empty;
        public string thread = string.Empty;
        public double ready_time;
        public double wait_time;
        public long context_switches;
    }

    [Serializable]
    public sealed class PerformanceSummaryDocument
    {
        public double capture_seconds;
        public double unresolved_exclusive_samples;
        public double total_exclusive_samples;
        public string schema = PerformanceCaptureSchemas.Summary;
        public string capture_id = string.Empty;
        public string status = string.Empty;
        public double presentation_fps;
        public double target_fps;
        public bool fps_budget_exceeded;
        public double logic_ticks_per_second;
        public int dropped_logic_ticks;
        public bool budget_passed;
        public int budget_exceeded_count;
        public string instrumentation_mode = string.Empty;
        public bool allocation_callstacks_recorded;
        public string timing_basis = string.Empty;
        public long timestamp_frequency;
        public int instrumentation_record_count;
        public int instrumentation_record_capacity;
        public int main_thread_native_id;
        public int main_thread_managed_id;
        public bool budget_evaluated;
        public string[] unavailable_budget_metrics = Array.Empty<string>();
        public PerformanceDistributionDocument logic_tick_render_frame_distribution = new PerformanceDistributionDocument();
        public PerformanceMetricSummaryDocument[] metrics = Array.Empty<PerformanceMetricSummaryDocument>();
        public PerformanceInstrumentationPointSummaryDocument[] instrumentation_points = Array.Empty<PerformanceInstrumentationPointSummaryDocument>();
        public PerformanceWorkloadSummaryDocument[] workloads = Array.Empty<PerformanceWorkloadSummaryDocument>();
        public PerformanceFunctionHotspotDocument[] hotspots = Array.Empty<PerformanceFunctionHotspotDocument>();
        public PerformanceThreadHotspotDocument[] thread_hotspots = Array.Empty<PerformanceThreadHotspotDocument>();
        public PerformanceWaitEvidenceDocument[] wait_evidence = Array.Empty<PerformanceWaitEvidenceDocument>();
    }

    [Serializable]
    public sealed class PerformanceComparisonMetricDocument
    {
        public string metric_id = string.Empty;
        public string unit = string.Empty;
        public string sample_scope = string.Empty;
        public string status = string.Empty;
        public bool percent_delta_available;
        public int baseline_sample_count;
        public int candidate_sample_count;
        public double baseline_p95;
        public double candidate_p95;
        public double absolute_delta;
        public double percent_delta;
    }

    [Serializable]
    public sealed class PerformanceHotspotComparisonDocument
    {
        public string status = string.Empty;
        public bool percent_delta_available;
        public double baseline_samples_per_second;
        public double candidate_samples_per_second;
        public string thread = string.Empty;
        public string module = string.Empty;
        public string function = string.Empty;
        public double baseline_inclusive_samples;
        public double candidate_inclusive_samples;
        public double absolute_delta;
        public double percent_delta;
    }

    [Serializable]
    public sealed class PerformanceInstrumentationPointComparisonDocument
    {
        public string status = string.Empty;
        public bool percent_delta_available;
        public int baseline_sample_count;
        public int candidate_sample_count;
        public string point_id = string.Empty;
        public string metric_id = string.Empty;
        public double baseline_p95;
        public double candidate_p95;
        public double absolute_delta;
        public double percent_delta;
        public long baseline_invocation_count;
        public long candidate_invocation_count;
    }

    [Serializable]
    public sealed class PerformanceComparisonDocument
    {
        public string schema = PerformanceCaptureSchemas.Comparison;
        public string baseline_manifest_path = string.Empty;
        public string baseline_manifest_hash = string.Empty;
        public string baseline_capture_id = string.Empty;
        public string candidate_capture_id = string.Empty;
        public string status = string.Empty;
        public string message = string.Empty;
        public bool baseline_budget_passed;
        public bool candidate_budget_passed;
        public bool baseline_budget_evaluated;
        public bool candidate_budget_evaluated;
        public bool budget_delta_available;
        public string[] warnings = Array.Empty<string>();
        public int budget_exceeded_delta;
        public PerformanceComparisonMetricDocument[] metrics = Array.Empty<PerformanceComparisonMetricDocument>();
        public PerformanceInstrumentationPointComparisonDocument[] instrumentation_points = Array.Empty<PerformanceInstrumentationPointComparisonDocument>();
        public PerformanceHotspotComparisonDocument[] hotspots = Array.Empty<PerformanceHotspotComparisonDocument>();
    }

    [Serializable]
    public sealed class PerformanceAnalysisRequestDocument
    {
        public string schema = PerformanceCaptureSchemas.AnalysisRequest;
        public string comparison_kind = "Regression";
        public string[] baseline_manifest_paths = Array.Empty<string>();
        public string[] candidate_manifest_paths = Array.Empty<string>();
    }

    [Serializable]
    public sealed class PerformanceAnalysisSourceDocument
    {
        public string group = string.Empty;
        public string manifest_path = string.Empty;
        public string manifest_hash = string.Empty;
        public string capture_id = string.Empty;
        public string build_id = string.Empty;
        public string build_inputs_hash = string.Empty;
        public string instrumentation_mode = string.Empty;
        public double capture_seconds;
        public int dropped_logic_ticks;
        public bool budget_evaluated;
        public bool budget_passed;
    }

    [Serializable]
    public sealed class PerformanceRunDistributionDocument
    {
        public double[] values = Array.Empty<double>();
        public double median;
        public double min;
        public double max;
        public double median_absolute_deviation;
    }

    [Serializable]
    public sealed class PerformanceAnalysisMetricDocument
    {
        public string metric_id = string.Empty;
        public string point_id = string.Empty;
        public string unit = string.Empty;
        public string statistic = string.Empty;
        public string sample_scope = string.Empty;
        public string status = string.Empty;
        public string[] unavailable_capture_ids = Array.Empty<string>();
        public PerformanceRunDistributionDocument baseline = new PerformanceRunDistributionDocument();
        public PerformanceRunDistributionDocument candidate = new PerformanceRunDistributionDocument();
        public double absolute_delta;
        public bool percent_delta_available;
        public double percent_delta;
    }

    [Serializable]
    public sealed class PerformanceAnalysisDocument
    {
        public string schema = PerformanceCaptureSchemas.Analysis;
        public string comparison_kind = string.Empty;
        public string status = string.Empty;
        public string message = string.Empty;
        public string created_utc = string.Empty;
        public string request_hash = string.Empty;
        public PerformanceAnalysisSourceDocument[] sources = Array.Empty<PerformanceAnalysisSourceDocument>();
        public string[] warnings = Array.Empty<string>();
        public PerformanceAnalysisMetricDocument[] metrics = Array.Empty<PerformanceAnalysisMetricDocument>();
    }

    [Serializable]
    public sealed class PerformanceCaptureManifestDocument
    {
        public string schema = PerformanceCaptureSchemas.Manifest;
        public string build_inputs_hash = string.Empty;
        public string capture_id = string.Empty;
        public string status = string.Empty;
        public string stage = string.Empty;
        public string message = string.Empty;
        public string created_utc = string.Empty;
        public string completed_utc = string.Empty;
        public string scenario_id = string.Empty;
        public string scenario_hash = string.Empty;
        public string runtime_id = string.Empty;
        public string roster_identity = string.Empty;
        public string build_id = string.Empty;
        public string build_mode = string.Empty;
        public string request_hash = string.Empty;
        public string player_manifest_hash = string.Empty;
        public string toolchain_identity = string.Empty;
        public string budget_hash = string.Empty;
        public string capture_profile_hash = string.Empty;
        public string content_identity = string.Empty;
        public string pipeline_identity = string.Empty;
        public string pose_graph_revision = string.Empty;
        public string solver_identity = string.Empty;
        public string hardware_identity = string.Empty;
        public bool allocation_callstacks_recorded;
        public string operating_system = string.Empty;
        public string processor = string.Empty;
        public string graphics_device = string.Empty;
        public string graphics_api = string.Empty;
        public string metric_catalog_revision = string.Empty;
        public string instrumentation_identity = string.Empty;
        public string instrumentation_mode = string.Empty;
        public int instrumentation_span_layout_revision;
        public string instrumentation_manifest_path = string.Empty;
        public string instrumentation_span_path = string.Empty;
        public string instrumentation_span_hash = string.Empty;
        public string wpr_profile = string.Empty;
        public string wpr_profile_hash = string.Empty;
        public string statistics_schema = PerformanceCaptureSchemas.Summary;
        public int width;
        public int height;
        public int quality_level;
        public int v_sync_count;
        public int target_frame_rate;
        public PerformanceProcessDocument process = new PerformanceProcessDocument();
        public PerformanceFileDocument[] files = Array.Empty<PerformanceFileDocument>();
    }

    [Serializable]
    public sealed class PerformanceBuildAssetDocument
    {
        public string path = string.Empty;
        public string dependency_hash = string.Empty;
    }

    [Serializable]
    public sealed class PerformanceBuildInputsDocument
    {
        public string schema = PerformanceCaptureSchemas.BuildInputs;
        public string unity_version = string.Empty;
        public string build_target = string.Empty;
        public string scripting_backend = string.Empty;
        public string build_options = string.Empty;
        public string[] scenes = Array.Empty<string>();
        public string[] common_extra_defines = Array.Empty<string>();
        public PerformanceBuildAssetDocument[] assets = Array.Empty<PerformanceBuildAssetDocument>();
        public PerformanceFileDocument[] files = Array.Empty<PerformanceFileDocument>();
    }

    public static class PerformanceCaptureIdentity
    {
        public static string CameraTrace(PerformanceCameraTraceDocument value)
        {
            var builder = Begin(value.schema, value.trace_id, value.revision, value.input_id, value.tick_rate, value.frame_count);
            PerformanceCameraTraceSegmentDocument[] segments = value.segments ?? Array.Empty<PerformanceCameraTraceSegmentDocument>();
            for (int i = 0; i < segments.Length; i++)
            {
                PerformanceCameraTraceSegmentDocument segment = segments[i];
                Append(builder, segment.first_frame, segment.frame_count, segment.x, segment.y);
            }
            return Hash(builder);
        }

        public static string Scenario(PerformanceScenarioDocument value) =>
            Hash(Begin(
                value.schema,
                value.scenario_id,
                value.revision,
                value.scene_path,
                value.runtime_id,
                value.actor_id,
                value.roster_identity,
                value.ready_condition,
                value.capture_start_boundary,
                value.capture_end_boundary,
                value.input_trace_path,
                value.input_trace_hash,
                value.camera_trace_path,
                value.camera_trace_hash,
                value.warmup_logic_ticks,
                value.capture_logic_ticks,
                value.width,
                value.height,
                value.quality_level,
                value.v_sync_count,
                value.target_frame_rate));

        public static string Budget(PerformanceBudgetDocument value)
        {
            var builder = Begin(
                value.schema,
                value.budget_id,
                value.revision,
                value.target_fps,
                value.maximum_dropped_logic_ticks);
            PerformanceMetricBudgetDocument[] metrics = value.metrics ?? Array.Empty<PerformanceMetricBudgetDocument>();
            for (int i = 0; i < metrics.Length; i++)
            {
                PerformanceMetricBudgetDocument metric = metrics[i];
                Append(builder, metric.metric_id, metric.p95_limit, metric.p99_limit, metric.unit);
            }
            return Hash(builder);
        }

        public static string Profile(PerformanceCaptureProfileDocument value) =>
            Hash(Begin(
                value.schema,
                value.profile_id,
                value.revision,
                value.maximum_presentation_fps,
                value.logic_tick_rate,
                value.sample_capacity_margin_percent,
                value.instrumentation_span_capacity,
                value.runtime_ready_timeout_seconds,
                value.capture_timeout_seconds));

        static StringBuilder Begin(params object[] values)
        {
            var builder = new StringBuilder(512);
            Append(builder, values);
            return builder;
        }

        static void Append(StringBuilder builder, params object[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                object value = values[i];
                string text = value switch
                {
                    null => string.Empty,
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString()
                };
                builder.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text).Append('|');
            }
        }

        static string Hash(StringBuilder builder)
        {
            using SHA256 sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
            var value = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                value.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return value.ToString();
        }
    }
}
