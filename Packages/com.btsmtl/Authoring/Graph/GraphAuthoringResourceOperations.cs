using System;

namespace BTSMTL.Authoring.Graph
{
    public enum GraphAuthoringReferenceSource
    {
        AdapterResource,
        AuthoringDocument
    }

    public sealed class GraphAuthoringExecutionScopeSelection
    {
        public string PreviewScope { get; set; }
        public string RuntimeScope { get; set; }
    }

    public sealed class GraphAuthoringAbilityRuntimeQuery
    {
        public string Adapter { get; set; }
        public string Ability { get; set; }
    }

    public sealed class GraphAuthoringAbilityRuntimeConfigurationRequest
    {
        public string Adapter { get; set; }
        public string Ability { get; set; }
        public string AssetPath { get; set; }
        public string ExpectedRevision { get; set; }
    }

    public sealed class GraphAuthoringAbilityRuntimeSnapshot
    {
        public string Adapter { get; set; }
        public string Ability { get; set; }
        public string AssetPath { get; set; }
        public bool Configured => AssetPath != null;
        public string Revision { get; set; }
        public GraphAuthoringDataTransaction Transaction { get; set; }
    }

    public sealed class GraphAuthoringResourceObjectQuery
    {
        public string Adapter { get; set; }
        public string Domain { get; set; }
        public string Context { get; set; }
        public string Asset { get; set; }
        public string ObjectType { get; set; }
    }

    public sealed class GraphAuthoringResourceObjectCandidate
    {
        public string Value { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Hierarchy { get; set; }
        public object Data { get; set; }
    }

    public sealed class GraphAuthoringResourceObjectResult
    {
        public bool Available { get; set; }
        public string Reason { get; set; }
        public string Revision { get; set; }
        public GraphAuthoringResourceObjectCandidate[] Items { get; set; }
    }

    public sealed class GraphAuthoringResourceChoice
    {
        public string Value { get; set; }
        public string Display { get; set; }
        public string Context { get; set; }
        public string Revision { get; set; }
        public string Conflict { get; set; }
    }

