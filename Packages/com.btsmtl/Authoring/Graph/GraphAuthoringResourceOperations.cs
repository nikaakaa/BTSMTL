using System;

namespace BTSMTL.Authoring.Graph
{
    public sealed class GraphAuthoringExecutionScopeSelection
    {
        public GraphAuthoringResourceReference? PreviewScope { get; set; }
        public GraphAuthoringResourceReference? RuntimeScope { get; set; }
    }

    public sealed class GraphAuthoringScopeDeclarationRequest
    {
        public GraphAuthoringResourceReference Reference { get; set; }
        public string Adapter { get; set; }
        public string NativeDefinitionId { get; set; }
    }

    public sealed class GraphAuthoringAbilityRuntimeQuery
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string Ability { get; set; }
    }

    public sealed class GraphAuthoringAbilityRuntimeConfigurationRequest
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string Ability { get; set; }
        public string AssetPath { get; set; }
        public string ExpectedRevision { get; set; }
    }

    public sealed class GraphAuthoringAbilityRuntimeSnapshot
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string Ability { get; set; }
        public string AssetPath { get; set; }
        public bool Configured => AssetPath != null;
        public string Revision { get; set; }
        public GraphAuthoringDataTransaction Transaction { get; set; }
    }

    public sealed class GraphAuthoringResourceObjectQuery
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string AssetGuid { get; set; }
        public string KeyKind { get; set; }
        public string Domain { get; set; }
        public string ObjectType { get; set; }
        public string ProjectionToken => "resource-objects:" + AssetGuid + ":" + KeyKind + ":" + Domain + ":" + ObjectType;
    }

    public sealed class GraphAuthoringResourceObjectCandidate
    {
        public string Token { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Hierarchy { get; set; }
    }

    public sealed class GraphAuthoringResourceObjectsProjection
    {
        public string Kind => "resource-objects";
        public string Token => Query.ProjectionToken;
        public string Protocol => "authoring-resource-objects/1";
        public GraphAuthoringResourceObjectQuery Query { get; set; }
        public GraphAuthoringResourceObjectCandidate[] Objects { get; set; }
        public string SourceRevision { get; set; }
    }

    public sealed class GraphAuthoringResourceObjectResult
    {
        public bool Available { get; set; }
        public string Reason { get; set; }
        public string Revision { get; set; }
        public GraphAuthoringResourceObjectCandidate[] Items { get; set; }
    }

    [GraphAuthoringAnnotation]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class GraphAuthoringResourceAttribute : Attribute
    {
        public GraphAuthoringResourceAttribute(string keyKind, string registrationInput, params Type[] objectTypes)
        {
            KeyKind = keyKind;
            RegistrationInput = registrationInput;
            ObjectTypes = objectTypes;
        }

        public string KeyKind { get; }
        public string RegistrationInput { get; }
        public string ResourceDomain { get; set; }
        public Type[] ObjectTypes { get; }
        public string Owner => "Authoring";
        public string Command => "register-resource";
        public Type ReferenceType => typeof(GraphAuthoringResourceReference);
    }

#if NET8_0_OR_GREATER
    [System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(GraphAuthoringAssetRegistrationInput), "asset")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(GraphAuthoringInstanceResourceRegistrationInput), "instance-resource")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(GraphAuthoringCameraEffectRegistrationInput), "camera-effect")]
