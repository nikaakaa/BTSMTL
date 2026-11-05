#if UNITY_EDITOR
using System;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using ThirdPersonSimulation;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    sealed class TimelineClipCreationRequest
    {
        public string TrackAuthoringId;
        public string Kind;
        public TimelineExecutionDomain ExecutionDomain;
        public OrchestrationTime StartTime;
        public OrchestrationTime EndTime;
        public OrchestrationTime DefaultEndTime;
        public OrchestrationTime TimelineEndTime;
        public UnityEngine.Object Resource;
        public GraphAuthoringResourceReference ResourceReference;
        public GraphAuthoringResourceRegistrationInput ResourceRegistration;
        public Clip Prototype;
        public TimelineAuthoringClipConfiguration Configuration;
        public ScriptableObject TreeGraph;
        public Type TreeGraphType;
        public string NewTreeGraphName = string.Empty;
        public TimelineClipExitSource TreeExitSource = TimelineClipExitSource.FrameBoundary;
    }

    sealed class TimelineClipCreationPopup : PopupWindowContent
    {
        readonly TimelineClipCreationRequest m_Request;
        readonly IReadOnlyList<TimelineExternalBindingDeclaration> m_Bindings;
        readonly Func<TimelineClipCreationRequest, string> m_Create;
        readonly IReadOnlyList<TimelineAuthoringProperty> m_Properties;
        string m_Error;
        string m_SubmitError;
        readonly TimelineResourceSelection m_Resources;

        public TimelineClipCreationPopup(TimelineClipCreationRequest request,
            IReadOnlyList<TimelineExternalBindingDeclaration> bindings, Func<TimelineClipCreationRequest, string> create)
        {
            m_Request = request;
            m_Bindings = bindings;
            m_Create = create;
            m_Properties = TimelineAuthoringClipBinding.Properties(request.Prototype);
            if (request.Prototype is ITimelineAuthoringResourceClip)
            {
                m_Resources = new TimelineResourceSelection(request.Prototype, UpdateResource, () => editorWindow.Repaint(), request.Resource);
                m_Request.Resource = null;
            }
        }

        void UpdateResource(TimelineResourceSelection selection)
        {
            UnityEngine.Object previous = m_Request.Resource;
            m_Request.Resource = selection.Source;
            m_Request.ResourceReference = selection.Reference;
            m_Request.ResourceRegistration = selection.Registration;
            foreach (TimelineAuthoringProperty property in m_Properties)
                if (property.IsSourceAsset) m_Request.Configuration.Set(property.PropertyId, selection.Reference);
            if (m_Request.Resource && previous != m_Request.Resource)
                UpdateDuration((ITimelineAuthoringClipSource)m_Request.Prototype);
        }

        void UpdateDuration(ITimelineAuthoringClipSource source)
        {
            float duration = source.GetSourceDuration(m_Request.Resource);
            OrchestrationTime end = m_Request.StartTime + OrchestrationTime.FromSeconds(duration);
            if (m_Request.EndTime == m_Request.DefaultEndTime) m_Request.EndTime = end;
            m_Request.DefaultEndTime = end;
            foreach (TimelineAuthoringProperty property in m_Properties)
            {
                if (property.SourceBoundary == TimelineAuthoringSourceBoundary.Start) m_Request.Configuration.Set(property.PropertyId, 0f);
                if (property.SourceBoundary == TimelineAuthoringSourceBoundary.End) m_Request.Configuration.Set(property.PropertyId, duration);
            }
        }

        public override Vector2 GetWindowSize() => new Vector2(380, 520);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Add Clip", EditorStyles.boldLabel);
            m_Request.StartTime = DrawTime("Start (Seconds)", m_Request.StartTime);
            bool dynamicTreeClip = IsTreeClip && m_Request.TreeExitSource == TimelineClipExitSource.TreeDecision;
            if (dynamicTreeClip && m_Request.ExecutionDomain == TimelineExecutionDomain.Presentation &&
                m_Request.EndTime == m_Request.DefaultEndTime)
                m_Request.EndTime = OrchestrationTime.Max(m_Request.StartTime + OrchestrationTime.FromRaw(1), m_Request.TimelineEndTime);
            if (dynamicTreeClip && m_Request.ExecutionDomain == TimelineExecutionDomain.Logic)
                EditorGUILayout.LabelField("End (Seconds)", $"Timeline End ({m_Request.EndTime})");
            else
                m_Request.EndTime = DrawTime(dynamicTreeClip ? "Layout End (Seconds)" : "End (Seconds)", m_Request.EndTime);

            if (IsTreeClip)
            {
                if (m_Request.ExecutionDomain == TimelineExecutionDomain.Presentation)
                    m_Request.TreeExitSource = (TimelineClipExitSource)EditorGUILayout.EnumPopup("Exit Source", m_Request.TreeExitSource);
                m_Request.TreeGraph = (ScriptableObject)EditorGUILayout.ObjectField("Skill Graph", m_Request.TreeGraph,
                    m_Request.TreeGraphType, false);
                m_Request.NewTreeGraphName = EditorGUILayout.TextField("New Graph Name", m_Request.NewTreeGraphName);
                EditorGUILayout.HelpBox("Skill Graph 为空时按名称在 Timeline 资产内创建子资产图。", MessageType.Info);
            }
            if (m_Resources != null)
                m_Resources.Draw();
            else if (m_Request.Prototype is ITimelineAuthoringClipSource source)
            {
                UnityEngine.Object previous = m_Request.Resource;
                m_Request.Resource = EditorGUILayout.ObjectField("Source", previous, source.SourceAssetType, false);
                if (!ReferenceEquals(previous, m_Request.Resource) && m_Request.Resource)
                {
                    float duration = source.GetSourceDuration(m_Request.Resource);
                    OrchestrationTime end = m_Request.StartTime + OrchestrationTime.FromSeconds(duration);
                    if (m_Request.EndTime == m_Request.DefaultEndTime)
                        m_Request.EndTime = end;
                    m_Request.DefaultEndTime = end;
                    foreach (TimelineAuthoringProperty property in m_Properties)
                    {
                        if (property.IsSourceAsset)
                            m_Request.Configuration.Set(property.PropertyId, m_Request.Resource);
                        if (property.SourceBoundary == TimelineAuthoringSourceBoundary.Start)
                            m_Request.Configuration.Set(property.PropertyId, 0f);
                        if (property.SourceBoundary == TimelineAuthoringSourceBoundary.End)
                            m_Request.Configuration.Set(property.PropertyId, duration);
                    }
                }
            }
            foreach (TimelineAuthoringProperty property in m_Properties)
            {
                if (property.IsSourceAsset || !property.Supports(m_Request.Prototype, m_Request.Resource))
                    continue;
                m_Request.Configuration.Set(property.PropertyId, TimelineAuthoringPropertyGUI.Draw(property,
                    ObjectNames.NicifyVariableName(property.PropertyId), m_Request.Configuration[property.PropertyId],
                    TimelineAuthoringClipBinding.ValueType(m_Request.Prototype, property), m_Bindings));
            }

            m_Error = Validate();
            if (!string.IsNullOrEmpty(m_Error))
                EditorGUILayout.HelpBox(m_Error, MessageType.Error);
            if (!string.IsNullOrEmpty(m_SubmitError))
                EditorGUILayout.HelpBox(m_SubmitError, MessageType.Error);
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(m_Error)))
            {
                if (GUILayout.Button("Create"))
                {
                    if (m_Resources != null) UpdateResource(m_Resources);
                    EditorApplication.delayCall += () =>
                    {
                        string error = m_Create(m_Request);
                        if (string.IsNullOrEmpty(error)) editorWindow.Close();
                        else { m_SubmitError = error; editorWindow.Repaint(); }
                    };
                }
            }
        }

        string Validate()
        {
            if (m_Request.StartTime < OrchestrationTime.Zero || m_Request.EndTime <= m_Request.StartTime)
                return "帧范围必须满足 Start < End。";
            if (m_Resources != null && !m_Resources.Ready) return "请选择实际 Scope、资源对象及对应业务键。";
            if (m_Request.Prototype is ITimelineAuthoringClipSource source && !m_Request.Resource)
                return source.SourceMissingError;
            if (IsTreeClip && m_Request.TreeGraph == null && string.IsNullOrWhiteSpace(m_Request.NewTreeGraphName))
                return "TreeClip必须绑定或创建 FlowCanvas SkillGraph。";
            return string.Empty;
        }

        static OrchestrationTime DrawTime(string label, OrchestrationTime time)
        {
            EditorGUI.BeginChangeCheck();
            double seconds = EditorGUILayout.DoubleField(label, time.AsSingle);
            return EditorGUI.EndChangeCheck() ? OrchestrationTime.FromSeconds(seconds) : time;
        }

        bool IsTreeClip => m_Request.Kind == TimelineContractKinds.TreeClip;

    }

    sealed class TimelineResourceSelection
    {
        readonly TimelineData m_Timeline;
        readonly TimelineAuthoringProperty m_Property;
        readonly Action<TimelineResourceSelection> m_Changed;
        readonly Action m_Repaint;
        ITimelineAuthoringResourceDocument m_Document;
        GraphAuthoringResourceReference[] m_Scopes;
        GraphAuthoringResourceReference m_Scope;
        GraphAuthoringResourceDeclaration m_Declaration;
        GraphAuthoringResourceReference[] m_References = Array.Empty<GraphAuthoringResourceReference>();
        GraphAuthoringResourceReference m_Reference;
        GraphAuthoringResourceObjectCandidate[] m_Objects = Array.Empty<GraphAuthoringResourceObjectCandidate>();
        int m_SelectedObject = -1;
        UnityEngine.Object m_File;
        UnityEngine.Object m_Source;
        GraphAuthoringResourceRegistrationInput m_Registration;
        string m_Error;

        internal TimelineResourceSelection(Clip clip, Action<TimelineResourceSelection> changed, Action repaint,
            UnityEngine.Object initialSource = null)
        {
            m_Timeline = clip.Timeline;
            m_Property = TimelineAuthoringClipBinding.Properties(clip).Single(property => property.IsSourceAsset);
            m_Changed = changed;
            m_Repaint = repaint;
            m_Reference = initialSource ? default : ((ITimelineAuthoringResourceClip)clip).ResourceReference;
            m_File = initialSource;
            Queue(Prepare);
        }

        internal UnityEngine.Object Source => m_Source;
        internal GraphAuthoringResourceRegistrationInput Registration => m_Registration;
        internal GraphAuthoringResourceReference Reference => m_Registration == null ? m_Reference :
            new GraphAuthoringResourceReference(m_Scope.Package, m_Property.SourceAssetKind, m_Registration.Key, m_Scope.Context);
        internal bool Ready => m_Source && (m_Registration != null || !m_Reference.IsEmpty);

        internal void Queue(Action action, Action completed = null)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                    m_Error = null;
                    m_Changed(this);
                    m_Repaint();
                    completed?.Invoke();
                }
                catch (Exception exception) { m_Error = exception.Message; m_Repaint(); }
            };
        }

        void Prepare()
        {
            m_Document = m_Timeline.AuthoringDocument as ITimelineAuthoringResourceDocument;
            if (m_Document == null) throw new InvalidOperationException("当前 Timeline 没有正式资源文档，请从其作者文档打开。");
            m_Scopes = m_Document.ResourceScopes().ToArray();
            m_Scope = m_Document.ResourceScope(m_Timeline);
            m_Declaration = null;
            m_Source = null;
            m_Registration = null;
            m_Objects = Array.Empty<GraphAuthoringResourceObjectCandidate>();
            if (m_Scope.IsEmpty) return;
            m_Declaration = m_Document.ResourceDeclaration(m_Timeline, m_Property.SourceAssetKind);
            m_References = m_Document.ResourceReferences(m_Timeline, m_Property.SourceAssetKind).ToArray();
            if (!m_Reference.IsEmpty) m_File = m_Source = m_Document.ResolveResource(m_Timeline, m_Reference);
            else if (m_File) SelectSource(m_File);
        }

        internal void Draw()
        {
            if (m_Scopes != null)
            {
                int index = Array.IndexOf(m_Scopes, m_Scope);
                int selected = EditorGUILayout.Popup(new GUIContent("作者 Scope"), index,
                    m_Scopes.Select(scope => new GUIContent(scope.Key, scope.Package + " / " + scope.Context)).ToArray());
                if (selected != index)
                {
                    GraphAuthoringResourceReference scope = m_Scopes[selected];
                    Queue(() =>
                    {
                        m_Reference = default;
                        try { m_Document.SelectResourceScope(m_Timeline, scope); }
                        finally { Prepare(); }
                    });
                }
                if (m_Scope.IsEmpty) EditorGUILayout.HelpBox(m_Scopes.Length == 0 ? "当前没有已声明的实际执行 Scope。" : "请明确选择资源所属的作者 Scope。", MessageType.Info);
            }
            if (m_Declaration != null)
            {
                int key = Array.IndexOf(m_References, m_Reference);
                int selected = EditorGUILayout.Popup("业务键", key, m_References.Select(reference => reference.Key).ToArray());
                if (selected != key)
                {
                    GraphAuthoringResourceReference reference = m_References[selected];
                    Queue(() => SelectReference(reference));
                }
                if (m_Declaration.Availability.Available)
                {
                    UnityEngine.Object source = EditorGUILayout.ObjectField("资源", m_File, typeof(UnityEngine.Object), false);
                    if (source != m_File) Queue(() => SelectSource(source));
                    if (m_Objects.Length != 0)
                    {
                        int choice = EditorGUILayout.Popup(new GUIContent("资源子对象"), m_SelectedObject,
                            m_Objects.Select(candidate => new GUIContent(candidate.Hierarchy + " · " + candidate.Type, candidate.Token)).ToArray());
                        if (choice != m_SelectedObject)
                        {
                            m_SelectedObject = choice;
                            GraphAuthoringResourceObjectCandidate candidate = m_Objects[choice];
                            Queue(() => SelectObject(candidate));
                        }
                    }
                }
                else EditorGUILayout.HelpBox(m_Declaration.Availability.Reason, MessageType.Info);
                if (m_Registration != null)
                {
                    m_Registration.Key = EditorGUILayout.TextField("新业务键", m_Registration.Key);
                    if (m_Registration is GraphAuthoringInstanceResourceRegistrationInput instance)
                        instance.InstanceCapacity = EditorGUILayout.IntField("实例容量", instance.InstanceCapacity);
                    if (m_Registration is GraphAuthoringCameraEffectRegistrationInput effect)
                        effect.EffectKind = EditorGUILayout.TextField("效果类型", effect.EffectKind);
                }
            }
            if (m_Error != null) EditorGUILayout.HelpBox(m_Error, MessageType.Error);
        }

        void SelectReference(GraphAuthoringResourceReference reference)
        {
            m_Reference = reference;
            m_Registration = null;
            m_File = m_Source = null;
            m_Objects = Array.Empty<GraphAuthoringResourceObjectCandidate>();
            m_SelectedObject = -1;
            m_File = m_Source = m_Document.ResolveResource(m_Timeline, reference);
        }

        internal void SelectSource(UnityEngine.Object source)
        {
            m_File = source;
            m_Source = null;
            m_Reference = default;
            m_Registration = null;
            m_References = Array.Empty<GraphAuthoringResourceReference>();
            m_Objects = Array.Empty<GraphAuthoringResourceObjectCandidate>();
            m_SelectedObject = -1;
            if (!source) return;
            GraphAuthoringResourceObjectResult result = m_Document.ResourceObjects(m_Timeline, m_Property.SourceAssetKind, source);
            if (!result.Available) throw new InvalidOperationException(result.Reason);
            m_Objects = result.Items;
            GraphAuthoringResourceObjectCandidate exact = m_Objects.SingleOrDefault(candidate => m_Document.ResolveResourceObject(candidate.Token) == source);
            if (exact != null) SelectObject(exact);
            else if (m_Objects.Length == 0) throw new InvalidOperationException("所选文件没有满足当前正式资源能力声明的对象。");
        }

        void SelectObject(GraphAuthoringResourceObjectCandidate candidate)
        {
            m_SelectedObject = Array.IndexOf(m_Objects, candidate);
            m_Source = m_Document.ResolveResourceObject(candidate.Token);
            m_References = m_Document.ResourceReferences(m_Timeline, m_Property.SourceAssetKind, m_Source).ToArray();
            m_Registration = null;
            m_Reference = default;
            if (m_References.Length == 1) m_Reference = m_References[0];
            else if (m_References.Length == 0)
            {
                if (m_Declaration.RegistrationInput == null) throw new InvalidOperationException("该资源类别没有正式登记操作，请先在原作者入口创建业务键。");
                Type inputType = typeof(GraphAuthoringResourceRegistrationInput).Assembly.GetType(m_Declaration.InputType, true);
                m_Registration = (GraphAuthoringResourceRegistrationInput)Activator.CreateInstance(inputType);
                m_Registration.Token = candidate.Token;
                m_Registration.Key = candidate.Name;
            }
        }

        internal static void ApplyReference(Clip clip, GraphAuthoringResourceReference reference, UnityEngine.Object source)
        {
            TimelineAuthoringClipConfiguration configuration = TimelineAuthoringClipBinding.Read(clip);
            foreach (TimelineAuthoringProperty property in TimelineAuthoringClipBinding.Properties(clip))
                if (property.IsSourceAsset) configuration.Set(property.PropertyId, reference);
            TimelineAuthoringClipBinding.Configure(clip.Timeline, clip, configuration);
            ((ITimelineAuthoringResourceClip)clip).BindSourceAsset(source);
        }

        internal void Commit(Clip clip)
        {
            if (m_Registration != null) m_Document.RegisterResource(clip, m_Registration, reference => ApplyReference(clip, reference, m_Source));
            else m_Timeline.ApplyModify(() => ApplyReference(clip, m_Reference, m_Source), "选择 Timeline 资源");
        }
    }

    sealed class TimelineResourceReplacementPopup : PopupWindowContent
    {
        readonly TimelineResourceSelection m_Selection;
        readonly Action<TimelineResourceSelection> m_Apply;

        internal TimelineResourceReplacementPopup(Clip clip, UnityEngine.Object source, Action<TimelineResourceSelection> apply)
        {
            m_Apply = apply;
            m_Selection = new TimelineResourceSelection(clip, _ => { }, () => editorWindow.Repaint(), source);
        }

        public override Vector2 GetWindowSize() => new Vector2(420, 420);

        public override void OnGUI(Rect rect)
        {
            m_Selection.Draw();
            using (new EditorGUI.DisabledScope(!m_Selection.Ready))
                if (GUILayout.Button(m_Selection.Registration == null ? "应用资源引用" : "登记并应用资源"))
                    m_Selection.Queue(() => m_Apply(m_Selection), () => editorWindow.Close());
        }
    }
}
#endif
