using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    public sealed partial class TimelineEditorWindow
    {
        // 预览起点：包含本 Timeline 的技能各自的根状态机；预览从根按输入一路推导，经过本 Timeline 时在编辑时间轴显示。
        readonly List<(TimelineOrchestrationQuery Query, int Machine)> m_StateContexts = new();
        readonly List<TimelineOrchestrationQuery> m_TimelineQueries = new();
        IMGUIContainer m_StateContextControl;
        int m_StateContextIndex;
        string[] m_StateContextLabels = Array.Empty<string>();
        GUIContent m_StateContextIssues = GUIContent.none;
        ITimelineOrchestrationAuthoringHost m_OrchestrationHost;
        bool m_StateContextRefreshQueued;
        string m_StateQueryReason = string.Empty;

        VisualElement CreateStateContextControl()
        {
            m_StateContextControl = new IMGUIContainer(DrawStateContext);
            m_StateContextControl.style.width = 340f;
            m_StateContextControl.style.height = 20f;
            m_StateContextControl.style.alignSelf = Align.Center;
            return m_StateContextControl;
        }

        void BindStateQueries()
        {
            m_OrchestrationHost = TimelineWorkspaceModeBridge.Authoring as ITimelineOrchestrationAuthoringHost;
            if (m_OrchestrationHost != null)
                m_OrchestrationHost.OrchestrationQueriesChanged += QueueStateQueryRefresh;
            QueueStateQueryRefresh();
        }

        void DisposeStateQueryBindings()
        {
            EditorApplication.delayCall -= RefreshStateQueries;
            if (m_OrchestrationHost != null)
                m_OrchestrationHost.OrchestrationQueriesChanged -= QueueStateQueryRefresh;
            m_OrchestrationHost = null;
            m_StateContextRefreshQueued = false;
            m_StateContexts.Clear();
            m_TimelineQueries.Clear();
            m_StateContextControl = null;
        }

        void QueueStateQueryRefresh()
        {
            if (m_StateContextRefreshQueued)
                return;
            m_StateContextRefreshQueued = true;
            EditorApplication.delayCall += RefreshStateQueries;
        }

        void RefreshStateQueries()
        {
            m_StateContextRefreshQueued = false;
            m_StateContexts.Clear();
            m_TimelineQueries.Clear();
            m_StateQueryReason = string.Empty;
            if (m_Timeline == null)
                return;
            if (TimelineWorkspaceModeBridge.Authoring is ITimelineOrchestrationAuthoringHost host &&
                host.TryGetOrchestrationQueries(m_Timeline, out IReadOnlyList<TimelineOrchestrationQuery> queries,
                    out m_StateQueryReason))
            {
                m_TimelineQueries.AddRange(queries);
                foreach (TimelineOrchestrationQuery query in queries)
                    foreach (int machine in query.Query.RootMachineOperations)
                        m_StateContexts.Add((query, machine));
            }
            if (m_NestedContextRequest is { } requested)
            {
                m_StateContextIndex = m_StateContexts.FindIndex(context => context.Query.Owner == requested);
                m_NestedContextRequest = null;
            }
            if (m_StateContextIndex < 0 || m_StateContextIndex >= m_StateContexts.Count)
                m_StateContextIndex = 0;
            m_StateContextLabels = m_StateContexts.Select(context =>
                context.Query.Label + " / 技能根" + (m_StateContexts.Count(other => other.Query == context.Query) > 1
                    ? $" #{context.Machine}" : string.Empty)).ToArray();
            ApplyStateContext();
            RefreshNestedViews();
            m_StateContextControl?.MarkDirtyRepaint();
        }

        void ApplyStateContext()
        {
            m_StateContextIssues = GUIContent.none;
            if (m_StateContexts.Count == 0)
            {
                TimelineAssetPreviewHost.Shared.SetOrchestrationContext(SourceAsset, null, -1);
                return;
            }
            var context = m_StateContexts[m_StateContextIndex];
            TimelineAssetPreviewHost.Shared.SetOrchestrationContext(SourceAsset, context.Query, context.Machine);
            if (context.Query.Query.Errors.Count != 0)
                m_StateContextIssues = new GUIContent($"⚠ {context.Query.Query.Errors.Count} 个问题",
                    string.Join("\n", context.Query.Query.Errors));
        }

        void DrawStateContext()
        {
            Rect rect = m_StateContextControl.contentRect;
            if (m_StateContexts.Count == 0)
            {
                EditorGUI.LabelField(rect, new GUIContent(m_StateQueryReason.Length == 0 ? "无技能上下文" : "无技能上下文 ⓘ",
                    m_StateQueryReason.Length == 0 ? "当前 Timeline 不属于任何技能；仍可独立预览资产。" : m_StateQueryReason),
                    EditorStyles.miniLabel);
                return;
            }
            float issuesWidth = m_StateContextIssues == GUIContent.none ? 0f : 72f;
            Rect popupRect = new Rect(rect.x, rect.y + 1f, rect.width - issuesWidth, 18f);
            int selected = EditorGUI.Popup(popupRect, m_StateContextIndex, m_StateContextLabels, EditorStyles.toolbarPopup);
            if (selected != m_StateContextIndex)
            {
                m_StateContextIndex = selected;
                ApplyStateContext();
                RefreshNestedViews();
            }
            if (issuesWidth > 0f)
                EditorGUI.LabelField(new Rect(popupRect.xMax + 4f, rect.y, issuesWidth - 4f, rect.height),
                    m_StateContextIssues, EditorStyles.miniLabel);
        }
    }
}
