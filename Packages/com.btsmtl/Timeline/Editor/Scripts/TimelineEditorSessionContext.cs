using System;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    public static class TimelineInspectorSelection
    {
        static UnityEngine.Object s_Owner;
        static string s_PropertyPath = string.Empty;
        static object s_Element;

        public static event Action Changed;

        public static void Set(UnityEngine.Object owner, string propertyPath, object element = null)
        {
            if (ReferenceEquals(s_Owner, owner) && string.Equals(s_PropertyPath, propertyPath, StringComparison.Ordinal) &&
                ReferenceEquals(s_Element, element))
                return;
            s_Owner = owner;
            s_PropertyPath = propertyPath ?? string.Empty;
            s_Element = element;
            Changed?.Invoke();
        }

        public static void Clear(UnityEngine.Object owner)
        {
            if (!ReferenceEquals(s_Owner, owner))
                return;
            s_Owner = null;
            s_PropertyPath = string.Empty;
            s_Element = null;
            Changed?.Invoke();
        }

        public static bool TryGet(UnityEngine.Object owner, out string propertyPath)
        {
            if (ReferenceEquals(s_Owner, owner) && !string.IsNullOrEmpty(s_PropertyPath))
            {
                propertyPath = s_PropertyPath;
                return true;
            }
            propertyPath = string.Empty;
            return false;
        }

        public static bool TryGetElement(UnityEngine.Object owner, out object element)
        {
            if (ReferenceEquals(s_Owner, owner) && s_Element != null)
            {
                element = s_Element;
                return true;
            }
            element = null;
            return false;
        }
    }

    public enum TimelineEditorSelectionKind : byte
    {
        None = 0,
        Track = 1,
        Clip = 2,
        TreeClip = 3,
        Curve = 4,
        Section = 5,
        Marker = 6
    }

    public interface ITimelineEditorRuntimeDebugBinding
    {
        string BindingId { get; }
    }

    public readonly struct TimelineEditorSelection
    {
        public TimelineEditorSelection(Track track, Clip clip)
            : this(track, clip, 0UL)
        {
        }

        internal TimelineEditorSelection(Track track, Clip clip, ulong revision)
            : this(
                clip is ITimelineOwnedAuthoringIdentity
                    ? TimelineEditorSelectionKind.TreeClip
                    : clip != null
                        ? TimelineEditorSelectionKind.Clip
                        : track != null
                            ? TimelineEditorSelectionKind.Track
                            : TimelineEditorSelectionKind.None,
                track,
                clip,
                clip?.AuthoringId ?? track?.AuthoringId ??
                string.Empty,
                string.Empty,
                Array.Empty<int>(),
                revision)
        {
        }

        public TimelineEditorSelection(TimelineSection section)
            : this(section, 0UL)
        {
        }

        internal TimelineEditorSelection(TimelineSection section, ulong revision)
            : this(
                section != null ? TimelineEditorSelectionKind.Section : TimelineEditorSelectionKind.None,
                null,
                null,
                section?.AuthoringId ?? string.Empty,
                string.Empty,
                Array.Empty<int>(),
                revision)
        {
            Section = section;
        }

        internal TimelineEditorSelection(TimelineMarker marker, ulong revision)
            : this(TimelineEditorSelectionKind.Marker, marker.Track, null, marker.AuthoringId,
                string.Empty, Array.Empty<int>(), revision)
        {
            Marker = marker;
        }

        TimelineEditorSelection(
            TimelineEditorSelectionKind kind,
            Track track,
            Clip clip,
            string elementAuthoringId,
            string subElementId,
            IReadOnlyList<int> keyIndices,
            ulong revision)
        {
            Kind = kind;
            Track = track;
            Clip = clip;
            Section = null;
            Marker = null;
            ElementAuthoringId =
                elementAuthoringId ?? string.Empty;
            SubElementId = subElementId ?? string.Empty;
            KeyIndices = keyIndices ?? Array.Empty<int>();
            Revision = revision;
        }

        public TimelineEditorSelectionKind Kind { get; }
        public Track Track { get; }
        public Clip Clip { get; }
        public TimelineSection Section { get; }
        public TimelineMarker Marker { get; }
        public string ElementAuthoringId { get; }
        public string SubElementId { get; }
        public IReadOnlyList<int> KeyIndices { get; }
        public ulong Revision { get; }
        public bool HasTrack => Track != null;
        public bool HasClip => Clip != null;
        public bool IsTreeClip =>
            Kind == TimelineEditorSelectionKind.TreeClip;
        public bool HasCurve =>
            Kind == TimelineEditorSelectionKind.Curve;

    }

    public sealed class TimelineEditorOpenRequest
    {
        readonly TimelineData m_Timeline;
        public TimelineEditorOpenRequest(
            TimelineData timeline,
            UnityEngine.Object serializedOwner,
            string serializedPropertyPath,
            string ownershipLabel,
            ITimelineEditorRuntimeDebugBinding runtimeDebugBinding,
            TimelineContractCatalog contractCatalog,
            ITimelineEditorAuthoringHost authoringHost,
            int previewFrameRate = TimelineUtility.FrameRate,
            ITimelineTimeRangePreview timeRangePreview = null)
        {
            m_Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            SerializedOwner = serializedOwner ? serializedOwner : throw new ArgumentNullException(nameof(serializedOwner));
            if (string.IsNullOrWhiteSpace(serializedPropertyPath))
                throw new ArgumentException("Timeline serialized property path is invalid.", nameof(serializedPropertyPath));
            SerializedPropertyPath = serializedPropertyPath;
            OwnershipLabel = ownershipLabel ?? string.Empty;
            RuntimeDebugBinding = runtimeDebugBinding;
            ContractCatalog = contractCatalog ?? throw new ArgumentNullException(nameof(contractCatalog));
            AuthoringHost = authoringHost;
            if (previewFrameRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(previewFrameRate));
            PreviewFrameRate = previewFrameRate;
            TimeRangePreview = timeRangePreview;
        }

        public TimelineData Timeline => TimeRangePreview == null ? m_Timeline : TimeRangePreview.WorkingData;
        public UnityEngine.Object SerializedOwner { get; }
        public string SerializedPropertyPath { get; }
        public string OwnershipLabel { get; }
        public ITimelineEditorRuntimeDebugBinding RuntimeDebugBinding { get; }
        public TimelineContractCatalog ContractCatalog { get; }
        public ITimelineEditorAuthoringHost AuthoringHost { get; }
        public int PreviewFrameRate { get; }
        public ITimelineTimeRangePreview TimeRangePreview { get; }
    }

    public enum TimelineAuthoringSnapMode : byte
    {
        None,
        LogicTick,
        SourceFrame,
        Seconds
    }

    public sealed class TimelineEditorSessionContext
    {
        readonly TimelineEditorOpenRequest m_Request;
        Func<bool> m_IsReadOnly;
        TimelineEditorSelection m_Selection;

        internal TimelineEditorSessionContext(TimelineEditorOpenRequest request)
        {
            m_Request = request ?? throw new ArgumentNullException(nameof(request));
        }

        public TimelineData Timeline => m_Request.Timeline;
        public UnityEngine.Object SerializedOwner => m_Request.SerializedOwner;
        public string SerializedPropertyPath => m_Request.SerializedPropertyPath;
        public string OwnershipLabel => m_Request.OwnershipLabel;
        public ITimelineEditorRuntimeDebugBinding RuntimeDebugBinding => m_Request.RuntimeDebugBinding;
        public TimelineEditorSelection Selection => m_Selection;
        public bool IsReadOnly => m_IsReadOnly != null && m_IsReadOnly();
        public int FrameRate => m_Request.PreviewFrameRate;
        public ScriptableObject GridPipeline { get; private set; }
        public TimelineAuthoringSnapMode SnapMode { get; private set; }
        public float SecondsInterval { get; private set; } = 0.01f;
        public int TickRate => GridPipeline ? m_Request.AuthoringHost.GetTimeGridTickRate(GridPipeline) : 0;

        public void ConfigureGrid(ScriptableObject pipeline, TimelineAuthoringSnapMode mode)
        {
            GridPipeline = pipeline;
            SnapMode = mode;
        }

        public void ConfigureSecondsInterval(float interval)
        {
            if (!float.IsFinite(interval) || interval < 0.000001f)
                throw new ArgumentOutOfRangeException(nameof(interval));
            SecondsInterval = interval;
        }

        public float SnapInterval => SnapMode switch
        {
            TimelineAuthoringSnapMode.None => 0f,
            TimelineAuthoringSnapMode.Seconds => SecondsInterval,
            TimelineAuthoringSnapMode.LogicTick => GridPipeline ? 1f / TickRate : 0f,
            TimelineAuthoringSnapMode.SourceFrame => Selection.Clip is AnimationClip animation && animation.Clip
                ? 1f / animation.Clip.frameRate : 0f,
            _ => throw new ArgumentOutOfRangeException()
        };

        public FixedScalar SnapTime(FixedScalar time, Clip clip = null, bool duration = false)
        {
            if (SnapMode == TimelineAuthoringSnapMode.None)
                return time;
            if (SnapMode == TimelineAuthoringSnapMode.Seconds)
            {
                decimal intervalRaw = (decimal)SecondsInterval * FixedScalar.OneRaw;
                decimal index = decimal.Round(time.Raw / intervalRaw, 0, MidpointRounding.ToEven);
                return FixedScalar.FromRaw(checked((long)decimal.Round(index * intervalRaw, 0, MidpointRounding.ToEven)));
            }
            if (SnapMode == TimelineAuthoringSnapMode.LogicTick)
            {
                if (!GridPipeline)
                    throw new InvalidOperationException("参考 Pipeline 已失效，无法进行逻辑 tick 吸附。");
                return TimelineTimeGrid.Position(TimelineTimeGrid.NearestIndex(time, TickRate), TickRate);
            }
            if (clip is not AnimationClip animation || !animation.Clip || animation.Clip.frameRate <= 0f)
                throw new InvalidOperationException("素材帧吸附需要带有效动画素材的 Clip。");
            decimal rate = (decimal)animation.Clip.frameRate;
            decimal offset = duration ? 0m : (decimal)clip.StartTime.Raw - clip.ClipInTime.Raw;
            decimal sourceRaw = time.Raw - offset;
            decimal frame = decimal.Round(sourceRaw * rate / FixedScalar.OneRaw, 0, MidpointRounding.ToEven);
            long raw = checked((long)decimal.Round(frame * FixedScalar.OneRaw / rate + offset,
                0, MidpointRounding.ToEven));
            return FixedScalar.Max(FixedScalar.Zero, FixedScalar.FromRaw(raw));
        }
        internal void RealignSelection()
        {
            if (SnapMode == TimelineAuthoringSnapMode.None)
                throw new InvalidOperationException("请先选择吸附网格。");
            if (Selection.Clip != null)
                AlignClip(Selection.Clip);
            else if (Selection.Marker != null)
                Selection.Marker.Configure(SnapTime(Selection.Marker.Time), Selection.Marker.Graph, Selection.Marker.ForceTriggerOnInitialSeek);
            else if (Selection.Section != null)
                Timeline.ConfigureSection(Selection.Section, Selection.Section.Name, SnapTime(Selection.Section.Time));
            else if (Selection.Track != null)
            {
                foreach (Clip clip in Selection.Track.Clips)
                    AlignClip(clip);
                foreach (TimelineMarker marker in Selection.Track.Markers)
                    marker.Configure(SnapTime(marker.Time), marker.Graph, marker.ForceTriggerOnInitialSeek);
            }
            else
                throw new InvalidOperationException("请选中要对齐的 Clip、Marker、Section 或轨道。");
        }

        void AlignClip(Clip clip)
        {
            FixedScalar start = SnapTime(clip.StartTime, clip);
            FixedScalar end = SnapTime(clip.EndTime, clip);
            if (end <= start)
                throw new InvalidOperationException($"Clip '{clip.AuthoringId}' 对齐后没有正时长，请选择更细网格。");
            clip.ConfigureTimeRange(start, end);
            clip.Track.UpdateMix();
        }

        public event Action<TimelineEditorSelection> SelectionChanged;

        internal void RefreshClipSelectionRevision()
        {
            if (m_Selection.HasClip)
                m_Selection = new TimelineEditorSelection(m_Selection.Track, m_Selection.Clip, CurrentRevision());
        }

        internal void SetSelection(object target)
        {
            ulong revision = CurrentRevision();
            TimelineEditorSelection selection = target switch
            {
                Clip clip => new TimelineEditorSelection(clip.Track, clip, revision),
                Track track => new TimelineEditorSelection(track, null, revision),
                TimelineSection section => new TimelineEditorSelection(section, revision),
                TimelineMarker marker => new TimelineEditorSelection(marker, revision),
                _ => default
            };
            if (selection.Kind == m_Selection.Kind &&
                ReferenceEquals(
                    selection.Track,
                    m_Selection.Track) &&
                ReferenceEquals(
                    selection.Clip,
                    m_Selection.Clip) &&
                ReferenceEquals(
                    selection.Section,
                    m_Selection.Section) &&
                ReferenceEquals(selection.Marker, m_Selection.Marker) &&
                string.Equals(
                    selection.ElementAuthoringId,
                    m_Selection.ElementAuthoringId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    selection.SubElementId,
                    m_Selection.SubElementId,
                    StringComparison.Ordinal) &&
                selection.Revision == m_Selection.Revision &&
                (selection.KeyIndices ?? Array.Empty<int>()).SequenceEqual(
                    m_Selection.KeyIndices ??
                    Array.Empty<int>()))
                return;
            m_Selection = selection;
            SelectionChanged?.Invoke(m_Selection);
        }

        ulong CurrentRevision()
        {
            return Timeline.AuthoringRevision;
        }

        public void Apply(Action mutation, string undoName)
        {
            if (m_Request.TimeRangePreview != null)
                throw new InvalidOperationException("预览工作副本只接受 Clip 时间范围；结构、资源和曲线请在作者模式编辑。");
            if (IsReadOnly)
                throw new InvalidOperationException(
                    "Timeline authoring mutation is unavailable during Scene Play or Live Debug.");
            Timeline.ApplyModify(mutation ?? throw new ArgumentNullException(nameof(mutation)), undoName);
        }

        internal void Dispose()
        {
            SelectionChanged = null;
            m_IsReadOnly = null;
            m_Selection = default;
        }
    }
}


