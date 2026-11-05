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

    [GraphAuthoringAnnotation]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class GraphAuthoringResourceAttribute : Attribute
    {
        public GraphAuthoringResourceAttribute(string keyKind, string registrationInput)
        {
            KeyKind = keyKind;
            RegistrationInput = registrationInput;
        }

        public string KeyKind { get; }
        public string RegistrationInput { get; }
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

    public sealed class GraphAuthoringResourceRegistrationRequest
    {
        public string Package { get; set; }
        public string Adapter { get; set; }
        public string Context { get; set; }
        public string KeyKind { get; set; }
        public string File { get; set; }
        public string[] MemberPath { get; set; }
        public GraphAuthoringResourceRegistrationInput Input { get; set; }
        public string ExpectedRevision { get; set; }
        public GraphAuthoringDataFile[] Files { get; set; }
        public string[] RemovedFiles { get; set; }
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
