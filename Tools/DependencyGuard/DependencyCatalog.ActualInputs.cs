using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Btsmtl.DependencyGuard;

sealed partial class DependencyCatalog
{
    readonly Dictionary<string, BoundaryFile> boundaryFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> boundaryBytes = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ActualBinary> actualBinaries = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ActualBinary> binaryIdentities = new(StringComparer.Ordinal);
    readonly Dictionary<string, ActualUnityCompilation> actualUnityCompilations = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (OfficialProducerRule Rule, OfficialInstallation Installation)> officialInstallations = new(StringComparer.Ordinal);
    readonly Dictionary<string, string[]> officialRuntimeEngineDependencies = new(StringComparer.Ordinal);
    readonly Dictionary<string, (OfficialProducerRule Rule, string Receipt)> officialGeneratedSources = new(StringComparer.OrdinalIgnoreCase);
    UnityBoundaryBatch? unityBatch;
    public string? UnityEvidencePath { get; set; }
    public string RulesSHA256 { get; set; } = "";
    public HashSet<string> GuardExecutionInputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ActualCompilationMatch> ActualCompilations { get; } = [];
    public List<AutomaticCompilationCandidate> AutomaticCompilationCandidates { get; } = [];
    public List<OfficialMetadataUse> VerifiedOfficialMetadata { get; } = [];

