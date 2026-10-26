using System;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public sealed class AuthoringGraphDocument
    {
        public AuthoringGraphDocument(GraphAuthoringElementId id, GraphAuthoringDocumentRoleId roleId,
            AuthoringContentReference owner, string displayName, string definitionVersion,
            ReadOnlyMemory<AuthoringNamedValue> fields, ReadOnlyMemory<AuthoringNodeDocument> nodes,
            ReadOnlyMemory<AuthoringEdgeDocument> edges, ReadOnlyMemory<AuthoringVariableDocument> variables)
        {
            Id = id;
            RoleId = roleId;
            Owner = owner;
            DisplayName = displayName;
            DefinitionVersion = definitionVersion;
            Fields = fields;
            Nodes = nodes;
            Edges = edges;
            Variables = variables;
        }

        public GraphAuthoringElementId Id { get; }
        public GraphAuthoringDocumentRoleId RoleId { get; }
        public AuthoringContentReference Owner { get; }
        public string DisplayName { get; }
        public string DefinitionVersion { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
        public ReadOnlyMemory<AuthoringNodeDocument> Nodes { get; }
        public ReadOnlyMemory<AuthoringEdgeDocument> Edges { get; }
        public ReadOnlyMemory<AuthoringVariableDocument> Variables { get; }
    }

    public sealed class AuthoringNodeDocument
    {
        public AuthoringNodeDocument(GraphAuthoringElementId id, GraphAuthoringCapabilityId capabilityId,
            string displayName, ReadOnlyMemory<AuthoringNamedValue> fields,
            ReadOnlyMemory<AuthoringPortDocument> dynamicPorts)
        {
            Id = id;
            CapabilityId = capabilityId;
            DisplayName = displayName;
            Fields = fields;
            DynamicPorts = dynamicPorts;
        }

        public GraphAuthoringElementId Id { get; }
        public GraphAuthoringCapabilityId CapabilityId { get; }
        public string DisplayName { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
        public ReadOnlyMemory<AuthoringPortDocument> DynamicPorts { get; }
    }

    public readonly struct AuthoringPortDocument
    {
        public AuthoringPortDocument(GraphAuthoringPortId id, string displayName, string valueTypeId,
            GraphAuthoringPortDirection direction, GraphAuthoringPortCapacity capacity, string interfacePortId)
        {
            Id = id;
            DisplayName = displayName;
            ValueTypeId = valueTypeId;
            Direction = direction;
            Capacity = capacity;
            InterfacePortId = interfacePortId;
        }

        public GraphAuthoringPortId Id { get; }
        public string DisplayName { get; }
        public string ValueTypeId { get; }
        public GraphAuthoringPortDirection Direction { get; }
        public GraphAuthoringPortCapacity Capacity { get; }
        public string InterfacePortId { get; }
    }

    public readonly struct AuthoringEdgeDocument
    {
        public AuthoringEdgeDocument(GraphAuthoringElementId id, GraphAuthoringElementId sourceNodeId,
            GraphAuthoringPortId sourcePortId, GraphAuthoringElementId targetNodeId,
            GraphAuthoringPortId targetPortId, int order, ReadOnlyMemory<AuthoringNamedValue> fields)
        {
            Id = id;
            SourceNodeId = sourceNodeId;
            SourcePortId = sourcePortId;
            TargetNodeId = targetNodeId;
            TargetPortId = targetPortId;
            Order = order;
            Fields = fields;
        }

        public GraphAuthoringElementId Id { get; }
        public GraphAuthoringElementId SourceNodeId { get; }
        public GraphAuthoringPortId SourcePortId { get; }
        public GraphAuthoringElementId TargetNodeId { get; }
        public GraphAuthoringPortId TargetPortId { get; }
        public int Order { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
    }

    public sealed class AuthoringVariableDocument
    {
        public AuthoringVariableDocument(GraphAuthoringElementId id, string key, string typeId,
            ReadOnlyMemory<AuthoringNamedValue> declaration, AuthoringValue defaultValue)
        {
            Id = id;
            Key = key;
            TypeId = typeId;
            Declaration = declaration;
            DefaultValue = defaultValue;
        }

        public GraphAuthoringElementId Id { get; }
        public string Key { get; }
        public string TypeId { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Declaration { get; }
        public AuthoringValue DefaultValue { get; }
    }
}