#endif
    public abstract class GraphAuthoringResourceRegistrationInput
    {
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public abstract string Kind { get; }
        public string Key { get; set; }
        public string Token { get; set; }
    }

    public sealed class GraphAuthoringAssetRegistrationInput : GraphAuthoringResourceRegistrationInput
    {
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public override string Kind => "asset";
    }

    public sealed class GraphAuthoringInstanceResourceRegistrationInput : GraphAuthoringResourceRegistrationInput
    {
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public override string Kind => "instance-resource";
        public int InstanceCapacity { get; set; }
    }

    public sealed class GraphAuthoringCameraEffectRegistrationInput : GraphAuthoringResourceRegistrationInput
    {
#if NET8_0_OR_GREATER
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public override string Kind => "camera-effect";
        public string EffectKind { get; set; }
    }

    public sealed class GraphAuthoringDataFile
    {
        public string Path { get; set; }
        public string Content { get; set; }
    }

    public abstract class GraphAuthoringResourceBindingRequest
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string Context { get; set; }
        public string KeyKind { get; set; }
        public GraphAuthoringResourceRegistrationInput Input { get; set; }
    }

    public sealed class GraphAuthoringResourceRegistrationRequest : GraphAuthoringResourceBindingRequest
    {
        public string File { get; set; }
        public string[] MemberPath { get; set; }
        public string ExpectedRevision { get; set; }
        public GraphAuthoringDataFile[] Files { get; set; }
        public string[] RemovedFiles { get; set; }
    }

    public sealed class GraphAuthoringScopeResourceRegistrationRequest : GraphAuthoringResourceBindingRequest
    {
        public GraphAuthoringResourceReference Scope { get; set; }
        public string ExpectedRevision { get; set; }
    }

    public sealed class GraphAuthoringScopeQuery
    {
        public GraphAuthoringResourceReference Scope { get; set; }
        public string Adapter { get; set; }
    }

    public sealed class GraphAuthoringScopeSnapshot
    {
        public GraphAuthoringResourceReference Scope { get; set; }
        public string Adapter { get; set; }
        public string Revision { get; set; }
        public GraphAuthoringResourceDeclaration[] ResourceDeclarations { get; set; }
    }

    public sealed class GraphAuthoringScopeReplayRequest
    {
        public GraphAuthoringResourceReference Scope { get; set; }
        public string Adapter { get; set; }
        public GraphAuthoringDataTransaction Transaction { get; set; }
    }

    public sealed class GraphAuthoringProfileTarget
    {
        public string AssetToken { get; set; }
        public string ExpectedRevision { get; set; }
    }

    public sealed class GraphAuthoringProfileQuery
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string AssetToken { get; set; }
    }

    public sealed class GraphAuthoringProfileResourceRegistrationRequest : GraphAuthoringResourceBindingRequest
    {
        public GraphAuthoringProfileTarget Profile { get; set; }
    }

    public sealed class GraphAuthoringProfileCapacityRequest
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string Context { get; set; }
        public string KeyKind { get; set; }
        public string Key { get; set; }
        public int InstanceCapacity { get; set; }
        public GraphAuthoringProfileTarget Profile { get; set; }
    }

    public sealed class GraphAuthoringProfileConfigurationRequest
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public GraphAuthoringProfileTarget Profile { get; set; }
        public string ConfigurationJson { get; set; }
    }

    public sealed class GraphAuthoringProfileResourceConfigurationRequest
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string Context { get; set; }
        public string KeyKind { get; set; }
        public string Key { get; set; }
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
        public string KeyKind { get; set; }
        public string ReferenceKind { get; set; }
        public string Domain { get; set; }
        public string RegistrationInput { get; set; }
        public string InputType { get; set; }
        public string ObjectType { get; set; }
        public GraphAuthoringResourceAvailability Availability { get; set; }
    }

    public sealed class GraphAuthoringResourceAvailability
    {
        public bool Available { get; set; }
        public string Reason { get; set; }
    }

    public sealed class GraphAuthoringProfileResource
    {
        public GraphAuthoringResourceReference Reference { get; set; }
        public string Token { get; set; }
        public int? InstanceCapacity { get; set; }
        public string ConfigurationJson { get; set; }
    }

    public sealed class GraphAuthoringProfileSnapshot
    {
        public string Package { get; set; }
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
        public GraphAuthoringResourceReference? Reference { get; set; }
        public bool Reused { get; set; }
        public int? InstanceCapacity { get; set; }
        public GraphAuthoringDataTransaction Transaction { get; set; }
        public GraphAuthoringProfileSnapshot Profile { get; set; }
        public string RuntimeState => "pending";
    }

    public sealed class GraphAuthoringProfileReplayRequest
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string AssetToken { get; set; }
        public GraphAuthoringDataTransaction Transaction { get; set; }
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

    public sealed class GraphAuthoringResourceRegistrationResult
    {
        public GraphAuthoringResourceReference Reference { get; set; }
        public bool Reused { get; set; }
        public int? InstanceCapacity { get; set; }
        public GraphAuthoringDataTransaction Transaction { get; set; }
        public string Revision { get; set; }
        public GraphAuthoringDataFile[] Files { get; set; }
        public string RuntimeState => "pending";
    }

    public sealed class GraphAuthoringDataReplayResult
    {
        public string Revision { get; set; }
        public GraphAuthoringDataFile[] Files { get; set; }
        public string RuntimeState => "pending";
    }
}
