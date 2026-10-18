using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Diagnostics.Editor
{
    // 按宿主登记的通道显示当前观察目标的宿主诊断；历史定位时显示游标处的已记录状态。
    public sealed class RuntimeHostTraceWindow : EditorWindow
    {
        readonly List<RuntimeDebugEventView> m_Events = new();
        readonly List<RuntimeDebugEventView> m_Channel = new();
        Vector2 m_Scroll;
        string m_Filter = string.Empty;
        bool m_ActiveOnly = true;

        [MenuItem("Window/BTSMTL/宿主诊断")]
        public static void Open() => GetWindow<RuntimeHostTraceWindow>("宿主诊断");

        void OnEnable()
        {
            RuntimeDebugSession.Shared.Changed += Repaint;
            RuntimeDebugSession.Shared.HistoryCursorChanged += Repaint;
        }

        void OnDisable()
        {
            RuntimeDebugSession.Shared.Changed -= Repaint;
            RuntimeDebugSession.Shared.HistoryCursorChanged -= Repaint;
            RuntimeDebugSession.Shared.ReleaseLiveInterest(this);
        }

        void OnGUI()
        {
            RuntimeDebugSession session = RuntimeDebugSession.Shared;
            RuntimeDebugViewModel view = session.ViewModel;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(view.Attached ? view.Target.DisplayName : "未连接观察目标", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                m_ActiveOnly = GUILayout.Toggle(m_ActiveOnly, "只看最新逻辑帧的连续事件", EditorStyles.toolbarButton);
                m_Filter = EditorGUILayout.TextField(m_Filter, EditorStyles.toolbarSearchField, GUILayout.Width(200f));
            }
            if (!view.Attached)
            {
                session.ReleaseLiveInterest(this);
                EditorGUILayout.HelpBox("在预览或 RuntimeDebug 中选择角色后显示宿主诊断。", MessageType.Info);
                return;
            }
            if (session.CanControlLiveTarget)
                session.EnsureLiveInterest(this, RuntimeTraceChannel.Host);
            view.CopyCurrentEvents(RuntimeTraceChannel.Host, m_Events);
            m_Events.Sort(static (left, right) =>
            {
                int channel = ((int)left.Event.Channel).CompareTo((int)right.Event.Channel);
                if (channel != 0)
                    return channel;
                int kind = ((int)left.Event.Kind).CompareTo((int)right.Event.Kind);
                return kind != 0 ? kind : left.Event.Sequence.CompareTo(right.Event.Sequence);
            });
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            int start = 0;
            while (start < m_Events.Count)
            {
                RuntimeTraceChannel channel = m_Events[start].Event.Channel;
                int end = start;
                m_Channel.Clear();
                while (end < m_Events.Count && m_Events[end].Event.Channel == channel)
                {
                    if (Visible(m_Events[end].Event, view))
                        m_Channel.Add(m_Events[end]);
                    end++;
                }
                DrawChannel(channel, m_Channel);
                start = end;
            }
            EditorGUILayout.EndScrollView();
        }

        bool Visible(in RuntimeTraceEvent traceEvent, RuntimeDebugViewModel view)
        {
            if (m_ActiveOnly && traceEvent.Domain == RuntimeTraceDomain.Logic &&
                RequiredContinuous(traceEvent.Kind) && traceEvent.Position != view.LatestLogicTick)
                return false;
            if (m_Filter.Length == 0)
                return true;
            RuntimeTracePayload payload = traceEvent.Payload;
            return RuntimeElementDebugState.StatusText(traceEvent.Kind, in payload).Contains(m_Filter) ||
                RuntimeHostTraceDisplay.Detail(traceEvent.Kind, in payload).Contains(m_Filter);
        }

        static bool RequiredContinuous(RuntimeTraceEventKind kind) =>
            RuntimeHostTraceKinds.IsHost(kind) &&
            RuntimeHostTraceKinds.CaptureDetail(kind) == RuntimeDiagnosticsCaptureDetail.Continuous;

        static void DrawChannel(RuntimeTraceChannel channel, List<RuntimeDebugEventView> events)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"{RuntimeHostTraceChannels.Name(channel)}（{events.Count}）", EditorStyles.boldLabel);
            for (int i = 0; i < events.Count; i++)
            {
                RuntimeTraceEvent traceEvent = events[i].Event;
                RuntimeTracePayload payload = traceEvent.Payload;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{traceEvent.Domain}:{traceEvent.Position}", GUILayout.Width(110f));
                    GUILayout.Label(RuntimeHostTraceKinds.Name(traceEvent.Kind), GUILayout.Width(170f));
                    GUILayout.Label(RuntimeElementDebugState.StatusText(traceEvent.Kind, in payload), GUILayout.Width(200f));
                    GUILayout.Label(RuntimeHostTraceDisplay.Detail(traceEvent.Kind, in payload), EditorStyles.wordWrappedLabel);
                }
            }
        }
    }
}
