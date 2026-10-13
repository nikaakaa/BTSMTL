using System;
using System.Collections.Generic;
using BTSMTL.Diagnostics;
using BTSMTL.Diagnostics.Editor;
using BTSMTL.Timeline.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    public enum TimelineWorkspaceMode : byte
    {
        Authoring = 0,
        Preview = 1
    }

    public interface ITimelineWorkspaceModeController
    {
        ITimelineEditorAuthoringHost Authoring { get; }
        Type PreviewConfigurationType { get; }
        UnityEngine.Object PreviewConfiguration { get; set; }
        void OpenPreview(UnityEngine.Object configuration, UnityEngine.Object animationProfile);
        string PoseParameterPreviewStatus(UnityEngine.Object asset);
        void ApplyPoseParameterChanges(UnityEngine.Object asset, string graphId, object requests);
        void SynchronizePoseParameters(UnityEngine.Object asset, string graphId);
        bool TryResolvePreviewActor(out UnityEngine.Object actor, out string error);
        VisualElement CreateControls(TimelineEditorWindow window);
        void ApplyToWindow(TimelineEditorWindow window);
        void OnWindowClosed(TimelineEditorWindow window);
    }

    public static class TimelineWorkspaceModeBridge
    {
        static ITimelineWorkspaceModeController s_Controller;

        public static bool RuntimeDebugEnabled { get; private set; }
        public static event Action RuntimeDebugEnabledChanged;
        public static event Action PreviewChanged;
        public static event Action PreviewTargetChanged;

        public static Type PreviewConfigurationType => s_Controller.PreviewConfigurationType;
        public static ITimelineEditorAuthoringHost Authoring => s_Controller.Authoring;
        public static UnityEngine.Object PreviewConfiguration
        {
            get => s_Controller.PreviewConfiguration;
            set => s_Controller.PreviewConfiguration = value;
        }
        public static void OpenPreview(UnityEngine.Object configuration, UnityEngine.Object animationProfile) =>
            s_Controller.OpenPreview(configuration, animationProfile);
        public static void NotifyPreviewTargetChanged() => PreviewTargetChanged?.Invoke();

        public static void NotifyPreviewChanged() => PreviewChanged?.Invoke();

        public static string PoseParameterPreviewStatus(UnityEngine.Object asset) =>
            s_Controller == null ? "Workbench Preview 尚未装配。" : s_Controller.PoseParameterPreviewStatus(asset);

        public static void ApplyPoseParameterChanges(UnityEngine.Object asset, string graphId,
            object requests) =>
            s_Controller?.ApplyPoseParameterChanges(asset, graphId, requests);

        public static void SynchronizePoseParameters(UnityEngine.Object asset, string graphId) =>
            s_Controller?.SynchronizePoseParameters(asset, graphId);

        public static void Register(ITimelineWorkspaceModeController controller)
        {
            s_Controller = controller;
        }

        public static bool TryResolvePreviewActor(out UnityEngine.Object actor, out string error)
        {
            if (s_Controller != null)
                return s_Controller.TryResolvePreviewActor(out actor, out error);
            actor = null;
            error = "ScenePlay Controller 尚未装配。";
            return false;
        }

        public static void SetRuntimeDebugEnabled(bool enabled)
        {
            if (RuntimeDebugEnabled == enabled)
                return;
            RuntimeDebugEnabled = enabled;
            RuntimeDebugEnabledChanged?.Invoke();
        }

        public static VisualElement CreateControls(TimelineEditorWindow window) =>
            s_Controller?.CreateControls(window);

        public static void ApplyToWindow(TimelineEditorWindow window) =>
            s_Controller?.ApplyToWindow(window);

        public static void OnWindowClosed(TimelineEditorWindow window) =>
            s_Controller?.OnWindowClosed(window);
    }

    public sealed class TimelineEditorWindow : EditorWindow
    {
        static readonly List<TimelineEditorWindow> s_Windows = new();
        internal static IReadOnlyList<TimelineEditorWindow> OpenWindows => s_Windows;
        public static event Action<TimelineAsset> AssetOpened;
        public static event Action<TimelineEditorWindow, TreeClip> AssetTreeOpened;
        public static event Func<TreeClip, bool> TreeClipOpenRequested;
        internal static event Action<TimelineEditorWindow> WindowOpened;
        public static event Action<TimelineEditorWindow> WindowClosed;
        public static event Action<TimelineEditorWindow> AuthoringRevisionChanged;

        [SerializeField]
        UnityEngine.Object m_SerializedOwner;
        ITimelineTimeRangePreview m_TimeRangePreview;

        [SerializeField]
        string m_SerializedPropertyPath;

        [SerializeField]
        string m_OwnershipLabel;

        [SerializeField]
        string m_SourceNodeGuid;

        [SerializeField]
        string m_SourceGraphAuthoringId;

        [SerializeField]
        UnityEngine.Object m_NavigationOwner;

        [SerializeField]
        string m_NavigationPropertyPath;

        [SerializeField]
        string m_NavigationOwnershipLabel;

        [SerializeField]
        string m_NavigationTrackAuthoringId;

        [SerializeField]
        string m_NavigationClipAuthoringId;

        [SerializeField]
        string m_NavigationSourceNodeGuid;

        [SerializeField]
        Vector2 m_NavigationViewport;

        BtsmtlSlateTimelineProjection m_SlateProjection;
        IMGUIContainer m_SlateSurface;
        TimelineData m_Timeline;
        TimelineEditorToolbarControls m_Toolbar;
        VisualElement m_WorkspaceModeControls;
        bool m_RuntimeObservationReadOnly;
        RuntimeInstanceKey m_RuntimeObservationScope;
        RuntimeInstanceKey m_RuntimeObservationPlayback;
        bool m_RuntimeObservationPinned;
        long m_RuntimePlaybackSelectionRevision = -1;
        RuntimeDebugViewModel m_RuntimePlaybackSelectionView;
        readonly List<RuntimeTimelinePlaybackDebugSummary> m_RuntimeObservationSummaries =
            new List<RuntimeTimelinePlaybackDebugSummary>();

        public TimelineData Timeline => m_TimeRangePreview == null ? m_Timeline : m_TimeRangePreview.WorkingData;
        public Clip SelectedClip => m_SlateProjection?.Selection.Clip;
        public string SourceGraphAuthoringId => m_SourceGraphAuthoringId ?? string.Empty;
        public string SourceNodeAuthoringId => m_SourceNodeGuid ?? string.Empty;
        public TimelineAsset SourceAsset => m_SerializedOwner as TimelineAsset;
        public ITimelineTimeRangePreview TimeRangePreview => m_TimeRangePreview;
        public void ApplyClipTimeRangeNumbers(string identity, ThirdPersonSimulation.Fixed.FixedScalar start,
            ThirdPersonSimulation.Fixed.FixedScalar end) => m_SlateProjection.ApplyClipTimeRangeNumbers(identity, start, end);

        public void SetTimeRangePreview(ITimelineTimeRangePreview preview)
        {
            if (ReferenceEquals(m_TimeRangePreview, preview))
                return;
            BtsmtlSlateTimelineViewState view = m_SlateProjection.CaptureViewState();
            TimelineAsset source = SourceAsset;
            preview?.SynchronizeUndo();
            m_TimeRangePreview = preview;
            Bind(preview == null ? source.Data : preview.WorkingData,
                preview == null ? source : preview.Workspace, "m_Data",
                preview == null ? "正式作者资产" : "预览时间草稿 · 应用后保存资产", m_SourceNodeGuid, m_SourceGraphAuthoringId);
            m_SlateProjection.RestoreViewState(view);
        }
        public bool RuntimeObservationReadOnly => m_RuntimeObservationReadOnly;
        public RuntimeInstanceKey RuntimeObservationScope => m_RuntimeObservationScope;
        public RuntimeInstanceKey RuntimeObservationPlayback => m_RuntimeObservationPlayback;
        public string AuthoringRevision => m_Timeline == null
            ? string.Empty
            : TimelineAuthoringFingerprint.Compute(m_Timeline);

        public IReadOnlyList<RuntimeTimelinePlaybackDebugSummary> GetRuntimeObservationSummaries()
        {
            if (m_Timeline == null)
                return Array.Empty<RuntimeTimelinePlaybackDebugSummary>();
            RuntimeDebugSession.Shared.ViewModel.CopyTimelinePlaybackSummaries(
                m_Timeline.AuthoringId,
                SourceGraphAuthoringId,
                m_RuntimeObservationSummaries);
            if (!string.IsNullOrEmpty(SourceNodeAuthoringId))
                for (int index = m_RuntimeObservationSummaries.Count - 1; index >= 0; index--)
                    if (!m_RuntimeObservationSummaries[index].Provenance.SourceNodeAuthoringId.Equals(SourceNodeAuthoringId))
                        m_RuntimeObservationSummaries.RemoveAt(index);
            FilterRuntimeScope(m_RuntimeObservationSummaries);
            return m_RuntimeObservationSummaries;
        }

        public bool TryResolveRuntimeObservation(
            out RuntimeTimelinePlaybackDebugSummary summary,
            out string message)
        {
            RuntimeDebugViewModel view = RuntimeDebugSession.Shared.ViewModel;
            long selectionRevision = m_Timeline == null ? 0 : view.GetTimelinePlaybackRevision(
                m_Timeline.AuthoringId, SourceGraphAuthoringId);
            if (m_Timeline != null && m_RuntimeObservationPlayback.IsValid &&
                ReferenceEquals(view, m_RuntimePlaybackSelectionView) &&
                selectionRevision == m_RuntimePlaybackSelectionRevision &&
                view.TryGetTimelinePlaybackSummary(
                    m_Timeline.AuthoringId,
                    m_RuntimeObservationPlayback,
                    out summary,
                    SourceGraphAuthoringId) &&
                (string.IsNullOrEmpty(SourceNodeAuthoringId) ||
                 summary.Provenance.SourceNodeAuthoringId.Equals(SourceNodeAuthoringId)) &&
                MatchesRuntimeScope(summary))
            {
                message = string.Empty;
                return true;
            }
            IReadOnlyList<RuntimeTimelinePlaybackDebugSummary> summaries = GetRuntimeObservationSummaries();
            m_RuntimePlaybackSelectionRevision = selectionRevision;
            m_RuntimePlaybackSelectionView = view;
            if (!m_RuntimeObservationPinned)
            {
                RuntimeInstanceKey activePlayback = default;
                int activeCount = 0;
                for (int index = 0; index < summaries.Count; index++)
                {
                    if (summaries[index].IsTerminal)
                        continue;
                    activePlayback = summaries[index].Playback;
                    activeCount++;
                }
                if (activeCount == 1 && !m_RuntimeObservationPlayback.Equals(activePlayback))
                {
                    m_RuntimeObservationPlayback = activePlayback;
                    ClearRuntimeTimelineObservation();
                }
            }
            if (m_RuntimeObservationPlayback.IsValid)
            {
                for (int index = 0; index < summaries.Count; index++)
                {
                    if (!summaries[index].Playback.Equals(m_RuntimeObservationPlayback))
                        continue;
                    summary = summaries[index];
                    message = string.Empty;
                    return true;
                }
                summary = default;
                message = "已选择的运行 Timeline 实例不在当前调用路径中。";
                return false;
            }
            if (summaries.Count == 1)
            {
                summary = summaries[0];
                message = string.Empty;
                return true;
            }
            summary = default;
            message = summaries.Count > 1
                ? "当前 Timeline 对应多个运行调用，请从 SkillGraph 选择具体实例。"
                : m_Timeline == null
                    ? "当前未打开 Timeline。"
                    : string.IsNullOrEmpty(SourceGraphAuthoringId)
                        ? "当前 Timeline 没有来源图绑定。"
                        : "当前 Timeline 没有匹配的运行调用。";
            return false;
        }

        public void SetRuntimeObservationScope(RuntimeInstanceKey scope)
        {
            if (m_RuntimeObservationScope.Equals(scope))
                return;
            m_RuntimeObservationScope = scope;
            m_RuntimeObservationPlayback = default;
            m_RuntimeObservationPinned = false;
            ClearRuntimeTimelineObservation();
        }

        public void SelectRuntimeObservationPlayback(RuntimeInstanceKey playback, bool pin = false)
        {
            if (m_RuntimeObservationPlayback.Equals(playback))
            {
                m_RuntimeObservationPinned = pin;
                return;
            }
            m_RuntimeObservationPlayback = playback;
            m_RuntimeObservationPinned = pin;
            ClearRuntimeTimelineObservation();
        }

        internal void ClearRuntimeObservationSelection()
        {
            m_RuntimeObservationScope = default;
            m_RuntimeObservationPlayback = default;
            m_RuntimeObservationPinned = false;
        }

        void FilterRuntimeScope(List<RuntimeTimelinePlaybackDebugSummary> summaries)
        {
            if (!m_RuntimeObservationScope.IsValid)
                return;
            for (int index = summaries.Count - 1; index >= 0; index--)
                if (!MatchesRuntimeScope(summaries[index]))
                    summaries.RemoveAt(index);
        }

        bool MatchesRuntimeScope(RuntimeTimelinePlaybackDebugSummary summary)
        {
            if (!m_RuntimeObservationScope.IsValid)
                return true;
            RuntimeTimelinePlaybackProvenance provenance = summary.Provenance;
            if (m_RuntimeObservationScope.Kind == RuntimeInstanceKind.SkillExecution)
                return summary.Playback.ActionInstanceId == m_RuntimeObservationScope.ActionInstanceId &&
                       provenance.SkillExecutionGeneration == m_RuntimeObservationScope.ActivationGeneration &&
                       provenance.SourceActivationGeneration == m_RuntimeObservationScope.InvocationGeneration &&
                       provenance.SourceInvocationPath.Equals(m_RuntimeObservationScope.CallSiteId);
            if (m_RuntimeObservationScope.GraphRuntimeId != Guid.Empty &&
                provenance.SourceGraphRuntimeId != m_RuntimeObservationScope.GraphRuntimeId)
                return false;
            ulong scopeGeneration = m_RuntimeObservationScope.InvocationGeneration != 0
                ? m_RuntimeObservationScope.InvocationGeneration
                : m_RuntimeObservationScope.ActivationGeneration;
            return scopeGeneration == 0 || provenance.SourceActivationGeneration == scopeGeneration;
        }

        public bool FocusSource(string trackAuthoringId, string clipAuthoringId)
        {
            return m_SlateProjection != null &&
                   m_SlateProjection.FocusSource(trackAuthoringId, clipAuthoringId);
        }

        public static TimelineEditorWindow Open(TimelineAsset asset)
        {
            return Open(asset, string.Empty, string.Empty);
        }

        public static TimelineEditorWindow Open(
            TimelineAsset asset,
            string sourceGraphAuthoringId,
            string sourceNodeGuid)
        {
            if (!asset)
                return null;

            TimelineEditorWindow window = GetWindow<TimelineEditorWindow>();
            window.ClearNavigation();
            window.BindAsset(asset, sourceGraphAuthoringId, sourceNodeGuid);
            window.Show();
            window.Focus();
            return window;
        }

        public static TimelineEditorWindow FindOpen(TimelineAsset asset)
        {
            if (!asset)
                return null;
            for (int index = 0; index < s_Windows.Count; index++)
                if (ReferenceEquals(s_Windows[index].m_SerializedOwner, asset) ||
                    ReferenceEquals(s_Windows[index].m_TimeRangePreview?.Workspace, asset))
                    return s_Windows[index];
            return null;
        }

        internal void ApplyRuntimeObservationOverlay(
            float visualTime,
            IReadOnlyDictionary<string, string> activeTracks,
            IReadOnlyDictionary<string, string> activeClips,
            IReadOnlyDictionary<string, float> dynamicClipEnds)
        {
            if (!m_RuntimeObservationReadOnly)
                return;
            m_SlateProjection?.ApplyRuntimeOverlay(visualTime, activeTracks, activeClips, dynamicClipEnds);
        }

        internal void ApplyHistoryObservationOverlay(
            float visualTime,
            IReadOnlyDictionary<string, string> activeTracks,
            IReadOnlyDictionary<string, string> activeClips,
            IReadOnlyDictionary<string, float> dynamicClipEnds)
        {
            if (!m_RuntimeObservationReadOnly)
                return;
            m_SlateProjection?.ApplyHistoryOverlay(visualTime, activeTracks, activeClips, dynamicClipEnds);
        }

        public void ClearRuntimeTimelineObservation()
        {
            m_SlateProjection?.ClearRuntimeOverlay();
        }

        public void SetRuntimeObservationReadOnly(bool readOnly)
        {
            if (m_RuntimeObservationReadOnly == readOnly)
                return;
            m_RuntimeObservationReadOnly = readOnly;
            m_SlateProjection?.SetRuntimeReadOnly(readOnly);
            if (!readOnly)
            {
                ClearRuntimeObservationSelection();
                m_SlateProjection?.ClearRuntimeOverlay();
            }
        }

        public void SetRuntimeObservationStatus(string message)
        {
            SetStatus(message);
        }

        public void ApplyRuntimeLocator(string graphAuthoringId, string sourceNodeAuthoringId)
        {
            m_SourceGraphAuthoringId = graphAuthoringId ?? string.Empty;
            m_SourceNodeGuid = sourceNodeAuthoringId ?? string.Empty;
            ClearRuntimeObservationSelection();
        }

        void BindAsset(TimelineAsset asset, string sourceGraphAuthoringId, string sourceNodeGuid)
        {
            if (!asset)
                throw new ArgumentNullException(nameof(asset));
            m_RuntimeObservationReadOnly = false;
            m_TimeRangePreview = null;
            ClearRuntimeObservationSelection();
            Bind(
                asset.Data,
                asset,
                "m_Data",
                AssetDatabase.IsSubAsset(asset) ? "Private Asset" : "Shared Asset",
                sourceNodeGuid ?? string.Empty,
                sourceGraphAuthoringId ?? string.Empty);
        }

        void OnEnable() => s_Windows.Add(this);

        public void CreateGUI()
        {
            TryRestoreBinding();
            if (m_SlateProjection == null)
                BuildUnboundView();
        }

        [MenuItem("Tools/BTSMTL/Timeline Editor", false, 3)]
        public static void OpenStandalone()
        {
            TimelineEditorWindow window = GetWindow<TimelineEditorWindow>();
            if (window.m_SlateProjection == null)
                window.BuildUnboundView();
            window.Show();
            window.Focus();
        }

        void Bind(
            TimelineData timeline,
            UnityEngine.Object serializedOwner,
            string serializedPropertyPath,
            string ownershipLabel,
            string sourceNodeGuid,
            string sourceGraphAuthoringId = null)
        {
            if (timeline == null || !serializedOwner || string.IsNullOrEmpty(serializedPropertyPath))
                throw new InvalidOperationException("TimelineEditorWindow requires a bound TimelineData owner/path.");

            DisposeView();
            timeline.BindSerializedOwner(serializedOwner, serializedPropertyPath);
            timeline.OnValueChanged += OnTimelineValueChanged;
            m_SerializedOwner = m_TimeRangePreview == null ? serializedOwner : m_TimeRangePreview.Source;
            m_SerializedPropertyPath = serializedPropertyPath;
            m_OwnershipLabel = ownershipLabel;
            if (string.IsNullOrEmpty(sourceNodeGuid))
                m_SourceGraphAuthoringId = string.Empty;
            if (sourceGraphAuthoringId != null)
                m_SourceGraphAuthoringId = sourceGraphAuthoringId;
            m_SourceNodeGuid = sourceNodeGuid ?? string.Empty;
            ClearRuntimeObservationSelection();
            titleContent = new GUIContent("Timeline Editor");
            m_Timeline = timeline;

            TimelineEditorOpenRequest openRequest = TimelineEditorOpenRequestComposition.Create(
                timeline,
                serializedOwner,
                serializedPropertyPath,
                ownershipLabel, timeRangePreview: m_TimeRangePreview);
            if (!BtsmtlSlateTimelineProjection.TryOpen(
                    openRequest,
                    OpenClip,
                    () =>
                    {
                        if (m_SlateSurface != null)
                            m_SlateSurface.MarkDirtyRepaint();
                    },
                    out m_SlateProjection,
                    out string unavailableReason))
            {
                rootVisualElement.Clear();
                rootVisualElement.Add(CreateWorkspaceHeader());
                rootVisualElement.Add(new HelpBox(
                    $"Slate Timeline unavailable: {unavailableReason}",
                    HelpBoxMessageType.Error));
                return;
            }

            m_SlateProjection.AuthoringIssue += OnAuthoringIssue;
            string savedView = SessionState.GetString(TimelineViewStateKey(), string.Empty);
            if (!string.IsNullOrEmpty(savedView))
                m_SlateProjection.RestoreViewState(JsonUtility.FromJson<BtsmtlSlateTimelineViewState>(savedView));
            m_SlateProjection.SetRuntimeReadOnly(m_RuntimeObservationReadOnly);
            TimelineWorkspaceModeBridge.ApplyToWindow(this);

            AssetOpened?.Invoke(SourceAsset);
            WindowOpened?.Invoke(this);
            rootVisualElement.Clear();
            rootVisualElement.Add(CreateWorkspaceHeader());
            Label ownership = new Label($"Timeline Ownership: {m_OwnershipLabel}");
            ownership.style.unityFontStyleAndWeight = FontStyle.Bold;
            ownership.style.paddingLeft = 8f;
            ownership.style.paddingTop = 4f;
            ownership.style.paddingBottom = 4f;
            rootVisualElement.Add(ownership);
            m_SlateSurface = new IMGUIContainer(DrawSlateSurface)
            {
                name = "slate-timeline-surface"
            };
            m_SlateSurface.style.flexGrow = 1f;
            m_SlateSurface.style.flexShrink = 1f;
            m_SlateSurface.style.minHeight = 320f;
            rootVisualElement.Add(m_SlateSurface);
        }

        void BuildUnboundView()
        {
            titleContent = new GUIContent("Timeline Editor");
            rootVisualElement.Clear();
            rootVisualElement.Add(CreateWorkspaceHeader());
            SetStatus("选择一个 Timeline 资产或从 Skill Graph 打开 Timeline。");
        }

        void ClearBinding()
        {
            ClearNavigation();
            DisposeView();
            m_SerializedOwner = null;
            m_SerializedPropertyPath = string.Empty;
            m_OwnershipLabel = string.Empty;
            m_SourceNodeGuid = string.Empty;
            m_SourceGraphAuthoringId = string.Empty;
            ClearRuntimeObservationSelection();
            BuildUnboundView();
        }

        void TryRestoreBinding()
        {
            if (m_SlateProjection != null || !m_SerializedOwner || string.IsNullOrEmpty(m_SerializedPropertyPath))
                return;

            TimelineData timeline = ResolveTimelineData();
            if (timeline == null)
                return;

            Bind(
                timeline,
                m_SerializedOwner,
                m_SerializedPropertyPath,
                m_OwnershipLabel,
                m_SourceNodeGuid,
                m_SourceGraphAuthoringId);
        }

        TimelineData ResolveTimelineData()
        {
            return ResolveTimelineData(m_SerializedOwner, m_SerializedPropertyPath);
        }

        static TimelineData ResolveTimelineData(UnityEngine.Object owner, string propertyPath)
        {
            if (owner is TimelineAsset asset)
                return asset.Data;
            if (!owner || string.IsNullOrWhiteSpace(propertyPath))
                return null;

            SerializedObject serializedObject = new SerializedObject(owner);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            return property?.propertyType == SerializedPropertyType.ManagedReference
                ? property.managedReferenceValue as TimelineData
                : null;
        }

        void OpenClip(Clip clip)
        {
            UnityEngine.Object sourceAsset = TimelineAuthoringClipBinding.SourceAsset(clip);
            if (sourceAsset)
            {
                Selection.activeObject = sourceAsset;
                EditorGUIUtility.PingObject(sourceAsset);
                return;
            }
            if (clip is TreeClip assetClip && assetClip.AssetTree)
            {
                if (OpenAssetTree(assetClip))
                {
                    AssetTreeOpened?.Invoke(this, assetClip);
                    return;
                }
                ShowNotification(new GUIContent("TreeClip 的正式 FlowCanvas SkillGraph 打开失败。"));
                return;
            }
            if (clip is not TreeClip treeClip)
                return;
            ShowNotification(new GUIContent(
                treeClip.AssetTree
                    ? "TreeClip 的正式 FlowCanvas SkillGraph 打开失败。"
                    : "TreeClip 必须绑定正式 FlowCanvas SkillGraph。"));
        }

        static bool OpenAssetTree(TreeClip clip)
        {
            if (TreeClipOpenRequested == null)
                return false;
            foreach (Func<TreeClip, bool> handler in TreeClipOpenRequested.GetInvocationList())
                if (handler(clip))
                    return true;
            return false;
        }

        void OnDisable()
        {
            s_Windows.Remove(this);
            WindowClosed?.Invoke(this);
            TimelineWorkspaceModeBridge.OnWindowClosed(this);
            DisposeView();
        }

        void DisposeView()
        {
            if (m_Timeline != null)
                m_Timeline.OnValueChanged -= OnTimelineValueChanged;
            if (m_SlateProjection != null)
            {
                if (m_SerializedOwner)
                    SessionState.SetString(TimelineViewStateKey(), JsonUtility.ToJson(m_SlateProjection.CaptureViewState()));
                m_SlateProjection.AuthoringIssue -= OnAuthoringIssue;
            }
            m_SlateProjection?.Dispose();
            m_SlateProjection = null;
            m_SlateSurface = null;
            m_Toolbar = null;
            m_WorkspaceModeControls = null;
            m_Timeline = null;
        }

        string TimelineViewStateKey() => "BTSMTL.Timeline.View." +
            GlobalObjectId.GetGlobalObjectIdSlow(m_SerializedOwner) + ":" + m_SerializedPropertyPath;

        void OnAuthoringIssue(string message)
        {
            if (!string.IsNullOrEmpty(message))
                ShowNotification(new GUIContent(message));
        }

        void DrawSlateSurface()
        {
            if (m_SlateProjection == null || m_SlateSurface == null)
                return;
            Rect rect = m_SlateSurface.contentRect;
            m_SlateProjection.DrawEmbeddedGUI(
                Mathf.Max(1f, rect.width),
                Mathf.Max(1f, rect.height),
                BeginWindows,
                EndWindows);
        }

        TimelineEditorBindingState BindingState => new TimelineEditorBindingState(
            m_Timeline,
            m_SerializedOwner,
            m_SerializedPropertyPath,
            m_OwnershipLabel,
            m_SourceNodeGuid,
            m_SourceGraphAuthoringId);

        VisualElement CreateWorkspaceHeader()
        {
            m_WorkspaceModeControls = TimelineWorkspaceModeBridge.CreateControls(this);
            m_Toolbar = TimelineEditorToolbarView.Create(
                BindingState,
                HasTimelineNavigation,
                ReturnToTimeline,
                OnSharedTimelineChanged,
                m_WorkspaceModeControls);
            var header = new VisualElement();
            header.Add(m_Toolbar.Toolbar);
            header.Add(m_Toolbar.Status);
            return header;
        }

        bool HasTimelineNavigation => m_NavigationOwner && !string.IsNullOrWhiteSpace(m_NavigationPropertyPath);

        void CaptureTimelineNavigation(AnimationClip clip)
        {
            if (m_SlateProjection == null || !m_SerializedOwner || string.IsNullOrWhiteSpace(m_SerializedPropertyPath))
                throw new InvalidOperationException("Sequence navigation requires a bound Action Timeline.");
            m_NavigationOwner = m_SerializedOwner;
            m_NavigationPropertyPath = m_SerializedPropertyPath;
            m_NavigationOwnershipLabel = m_OwnershipLabel;
            m_NavigationTrackAuthoringId = clip.Track?.AuthoringId ?? string.Empty;
            m_NavigationClipAuthoringId = clip.AuthoringId;
            m_NavigationSourceNodeGuid = m_SourceNodeGuid;
            m_NavigationViewport = Vector2.zero;
        }

        void ReturnToTimeline()
        {
            if (!HasTimelineNavigation)
                return;
            UnityEngine.Object owner = m_NavigationOwner;
            string propertyPath = m_NavigationPropertyPath;
            string ownershipLabel = m_NavigationOwnershipLabel;
            string trackAuthoringId = m_NavigationTrackAuthoringId;
            string clipAuthoringId = m_NavigationClipAuthoringId;
            string sourceNodeGuid = m_NavigationSourceNodeGuid;
            TimelineData timeline = ResolveTimelineData(owner, propertyPath);
            if (timeline == null)
                throw new InvalidOperationException("The source Action Timeline can no longer be resolved.");
            ClearNavigation();
            Bind(timeline, owner, propertyPath, ownershipLabel, sourceNodeGuid);
            m_SlateProjection?.FocusSource(trackAuthoringId, clipAuthoringId);
        }

        void ClearNavigation()
        {
            m_NavigationOwner = null;
            m_NavigationPropertyPath = string.Empty;
            m_NavigationOwnershipLabel = string.Empty;
            m_NavigationTrackAuthoringId = string.Empty;
            m_NavigationClipAuthoringId = string.Empty;
            m_NavigationSourceNodeGuid = string.Empty;
            m_NavigationViewport = Vector2.zero;
            m_Toolbar?.SetBackVisible(false);
        }

        void OnSharedTimelineChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            TimelineAsset asset = evt.newValue as TimelineAsset;
            if (asset)
            {
                ClearNavigation();
                BindAsset(asset, m_SourceGraphAuthoringId, m_SourceNodeGuid);
                return;
            }

            if (m_SerializedOwner is TimelineAsset)
                ClearBinding();
            else
                m_Toolbar?.SetDocumentWithoutNotify(null);
        }

        void SetStatus(string value) => m_Toolbar?.SetStatus(value);

        void OnTimelineValueChanged()
        {
            if (m_TimeRangePreview != null)
                return;
            m_Toolbar?.SetSourceSummary(BindingState.CreateSourceSummary());
            AuthoringRevisionChanged?.Invoke(this);
        }
    }

    [InitializeOnLoad]
    public static class TimelineRuntimeObservationBridge
    {
        static readonly Dictionary<TimelineEditorWindow, RuntimeTimelineObservationBuffer> s_ObservationBuffers =
            new Dictionary<TimelineEditorWindow, RuntimeTimelineObservationBuffer>();
        static RuntimeDebugViewModel s_ObservedView;
        static RuntimeDebugAttachmentState s_ObservedAttachmentState;

        static TimelineRuntimeObservationBridge()
        {
            RuntimeDebugSession.Shared.Changed += Refresh;
            TimelineWorkspaceModeBridge.RuntimeDebugEnabledChanged += OnRuntimeDebugEnabledChanged;
            TimelineEditorWindow.WindowOpened += RefreshWindow;
            TimelineEditorWindow.WindowClosed += OnWindowClosed;
        }

        static void OnRuntimeDebugEnabledChanged()
        {
            if (TimelineWorkspaceModeBridge.RuntimeDebugEnabled)
                EditorApplication.delayCall += RefreshAfterEnabled;
            else
            {
                s_ObservationBuffers.Clear();
                IReadOnlyList<TimelineEditorWindow> windows = TimelineEditorWindow.OpenWindows;
                for (int index = 0; index < windows.Count; index++)
                    windows[index].SetRuntimeObservationReadOnly(false);
            }
        }

        static void RefreshAfterEnabled()
        {
            s_ObservedView = null;
            Refresh();
        }

        static void Refresh()
        {
            if (!TimelineWorkspaceModeBridge.RuntimeDebugEnabled)
                return;
            RuntimeDebugSession session = RuntimeDebugSession.Shared;
            RuntimeDebugViewModel view = session.ViewModel;
            bool refreshAll = !ReferenceEquals(s_ObservedView, view) ||
                              s_ObservedAttachmentState != session.AttachmentState;
            s_ObservedView = view;
            s_ObservedAttachmentState = session.AttachmentState;
            IReadOnlyList<TimelineEditorWindow> windows = TimelineEditorWindow.OpenWindows;
            for (int index = 0; index < windows.Count; index++)
            {
                TimelineEditorWindow window = windows[index];
                if (window.RuntimeObservationReadOnly && window.Timeline != null &&
                    (refreshAll || view.Changes.AffectsTimeline(window.Timeline.AuthoringId, default)))
                    Refresh(window);
            }
        }

        public static void RefreshWindow(TimelineEditorWindow window)
        {
            Refresh(window);
        }

        static void OnWindowClosed(TimelineEditorWindow window)
        {
            s_ObservationBuffers.Remove(window);
        }

        static void Refresh(TimelineEditorWindow window)
        {
            if (!window || window.Timeline == null)
                return;
            if (!window.RuntimeObservationReadOnly)
            {
                s_ObservationBuffers.Remove(window);
                return;
            }
            if (window.TryResolveRuntimeObservation(
                    out RuntimeTimelinePlaybackDebugSummary summary,
                    out string observationMessage))
            {
                ApplyRuntimeObservation(window, summary);
                return;
            }
            s_ObservationBuffers.Remove(window);
            window.ClearRuntimeTimelineObservation();
            if (!string.IsNullOrEmpty(observationMessage))
                window.SetRuntimeObservationStatus(observationMessage);
        }

        static void ApplyRuntimeObservation(
            TimelineEditorWindow window,
            RuntimeTimelinePlaybackDebugSummary summary)
        {
            RuntimeTimelineObservationBuffer observation = GetObservationBuffer(window);
            Dictionary<string, string> activeTracks = observation.ActiveTracks;
            Dictionary<string, string> activeClips = observation.ActiveClips;
            activeTracks.Clear();
            activeClips.Clear();
            Dictionary<string, float> dynamicClipEnds = observation.DynamicClipEnds;
            dynamicClipEnds.Clear();
            Dictionary<string, RuntimeDebugEventView> latestClips = observation.LatestClips;
            latestClips.Clear();
            Dictionary<string, RuntimeDynamicTreeClipObservation> dynamicClips = observation.DynamicClips;
            dynamicClips.Clear();
            RuntimeDebugSession.Shared.ViewModel.CopyTimelineCurrentEvents(
                window.Timeline.AuthoringId,
                summary.Playback,
                observation.EventBuffer,
                summary.Provenance.SourceGraphAuthoringId.ToString());
            IReadOnlyList<RuntimeDebugEventView> events = observation.EventBuffer;
            for (int index = 0; index < events.Count; index++)
            {
                RuntimeDebugEventView item = events[index];
                RuntimeSourceElementKey source = item.Source;
                bool presentation = item.Event.Domain == RuntimeTraceDomain.Presentation;
                ulong latestPosition = presentation ? summary.LatestPresentationFrame : summary.LatestLogicTick;
                int cycle = presentation ? summary.VisualCycle : summary.LogicCycle;
                bool terminal = !presentation && summary.IsTerminal;
                if (source.Kind == RuntimeSourceElementKind.Track && !string.IsNullOrEmpty(source.TrackAuthoringId))
                {
                    if (!terminal && item.Event.Position == latestPosition &&
                        item.Event.Payload.Cycle == cycle)
                        activeTracks.TryAdd(source.TrackAuthoringId, new RuntimeElementDebugState(item).Status);
                }
                else if ((source.Kind == RuntimeSourceElementKind.Clip || source.Kind == RuntimeSourceElementKind.TreeClip) &&
                         !string.IsNullOrEmpty(source.ClipAuthoringId))
                {
                    int eventCycle = item.Event.Payload.Cycle;
                    if (eventCycle > cycle || item.Event.Position > latestPosition)
                        continue;
                    if (source.Kind == RuntimeSourceElementKind.TreeClip && eventCycle == cycle &&
                        item.Event.Payload.Detail.Equals("TreeDecision"))
                    {
                        dynamicClips.TryGetValue(source.ClipAuthoringId, out RuntimeDynamicTreeClipObservation dynamicClip);
                        dynamicClip.Observe(item);
                        dynamicClips[source.ClipAuthoringId] = dynamicClip;
                    }
                    if (item.Event.Kind is RuntimeTraceEventKind.ClipActive or
                        RuntimeTraceEventKind.TreeClipEntered or
                        RuntimeTraceEventKind.TreeClipUpdated or
                        RuntimeTraceEventKind.TreeClipExited or
                        RuntimeTraceEventKind.TreeClipDestroyed)
                    {
                        if (!latestClips.TryGetValue(source.ClipAuthoringId, out RuntimeDebugEventView latest) ||
                            item.Event.Sequence > latest.Event.Sequence)
                            latestClips[source.ClipAuthoringId] = item;
                    }
                }
            }
            foreach (KeyValuePair<string, RuntimeDynamicTreeClipObservation> pair in dynamicClips)
            {
                bool presentation = pair.Value.Latest.Event.Domain == RuntimeTraceDomain.Presentation;
                dynamicClipEnds.Add(pair.Key, pair.Value.EndTime(presentation ? summary.VisualTime : summary.LogicTime));
            }
            foreach (KeyValuePair<string, RuntimeDebugEventView> pair in latestClips)
            {
                RuntimeDebugEventView item = pair.Value;
                bool presentation = item.Event.Domain == RuntimeTraceDomain.Presentation;
                bool current = (presentation || !summary.IsTerminal) &&
                               item.Event.Position == (presentation ? summary.LatestPresentationFrame : summary.LatestLogicTick) &&
                               item.Event.Payload.Cycle == (presentation ? summary.VisualCycle : summary.LogicCycle);
                string status = !current ? "已执行" :
                    item.Event.Kind is RuntimeTraceEventKind.TreeClipExited or RuntimeTraceEventKind.TreeClipDestroyed
                        ? "已退出" : dynamicClips.ContainsKey(pair.Key) ? "open" : item.Event.Payload.Status.ToString();
                activeClips.Add(pair.Key, status);
            }
            if (RuntimeDebugSession.Shared.AttachmentState is RuntimeDebugAttachmentState.CaptureHistory or RuntimeDebugAttachmentState.Ended)
                window.ApplyHistoryObservationOverlay(summary.VisualTime, activeTracks, activeClips, dynamicClipEnds);
            else
                window.ApplyRuntimeObservationOverlay(summary.VisualTime, activeTracks, activeClips, dynamicClipEnds);
        }

        static RuntimeTimelineObservationBuffer GetObservationBuffer(TimelineEditorWindow window)
        {
            if (!s_ObservationBuffers.TryGetValue(window, out RuntimeTimelineObservationBuffer observation))
            {
                observation = new RuntimeTimelineObservationBuffer();
                s_ObservationBuffers.Add(window, observation);
            }
            return observation;
        }

    }
}