    [GraphAuthoringAnnotation]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
    public sealed class GraphAuthoringResourceAttribute : Attribute
    {
        public GraphAuthoringResourceAttribute(string domain, params Type[] objectTypes)
        { Domain = domain; ObjectTypes = objectTypes; }
        public string Domain { get; }
        public string Selection { get; set; }
        public string Profile { get; set; }
        public Type[] ObjectTypes { get; }
        public Type MappingInputType { get; set; }
        public GraphAuthoringReferenceSource Source { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class GraphAuthoringResourceRuleAttribute : Attribute
    {
        public GraphAuthoringResourceRuleAttribute(string memberName, string contextMember)
        { MemberName = memberName; ContextMember = contextMember; }

        public GraphAuthoringResourceRuleAttribute(string contextFieldId, Type contextType,
            Func<object, object, GraphAuthoringResourceAttribute> resolve)
        { ContextFieldId = contextFieldId; ContextType = contextType; Resolve = resolve; }

        public string MemberName { get; }
        public string ContextMember { get; }
        public string ContextFieldId { get; }
        public Type ContextType { get; }
        public Func<object, object, GraphAuthoringResourceAttribute> Resolve { get; }
    }

    public sealed class GraphAuthoringScopeQuery
    {
        public string Scope { get; set; }
        public string Adapter { get; set; }
    }

    public sealed class GraphAuthoringScopeSnapshot
    {
        public string Scope { get; set; }
        public string Adapter { get; set; }
        public string Context { get; set; }
        public string Revision { get; set; }
        public GraphAuthoringResourceDeclaration[] ResourceDeclarations { get; set; }
    }

    public sealed class GraphAuthoringProfileTarget
    {
        public string AssetToken { get; set; }
        public string ExpectedRevision { get; set; }
    }

    public sealed class GraphAuthoringProfileQuery
    {
        public string Group { get; set; }
        public string Adapter { get; set; }
        public string AssetToken { get; set; }
    }

#if NET8_0_OR_GREATER
    [System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(GraphAuthoringProfileAssetMappingInput), "asset")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(GraphAuthoringProfileInstanceMappingInput), "instance-resource")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(GraphAuthoringProfileCameraEffectMappingInput), "camera-effect")]
#endif
    public abstract partial class GraphAuthoringProfileMappingInput
    {
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public abstract string Kind { get; }
        [GraphAuthoringField(GraphAuthoringFieldValueKind.String, DisplayName = "映射名称", NonEmpty = true)]
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonInclude]
#endif
        public string Name;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.AssetReference, DisplayName = "资源对象", NonEmpty = true,
            PickerKind = "profile-object")]
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonInclude]
#endif
        public string Token;
    }

    public sealed partial class GraphAuthoringProfileAssetMappingInput : GraphAuthoringProfileMappingInput
    {
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public override string Kind => "asset";
    }

    public sealed partial class GraphAuthoringProfileInstanceMappingInput : GraphAuthoringProfileMappingInput
    {
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public override string Kind => "instance-resource";
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Integer, DisplayName = "实例容量", HasMinimum = true, Minimum = 1)]
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonInclude]
#endif
        public int InstanceCapacity;
    }

    public sealed partial class GraphAuthoringProfileCameraEffectMappingInput : GraphAuthoringProfileMappingInput
    {
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public override string Kind => "camera-effect";
        [GraphAuthoringField(GraphAuthoringFieldValueKind.String, DisplayName = "效果类别", NonEmpty = true,
            PickerKind = "camera-effect-kind")]
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonInclude]
#endif
        public string EffectKind;
    }

    public sealed class GraphAuthoringProfileMappingRequest
    {
        public string Group { get; set; }
        public string Adapter { get; set; }
        public string Context { get; set; }
        public string Domain { get; set; }
        public GraphAuthoringProfileMappingInput Input { get; set; }
        public GraphAuthoringProfileTarget Profile { get; set; }
    }

    public sealed class GraphAuthoringProfileCapacityRequest
    {
        public string Group { get; set; }
        public string Adapter { get; set; }
        public string Context { get; set; }
        public string Domain { get; set; }
        public string Value { get; set; }
        public int InstanceCapacity { get; set; }
        public GraphAuthoringProfileTarget Profile { get; set; }
    }

    public sealed class GraphAuthoringProfileConfigurationRequest
    {
        public string Group { get; set; }
        public string Adapter { get; set; }
        public GraphAuthoringProfileTarget Profile { get; set; }
        public string ConfigurationJson { get; set; }
    }

    public sealed class GraphAuthoringProfileResourceConfigurationRequest
    {
        public string Group { get; set; }
        public string Adapter { get; set; }
        public string Context { get; set; }
        public string Domain { get; set; }
        public string Value { get; set; }
        public GraphAuthoringProfileTarget Profile { get; set; }
        public string ConfigurationJson { get; set; }
    }

    public sealed class GraphAuthoringOwnerOperation
    {
        public string Command { get; set; }
        public string InputType { get; set; }
        public string ResultType { get; set; }
    }

    public sealed class GraphAuthoringResourceDeclaration
    {
        public string Domain { get; set; }
        public string Selection { get; set; }
        public string Profile { get; set; }
        public string ObjectType { get; set; }
        public string MappingInputType { get; set; }
        public GraphAuthoringReferenceSource Source { get; set; }
        public GraphAuthoringResourceAvailability Availability { get; set; }
    }

    public sealed class GraphAuthoringResourceAvailability
    {
        public bool Available { get; set; }
        public string Reason { get; set; }
    }

    public sealed class GraphAuthoringProfileResource
    {
        public string Domain { get; set; }
        public string Value { get; set; }
        public string Context { get; set; }
        public string Token { get; set; }
        public int? InstanceCapacity { get; set; }
        public string ConfigurationJson { get; set; }
    }

    public sealed class GraphAuthoringProfileSnapshot
    {
        public string Group { get; set; }
        public string Adapter { get; set; }
        public string AssetToken { get; set; }
        public string Domain { get; set; }
        public string Context { get; set; }
        public string Revision { get; set; }
        public string ConfigurationJson { get; set; }
        public GraphAuthoringResourceDeclaration[] ResourceDeclarations { get; set; }
        public GraphAuthoringProfileResource[] Resources { get; set; }
        public GraphAuthoringOwnerOperation[] Operations { get; set; }
    }

    public sealed class GraphAuthoringProfileOperationResult
    {
        public string Value { get; set; }
        public bool Reused { get; set; }
        public int? InstanceCapacity { get; set; }
        public GraphAuthoringDataTransaction Transaction { get; set; }
        public GraphAuthoringProfileSnapshot Profile { get; set; }
        public string RuntimeState => "pending";
    }

    public sealed class GraphAuthoringProfileReplayRequest
    {
        public string Group { get; set; }
        public string Adapter { get; set; }
        public string AssetToken { get; set; }
        public GraphAuthoringDataTransaction Transaction { get; set; }
    }

    public sealed class GraphAuthoringDataFile
    {
        public string Path { get; set; }
        public string Content { get; set; }
    }

    public sealed class GraphAuthoringDataChange
    {
        public string Path { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
    }

    public sealed class GraphAuthoringDataTransaction
    {
        public GraphAuthoringDataChange[] Changes { get; set; }
    }

    public sealed class GraphAuthoringDataReplayResult
    {
        public string Revision { get; set; }
        public GraphAuthoringDataFile[] Files { get; set; }
        public string RuntimeState => "pending";
    }
}
