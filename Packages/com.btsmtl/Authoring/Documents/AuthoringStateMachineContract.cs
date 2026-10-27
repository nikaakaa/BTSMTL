using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public enum AuthoringStateKind { State, Entry, Any, Exit }

    public readonly struct AuthoringStateAnchor
    {
        public AuthoringStateAnchor(AuthoringStateKind kind, string name, string identity)
        { Kind = kind; Name = name; Identity = identity; }
        public AuthoringStateKind Kind { get; }
        public string Name { get; }
        public string Identity { get; }
    }

    public sealed class AuthoringStateMachineDefinition
    {
        public AuthoringStateMachineDefinition(IReadOnlyList<AuthoringStateAnchor> anchors,
            GraphAuthoringDocumentRoleId bodyRole, GraphAuthoringDocumentRoleId conditionRole)
        { Anchors = anchors; BodyRole = bodyRole; ConditionRole = conditionRole; }
        public IReadOnlyList<AuthoringStateAnchor> Anchors { get; }
        public GraphAuthoringDocumentRoleId BodyRole { get; }
        public GraphAuthoringDocumentRoleId ConditionRole { get; }
    }

    public readonly struct AuthoringState
    {
        public AuthoringState(string identity, AuthoringStateKind kind, bool hasBody)
        { Identity = identity; Kind = kind; HasBody = hasBody; }
        public string Identity { get; }
        public AuthoringStateKind Kind { get; }
        public bool HasBody { get; }
    }

    public readonly struct AuthoringStateTransition
    {
        public AuthoringStateTransition(string identity, string source, string target, int order,
            GraphAuthoringDocumentRoleId conditionRole)
        { Identity = identity; Source = source; Target = target; Order = order; ConditionRole = conditionRole; }
        public string Identity { get; }
        public string Source { get; }
        public string Target { get; }
        public int Order { get; }
        public GraphAuthoringDocumentRoleId ConditionRole { get; }
    }

    public static class AuthoringStateMachineContract
    {
        public static void ValidateDocument(AuthoringGraphDocument machine,
            IReadOnlyDictionary<GraphAuthoringCapabilityId, AuthoringStateKind> kinds,
            GraphAuthoringFieldId bodyField, GraphAuthoringFieldId conditionField,
            Func<AuthoringContentReference, AuthoringGraphDocument> resolve,
            AuthoringStateMachineDefinition definition, bool requireComplete)
        {
            var states = new List<AuthoringState>();
            foreach (AuthoringNodeDocument node in machine.Nodes.Span)
            {
                AuthoringStateKind kind = kinds[node.CapabilityId];
                bool hasBody = false;
                if (kind == AuthoringStateKind.State)
                {
                    AuthoringValue body = node.Fields.ToArray().Single(field => field.FieldId.Equals(bodyField)).Value;
                    if (body is AuthoringReferenceValue reference)
                    {
                        if (!resolve(reference.Target).RoleId.Equals(definition.BodyRole))
                            throw new InvalidOperationException($"原生状态 {node.Id} 的主体图不是StateBody。");
                        hasBody = true;
                    }
                    else if (body is not AuthoringNullValue)
                        throw new ArgumentException($"原生状态 {node.Id} 的StateBody必须是正式图引用。");
                }
                states.Add(new AuthoringState(node.Id.Value, kind, hasBody));
            }
            var transitions = new List<AuthoringStateTransition>();
            foreach (AuthoringEdgeDocument edge in machine.Edges.Span)
            {
                AuthoringValue condition = edge.Fields.ToArray().Single(field => field.FieldId.Equals(conditionField)).Value;
                GraphAuthoringDocumentRoleId conditionRole = default;
                if (condition is AuthoringReferenceValue reference)
                    conditionRole = resolve(reference.Target).RoleId;
                else if (condition is not AuthoringNullValue)
                    throw new ArgumentException($"原生转移 {edge.Id} 的条件必须是正式图引用。");
                transitions.Add(new AuthoringStateTransition(edge.Id.Value, edge.SourceNodeId.Value,
                    edge.TargetNodeId.Value, edge.Order, conditionRole));
            }
            Validate(machine.Id.Value, machine.Owner.DocumentId.Value, machine.Owner.ElementId.Value,
                definition.Anchors.Single(anchor => anchor.Kind == AuthoringStateKind.Entry).Identity,
                states, transitions, definition, requireComplete);
        }

        public static void RequireTransitionEndpoints(AuthoringStateKind source, AuthoringStateKind target)
        {
            if (target == AuthoringStateKind.Entry || target == AuthoringStateKind.Any || source == AuthoringStateKind.Exit)
                throw new InvalidOperationException("技能FSM转移必须连接同一状态机中的State到State或Exit。");
        }

        public static void Validate(string identity, string ownerGraphId, string ownerNodeId, string primeNodeId,
            IReadOnlyList<AuthoringState> states, IReadOnlyList<AuthoringStateTransition> transitions,
            AuthoringStateMachineDefinition definition, bool requireComplete)
        {
            if (string.IsNullOrWhiteSpace(identity))
                throw new InvalidOperationException("原生状态机缺少稳定身份。");
            if (string.IsNullOrWhiteSpace(ownerGraphId) || string.IsNullOrWhiteSpace(ownerNodeId))
                throw new InvalidOperationException($"原生状态机 {identity} 缺少唯一调用owner。");
            if (states.Select(state => state.Identity).Distinct(StringComparer.Ordinal).Count() != states.Count)
                throw new InvalidOperationException($"原生状态机 {identity} 的State identity重复。");
            var indexed = states.ToDictionary(state => state.Identity, StringComparer.Ordinal);
            foreach (AuthoringStateAnchor anchor in definition.Anchors)
            {
                AuthoringState[] declared = states.Where(state => state.Kind == anchor.Kind).ToArray();
                if ((requireComplete && declared.Length != 1) || (!requireComplete && declared.Length > 1))
                    throw new InvalidOperationException($"原生状态机的 {anchor.Name} 锚点数量错误：{declared.Length}。");
                if (requireComplete && declared[0].Identity != anchor.Identity)
                    throw new InvalidOperationException($"原生状态机的 {anchor.Name} 锚点必须使用固定身份 '{anchor.Identity}'。");
            }
            if (requireComplete && (!indexed.TryGetValue(primeNodeId, out AuthoringState prime) ||
                prime.Kind != AuthoringStateKind.Entry))
                throw new InvalidOperationException($"原生状态机 {identity} 的Prime必须是Entry。");
            var orders = new HashSet<(string Source, int Order)>();
            foreach (AuthoringStateTransition transition in transitions)
            {
                if (!indexed.TryGetValue(transition.Source, out AuthoringState source) ||
                    !indexed.TryGetValue(transition.Target, out AuthoringState target))
                    throw new InvalidOperationException($"原生状态机 {identity} 的Connection端点不属于Skill FSM。");
                RequireTransitionEndpoints(source.Kind, target.Kind);
                if (!orders.Add((source.Identity, transition.Order)))
                    throw new InvalidOperationException($"原生状态 {source.Identity} 的转移order重复。");
                if (transition.ConditionRole.IsValid && !transition.ConditionRole.Equals(definition.ConditionRole))
                    throw new InvalidOperationException($"原生转移 {transition.Identity} 的条件图不是ConditionRule。");
                if (source.Kind == AuthoringStateKind.Any && !transition.ConditionRole.IsValid)
                    throw new InvalidOperationException($"原生Any转移 {transition.Identity} 必须有条件图。");
            }
            if (requireComplete)
                foreach (AuthoringState state in states)
                    if (state.Kind == AuthoringStateKind.State && !state.HasBody)
                        throw new InvalidOperationException($"原生状态 {state.Identity} 的StateBody引用不完整。");
        }
    }
}
