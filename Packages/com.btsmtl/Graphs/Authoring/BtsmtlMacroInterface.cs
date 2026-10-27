using System;
using System.Collections.Generic;
using System.Linq;
using FlowCanvas;
using FlowCanvas.Macros;
using ParadoxNotion;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BTSMTL.Graphs
{
    // 子图参数接口：唯一执行入口、参数身份与名称唯一、值类型必须有编译合同；调用节点端口必须与接口逐项一致。
    public static class BtsmtlMacroInterface
    {
        static readonly Type[] s_EditorValueTypes =
        {
            typeof(bool), typeof(int), typeof(float), typeof(string),
            typeof(Vector2), typeof(Vector3), typeof(Quaternion), typeof(uint), typeof(ulong)
        };

        public static IReadOnlyList<Type> EditorValueTypes => s_EditorValueTypes;

        public static void Validate(Macro macro) => Validate(macro.inputDefinitions, macro.outputDefinitions);

        public static void Validate(IEnumerable<DynamicParameterDefinition> inputs, IEnumerable<DynamicParameterDefinition> outputs)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            int flowInputs = 0;
            foreach (DynamicParameterDefinition input in inputs)
            {
                RequireParameter(input, identities, names);
                if (input.type == typeof(Flow))
                    flowInputs++;
                else
                    RequireValueType(input.type);
            }
            if (flowInputs != 1)
                throw new InvalidOperationException("子图必须有且仅有一个执行入口。");
            names.Clear();
            foreach (DynamicParameterDefinition output in outputs)
            {
                RequireParameter(output, identities, names);
                RequireValueType(output.type);
            }
        }

        public static void RequireValueType(Type type)
        {
            if (type == null || type == typeof(Flow))
                throw new InvalidOperationException("子图值参数必须是有编译合同的值类型；执行完成由调用返回。");
            try { BtsmtlGraphValueTypes.Kind(type); }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException($"子图不支持'{type.FullName}'值参数。");
            }
        }

        public static void ValidateCall(MacroNodeWrapper call, Macro macro, string path)
        {
            if (call == null || macro == null)
                throw new InvalidOperationException($"{path}: 子图调用目标缺失。");
            Validate(macro);
            var inputIds = new HashSet<string>(StringComparer.Ordinal);
            var outputIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DynamicParameterDefinition definition in macro.inputDefinitions)
            {
                RequirePort(call.GetInputPort(definition.ID), definition, true, path);
                inputIds.Add(definition.ID);
            }
            foreach (DynamicParameterDefinition definition in macro.outputDefinitions)
            {
                RequirePort(call.GetOutputPort(definition.ID), definition, false, path);
                outputIds.Add(definition.ID);
            }
            foreach (Port port in call.GetInputFlowPorts().Cast<Port>().Concat(call.GetInputValuePorts()))
                if (!inputIds.Contains(port.ID))
                    throw new InvalidOperationException($"{path}: 调用节点包含未登记输入端口'{port.ID}'。");
            foreach (Port port in call.GetOutputFlowPorts().Cast<Port>().Concat(call.GetOutputValuePorts()))
                if (!outputIds.Contains(port.ID))
                    throw new InvalidOperationException($"{path}: 调用节点包含未登记输出端口'{port.ID}'。");
        }

        static void RequireParameter(DynamicParameterDefinition parameter, HashSet<string> identities, HashSet<string> names)
        {
            if (parameter == null || !parameter.hasStableIdentity || !identities.Add(parameter.ID) ||
                string.IsNullOrWhiteSpace(parameter.name) || !names.Add(parameter.name))
                throw new InvalidOperationException("子图参数必须有独立稳定身份和名称。");
        }

        static void RequirePort(Port port, DynamicParameterDefinition definition, bool input, string path)
        {
            if (port == null || port.IsFlowPort() != (definition.type == typeof(Flow)) ||
                definition.type != typeof(Flow) && port.type != definition.type)
                throw new InvalidOperationException(
                    $"{path}: 子图{(input ? "输入" : "输出")}端口'{definition.ID}'类型或方向与接口不一致。");
        }