    public void LoadActualUnityInputs()
    {
        if (clientRoot is null) return;
        if (!File.Exists(UnityEvidencePath))
            throw new InvalidDataException($"完整 Unity 检查缺少本批正式编译证据：{UnityEvidencePath}。");
        unityBatch = JsonSerializer.Deserialize<UnityBoundaryBatch>(ReadText(UnityEvidencePath!),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, RespectRequiredConstructorParameters = true,
                RespectNullableAnnotations = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ??
            throw new InvalidDataException("正式编译证据必须是完整对象。");
        if (unityBatch.Version != 2 || unityBatch.BatchId.Length == 0 || unityBatch.RulesSHA256 != RulesSHA256 ||
            !Path.GetFullPath(unityBatch.ClientRoot).Equals(clientRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("编译证据版本、批次、当前规则身份或目标 Client 不匹配。");
        foreach (BoundaryFile file in unityBatch.Files)
        {
            string path = Path.GetFullPath(file.Path);
            if (!boundaryFiles.TryAdd(path, file))
                throw new InvalidDataException($"本批输入重复：{path}。");
            byte[] bytes = ReadBytes(path);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.SHA256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"本批输入身份已变化：{path}。");
            boundaryBytes.Add(path, bytes);
        }
        foreach (BoundaryFile file in unityBatch.Files.Where(file => file.Kind == "assembly"))
        {
            ActualBinary binary = ReadActualBinary(Path.GetFullPath(file.Path));
            if (binaryIdentities.TryGetValue(binary.Identity, out ActualBinary? prior))
            {
                if (!boundaryFiles[prior.Path].SHA256.Equals(file.SHA256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"实际依赖身份有不同字节的多个候选：{binary.Identity}；{prior.Path}；{file.Path}。");
            }
            else binaryIdentities.Add(binary.Identity, binary);
        }
        LoadOfficialInstallations();
        foreach (UnityCompilationProof proof in unityBatch.Compilations)
            LoadActualCompilation(proof);
    }

    public void DiscardActualUnityInputs()
    {
        unityBatch = null;
        boundaryFiles.Clear();
        boundaryBytes.Clear();
        actualBinaries.Clear();
        binaryIdentities.Clear();
        actualUnityCompilations.Clear();
        officialInstallations.Clear();
        officialRuntimeEngineDependencies.Clear();
        officialGeneratedSources.Clear();
        referencePolicyFacts.Clear();
        selectedPackages.Clear();
        ActualCompilations.Clear();
        AutomaticCompilationCandidates.Clear();
        VerifiedOfficialMetadata.Clear();
    }

    byte[] ActualBytes(string path) => boundaryBytes.TryGetValue(Path.GetFullPath(path), out byte[]? bytes) ? bytes :
        throw new InvalidDataException($"正式证据没有捕获实际输入：{path}。");

    T ActualJson<T>(string path) where T : class => JsonSerializer.Deserialize<T>(ActualBytes(path),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
            UnmappedMemberHandling = typeof(T) == typeof(UnityCompilerTrace) || typeof(T) == typeof(UnityReferencePolicyFacts)
                ? System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow : System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip }) ??
        throw new InvalidDataException($"正式证据必须是完整对象：{path}。");

    CompilationExecutionReceipt ExecutionReceipt(string path)
    {
        var receipt = ActualJson<CompilationExecutionReceipt>(path);
        if (receipt.BatchId != unityBatch!.BatchId || receipt.ExitCode != 0)
            throw new InvalidDataException($"没有本批成功执行的正式回执：{path}。");
        foreach ((string inputPath, string hash) in receipt.Inputs.Concat(receipt.Outputs))
        {
            ActualBytes(inputPath);
            if (!boundaryFiles[Path.GetFullPath(inputPath)].SHA256.Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"实际执行时的文件身份与本批快照不一致：{inputPath}。");
        }
        return receipt;
    }

    void LoadOfficialInstallations()
    {
        foreach (OfficialInstallation installation in unityBatch!.Installations)
        {
            OfficialProducerRule[] matches = rules.UnityActualBoundary.OfficialProducers.Where(rule => rule.Id == installation.OriginId).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"没有唯一已核定的官方生产来源：{installation.OriginId}。");
            OfficialProducerRule rule = matches[0];
            foreach (string path in new[] { installation.Producer, installation.VersionFile, installation.RuntimeAssembly }) ActualBytes(path);
            if (!boundaryFiles[Path.GetFullPath(installation.Producer)].SHA256.Equals(rule.ProducerSHA256, StringComparison.OrdinalIgnoreCase) ||
                !boundaryFiles[Path.GetFullPath(installation.VersionFile)].SHA256.Equals(rule.VersionFileSHA256, StringComparison.OrdinalIgnoreCase) ||
                !boundaryFiles[Path.GetFullPath(installation.RuntimeAssembly)].SHA256.Equals(rule.RuntimeSHA256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"官方来源、安装版本或原始运行程序集身份不匹配：{rule.Origin} {rule.Version}。");
            officialInstallations.Add(rule.Id, (rule, installation));
            if (rule.AssetBaseTypes.Length != 0)
                officialRuntimeEngineDependencies.Add(rule.Id, ActualClosure(ReadActualBinary(installation.RuntimeAssembly))
                    .Where(binary => BinaryReferenceKind(binary.Path, binary.Name) == "UnityEngineLibrary")
                    .Select(binary => binary.Identity).ToArray());
        }
        foreach (OfficialGenerationProof generation in unityBatch.Generations)
        {
            if (!officialInstallations.TryGetValue(generation.OriginId, out var origin))
                throw new InvalidDataException($"生成回执缺少已核定的安装来源：{generation.OriginId}。");
            if (origin.Rule.Kind != "QuantumGenerator")
                throw new InvalidDataException($"生成来源种类不匹配：{generation.OriginId}。");
            CompilationExecutionReceipt receipt = ExecutionReceipt(generation.Receipt);
            RequireReceiptInput(receipt, origin.Installation.Producer);
            RequireReceiptInput(receipt, origin.Installation.VersionFile);
            foreach (string output in receipt.Outputs.Keys.Where(output => output.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                officialGeneratedSources.Add(Path.GetFullPath(output), (origin.Rule, generation.Receipt));
        }
    }

    static void RequireReceiptInput(CompilationExecutionReceipt receipt, string path)
    {
        if (!receipt.Inputs.Keys.Any(input => Path.GetFullPath(input).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"执行回执未包含实际输入：{path}。");
    }

    IEnumerable<string> ResponseArguments(string response, string directory, CompilationExecutionReceipt? receipt = null)
    {
        if (receipt is not null) RequireReceiptInput(receipt, response);
        string text = Encoding.UTF8.GetString(ActualBytes(response)).TrimStart('\uFEFF');
        return ExpandResponseArguments(CommandLineParser.SplitCommandLineIntoArguments(text, true), directory, receipt);
    }

    IEnumerable<string> ExpandResponseArguments(IEnumerable<string> arguments, string directory, CompilationExecutionReceipt? receipt = null)
    {
        foreach (string argument in arguments)
            if (argument.StartsWith('@'))
                foreach (string nested in ResponseArguments(UnityPolicyPath(argument[1..].Trim('"'), directory), directory, receipt))
                    yield return nested;
            else yield return argument;
    }

    string ResolveActualReference(string reference, UnityCompilationProof proof, CSharpCommandLineArguments arguments)
    {
        string[] paths = new[] { UnityPolicyPath(reference, proof.WorkingDirectory) }
            .Concat(arguments.ReferencePaths.Select(directory => UnityPolicyPath(FullPath(directory, reference), proof.WorkingDirectory)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string? resolved = paths.FirstOrDefault(File.Exists);
        if (resolved is null)
            throw new InvalidDataException($"实际 rsp 引用无法按编译器路径解析：{proof.ResponseFile}；{reference}。");
        ActualBytes(resolved);
        return resolved;
    }

    void LoadActualCompilation(UnityCompilationProof proof)
    {
        string definition = Path.GetFullPath(proof.AssemblyDefinition);
        string projectPath = Path.GetFullPath(proof.Project);
        CompilationExecutionReceipt receipt = ExecutionReceipt(proof.Receipt);
        var trace = ActualJson<UnityCompilerTrace>(proof.CompilerTrace);
        if (!receipt.Outputs.Keys.Any(output => Path.GetFullPath(output).Equals(Path.GetFullPath(proof.CompilerTrace), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"实际编译输入生产记录缺少本批输出身份：{proof.CompilerTrace}。");
        if (trace.BatchId != unityBatch!.BatchId || !Path.GetFullPath(trace.AssemblyDefinition).Equals(definition, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(trace.ResponseFile).Equals(Path.GetFullPath(proof.ResponseFile), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"官方编译输入生产记录与本次声明或 rsp 不一致：{proof.CompilerTrace}。");
        if (!officialInstallations.TryGetValue(trace.OriginId, out var origin))
            throw new InvalidDataException($"编译输入生产记录缺少已核定的安装来源：{trace.OriginId}。");
        if (origin.Rule.Kind != "UnityCompiler")
            throw new InvalidDataException($"自动候选未来自已核定的安装编译器：{trace.OriginId}。");
        RequireReceiptInput(receipt, origin.Installation.Producer);
        RequireReceiptInput(receipt, origin.Installation.VersionFile);
        UnityReferencePolicyFacts policies = ReadReferencePolicyFacts(trace, receipt, origin.Installation);
        if (!byPath.TryGetValue(definition, out Module? owner) || owner.Kind != "asmdef" ||
            !byPath.TryGetValue(projectPath, out Module? project) || project.Kind != "project" ||
            !Path.GetDirectoryName(projectPath)!.Equals(clientRoot, StringComparison.OrdinalIgnoreCase) ||
            project.AssemblyDefinitionPath is null || !project.AssemblyDefinitionPath.Equals(definition, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"实际编译没有唯一当前声明和项目归属：{definition}；{projectPath}。");
        foreach (string path in new[] { definition, projectPath }) RequireReceiptInput(receipt, path);
        var arguments = CSharpCommandLineParser.Default.Parse(ResponseArguments(proof.ResponseFile, proof.WorkingDirectory, receipt),
            proof.WorkingDirectory, null, null);
        if (arguments.Errors.Any(error => error.Severity == DiagnosticSeverity.Error))
            throw new InvalidDataException($"实际 rsp 未完整解析：{proof.ResponseFile}；{string.Join("；", arguments.Errors)}。");
        string outputPath = FullPath(arguments.OutputDirectory, arguments.OutputFileName!);
        if (!outputPath.Equals(Path.GetFullPath(proof.Output), StringComparison.OrdinalIgnoreCase) ||
            !receipt.Outputs.Keys.Any(output => Path.GetFullPath(output).Equals(outputPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"当前产物不是实际 rsp 的本批输出：{proof.ResponseFile}；{proof.Output}。");
        string[] sourcePaths = arguments.SourceFiles.Select(source => UnityPolicyPath(source.Path, proof.WorkingDirectory)).ToArray();
        string[] referencePaths = arguments.MetadataReferences.Select(reference => ResolveActualReference(reference.Reference, proof, arguments)).ToArray();
        foreach (string path in sourcePaths.Concat(referencePaths).Concat(arguments.AnalyzerReferences.Select(reference =>
                     ResolveActualReference(reference.FilePath, proof, arguments))).Concat(arguments.AdditionalFiles.Select(source =>
                     UnityPolicyPath(source.Path, proof.WorkingDirectory))).Concat(arguments.AnalyzerConfigPaths.Select(path => UnityPolicyPath(path, proof.WorkingDirectory))))
        {
            ActualBytes(path);
            RequireReceiptInput(receipt, path);
        }
        string[] declaredSources = projectSourceOwners.Where(pair => pair.Value.Contains(project)).Select(pair => pair.Key).ToArray();
        if (declaredSources.Any(source => !sourcePaths.Contains(source, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException($"实际 rsp 漏收当前项目源码：{projectPath}。");
        foreach (string generated in proof.GeneratorOutputs)
            if (!receipt.Outputs.Keys.Any(output => Path.GetFullPath(output).Equals(Path.GetFullPath(generated), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"编译器生成源缺少本批实际输出回执：{generated}。");
        var trees = sourcePaths.Concat(proof.GeneratorOutputs).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(path => Path.GetFullPath(path),
            path => (SyntaxTree)CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(ActualBytes(path)).TrimStart('\uFEFF'),
                arguments.ParseOptions, Path.GetFullPath(path), Encoding.UTF8), StringComparer.OrdinalIgnoreCase);
        foreach (string path in trees.Keys)
        {
            sourceFiles.Add(path);
            if (!actualSourceOwners.TryGetValue(path, out HashSet<Module>? owners)) actualSourceOwners.Add(path, owners = []);
            owners.Add(owner);
        }
        MetadataReference[] references = referencePaths.Select((path, index) => (MetadataReference)MetadataReference.CreateFromImage(
            ActualBytes(path), arguments.MetadataReferences[index].Properties, filePath: path)).ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(owner.Name, trees.Values, references, arguments.CompilationOptions);
        Diagnostic[] bindingErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (bindingErrors.Length != 0)
            throw new InvalidDataException($"本批源码绑定不完整：{owner.Name}；{string.Join("；", bindingErrors.Take(10).Select(error => error.ToString()))}。");
        if (!PathSet(sourcePaths).SetEquals(PathSet(trace.Sources)) || !PathSet(referencePaths).SetEquals(PathSet(trace.References)))
            throw new InvalidDataException($"官方编译输入生产记录与实际 rsp 不一致：{proof.CompilerTrace}。");
        string compilerPath = Path.GetFullPath(origin.Installation.Producer).Replace('\\', '/');
        const string editorData = "/Editor/Data/";
        int editorDataIndex = compilerPath.IndexOf(editorData, StringComparison.OrdinalIgnoreCase);
        if (editorDataIndex < 0)
            throw new InvalidDataException($"编译输入生产者没有实际 Unity 安装目录：{origin.Installation.Producer}。");
        string unityDataDirectory = Path.GetFullPath(compilerPath[..(editorDataIndex + editorData.Length)]);
        ActualBinary outputBinary = ReadActualBinary(outputPath);
        if (outputBinary.Name != owner.Name)
            throw new InvalidDataException($"实际产物程序集身份不匹配：{outputPath}；{owner.Name}。");
        ActualBinary[] closure = ActualClosure(outputBinary).ToArray();
        HashSet<string> automaticReferences = ReadAutomaticReferencePolicies(trace, receipt, proof,
            policies, owner, arguments, referencePaths, out HashSet<string> declaredReferences);
        var actual = new ActualUnityCompilation(owner, proof, PathSet(trees.Keys), referencePaths.Select(ReadActualBinary).DistinctBy(binary => binary.Identity).ToArray(),
            closure, automaticReferences, unityDataDirectory);
        actual.DeclaredReferencePaths.UnionWith(declaredReferences);
        actualUnityCompilations.Add(definition, actual);
        ReadActualBindings(actual, compilation, trees);
        foreach (string automatic in actual.AutomaticReferences)
        {
            ActualBinary[] matches = actual.References.Where(reference => reference.Path.Equals(automatic, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"自动候选不在完整实际 rsp 引用集合中：{automatic}。");
            ActualBinary candidate = matches[0];
            bool declared = IsProjectDeclaredReference(owner, candidate);
            bool used = actual.UsedAssemblies.Contains(candidate.Identity);
            bool outputUsed = closure.Any(binary => binary.Identity == candidate.Identity);
            bool inputOnly = !declared && !used && !outputUsed;
            if (inputOnly) actual.InputOnlyAssemblies.Add(candidate.Identity);
            AutomaticCompilationCandidates.Add(new(definition, projectPath, proof.ResponseFile, candidate.Path, candidate.Name,
                origin.Rule.Origin, origin.Rule.Version, outputPath, declared, used, outputUsed, inputOnly));
        }
        ActualCompilations.Add(new(definition, projectPath, proof.ResponseFile, outputPath, proof.Receipt,
            unityBatch.BatchId, trees.Count, references.Length, closure.Length));
    }

    static HashSet<string> PathSet(IEnumerable<string> paths) => new(paths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

    bool IsProjectDeclaredReference(Module owner, ActualBinary candidate)
    {
        if (actualUnityCompilations.TryGetValue(owner.Path, out ActualUnityCompilation? actual) &&
            actual.DeclaredReferencePaths.Contains(candidate.Path)) return true;
        if (owner.Dependencies.Any(dependency => dependency.Kind != "engine" && dependency.Name == candidate.Name)) return true;
        string candidatePackage = ActualPackage(candidate);
        return candidatePackage.Length != 0 && Modules.Any(package => package.Kind == "package" && package.Name == owner.Package &&
            package.Dependencies.Any(dependency => dependency.Kind == "package" && dependency.Name == candidatePackage));
    }

    string ActualPackage(ActualBinary binary)
    {
        string package = PackageFor(binary.Path);
        if (package.Length != 0) return package;
        string[] declarations = Modules.Where(module => module.Kind == "asmdef" && module.Name == binary.Name)
            .Select(module => module.Package).Distinct(StringComparer.Ordinal).ToArray();
        return declarations.Length == 1 ? declarations[0] : "";
    }

    ActualBinary ReadActualBinary(string path)
    {
        path = Path.GetFullPath(path);
        if (actualBinaries.TryGetValue(path, out ActualBinary? cached)) return cached;
        using var stream = new MemoryStream(ActualBytes(path));
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        AssemblyDefinition definition = metadata.GetAssemblyDefinition();
        string name = metadata.GetString(definition.Name);
        binaryAssemblyNames[path] = name;
        string identity = BinaryIdentity(name, definition.Version, metadata.GetString(definition.Culture),
            metadata.GetBlobBytes(definition.PublicKey), true);
        string[] references = metadata.AssemblyReferences.Select(handle => metadata.GetAssemblyReference(handle)).Select(reference =>
            BinaryIdentity(metadata.GetString(reference.Name), reference.Version, metadata.GetString(reference.Culture),
                metadata.GetBlobBytes(reference.PublicKeyOrToken), (reference.Flags & AssemblyFlags.PublicKey) != 0)).ToArray();
        var binary = new ActualBinary(path, name, identity, references);
        actualBinaries.Add(path, binary);
        return binary;
    }

    static string BinaryIdentity(string name, Version version, string culture, byte[] key, bool fullKey)
    {
        byte[] token = fullKey && key.Length != 0 ? SHA1.HashData(key)[^8..].Reverse().ToArray() : key;
        return $"{name}|{version}|{culture}|{Convert.ToHexString(token)}";
    }

    IEnumerable<ActualBinary> ActualClosure(ActualBinary root)
    {
        var pending = new Queue<ActualBinary>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue(root);
        while (pending.Count != 0)
        {
            ActualBinary current = pending.Dequeue();
            if (!visited.Add(current.Identity)) continue;
            yield return current;
            foreach (string reference in current.References)
                if (binaryIdentities.TryGetValue(reference, out ActualBinary? target)) pending.Enqueue(target);
                else throw new InvalidDataException($"实际传递依赖闭包缺失：{current.Path} -> {reference}。");
        }
    }

    void ReadActualBindings(ActualUnityCompilation actual, CSharpCompilation compilation,
        Dictionary<string, SyntaxTree> trees)
    {
        foreach ((string path, SyntaxTree tree) in trees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            var visitedOperations = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<IOperation>();
            foreach (SyntaxNode node in tree.GetRoot().DescendantNodesAndSelf())
            {
                if (node is NameSyntax or MemberAccessExpressionSyntax)
                    RecordActualBinding(actual, path, node, model.GetSymbolInfo(node).Symbol);
                if (model.GetOperation(node) is not IOperation root) continue;
                pending.Push(root);
                while (pending.TryPop(out IOperation? operation))
                {
                    if (!visitedOperations.Add(operation)) continue;
                    SyntaxNode syntax = operation.Syntax;
                    switch (operation)
                    {
                        case IConversionOperation conversion:
                            RecordActualBinding(actual, path, syntax, conversion.OperatorMethod);
                            break;
                        case IBinaryOperation binary:
                            RecordActualBinding(actual, path, syntax, binary.OperatorMethod);
                            break;
                        case IUnaryOperation unary:
                            RecordActualBinding(actual, path, syntax, unary.OperatorMethod);
                            break;
                        case IIncrementOrDecrementOperation increment:
                            RecordActualBinding(actual, path, syntax, increment.OperatorMethod);
                            break;
                        case ICompoundAssignmentOperation compound:
                            RecordActualBinding(actual, path, syntax, compound.OperatorMethod);
                            RecordActualBinding(actual, path, syntax, compound.InConversion.MethodSymbol);
                            RecordActualBinding(actual, path, syntax, compound.OutConversion.MethodSymbol);
                            break;
                        case IArgumentOperation argument:
                            RecordActualBinding(actual, path, syntax, argument.InConversion.MethodSymbol);
                            RecordActualBinding(actual, path, syntax, argument.OutConversion.MethodSymbol);
                            break;
                    }
                    foreach (IOperation child in operation.ChildOperations) pending.Push(child);
                }
            }
        }
    }

    void RecordActualBinding(ActualUnityCompilation actual, string path, SyntaxNode node, ISymbol? symbol)
    {
        if (symbol is IAliasSymbol alias) symbol = alias.Target;
        if (symbol is null || symbol.ContainingAssembly is null || symbol.ContainingAssembly.Name == actual.Owner.Name) return;
        INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        string typeName = type?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? symbol.ToDisplayString();
        string assembly = symbol.ContainingAssembly.Name;
        string identity = SymbolAssemblyIdentity(symbol.ContainingAssembly);
        actual.UsedAssemblies.Add(identity);
        string? officialRule = OfficialMetadataBinding(actual, path, node, type);
        bool attributeUse = node.AncestorsAndSelf().OfType<AttributeSyntax>().Any(attribute => attribute.Name.Span.Contains(node.Span));
        actual.SourceBindings.Add(new(path, node.SpanStart, node.Span.Length,
            node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, typeName, assembly, identity,
            symbol.ContainingNamespace?.ToDisplayString() ?? "", attributeUse, officialRule));
    }

    string? OfficialMetadataBinding(ActualUnityCompilation actual, string path, SyntaxNode node, INamedTypeSymbol? type)
    {
        if (type is null) return null;
        string typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        foreach (var origin in officialInstallations.Values.Where(origin => origin.Rule.Kind == "QuantumGenerator" &&
                     rules.DependencyRules.Any(rule => rule.Id == origin.Rule.RuleId && rule.Applies(actual.Owner))))
        {
            string? receipt = null;
            bool attributeUse = node.AncestorsAndSelf().OfType<AttributeSyntax>().Any(attribute => attribute.Name.Span.Contains(node.Span));
            bool metadataName = node.AncestorsAndSelf().OfType<UsingDirectiveSyntax>().Any(usingDirective => usingDirective.Alias is not null &&
                usingDirective.Name!.Span.Contains(node.Span)) || attributeUse &&
                !rules.UnityActualBoundary.ProhibitedProjectAttributeTypes.Contains(typeName, StringComparer.Ordinal);
            if (metadataName && origin.Rule.MetadataTypes.Contains(typeName, StringComparer.Ordinal) &&
                officialGeneratedSources.TryGetValue(path, out var generated) && generated.Rule.Id == origin.Rule.Id)
            {
                ActualBinary binary = BoundAssembly(actual, SymbolAssemblyIdentity(type.ContainingAssembly));
                RequireOfficialEngine(actual, binary);
                actual.OfficialEngineAssemblies.Add(binary.Identity);
                receipt = generated.Receipt;
            }
            if (origin.Rule.AssetBaseTypes.Contains(typeName, StringComparer.Ordinal) &&
                node.AncestorsAndSelf().OfType<SimpleBaseTypeSyntax>().Any(baseType => baseType.Type.Span.Contains(node.Span)) &&
                type.BaseType?.ToDisplayString() == "UnityEngine.ScriptableObject")
            {
                ActualBinary runtime = ReadActualBinary(Path.GetFullPath(origin.Installation.RuntimeAssembly));
                ActualBinary boundRuntime = BoundAssembly(actual, SymbolAssemblyIdentity(type.ContainingAssembly));
                if (runtime.Identity != boundRuntime.Identity || boundaryFiles[runtime.Path].SHA256 != boundaryFiles[boundRuntime.Path].SHA256)
                    throw new InvalidDataException($"官方资产基类绑定到了另一程序集：{typeName}。");
                foreach (string identity in officialRuntimeEngineDependencies[origin.Rule.Id])
                {
                    RequireOfficialEngine(actual, binaryIdentities[identity]);
                    actual.OfficialEngineAssemblies.Add(identity);
                }
                receipt = actual.Proof.Receipt;
            }
            if (receipt is null) continue;
            VerifiedOfficialMetadata.Add(new(path, node.SpanStart, node.Span.Length, typeName, origin.Rule.RuleId,
                origin.Rule.Origin, origin.Rule.Version, origin.Rule.ProducerSHA256, receipt));
            return origin.Rule.RuleId;
        }
        return null;
    }

    public bool IsOfficialMetadataUse(string path, int start, int length, string rule) => VerifiedOfficialMetadata.Any(use =>
        use.Rule == rule && use.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && use.Start <= start && use.Start + use.Length >= start + length);

    void RequireOfficialEngine(ActualUnityCompilation actual, ActualBinary binary)
    {
        if (BinaryReferenceKind(binary.Path, binary.Name) != "UnityEngineLibrary" || !IsUnderDirectory(binary.Path, actual.UnityDataDirectory))
            throw new InvalidDataException($"官方元数据或资产基类没有绑定本批安装引擎原件：{binary.Path}。");
    }

    static string SymbolAssemblyIdentity(IAssemblySymbol symbol) => BinaryIdentity(symbol.Identity.Name, symbol.Identity.Version,
        symbol.Identity.CultureName, symbol.Identity.PublicKeyToken.ToArray(), false);

    ActualBinary BoundAssembly(ActualUnityCompilation actual, string identity)
    {
        ActualBinary? binary = actual.References.FirstOrDefault(reference => reference.Identity == identity);
        if (binary is not null) return binary;
        return binaryIdentities.TryGetValue(identity, out ActualBinary? forwarded) ? forwarded :
            throw new InvalidDataException($"实际源码绑定缺少程序集原件：{actual.Proof.ResponseFile} -> {identity}。");
    }

    bool IsInputOnlyCandidate(Module module, Dependency dependency)
    {
        if (module.Kind != "project" || module.AssemblyDefinitionPath is null ||
            !actualUnityCompilations.TryGetValue(module.AssemblyDefinitionPath, out ActualUnityCompilation? actual) ||
            !module.Path.Equals(Path.GetFullPath(actual.Proof.Project), StringComparison.OrdinalIgnoreCase)) return false;
        IEnumerable<ActualBinary> references = actual.References.Where(reference => reference.Name == dependency.Name);
        if (dependency.BinaryPath is not null)
        {
            string identity = ReadActualBinary(dependency.BinaryPath).Identity;
            references = references.Where(reference => identity == reference.Identity);
        }
        return references.Any(reference => actual.InputOnlyAssemblies.Contains(reference.Identity) && !IsProjectDeclaredReference(actual.Owner, reference));
    }

    public void CheckActualCompilationCoverage()
    {
        if (clientRoot is null) return;
        if (!Modules.Any(module => module.Kind == "asmdef" && module.Name == "Quantum.Simulation"))
            throw new InvalidDataException("完整工作区输入没有实际 Quantum.Simulation asmdef，不能声称已检查模拟宿主。");
        foreach (Module module in Modules.Where(module => module.Kind == "project" && module.AssemblyDefinitionPath is not null &&
                     Path.GetDirectoryName(module.Path)!.Equals(clientRoot, StringComparison.OrdinalIgnoreCase) &&
                     (IsControlled(module) || dependencySourceRules.ContainsKey(module))))
            if (!actualUnityCompilations.ContainsKey(module.AssemblyDefinitionPath!))
                throw new InvalidDataException($"缺少当前受控 Unity 项目的完整实际编译证据：{module.Path}。");
    }

    public void CheckActualSourceBindings()
    {
        if (clientRoot is not null)
            foreach ((string path, string hash) in Inputs.Where(input => !GuardExecutionInputs.Contains(input.Key)))
                if (!boundaryFiles.TryGetValue(path, out BoundaryFile? captured) || !captured.SHA256.Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"声明、源码或依赖不属于同批完整输入快照：{path}。");
        foreach (ActualUnityCompilation actual in actualUnityCompilations.Values)
            foreach (ActualSourceBinding binding in actual.SourceBindings)
            {
                if (binding.AttributeUse && rules.UnityActualBoundary.ProhibitedProjectAttributeTypes.Contains(binding.Type, StringComparer.Ordinal) &&
                    !VerifiedVendorSources.Any(match => match.Path.Equals(binding.Path, StringComparison.OrdinalIgnoreCase) &&
                        match.AssemblyDefinition.Equals(actual.Owner.Path, StringComparison.OrdinalIgnoreCase)))
                    Violations.Add(new("PROJECT_MIGRATION_METADATA", binding.Path, binding.Line, actual.Owner.Name,
                        $"项目源码不得引入迁移兼容属性：{binding.Type}。"));
                ActualBinary target = BoundAssembly(actual, binding.AssemblyIdentity);
                bool framework = BinaryReferenceKind(target.Path, target.Name) == "Framework";
                string package = ActualPackage(target);
                foreach (DependencyRule rule in SourceRules(binding.Path, actual.Owner).Where(rule => rule.Id != binding.OfficialRule &&
                             !IsOfficialMetadataUse(binding.Path, binding.Start, binding.Length, rule.Id) &&
                             (GuardRules.Matches(rule.ForbiddenNamespaces, binding.Namespace) || rule.Forbids(actual.Owner, binding.Assembly, "assembly", true, framework) ||
                              package.Length != 0 && rule.Forbids(actual.Owner, package, "package"))))
                    Violations.Add(new(rule.Id, binding.Path, binding.Line, actual.Owner.Name,
                        $"{rule.Reason} 本批实际源码绑定：{binding.Type} -> {binding.Assembly}。"));
            }
    }

    void CheckActualOutputDependencies()
    {
        foreach (ActualUnityCompilation actual in actualUnityCompilations.Values)
            foreach (DependencyRule rule in rules.DependencyRules.Where(rule => rule.Applies(actual.Owner)))
                foreach (ActualBinary dependency in actual.Closure.Skip(1))
                {
                    bool framework = BinaryReferenceKind(dependency.Path, dependency.Name) == "Framework";
                    bool direct = actual.Closure[0].References.Contains(dependency.Identity, StringComparer.Ordinal);
                    bool originalEngine = direct && AllowsVendorEngineDependency(byPath[Path.GetFullPath(actual.Proof.Project)], rule,
                        new(dependency.Name, "assembly", 1, BinaryPath: dependency.Path));
                    bool metadataEngine = actual.OfficialEngineAssemblies.Contains(dependency.Identity) &&
                        VerifiedOfficialMetadata.Any(use => use.Rule == rule.Id && actual.SourcePaths.Contains(use.Path));
                    string package = ActualPackage(dependency);
                    if ((rule.Forbids(actual.Owner, dependency.Name, "assembly", direct, framework) ||
                         package.Length != 0 && rule.Forbids(actual.Owner, package, "package")) && !originalEngine && !metadataEngine)
                        AddViolation(rule.Id, actual.Owner, 1, $"{rule.Reason} 本批产物及真实传递依赖：{actual.Proof.Output} -> {dependency.Identity}（{dependency.Path}）。");
                }
    }
}
