using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

public enum WorkbenchDocumentSource { Workspace, Migrations }

public sealed record GraphEditPreviewRequest([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] WorkbenchDocumentSource Source, [property: JsonRequired] string RootPath,
    [property: JsonRequired] string RootRevision, [property: JsonRequired] string DefinitionsRevision,
    [property: JsonRequired] GraphEditBatchRequest[] Batches);

public sealed record GraphEditBatchRequest([property: JsonRequired] string DocumentId,
    [property: JsonRequired] GraphEditRequest[] Edits);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RenameGraphRequest), "rename-graph")]
[JsonDerivedType(typeof(SetGraphFieldRequest), "set-graph-field")]
[JsonDerivedType(typeof(RemoveGraphFieldRequest), "remove-graph-field")]
[JsonDerivedType(typeof(InsertNodeRequest), "insert-node")]
[JsonDerivedType(typeof(DeleteNodeRequest), "delete-node")]
[JsonDerivedType(typeof(RenameNodeRequest), "rename-node")]
[JsonDerivedType(typeof(SetNodeFieldRequest), "set-node-field")]
[JsonDerivedType(typeof(RemoveNodeFieldRequest), "remove-node-field")]
[JsonDerivedType(typeof(ReplaceDynamicPortsRequest), "replace-dynamic-ports")]
[JsonDerivedType(typeof(InsertEdgeRequest), "insert-edge")]
[JsonDerivedType(typeof(DeleteEdgeRequest), "delete-edge")]
[JsonDerivedType(typeof(ReorderEdgesRequest), "reorder-edges")]
[JsonDerivedType(typeof(SetEdgeOrderRequest), "set-edge-order")]
[JsonDerivedType(typeof(SetEdgeFieldRequest), "set-edge-field")]
[JsonDerivedType(typeof(RemoveEdgeFieldRequest), "remove-edge-field")]
[JsonDerivedType(typeof(InsertVariableRequest), "insert-variable")]
[JsonDerivedType(typeof(DeleteVariableRequest), "delete-variable")]
[JsonDerivedType(typeof(ConfigureVariableRequest), "configure-variable")]
public abstract record GraphEditRequest;

public sealed record RenameGraphRequest([property: JsonRequired] string DisplayName) : GraphEditRequest;
public sealed record SetGraphFieldRequest([property: JsonRequired] string FieldId,
    [property: JsonRequired] JsonElement Value) : GraphEditRequest;
public sealed record RemoveGraphFieldRequest([property: JsonRequired] string FieldId) : GraphEditRequest;
public sealed record InsertNodeRequest([property: JsonRequired] int Index,
    [property: JsonRequired] AuthoringNodeRequest Node) : GraphEditRequest;
public sealed record DeleteNodeRequest([property: JsonRequired] string NodeId) : GraphEditRequest;
public sealed record RenameNodeRequest([property: JsonRequired] string NodeId,
    [property: JsonRequired] string DisplayName) : GraphEditRequest;
public sealed record SetNodeFieldRequest([property: JsonRequired] string NodeId,
    [property: JsonRequired] string FieldId, [property: JsonRequired] JsonElement Value) : GraphEditRequest;
public sealed record RemoveNodeFieldRequest([property: JsonRequired] string NodeId,
    [property: JsonRequired] string FieldId) : GraphEditRequest;
public sealed record ReplaceDynamicPortsRequest([property: JsonRequired] string NodeId,
    [property: JsonRequired] AuthoringPortRequest[] Ports) : GraphEditRequest;
public sealed record InsertEdgeRequest([property: JsonRequired] int Index,
    [property: JsonRequired] AuthoringEdgeRequest Edge) : GraphEditRequest;
public sealed record DeleteEdgeRequest([property: JsonRequired] string EdgeId) : GraphEditRequest;
public sealed record ReorderEdgesRequest([property: JsonRequired] string SourceNodeId,
    [property: JsonRequired] string SourcePortId, [property: JsonRequired] string[] EdgeIds) : GraphEditRequest;
public sealed record SetEdgeOrderRequest([property: JsonRequired] string EdgeId,
    [property: JsonRequired] int Order) : GraphEditRequest;
public sealed record SetEdgeFieldRequest([property: JsonRequired] string EdgeId,
    [property: JsonRequired] string FieldId, [property: JsonRequired] JsonElement Value) : GraphEditRequest;
public sealed record RemoveEdgeFieldRequest([property: JsonRequired] string EdgeId,
    [property: JsonRequired] string FieldId) : GraphEditRequest;
public sealed record InsertVariableRequest([property: JsonRequired] int Index,
    [property: JsonRequired] AuthoringVariableRequest Variable) : GraphEditRequest;
public sealed record DeleteVariableRequest([property: JsonRequired] string VariableId) : GraphEditRequest;
public sealed record ConfigureVariableRequest([property: JsonRequired] string VariableId,
    [property: JsonRequired] string Key, [property: JsonRequired] string TypeId,
    [property: JsonRequired] Dictionary<string, JsonElement> Declaration,
    [property: JsonRequired] JsonElement DefaultValue) : GraphEditRequest;

public sealed record AuthoringNodeRequest([property: JsonRequired] string Id,
    [property: JsonRequired] string Capability, [property: JsonRequired] string DisplayName,
    [property: JsonRequired] Dictionary<string, JsonElement> Fields,
    [property: JsonRequired] AuthoringPortRequest[] DynamicPorts);
public sealed record AuthoringPortRequest([property: JsonRequired] string Id,
    [property: JsonRequired] string DisplayName, [property: JsonRequired] string ValueType,
    [property: JsonRequired] string Direction, [property: JsonRequired] string Capacity,
    [property: JsonRequired] string InterfacePortId);
public sealed record AuthoringEdgeRequest([property: JsonRequired] string Id,
    [property: JsonRequired] string SourceNode, [property: JsonRequired] string SourcePort,
    [property: JsonRequired] string TargetNode, [property: JsonRequired] string TargetPort,
    [property: JsonRequired] int Order, [property: JsonRequired] Dictionary<string, JsonElement> Fields);
public sealed record AuthoringVariableRequest([property: JsonRequired] string Id,
    [property: JsonRequired] string Key, [property: JsonRequired] string Type,
    [property: JsonRequired] Dictionary<string, JsonElement> Declaration,
    [property: JsonRequired] JsonElement DefaultValue);