#if UNITY_EDITOR
        public static void OpenEditor(BtsmtlMacroGraph macro) =>
            BtsmtlMacroInterfaceWindow.Open(macro ? macro : throw new ArgumentNullException(nameof(macro)));

        // 生成与人工编辑共用：整体替换接口并重新收集出入口端口。接口未变时不触碰子图与调用方；
        // 删除参数或修改类型沿人工编辑的同一规则，调用方已连接该端口时拒绝。
        public static void Configure(BtsmtlMacroGraph macro, IEnumerable<DynamicParameterDefinition> inputs,
            IEnumerable<DynamicParameterDefinition> outputs)
        {
            List<DynamicParameterDefinition> inputValues = inputs.ToList();
            List<DynamicParameterDefinition> outputValues = outputs.ToList();
            Validate(inputValues, outputValues);
            if (SameParameters(macro.inputDefinitions, inputValues) && SameParameters(macro.outputDefinitions, outputValues))
                return;
            RequireRetainedParametersAllowed(macro, macro.inputDefinitions, inputValues);
            RequireRetainedParametersAllowed(macro, macro.outputDefinitions, outputValues);
            ApplyMutation(macro, "配置子图接口", () =>
            {
                macro.inputDefinitions.Clear();
                macro.inputDefinitions.AddRange(inputValues);
                macro.outputDefinitions.Clear();
                macro.outputDefinitions.AddRange(outputValues);
            });
        }

        static bool SameParameters(List<DynamicParameterDefinition> current, List<DynamicParameterDefinition> next)
        {
            if (current.Count != next.Count)
                return false;
            for (int i = 0; i < current.Count; i++)
                if (current[i] == null || current[i].ID != next[i].ID || current[i].name != next[i].name ||
                    current[i].type != next[i].type)
                    return false;
            return true;
        }

        static void RequireRetainedParametersAllowed(BtsmtlMacroGraph macro, List<DynamicParameterDefinition> current,
            List<DynamicParameterDefinition> next)
        {
            foreach (DynamicParameterDefinition parameter in current)
            {
                if (parameter == null || !parameter.hasStableIdentity)
                    continue;
                DynamicParameterDefinition retained = next.Find(value => value.ID == parameter.ID);
                if (retained == null || retained.type != parameter.type)
                    RequireParameterChangeAllowed(macro, parameter.ID);
            }
        }

        // 接口变更与全部调用节点的端口刷新在同一撤销组内完成，任一调用不一致则整体回退。
        // 调用方只在序列化内容实际变化时标记修改；端口按参数身份连接，重命名或排序不改变调用方资产。
        public static void ApplyMutation(BtsmtlMacroGraph macro, string title, Action mutation)
        {
            Caller[] callers = FindCallers(macro).ToArray();
            FlowGraph[] graphs = callers.Select(caller => caller.Graph).Append(macro).Distinct().ToArray();
            if (graphs.Any(graph => graph.isEditorReadOnly))
                throw new InvalidOperationException("运行观察期间不能编辑子图接口。");
            foreach (FlowGraph graph in graphs)
                graph.SelfSerialize();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(title);
            Undo.RegisterCompleteObjectUndo(graphs.Cast<UnityEngine.Object>().ToArray(), title);
            try
            {
                macro.EditAuthoring(title, () =>
                {
                    mutation();
                    Validate(macro);
                    macro.EnsureAnchors();
                });
                foreach (Caller caller in callers)
                {
                    caller.Call.GatherPorts();
                    ValidateCall(caller.Call, macro, caller.Path);
                }
                foreach (FlowGraph graph in graphs)
                    if (graph.SelfSerialize() || ReferenceEquals(graph, macro))
                        EditorUtility.SetDirty(graph);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(group);
                foreach (FlowGraph graph in graphs)
                {
                    graph.SelfDeserialize();
                    graph.Validate();
                }
                throw;
            }
        }

        public static void RequireParameterChangeAllowed(BtsmtlMacroGraph macro, string parameterId)
        {
            foreach (Caller caller in FindCallers(macro))
            {
                Port input = caller.Call.GetInputPort(parameterId);
                Port output = caller.Call.GetOutputPort(parameterId);
                if (input?.connections > 0 || output?.connections > 0)
                    throw new InvalidOperationException($"{caller.Path}: 已连接的子图参数必须先断开，不能直接修改类型或删除。");
            }
        }

        // 接口编辑是低频操作，不维护常驻索引；先用资产数据库已缓存的直接依赖筛出引用本子图所在资产的图，
        // 只加载这些资产，避免为一次接口修改反序列化工程内全部图。
        static IEnumerable<Caller> FindCallers(BtsmtlMacroGraph macro)
        {
            string macroPath = AssetDatabase.GetAssetPath(macro);
            if (string.IsNullOrEmpty(macroPath))
                yield break;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(FlowGraph)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path != macroPath && Array.IndexOf(AssetDatabase.GetDependencies(path, false), macroPath) < 0)
                    continue;
                foreach (FlowGraph graph in AssetDatabase.LoadAllAssetsAtPath(path).OfType<FlowGraph>())
                    foreach (MacroNodeWrapper call in graph.allNodes.OfType<MacroNodeWrapper>())
                        if (ReferenceEquals(call.macro, macro))
                            yield return new Caller(graph, call, $"{path}/{graph.name}/node:{call.UID}");
            }
        }

        readonly struct Caller
        {
            internal Caller(FlowGraph graph, MacroNodeWrapper call, string path)
            {
                Graph = graph;
                Call = call;
                Path = path;
            }

            internal FlowGraph Graph { get; }
            internal MacroNodeWrapper Call { get; }
            internal string Path { get; }
        }
