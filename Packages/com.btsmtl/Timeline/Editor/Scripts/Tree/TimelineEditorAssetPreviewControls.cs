using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using Slate;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    public sealed partial class TimelineEditorWindow
    {
        // 窗口记住的预览：档位、Scene 开关与游标；域重载与退出 Play 后按这些恢复同一预览。
        [SerializeField] TimelinePreviewMode m_PreviewMode;
        [SerializeField] bool m_PreviewHidesScene;
        [SerializeField] float m_PreviewTime;
        bool m_RestorePreviewTime;
        Label m_AssetPreviewStatus;
        ToolbarButton m_PreviewRetry;
        ToolbarButton m_PreviewCopyDetail;
        ObjectField m_AssetPreviewConfiguration;
        ToolbarToggle m_FastTier;
        ToolbarToggle m_RealTier;
        ToolbarToggle m_SceneToggle;
        ToolbarButton m_FastPlayback;
        Image m_FastPlaybackIcon;
        Label m_PreviewTimeLabel;
        ToolbarButton m_SaveButton;
        TimelinePreviewRealControls m_RealControls;
        Rect m_InputHeaderAnchor;
        Vector2 m_PreviewInputsScroll;
        ulong m_AppliedAssetSample;
        long m_PreviewPathRevision = -1;
        int m_PreviewPathPlayback = -1;
        bool m_SeekToSourcePlayback;
        TimelineAsset m_RouteInputs;
        readonly List<UnityEngine.Object> m_UnsavedAssets = new();

        // 编辑行右侧的保存：只在本技能根有未保存作者资产时出现。
        ToolbarButton CreateSaveButton()
        {
            m_SaveButton = new ToolbarButton(SaveRouteAssets);
            m_SaveButton.schedule.Execute(RefreshSaveButton).Every(500);
            RefreshSaveButton();
            return m_SaveButton;
        }

        // 预览行：档位、播放与时刻（出错时换成原因、重试与复制详情）、真实档控制与结论，右侧显示角色与预览场景；
        // 真实档的单步、动作与控制输入在下一行。方案与条件在轨道区预览输入组的组头。
        VisualElement CreateAssetPreviewRow()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            var panel = new VisualElement();
            var row = new Toolbar();
            row.style.flexWrap = Wrap.Wrap;
            row.style.height = StyleKeyword.Auto;
            row.style.minHeight = 22f;
            row.style.alignItems = Align.Center;
            m_FastTier = new ToolbarToggle { text = "快速推导", tooltip = "按静态推导即时显示，改输入与作者内容立即重新推导" };
            m_RealTier = new ToolbarToggle { text = "真实重演", tooltip = "以同一输入方案经正式会话重演，确认物理与命中，并与快速推导对比" };
            m_FastTier.RegisterValueChangedCallback(change => SelectPreviewTier(TimelinePreviewMode.Fast));
            m_RealTier.RegisterValueChangedCallback(change => SelectPreviewTier(TimelinePreviewMode.Real));
            row.Add(m_FastTier);
            row.Add(m_RealTier);
            m_FastPlaybackIcon = new Image { scaleMode = ScaleMode.ScaleToFit, style = { width = 16f, height = 16f } };
            m_FastPlayback = new ToolbarButton(() => preview.SetPlaying(!preview.IsPlaying));
            m_FastPlayback.Add(m_FastPlaybackIcon);
            row.Add(m_FastPlayback);
            m_PreviewTimeLabel = new Label { style = { minWidth = 64f, unityTextAlign = TextAnchor.MiddleLeft, paddingLeft = 4f } };
            row.Add(m_PreviewTimeLabel);
            m_RealControls = preview.CreateRealControls();
            if (m_RealControls != null)
            {
                m_RealControls.Transport.style.flexDirection = FlexDirection.Row;
                m_RealControls.Transport.style.alignItems = Align.Center;
                m_RealControls.AuthoringMenu.AppendAction("打开所选片段图",
                    _ => m_SlateProjection.SelectedClipGraphNavigation.Invoke(),
                    _ => m_SlateProjection?.SelectedClipGraphNavigation != null
                        ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                row.Add(m_RealControls.Transport);
            }
            m_AssetPreviewStatus = new Label();
            m_AssetPreviewStatus.style.flexGrow = 1f;
            m_AssetPreviewStatus.style.flexShrink = 1f;
            m_AssetPreviewStatus.style.minWidth = 0f;
            m_AssetPreviewStatus.style.paddingLeft = 6f;
            m_AssetPreviewStatus.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_AssetPreviewStatus.style.overflow = Overflow.Hidden;
            m_AssetPreviewStatus.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(m_AssetPreviewStatus);
            m_PreviewRetry = new ToolbarButton(() => preview.Retry()) { text = "重试" };
            row.Add(m_PreviewRetry);
            m_PreviewCopyDetail = new ToolbarButton(() => EditorGUIUtility.systemCopyBuffer = m_AssetPreviewStatus.tooltip)
            {
                text = "复制详情",
                tooltip = "复制完整错误信息"
            };
            row.Add(m_PreviewCopyDetail);
            m_SceneToggle = new ToolbarToggle { text = "显示角色", tooltip = "在 Scene 窗口显示预览角色；关掉只释放预览场景，路线与推导保留" };
            m_SceneToggle.RegisterValueChangedCallback(change =>
            {
                m_PreviewHidesScene = !change.newValue;
                preview.SetShowsScene(change.newValue);
            });
            row.Add(m_SceneToggle);
            row.Add(new Label("场景") { style = { paddingLeft = 6f, paddingRight = 2f, unityTextAlign = TextAnchor.MiddleLeft } });
            m_AssetPreviewConfiguration = new ObjectField
            {
                objectType = preview.ConfigurationType,
                allowSceneObjects = false,
                value = preview.Configuration,
                tooltip = "快速推导与真实重演共用的预览场景；更换后原地重新准备"
            };
            m_AssetPreviewConfiguration.style.width = 220f;
            m_AssetPreviewConfiguration.style.flexShrink = 0f;
            m_AssetPreviewConfiguration.RegisterValueChangedCallback(change => preview.Configuration = change.newValue);
            row.Add(m_AssetPreviewConfiguration);
            panel.Add(row);
            if (m_RealControls != null)
                panel.Add(m_RealControls.Settings);
            panel.SetEnabled(SourceAsset);
            preview.Changed += RefreshAssetPreviewControls;
            RefreshAssetPreviewControls();
            return panel;
        }

        void SelectPreviewTier(TimelinePreviewMode mode)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            bool current = preview.IsOpen && preview.Mode == mode;
            m_PreviewMode = mode;
            if (!current)
            {
                m_RestorePreviewTime = false;
                OpenTimelinePreview();
            }
            RefreshAssetPreviewControls();
        }

        void OpenTimelinePreview()
        {
            m_SeekToSourcePlayback = !m_RestorePreviewTime;
            TimelineAssetPreviewHost.Shared.Open(SelectedRoot(), m_PreviewMode, !m_PreviewHidesScene);
        }

        // 窗口所选的预览根：顶栏选中的编排根；当前 Timeline 不属于任何根时为它自身。
        TimelinePreviewRoot SelectedRoot()
        {
            if (m_StateContexts.Count == 0)
                return TimelinePreviewRoot.Standalone(SourceAsset);
            var context = m_StateContexts[m_StateContextIndex];
            return TimelinePreviewRoot.Orchestration(context.Query, context.Machine);
        }

        // 窗口有主体且未在 Play 中时预览常开；在同一根的 Timeline 间切换只换显示，输入 Timeline 本身沿用进入前的预览。
        void EnsurePreview()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (!SourceAsset || !preview.IsRegistered || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || preview.IsOpeningSession ||
                TimelinePreviewInputSchemeStore.OwnsInputTimeline(m_Timeline) ||
                preview.IsOpen && preview.Root.SameAs(SelectedRoot()))
                return;
            OpenTimelinePreview();
        }

        void OnPreviewPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                m_RestorePreviewTime = true;
                EnsurePreview();
            }
            RefreshAssetPreviewControls();
        }

        // 本技能根内未保存的作者资产：路线上与技能引用的 Timeline 及其所在资产。
        void CollectUnsavedAssets(TimelineAssetPreviewHost preview)
        {
            m_UnsavedAssets.Clear();
            var timelines = new HashSet<TimelineAsset>();
            if (SourceAsset)
                timelines.Add(SourceAsset);
            if (preview.IsOpen && preview.Query != null)
                foreach (OrchestrationStaticTimeline timeline in preview.Query.Query.Timelines)
                    if (preview.Query.ResolveTimeline(timeline.Identity) is { } resolved && resolved)
                        timelines.Add(resolved);
            // Clip／Marker 的节点图是同文件子资产，只改图时主资产与 Timeline 都不脏，按整份文件判断。
            foreach (string path in timelines.Select(AssetDatabase.GetAssetPath).Distinct())
            {
                UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main && AssetDatabase.LoadAllAssetsAtPath(path).Any(EditorUtility.IsDirty))
                    m_UnsavedAssets.Add(main);
            }
        }

        void RefreshSaveButton()
        {
            if (m_SaveButton == null)
                return;
            CollectUnsavedAssets(TimelineAssetPreviewHost.Shared);
            SetDisplayed(m_SaveButton, m_UnsavedAssets.Count != 0);
            if (m_UnsavedAssets.Count == 0)
                return;
            m_SaveButton.text = $"保存 {m_UnsavedAssets.Count}";
            m_SaveButton.tooltip = "未保存：\n" + string.Join("\n", m_UnsavedAssets.Select(asset => AssetDatabase.GetAssetPath(asset)));
        }

        void SaveRouteAssets()
        {
            CollectUnsavedAssets(TimelineAssetPreviewHost.Shared);
            foreach (UnityEngine.Object asset in m_UnsavedAssets)
                AssetDatabase.SaveAssetIfDirty(asset);
            RefreshSaveButton();
        }

        void DisposeAssetPreviewBindings()
        {
            TimelineAssetPreviewHost.Shared.Changed -= RefreshAssetPreviewControls;
            m_AssetPreviewStatus = null;
            m_PreviewRetry = null;
            m_PreviewCopyDetail = null;
            m_AssetPreviewConfiguration = null;
            m_FastTier = null;
            m_RealTier = null;
            m_SceneToggle = null;
            m_FastPlayback = null;
            m_FastPlaybackIcon = null;
            m_PreviewTimeLabel = null;
            m_SaveButton = null;
            m_RealControls = null;
            m_AppliedAssetSample = 0;
            m_PreviewPathRevision = -1;
            m_PreviewPathPlayback = -1;
            m_RouteInputs = null;
        }

        void RefreshAssetPreviewControls()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            bool active = preview.IsOpen && preview.Covers(SourceAsset);
            if (active && preview.Route.Count != 0 && !preview.IsReal)
            {
                // 恢复（域重载、退出 Play）回到原游标；新打开时游标放到本 Timeline 第一次播放的起点。
                if (m_RestorePreviewTime)
                {
                    m_RestorePreviewTime = false;
                    m_SeekToSourcePlayback = false;
                    preview.SetTime(m_PreviewTime);
                    return;
                }
                if (m_SeekToSourcePlayback && preview.TryGetPlayback(SourceAsset, out TimelinePreviewRouteSegment[] own))
                {
                    m_SeekToSourcePlayback = false;
                    preview.SetTime(own[0].Start.AsSingle);
                    return;
                }
            }
            if (active && !m_RestorePreviewTime)
            {
                m_PreviewTime = preview.Time;
                if (!preview.IsReal)
                    m_PreviewHidesScene = !preview.ShowsScene;
            }
            bool real = m_PreviewMode == TimelinePreviewMode.Real;
            if (m_AssetPreviewConfiguration != null)
            {
                m_AssetPreviewConfiguration.objectType = preview.ConfigurationType;
                m_AssetPreviewConfiguration.SetValueWithoutNotify(preview.Configuration);
                m_AssetPreviewConfiguration.SetEnabled(preview.IsRegistered);
            }
            // 出错时播放与时刻整段让位给原因和重试，不留看似可用的控件。
            bool failed = !preview.IsRegistered || preview.Lifecycle == TimelinePreviewLifecycle.Failed;
            if (m_FastTier != null)
            {
                m_FastTier.SetValueWithoutNotify(!real);
                m_RealTier.SetValueWithoutNotify(real);
                m_SceneToggle.SetValueWithoutNotify(!m_PreviewHidesScene);
                SetDisplayed(m_SceneToggle, !real);
                m_SceneToggle.SetEnabled(active);
                SetDisplayed(m_PreviewTimeLabel, active);
                m_PreviewTimeLabel.text = active ? $"{preview.Time:0.000} 秒" : string.Empty;
            }
            if (m_FastPlayback != null)
            {
                SetDisplayed(m_FastPlayback, !real && active);
                m_FastPlaybackIcon.image = EditorGUIUtility.IconContent(preview.IsPlaying ? "PauseButton" : "PlayButton").image;
                m_FastPlayback.tooltip = preview.IsPlaying ? "暂停快速推导" : "播放快速推导";
            }
            if (m_RealControls != null)
            {
                SetDisplayed(m_RealControls.Transport, real && !failed);
                SetDisplayed(m_RealControls.Settings, real && !failed);
            }
            if (m_AssetPreviewStatus != null)
                RefreshAssetPreviewStatus(preview, active);
            ApplyPreviewPath(active);
            if (active && m_AppliedAssetSample != preview.SampledRevision)
            {
                m_AppliedAssetSample = preview.SampledRevision;
                if (m_SlateProjection?.ShowsRoute == true)
                    m_SlateProjection.ApplyRouteTime(preview.Time);
                else if (preview.SampledSource == SourceAsset && m_SlateProjection != null &&
                    m_SlateProjection.CurrentTime != preview.LocalTime)
                    m_SlateProjection.ApplyAuthoringPreviewTime(preview.LocalTime);
            }
            m_SlateSurface?.MarkDirtyRepaint();
        }

        static void SetDisplayed(VisualElement element, bool displayed) =>
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;

        // 预览行的一句结论：先是生命周期（Play 中、出错、等待、准备中），就绪时为所在 Timeline 与本地时刻、
        // 结果是否最新；档位与全局时刻已在左侧。出错带原因第一行，完整异常、会话时刻、过期原因与差异进悬停说明。
        void RefreshAssetPreviewStatus(TimelineAssetPreviewHost preview, bool active)
        {
            string text;
            string detail = string.Empty;
            Color color = new(.78f, .78f, .78f);
            Color warning = new(1f, .78f, .3f);
            Color error = new(.95f, .45f, .4f);
            bool retry = false;
            if (!preview.IsRegistered)
                text = TimelineAssetPreviewHost.MissingProviderReason;
            else if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                text = "等待 · Play 模式中，退出后自动恢复预览";
                color = warning;
            }
            else if (preview.Lifecycle == TimelinePreviewLifecycle.Failed)
            {
                detail = preview.LifecycleDetail;
                string cause = FirstLine(detail.Trim());
                text = "预览出错 · " + preview.LifecycleReason + (cause.Length == 0 ? string.Empty : "：" + cause);
                color = error;
                retry = true;
            }
            else if (preview.Error.Length != 0)
            {
                text = FirstLine(preview.Error);
                detail = preview.Error;
                color = error;
            }
            else if (preview.Lifecycle == TimelinePreviewLifecycle.Waiting)
            {
                text = "等待 · " + preview.LifecycleReason;
                color = warning;
            }
            else if (preview.Lifecycle == TimelinePreviewLifecycle.Preparing || preview.IsOpeningSession)
                text = "准备中 · " + preview.LifecycleReason;
            else if (!active)
                text = m_Toolbar.Status;
            else if (!preview.IsReal && !preview.ShowsScene)
            {
                text = "未显示角色，只推导路线";
                detail = preview.Chain?.Reason ?? string.Empty;
            }
            else
            {
                string at;
                if (m_SlateProjection?.ShowsRoute == true)
                    at = $"在 {preview.SampledSource?.name} 本地 {preview.LocalTime:0.000} 秒";
                else
                {
                    string playback = preview.TryGetPlayback(SourceAsset, out TimelinePreviewRouteSegment[] spans)
                        ? spans.Length > 1 ? $"{spans.Length} 圈" : string.Empty
                        : "从根按当前输入没有走到本 Timeline";
                    at = $"{preview.SampledSource?.name} {preview.LocalTime:0.000} 秒" +
                        (playback.Length == 0 ? string.Empty : " · " + playback);
                }
                string note = preview.LocalNote.Length == 0 ? string.Empty : "\n" + preview.LocalNote;
                if (preview.IsReal)
                {
                    string result = preview.RealStale ? "结果过期"
                        : preview.Differences.Count == 0 ? "与快速推导一致"
                        : $"{preview.Differences.Count} 处与快速推导不同";
                    if (preview.RealLeftRootAt is { } left)
                        result += $" · {left:0.000} 秒后离开本根";
                    text = $"{at} · {result}";
                    detail = $"会话 {preview.Time:0.000} 秒{note}" +
                        (preview.RealStale ? "\n结果过期，待重演：" + preview.RealStaleReason : string.Empty) +
                        (preview.RealLeftRootAt is { } exit ? $"\n第 {exit:0.000} 秒起执行到本根之外，超出快速档范围，不参与对比。" : string.Empty);
                    foreach (TimelinePreviewDifference difference in preview.Differences)
                        detail += $"\n第 {difference.Time:0.000} 秒：{difference.Message}";
                    if (preview.RealStale)
                        color = new Color(1f, .78f, .3f);
                }
                else
                {
                    text = at + (preview.Status.Length == 0 ? string.Empty : " · " + FirstLine(preview.Status));
                    detail = $"预览 {preview.Time:0.000} 秒{note}\n{preview.Status}\n{preview.Chain?.Reason}";
                }
            }
            if (active && preview.IsReal && m_RealControls != null)
            {
                var session = m_RealControls.Status();
                if (session.Summary.Length != 0)
                    text += " · " + session.Summary;
                detail += "\n" + session.Detail;
            }
            m_AssetPreviewStatus.text = text;
            m_AssetPreviewStatus.tooltip = detail.Trim();
            m_AssetPreviewStatus.style.color = color;
            SetDisplayed(m_PreviewRetry, retry);
            SetDisplayed(m_PreviewCopyDetail, retry && m_AssetPreviewStatus.tooltip.Length != 0);
        }

        static string FirstLine(string value)
        {
            int end = value.IndexOf('\n');
            return end < 0 ? value : value[..end];
        }

        // 有技能根上下文、窗口在导航起点且不是输入 Timeline 本身时，预览以路线视图显示；双击组头进入的单个 Timeline 不切。
        bool ShowsRouteView(TimelineAssetPreviewHost preview, bool active) =>
            active && preview.Query != null && preview.Route.Count != 0 && !HasTimelineNavigation &&
            !TimelinePreviewInputSchemeStore.OwnsInputTimeline(m_Timeline);

        // 预览路线视图：输入 Timeline 一组、路线上每次播放一组，摆在预览全局时刻。
        void ApplyRouteView(TimelineAssetPreviewHost preview)
        {
            TimelineAsset inputs = preview.Preferences.InputTimeline;
            if (m_PreviewPathRevision == preview.Revision && m_RouteInputs == inputs && m_SlateProjection.ShowsRoute)
                return;
            m_PreviewPathRevision = preview.Revision;
            m_RouteInputs = inputs;
            float duration = preview.Duration;
            // 时间轴要容下路线结束后的输入与各组完整内容，路线结束后的部分压暗并在输入组头说明。
            float length = inputs ? Mathf.Max(duration, inputs.Data.Duration) : duration;
            var groups = new List<TimelineRouteGroup>
            {
                new(inputs, "预览输入", 0f, 0f, duration, inputs ? OpenInputTimeline : null,
                    (graph, start) => DescribeInputContent(preview, graph, start), DrawInputGroupHeader)
            };
            foreach (TimelinePreviewRouteSegment segment in preview.Route.OrderBy(value => value.Start))
            {
                float start = segment.Start.AsSingle;
                float end = segment.End.AsSingle;
                float offset = start - segment.LocalStart.AsSingle;
                string note = segment.IsNested ? " · 被调用" : string.Empty;
                if (segment.Cycle > 0)
                    note += $" · 第 {segment.Cycle + 1} 圈";
                if (preview.IsReal && segment.TryLocal(segment.End, out OrchestrationTime local, out _) &&
                    Math.Abs(local.AsSingle - (segment.LocalStart.AsSingle + end - start)) > 0.001f)
                    note += " · 含停顿，组内位置按 1 倍速";
                TimelineAsset source = segment.Source;
                length = Mathf.Max(length, offset + source.Data.Duration);
                groups.Add(new TimelineRouteGroup(source,
                    $"{source.name} · {preview.DescribeState(segment.State)} · {start:0.00}–{end:0.00} s{note}",
                    offset, start, end, () => EditorApplication.delayCall += () => NavigateTimeline(source, string.Empty)));
            }
            m_SlateProjection.ShowRoute(groups, length, RouteMarks(preview), preview.IsReal,
                () => preview.InputSchemes.EnsureInputTimeline());
            m_SlateProjection.ApplyRouteTime(preview.Time);
        }

        // 路线上的转移时刻：快速档来自推导链（含并行分支），真实档来自执行记录。
        static List<TimelinePreviewMark> RouteMarks(TimelineAssetPreviewHost preview)
        {
            var marks = new List<TimelinePreviewMark>();
            if (preview.IsReal)
            {
                foreach (TimelinePreviewRealTransition transition in preview.RealTransitions)
                    marks.Add(new TimelinePreviewMark(transition.Time,
                        transition.Target == null ? "实际收招" : "实际转移到 " + preview.DescribeState(transition.Target),
                        $"会话第 {transition.Time:0.000} 秒"));
                foreach (TimelinePreviewDifference difference in preview.Differences)
                    marks.Add(new TimelinePreviewMark((float)difference.Time, "与快速不同", difference.Message));
            }
            else if (preview.Chain != null)
                AddChainMarks(preview, preview.Chain, marks);
            return marks;
        }

        static void AddChainMarks(TimelineAssetPreviewHost preview, TimelineStaticChain chain, List<TimelinePreviewMark> marks)
        {
            foreach (TimelineStaticChainMarker marker in chain.Markers)
                marks.Add(new TimelinePreviewMark(marker.Time.AsSingle,
                    marker.Transition.Target == null ? "收招" : "转移到 " + preview.Query.DescribeState(marker.Transition.Target),
                    $"预览第 {marker.Time.AsSingle:0.00} 秒"));
            foreach (TimelineStaticChainBranch branch in chain.Branches)
                AddChainMarks(preview, branch.Chain, marks);
        }

        // 资产预览路线叠加到编辑时间轴：全部换算为本 Timeline 的本地时间。
        void ApplyPreviewPath(bool active)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (m_SlateProjection == null)
                return;
            if (ShowsRouteView(preview, active))
            {
                ApplyRouteView(preview);
                return;
            }
            if (m_SlateProjection.ShowsRoute)
            {
                m_SlateProjection.HideRoute();
                m_PreviewPathRevision = -2;
            }
            if (!active)
            {
                if (m_PreviewPathRevision != -1)
                {
                    m_SlateProjection.SetPreviewPath(null, null, null);
                    RefreshNestedViews();
                }
                m_PreviewPathRevision = -1;
                return;
            }
            // 编辑时间轴只显示本 Timeline 的一次播放（游标所在的，否则第一次）；换播放时重建叠加。
            preview.TryGetPlayback(SourceAsset, out TimelinePreviewRouteSegment[] spans);
            int playback = spans.Length == 0 ? -1 : spans[0].Playback;
            if (m_PreviewPathRevision == preview.Revision && m_PreviewPathPlayback == playback)
                return;
            m_PreviewPathRevision = preview.Revision;
            m_PreviewPathPlayback = playback;
            RefreshNestedViews();
            var visited = spans.Select(span => new Vector2(span.LocalStart.AsSingle, span.LocalEnd.AsSingle)).ToArray();
            var marks = new List<TimelinePreviewMark>();
            TimelineStaticChain chain = preview.Chain;
            if (preview.IsReal)
            {
                // 真实档：标出源状态在这次播放中正式选中的转移；与快速推导的差异一并标出。
                foreach (TimelinePreviewRealTransition transition in preview.RealTransitions)
                    if (transition.Playback != null && spans.Any(span => SamePlayback(span, transition.Playback.Record)))
                        marks.Add(new TimelinePreviewMark(transition.LocalTime,
                            transition.Transition == null ? "实际转移（连线已不在当前内容）"
                                : transition.Target == null ? "实际收招" : "实际转移到 " + preview.DescribeState(transition.Target),
                            $"会话第 {transition.Time:0.000} 秒" + (transition.LocalNote == null ? string.Empty : " · " + transition.LocalNote)));
                foreach (TimelinePreviewDifference difference in preview.Differences)
                    if (difference.State != null && preview.Query.ResolveTimeline(difference.State.TimelineIdentity) == SourceAsset &&
                        TryLocal(spans, difference.Time, out float local, out _, out _))
                        marks.Add(new TimelinePreviewMark(local, "与快速不同", difference.Message));
            }
            else if (chain != null)
            {
                foreach (TimelineStaticChainMarker marker in chain.Markers)
                {
                    if (preview.Query.ResolveTimeline(marker.Source.TimelineIdentity) != SourceAsset ||
                        !spans.Any(span => span.Contains(marker.Time)))
                        continue;
                    string label = marker.Transition.Target == null ? "收招"
                        : "转移到 " + preview.Query.DescribeState(marker.Transition.Target);
                    marks.Add(new TimelinePreviewMark(marker.FromLocal.AsSingle, label,
                        $"预览第 {marker.Time.AsSingle:0.00} 秒"));
                }
                TimelineStaticChainSpan last = chain.Spans.Count == 0 ? null : chain.Spans[^1];
                if (last != null && chain.StopReason != TimelineStaticChainStopReason.StateMachineExited &&
                    spans.Any(span => span.Start == last.Start))
                    marks.Add(new TimelinePreviewMark(last.LocalEnd.AsSingle,
                        chain.StopReason == TimelineStaticChainStopReason.TimelineCompleted ? "播完收招" : "预览结束",
                        chain.Reason));
            }
            m_SlateProjection.SetPreviewPath(SourceAsset.name, visited, marks);
        }

        // 预览输入组中一个 Clip 或 Marker 的文字：所写的值，加上各输入在起点的推导或执行结论（接上的标 ✔）。
        static string DescribeInputContent(TimelineAssetPreviewHost preview, ScriptableObject graph, OrchestrationTime start)
        {
            if (!preview.TryDescribeInputGraph(graph, out string name, out IReadOnlyList<string> inputIds))
                return name.Length != 0 ? name + " · 没有写入按键" : "未引用片段图";
            var parts = new List<string> { name };
            foreach (TimelinePreviewInputPoint point in preview.Preferences.Inputs)
            {
                if (OrchestrationTime.FromSeconds(point.Time) != start || !inputIds.Contains(point.InputId))
                    continue;
                bool accepted = DescribeInput(preview, point, out string reason);
                parts.Add((accepted ? "✔ " : string.Empty) + reason);
            }
            return string.Join(" · ", parts);
        }

        void OpenInputTimeline()
        {
            TimelineAsset inputs = TimelineAssetPreviewHost.Shared.InputSchemes.EnsureInputTimeline();
            EditorApplication.delayCall += () => NavigateTimeline(inputs, string.Empty);
        }

        static bool SamePlayback(TimelinePreviewRouteSegment span, BtsmtlExecutionRecord.RecordedPlayback played) =>
            span.Recorded is { } recorded && recorded.PlaybackId == played.PlaybackId && recorded.Owner.Equals(played.Owner);

        // 开关类输入的按下（true）与松开（false）；其它类型返回 null。
        static bool? PreviewInputPressed(TimelineAssetPreviewHost preview, TimelinePreviewInputPoint point)
        {
            foreach (GraphHostInputDescriptor descriptor in preview.Inputs)
                if (descriptor.InputId == point.InputId)
                    return descriptor.ValueKind == GraphHostValueKind.Bool ? point.Boolean : null;
            return null;
        }

        // 输入点与转移的关系：读取列表与有效期重叠只用于找出相关转移；只有转移条件沿“全部成立”必须读到该开关键按下、
        // 且这一点是有效期内该键唯一的值时，才确认它参与成立并标为接上。OR、NOT、非开关输入只说明相关，不声称因果。
        static bool DescribeInput(TimelineAssetPreviewHost preview, TimelinePreviewInputPoint point, out string reason)
        {
            List<TimelinePreviewInputPoint> points = preview.Preferences.Inputs;
            double until = preview.Preferences.RangeEnd;
            bool request = false;
            foreach (TimelinePreviewInputRequest value in preview.InputRequests)
                if (value.InputId == point.InputId)
                {
                    until = point.Time + value.DurationSeconds;
                    request = true;
                }
            if (!request)
                foreach (TimelinePreviewInputPoint other in points)
                    if (other != point && other.InputId == point.InputId && other.Time > point.Time)
                        until = Math.Min(until, other.Time);
            string window = $"有效期 {point.Time:0.000}–{until:0.000} 秒";
            bool pressed = PreviewInputPressed(preview, point) == true;
            if (preview.IsReal)
            {
                if (preview.RealStale)
                {
                    reason = "结果过期，待重演：" + preview.RealStaleReason;
                    return false;
                }
                foreach (TimelinePreviewRealTransition real in preview.RealTransitions)
                {
                    if (real.Transition == null || real.Time < point.Time || real.Time >= until ||
                        !ReadsInput(real.Transition, point.InputId))
                        continue;
                    string edge = $"{preview.DescribeState(real.Source)}（第 {real.Record.Source.ActivationGeneration} 次激活）→ " +
                        $"{preview.DescribeState(real.Target)}，第 {real.Time:0.000} 秒";
                    if (pressed && RequiresInput(real.Transition.Condition, point.InputId) &&
                        SoleValue(points, point, real.Time))
                    {
                        reason = $"实际接上：{edge}；该转移须读到此键按下，{window}内只有这一点";
                        return true;
                    }
                    reason = $"实际相关：{window}内发生读取该键的转移 {edge}；条件含或／非、非开关输入或有多个输入点，无法确认这一点是否促成";
                    return false;
                }
                reason = $"真实重演{window}内没有读取该键的转移发生";
                return false;
            }
            if (preview.Chain != null &&
                FindRelated(preview.Chain, point.InputId, point.Time, until, out TimelineStaticChainMarker marker))
            {
                string edge = $"{preview.Query.DescribeState(marker.Source)} → " +
                    $"{(marker.Transition.Target == null ? "出口" : preview.Query.DescribeState(marker.Transition.Target))}，" +
                    $"第 {marker.Time.AsSingle:0.000} 秒";
                string basis = preview.Conditions.Count == 0 ? "按当前方案推导" : "按当前方案与输入轨上的预览条件推导";
                if (pressed && RequiresInput(marker.Transition.Condition, point.InputId) &&
                    SoleValue(points, point, marker.Time.AsSingle))
                {
                    reason = $"接上：{edge}；该转移须读到此键按下，{window}内只有这一点（{basis}）";
                    return true;
                }
                reason = $"相关：{window}内推导发生读取该键的转移 {edge}；条件含或／非、非开关输入或有多个输入点，无法确认这一点是否促成（{basis}）";
                return false;
            }
            TimelinePreviewRouteSegment? at = null;
            OrchestrationTime time = OrchestrationTime.FromSeconds(point.Time);
            foreach (TimelinePreviewRouteSegment segment in preview.Route)
                if (segment.Contains(time) && segment.State != null)
                    at = segment;
            if (at is not { } current)
            {
                reason = $"{window}内没有读取该键的转移发生；按下时不在预览路线上的任何状态内";
                return false;
            }
            var labels = new List<string>();
            foreach (OrchestrationTransitionWindow transition in OrchestrationStateTransitionWindows.Build(current.State).Windows)
                foreach (OrchestrationStaticRead read in transition.HostInputs)
                    if (read.Key == point.InputId)
                        labels.Add(transition.EdgeLabel);
            string state = preview.Query.DescribeState(current.State);
            string local = current.TryLocal(time, out OrchestrationTime mapped, out _) ? $"本地 {mapped.AsSingle:0.000} 秒" : current.Label;
            reason = labels.Count == 0
                ? $"{window}内没有读取该键的转移发生；按下时在 {state}（{local}），它的出边都不读 {point.InputId}"
                : $"{window}内没有读取该键的转移发生；按下时在 {state}（{local}），读该键的转移可走时段：{string.Join("；", labels)}";
            return false;
        }

        static bool ReadsInput(OrchestrationStaticTransition transition, string inputId) =>
            transition.Reads.Any(read => read.Source == OrchestrationStaticReadSource.HostInput && read.Key == inputId);

        // 条件只有读到该键为真才能成立：键本身，或“全部成立”中的一项；“任一成立”需每项都要求它；NOT 下无法确认。
        static bool RequiresInput(OrchestrationStaticCondition condition, string inputId) => condition.Kind switch
        {
            OrchestrationStaticConditionKind.Read => condition.Read.Source == OrchestrationStaticReadSource.HostInput &&
                condition.Read.Key == inputId,
            OrchestrationStaticConditionKind.All => condition.Children.Any(child => RequiresInput(child, inputId)),
            OrchestrationStaticConditionKind.Any => condition.Children.Count != 0 &&
                condition.Children.All(child => RequiresInput(child, inputId)),
            _ => false
        };

        // 从这一点到转移时刻之间没有同键的其它输入点。
        static bool SoleValue(List<TimelinePreviewInputPoint> points, TimelinePreviewInputPoint point, double time) =>
            !points.Any(other => other != point && other.InputId == point.InputId &&
                other.Time >= point.Time && other.Time <= time);

        static bool FindRelated(TimelineStaticChain chain, string inputId, double from, double until,
            out TimelineStaticChainMarker found)
        {
            foreach (TimelineStaticChainMarker marker in chain.Markers)
            {
                double time = marker.Time.AsSingle;
                if (time >= from && time < until && ReadsInput(marker.Transition, inputId))
                {
                    found = marker;
                    return true;
                }
            }
            foreach (TimelineStaticChainBranch branch in chain.Branches)
                if (FindRelated(branch.Chain, inputId, from, until, out found))
                    return true;
            found = null;
            return false;
        }

        // 预览时刻落在这次播放的某一圈内时经该段的正式映射得到本地时刻；不在这次播放内不显示，不外推。
        static bool TryLocal(TimelinePreviewRouteSegment[] spans, float time, out float local,
            out TimelinePreviewRouteSegment at, out string note)
        {
            OrchestrationTime value = OrchestrationTime.FromSeconds(time);
            foreach (TimelinePreviewRouteSegment span in spans)
                if (span.Contains(value) && span.TryLocal(value, out OrchestrationTime mapped, out note))
                {
                    local = mapped.AsSingle;
                    at = span;
                    return true;
                }
            local = 0f;
            at = default;
            note = null;
            return false;
        }

        // 预览输入组头：方案菜单、条件菜单，其后是输入错误、还没有输入或路线提前结束的提示。
        void DrawInputGroupHeader(Rect rect)
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (preview.Preferences == null)
                return;
            TimelinePreviewInputSchemeStore store = preview.InputSchemes;
            GUIStyle pullDown = EditorStyles.miniPullDown;
            var scheme = new GUIContent("方案：" + preview.Preferences.Name,
                store.Path.Length != 0 ? "输入方案存于 " + store.Path : "输入方案尚未保存：第一次修改时在入口 Timeline 旁创建");
            float height = Mathf.Min(rect.height, 16f);
            float y = rect.y + (rect.height - height) * 0.5f;
            var schemeRect = new Rect(rect.x, y, Mathf.Min(pullDown.CalcSize(scheme).x, rect.width), height);
            if (EditorGUI.DropdownButton(schemeRect, scheme, FocusType.Passive, pullDown))
            {
                m_InputHeaderAnchor = WindowRect(schemeRect);
                ShowSchemeMenu(preview, schemeRect);
            }
            var condition = new GUIContent("条件",
                "路线读到的运行数据条件：在输入轨上生成条件 Clip，或让没走成的转移可走");
            var conditionRect = new Rect(schemeRect.xMax + 4f, y, pullDown.CalcSize(condition).x, height);
            if (conditionRect.xMax > rect.xMax)
                return;
            if (EditorGUI.DropdownButton(conditionRect, condition, FocusType.Passive, pullDown))
                ShowConditionMenu(preview, conditionRect);
            string note = InputGroupNote(preview, out bool error);
            if (note.Length == 0)
                return;
            var noteRect = Rect.MinMaxRect(conditionRect.xMax + 6f, rect.y, rect.xMax, rect.yMax);
            Color previous = GUI.contentColor;
            if (error)
                GUI.contentColor = new Color(.95f, .45f, .4f);
            GUI.Label(noteRect, new GUIContent(note, note), EditorStyles.miniLabel);
            GUI.contentColor = previous;
        }

        static string InputGroupNote(TimelineAssetPreviewHost preview, out bool error)
        {
            TimelineAsset inputs = preview.Preferences.InputTimeline;
            int invalid = preview.Preferences.InputErrors.Count;
            string inputError = preview.InputError;
            error = invalid != 0 || inputError.Length != 0;
            if (!inputs)
                return "右键“添加轨道”开始摆输入";
            if (invalid != 0)
                return $"输入 {invalid} 处错误" + (inputError.Length != 0 ? "：" + FirstLine(inputError) : string.Empty);
            if (inputError.Length != 0)
                return "有错误：" + FirstLine(inputError);
            float duration = preview.Duration;
            return inputs.Data.Duration > duration + 0.001f && preview.Chain != null
                ? $"路线在 {duration:0.00} 秒结束（{preview.Chain.Reason.TrimEnd('。')}），之后的输入不起作用"
                : string.Empty;
        }

        // IMGUI 区域换算到窗口坐标，供菜单回调里弹出命名与设置窗口。
        Rect WindowRect(Rect guiRect)
        {
            Rect screen = GUIUtility.GUIToScreenRect(guiRect);
            screen.position -= position.position;
            return screen;
        }

        // 方案菜单：切换、另存、重命名、删除、从录制输入导入，范围与假设目标，编辑输入 Timeline。
        void ShowSchemeMenu(TimelineAssetPreviewHost preview, Rect anchor)
        {
            TimelinePreviewInputSchemeStore store = preview.InputSchemes;
            var menu = new GenericMenu();
            IReadOnlyList<TimelineAssetPreviewPreferences> list = store.All;
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                menu.AddItem(new GUIContent(MenuText(list[i].Name)), list[i] == preview.Preferences, () =>
                {
                    if (store.Select(index))
                        preview.PreferencesChanged();
                });
            }
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("另存为…"), false, () => AskSchemeName(preview.Preferences.Name + " 副本", name =>
            {
                store.SaveAs(name);
                preview.PreferencesChanged();
            }));
            menu.AddItem(new GUIContent("重命名…"), false, () => AskSchemeName(preview.Preferences.Name, name =>
            {
                store.Rename(name);
                preview.PreferencesChanged();
            }));
            if (list.Count > 1)
                menu.AddItem(new GUIContent("删除当前方案"), false, () =>
                {
                    if (store.DeleteCurrent())
                        preview.PreferencesChanged();
                });
            else
                menu.AddDisabledItem(new GUIContent("删除当前方案"));
            menu.AddItem(new GUIContent("从录制输入导入…"), false, () =>
            {
                string path = EditorUtility.OpenFilePanel("选择录制输入", Application.dataPath, string.Empty);
                if (path.Length != 0)
                    preview.ImportInputs(path);
            });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("范围与假设目标…"), false,
                () => UnityEditor.PopupWindow.Show(m_InputHeaderAnchor, new InputSettingsPopup(this)));
            menu.AddItem(new GUIContent(preview.Preferences.InputTimeline ? "编辑输入 Timeline" : "创建输入 Timeline"), false,
                OpenInputTimeline);
            menu.DropDown(anchor);
        }

        // 条件菜单：路线上没走成的出边所缺条件一键生成；路线读到的每个条件可在游标所在状态的播放区间生成成立或不成立的 Clip。
        static void ShowConditionMenu(TimelineAssetPreviewHost preview, Rect anchor)
        {
            var menu = new GenericMenu();
            AddConditionItems(preview, menu);
            menu.DropDown(anchor);
        }

        static void AddConditionItems(TimelineAssetPreviewHost preview, GenericMenu menu)
        {
            if (preview.IsReal)
            {
                menu.AddDisabledItem(new GUIContent("真实重演用会话的真实数据，条件只影响快速推导"));
                return;
            }
            foreach (TimelinePreviewConditionSuggestion suggestion in preview.ConditionSuggestions)
            {
                string missing = string.Join("、", suggestion.Missing.Select(read => read.Label));
                string target = suggestion.Transition.Target == null ? "出口" : preview.DescribeState(suggestion.Transition.Target);
                TimelinePreviewConditionSuggestion captured = suggestion;
                menu.AddItem(new GUIContent(MenuText($"让没走成的转移可走/{preview.DescribeState(suggestion.Source)} → {target}：缺 {missing}，" +
                    $"在 {suggestion.Start.AsSingle:0.00}–{suggestion.End.AsSingle:0.00} 秒让它成立")), false, () =>
                {
                    foreach (OrchestrationStaticRead read in captured.Missing)
                        preview.AddConditionClip(read, true, captured.Start, captured.End);
                });
            }
            if (preview.Conditions.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("路线没有读到运行数据条件"));
                return;
            }
            TimelinePreviewRouteSegment? at = null;
            OrchestrationTime cursor = OrchestrationTime.FromSeconds(preview.Time);
            foreach (TimelinePreviewRouteSegment segment in preview.Route)
                if (segment.State != null && !segment.IsNested && segment.Contains(cursor))
                    at = segment;
            TimelineAssetPreviewReadIntervals values;
            try
            {
                values = new TimelineAssetPreviewReadIntervals(preview.Preferences, preview.Inputs, preview.InputRequests);
            }
            catch (TimelineAssetPreviewInputUnavailableException)
            {
                values = null;
            }
            foreach (OrchestrationStaticRead read in preview.Conditions)
            {
                string current = string.Empty;
                if (values != null)
                {
                    bool value = values.ValueAt(in read, cursor, out bool written);
                    current = $"（游标处{(value ? "成立" : "不成立")}，{(written ? "来自输入轨" : "默认")}）";
                }
                string label = MenuText(read.Label + current);
                if (at is not { } span)
                {
                    menu.AddDisabledItem(new GUIContent($"路线读到的条件/{label}/游标不在任何状态的播放内"));
                    continue;
                }
                OrchestrationStaticRead captured = read;
                string range = $"{span.Start.AsSingle:0.00}–{span.End.AsSingle:0.00} 秒";
                menu.AddItem(new GUIContent($"路线读到的条件/{label}/在 {range} 设为成立"), false,
                    () => preview.AddConditionClip(captured, true, span.Start, span.End));
                menu.AddItem(new GUIContent($"路线读到的条件/{label}/在 {range} 设为不成立"), false,
                    () => preview.AddConditionClip(captured, false, span.Start, span.End));
            }
        }

        static string MenuText(string value) => value.Replace('/', '／');

        void AskSchemeName(string initial, Action<string> apply) =>
            UnityEditor.PopupWindow.Show(m_InputHeaderAnchor, new SchemeNamePopup(initial, apply));

        sealed class InputSettingsPopup : PopupWindowContent
        {
            readonly TimelineEditorWindow m_Owner;

            internal InputSettingsPopup(TimelineEditorWindow owner) => m_Owner = owner;

            public override Vector2 GetWindowSize() => new(380f, 200f);

            public override void OnGUI(Rect rect) => m_Owner.DrawAssetPreviewInputs();
        }

        sealed class SchemeNamePopup : PopupWindowContent
        {
            readonly Action<string> m_Apply;
            string m_Name;

            internal SchemeNamePopup(string name, Action<string> apply)
            {
                m_Name = name;
                m_Apply = apply;
            }

            public override Vector2 GetWindowSize() => new(260f, 48f);

            public override void OnGUI(Rect rect)
            {
                GUI.SetNextControlName("scheme-name");
                m_Name = EditorGUILayout.TextField("方案名", m_Name);
                EditorGUI.FocusTextInControl("scheme-name");
                bool submit = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                if ((GUILayout.Button("确定") || submit) && !string.IsNullOrWhiteSpace(m_Name))
                {
                    m_Apply(m_Name.Trim());
                    editorWindow.Close();
                }
            }
        }

        // 方案的范围、角色起点与假设目标，以及输入 Timeline 的读取错误；从方案菜单打开。
        void DrawAssetPreviewInputs()
        {
            var preview = TimelineAssetPreviewHost.Shared;
            if (!preview.IsOpen || !preview.Covers(SourceAsset))
                return;
            TimelineAssetPreviewPreferences preferences = preview.Preferences;
            m_PreviewInputsScroll = EditorGUILayout.BeginScrollView(m_PreviewInputsScroll);
            EditorGUI.BeginChangeCheck();
            preferences.RangeEnd = EditorGUILayout.FloatField("预览范围终点（秒）", preferences.RangeEnd);
            if (preferences.RangeEnd <= 0f)
                EditorGUILayout.HelpBox("预览范围终点必须大于零。", MessageType.Error);
            preferences.StartPosition = EditorGUILayout.Vector3Field("角色起点位置", preferences.StartPosition);
            preferences.StartYaw = EditorGUILayout.FloatField("角色起点朝向（度）", preferences.StartYaw);
            preferences.HasTarget = EditorGUILayout.Toggle("提供假设目标", preferences.HasTarget);
            if (preferences.HasTarget)
            {
                preferences.TargetPosition = EditorGUILayout.Vector3Field("假设目标位置", preferences.TargetPosition);
                preferences.TargetEuler = EditorGUILayout.Vector3Field("假设目标朝向", preferences.TargetEuler);
            }
            bool changed = EditorGUI.EndChangeCheck();
            foreach (string error in preferences.InputErrors)
                EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.EndScrollView();
            if (changed)
                preview.PreferencesChanged();
        }

    }
}
