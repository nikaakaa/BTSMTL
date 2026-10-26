#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Authoring.Graph;
using FlowCanvas;
using NodeCanvas.Framework;
using ParadoxNotion.Design;
using ParadoxNotion.Serialization;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Graphs
{
    static class BtsmtlGraphAuthoringGUI
    {
        static readonly Dictionary<Type, GraphAuthoringFieldAttribute[]> s_Fields = new();

        static GraphAuthoringFieldAttribute[] Fields(Type type)
        {
            if (!s_Fields.TryGetValue(type, out GraphAuthoringFieldAttribute[] fields))
            {
                fields = GraphAuthoringMemberBindings.Contract(type).Fields.Select(field => field.Metadata).ToArray();
                s_Fields.Add(type, fields);
            }
            return fields;
        }

        internal static void DrawFields(BtsmtlFlowNode node)
        {
            var graph = (IBtsmtlAuthoringGraph)node.graph;
            EditorGUI.BeginDisabledGroup(graph.Graph.isEditorReadOnly);
            GraphAuthoringCandidate edits = DrawFields(node, graph);
            EditorGUI.EndDisabledGroup();
            if (edits.HasChanges)
            {
                try
                {
                    graph.Authoring.Edit("编辑图节点字段", () => edits.Apply(node));
                }
                catch (InvalidOperationException error) { NodeCanvas.Editor.GraphEditor.current.ShowNotification(new GUIContent(error.Message)); }
                catch (ArgumentException error) { NodeCanvas.Editor.GraphEditor.current.ShowNotification(new GUIContent(error.Message)); }
            }
        }

        static GraphAuthoringCandidate DrawFields(object target, IBtsmtlAuthoringGraph graph)
        {
            Type type = target.GetType();
            GraphAuthoringCandidate edits = GraphAuthoringMemberBindings.CreateCandidate(target);
            foreach (GraphAuthoringFieldAttribute field in Fields(type))
            {
                if (!field.AuthoringVisible)
                    continue;
                GraphAuthoringMember member = edits.Contract[field.FieldId];
                object value = member.Evaluate(edits);
                Type valueType = GraphAuthoringMemberBindings.ValueType(type, field.FieldId);
                EditorGUI.BeginDisabledGroup(field.ReadOnly);
                EditorGUI.BeginChangeCheck();
                object edited;
                var label = new GUIContent(field.DisplayName ?? field.FieldId);
                if (field.PickerKind == "graph-variable")
                {
                    var reference = (IBtsmtlVariableFlowNode)target;
                    GraphVariableDescriptor[] candidates = graph.Variables.CreateContract().Descriptors
                        .Where(variable => variable.ValueType == reference.ValueType).ToArray();
                    int index = Array.FindIndex(candidates, variable => variable.Reference.VariableId == (string)value);
                    int selected = EditorGUILayout.Popup(label, index,
                        candidates.Select(variable => new GUIContent(variable.Name)).ToArray());
                    edited = selected >= 0 ? candidates[selected].Reference.VariableId : value;
                    if (selected != index && selected >= 0)
                        edits.Contract[field.PickerOwnerField].WriteCandidate(edits, graph.Variables.OwnerId);
                }
                else
                    edited = DrawValue(label, value, valueType, graph);
                bool changed = EditorGUI.EndChangeCheck();
                EditorGUI.EndDisabledGroup();
                if (changed)
                    member.WriteCandidate(edits, edited);
            }
            return edits;
        }

        static object DrawValue(GUIContent label, object value, Type type, IBtsmtlAuthoringGraph graph)
        {
            Type collection = type.GetInterfaces().Concat(new[] { type })
                .FirstOrDefault(candidate => candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IReadOnlyList<>));
            if (collection != null)
            {
                Type elementType = collection.GetGenericArguments()[0];
                object[] items = ((IEnumerable)value).Cast<object>().ToArray();
                var next = new List<object>(items);
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                for (int i = 0; i < items.Length; i++)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GraphAuthoringCandidate edits = DrawFields(items[i], graph);
                    if (edits.HasChanges)
                    {
                        object edited = JSONSerializer.Clone<object>(items[i]);
                        next[i] = edits.Apply(edited);
                    }
                    if (GUILayout.Button("移除"))
                    {
                        next.RemoveAt(i);
                        GUI.changed = true;
                    }
                    EditorGUILayout.EndVertical();
                }
                if (GUILayout.Button("添加"))
                {
                    next.Add(Activator.CreateInstance(elementType));
                    GUI.changed = true;
                }
                Array array = Array.CreateInstance(elementType, next.Count);
                for (int i = 0; i < next.Count; i++)
                    array.SetValue(next[i], i);
                return array;
            }
            return EditorUtils.DirectFieldControl(label, value, type, graph.Graph,
                Array.Empty<object>(), out _);
        }

        internal static void DrawValueInputs(IBtsmtlAuthoringGraph graph, FlowNode node)
        {
            EditorGUI.BeginDisabledGroup(graph.Graph.isEditorReadOnly);
            foreach (ValueInput input in node.GetInputValuePorts())
            {
                if (input.isConnected)
                    continue;
                EditorGUI.BeginChangeCheck();
                object value = EditorUtils.DirectFieldControl(new GUIContent(input.name),
                    input.serializedValue, input.type, graph.Graph, Array.Empty<object>(), out _);
                if (EditorGUI.EndChangeCheck())
                    graph.Authoring.Edit("编辑图输入常量", () => input.serializedValue = value);
            }
            EditorGUI.EndDisabledGroup();
        }

        internal static GenericMenu VariableCreationMenu(IBtsmtlAuthoringGraph graph)
        {
            var menu = new GenericMenu();
            foreach (Type type in new[] { typeof(bool), typeof(int), typeof(float), typeof(string),
                         typeof(Vector2), typeof(Vector3), typeof(Quaternion) })
                menu.AddItem(new GUIContent(type.Name), false, () =>
                {
                    string name = "New " + type.Name;
                    int suffix = 2;
                    while (graph.Variables.Blackboard.variables.ContainsKey(name))
                        name = "New " + type.Name + " " + suffix++;
                    object value = type == typeof(string) ? string.Empty :
                        type == typeof(Quaternion) ? Quaternion.identity : Activator.CreateInstance(type);
                    typeof(BtsmtlGraphVariableDeclarations).GetMethod(nameof(BtsmtlGraphVariableDeclarations.Declare))
                        .MakeGenericMethod(type).Invoke(graph.Variables,
                            new[] { (object)Guid.NewGuid().ToString("N"), name, value });
                });
            return menu;
        }

        internal static GenericMenu VariableNodeMenu(IBtsmtlAuthoringGraph graph, Variable variable, Vector2 position)
        {
            var menu = new GenericMenu();
            Add("读取变量", typeof(BtsmtlVariableGetFlowNode<>));
            Add("写入变量", typeof(BtsmtlVariableSetFlowNode<>));
            if (!graph.PublishesVariables)
                return menu;
            menu.AddItem(new GUIContent("发布到宿主"), variable.isExposedPublic,
                () => graph.Variables.ConfigureVariable(variable.ID, !variable.isExposedPublic));
            BtsmtlBlackboardDeclaration current = graph.Variables.RequireDeclaration(variable.ID);
            foreach (PipelineBlackboardVariableScope scope in Enum.GetValues(typeof(PipelineBlackboardVariableScope)))
            {
                PipelineBlackboardVariableLifetime lifetime = PipelineBlackboardVariablePolicy.DefaultLifetime(scope);
                menu.AddItem(new GUIContent("黑板作用域/" + scope), current.Scope == scope, () =>
                    graph.Variables.ConfigureBlackboard(variable.ID, scope, lifetime, current.Category));
            }
            foreach (PipelineBlackboardVariableLifetime lifetime in Enum.GetValues(typeof(PipelineBlackboardVariableLifetime)))
            {
                if (!PipelineBlackboardVariablePolicy.IsValid(current.Scope, lifetime))
                    continue;
                menu.AddItem(new GUIContent("黑板生命周期/" + lifetime), current.Lifetime == lifetime, () =>
                    graph.Variables.ConfigureBlackboard(variable.ID, current.Scope, lifetime, current.Category, current.InputBinding));
            }
            return menu;

            void Add(string title, Type generic) => menu.AddItem(new GUIContent(title), false, () =>
                graph.Authoring.Edit(title, () =>
                {
                    var node = (IBtsmtlVariableFlowNode)graph.Graph.AddNode(generic.MakeGenericType(variable.varType), position);
                    node.ConfigureVariable(graph.Variables.OwnerId, variable.ID);
                }));
        }
    }
}
#endif
