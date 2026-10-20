using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    public enum BtsmtlTimeBarItemKind : byte
    {
        State, Playback, Clip, TreeClip, Provider, Window, Transition, Input
    }

    public enum BtsmtlTimeBarEditKind : byte { Move, TrimStart, TrimEnd }

    public sealed class BtsmtlTimeBarRow
    {
        public BtsmtlTimeBarRow(string id, int parentIndex, BtsmtlTimeBarItemKind kind, string label)
        {
            Id = id;
            ParentIndex = parentIndex;
            Kind = kind;
            Label = label;
        }
        public string Id { get; }
        public int ParentIndex { get; }
        public BtsmtlTimeBarItemKind Kind { get; }
        public string Label { get; }
        public bool Pending { get; set; }
    }

    public sealed class BtsmtlTimeBarItem
    {
        public BtsmtlTimeBarItem(string id, int rowIndex, BtsmtlTimeBarItemKind kind, double start,
            double end, string label, string tooltip, object context = null)
        {
            Id = id;
            RowIndex = rowIndex;
            Kind = kind;
            Start = start;
            End = end;
            Label = label;
            Tooltip = tooltip;
            Context = context;
        }
        public string Id { get; }
        public int RowIndex { get; }
        public BtsmtlTimeBarItemKind Kind { get; }
        public double Start { get; set; }
        public double End { get; set; }
        public string Label { get; }
        public string Tooltip { get; set; }
        public object Context { get; }
        // 该 Clip 提供了当前选中转移所读的状态。
        public bool ProviderHighlighted { get; set; }
        public bool SourceHighlighted { get; set; }
        public bool Pending { get; set; }
    }

    public interface IBtsmtlTimeBarDataSource
    {
        IReadOnlyList<BtsmtlTimeBarRow> Rows { get; }
        IReadOnlyList<BtsmtlTimeBarItem> Items { get; }
        double StartTime { get; }
        double EndTime { get; }
        double CurrentTime { get; set; }
        long Revision { get; }
        string Status { get; }
        bool TryBeginEdit(BtsmtlTimeBarItem item, BtsmtlTimeBarEditKind kind, out string reason);
        bool TrySetRange(BtsmtlTimeBarItem item, double start, double end, out string message);
        void CompleteEdit();
        void CancelEdit();
        void Navigate(BtsmtlTimeBarItem item);
        string GetReadOnlyReason(BtsmtlTimeBarItem item);
        void AddNavigationActions(BtsmtlTimeBarItem item, GenericMenu menu);
    }

    public abstract class BtsmtlTimeBarDataSource : IBtsmtlTimeBarDataSource
    {
        public abstract IReadOnlyList<BtsmtlTimeBarRow> Rows { get; }
        public abstract IReadOnlyList<BtsmtlTimeBarItem> Items { get; }
        public abstract double StartTime { get; }
        public abstract double EndTime { get; }
        public abstract double CurrentTime { get; set; }
        public abstract long Revision { get; }
        public virtual string Status => null;
        public virtual bool TryBeginEdit(BtsmtlTimeBarItem item, BtsmtlTimeBarEditKind kind, out string reason)
        {
            reason = GetReadOnlyReason(item);
            return false;
        }
        public virtual bool TrySetRange(BtsmtlTimeBarItem item, double start, double end, out string message)
        {
            message = GetReadOnlyReason(item);
            return false;
        }
        public virtual void CompleteEdit() { }
        public virtual void CancelEdit() { }
        public abstract void Navigate(BtsmtlTimeBarItem item);
        public virtual void AddNavigationActions(BtsmtlTimeBarItem item, GenericMenu menu) { }
        public virtual string GetReadOnlyReason(BtsmtlTimeBarItem item) =>
            item.Kind == BtsmtlTimeBarItemKind.Transition
                ? "该时刻由转移条件决定；双击可定位作者来源。" : "此时间条只读；双击可定位作者来源。";
    }

    public sealed class BtsmtlTimeBarView : IDisposable
    {
        const float LabelWidth = 230;
        const float RowHeight = 25;
        const float HeaderHeight = 24;
        readonly HashSet<string> m_Collapsed = new();
        readonly List<int> m_VisibleRows = new();
        readonly Dictionary<int, List<BtsmtlTimeBarItem>> m_RowItems = new();
        long m_ItemRevision = long.MinValue;
        Vector2 m_Scroll;
        IBtsmtlTimeBarDataSource m_Source;
        BtsmtlTimeBarItem m_Dragged;
        BtsmtlTimeBarEditKind m_DragKind;
        double m_DragStart;
        double m_OriginalStart;
        double m_OriginalEnd;
        double m_ViewStart;
        double m_ViewEnd;
        bool m_FollowEnd = true;
        int m_Control;
        string m_Message;
        public double ViewStart => m_ViewStart;
        public double ViewEnd => m_ViewEnd;
        public bool IsEditing => m_Dragged != null;
        public bool UserNavigated { get; private set; }

        public void SetSource(IBtsmtlTimeBarDataSource source)
        {
            if (ReferenceEquals(source, m_Source))
                return;
            CancelEdit();
            m_Source = source;
            m_ItemRevision = long.MinValue;
            m_ViewStart = source?.StartTime ?? 0;
            m_ViewEnd = source?.EndTime ?? 1;
            m_Scroll = Vector2.zero;
        }

        public void Draw(Rect rect)
        {
            UserNavigated = false;
            if (m_Source == null)
                return;
            if (m_ViewEnd <= m_ViewStart)
                FrameAll();
            if (m_FollowEnd)
                m_ViewEnd = Math.Max(m_ViewStart + .02, m_Source.EndTime);
            Event current = Event.current;
            if (m_ItemRevision != m_Source.Revision)
            {
                m_RowItems.Clear();
                for (int i = 0; i < m_Source.Items.Count; i++)
                {
                    BtsmtlTimeBarItem item = m_Source.Items[i];
                    if (!m_RowItems.TryGetValue(item.RowIndex, out List<BtsmtlTimeBarItem> items))
                        m_RowItems.Add(item.RowIndex, items = new List<BtsmtlTimeBarItem>());
                    items.Add(item);
                }
                m_ItemRevision = m_Source.Revision;
            }
            if (m_Dragged != null && current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                CancelEdit();
                current.Use();
            }
            Rect toolbar = new(rect.x, rect.y, rect.width, HeaderHeight);
            GUI.Box(toolbar, GUIContent.none, EditorStyles.toolbar);
            if (GUI.Button(new Rect(toolbar.x + 3, toolbar.y, 48, HeaderHeight), "全览", EditorStyles.toolbarButton))
                FrameAll();
            string status = m_Source.Status ?? m_Message ?? "拖动 Clip 调整作者时间；双击定位来源；Esc 取消";
            GUI.Label(new Rect(toolbar.x + 58, toolbar.y, toolbar.width - 64, HeaderHeight),
                new GUIContent(status, status), EditorStyles.miniLabel);
            Rect area = new(rect.x, rect.y + HeaderHeight, rect.width, rect.height - HeaderHeight);
            BuildVisibleRows();
            Rect content = new(0, 0, Math.Max(area.width - 16, LabelWidth + 20),
                HeaderHeight + m_VisibleRows.Count * RowHeight);
            m_Scroll = GUI.BeginScrollView(area, m_Scroll, content);
            Rect axis = new(LabelWidth, 0, content.width - LabelWidth, HeaderHeight);
            DrawRuler(axis);
            int firstLine = Math.Max(0, (int)((m_Scroll.y - HeaderHeight) / RowHeight));
            int lastLine = Math.Min(m_VisibleRows.Count, (int)Math.Ceiling((m_Scroll.y + area.height) / RowHeight));
            for (int line = firstLine; line < lastLine; line++)
            {
                int rowIndex = m_VisibleRows[line];
                BtsmtlTimeBarRow row = m_Source.Rows[rowIndex];
                Rect rowRect = new(0, HeaderHeight + line * RowHeight, content.width, RowHeight);
                DrawRow(rowRect, row, rowIndex);
            }
            float cursorX = TimeToX(axis, m_Source.CurrentTime, m_ViewStart, m_ViewEnd);
            EditorGUI.DrawRect(new Rect(cursorX, 0, 1, content.height), new Color(1f, .7f, .2f));
            if (current.type == EventType.ScrollWheel && axis.Contains(current.mousePosition))
            {
                double at = XToTime(axis, current.mousePosition.x, m_ViewStart, m_ViewEnd);
                double scale = Math.Pow(1.15, current.delta.y);
                m_ViewStart = at + (m_ViewStart - at) * scale;
                m_ViewEnd = at + (m_ViewEnd - at) * scale;
                m_FollowEnd = false;
                UserNavigated = true;
                current.Use();
            }
            if (current.type == EventType.MouseDown && current.button == 0 && axis.Contains(current.mousePosition))
            {
                m_Source.CurrentTime = XToTime(axis, current.mousePosition.x, m_ViewStart, m_ViewEnd);
                current.Use();
            }
            HandleDrag(axis);
            GUI.EndScrollView();
        }

        void BuildVisibleRows()
        {
            m_VisibleRows.Clear();
            for (int i = 0; i < m_Source.Rows.Count; i++)
            {
                bool hidden = false;
                for (int parent = m_Source.Rows[i].ParentIndex; parent >= 0; parent = m_Source.Rows[parent].ParentIndex)
                    hidden |= m_Collapsed.Contains(m_Source.Rows[parent].Id);
                if (!hidden)
                    m_VisibleRows.Add(i);
            }
        }

        void DrawRow(Rect rect, BtsmtlTimeBarRow row, int rowIndex)
        {
            EditorGUI.DrawRect(rect, rowIndex % 2 == 0 ? new Color(.17f, .17f, .17f) : new Color(.2f, .2f, .2f));
            int depth = 0;
            for (int parent = row.ParentIndex; parent >= 0; parent = m_Source.Rows[parent].ParentIndex)
                depth++;
            bool children = rowIndex + 1 < m_Source.Rows.Count && m_Source.Rows[rowIndex + 1].ParentIndex == rowIndex;
            Rect label = new(rect.x + depth * 12, rect.y + 2, LabelWidth - depth * 12 - 4, RowHeight - 4);
            if (children)
            {
                bool expanded = !m_Collapsed.Contains(row.Id);
                bool next = EditorGUI.Foldout(label, expanded, row.Label + (row.Pending ? " · 待更新" : ""), true);
                if (next != expanded)
                {
                    if (next) m_Collapsed.Remove(row.Id);
                    else m_Collapsed.Add(row.Id);
                }
            }
            else
                GUI.Label(label, row.Label + (row.Pending ? " · 待更新" : ""), EditorStyles.miniLabel);
            Rect axis = new(LabelWidth, rect.y + 3, rect.width - LabelWidth, RowHeight - 6);
            if (!m_RowItems.TryGetValue(rowIndex, out List<BtsmtlTimeBarItem> rowItems))
                return;
            for (int i = 0; i < rowItems.Count; i++)
            {
                BtsmtlTimeBarItem item = rowItems[i];
                if (item.End < m_ViewStart || item.Start > m_ViewEnd)
                    continue;
                Rect itemRect = ItemRect(axis, item, m_ViewStart, m_ViewEnd);
                float right = Math.Min(axis.xMax, itemRect.xMax);
                itemRect.x = Math.Max(axis.x, itemRect.x);
                itemRect.width = Math.Max(1, right - itemRect.x);
                DrawItem(itemRect, item);
                HandleItem(axis, itemRect, item);
            }
        }

        void HandleItem(Rect axis, Rect rect, BtsmtlTimeBarItem item)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown || !rect.Contains(current.mousePosition))
                return;
            if (current.button == 1)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("定位作者来源"), false, () => m_Source.Navigate(item));
                m_Source.AddNavigationActions(item, menu);
                string reason = m_Source.GetReadOnlyReason(item);
                if (!string.IsNullOrEmpty(reason))
                    menu.AddDisabledItem(new GUIContent(reason));
                menu.ShowAsContext();
                current.Use();
                return;
            }
            if (current.button != 0)
                return;
            if (current.clickCount == 2)
            {
                m_Source.Navigate(item);
                current.Use();
                return;
            }
            BtsmtlTimeBarEditKind kind = item.Kind == BtsmtlTimeBarItemKind.Input || rect.width <= 12
                ? BtsmtlTimeBarEditKind.Move : current.mousePosition.x - rect.x < 5
                ? BtsmtlTimeBarEditKind.TrimStart : rect.xMax - current.mousePosition.x < 5
                    ? BtsmtlTimeBarEditKind.TrimEnd : BtsmtlTimeBarEditKind.Move;
            if (m_Source.TryBeginEdit(item, kind, out m_Message))
            {
                m_Dragged = item;
                m_DragKind = kind;
                m_DragStart = XToTime(axis, current.mousePosition.x, m_ViewStart, m_ViewEnd);
                m_OriginalStart = item.Start;
                m_OriginalEnd = item.End;
                m_Control = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = m_Control;
                GUIUtility.keyboardControl = m_Control;
            }
            current.Use();
        }

        void HandleDrag(Rect axis)
        {
            if (m_Dragged == null)
                return;
            Event current = Event.current;
            if (current.type == EventType.MouseDrag)
            {
                double delta = XToTime(axis, current.mousePosition.x, m_ViewStart, m_ViewEnd) - m_DragStart;
                double start = m_DragKind == BtsmtlTimeBarEditKind.TrimEnd ? m_OriginalStart : m_OriginalStart + delta;
                double end = m_DragKind == BtsmtlTimeBarEditKind.TrimStart ? m_OriginalEnd : m_OriginalEnd + delta;
                if (!m_Source.TrySetRange(m_Dragged, start, end, out m_Message))
                    CancelEdit();
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0)
            {
                m_Source.CompleteEdit();
                m_Dragged = null;
                GUIUtility.hotControl = 0;
                current.Use();
            }
        }

        public void CancelEdit()
        {
            if (m_Dragged == null)
                return;
            m_Source.CancelEdit();
            m_Dragged = null;
            GUIUtility.hotControl = 0;
        }

        public void FrameAll()
        {
            m_ViewStart = m_Source.StartTime;
            m_ViewEnd = Math.Max(m_ViewStart + .02, m_Source.EndTime);
            m_FollowEnd = true;
        }

        void DrawRuler(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(.12f, .12f, .12f));
            double raw = (m_ViewEnd - m_ViewStart) / Math.Max(1, rect.width / 80);
            double step = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            step *= raw / step >= 5 ? 5 : raw / step >= 2 ? 2 : 1;
            for (double value = Math.Ceiling(m_ViewStart / step) * step; value <= m_ViewEnd; value += step)
            {
                float x = TimeToX(rect, value, m_ViewStart, m_ViewEnd);
                GUI.Label(new Rect(x + 3, rect.y, 75, rect.height), value.ToString("0.###") + "s", EditorStyles.miniLabel);
                EditorGUI.DrawRect(new Rect(x, rect.yMax - 5, 1, 5), Color.gray);
            }
        }

        static void DrawItem(Rect rect, BtsmtlTimeBarItem item)
        {
            Color color = item.Kind switch
            {
                BtsmtlTimeBarItemKind.State => new Color(.3f, .45f, .67f),
                BtsmtlTimeBarItemKind.Provider => new Color(.72f, .52f, .22f),
                BtsmtlTimeBarItemKind.Window => new Color(.3f, .58f, .45f),
                BtsmtlTimeBarItemKind.Transition => new Color(.95f, .6f, .16f),
                _ => new Color(.3f, .6f, .72f)
            };
            if (item.Pending) color.a = .45f;
            if (item.ProviderHighlighted || item.SourceHighlighted)
                EditorGUI.DrawRect(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2),
                    item.ProviderHighlighted ? new Color(1f, .75f, .15f) : Color.cyan);
            EditorGUI.DrawRect(rect, color);
            GUI.Label(rect, new GUIContent(item.Label + (item.Pending ? " · 待更新" : ""), item.Tooltip), EditorStyles.miniLabel);
        }

        static Rect ItemRect(Rect rect, BtsmtlTimeBarItem item, double start, double end)
        {
            float left = TimeToX(rect, item.Start, start, end);
            float right = TimeToX(rect, item.End, start, end);
            bool marker = item.Kind == BtsmtlTimeBarItemKind.Transition;
            return new Rect(marker ? left - 3 : left, rect.y, marker ? 7 : Math.Max(3, right - left), rect.height);
        }

        public static Rect GetItemRect(Rect rect, IBtsmtlTimeBarDataSource source, BtsmtlTimeBarItem item)
        {
            float height = rect.height / Math.Max(1, source.Rows.Count);
            Rect lane = new(rect.x, rect.y + item.RowIndex * height + 1, rect.width, height - 2);
            return ItemRect(lane, item, source.StartTime, Math.Max(source.StartTime + .0001, source.EndTime));
        }

        static IBtsmtlTimeBarDataSource s_MiniSource;
        static BtsmtlTimeBarItem s_MiniItem;
        static BtsmtlTimeBarEditKind s_MiniKind;
        static float s_MiniPressX;
        static double s_MiniStart;
        static double s_MiniEnd;
        static int s_MiniControl;
        static string s_MiniMessage;

        // 迷你时间条：单击返回被按下的条目；拖两端或中间按数据源规则调整，双击定位来源，Esc 取消。
        public static BtsmtlTimeBarItem DrawMini(Rect rect, IBtsmtlTimeBarDataSource source, out string message)
        {
            message = s_MiniSource == source ? s_MiniMessage : null;
            EditorGUI.DrawRect(rect, new Color(.13f, .13f, .13f));
            Event current = Event.current;
            BtsmtlTimeBarItem pressed = null;
            if (current.type == EventType.MouseDown && s_MiniItem == null && s_MiniSource == source)
                message = s_MiniMessage = null;
            double seconds = Math.Max(.0001, source.EndTime - source.StartTime) / Math.Max(1, rect.width);
            for (int i = 0; i < source.Items.Count; i++)
            {
                BtsmtlTimeBarItem item = source.Items[i];
                Rect itemRect = GetItemRect(rect, source, item);
                DrawItem(itemRect, item);
                if (source.GetReadOnlyReason(item) == null)
                {
                    EditorGUIUtility.AddCursorRect(itemRect, MouseCursor.Pan);
                    EditorGUIUtility.AddCursorRect(new Rect(itemRect.x - 3, itemRect.y, 6, itemRect.height), MouseCursor.ResizeHorizontal);
                    EditorGUIUtility.AddCursorRect(new Rect(itemRect.xMax - 3, itemRect.y, 6, itemRect.height), MouseCursor.ResizeHorizontal);
                }
                if (current.type != EventType.MouseDown || current.button != 0 ||
                    !Rect.MinMaxRect(itemRect.xMin - 3, itemRect.yMin, itemRect.xMax + 3, itemRect.yMax).Contains(current.mousePosition))
                    continue;
                pressed = item;
                if (current.clickCount == 2)
                    source.Navigate(item);
                else
                {
                    BtsmtlTimeBarEditKind kind = current.mousePosition.x - itemRect.x < 4 ? BtsmtlTimeBarEditKind.TrimStart
                        : itemRect.xMax - current.mousePosition.x < 4 ? BtsmtlTimeBarEditKind.TrimEnd
                        : BtsmtlTimeBarEditKind.Move;
                    if (source.TryBeginEdit(item, kind, out s_MiniMessage))
                    {
                        s_MiniSource = source;
                        s_MiniItem = item;
                        s_MiniKind = kind;
                        s_MiniPressX = current.mousePosition.x;
                        s_MiniStart = item.Start;
                        s_MiniEnd = item.End;
                        s_MiniControl = GUIUtility.GetControlID(FocusType.Passive);
                        GUIUtility.hotControl = s_MiniControl;
                        GUIUtility.keyboardControl = s_MiniControl;
                    }
                    else
                        s_MiniSource = source;
                    message = s_MiniMessage;
                }
                current.Use();
                break;
            }
            double time = source.CurrentTime;
            if (time >= source.StartTime && time <= source.EndTime)
                EditorGUI.DrawRect(new Rect(rect.x + (float)((time - source.StartTime) / seconds), rect.y, 1, rect.height),
                    new Color(1f, .7f, .2f));
            if (s_MiniSource != source || s_MiniItem == null)
                return pressed;
            if (current.type == EventType.MouseDrag)
            {
                double delta = (current.mousePosition.x - s_MiniPressX) * seconds;
                double start = s_MiniKind == BtsmtlTimeBarEditKind.TrimEnd ? s_MiniStart : s_MiniStart + delta;
                double end = s_MiniKind == BtsmtlTimeBarEditKind.TrimStart ? s_MiniEnd : s_MiniEnd + delta;
                if (!source.TrySetRange(s_MiniItem, start, end, out s_MiniMessage))
                    CancelMini();
                message = s_MiniMessage;
                current.Use();
            }
            else if (current.rawType == EventType.MouseUp && current.button == 0)
            {
                s_MiniItem = null;
                s_MiniMessage = null;
                GUIUtility.hotControl = 0;
                source.CompleteEdit();
                current.Use();
            }
            else if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                CancelMini();
                current.Use();
            }
            return pressed;
        }

        static void CancelMini()
        {
            if (s_MiniItem == null)
                return;
            s_MiniSource.CancelEdit();
            s_MiniItem = null;
            GUIUtility.hotControl = 0;
        }

        static float TimeToX(Rect rect, double value, double start, double end) =>
            rect.x + (float)((value - start) / (end - start)) * rect.width;
        static double XToTime(Rect rect, float value, double start, double end) =>
            start + (value - rect.x) / rect.width * (end - start);
        public void Dispose() => CancelEdit();
    }
}
