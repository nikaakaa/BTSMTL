#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FlowCanvas;
using FlowCanvas.Macros;
using FlowCanvas.Nodes;
using NodeCanvas.Editor;
using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.EventGraphs
{
    static class HostEventGraphEditorMutation
    {
        sealed class Scope
        {
            internal int Depth;
        }

        static readonly ConditionalWeakTable<HostEventGraph, Scope> s_Scopes =
            new ConditionalWeakTable<HostEventGraph, Scope>();
        static int s_BinderValidationDepth;

        internal static UnityEngine.Object UndoTarget(HostEventGraph graph) =>
            s_Scopes.TryGetValue(graph, out Scope scope) && scope.Depth != 0
                ? null
                : graph;

        internal static Node AddNode(
            HostEventGraph graph,
            Type nodeType,
            Vector2 position)
        {
            if (!graph.CanAuthorNodeType(nodeType))
                throw new InvalidOperationException(
                    $"Event graph node type '{nodeType?.FullName}' is not registered.");
            Node result = null;
            Apply(graph, "Create Event Graph Node", () =>
            {
                result = graph.AddNodeNative(nodeType, position);
                if (result == null)
                    throw new InvalidOperationException("Event graph node could not be created.");
            });
            GraphEditorUtility.activeElement = result;
            return result;
        }

        internal static BinderConnection CreatePortConnection(
            HostEventGraph graph,
            Port source,
            Port target)
        {
            return CreatePortConnection(graph, null, source, target);
        }

        internal static BinderConnection CreatePortConnection(
            HostEventGraph graph,
            string connectionIdentity,
            Port source,
            Port target)
        {
            if (!CanConnect(graph, source, target, out string reason))
                throw new InvalidOperationException(reason);
            if (!string.IsNullOrWhiteSpace(connectionIdentity) &&
                graph.GetAllConnections().Any(value =>
                    string.Equals(
                        value.UID,
                        connectionIdentity,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Event graph connection '{connectionIdentity}' already exists.");
            }
            BinderConnection result = null;
            Apply(graph, "Connect Event Graph Ports", () =>
            {
                result = graph.CreatePortConnectionNative(source, target);
                if (result == null)
                    throw new InvalidOperationException("Event graph port connection could not be created.");
                if (!string.IsNullOrWhiteSpace(connectionIdentity))
                    result.ConfigureAuthoringIdentity(connectionIdentity.Trim());
            });
            return result;
        }

        internal static void RemoveNode(
            HostEventGraph graph,
            Node node,
            bool recordUndo,
            bool force)
        {
            if (node == null || node.graph != graph || !graph.allNodes.Contains(node))
                throw new InvalidOperationException("Event graph node does not belong to the current graph.");
            Apply(graph, "Delete Event Graph Node", () =>
                graph.RemoveNodeNative(node, recordUndo, force));
        }

        internal static void RemoveConnection(
            HostEventGraph graph,
            Connection connection,
            bool recordUndo)
        {
            if (connection == null || connection.graph != graph)
                throw new InvalidOperationException("Event graph connection does not belong to the current graph.");
            Apply(graph, "Delete Event Graph Connection", () =>
                graph.RemoveConnectionNative(connection, recordUndo));
        }

        internal static List<Node> DuplicateNodes(
            HostEventGraph graph,
            List<Node> nodes,
            Vector2 originPosition)
        {
            if (nodes == null || nodes.Count == 0)
                return new List<Node>();
            if (nodes.Any(node => node == null || node.graph != graph))
                throw new InvalidOperationException("Event graph duplicate selection is invalid.");
            List<Node> result = null;
            Apply(graph, "Duplicate Event Graph Nodes", () =>
                result = graph.DuplicateNodesNative(nodes, originPosition));
            return result;
        }

        internal static void DisconnectPort(HostEventGraph graph, Port port)
        {
            if (port == null || port.parent == null || port.parent.graph != graph)
                throw new InvalidOperationException("Event graph port does not belong to the current graph.");
            Apply(graph, "Disconnect Event Graph Port", () =>
            {
                foreach (BinderConnection connection in port.GetPortConnections().ToArray())
                    graph.RemoveConnectionNative(connection, true);
            });
        }

        internal static void Clear(HostEventGraph graph)
        {
            Apply(graph, "Clear Event Graph", () =>
            {
                foreach (Connection connection in graph.allNodes
                             .SelectMany(node => node.outConnections)
                             .ToArray())
                    graph.RemoveConnectionNative(connection, true);
                graph.ClearNativeNodes();
            });
            GraphEditorUtility.activeElement = null;
            GraphEditorUtility.activeElements = null;
        }

        internal static bool HandleCommand(
            HostEventGraph graph,
            string command,
            Vector2 position)
        {
            if (command is not ("Copy" or "Cut" or "Paste" or "Duplicate" or "Delete" or "SoftDelete"))
                return false;
            try
            {
                if (graph.isEditorReadOnly && command != "Copy")
                    throw new InvalidOperationException("Event graph is read-only while observing Play Mode.");
                IGraphElement[] selected =
                    GraphEditorUtility.activeElements?.ToArray() ??
                    (GraphEditorUtility.activeElement != null
                        ? new[] { GraphEditorUtility.activeElement }
                        : Array.Empty<IGraphElement>());
                if (selected.Any(element => element.graph != graph))
                    throw new InvalidOperationException("Event graph selection belongs to another graph.");
                Node[] nodes = selected.OfType<Node>().ToArray();
                if (command is "Copy" or "Cut")
                {
                    if (nodes.Length != 0)
                        CopyBuffer.SetCache<Node[]>(
                            Graph.CloneNodes(nodes.ToList()).ToArray());
                }
                if (command is "Cut" or "Delete" or "SoftDelete")
                {
                    Apply(graph, "Delete Event Graph Selection", () =>
                    {
                        foreach (Connection connection in selected.OfType<Connection>())
                            graph.RemoveConnectionNative(connection, true);
                        foreach (Node node in nodes)
                            graph.RemoveNodeNative(node, false, false);
                    });
                    GraphEditorUtility.activeElement = null;
                    GraphEditorUtility.activeElements = null;
                }
                if (command == "Paste" &&
                    CopyBuffer.TryGetCache<Node[]>(out Node[] copied))
                {
                    GraphEditorUtility.activeElements =
                        graph.DuplicateNodes(copied.ToList(), position)
                            .Cast<IGraphElement>()
                            .ToList();
                }
                if (command == "Duplicate" && nodes.Length != 0)
                {
                    GraphEditorUtility.activeElements =
                        graph.DuplicateNodes(nodes.ToList(), position)
                            .Cast<IGraphElement>()
                            .ToList();
                }
            }
            catch (Exception error) when (
                error is InvalidOperationException ||
                error is ArgumentException)
            {
                GraphEditor.current?.ShowNotification(new GUIContent(error.Message));
            }
            return true;
        }

        internal static bool CanConnect(
            HostEventGraph graph,
            Port source,
            Port target,
            out string reason)
        {
            reason = null;
            if (graph == null || graph.isEditorReadOnly)
                reason = "Event graph is read-only.";
            else if (source == null || target == null)
                reason = "Event graph connection requires two ports.";
            else if (source.parent?.graph != graph || target.parent?.graph != graph)
                reason = "Event graph connections must remain inside one graph.";
            else if (!source.IsOutputPort() || !target.IsInputPort())
                reason = "Event graph connections require output to input.";
            else if (source.type != target.type)
                reason = "Event graph ports require exact matching types.";
            else if (source.IsFlowPort() != target.IsFlowPort())
                reason = "Event graph flow and value ports cannot be mixed.";
            else if (target is ValueInput && target.connections != 0)
                reason = "Event graph value inputs accept one connection.";
            else if (s_BinderValidationDepth == 0)
            {
                s_BinderValidationDepth++;
                try
                {
                    if (!BinderConnection.CanBeBoundVerbosed(
                            source,
                            target,
                            null,
                            out string bindingReason))
                        reason = bindingReason;
                }
                finally
                {
                    s_BinderValidationDepth--;
                }
            }
            return reason == null;
        }

        internal static void HandleBlackboardVariableDrop(
            HostEventGraph graph,
            IBlackboard blackboard,
            Variable variable,
            Vector2 position)
        {
            if (variable == null)
                return;
            if (graph.isEditorReadOnly)
            {
                GraphEditor.current?.ShowNotification(
                    new GUIContent("Event graph is read-only while observing Play Mode."));
                return;
            }
            if (blackboard != graph.blackboard)
            {
                GraphEditor.current?.ShowNotification(
                    new GUIContent("Event graph variables must come from its own Blackboard."));
                return;
            }
            if (!variable.hasStableIdentity ||
                !EventGraphValueKinds.IsSupported(variable.varType))
            {
                GraphEditor.current?.ShowNotification(
                    new GUIContent("The Blackboard variable has no supported stable type."));
                return;
            }
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("Get " + variable.name),
                false,
                () => CreateVariableNode(graph, variable, position, false));
            menu.AddItem(
                new GUIContent("Set " + variable.name),
                false,
                () => CreateVariableNode(graph, variable, position, true));
            menu.ShowAsContext();
            Event.current.Use();
        }

        internal static void AppendCreationItem(
            HostEventGraph graph,
            GenericMenu menu,
            string category,
            Type type,
            Vector2 position,
            Port context,
            object dropInstance)
        {
            if (!graph.CanAuthorNodeType(type) || graph.isEditorReadOnly)
                return;
            menu.AddItem(
                new GUIContent(category),
                false,
                () => CreateNode(graph, type, position, context, dropInstance));
        }

        internal static Variable<T> DeclareVariable<T>(
            HostEventGraph graph,
            string variableId,
            string variableName,
            T defaultValue)
        {
            if (!EventGraphValueKinds.IsSupported(typeof(T)))
                throw new InvalidOperationException(
                    $"Event graph variable type '{typeof(T).FullName}' is unsupported.");
            variableId = RequireIdentity(variableId, "variableId");
            variableName = RequireName(variableName, "variableName");
            EventGraphValue.FromObject(defaultValue);
            Variable<T> result = null;
            Apply(graph, "Declare Event Graph Variable", () =>
            {
                if (graph.blackboard.variables.Values.Any(value =>
                        string.Equals(value.ID, variableId, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"Event graph variable '{variableId}' already exists.");
                }
                if (graph.blackboard.variables.ContainsKey(variableName))
                    throw new InvalidOperationException(
                        $"Event graph variable name '{variableName}' already exists.");
                result = new Variable<T>(variableName, variableId)
                {
                    value = defaultValue
                };
                graph.blackboard.variables.Add(variableName, result);
                graph.blackboard.TryInvokeOnVariableAdded(result);
            });
            return result;
        }

        internal static void EditVariables(HostEventGraph graph, string title, Action mutation) =>
            Apply(graph, title, () =>
            {
                mutation();
                graph.blackboard.variables = graph.blackboard.variables.Values
                    .ToDictionary(variable => variable.name, variable => variable);
            });

        internal static void ReplaceVariables(HostEventGraph graph, IReadOnlyList<Variable> variables) =>
            Apply(graph, "编辑动画变量列表", () => graph.blackboard.variables =
                variables.ToDictionary(variable => variable.name, variable => variable));

        internal static void ConfigureVariable(
            HostEventGraph graph,
            string variableId,
            bool isExposedPublic)
        {
            variableId = RequireIdentity(variableId, "variableId");
            Variable variable = graph.blackboard.variables.Values.SingleOrDefault(value =>
                string.Equals(value.ID, variableId, StringComparison.Ordinal));
            if (variable == null)
                throw new InvalidOperationException(
                    $"Event graph variable '{variableId}' does not exist.");
            if (variable.isPropertyBound)
                throw new InvalidOperationException(
                    $"Event graph variable '{variableId}' uses an unsupported property binding.");
            Apply(graph, "Configure Event Graph Variable", () =>
                variable.isExposedPublic = isExposedPublic);
        }

        internal static Node AddAuthoringNode(
            HostEventGraph graph,
            Type nodeType,
            string authoringIdentity,
            Vector2 position)
        {
            if (!graph.CanAuthorNodeType(nodeType))
                throw new InvalidOperationException(
                    $"Event graph node type '{nodeType?.FullName}' is not registered.");
            authoringIdentity = RequireIdentity(authoringIdentity, "authoringIdentity");
            EnsureFinite(position, nameof(position));
            if (graph.allNodes.Any(value =>
                    string.Equals(value.UID, authoringIdentity, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Event graph node '{authoringIdentity}' already exists.");
            }
            Node result = null;
            Apply(graph, "Create Event Graph Node", () =>
            {
                result = graph.AddNodeNative(nodeType, position);
                if (result == null)
                    throw new InvalidOperationException("Event graph node could not be created.");
                result.ConfigureAuthoringIdentity(authoringIdentity);
                if (result is not ParameterVariableNode &&
                    result is not SimplexNodeWrapper)
                {
                    result.name = DisplayNameFromAuthoringIdentity(authoringIdentity);
                }
            });
            return result;
        }

        internal static void ConfigureNodeMetadata(
            HostEventGraph graph,
            Node node,
            string tag,
            string comments,
            bool isBreakpoint)
        {
            RequireNode(graph, node);
            Apply(graph, "Configure Event Graph Node", () =>
            {
                node.tag = tag ?? string.Empty;
                node.comments = comments ?? string.Empty;
                node.isBreakpoint = isBreakpoint;
            });
        }

        internal static void ConfigureCanvas(
            HostEventGraph graph,
            string category,
            string comments,
            Vector2 translation,
            float zoomFactor)
        {
            EnsureFinite(translation, nameof(translation));
            if (!float.IsFinite(zoomFactor) || zoomFactor <= 0f)
                throw new ArgumentOutOfRangeException(nameof(zoomFactor));
            Apply(graph, "Configure Event Graph Canvas", () =>
            {
                graph.category = category ?? string.Empty;
                graph.comments = comments ?? string.Empty;
                graph.translation = translation;
                graph.zoomFactor = zoomFactor;
            });
        }

        internal static void ConfigureIdentity(
            HostEventGraph graph,
            string authoringId,
            string contentRevision)
        {
            authoringId = RequireIdentity(authoringId, "authoringId");
            contentRevision = RequireIdentity(contentRevision, "contentRevision");
            Apply(graph, "Configure Event Graph Identity", () =>
                graph.ConfigureIdentity(authoringId, contentRevision),
                true,
                false);
        }

        internal static void ClearAuthoringContent(HostEventGraph graph)
        {
            Apply(graph, "Clear Event Graph Authoring Content", () =>
            {
                foreach (Connection connection in graph.GetAllConnections().ToArray())
                    graph.RemoveConnectionNative(connection, false);
                graph.ClearNativeNodes();
                foreach (Variable variable in graph.blackboard.variables.Values.ToArray())
                    graph.blackboard.RemoveVariable(variable.name);
                graph.canvasGroups = new List<NodeCanvas.Framework.CanvasGroup>();
                graph.category = string.Empty;
                graph.comments = string.Empty;
                graph.translation = Vector2.zero;
                graph.zoomFactor = 1f;
            });
        }

        internal static void BindVariableNode(
            HostEventGraph graph,
            ParameterVariableNode node,
            string variableId)
        {
            RequireNode(graph, node);
            if (node.parameter == null)
                throw new InvalidOperationException("Event graph variable node has no parameter.");
            variableId = RequireIdentity(variableId, "variableId");
            Variable variable = graph.blackboard.variables.Values.SingleOrDefault(value =>
                string.Equals(value.ID, variableId, StringComparison.Ordinal));
            if (variable == null)
                throw new InvalidOperationException(
                    $"Event graph variable '{variableId}' does not exist.");
            if (variable.varType != node.parameter.varType)
                throw new InvalidOperationException(
                    $"Event graph variable '{variableId}' type does not match node type.");
            Apply(graph, "Bind Event Graph Variable", () =>
                node.parameter.SetTargetVariable(graph.blackboard, variable));
        }

        internal static void ConfigureValueInput<T>(
            HostEventGraph graph,
            FlowNode node,
            string portId,
            T value)
        {
            RequireNode(graph, node);
            if (!EventGraphValueKinds.IsSupported(typeof(T)))
                throw new InvalidOperationException(
                    $"Event graph value type '{typeof(T).FullName}' is unsupported.");
            portId = RequireIdentity(portId, "portId");
            Port port = node.GetInputPort(portId);
            if (port is not ValueInput valueInput)
                throw new InvalidOperationException(
                    $"Event graph node '{node.UID}' has no value input '{portId}'.");
            if (valueInput.type != typeof(T))
                throw new InvalidOperationException(
                    $"Event graph value input '{portId}' requires '{valueInput.type.FullName}'.");
            if (valueInput.isConnected)
                throw new InvalidOperationException(
                    $"Event graph value input '{portId}' is already connected.");
            EventGraphValue.FromObject(value);
            Apply(graph, "Configure Event Graph Value", () =>
                valueInput.serializedValue = value);
        }

        internal static void ConfigureHostInput(
            HostEventGraph graph,
            Node node,
            string inputId)
        {
            RequireNode(graph, node);
            inputId = RequireIdentity(inputId, "inputId");
            Apply(graph, "Configure Event Graph Host Input", () =>
            {
                if (node is not EventGraphHostInputNodeMarker input)
                    throw new InvalidOperationException(
                        $"Node '{node.UID}' is not a host input node.");
                input.ConfigureInput(inputId);
                node.name = graph.GetHostInputDisplayName(inputId);
            });
        }

        internal static void ConfigureUpdateEvent(
            HostEventGraph graph,
            UpdateEvent node)
        {
            RequireNode(graph, node);
            Apply(graph, "Configure Event Graph Update", () =>
            {
                node.updateInterval.useBlackboard = false;
                node.updateInterval.value = 0f;
            });
        }

        internal static void ConfigureInstantSplit(
            HostEventGraph graph,
            Split node,
            int portCount)
        {
            RequireNode(graph, node);
            if (portCount < 2)
                throw new ArgumentOutOfRangeException(nameof(portCount));
            Apply(graph, "Configure Event Graph Split", () =>
            {
                node.mode = Split.Mode.Instant;
                node.ConfigurePortCount(portCount);
            });
        }

        internal static void ConfigureSequence(
            HostEventGraph graph,
            Sequence node,
            int portCount,
            int startIndex)
        {
            RequireNode(graph, node);
            if (portCount < 2)
                throw new ArgumentOutOfRangeException(nameof(portCount));
            if (startIndex < 0 || startIndex >= portCount)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            Apply(graph, "Configure Event Graph Sequence", () =>
            {
                node.ConfigurePortCount(portCount);
                node.current = startIndex;
            });
        }

        internal static void ConfigureAssignment<T>(
            HostEventGraph graph,
            SetVariable<T> node,
            AssignOp operation,
            bool perSecond)
        {
            RequireNode(graph, node);
            if (!EventGraphValueKinds.IsSupported(typeof(T)))
                throw new InvalidOperationException(
                    $"Event graph Set type '{typeof(T).FullName}' is unsupported.");
            if (typeof(T) == typeof(bool) || typeof(T) == typeof(Vector2) ||
                typeof(T) == typeof(Quaternion) || typeof(T).IsEnum)
            {
                if (operation != AssignOp.Set || perSecond)
                    throw new InvalidOperationException(
                        $"Event graph Set<{typeof(T).Name}> only supports AssignOp.Set.");
            }
            if (typeof(T) == typeof(int) && perSecond)
                throw new InvalidOperationException(
                    "Event graph Set<int> cannot use per-second time.");
            Apply(graph, "Configure Event Graph Set", () =>
            {
                node.operation = operation;
                node.perSecond = perSecond;
            });
        }

        internal static void ConfigureMacro(
            HostEventGraph graph,
            MacroNodeWrapper node,
            Macro macro)
        {
            RequireNode(graph, node);
            if (macro == null)
                throw new ArgumentNullException(nameof(macro));
            if (macro.usesExternalExecution)
                throw new InvalidOperationException(
                    "Event graph Macro cannot use external execution.");
            Apply(graph, "Configure Event Graph Macro", () => node.macro = macro);
        }

        internal static BinderConnection ConnectAuthoringPorts(
            HostEventGraph graph,
            string connectionIdentity,
            Node sourceNode,
            string sourcePortId,
            Node targetNode,
            string targetPortId)
        {
            RequireNode(graph, sourceNode);
            RequireNode(graph, targetNode);
            if (sourceNode is not FlowNode sourceFlowNode ||
                targetNode is not FlowNode targetFlowNode)
            {
                throw new InvalidOperationException(
                    "Event graph connections require FlowNode endpoints.");
            }
            Port source = sourceFlowNode.GetOutputPort(
                RequireIdentity(sourcePortId, "sourcePortId"));
            Port target = targetFlowNode.GetInputPort(
                RequireIdentity(targetPortId, "targetPortId"));
            return CreatePortConnection(
                graph,
                RequireIdentity(connectionIdentity, "connectionIdentity"),
                source,
                target);
        }

        static void CreateNode(
            HostEventGraph graph,
            Type type,
            Vector2 position,
            Port context,
            object dropInstance)
        {
            try
            {
                Type resolvedType = ResolveNodeType(type, context);
                FlowNode created = null;
                Apply(graph, "Create Event Graph Node", () =>
                {
                    created = (FlowNode)graph.AddNodeNative(resolvedType, position);
                    if (created == null)
                        throw new InvalidOperationException("Event graph node could not be created.");
                    if (dropInstance != null)
                        created.SetFirstPortOfTypeToInstance(dropInstance);
                    if (context == null)
                        return;
                    FlowNode prototype = (FlowNode)Activator.CreateInstance(resolvedType);
                    prototype.GatherPorts();
                    Port[] candidates = context.IsOutputPort()
                        ? prototype.GetInputFlowPorts().Cast<Port>()
                            .Concat(prototype.GetInputValuePorts()).ToArray()
                        : prototype.GetOutputFlowPorts().Cast<Port>()
                            .Concat(prototype.GetOutputValuePorts()).ToArray();
                    Port selected = candidates.FirstOrDefault(value =>
                        value.type == context.type &&
                        value.IsFlowPort() == context.IsFlowPort());
                    if (selected == null)
                        throw new InvalidOperationException("Event graph node has no compatible port.");
                    Port endpoint = context.IsInputPort()
                        ? context.parent.GetInputPort(context.ID)
                        : context.parent.GetOutputPort(context.ID);
                    Port createdPort = context.IsOutputPort()
                        ? created.GetInputPort(selected.ID)
                        : created.GetOutputPort(selected.ID);
                    Port source = context.IsOutputPort() ? endpoint : createdPort;
                    Port target = context.IsOutputPort() ? createdPort : endpoint;
                    if (!CanConnect(graph, source, target, out string reason))
                        throw new InvalidOperationException(reason);
                    graph.CreatePortConnectionNative(source, target);
                });
                GraphEditorUtility.activeElement = created;
            }
            catch (Exception error) when (
                error is InvalidOperationException ||
                error is ArgumentException)
            {
                GraphEditor.current?.ShowNotification(new GUIContent(error.Message));
            }
        }

        static Type ResolveNodeType(Type type, Port context)
        {
            if (type == null || !type.IsGenericTypeDefinition)
                return type;
            if (context != null && context.IsValuePort())
                return type.MakeGenericType(context.type);
            return type.MakeGenericType(type.GetFirstGenericParameterConstraintType());
        }

        static void CreateVariableNode(
            HostEventGraph graph,
            Variable variable,
            Vector2 position,
            bool writes)
        {
            try
            {
                FlowNode created = null;
                Apply(graph, writes ? "Create Event Graph Set" : "Create Event Graph Get", () =>
                {
                    Type generic = writes
                        ? typeof(SetVariable<>)
                        : typeof(GetVariable<>);
                    created = (FlowNode)graph.AddNodeNative(
                        generic.MakeGenericType(variable.varType),
                        position);
                    if (created is not ParameterVariableNode parameterNode)
                        throw new InvalidOperationException("Event graph variable node is invalid.");
                    parameterNode.parameter.SetTargetVariable(graph.blackboard, variable);
                });
                GraphEditorUtility.activeElement = created;
            }
            catch (Exception error) when (
                error is InvalidOperationException ||
                error is ArgumentException)
            {
                GraphEditor.current?.ShowNotification(new GUIContent(error.Message));
            }
        }

        static void RequireNode(HostEventGraph graph, Node node)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (node == null || node.graph != graph || !graph.allNodes.Contains(node))
                throw new InvalidOperationException(
                    "Event graph node does not belong to the current graph.");
        }

        static string RequireIdentity(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "Event graph authoring identity is missing.",
                    parameterName);
            return value.Trim();
        }

        static string RequireName(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "Event graph authoring name is missing.",
                    parameterName);
            return value.Trim();
        }

        static string DisplayNameFromAuthoringIdentity(string identity)
        {
            string leaf = identity.Substring(identity.LastIndexOf('.') + 1);
            string display = HumanizeIdentity(leaf);
            if (identity.Contains(".input.", StringComparison.Ordinal))
                return "Read " + display;
            if (identity.Contains(".calculate.", StringComparison.Ordinal))
                return "Calculate " + display;
            if (identity.Contains(".branch.", StringComparison.Ordinal))
                return "Branch " + display;
            if (identity.Contains(".history.get-", StringComparison.Ordinal))
                return "Get " + display;
            if (identity.Contains(".history.set-", StringComparison.Ordinal))
                return "Set " + display;
            if (identity.Contains(".set.", StringComparison.Ordinal))
                return "Set " + display;
            return display;
        }

        static string HumanizeIdentity(string value)
        {
            string[] words = value.Split(
                new[] { '-', '_', '.' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0)
                    continue;
                words[i] = char.ToUpperInvariant(words[i][0]) +
                           words[i].Substring(1);
            }
            return string.Join(" ", words);
        }

        static void EnsureFinite(Vector2 value, string parameterName)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        static void Apply(
            HostEventGraph graph,
            string title,
            Action mutation,
            bool recordUndo = true,
            bool advanceRevision = true)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (graph.isEditorReadOnly)
                throw new InvalidOperationException("Event graph is read-only.");
            Scope scope = s_Scopes.GetValue(graph, _ => new Scope());
            if (scope.Depth != 0)
            {
                mutation();
                return;
            }
            graph.SelfSerialize();
            int undoGroup = -1;
            if (recordUndo)
            {
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(title);
                Undo.RegisterCompleteObjectUndo(graph, title);
            }
            scope.Depth++;
            try
            {
                mutation();
                if (advanceRevision)
                    graph.AdvanceContentRevision();
                graph.UpdateNodeIDs(true);
                graph.SelfSerialize();
                EditorUtility.SetDirty(graph);
                if (recordUndo)
                    Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                if (recordUndo)
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.RevertAllDownToGroup(undoGroup);
                    graph.SelfDeserialize();
                }
                throw;
            }
            finally
            {
                scope.Depth--;
            }
        }
    }
}
#endif
