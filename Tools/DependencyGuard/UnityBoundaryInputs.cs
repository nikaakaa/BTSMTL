namespace Btsmtl.DependencyGuard;

sealed class UnityBoundaryBatch
{
    public required int Version { get; init; }
    public required string RulesSHA256 { get; init; }
    public required string BatchId { get; init; }
    public required string ClientRoot { get; init; }
    public required BoundaryFile[] Files { get; init; }
    public required UnityCompilationProof[] Compilations { get; init; }
    public required OfficialInstallation[] Installations { get; init; }
    public required OfficialGenerationProof[] Generations { get; init; }
}

sealed record BoundaryFile(string Path, string SHA256, string Kind);

sealed class UnityCompilationProof
{
    public required string AssemblyDefinition { get; init; }
    public required string Project { get; init; }
    public required string ResponseFile { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string Output { get; init; }
    public required string Receipt { get; init; }
    public required string CompilerTrace { get; init; }
    public required string[] GeneratorOutputs { get; init; }
}

sealed class CompilationExecutionReceipt
{
    public required string BatchId { get; init; }
    public required int ExitCode { get; init; }
    public required Dictionary<string, string> Inputs { get; init; }
    public required Dictionary<string, string> Outputs { get; init; }
}

sealed class UnityCompilerTrace
{
    public required string BatchId { get; init; }
    public required string OriginId { get; init; }
    public required string AssemblyDefinition { get; init; }
    public required string ResponseFile { get; init; }
    public required string[] Sources { get; init; }
    public required string[] References { get; init; }
    public required string ReferencePolicyFacts { get; init; }
    public required string PolicyCaptureSource { get; init; }
    public required AutomaticReferencePolicy[] AutomaticReferencePolicies { get; init; }
}

sealed record AutomaticReferencePolicy(string Reference, string Kind);

sealed class AutomaticReferencePolicyProductionRequest
{
    public required int Version { get; init; }
    public required string BatchId { get; init; }
    public required string ClientRoot { get; init; }
    public required string RulesSHA256 { get; init; }
    public required BoundaryFile[] Files { get; init; }
    public required OfficialInstallation[] Installations { get; init; }
    public required AutomaticReferencePolicyProductionCompilation[] Compilations { get; init; }
}

sealed class AutomaticReferencePolicyProductionCompilation
{
    public required string AssemblyDefinition { get; init; }
    public required string ResponseFile { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string ReferencePolicyFacts { get; init; }
    public required string PolicyCaptureSource { get; init; }
    public required string OriginId { get; init; }
    public required string Receipt { get; init; }
}

sealed record AutomaticReferencePolicyProduction(string AssemblyDefinition, string ResponseFile,
    string[] References, AutomaticReferencePolicy[] AutomaticReferencePolicies);

sealed class UnityReferencePolicyFacts
{
    public required int Version { get; init; }
    public required string BatchId { get; init; }
    public required string CapturePhase { get; init; }
    public required string ProducerPath { get; init; }
    public required string ProducerModuleVersionId { get; init; }
    public required string ProviderType { get; init; }
    public required UnityReferencePolicySettings Settings { get; init; }
    public required UnitySelectedPackage[] SelectedPackages { get; init; }
    public required UnityReferencePolicyScript[] ScriptAssemblies { get; init; }
    public required UnityReferencePolicyTarget[] TargetAssemblies { get; init; }
    public required UnityReferencePolicyBinary[]? EditorPrecompiledAssemblies { get; init; }
    public required UnityReferencePolicyBinary[]? UnityAssemblies { get; init; }
    public required UnityAutoReferencedPackageFacts AutoReferencedPackages { get; init; }
    public required UnityTestRunnerFacts TestRunner { get; init; }
    public required UnityCodeGenFacts CodeGen { get; init; }
}

sealed record UnityReferencePolicySettings(bool BuildingForEditor, int BuildTarget, int BuildTargetGroup,
    int CompilationOptions, string ProjectDirectory, string[]? ExtraGeneralDefines);
sealed record UnitySelectedPackage(string Name, string Version, string PackageId, string Source, string AssetPath, string ResolvedPath);
sealed record UnityReferencePolicyScript(string Filename, string? AsmDefPath, int Flags, string[] References,
    string[] ScriptAssemblyReferences, string[]? ResponseFiles, string[]? AdditionalCompilerArguments);
sealed record UnityReferencePolicyTarget(string Filename, string AsmDefPath, int Flags,
    string[] ExplicitPrecompiledReferences, string[] References);
sealed record UnityReferencePolicyBinary(string? Key, string Path, int Flags, bool Redirected);
sealed record UnityAutoReferencedPackageFacts(string[] RuntimeAssemblyNames, string[] EditorAssemblyNames, string[] IgnoreAssemblies);
sealed record UnityTestRunnerFacts(string NunitAssemblyName, string EditorTestRunnerAssemblyName,
    string EngineTestRunnerAssemblyName, bool PlayModeTestRunnerEnabled);
sealed record UnityCodeGenFacts(string Prefix, string CompilerSuffix, string CodeGenSuffix);

sealed record OfficialInstallation(string OriginId, string Producer, string VersionFile, string RuntimeAssembly);
sealed record OfficialGenerationProof(string OriginId, string Receipt);
sealed record ActualBinary(string Path, string Name, string Identity, string[] References);
sealed record ActualSourceBinding(string Path, int Start, int Length, int Line, string Type,
    string Assembly, string AssemblyIdentity, string Namespace, bool AttributeUse, string? OfficialRule);
sealed record OfficialMetadataUse(string Path, int Start, int Length, string Type, string Rule,
    string Origin, string Version, string ProducerSHA256, string Receipt);
sealed record AutomaticCompilationCandidate(string AssemblyDefinition, string Project, string ResponseFile,
    string Reference, string Assembly, string Origin, string Version, string Output,
    bool Declared, bool SourceUsed, bool OutputClosureUsed, bool InputOnly);
sealed record ActualCompilationMatch(string AssemblyDefinition, string Project, string ResponseFile,
    string Output, string Receipt, string BatchId, int SourceCount, int ReferenceCount, int ClosureCount);

sealed class ActualUnityCompilation(Module owner, UnityCompilationProof proof,
    HashSet<string> sourcePaths, ActualBinary[] references,
    ActualBinary[] closure, HashSet<string> automaticReferences, string unityDataDirectory)
{
    public Module Owner { get; } = owner;
    public UnityCompilationProof Proof { get; } = proof;
    public HashSet<string> SourcePaths { get; } = sourcePaths;
    public ActualBinary[] References { get; } = references;
    public ActualBinary[] Closure { get; } = closure;
    public HashSet<string> AutomaticReferences { get; } = automaticReferences;
    public string UnityDataDirectory { get; } = unityDataDirectory;
    public HashSet<string> UsedAssemblies { get; } = new(StringComparer.Ordinal);
    public HashSet<string> InputOnlyAssemblies { get; } = new(StringComparer.Ordinal);
    public HashSet<string> DeclaredReferencePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> OfficialEngineAssemblies { get; } = new(StringComparer.Ordinal);
    public List<ActualSourceBinding> SourceBindings { get; } = [];
}
