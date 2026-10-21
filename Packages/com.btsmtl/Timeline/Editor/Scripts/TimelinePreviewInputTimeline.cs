using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Diagnostics;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using FlowCanvas;
using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    // 写预览输入：所在 Clip 起点写入该值、终点写回该类型的零值；所在 Marker 写入并保持到同一输入的下一次写入。
    [Name("写预览输入"), Category("BTSMTL/预览输入")]
    public sealed class TimelinePreviewInputWriteNode : BtsmtlFlowNode
    {
        [SerializeField] string m_InputId = string.Empty;
        [SerializeField] bool m_Boolean = true;
        [SerializeField] int m_Integer;
        [SerializeField] float m_Scalar;
        [SerializeField] Vector2 m_Vector2;
        [SerializeField] Vector3 m_Vector3;
        [SerializeField] Quaternion m_Rotation = Quaternion.identity;
        [SerializeField] string m_Text = string.Empty;

        public string InputId => m_InputId ?? string.Empty;

        protected override void RegisterPorts() { }

        public TimelinePreviewInputPoint At(float time) => new()
        {
            InputId = InputId,
            Time = time,
            Boolean = m_Boolean,
            Integer = m_Integer,
            Scalar = m_Scalar,
            Vector2 = m_Vector2,
            Vector3 = m_Vector3,
            Rotation = m_Rotation,
            Text = m_Text ?? string.Empty
        };

        public void Configure(TimelinePreviewInputPoint value)
        {
            m_InputId = value.InputId;
            m_Boolean = value.Boolean;
            m_Integer = value.Integer;
            m_Scalar = value.Scalar;
            m_Vector2 = value.Vector2;
            m_Vector3 = value.Vector3;
            m_Rotation = value.Rotation;
            m_Text = value.Text ?? string.Empty;
        }

        public bool SameValue(TimelinePreviewInputPoint value) =>
            InputId == value.InputId && m_Boolean == value.Boolean && m_Integer == value.Integer &&
            m_Scalar.Equals(value.Scalar) && m_Vector2 == value.Vector2 && m_Vector3 == value.Vector3 &&
            m_Rotation == value.Rotation && m_Text == (value.Text ?? string.Empty);

        internal void DrawInspector(IReadOnlyList<GraphHostInputDescriptor> inputs)
        {
            string[] choices = inputs.Select(input => input.InputId).ToArray();
            int index = Array.IndexOf(choices, m_InputId);
            if (choices.Length == 0)
            {
                EditorGUILayout.HelpBox("预览宿主没有声明输入；先在 Timeline 窗口选择预览配置。", MessageType.Warning);
                m_InputId = EditorGUILayout.TextField("输入身份", m_InputId);
                return;
            }
            int selected = EditorGUILayout.Popup("输入身份", index, choices);
            if (selected >= 0)
                m_InputId = choices[selected];
            if (index < 0 && !string.IsNullOrEmpty(m_InputId))
                EditorGUILayout.HelpBox($"宿主已不声明输入 '{m_InputId}'。", MessageType.Error);
            GraphHostInputDescriptor descriptor = selected >= 0 ? inputs[selected] : null;
            switch (descriptor?.ValueKind)
            {
                case GraphHostValueKind.Bool: m_Boolean = EditorGUILayout.Toggle("按下", m_Boolean); break;
                case GraphHostValueKind.Int32: m_Integer = EditorGUILayout.IntField("值", m_Integer); break;
                case GraphHostValueKind.Float32: m_Scalar = EditorGUILayout.FloatField("值", m_Scalar); break;
                case GraphHostValueKind.Vector2: m_Vector2 = EditorGUILayout.Vector2Field("值", m_Vector2); break;
                case GraphHostValueKind.Vector3: m_Vector3 = EditorGUILayout.Vector3Field("值", m_Vector3); break;
                case GraphHostValueKind.Quaternion:
                    m_Rotation = Quaternion.Euler(EditorGUILayout.Vector3Field("值", m_Rotation.eulerAngles));
                    break;
                case GraphHostValueKind.Enum:
                    m_Integer = Convert.ToInt32(EditorGUILayout.EnumPopup("值",
                        (Enum)Enum.ToObject(descriptor.ValueType, m_Integer)));
                    break;
                case GraphHostValueKind.Text: m_Text = EditorGUILayout.TextField("值", m_Text); break;
            }
        }
    }

    // 预览输入图：只写预览输入，供输入 Timeline 的 Clip 与 Marker 复用；不进入技能内容闭包与运行发布。
    [CreateAssetMenu(fileName = "PreviewInput", menuName = "BTSMTL/Timeline/Preview Input Graph")]
    public sealed class TimelinePreviewInputGraph : BtsmtlFlowGraph, ITimelineTreeGraphAsset, IBtsmtlGraphNodeInspector
    {
        [SerializeField, HideInInspector] string m_AuthoringId = Guid.NewGuid().ToString("N");

        public override string AuthoringId => m_AuthoringId;
        public bool IsTimelineTree => true;
        public bool IsTimelineTrigger => true;
        public IEnumerable<TimelinePreviewInputWriteNode> Writes => allNodes.OfType<TimelinePreviewInputWriteNode>();

        public override bool CanAuthorNodeType(Type nodeType) =>
            nodeType == typeof(TimelinePreviewInputWriteNode) || nodeType == typeof(BtsmtlEntryFlowNode);

        public void BindTimelineAuthoringContext(TimelineData timeline, Clip clip) { }

        public bool TryGetDeclaredEndTime(out OrchestrationTime endTime)
        {
            endTime = default;
            return false;
        }

        public void ValidateTimelineBinding(TimelineData timeline, Clip clip, List<string> errors, bool requireComplete)
        {
            foreach (Node node in allNodes)
                if (!CanAuthorNodeType(node.GetType()))
                    errors?.Add($"预览输入图 '{name}' 含有不能静态读取的节点 '{node.name}'。");
        }

        public Func<ScriptableObject, string> CreateTimelineContentFingerprint() =>
            graph => EditorUtility.GetDirtyCount(graph).ToString();

        public void CollectTimelineContentClosure(TimelineContentClosureBuilder builder, string sourcePath,
            TimelineExecutionDomain domain, Func<ScriptableObject, string> fingerprint) =>
            builder.AddError("timeline_preview_input_graph", sourcePath, "预览输入图只能用于预览输入 Timeline，不能进入技能内容。");

        public void DrawNodeInspector(BtsmtlFlowNode node)
        {
            if (node is not TimelinePreviewInputWriteNode write)
                return;
            IReadOnlyList<GraphHostInputDescriptor> inputs = TimelineAssetPreviewHost.Shared.IsRegistered
                ? TimelineAssetPreviewHost.Shared.Inputs : Array.Empty<GraphHostInputDescriptor>();
            UndoUtility.RecordObject(this, "修改预览输入");
            EditorGUI.BeginChangeCheck();
            write.DrawInspector(inputs);
            if (EditorGUI.EndChangeCheck())
            {
                SelfSerialize();
                UndoUtility.SetDirty(this);
            }
        }
    }

    // 输入 Timeline 的作者宿主：沿用原 Track／Clip／Marker 创建与编辑流程，只把节点图换成预览输入图；
    // 输入 Timeline 不属于技能内容，提交改动不做技能资产回收。时间网格与源资源编辑交给原宿主。
    public sealed class TimelinePreviewInputAuthoringHost : ITimelineEditorAuthoringHost
    {
        readonly ITimelineEditorAuthoringHost m_Base;

        TimelinePreviewInputAuthoringHost(ITimelineEditorAuthoringHost baseHost) =>
            m_Base = baseHost ?? throw new ArgumentNullException(nameof(baseHost));

        // 输入 Timeline 是输入方案资产的子资产；按所在资产判断，不按名称。
        public static bool Owns(TimelineData timeline) =>
            timeline?.SerializedOwner is TimelineAsset asset &&
            AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(asset)) is TimelinePreviewInputSchemes;

        public static ITimelineEditorAuthoringHost For(TimelineData timeline, ITimelineEditorAuthoringHost baseHost) =>
            Owns(timeline) ? new TimelinePreviewInputAuthoringHost(baseHost) : baseHost;

        public TimelineContractCatalog Contracts => m_Base.Contracts;
        public Type TreeGraphType => typeof(TimelinePreviewInputGraph);
        public bool AllowsPresentationTreeClips => true;
        public Type TimeGridSourceType => m_Base.TimeGridSourceType;
        public int GetTimeGridTickRate(ScriptableObject source) => m_Base.GetTimeGridTickRate(source);

        public ScriptableObject CreateGraph(UnityEngine.Object container, string graphName, bool isTrigger)
        {
            string path = AssetDatabase.GetAssetPath(container);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("输入 Timeline 不在资产中，无法创建预览输入图。");
            var graph = ScriptableObject.CreateInstance<TimelinePreviewInputGraph>();
            graph.name = string.IsNullOrWhiteSpace(graphName) || graphName == "Timeline Tree" ? "预览输入" : graphName.Trim();
            graph.AddNode(typeof(TimelinePreviewInputWriteNode), Vector2.zero);
            graph.SelfSerialize();
            AssetDatabase.AddObjectToAsset(graph, path);
            Undo.RegisterCreatedObjectUndo(graph, "创建预览输入图");
            EditorUtility.SetDirty(container);
            AssetDatabase.SaveAssetIfDirty(container);
            return graph;
        }

        public void OpenGraph(ScriptableObject source)
        {
            if (source is not TimelinePreviewInputGraph)
                throw new InvalidOperationException("输入 Timeline 的 Clip 与 Marker 只能引用预览输入图。");
            AssetDatabase.OpenAsset(source);
        }

        public void MutateOwnedContent(TimelineData timeline, Action mutation) => mutation();

        public void ShowSourceCreation(ScriptableObject pipeline, Type sourceType, UnityEngine.Object source,
            Func<UnityEngine.Object, string> attach) => m_Base.ShowSourceCreation(pipeline, sourceType, source, attach);

        public void DrawSourceInspector(ScriptableObject pipeline, UnityEngine.Object source, float sourceTime) =>
            m_Base.DrawSourceInspector(pipeline, source, sourceTime);

        public void ReleaseSourceEditing() => m_Base.ReleaseSourceEditing();
    }

    // 输入 Timeline 的唯一读法与写法：Clip 起点写值、终点写回零值，Marker 写值并保持；静音 Track 不参与。
    public static class TimelinePreviewInputTimeline
    {
        readonly struct Write
        {
            public Write(TimelinePreviewInputPoint value, OrchestrationTime start, OrchestrationTime? end, string element)
            {
                Value = value;
                Start = start;
                End = end;
                Element = element;
            }

            public TimelinePreviewInputPoint Value { get; }
            public OrchestrationTime Start { get; }
            public OrchestrationTime? End { get; }
            public string Element { get; }
        }

        // Clip 或 Marker 引用的预览输入图的名称与写入的输入身份；不是预览输入图时返回 false。
        public static bool TryDescribe(ScriptableObject graph, out string name, out string[] inputIds)
        {
            if (graph is TimelinePreviewInputGraph input)
            {
                name = input.name;
                inputIds = input.Writes.Select(write => write.InputId).ToArray();
                return true;
            }
            name = null;
            inputIds = Array.Empty<string>();
            return false;
        }

        // 读出按时刻排序的值变化；有任何错误时返回空列表，错误逐条写明元素。
        public static List<TimelinePreviewInputPoint> Read(TimelineAsset timeline,
            IReadOnlyList<GraphHostInputDescriptor> declared, List<string> errors)
        {
            var points = new List<TimelinePreviewInputPoint>();
            if (!timeline)
                return points;
            var writes = new List<Write>();
            foreach (Track track in timeline.Data.Tracks)
            {
                if (track.PersistentMuted)
                    continue;
                foreach (Clip clip in track.Clips)
                {
                    string element = $"{track.Name} / {clip.StartTime.AsSingle:0.###}–{clip.EndTime.AsSingle:0.###} 秒的 Clip";
                    if (clip is not TreeClip tree || tree.AssetTree is not TimelinePreviewInputGraph graph)
                    {
                        errors.Add($"{element} 必须是引用预览输入图的 Tree Clip。");
                        continue;
                    }
                    AddWrites(graph, element, clip.StartTime, clip.EndTime, declared, writes, errors);
                }
                foreach (TimelineMarker marker in track.Markers)
                {
                    string element = $"{track.Name} / {marker.Time.AsSingle:0.###} 秒的 Marker";
                    if (marker.Graph is not TimelinePreviewInputGraph graph)
                    {
                        errors.Add($"{element} 必须引用预览输入图。");
                        continue;
                    }
                    AddWrites(graph, element, marker.Time, null, declared, writes, errors);
                }
            }
            foreach (IGrouping<string, Write> input in writes.GroupBy(write => write.Value.InputId))
            {
                Write[] ordered = input.OrderBy(write => write.Start).ToArray();
                for (int i = 0; i < ordered.Length; i++)
                    for (int j = i + 1; j < ordered.Length; j++)
                        if (Overlaps(ordered[i], ordered[j]))
                            errors.Add($"输入 '{input.Key}' 的写入重叠：{ordered[i].Element} 与 {ordered[j].Element}。");
            }
            if (errors.Count != 0)
                return points;
            // 同一时刻先松开再写入，前一个 Clip 的终点不会覆盖紧接着的下一个 Clip 起点。
            var changes = new List<(TimelinePreviewInputPoint Point, bool Release)>();
            foreach (Write write in writes)
            {
                changes.Add((write.Value, false));
                if (write.End is { } end)
                    changes.Add((new TimelinePreviewInputPoint { InputId = write.Value.InputId, Time = end.AsSingle }, true));
            }
            points.AddRange(changes.OrderBy(change => change.Point.Time).ThenBy(change => change.Release ? 0 : 1)
                .Select(change => change.Point));
            return points;
        }

        static void AddWrites(TimelinePreviewInputGraph graph, string element, OrchestrationTime start, OrchestrationTime? end,
            IReadOnlyList<GraphHostInputDescriptor> declared, List<Write> writes, List<string> errors)
        {
            var graphErrors = new List<string>();
            graph.ValidateTimelineBinding(null, null, graphErrors, true);
            foreach (string error in graphErrors)
                errors.Add($"{element}：{error}");
            foreach (TimelinePreviewInputWriteNode node in graph.Writes)
            {
                if (!declared.Any(input => input.InputId == node.InputId))
                {
                    errors.Add($"{element} 的图 '{graph.name}' 写入宿主未声明的输入 '{node.InputId}'。");
                    continue;
                }
                writes.Add(new Write(node.At(start.AsSingle), start, end, element));
            }
        }

        static bool Overlaps(in Write left, in Write right)
        {
            OrchestrationTime leftEnd = left.End ?? left.Start;
            OrchestrationTime rightEnd = right.End ?? right.Start;
            if (left.End == null && right.End == null)
                return left.Start == right.Start;
            return left.Start < rightEnd && right.Start < leftEnd ||
                left.End == null && left.Start >= right.Start && left.Start < rightEnd ||
                right.End == null && right.Start >= left.Start && right.Start < leftEnd;
        }

        // 把值变化写成输入 Timeline 内容：开关输入的按下到松开成 Clip，其余成 Marker；每种（输入，值）一张图，
        // 作为方案资产的子资产生成并复用。原内容整体替换。
        public static void Replace(TimelineAsset timeline, TimelineContractCatalog catalog,
            IReadOnlyList<GraphHostInputDescriptor> declared, IReadOnlyList<TimelinePreviewInputPoint> points)
        {
            TimelineData data = timeline.Data;
            Undo.RegisterCompleteObjectUndo(timeline, "写入预览输入");
            foreach (Track track in data.Tracks.ToArray())
                data.RemoveTrack(track);
            var graphs = new List<TimelinePreviewInputGraph>();
            foreach (IGrouping<string, TimelinePreviewInputPoint> input in points.GroupBy(point => point.InputId))
            {
                GraphHostInputDescriptor descriptor = declared.FirstOrDefault(value => value.InputId == input.Key) ??
                    throw new InvalidOperationException($"宿主未声明输入 '{input.Key}'。");
                Track track = data.EnsureTrack(typeof(TreeTrack), catalog, AuthoringIdentity.Create(), input.Key,
                    TimelineExecutionDomain.Presentation);
                TimelinePreviewInputPoint[] ordered = input.OrderBy(point => point.Time).ToArray();
                for (int i = 0; i < ordered.Length; i++)
                {
                    TimelinePreviewInputPoint point = ordered[i];
                    var start = OrchestrationTime.FromSeconds(point.Time);
                    if (descriptor.ValueKind == GraphHostValueKind.Bool && point.Boolean && i + 1 < ordered.Length &&
                        !ordered[i + 1].Boolean)
                    {
                        data.EnsureClip(catalog, track, AuthoringIdentity.Create(), GraphFor(timeline, graphs, point),
                            start, OrchestrationTime.FromSeconds(ordered[i + 1].Time));
                        i++;
                        continue;
                    }
                    track.EnsureMarker(AuthoringIdentity.Create(), start, GraphFor(timeline, graphs, point));
                }
            }
            data.Init();
            EditorUtility.SetDirty(timeline);
        }

        static TimelinePreviewInputGraph GraphFor(TimelineAsset timeline, List<TimelinePreviewInputGraph> graphs,
            TimelinePreviewInputPoint value)
        {
            foreach (TimelinePreviewInputGraph known in graphs)
                if (known.Writes.Single().SameValue(value))
                    return known;
            var graph = ScriptableObject.CreateInstance<TimelinePreviewInputGraph>();
            graph.name = $"{value.InputId} = {Describe(value)}";
            graph.AddNode(typeof(TimelinePreviewInputWriteNode), Vector2.zero);
            graph.Writes.Single().Configure(value);
            graph.SelfSerialize();
            AssetDatabase.AddObjectToAsset(graph, timeline);
            graphs.Add(graph);
            return graph;
        }

        static string Describe(TimelinePreviewInputPoint value) =>
            value.Boolean ? "按下" : value.Scalar != 0f ? value.Scalar.ToString("0.###")
                : value.Vector2 != Vector2.zero ? value.Vector2.ToString()
                : value.Vector3 != Vector3.zero ? value.Vector3.ToString()
                : value.Text.Length != 0 ? value.Text : value.Integer.ToString();
    }
}