#endif
    }

#if UNITY_EDITOR
    sealed class BtsmtlMacroInterfaceWindow : EditorWindow
    {
        BtsmtlMacroGraph m_Macro;
        Vector2 m_Scroll;

        public static void Open(BtsmtlMacroGraph macro)
        {
            var window = GetWindow<BtsmtlMacroInterfaceWindow>("子图接口");
            window.m_Macro = macro;
            window.minSize = new Vector2(420f, 320f);
            window.Show();
        }

        void OnGUI()
        {
            if (!m_Macro)
            {
                EditorGUILayout.HelpBox("子图资产已失效。", MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField(m_Macro.name, EditorStyles.boldLabel);
            bool shared = AssetDatabase.IsMainAsset(m_Macro);
            EditorGUILayout.LabelField(shared ? "共享子图" : "私有子图", m_Macro.Host?.DisplayName ?? "未声明宿主");
            using (new EditorGUI.DisabledScope(m_Macro.isEditorReadOnly))
            {
                if (shared && m_Macro.Host is BtsmtlGraphMacroHost host)
                    DrawDomains(host);
                m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
                DrawParameters("输入参数", m_Macro.inputDefinitions, true);
                EditorGUILayout.Space(8f);
                DrawParameters("输出参数", m_Macro.outputDefinitions, false);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("添加值输入"))
                        Apply("添加子图输入", () => m_Macro.inputDefinitions.Add(NewParameter(m_Macro.inputDefinitions)));
                    if (GUILayout.Button("添加值输出"))
                        Apply("添加子图输出", () => m_Macro.outputDefinitions.Add(NewParameter(m_Macro.outputDefinitions)));
                }
            }
        }

        void DrawDomains(BtsmtlGraphMacroHost host)
        {
            string current = string.Join(", ", host.Domains);
            string edited = EditorGUILayout.DelayedTextField("宿主领域", current);
            if (edited == current)
                return;
            try
            {
                m_Macro.ConfigureHost(new BtsmtlGraphMacroHost(edited.Split(',')
                    .Select(value => value.Trim()).Where(value => value.Length != 0)));
            }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); }
        }

        void DrawParameters(string title, List<DynamicParameterDefinition> definitions, bool input)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            foreach (DynamicParameterDefinition definition in definitions.ToArray())
            {
                using var row = new EditorGUILayout.HorizontalScope();
                bool execution = input && definition.type == typeof(Flow);
                using (new EditorGUI.DisabledScope(execution))
                {
                    string name = EditorGUILayout.DelayedTextField(definition.name, GUILayout.Width(150f));
                    if (!string.Equals(name, definition.name, StringComparison.Ordinal))
                        Apply("重命名子图参数", () => definition.name = name);
                    if (!execution)
                    {
                        int index = Array.IndexOf(BtsmtlMacroInterface.EditorValueTypes.ToArray(), definition.type);
                        string[] labels = BtsmtlMacroInterface.EditorValueTypes.Select(type => type.Name)
                            .Concat(index < 0 ? new[] { definition.type.Name } : Array.Empty<string>()).ToArray();
                        int shown = index < 0 ? labels.Length - 1 : index;
                        int selected = EditorGUILayout.Popup(shown, labels, GUILayout.Width(100f));
                        if (selected != shown && selected < BtsmtlMacroInterface.EditorValueTypes.Count)
                            ChangeType(definition, BtsmtlMacroInterface.EditorValueTypes[selected]);
                    }
                }
                if (!execution && GUILayout.Button("删除", GUILayout.Width(48f)))
                    Remove(definitions, definition);
            }
        }

        static DynamicParameterDefinition NewParameter(List<DynamicParameterDefinition> definitions)
        {
            string name = "值";
            for (int suffix = 2; definitions.Any(value => value.name == name); suffix++)
                name = "值 " + suffix;
            return new DynamicParameterDefinition(Guid.NewGuid().ToString("N"), name, typeof(float));
        }

        void ChangeType(DynamicParameterDefinition definition, Type type)
        {
            try
            {
                BtsmtlMacroInterface.RequireParameterChangeAllowed(m_Macro, definition.ID);
                Apply("修改子图参数类型", () => definition.type = type);
            }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); }
        }

        void Remove(List<DynamicParameterDefinition> definitions, DynamicParameterDefinition definition)
        {
            try
            {
                BtsmtlMacroInterface.RequireParameterChangeAllowed(m_Macro, definition.ID);
                Apply("删除子图参数", () => definitions.Remove(definition));
            }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); }
        }

        void Apply(string title, Action mutation)
        {
            try { BtsmtlMacroInterface.ApplyMutation(m_Macro, title, mutation); }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); }
        }
    }
#endif
}
