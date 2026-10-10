#if UNITY_EDITOR
using System;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Timeline;
using Slate;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    sealed class BtsmtlSlateTimelineBinding : IEmbeddedTimelineBinding
    {
        static readonly Unity.Profiling.ProfilerMarker s_TimeEditInput = new("Workbench.TimeEdit.Input");
        readonly TimelineEditorOpenRequest m_Request;
        readonly TimelineEditorSessionContext m_Session;
        readonly Action<Clip> m_OpenSourceClip;
        readonly List<IEmbeddedTimelineGroupBinding> m_Groups = new List<IEmbeddedTimelineGroupBinding>();
        readonly List<IEmbeddedTimelineSectionBinding> m_Sections = new List<IEmbeddedTimelineSectionBinding>();
        readonly Dictionary<string, BtsmtlTimelineTrackBinding> m_Tracks = new Dictionary<string, BtsmtlTimelineTrackBinding>(StringComparer.Ordinal);
        readonly Dictionary<string, BtsmtlTimelineClipBinding> m_Clips = new Dictionary<string, BtsmtlTimelineClipBinding>(StringComparer.Ordinal);
        readonly Dictionary<string, BtsmtlTimelineMarkerBinding> m_Markers = new Dictionary<string, BtsmtlTimelineMarkerBinding>(StringComparer.Ordinal);
        readonly Dictionary<string, BtsmtlTimelineSectionBinding> m_SectionsById = new Dictionary<string, BtsmtlTimelineSectionBinding>(StringComparer.Ordinal);
        TimelineData m_Timeline;
        ulong m_SourceRevision;
        IEmbeddedTimelineElementBinding m_Selected;
        Clip m_CopiedClip;
        bool m_EditActive;
        bool m_ReadOnly;
        string m_EditUndoName;
        ulong m_EditSourceRevision;
        bool m_CommittingEdit;
        EmbeddedTimelineEditKind m_EditKind;
        BtsmtlTimelineClipBinding[] m_TimePreviewClips = Array.Empty<BtsmtlTimelineClipBinding>();
        TimelineClipTimeRange[] m_TimePreviewRanges = Array.Empty<TimelineClipTimeRange>();
        float m_CurrentTime;
        bool m_DisplayFrames;
        const float MinimumViewDuration = 0.001f;
        float m_ViewTimeMin;
        float m_ViewTimeMax;
        Vector2 m_PopupPosition;

        public BtsmtlSlateTimelineBinding(
            TimelineEditorOpenRequest request,
            TimelineEditorSessionContext session,
            Action<Clip> openSourceClip = null)
        {
            m_Request = request ?? throw new ArgumentNullException(nameof(request));
            m_Session = session ?? throw new ArgumentNullException(nameof(session));
            m_OpenSourceClip = openSourceClip;
            m_Timeline = request.Timeline;
            m_SourceRevision = m_Timeline.AuthoringRevision;
            m_ViewTimeMin = 0f;
            m_ViewTimeMax = Mathf.Max(MinimumViewDuration, Timeline.Duration);
            BuildBindings();
        }

        public TimelineData Timeline => m_Timeline;
        public TimelineContractCatalog ContractCatalog => m_Request.ContractCatalog;
        public TimelineEditorSessionContext Session => m_Session;
        public int FrameRate => Mathf.Max(1, m_Session.FrameRate);
        public int TickRate => m_Session.TickRate;
        public bool DisplayFrames
        {
            get => m_DisplayFrames;
            set { m_DisplayFrames = value; RequestRepaint(); }
        }
        public float SnapInterval => m_Session.SnapInterval;
        public float StepInterval => m_Session.SnapMode == TimelineAuthoringSnapMode.None
            ? DisplayFrames ? 1f / FrameRate : m_Session.SecondsInterval
            : SnapInterval > 0f ? SnapInterval : throw new InvalidOperationException("当前吸附网格缺少参考 Pipeline 或动画素材。");
        public float SnapTime(float time) => m_Session.SnapTime(
            FixedScalar.FromDouble(Math.Max(0d, time)), m_Session.Selection.Clip).ToSingle();
        public string SnapLabel => m_Session.SnapMode switch
        {
            TimelineAuthoringSnapMode.LogicTick => TickRate > 0 ? $"吸附 {TickRate}Hz" : "吸附 缺少 Pipeline",
            TimelineAuthoringSnapMode.SourceFrame => "吸附 素材帧",
            TimelineAuthoringSnapMode.Seconds => $"吸附 {m_Session.SecondsInterval:0.######} s",
            _ => "吸附 关闭"
        };
        public void ShowSnapSettings(Rect rect) => PopupWindow.Show(rect, new GridSettingsPopup(this));

        sealed class GridSettingsPopup : PopupWindowContent
        {
            static readonly string[] s_SecondsIntervals = { "0.001 s", "0.01 s", "0.1 s" };
            readonly BtsmtlSlateTimelineBinding m_Owner;
            string m_Error;

            internal GridSettingsPopup(BtsmtlSlateTimelineBinding owner) => m_Owner = owner;
            public override Vector2 GetWindowSize() => new Vector2(340f, 260f);
            public override void OnGUI(Rect rect)
            {
                TimelineEditorSessionContext session = m_Owner.Session;
                EditorGUILayout.LabelField("Timeline 时间网格", EditorStyles.boldLabel);
                var pipeline = (ScriptableObject)EditorGUILayout.ObjectField(
                    "参考 Pipeline", session.GridPipeline, m_Owner.m_Request.AuthoringHost.TimeGridSourceType, false);
                if (pipeline != session.GridPipeline)
                    session.ConfigureGrid(pipeline, !pipeline && session.SnapMode == TimelineAuthoringSnapMode.LogicTick
                        ? TimelineAuthoringSnapMode.None : session.SnapMode);
                if (GUILayout.Toggle(session.SnapMode == TimelineAuthoringSnapMode.None, "关闭吸附", EditorStyles.radioButton))
                    session.ConfigureGrid(pipeline, TimelineAuthoringSnapMode.None);
                if (GUILayout.Toggle(session.SnapMode == TimelineAuthoringSnapMode.Seconds, "秒", EditorStyles.radioButton))
                    session.ConfigureGrid(pipeline, TimelineAuthoringSnapMode.Seconds);
                using (new EditorGUI.DisabledScope(session.SnapMode != TimelineAuthoringSnapMode.Seconds))
                {
                    int intervalIndex = session.SecondsInterval == 0.001f ? 0 : session.SecondsInterval == 0.01f ? 1 : 2;
                    int nextIndex = EditorGUILayout.Popup("秒吸附间隔", intervalIndex, s_SecondsIntervals);
                    if (nextIndex != intervalIndex)
                        session.ConfigureSecondsInterval(nextIndex == 0 ? 0.001f : nextIndex == 1 ? 0.01f : 0.1f);
                }
                using (new EditorGUI.DisabledScope(!pipeline))
                    if (GUILayout.Toggle(session.SnapMode == TimelineAuthoringSnapMode.LogicTick,
                        pipeline ? $"逻辑 tick：{session.TickRate} Hz" : "逻辑 tick：请先选择 Pipeline", EditorStyles.radioButton))
                        session.ConfigureGrid(pipeline, TimelineAuthoringSnapMode.LogicTick);
                bool hasSource = session.Selection.Clip is AnimationClip animation && animation.Clip;
                using (new EditorGUI.DisabledScope(!hasSource))
                    if (GUILayout.Toggle(session.SnapMode == TimelineAuthoringSnapMode.SourceFrame,
                        hasSource ? "动画素材帧（含 ClipIn 映射）" : "素材帧：请选中动画 Clip", EditorStyles.radioButton))
                        session.ConfigureGrid(pipeline, TimelineAuthoringSnapMode.SourceFrame);
                using (new EditorGUI.DisabledScope(m_Owner.IsReadOnly || session.SnapMode == TimelineAuthoringSnapMode.None))
                    if (GUILayout.Button("将选中内容重新对齐到当前网格"))
                    {
                        try
                        {
                            m_Owner.Apply(session.RealignSelection, "按当前网格重新对齐");
                            m_Error = null;
                        }
                        catch (Exception exception)
                        {
                            m_Error = exception.Message;
                        }
                    }
                EditorGUILayout.LabelField("网格换算前提：正常速率、未暂停。", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(m_Error))
                    EditorGUILayout.HelpBox(m_Error, MessageType.Error);
                if (GUI.changed)
                    m_Owner.RequestRepaint();
            }
        }

        public string DisplayName => Timeline.Name;
        public float Length => Timeline.Duration;
        public float Duration => Timeline.Duration;
        public IReadOnlyList<Track> Tracks => Timeline.Tracks;
        public IReadOnlyList<TimelineSection> SourceSections => Timeline.Sections;
        public float CurrentTime
        {
            get => m_CurrentTime;
            set
            {
                float nextTime = Mathf.Clamp(value, 0f, Length);
                if (m_CurrentTime == nextTime)
                    return;
                m_CurrentTime = nextTime;
                RequestRepaint();
            }
        }
        public float ViewTimeMin
        {
            get => m_ViewTimeMin;
            set
            {
                float nextValue = Mathf.Min(value, ViewTimeMax - MinimumViewDuration);
                if (Mathf.Approximately(m_ViewTimeMin, nextValue))
                    return;
                m_ViewTimeMin = nextValue;
                RequestRepaint();
            }
        }
        public float ViewTimeMax
        {
            get => m_ViewTimeMax;
            set
            {
                float nextValue = Mathf.Max(value, ViewTimeMin + MinimumViewDuration);
                if (Mathf.Approximately(m_ViewTimeMax, nextValue))
                    return;
                m_ViewTimeMax = nextValue;
                RequestRepaint();
            }
        }
        public bool IsReadOnly => m_ReadOnly || m_Session.IsReadOnly;
        internal bool IsCommittingEdit => m_CommittingEdit;
        public IReadOnlyList<IEmbeddedTimelineGroupBinding> Groups => m_Groups;
        public IReadOnlyList<IEmbeddedTimelineSectionBinding> Sections => m_Sections;
        public IEmbeddedTimelineElementBinding Selected => m_Selected;

        public void SetReadOnly(bool readOnly)
        {
            m_ReadOnly = readOnly;
        }

        public bool IsSourceCurrent()
        {
            return m_SourceRevision == Timeline.AuthoringRevision;
        }

        public void Select(IEmbeddedTimelineElementBinding element)
        {
            bool changed = !ReferenceEquals(m_Selected, element);
            m_Selected = element;
            Selection.activeObject = m_Request.SerializedOwner;
            object source = element is BtsmtlTimelineMarkerBinding markerSource
                ? markerSource.Source
                : element is BtsmtlTimelineClipBinding clipSource
                    ? clipSource.Source
                    : element is BtsmtlTimelineTrackBinding trackSource
                    ? trackSource.Source
                    : element is BtsmtlTimelineSectionBinding sectionSource
                            ? sectionSource.Source
                            : null;
            if (source != null && Timeline.TryGetSerializedPropertyPath(source, out string propertyPath))
                TimelineInspectorSelection.Set(m_Request.SerializedOwner, propertyPath, source);
            else
                TimelineInspectorSelection.Clear(m_Request.SerializedOwner);
            m_Session.SetSelection(
                element is BtsmtlTimelineMarkerBinding selectedMarker
                    ? selectedMarker.Source
                    : element is BtsmtlTimelineClipBinding selectedClip
                        ? selectedClip.Source
                        : element is BtsmtlTimelineTrackBinding selectedTrack
                            ? selectedTrack.Source
                            : element is BtsmtlTimelineSectionBinding selectedSection
                                ? selectedSection.Source
                                : null);
            if (changed)
                RequestRepaint();
        }

        public void BeginEdit(string undoName, EmbeddedTimelineEditKind kind = EmbeddedTimelineEditKind.Content)
        {
            if (m_EditActive || IsReadOnly)
                return;
            m_EditActive = true;
            m_EditUndoName = string.IsNullOrWhiteSpace(undoName) ? "Timeline Edit" : undoName;
            m_EditSourceRevision = m_SourceRevision;
            m_EditKind = kind;
            if (kind == EmbeddedTimelineEditKind.ClipTimeRange)
                foreach (BtsmtlTimelineClipBinding clip in m_Clips.Values)
                    clip.CaptureGestureTimeRange();
            if (m_Request.TimeRangePreview != null && kind == EmbeddedTimelineEditKind.ClipTimeRange)
                m_Request.TimeRangePreview.BeginEdit(m_EditUndoName);
        }

        public void CommitEdit()
        {
            if (!m_EditActive)
                return;
            if (m_Request.TimeRangePreview != null)
            {
                if (m_EditKind != EmbeddedTimelineEditKind.ClipTimeRange)
                {
                    m_EditActive = false;
                    Rebuild();
                    ReportIssue("预览工作副本只支持 Clip 平移与裁剪；其它内容请在作者模式编辑。");
                    return;
                }
                bool accepted = FlushTimePreviewRanges();
                m_EditActive = false;
                if (accepted)
                    m_Request.TimeRangePreview.CompleteEdit();
                else
                {
                    m_Request.TimeRangePreview.CancelEdit();
                    Rebuild();
                }
                m_EditSourceRevision = 0;
                RequestRepaint();
                return;
            }
            float previousLength = Length;
            m_EditActive = false;
            if (m_EditSourceRevision != m_SourceRevision ||
                !IsSourceCurrent())
            {
                m_EditSourceRevision = 0;
                Rebuild();
                ExpandViewToLength(previousLength);
                ReportIssue("Timeline 内容已被外部修改，当前编辑已丢弃。");
                return;
            }
            if (!m_Clips.Values.Any(clip => clip.HasChanges()) &&
                !m_Markers.Values.Any(marker => marker.HasChanges()) &&
                !m_SectionsById.Values.Any(section => section.HasChanges()))
            {
                m_EditSourceRevision = 0;
                ExpandViewToLength(previousLength);
                return;
            }
            try
            {
                m_CommittingEdit = true;
                m_Session.Apply(() =>
                {
                    bool changed = false;
                    foreach (BtsmtlTimelineClipBinding clip in m_Clips.Values)
                        changed |= clip.CommitSource();
                    foreach (BtsmtlTimelineMarkerBinding marker in m_Markers.Values)
                        changed |= marker.CommitSource();
                    foreach (BtsmtlTimelineSectionBinding section in m_SectionsById.Values)
                        changed |= section.CommitSource();
                    if (!changed)
                        return;
                    Timeline.Init();
                    ValidateTimeline();
                }, m_EditUndoName);
            }
            catch (Exception exception)
            {
                m_EditSourceRevision = 0;
                Rebuild();
                ExpandViewToLength(previousLength);
                ReportIssue(exception.Message);
                return;
            }
            finally
            {
                m_CommittingEdit = false;
            }
            m_EditSourceRevision = 0;
            foreach (BtsmtlTimelineClipBinding clip in m_Clips.Values)
                clip.SynchronizeTimeRange();
            m_SourceRevision = Timeline.AuthoringRevision;
            ExpandViewToLength(previousLength);
            RequestRepaint();
        }

        public void CancelEdit()
        {
            if (!m_EditActive)
                return;
            m_EditActive = false;
            m_EditSourceRevision = 0;
            if (m_Request.TimeRangePreview != null && m_EditKind == EmbeddedTimelineEditKind.ClipTimeRange)
                m_Request.TimeRangePreview.CancelEdit();
            Rebuild();
        }

        bool FlushTimePreviewRanges()
        {
            using var marker = s_TimeEditInput.Auto();
            bool changed = false;
            for (int i = 0; i < m_TimePreviewClips.Length; i++)
            {
                m_TimePreviewRanges[i] = m_TimePreviewClips[i].CaptureTimeRange();
                changed |= !m_TimePreviewRanges[i].Equals(m_TimePreviewClips[i].Source.CaptureTimeRange());
            }
            if (!changed)
                return true;
            m_CommittingEdit = true;
            bool accepted;
            string error;
            try
            {
                accepted = m_Request.TimeRangePreview.TrySetRanges(m_TimePreviewRanges, out error);
            }
            finally
            {
                m_CommittingEdit = false;
            }
            if (!accepted)
            {
                ReportIssue(error);
                return false;
            }
            for (int i = 0; i < m_TimePreviewClips.Length; i++)
                m_TimePreviewClips[i].SynchronizeTimeRange();
            m_SourceRevision = m_EditSourceRevision = Timeline.AuthoringRevision;
            m_Session.RefreshClipSelectionRevision();
            m_Request.TimeRangePreview.NotifyControlsSynchronized();
            return true;
        }

        public void ApplyClipTimeRangeNumbers(string clipIdentity, FixedScalar start, FixedScalar end)
        {
            var clip = m_Clips[clipIdentity];
            FixedScalar originalStart = clip.Source.StartTime;
            FixedScalar originalDuration = clip.Source.DurationTime;
            BeginEdit("编辑片段时间范围", EmbeddedTimelineEditKind.ClipTimeRange);
            clip.StartTime = start.ToSingle();
            clip.EndTime = end.ToSingle();
            if (end - start != originalDuration)
            {
                bool trimStart = start != originalStart;
                if (trimStart && clip.CanClipIn)
                    clip.ClipInTime = Math.Max(0d, (clip.Source.ClipInTime + start - originalStart).ToDouble());
                clip.AdjustSourceRange(originalStart.ToDouble(), start.ToDouble(), end.ToDouble(), trimStart);
            }
            CommitEdit();
        }

        public event Action RepaintRequested;
        public bool TimeRangeOnly => m_Request.TimeRangePreview != null;

        public void RequestRepaint()
        {
            if (m_EditActive && m_EditKind == EmbeddedTimelineEditKind.ClipTimeRange &&
                m_Request.TimeRangePreview != null && !m_CommittingEdit)
                FlushTimePreviewRanges();
            RepaintRequested?.Invoke();
        }

        public void AddTrack()
        {
            if (IsReadOnly)
                return;
            CapturePopupPosition();
            var menu = new GenericMenu();
            foreach (TimelineTrackContract contract in ContractCatalog.Tracks)
            {
                TimelineTrackContract candidate = contract;
                IReadOnlyList<TimelineAuthoringTrackFieldAttribute> fields = TimelineAuthoringTrackBinding.GetFields(candidate.Kind);
                menu.AddItem(new GUIContent(DisplayKind(candidate.Kind)), false, () => ShowTrackCreationPopup(candidate.Kind, fields));
            }
            menu.ShowAsContext();
        }

        public void AddClip(IEmbeddedTimelineTrackBinding track, double time)
        {
            if (IsReadOnly)
                return;
            if (!(track is BtsmtlTimelineTrackBinding formalTrack))
            {
                ReportIssue("Add Clip 需要正式 Timeline Track。");
                return;
            }
            if (formalTrack.IsLocked)
            {
                ReportIssue("当前 Track 已锁定，不能添加 Clip。");
                return;
            }
            CapturePopupPosition();
            if (ContractCatalog.TryGetTrack(formalTrack.Source.ContractKind, out TimelineTrackContract contract) &&
                contract.AllowedClipKinds.Count == 1 &&
                string.Equals(contract.AllowedClipKinds[0], TimelineContractKinds.TreeClip, StringComparison.Ordinal))
            {
                if (formalTrack.Source.ExecutionDomain == TimelineExecutionDomain.Presentation)
                {
                    ReportIssue("表现域点事件请使用 Add Marker 创建触发图。");
                    return;
                }
                ShowClipCreationPopup(formalTrack.Source.AuthoringId, TimelineContractKinds.TreeClip, time);
                return;
            }
            ShowAddClipMenu(formalTrack.Source.AuthoringId, time);
        }

        public void SetTrackActive(IEmbeddedTimelineTrackBinding track, bool active)
        {
            if (IsReadOnly || !(track is BtsmtlTimelineTrackBinding formalTrack))
                return;
            if (formalTrack.IsLocked)
            {
                ReportIssue("当前 Track 已锁定，不能修改 Active 状态。");
                return;
            }
            ApplyImmediate(() => formalTrack.Source.PersistentMuted = !active, "Track Active");
        }

        public void DeleteTrack(IEmbeddedTimelineTrackBinding track)
        {
            if (IsReadOnly || !(track is BtsmtlTimelineTrackBinding formalTrack))
                return;
            if (formalTrack.IsLocked)
            {
                ReportIssue("当前 Track 已锁定，不能删除。");
                return;
            }
            ApplyImmediate(() => m_Request.AuthoringHost.MutateOwnedContent(
                Timeline, () => Timeline.RemoveTrack(formalTrack.Source)), "Delete Timeline Track");
        }

        public void AddMarker(IEmbeddedTimelineTrackBinding track, double time)
        {
            if (IsReadOnly || !(track is BtsmtlTimelineTrackBinding formalTrack))
                return;
            if (formalTrack.IsLocked)
            {
                ReportIssue("当前 Track 已锁定，不能添加 Marker。");
                return;
            }
            TimelineMarker added = null;
            if (ApplyImmediate(() =>
            {
                ScriptableObject graph = m_Request.AuthoringHost.CreateGraph(
                    m_Request.SerializedOwner, $"{formalTrack.Source.Name} Marker {time:0.#########}s", true);
                added = formalTrack.Source.AddMarker(m_Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, time))), graph);
            }, "Add Timeline Marker") && m_Markers.TryGetValue(added.AuthoringId, out BtsmtlTimelineMarkerBinding binding))
                Select(binding);
        }

        public void DeleteMarker(IEmbeddedTimelineMarkerBinding marker)
        {
            if (IsReadOnly || !(marker is BtsmtlTimelineMarkerBinding formalMarker))
                return;
            if (formalMarker.IsLocked)
            {
                ReportIssue("当前 Marker 或所属 Track 已锁定，不能删除。");
                return;
            }
            ApplyImmediate(() => m_Request.AuthoringHost.MutateOwnedContent(
                Timeline, () => formalMarker.FormalTrack.Source.RemoveMarker(formalMarker.Source)), "Delete Timeline Marker");
        }

        public void MoveMarker(IEmbeddedTimelineMarkerBinding marker, double time)
        {
            if (IsReadOnly || !(marker is BtsmtlTimelineMarkerBinding formalMarker))
                return;
            FixedScalar nextTime = m_Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, time)));
            if (formalMarker.Time == nextTime.ToDouble())
                return;
            if (formalMarker.IsLocked)
            {
                ReportIssue("当前 Marker 或所属 Track 已锁定，不能移动。");
                return;
            }
            if (m_EditActive)
                formalMarker.SetTime(nextTime);
            else
                ApplyImmediate(() => formalMarker.Source.Configure(nextTime, formalMarker.Source.Graph, formalMarker.Source.ForceTriggerOnInitialSeek), "Move Timeline Marker");
        }

        public void OpenMarker(IEmbeddedTimelineMarkerBinding marker)
        {
            if (marker is BtsmtlTimelineMarkerBinding binding)
                m_Request.AuthoringHost.OpenGraph(binding.Source.Graph);
        }

        public bool TryGetMarkerBinding(string authoringId, out IEmbeddedTimelineMarkerBinding binding)
        {
            bool found = m_Markers.TryGetValue(authoringId ?? string.Empty, out BtsmtlTimelineMarkerBinding value);
            binding = value;
            return found;
        }

        public void DeleteClip(IEmbeddedTimelineClipBinding clip)
        {
            if (IsReadOnly || !(clip is BtsmtlTimelineClipBinding formalClip))
                return;
            if (formalClip.IsLocked)
            {
                ReportIssue("当前 Clip 或所属 Track 已锁定，不能删除。");
                return;
            }
            ApplyImmediate(() => Timeline.RemoveClip(formalClip.Source), "Delete Timeline Clip");
        }

        public void DeleteClips(IReadOnlyList<IEmbeddedTimelineClipBinding> clips)
        {
            if (clips == null || IsReadOnly)
                return;
            for (int index = 0; index < clips.Count; index++)
            {
                if (clips[index] is BtsmtlTimelineClipBinding formalClip && formalClip.IsLocked)
                {
                    ReportIssue("选中的 Clip 或所属 Track 已锁定，不能删除。");
                    return;
                }
            }
            ApplyImmediate(() =>
            {
                for (int index = 0; index < clips.Count; index++)
                    if (clips[index] is BtsmtlTimelineClipBinding formalClip)
                        Timeline.RemoveClip(formalClip.Source);
            }, "Delete Timeline Clips");
        }

        public void SplitClip(IEmbeddedTimelineClipBinding clip, double time)
        {
            if (IsReadOnly || !(clip is BtsmtlTimelineClipBinding formalClip))
                return;
            if (formalClip.IsLocked)
            {
                ReportIssue("当前 Clip 或所属 Track 已锁定，不能切分。");
                return;
            }
            FixedScalar splitTime = m_Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, time)), formalClip.Source);
            if (splitTime <= formalClip.Source.StartTime || splitTime >= formalClip.Source.EndTime)
                return;
            if (formalClip.Source is not AnimationClip && formalClip.Source is not MotionCurveClip)
            {
                ReportIssue($"Clip kind '{formalClip.Source.ContractKind}' does not provide a formal split source operation.");
                return;
            }
            ApplyImmediate(() =>
            {
                FixedScalar originalStartTime = formalClip.Source.StartTime;
                FixedScalar originalEndTime = formalClip.Source.EndTime;
                Clip copy = ManagedReferenceCloneUtility.Clone(formalClip.Source);
                copy.RegenerateAuthoringIdentity();
                if (copy is ITimelineOwnedAuthoringIdentity owned)
                    owned.RegenerateOwnedAuthoringIdentity();
                copy.ConfigureTimeRange(splitTime, originalEndTime);
                if (formalClip.Source is AnimationClip animation && copy is AnimationClip animationCopy)
                    animationCopy.ConfigureClipIn(animation.ClipInTime + (splitTime - originalStartTime));
                if (formalClip.Source is MotionCurveClip motion && copy is MotionCurveClip motionCopy)
                {
                    float splitSourceTime = motion.SourceStartTime +
                        (splitTime - originalStartTime).ToSingle();
                    float originalSourceEnd = motion.SourceEndTime;
                    motion.ConfigureSource(motion.SourceCurve, motion.SourceStartTime, splitSourceTime);
                    motionCopy.ConfigureSource(motionCopy.SourceCurve, splitSourceTime, originalSourceEnd);
                }
                formalClip.Source.ConfigureTimeRange(originalStartTime, splitTime);
                formalClip.Source.ConfigureEase(formalClip.Source.SelfEaseInTime, FixedScalar.Zero);
                copy.ConfigureEase(FixedScalar.Zero, copy.SelfEaseOutTime);
                copy.Track = formalClip.Source.Track;
                formalClip.Source.Track.Clips.Add(copy);
                SplitClipCurves(
                    formalClip.Source,
                    copy,
                    ((splitTime - originalStartTime) / (originalEndTime - originalStartTime)).ToSingle());
                formalClip.Source.Track.UpdateMix();
            }, "Split Timeline Clip");
        }

        public void MoveTrack(IEmbeddedTimelineTrackBinding track, int index)
        {
            if (IsReadOnly || !(track is BtsmtlTimelineTrackBinding formalTrack))
                return;
            if (formalTrack.IsLocked)
            {
                ReportIssue("当前 Track 已锁定，不能排序。");
                return;
            }
            ApplyImmediate(() =>
            {
                List<Track> tracks = Timeline.Tracks;
                tracks.Remove(formalTrack.Source);
                tracks.Insert(Mathf.Clamp(index, 0, tracks.Count), formalTrack.Source);
            }, "Reorder Timeline Track");
        }

        public void ConfigureSection(IEmbeddedTimelineSectionBinding section, string name, double time)
        {
            if (IsReadOnly || !(section is BtsmtlTimelineSectionBinding formalSection))
                return;
            if (formalSection.IsLocked)
            {
                ReportIssue("当前 Section 已锁定，不能修改。");
                return;
            }
            formalSection.Name = name ?? string.Empty;
            formalSection.Time = m_Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, time))).ToSingle();
        }

        public void DeleteSection(IEmbeddedTimelineSectionBinding section)
        {
            if (IsReadOnly || !(section is BtsmtlTimelineSectionBinding formalSection))
                return;
            if (formalSection.IsLocked)
            {
                ReportIssue("当前 Section 已锁定，不能删除。");
                return;
            }
            ApplyImmediate(() => Timeline.RemoveSection(formalSection.Source), "Delete Timeline Section");
        }

        public void AddSection(double time)
        {
            if (IsReadOnly)
                return;
            ApplyImmediate(() => Timeline.AddSection("Section", m_Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, time)))), "Add Timeline Section");
        }

        public bool CanPasteClip => m_CopiedClip != null;
        public event Action<string> AuthoringIssue;

        public void CopyClip(IEmbeddedTimelineClipBinding clip)
        {
            m_CopiedClip = clip is BtsmtlTimelineClipBinding formalClip
                ? ManagedReferenceCloneUtility.Clone(formalClip.Source)
                : null;
        }

        public void CopySourceClip(Clip clip)
        {
            m_CopiedClip = clip != null && m_Clips.TryGetValue(clip.AuthoringId, out BtsmtlTimelineClipBinding value)
                ? ManagedReferenceCloneUtility.Clone(value.Source)
                : null;
        }

        public void PasteClip(IEmbeddedTimelineTrackBinding track, double time)
        {
            if (IsReadOnly || m_CopiedClip == null || !(track is BtsmtlTimelineTrackBinding formalTrack))
                return;
            if (formalTrack.IsLocked)
            {
                ReportIssue("当前 Track 已锁定，不能粘贴 Clip。");
                return;
            }
            ApplyImmediate(() =>
            {
                Clip clone = ManagedReferenceCloneUtility.Clone(m_CopiedClip);
                clone.RegenerateAuthoringIdentity();
                if (clone is ITimelineOwnedAuthoringIdentity owned)
                    owned.RegenerateOwnedAuthoringIdentity();
                FixedScalar startTime = m_Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, time)), m_CopiedClip);
                clone.ConfigureTimeRange(startTime, startTime + m_CopiedClip.DurationTime);
                ContractCatalog.RequireClipPlacement(formalTrack.Source, clone);
                formalTrack.Source.Clips.Add(clone);
                formalTrack.Source.UpdateMix();
            }, "Paste Timeline Clip");
        }

        public Action CreateSourceNavigation(IEmbeddedTimelineClipBinding clip)
        {
            Clip source = ((BtsmtlTimelineClipBinding)clip).Source;
            return () => m_OpenSourceClip?.Invoke(source);
        }


        public bool TryGetTrack(string authoringId, out Track track)
        {
            track = null;
            return m_Tracks.TryGetValue(authoringId ?? string.Empty, out BtsmtlTimelineTrackBinding binding) &&
                   (track = binding.Source) != null;
        }

        public bool TryGetTrackBinding(string authoringId, out IEmbeddedTimelineTrackBinding binding)
        {
            if (m_Tracks.TryGetValue(authoringId ?? string.Empty, out BtsmtlTimelineTrackBinding value))
            {
                binding = value;
                return true;
            }
            binding = null;
            return false;
        }

        public bool TryGetSectionBinding(string authoringId, out IEmbeddedTimelineSectionBinding binding)
        {
            if (m_SectionsById.TryGetValue(authoringId ?? string.Empty, out BtsmtlTimelineSectionBinding value))
            {
                binding = value;
                return true;
            }
            binding = null;
            return false;
        }

        bool IEmbeddedTimelineBinding.TryGetTrack(string authoringId, out IEmbeddedTimelineTrackBinding track)
        {
            return TryGetTrackBinding(authoringId, out track);
        }

        bool IEmbeddedTimelineBinding.TryGetClip(string authoringId, out IEmbeddedTimelineClipBinding clip)
        {
            if (m_Clips.TryGetValue(authoringId ?? string.Empty, out BtsmtlTimelineClipBinding value))
            {
                clip = value;
                return true;
            }
            clip = null;
            return false;
        }

        public bool TryGetClip(string authoringId, out Clip clip)
        {
            clip = null;
            return m_Clips.TryGetValue(authoringId ?? string.Empty, out BtsmtlTimelineClipBinding binding) &&
                   (clip = binding.Source) != null;
        }

        public bool TryGetClipBinding(string authoringId, out IEmbeddedTimelineClipBinding binding)
        {
            if (m_Clips.TryGetValue(authoringId ?? string.Empty, out BtsmtlTimelineClipBinding value))
            {
                binding = value;
                return true;
            }
            binding = null;
            return false;
        }

        public void Apply(Action mutation, string undoName)
        {
            if (!IsSourceCurrent())
            {
                Rebuild();
                ReportIssue("Timeline 内容已被外部修改，当前字段修改未提交。");
                return;
            }
            m_Session.Apply(mutation, undoName);
        }

        public List<TimelineCurveChannelDescriptor> CollectCurveChannels(Track track)
        {
            var result = new List<TimelineCurveChannelDescriptor>();
            if (track != null)
                TimelineCurveChannelCatalog.CollectForTrack(track, result);
            return result;
        }

        public void Rebuild()
        {
            m_Timeline = m_Request.Timeline;
            string selectedId = m_Selected?.AuthoringId ?? string.Empty;
            BuildBindings();
            if (m_Clips.TryGetValue(selectedId, out BtsmtlTimelineClipBinding clip))
                Select(clip);
            else if (m_Markers.TryGetValue(selectedId, out BtsmtlTimelineMarkerBinding marker))
                Select(marker);
            else if (m_Tracks.TryGetValue(selectedId, out BtsmtlTimelineTrackBinding track))
                Select(track);
            else if (m_SectionsById.TryGetValue(selectedId, out BtsmtlTimelineSectionBinding section))
                Select(section);
            else
                Select(null);
        }

        bool ApplyImmediate(Action mutation, string undoName)
        {
            if (!IsSourceCurrent())
            {
                Rebuild();
                ReportIssue("Timeline 内容已被外部修改，当前操作未提交。");
                return false;
            }
            float previousLength = Length;
            try
            {
                m_Session.Apply(() =>
                {
                    mutation();
                    Timeline.Init();
                    ValidateTimeline();
                }, undoName);
            }
            catch (Exception exception)
            {
                Rebuild();
                ReportIssue(exception.Message);
                return false;
            }
            Rebuild();
            ExpandViewToLength(previousLength);
            return true;
        }

        void ExpandViewToLength(float previousLength)
        {
            if (Length > previousLength + MinimumViewDuration)
                m_ViewTimeMax = Mathf.Max(m_ViewTimeMax, Length);
        }

        void ValidateTimeline()
        {
            var errors = new List<string>();
            if (!Timeline.ValidateContent(ContractCatalog, errors))
                throw new InvalidOperationException(string.Join("\n", errors));
        }

        void BuildBindings()
        {
            var expandedTracks = new HashSet<string>(
                m_Tracks.Values
                    .Where(track => track.ShowCurves)
                    .Select(track => track.AuthoringId),
                StringComparer.Ordinal);
            var lockedTracks = new HashSet<string>(
                m_Tracks.Values
                    .Where(track => track.IsLocked)
                    .Select(track => track.AuthoringId),
                StringComparer.Ordinal);
            var lockedClips = new HashSet<string>(
                m_Clips.Values
                    .Where(clip => clip.IsLocked)
                    .Select(clip => clip.AuthoringId),
                StringComparer.Ordinal);
            var customHeights = m_Tracks.Values.ToDictionary(
                track => track.AuthoringId,
                track => track.CustomHeight,
                StringComparer.Ordinal);
            m_Groups.Clear();
            m_Sections.Clear();
            m_Tracks.Clear();
            m_Clips.Clear();
            m_Markers.Clear();
            m_SectionsById.Clear();
            var group = new BtsmtlTimelineGroupBinding(this, Timeline.Name);
            m_Groups.Add(group);
            for (int trackIndex = 0; trackIndex < Timeline.Tracks.Count; trackIndex++)
            {
                Track source = Timeline.Tracks[trackIndex];
                if (source == null)
                    continue;
                var track = new BtsmtlTimelineTrackBinding(this, group, source);
                track.ShowCurves = expandedTracks.Contains(source.AuthoringId);
                track.IsLocked = lockedTracks.Contains(source.AuthoringId);
                if (customHeights.TryGetValue(source.AuthoringId, out float customHeight))
                    track.CustomHeight = customHeight;
                group.AddTrack(track);
                m_Tracks[source.AuthoringId] = track;
                for (int markerIndex = 0; markerIndex < source.Markers.Count; markerIndex++)
                {
                    TimelineMarker sourceMarker = source.Markers[markerIndex];
                    if (sourceMarker == null)
                        continue;
                    var marker = new BtsmtlTimelineMarkerBinding(track, sourceMarker);
                    track.AddMarker(marker);
                    m_Markers.Add(marker.AuthoringId, marker);
                }
                for (int clipIndex = 0; clipIndex < source.Clips.Count; clipIndex++)
                {
                    Clip sourceClip = source.Clips[clipIndex];
                    if (sourceClip == null)
                        continue;
                    var clip = new BtsmtlTimelineClipBinding(this, track, sourceClip);
                    clip.IsLocked = lockedClips.Contains(sourceClip.AuthoringId);
                    track.AddClip(clip);
                    m_Clips[sourceClip.AuthoringId] = clip;
                }
            }
            for (int sectionIndex = 0; sectionIndex < Timeline.Sections.Count; sectionIndex++)
            {
                TimelineSection source = Timeline.Sections[sectionIndex];
                if (source == null)
                    continue;
                var section = new BtsmtlTimelineSectionBinding(this, source);
                m_Sections.Add(section);
                m_SectionsById[source.AuthoringId] = section;
            }
            m_SourceRevision = Timeline.AuthoringRevision;
            m_CurrentTime = Mathf.Clamp(m_CurrentTime, 0f, Length);
            if (m_Request.TimeRangePreview != null)
            {
                int count = m_Request.TimeRangePreview.ClipCount;
                m_TimePreviewClips = new BtsmtlTimelineClipBinding[count];
                m_TimePreviewRanges = new TimelineClipTimeRange[count];
                for (int i = 0; i < count; i++)
                    m_TimePreviewClips[i] = m_Clips[m_Request.TimeRangePreview.ClipIdentityAt(i)];
            }
        }

        void ShowTrackCreationPopup(string kind, IReadOnlyList<TimelineAuthoringTrackFieldAttribute> fields)
        {
            PopupWindow.Show(new Rect(m_PopupPosition, Vector2.zero), new TimelineTrackCreationPopup(
                kind,
                DisplayKind(kind),
                fields,
                (name, values) => CreateTrack(kind, name, values)));
        }

        string CreateTrack(string kind, string name, IReadOnlyDictionary<string, string> values)
        {
            if (IsReadOnly)
                return "Timeline 当前只读，不能添加 Track。";
            if (!IsSourceCurrent())
            {
                Rebuild();
                ReportIssue("Timeline 内容已被外部修改，Add Track 已取消。");
                return "Timeline 内容已被外部修改，Add Track 已取消。";
            }
            try
            {
                Track added = null;
                if (!ApplyImmediate(() =>
                {
                    Type trackType = TimelineAuthoringTypeCatalog.RequireTrackType(kind);
                    int count = Timeline.Tracks.Count;
                    Timeline.AddTrack(trackType, ContractCatalog);
                    added = Timeline.Tracks[count];
                    added.Name = string.IsNullOrWhiteSpace(name) ? DisplayKind(kind) : name.Trim();
                    TimelineAuthoringTrackBinding.Apply(added, values);
                }, "Add Timeline Track"))
                    return "正式 Timeline Track 提交失败。";
                if (added != null && m_Tracks.TryGetValue(added.AuthoringId, out BtsmtlTimelineTrackBinding addedBinding))
                    Select(addedBinding);
                return string.Empty;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ReportIssue(exception.Message);
                return exception.Message;
            }
        }

        void ShowAddClipMenu(string trackAuthoringId, double time)
        {
            if (!m_Tracks.TryGetValue(trackAuthoringId ?? string.Empty, out BtsmtlTimelineTrackBinding track) ||
                !ContractCatalog.TryGetTrack(track.Source.ContractKind, out TimelineTrackContract contract))
            {
                ReportIssue("Add Clip 找不到正式 Track contract。");
                return;
            }
            var menu = new GenericMenu();
            for (int index = 0; index < contract.AllowedClipKinds.Count; index++)
            {
                string kind = contract.AllowedClipKinds[index];
                if (!ContractCatalog.RequireClip(kind).SupportsExecutionDomain(track.Source.ExecutionDomain))
                    continue;
                menu.AddItem(new GUIContent(DisplayKind(kind)), false, () => ShowClipCreationPopup(trackAuthoringId, kind, time));
            }
            menu.ShowAsContext();
        }

        void ShowClipCreationPopup(string trackAuthoringId, string kind, double time)
        {
            if (!m_Tracks.TryGetValue(trackAuthoringId ?? string.Empty, out BtsmtlTimelineTrackBinding track))
            {
                ReportIssue("Add Clip 的目标 Track 已失效。");
                return;
            }
            TimelineExecutionDomain executionDomain = track.Source.ExecutionDomain;
            bool isTreeClip = kind == TimelineContractKinds.TreeClip;
            bool terminalLogicTreeClip = isTreeClip &&
                executionDomain == TimelineExecutionDomain.Logic;
            FixedScalar startTime = m_Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, time)), m_Session.Selection.Clip);
            FixedScalar defaultEndTime = terminalLogicTreeClip
                ? FixedScalar.Max(startTime + FixedScalar.FromRaw(1), Timeline.DurationTime)
                : startTime + FixedScalar.FromDecimal(0.05m);
            var request = new TimelineClipCreationRequest
            {
                TreeGraphType = m_Request.AuthoringHost.TreeGraphType,
                TrackAuthoringId = trackAuthoringId,
                Kind = kind,
                ExecutionDomain = executionDomain,
                StartTime = startTime,
                EndTime = defaultEndTime,
                DefaultEndTime = defaultEndTime,
                TimelineEndTime = Timeline.DurationTime,
            };
            if (isTreeClip)
                request.NewTreeGraphName = "Timeline Tree";
            PopupWindow.Show(new Rect(m_PopupPosition, Vector2.zero), new TimelineClipCreationPopup(
                request,
                Timeline.ExternalBindings,
                CreateClip));
        }

        void CapturePopupPosition()
        {
            Event currentEvent = Event.current;
            if (currentEvent != null)
                m_PopupPosition = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
        }

        string CreateClip(TimelineClipCreationRequest request)
        {
            if (IsReadOnly)
                return "Timeline 当前只读，不能添加 Clip。";
            if (!IsSourceCurrent())
            {
                Rebuild();
                ReportIssue("Timeline 内容已被外部修改，Add Clip 已取消。");
                return "Timeline 内容已被外部修改，Add Clip 已取消。";
            }
            if (!m_Tracks.TryGetValue(request.TrackAuthoringId, out BtsmtlTimelineTrackBinding track))
            {
                ReportIssue("Add Clip 的目标 Track 已失效。");
                return "Add Clip 的目标 Track 已失效。";
            }
            if (track.IsLocked)
            {
                ReportIssue("Add Clip 的目标 Track 已锁定。");
                return "Add Clip 的目标 Track 已锁定。";
            }
            bool isTreeClip = request.Kind == TimelineContractKinds.TreeClip;
            bool terminalLogicTreeClip = isTreeClip && track.Source.ExecutionDomain == TimelineExecutionDomain.Logic;
            FixedScalar presentationLayoutEnd = request.EndTime;
            if (isTreeClip && track.Source.ExecutionDomain == TimelineExecutionDomain.Presentation &&
                request.TreeExitSource == TimelineClipExitSource.TreeDecision &&
                presentationLayoutEnd == request.DefaultEndTime)
                presentationLayoutEnd = FixedScalar.Max(
                    request.StartTime + FixedScalar.FromRaw(1),
                    request.TimelineEndTime);
            FixedScalar terminalTime = FixedScalar.Max(request.StartTime + FixedScalar.FromRaw(1), Timeline.DurationTime);
            try
            {
                Clip added = null;
                if (!ApplyImmediate(() =>
                {
                    UnityEngine.Object treeGraph = null;
                    if (isTreeClip)
                    {
                        treeGraph = request.TreeGraph;
                        if (treeGraph == null)
                            treeGraph = m_Request.AuthoringHost.CreateGraph(m_Request.SerializedOwner, request.NewTreeGraphName, false);
                    }
                    added = request.Kind == TimelineContractKinds.AnimationClip
                        ? TimelineAuthoringTrackBinding.CreateClip(Timeline, ContractCatalog, track.Source, request.Resource as UnityEngine.AnimationClip, request.StartTime)
                        : request.Kind == TimelineContractKinds.MotionCurveClip
                            ? Timeline.AddClip(ContractCatalog, request.SourceCurve, track.Source, request.StartTime)
                        : treeGraph != null
                            ? Timeline.AddClip(ContractCatalog, treeGraph, track.Source, request.StartTime)
                        : request.Resource != null
                            ? Timeline.AddClip(ContractCatalog, request.Resource, track.Source, request.StartTime)
                            : Timeline.AddClip(ContractCatalog, track.Source, request.StartTime);
                    if (added is TreeClip treeClip && track.Source.ExecutionDomain == TimelineExecutionDomain.Presentation)
                        treeClip.SetExitSource(request.TreeExitSource);
                    added.ConfigureTimeRange(added.StartTime, terminalLogicTreeClip ? terminalTime : presentationLayoutEnd);
                    TimelineAuthoringClipBinding.Configure(Timeline, added, ReadConfiguration(added, request));
                    added.Track.UpdateMix();
                }, "Add Timeline Clip"))
                    return "正式 Timeline Clip 提交失败。";
                if (added != null && m_Clips.TryGetValue(added.AuthoringId, out BtsmtlTimelineClipBinding addedBinding))
                    Select(addedBinding);
                return string.Empty;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ReportIssue(exception.Message);
                return exception.Message;
            }
        }

        static TimelineAuthoringClipConfiguration ReadConfiguration(Clip clip, TimelineClipCreationRequest request)
        {
            TimelineAuthoringClipConfiguration configuration = TimelineAuthoringClipBinding.Read(clip);
            if (request.Kind == TimelineContractKinds.AnimationClip)
            {
                configuration.Extrapolation = request.Extrapolation;
                configuration.BlendProfileId = request.BlendProfileId;
            }
            if (request.Kind == TimelineContractKinds.MotionCurveClip)
            {
                configuration.CurveId = request.CurveId;
                configuration.SourceCurve = request.SourceCurve;
                configuration.SourceStartTime = request.SourceStartTime;
                configuration.SourceEndTime = request.SourceEndTime;
                configuration.Space = request.Space;
                configuration.Channel = request.Channel;
                configuration.BlendMode = request.BlendMode;
                configuration.Priority = request.Priority;
                configuration.ConsumeLowerChannels = request.ConsumeLowerChannels;
            }
            if (request.Kind == TimelineContractKinds.CameraStateClip)
            {
                configuration.CameraMode = request.CameraMode;
                configuration.Priority = request.CameraPriority;
                configuration.CameraBlendInSeconds = request.CameraBlendInSeconds;
                configuration.CameraBlendOutSeconds = request.CameraBlendOutSeconds;
                configuration.CameraTargetKey = request.CameraTargetKey;
                configuration.CameraInterruptPolicy = request.CameraInterruptPolicy;
            }
            if (request.Kind == TimelineContractKinds.CameraResponseClip)
            {
                configuration.CameraLookResponse = request.CameraLookResponse;
                configuration.ManualOrbitWeight = request.ManualOrbitWeight;
                configuration.PitchResponseWeight = request.PitchResponseWeight;
                configuration.YawResponseWeight = request.YawResponseWeight;
                configuration.Priority = request.CameraPriority;
            }
            if (request.Kind == TimelineContractKinds.ScenePresentationParameterCurveClip)
            {
                configuration.TargetBindingId = request.TargetBindingId;
                configuration.ParameterBindingId = request.ParameterBindingId;
            }
            return configuration;
        }

        static void SplitClipCurves(Clip first, Clip second, float split)
        {
            if (first?.Track == null || second == null)
                return;
            var descriptors = new List<TimelineCurveChannelDescriptor>();
            TimelineCurveChannelCatalog.CollectForTrack(first.Track, descriptors);
            if (split <= 0f || split >= 1f)
                throw new InvalidOperationException("切分位置超出素材归一化曲线可表达的内部范围。");
            float normalizedSplit = split;
            for (int index = 0; index < descriptors.Count; index++)
            {
                TimelineCurveChannelDescriptor descriptor = descriptors[index];
                if (!descriptor.Supports(first) || !descriptor.Supports(second))
                    continue;
                AnimationCurve source = descriptor.Read(first);
                descriptor.Replace(first, SplitNormalizedCurve(source, 0f, normalizedSplit));
                descriptor.Replace(second, SplitNormalizedCurve(source, normalizedSplit, 1f));
            }
        }

        static AnimationCurve SplitNormalizedCurve(AnimationCurve source, float start, float end)
        {
            if (source == null)
                return new AnimationCurve();
            float duration = end - start;
            if (duration <= 0f)
                throw new InvalidOperationException("曲线切分区间必须具有正时长。");
            var keys = new List<Keyframe>();
            Keyframe[] sourceKeys = source.keys;
            bool hasStart = false;
            bool hasEnd = false;
            for (int index = 0; index < sourceKeys.Length; index++)
            {
                Keyframe key = sourceKeys[index];
                if (key.time < start || key.time > end)
                    continue;
                key.time = Mathf.Clamp01((key.time - start) / duration);
                key.inTangent *= duration;
                key.outTangent *= duration;
                hasStart |= key.time == 0f;
                hasEnd |= key.time == 1f;
                keys.Add(key);
            }
            if (!hasStart)
                keys.Add(CreateBoundaryKey(source, start, 0f, duration));
            if (!hasEnd)
                keys.Add(CreateBoundaryKey(source, end, 1f, duration));
            keys.Sort((left, right) => left.time.CompareTo(right.time));
            var result = new AnimationCurve(keys.ToArray())
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
            return result;
        }

        static Keyframe CreateBoundaryKey(AnimationCurve source, float sourceTime, float normalizedTime, float duration)
        {
            float tangent = BoundaryTangent(source, sourceTime) * duration;
            return new Keyframe(normalizedTime, source.Evaluate(sourceTime), tangent, tangent);
        }

        static float BoundaryTangent(AnimationCurve source, float sourceTime)
        {
            Keyframe[] keys = source.keys;
            if (keys.Length == 0)
                return 0f;
            if (sourceTime <= keys[0].time)
                return keys[0].outTangent;
            if (sourceTime >= keys[keys.Length - 1].time)
                return keys[keys.Length - 1].inTangent;
            for (int index = 1; index < keys.Length; index++)
                if (sourceTime <= keys[index].time)
                    return keys[index - 1].outTangent;
            return 0f;
        }

        static string DisplayKind(string kind)
        {
            int separator = kind.LastIndexOf('.');
            return (separator >= 0 ? kind.Substring(0, separator) : kind).Replace('-', ' ');
        }

        void ReportIssue(string message)
        {
            AuthoringIssue?.Invoke(message ?? string.Empty);
        }

        sealed class BtsmtlTimelineGroupBinding : IEmbeddedTimelineGroupBinding
        {
            readonly List<IEmbeddedTimelineTrackBinding> m_Tracks = new List<IEmbeddedTimelineTrackBinding>();
            public BtsmtlTimelineGroupBinding(BtsmtlSlateTimelineBinding owner, string name)
            {
                Owner = owner;
                DisplayName = string.IsNullOrEmpty(name) ? "Timeline" : name;
            }
            public BtsmtlSlateTimelineBinding Owner { get; }
            public string AuthoringId => Owner.Timeline.AuthoringId;
            public string DisplayName { get; }
            public bool IsActive { get; set; } = true;
            public bool IsCollapsed { get; set; }
            public bool IsLocked { get; set; }
            public IReadOnlyList<IEmbeddedTimelineTrackBinding> Tracks => m_Tracks;
            public void AddTrack(BtsmtlTimelineTrackBinding track) => m_Tracks.Add(track);
        }

        sealed class BtsmtlTimelineTrackBinding : IEmbeddedTimelineTrackBinding, IEmbeddedTimelineMarkerTrackBinding
        {
            readonly List<IEmbeddedTimelineClipBinding> m_Clips = new List<IEmbeddedTimelineClipBinding>();
            readonly List<IEmbeddedTimelineMarkerBinding> m_Markers = new List<IEmbeddedTimelineMarkerBinding>();
            readonly Color m_Color;
            float m_CustomHeight;
            public BtsmtlTimelineTrackBinding(BtsmtlSlateTimelineBinding owner, BtsmtlTimelineGroupBinding group, Track source)
            {
                Owner = owner;
                Group = group;
                Source = source;
                m_Color = source.Color();
            }
            public BtsmtlSlateTimelineBinding Owner { get; }
            public BtsmtlTimelineGroupBinding Group { get; }
            public Track Source { get; }
            public string AuthoringId => Source.AuthoringId;
            public string DisplayName => string.IsNullOrEmpty(Source.Name) ? Source.ContractKind : Source.Name;
            public bool IsActive { get => !Source.PersistentMuted; set => Source.PersistentMuted = !value; }
            public bool IsLocked { get; set; }
            public bool ShowCurves { get; set; }
            public float CustomHeight
            {
                get => m_CustomHeight > 0f
                    ? m_CustomHeight
                    : ShowCurves
                        ? GetNaturalExpandedHeight(string.Empty)
                        : 0f;
                set => m_CustomHeight = Mathf.Clamp(value, DefaultHeight + 32f, 600f);
            }
            public Color Color => m_Color;
            public float StartTime => 0f;
            public float EndTime => Owner.Length;
            public float DefaultHeight => 32f;
            public float FinalHeight => GetFinalHeight(string.Empty);
            public float GetFinalHeight(string inspectedParameterId)
            {
                float naturalHeight = GetNaturalExpandedHeight(inspectedParameterId);
                if (!ShowCurves || string.IsNullOrEmpty(inspectedParameterId))
                    return naturalHeight;
                return Mathf.Max(naturalHeight, m_CustomHeight);
            }

            float GetNaturalExpandedHeight(string inspectedParameterId)
            {
                if (!ShowCurves)
                    return DefaultHeight;
                IEmbeddedTimelineClipBinding clip = SelectedClip;
                if (clip == null || clip.Parameters == null || clip.Parameters.Count == 0)
                    return DefaultHeight + 50f;
                bool hasInspectedParameter = false;
                for (int index = 0; index < clip.Parameters.Count; index++)
                {
                    if (string.Equals(clip.Parameters[index].ParameterId, inspectedParameterId, StringComparison.Ordinal))
                    {
                        hasInspectedParameter = true;
                        break;
                    }
                }
                float parameterHeight = DefaultHeight + 10f + clip.Parameters.Count * 20f;
                if (!hasInspectedParameter)
                    return Mathf.Max(parameterHeight, DefaultHeight + 50f);
                return parameterHeight + 65f;
            }
            public IReadOnlyList<IEmbeddedTimelineClipBinding> Clips => m_Clips;
            public IEmbeddedTimelineClipBinding SelectedClip =>
                Owner.Selected is BtsmtlTimelineClipBinding selected &&
                ReferenceEquals(selected.Track, this)
                    ? selected
                    : null;
            public void AddClip(BtsmtlTimelineClipBinding clip) => m_Clips.Add(clip);

            public IReadOnlyList<IEmbeddedTimelineMarkerBinding> Markers => m_Markers;
            public void AddMarker(BtsmtlTimelineMarkerBinding marker) => m_Markers.Add(marker);
        }

        sealed class BtsmtlTimelineMarkerBinding : IEmbeddedTimelineMarkerBinding
        {
            readonly BtsmtlTimelineTrackBinding m_Track;
            string m_DisplayName;
            FixedScalar m_InitialTime;
            FixedScalar m_Time;

            public BtsmtlTimelineMarkerBinding(BtsmtlTimelineTrackBinding track, TimelineMarker source)
            {
                m_Track = track;
                Source = source;
                m_DisplayName = $"Marker {source.Time.ToDouble():0.#########}s";
                m_Time = source.Time;
                m_InitialTime = m_Time;
            }

            public TimelineMarker Source { get; }
            public BtsmtlTimelineTrackBinding FormalTrack => m_Track;
            IEmbeddedTimelineTrackBinding IEmbeddedTimelineMarkerBinding.Track => m_Track;
            public string AuthoringId => Source.AuthoringId;
            public string DisplayName => m_DisplayName;
            public double Time => m_Time.ToDouble();
            internal void SetTime(FixedScalar time) => m_Time = time;
            public bool IsLocked { get => m_Track.IsLocked; set => m_Track.IsLocked = value; }

            public bool HasChanges() => m_Time != m_InitialTime;

            public bool CommitSource()
            {
                if (!HasChanges())
                    return false;
                Source.Configure(m_Time, Source.Graph, Source.ForceTriggerOnInitialSeek);
                m_DisplayName = $"Marker {m_Time.ToDouble():0.#########}s";
                m_InitialTime = m_Time;
                return true;
            }
        }

        sealed class BtsmtlTimelineClipBinding : IEmbeddedTimelineClipBinding, IEmbeddedTimelineSourceRangeBinding
        {
            readonly List<IEmbeddedTimelineCurveBinding> m_Curves = new List<IEmbeddedTimelineCurveBinding>();
            readonly List<IEmbeddedTimelineParameterBinding> m_Parameters = new List<IEmbeddedTimelineParameterBinding>();
            readonly BtsmtlSlateTimelineBinding m_Owner;
            readonly BtsmtlTimelineTrackBinding m_Track;
            readonly string m_Info;
            float m_StartTime;
            float m_EndTime;
            float m_BlendIn;
            float m_InitialBlendIn;
            float m_BlendOut;
            float m_InitialBlendOut;
            FixedScalar m_ClipInTime;
            float m_SourceStartTime;
            float m_SourceEndTime;
            TimelineClipTimeRange m_GestureTimeRange;
            bool m_IsCollapsed;
            bool m_IsLocked;

            public BtsmtlTimelineClipBinding(BtsmtlSlateTimelineBinding owner, BtsmtlTimelineTrackBinding track, Clip source)
            {
                m_Owner = owner;
                m_Track = track;
                Source = source;
                m_Info = source is MotionCurveClip infoMotion && infoMotion.SourceCurve != null
                    ? string.Concat(source.Name, "  [Ref: ", infoMotion.SourceCurve.name, "]")
                    : source.Name;
                m_StartTime = source.StartTime.ToSingle();
                m_EndTime = source.EndTime.ToSingle();
                m_BlendIn = source.SelfEaseInTime.ToSingle();
                m_InitialBlendIn = m_BlendIn;
                m_BlendOut = source.SelfEaseOutTime.ToSingle();
                m_InitialBlendOut = m_BlendOut;
                m_ClipInTime = source.ClipInTime;
                if (source is MotionCurveClip motion)
                {
                    m_SourceStartTime = motion.SourceStartTime;
                    m_SourceEndTime = motion.SourceEndTime;
                }
                var descriptors = owner.CollectCurveChannels(source.Track);
                for (int index = 0; index < descriptors.Count; index++)
                {
                    TimelineCurveChannelDescriptor descriptor = descriptors[index];
                    if (!descriptor.Supports(source))
                        continue;
                    var curve = new BtsmtlTimelineCurveBinding(this, descriptor);
                    m_Curves.Add(curve);
                    m_Parameters.Add(new BtsmtlTimelineParameterBinding(this, curve));
                }
                if (source is MotionCurveClip sourceMotion && sourceMotion.SourceCurve != null)
                {
                    AddReferenceParameter(sourceMotion, "source.position-x", "Position X", sourceMotion.SourcePositionX);
                    AddReferenceParameter(sourceMotion, "source.position-y", "Position Y", sourceMotion.SourcePositionY);
                    AddReferenceParameter(sourceMotion, "source.position-z", "Position Z", sourceMotion.SourcePositionZ);
                    AddReferenceParameter(sourceMotion, "source.yaw", "Yaw", sourceMotion.SourceYaw);
                }
            }

            public Clip Source { get; }
            internal BtsmtlSlateTimelineBinding Owner => m_Owner;
            public BtsmtlTimelineTrackBinding Track => m_Track;
            IEmbeddedTimelineTrackBinding IEmbeddedTimelineClipBinding.Track => m_Track;
            public bool IsTimeQuantized => false;
            public string AuthoringId => Source.AuthoringId;
            public string DisplayName => Source.Name;
            public string Info => m_Info;
            public bool IsActive => m_Track.IsActive;
            public bool IsValid => !Source.Invalid;
            public bool IsCollapsed { get => m_IsCollapsed; set => m_IsCollapsed = value; }
            public bool IsLocked { get => m_IsLocked || m_Track.IsLocked; set => m_IsLocked = value; }
            public float StartTime
            {
                get => m_StartTime;
                set
                {
                    float nextStart = SnapTime(value);
                    float delta = nextStart - m_StartTime;
                    m_StartTime = nextStart;
                    m_EndTime = Mathf.Max(m_StartTime, m_EndTime + delta);
                }
            }
            public float EndTime
            {
                get => m_EndTime;
                set
                {
                    float nextEnd = Mathf.Max(StartTime, SnapTime(value));
                    if (nextEnd == m_EndTime)
                        return;
                    m_EndTime = nextEnd;
                    RefreshReferenceCurves();
                }
            }
            public float Length => Mathf.Max(0f, EndTime - StartTime);
            public float BlendIn
            {
                get => Mathf.Clamp(m_BlendIn, 0f, Length);
                set => m_BlendIn = Mathf.Clamp(m_Owner.Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, value)), Source, true).ToSingle(), 0f, Length);
            }
            public float BlendOut
            {
                get => Mathf.Clamp(m_BlendOut, 0f, Length);
                set => m_BlendOut = Mathf.Clamp(m_Owner.Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, value)), Source, true).ToSingle(), 0f, Length);
            }
            public bool CanScale => Source.IsResizable();
            public bool CanClipIn => Source.IsClipInable();
            public double ClipInTime
            {
                get => m_ClipInTime.ToDouble();
                set
                {
                    if (value != m_ClipInTime.ToDouble())
                        m_ClipInTime = m_Owner.Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, value)), Source, true);
                }
            }
            public bool CanBlendIn => Source.IsMixable();
            public bool CanBlendOut => Source.IsMixable();
            public IReadOnlyList<IEmbeddedTimelineParameterBinding> Parameters => m_Parameters;
            public IReadOnlyList<IEmbeddedTimelineCurveBinding> Curves => m_Curves;

            public void AdjustSourceRange(
                double originalStartTime,
                double currentStartTime,
                double currentEndTime,
                bool trimStart)
            {
                if (Source is not MotionCurveClip motion || motion.SourceCurve == null)
                    return;
                double duration = Math.Max(0d, currentEndTime - currentStartTime);
                if (trimStart)
                    m_SourceStartTime = (float)Math.Max(0d,
                        Math.Min(m_GestureTimeRange.SourceEnd, m_GestureTimeRange.SourceStart + currentStartTime - originalStartTime));
                m_SourceEndTime = (float)Math.Min(motion.SourceCurve.Duration, m_SourceStartTime + duration);
                RefreshReferenceCurves();
            }

            public bool CanCrossBlend(IEmbeddedTimelineClipBinding other)
            {
                return other is BtsmtlTimelineClipBinding formal && formal.Source.GetType() == Source.GetType() && Source.IsMixable();
            }

            public void AddIdentityKey(float time)
            {
                for (int index = 0; index < m_Curves.Count; index++)
                {
                    AnimationCurve curve = m_Curves[index].Curve;
                    BtsmtlTimelineCurveBinding binding = (BtsmtlTimelineCurveBinding)m_Curves[index];
                    float snappedTime = binding.SnapTime(time);
                    curve.AddKey(snappedTime, curve.Evaluate(snappedTime));
                }
            }

            void AddReferenceParameter(MotionCurveClip motion, string channelId, string displayName, AnimationCurve source)
            {
                m_Parameters.Add(new BtsmtlTimelineReferenceParameterBinding(
                    this,
                    channelId,
                    displayName,
                    motion.SourceCurve.name,
                    curve => motion.UpdateSourceDisplayCurve(curve, source, m_SourceStartTime, m_SourceEndTime, Length)));
            }

            void RefreshReferenceCurves()
            {
                for (int index = 0; index < m_Parameters.Count; index++)
                    if (m_Parameters[index] is BtsmtlTimelineReferenceParameterBinding reference)
                        reference.Refresh();
            }

            public void Split(float time) => m_Owner.SplitClip(this, time);

            public void StretchFit()
            {
                StartTime = 0f;
                EndTime = m_Track.EndTime;
            }

            public void CleanKeysOffRange()
            {
                for (int curveIndex = 0; curveIndex < m_Curves.Count; curveIndex++)
                    ((BtsmtlTimelineCurveBinding)m_Curves[curveIndex]).Trim(0f, Length);
            }

            public void ResetAnimation()
            {
                for (int index = 0; index < m_Curves.Count; index++)
                    ((BtsmtlTimelineCurveBinding)m_Curves[index]).Replace(new AnimationCurve());
            }

            public bool CommitSource()
            {
                TimelineClipTimeRange range = CaptureTimeRange();
                bool changed = !range.Equals(Source.CaptureTimeRange());
                for (int index = 0; index < m_Curves.Count; index++)
                    changed |= ((BtsmtlTimelineCurveBinding)m_Curves[index]).CommitSource();
                if (!changed)
                    return false;
                Source.ApplyTimeRange(in range);
                m_InitialBlendIn = m_BlendIn;
                m_InitialBlendOut = m_BlendOut;
                Source.Track.UpdateMix();
                return true;
            }

            public bool HasChanges()
            {
                if (!CaptureTimeRange().Equals(Source.CaptureTimeRange()))
                    return true;
                for (int index = 0; index < m_Curves.Count; index++)
                    if (((BtsmtlTimelineCurveBinding)m_Curves[index]).HasChanges())
                        return true;
                return false;
            }

            internal TimelineClipTimeRange CaptureTimeRange() => new TimelineClipTimeRange(
                StartTime == Source.StartTime.ToSingle() ? Source.StartTime : m_Owner.Session.SnapTime(FixedScalar.FromDouble(StartTime), Source),
                EndTime == Source.EndTime.ToSingle() ? Source.EndTime : m_Owner.Session.SnapTime(FixedScalar.FromDouble(EndTime), Source),
                m_BlendIn == m_InitialBlendIn ? Source.SelfEaseInTime : m_Owner.Session.SnapTime(FixedScalar.FromDouble(BlendIn), Source, true),
                m_BlendOut == m_InitialBlendOut ? Source.SelfEaseOutTime : m_Owner.Session.SnapTime(FixedScalar.FromDouble(BlendOut), Source, true),
                CanClipIn ? m_ClipInTime : Source.ClipInTime, m_SourceStartTime, m_SourceEndTime,
                Source.OtherEaseInTime, Source.OtherEaseOutTime);

            internal void CaptureGestureTimeRange() => m_GestureTimeRange = Source.CaptureTimeRange();

            internal void SynchronizeTimeRange()
            {
                m_StartTime = Source.StartTime.ToSingle();
                m_EndTime = Source.EndTime.ToSingle();
                m_BlendIn = m_InitialBlendIn = Source.SelfEaseInTime.ToSingle();
                m_BlendOut = m_InitialBlendOut = Source.SelfEaseOutTime.ToSingle();
                m_ClipInTime = Source.ClipInTime;
                if (m_Owner.TimeRangeOnly)
                    for (int i = 0; i < m_Curves.Count; i++)
                        ((BtsmtlTimelineCurveBinding)m_Curves[i]).SynchronizeDisplayDuration();
                if (Source is MotionCurveClip motion)
                {
                    m_SourceStartTime = motion.SourceStartTime;
                    m_SourceEndTime = motion.SourceEndTime;
                }
            }

            float SnapTime(float value)
            {
                return m_Owner.Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, value)), Source).ToSingle();
            }
        }

        sealed class BtsmtlTimelineCurveBinding : IEmbeddedTimelineCurveBinding
        {
            readonly BtsmtlTimelineClipBinding m_Clip;
            readonly TimelineCurveChannelDescriptor m_Descriptor;
            AnimationCurve m_Curve;
            float m_DisplayDuration;

            public BtsmtlTimelineCurveBinding(BtsmtlTimelineClipBinding clip, TimelineCurveChannelDescriptor descriptor)
            {
                m_Clip = clip;
                m_Descriptor = descriptor;
                m_DisplayDuration = clip.Source.DurationTime.ToSingle();
                m_Curve = ConvertCurveTime(
                    descriptor.Read(clip.Source),
                    clip.Source.DurationTime.ToSingle(),
                    false);
            }
            public string ChannelId => m_Descriptor.ChannelId.Value;
            public string DisplayName => m_Descriptor.DisplayName;
            public TimelineCurveValueDomain ValueDomain => m_Descriptor.ValueDomain;
            public AnimationCurve Curve => m_Curve;
            public float Duration => m_Clip.Length;
            internal void SynchronizeDisplayDuration()
            {
                float duration = Duration;
                if (m_DisplayDuration == duration) return;
                AnimationCurve source = m_Descriptor.Read(m_Clip.Source);
                for (int step = 0; step < source.length; step++)
                {
                    int index = duration > m_DisplayDuration ? source.length - step - 1 : step;
                    Keyframe key = source[index];
                    key.time *= duration;
                    key.inTangent /= duration;
                    key.outTangent /= duration;
                    m_Curve.MoveKey(index, key);
                }
                m_DisplayDuration = duration;
            }
            public void Replace(AnimationCurve curve) => m_Curve = SnapCurve(curve);
            public void Trim(float min, float max)
            {
                for (int index = m_Curve.length - 1; index >= 0; index--)
                    if (m_Curve[index].time < min || m_Curve[index].time > max)
                        m_Curve.RemoveKey(index);
            }
            public bool CommitSource()
            {
                if (!HasChanges())
                    return false;
                m_Descriptor.Replace(m_Clip.Source, ConvertCurveTime(m_Curve, Duration, true));
                return true;
            }

            public bool HasChanges()
            {
                AnimationCurve source = m_Descriptor.Read(m_Clip.Source);
                if (source.preWrapMode != m_Curve.preWrapMode || source.postWrapMode != m_Curve.postWrapMode ||
                    source.length != m_Curve.length)
                    return true;
                for (int i = 0; i < source.length; i++)
                {
                    Keyframe key = m_Curve[i];
                    if (key.time < 0f || key.time > Duration)
                        return true;
                    key.time /= Duration;
                    key.inTangent *= Duration;
                    key.outTangent *= Duration;
                    if (!TimelineCurveAuthoring.AreEquivalent(source[i], key))
                        return true;
                }
                return source.length == 0 || m_Curve[0].time != 0f || m_Curve[m_Curve.length - 1].time != Duration;
            }

            public float SnapTime(float time)
            {
                FixedScalar origin = m_Clip.StartTime == m_Clip.Source.StartTime.ToSingle()
                    ? m_Clip.Source.StartTime : FixedScalar.FromDouble(m_Clip.StartTime);
                FixedScalar localTime = FixedScalar.FromDouble(Math.Clamp((double)time, 0d, Duration));
                FixedScalar snapped = m_Clip.Owner.Session.SnapTime(origin + localTime, m_Clip.Source);
                return Mathf.Clamp((snapped - origin).ToSingle(), 0f, Duration);
            }

            AnimationCurve SnapCurve(AnimationCurve source)
            {
                AnimationCurve result = TimelineCurveAuthoring.CopyCurve(source);
                Keyframe[] keys = result.keys;
                for (int index = 0; index < keys.Length; index++)
                {
                    Keyframe key = keys[index];
                    bool existingTime = false;
                    for (int sourceIndex = 0; sourceIndex < m_Curve.length; sourceIndex++)
                        if (m_Curve[sourceIndex].time == key.time)
                        {
                            existingTime = true;
                            break;
                        }
                    if (!existingTime)
                        key.time = SnapTime(key.time);
                    keys[index] = key;
                }
                result.keys = keys;
                return result;
            }

            static AnimationCurve ConvertCurveTime(AnimationCurve source, float duration, bool toNormalized)
            {
                AnimationCurve result = TimelineCurveAuthoring.CopyCurve(source);
                if (duration <= 0f)
                    throw new InvalidOperationException("曲线所属 Clip 必须具有正时长。");
                float safeDuration = duration;
                if (toNormalized)
                    result = ClipCurveToDuration(result, safeDuration);
                Keyframe[] keys = result.keys;
                for (int index = 0; index < keys.Length; index++)
                {
                    Keyframe key = keys[index];
                    if (toNormalized)
                    {
                        key.time /= safeDuration;
                        key.inTangent *= safeDuration;
                        key.outTangent *= safeDuration;
                    }
                    else
                    {
                        key.time *= safeDuration;
                        key.inTangent /= safeDuration;
                        key.outTangent /= safeDuration;
                    }
                    keys[index] = key;
                }
                result.keys = keys;
                return result;
            }

            static AnimationCurve ClipCurveToDuration(AnimationCurve source, float duration)
            {
                var keys = new List<Keyframe>();
                Keyframe[] sourceKeys = source.keys;
                for (int index = 0; index < sourceKeys.Length; index++)
                {
                    Keyframe key = sourceKeys[index];
                    if (key.time < 0f || key.time > duration)
                        continue;
                    key.time = Mathf.Clamp(key.time, 0f, duration);
                    keys.Add(key);
                }
                AddBoundaryKey(source, keys, 0f, duration);
                AddBoundaryKey(source, keys, duration, duration);
                keys.Sort((left, right) => left.time.CompareTo(right.time));
                return new AnimationCurve(keys.ToArray())
                {
                    preWrapMode = source.preWrapMode,
                    postWrapMode = source.postWrapMode
                };
            }

            static void AddBoundaryKey(AnimationCurve source, List<Keyframe> keys, float time, float duration)
            {
                for (int index = 0; index < keys.Count; index++)
                    if (keys[index].time == time)
                        return;
                float tangent = BoundaryTangent(source, time);
                keys.Add(new Keyframe(time, source.Evaluate(time), tangent, tangent));
            }

            static float BoundaryTangent(AnimationCurve source, float time)
            {
                Keyframe[] keys = source.keys;
                if (keys.Length == 0)
                    return 0f;
                if (time <= keys[0].time)
                    return keys[0].outTangent;
                if (time >= keys[keys.Length - 1].time)
                    return keys[keys.Length - 1].inTangent;
                for (int index = 1; index < keys.Length; index++)
                    if (time <= keys[index].time)
                        return keys[index - 1].outTangent;
                return 0f;
            }
        }

        sealed class BtsmtlTimelineParameterBinding : IEmbeddedTimelineParameterBinding
        {
            readonly BtsmtlTimelineClipBinding m_Clip;
            readonly BtsmtlTimelineCurveBinding m_Curve;
            public BtsmtlTimelineParameterBinding(BtsmtlTimelineClipBinding clip, BtsmtlTimelineCurveBinding curve)
            {
                m_Clip = clip;
                m_Curve = curve;
            }
            public string ParameterId => m_Curve.ChannelId;
            public string DisplayName => m_Curve.DisplayName;
            public string ValueDomainSummary => m_Curve.ValueDomain.Summary;
            public bool Enabled { get; set; } = true;
            public float CurrentValue => m_Curve.Curve.Evaluate(Mathf.Clamp(
                m_Clip.Owner.CurrentTime - m_Clip.StartTime,
                0f,
                m_Curve.Duration));
            public IReadOnlyList<IEmbeddedTimelineCurveBinding> Curves => new[] { m_Curve };
            public void AddKey(float localTime)
            {
                float snappedTime = m_Curve.SnapTime(localTime);
                m_Curve.Curve.AddKey(snappedTime, m_Curve.Curve.Evaluate(snappedTime));
            }
            public void RemoveKey(float localTime)
            {
                float snappedTime = m_Curve.SnapTime(localTime);
                for (int index = m_Curve.Curve.length - 1; index >= 0; index--)
                    if (m_Curve.Curve[index].time == snappedTime)
                        m_Curve.Curve.RemoveKey(index);
            }
            public void SelectPreviousKey(float localTime)
            {
                float? previous = null;
                Keyframe[] keys = m_Curve.Curve.keys;
                for (int index = 0; index < keys.Length; index++)
                    if (keys[index].time < localTime && (!previous.HasValue || keys[index].time > previous.Value))
                        previous = keys[index].time;
                if (previous.HasValue)
                    m_Clip.Owner.CurrentTime = m_Clip.StartTime + previous.Value;
            }

            public void SelectNextKey(float localTime)
            {
                float? next = null;
                Keyframe[] keys = m_Curve.Curve.keys;
                for (int index = 0; index < keys.Length; index++)
                    if (keys[index].time > localTime && (!next.HasValue || keys[index].time < next.Value))
                        next = keys[index].time;
                if (next.HasValue)
                    m_Clip.Owner.CurrentTime = m_Clip.StartTime + next.Value;
            }
        }

        sealed class BtsmtlTimelineReferenceParameterBinding : IEmbeddedTimelineParameterBinding, IEmbeddedTimelineReferenceParameterBinding
        {
            readonly BtsmtlTimelineClipBinding m_Clip;
            readonly BtsmtlTimelineReferenceCurveBinding m_Curve;
            readonly IEmbeddedTimelineCurveBinding[] m_Curves;

            public BtsmtlTimelineReferenceParameterBinding(
                BtsmtlTimelineClipBinding clip,
                string channelId,
                string displayName,
                string referenceLabel,
                Action<AnimationCurve> updateCurve)
            {
                m_Clip = clip;
                m_Curve = new BtsmtlTimelineReferenceCurveBinding(clip, channelId, displayName, updateCurve);
                m_Curves = new IEmbeddedTimelineCurveBinding[] { m_Curve };
                ReferenceLabel = referenceLabel ?? string.Empty;
            }

            public string ParameterId => m_Curve.ChannelId;
            public string DisplayName => m_Curve.DisplayName;
            public string ValueDomainSummary => "Source Reference";
            public string ReferenceLabel { get; }
            public bool Enabled { get => true; set { } }
            public float CurrentValue => m_Curve.Curve.Evaluate(Mathf.Clamp(
                m_Clip.Owner.CurrentTime - m_Clip.StartTime,
                0f,
                m_Curve.Duration));
            public IReadOnlyList<IEmbeddedTimelineCurveBinding> Curves => m_Curves;
            public void AddKey(float localTime) { }
            public void RemoveKey(float localTime) { }
            public void SelectPreviousKey(float localTime) { }
            public void SelectNextKey(float localTime) { }
            public void Refresh() => m_Curve.Refresh();
        }

        sealed class BtsmtlTimelineReferenceCurveBinding : IEmbeddedTimelineCurveBinding
        {
            readonly BtsmtlTimelineClipBinding m_Clip;
            readonly Action<AnimationCurve> m_UpdateCurve;
            readonly AnimationCurve m_Curve = new AnimationCurve();

            public BtsmtlTimelineReferenceCurveBinding(
                BtsmtlTimelineClipBinding clip,
                string channelId,
                string displayName,
                Action<AnimationCurve> updateCurve)
            {
                m_Clip = clip;
                ChannelId = channelId;
                DisplayName = displayName;
                m_UpdateCurve = updateCurve;
                m_UpdateCurve(m_Curve);
            }

            public string ChannelId { get; }
            public string DisplayName { get; }
            public AnimationCurve Curve => m_Curve;
            public float Duration => m_Clip.Length;
            public void Replace(AnimationCurve curve) { }
            public void Refresh() => m_UpdateCurve(m_Curve);
        }

        sealed class BtsmtlTimelineSectionBinding : IEmbeddedTimelineSectionBinding
        {
            readonly BtsmtlSlateTimelineBinding m_Owner;
            float m_InitialTime;
            public BtsmtlTimelineSectionBinding(BtsmtlSlateTimelineBinding owner, TimelineSection source)
            {
                m_Owner = owner;
                Source = source;
                Name = source.Name;
                Time = source.Time.ToSingle();
                m_InitialTime = Time;
            }
            public TimelineSection Source { get; }
            public string AuthoringId => Source.AuthoringId;
            public string DisplayName => Name;
            public bool IsLocked { get; set; }
            public string Name { get; set; }
            public float Time { get; set; }
            public Color Color => Color.white;
            public bool CommitSource()
            {
                if (!HasChanges())
                    return false;
                m_Owner.Timeline.ConfigureSection(Source, Name, Time == m_InitialTime ? Source.Time :
                    m_Owner.Session.SnapTime(FixedScalar.FromDouble(Math.Max(0d, Time))));
                m_InitialTime = Time;
                return true;
            }

            public bool HasChanges()
            {
                return !string.Equals(Source.Name, Name, StringComparison.Ordinal) || Time != m_InitialTime;
            }
        }
    }
}
#endif
