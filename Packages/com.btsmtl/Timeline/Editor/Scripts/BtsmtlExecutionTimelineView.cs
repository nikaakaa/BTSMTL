using System;
using BTSMTL.Diagnostics;
using BTSMTL.Diagnostics.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    public sealed class BtsmtlExecutionTimelineView : VisualElement, IDisposable
    {
        public const RuntimeTraceChannel TraceChannels = RuntimeTraceChannel.Graph |
            RuntimeTraceChannel.StateMachine | RuntimeTraceChannel.Timeline;
        readonly RuntimeDebugSession m_Session = RuntimeDebugSession.Shared;
        readonly IMGUIContainer m_Surface;
        readonly BtsmtlTimeBarView m_View = new();
        readonly BtsmtlExecutionTimeBarDataSource m_Source;
        readonly Func<bool> m_CanSeek;
        bool m_Visible;
        long m_Version = -1;
        long m_ProjectionVersion = -1;
        long m_TargetRevision = -1;
        Guid m_Capture;
        RuntimeDebugAttachmentState m_State;
        public bool IsAuthoringResultPending => m_View.IsEditing || m_Source.IsAwaitingResult;
        public bool HasRetainedAuthoringResult => IsAuthoringResultPending || m_Source.EditError != null;

        public BtsmtlExecutionTimelineView(Action<RuntimeExecutionSpan> openSource,
            Action<float> seekPreview, Func<bool> canSeekPreview, Func<float> previewTime)
        {
            m_CanSeek = canSeekPreview;
            m_Source = new BtsmtlExecutionTimeBarDataSource(m_Session, openSource,
                seekPreview, previewTime, canSeekPreview);
            m_View.SetSource(m_Source);
            name = "workbench-execution-timeline";
            style.flexGrow = 1;
            style.minHeight = 140;
            m_Surface = new IMGUIContainer(Draw) { name = "execution-time-bar" };
            m_Surface.style.flexGrow = 1;
            Add(m_Surface);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                m_Session.Changed += RefreshProjection;
                m_Session.HistoryCursorChanged += RefreshCursor;
                EditorApplication.update += RefreshCursor;
                RefreshProjection();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                m_View.CancelEdit();
                Unsubscribe();
            });
        }

        public void ConfigureAuthoring(IBtsmtlExecutionTimelineAuthoringHost authoring)
        {
            m_Source.ConfigureAuthoring(authoring);
            m_Version = -1;
            RefreshProjection();
        }

        public void CancelAuthoringInteraction()
        {
            m_View.CancelEdit();
            m_Source.CancelEdit();
        }

        public void SetVisible(bool visible)
        {
            m_Visible = visible;
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) RefreshProjection();
            else
            {
                m_View.CancelEdit();
                m_Source.Clear();
                m_Version = -1;
            }
        }

        void RefreshCursor()
        {
            if (!m_Visible)
                return;
            if (m_Source.RestoreFailedResult())
                m_Surface.MarkDirtyRepaint();
            if (!m_CanSeek())
                return;
            if (m_Source.IsAwaitingResult)
                RefreshProjection();
            m_Surface.MarkDirtyRepaint();
        }

        void RefreshProjection()
        {
            if (!m_Visible || panel == null || !m_CanSeek())
                return;
            if (!m_Source.IsAwaitingResult && m_Version == m_Session.CaptureVersion &&
                m_ProjectionVersion == m_Session.ProjectionVersion &&
                m_TargetRevision == m_Session.TargetRevision && m_Capture == m_Session.CaptureId &&
                m_State == m_Session.AttachmentState)
                return;
            RuntimeExecutionTimeline timeline = m_Session.BuildExecutionTimeline();
            if (timeline == null)
                return;
            bool captureChanged = m_Capture != timeline.CaptureId;
            if (!m_Source.Apply(timeline))
                return;
            m_Version = m_Session.CaptureVersion;
            m_ProjectionVersion = m_Session.ProjectionVersion;
            m_TargetRevision = m_Session.TargetRevision;
            m_Capture = m_Session.CaptureId;
            m_State = m_Session.AttachmentState;
            if (captureChanged || m_View.ViewEnd <= .02)
                m_View.FrameAll();
            m_Surface.MarkDirtyRepaint();
        }

        void Draw()
        {
            m_View.Draw(new Rect(0, 0, Mathf.Max(1, m_Surface.contentRect.width),
                Mathf.Max(1, m_Surface.contentRect.height)));
            if (m_CanSeek() && !m_View.IsEditing && !m_Source.IsAwaitingResult)
            {
                double origin = m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Logic);
                m_Session.SetExecutionWindow(RuntimeTraceDomain.Logic, origin + m_View.ViewStart,
                    origin + m_View.ViewEnd, .1, m_View.UserNavigated);
            }
        }

        void Unsubscribe()
        {
            m_Session.Changed -= RefreshProjection;
            m_Session.HistoryCursorChanged -= RefreshCursor;
            EditorApplication.update -= RefreshCursor;
        }

        public void Dispose()
        {
            Unsubscribe();
            m_View.Dispose();
            m_Source.Clear();
        }
    }
}
