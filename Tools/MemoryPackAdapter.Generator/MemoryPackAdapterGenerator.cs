using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static ThirdPerson.ValueGeneration.ValueModel;

namespace ThirdPerson.MemoryPackAdapters
{
    [Generator]
    public sealed partial class MemoryPackAdapterGenerator : ISourceGenerator
    {
        const string PresenceAttribute = "ThirdPersonSimulation.ValuePresenceAttribute";
        const string PayloadAttribute = "ThirdPersonSimulation.ValuePayloadAttribute";
        const string TextAttribute = "ThirdPersonSimulation.ValueTextAttribute";

        public void Initialize(GeneratorInitializationContext context) =>
            context.RegisterForSyntaxNotifications(() => new Receiver());

        public void Execute(GeneratorExecutionContext context) =>
            EmitMemoryPackAdapters(context, (Receiver)context.SyntaxReceiver);

        sealed class Receiver : ISyntaxReceiver
        {
            public readonly List<InvocationExpressionSyntax> Calls = new List<InvocationExpressionSyntax>();
            public void OnVisitSyntaxNode(SyntaxNode node)
            {
                if (node is InvocationExpressionSyntax call) Calls.Add(call);
            }
        }

        static readonly DiagnosticDescriptor InvalidMemoryPackAdapter = new DiagnosticDescriptor(
            "MPADAPT001", "原数据无法生成外部 MemoryPack 适配", "{0}: {1}", "ValueGeneration",
            DiagnosticSeverity.Error, true);

        static void EmitMemoryPackAdapters(GeneratorExecutionContext context, Receiver receiver)
        {
            if (context.Compilation.GetTypeByMetadataName("MemoryPack.MemoryPackSerializer") == null) return;
            var records = new SortedDictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var logicalStorages = new Dictionary<string, LogicalStoragePlan>(StringComparer.Ordinal);
            var sequences = new SortedDictionary<string, ITypeSymbol>(StringComparer.Ordinal);
            var nullableValues = new SortedDictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var externalLeaves = new HashSet<string>(StringComparer.Ordinal);
            foreach (IAssemblySymbol assembly in context.Compilation.SourceModule.ReferencedAssemblySymbols)
                if (assembly.Name == "MemoryPack.Core" || assembly.Modules.Any(module =>
                    module.ReferencedAssemblySymbols.Any(reference => reference.Name == "MemoryPack.Core")))
                    foreach (INamedTypeSymbol type in FormatterTypes(assembly.GlobalNamespace))
                        for (INamedTypeSymbol parent = type.BaseType; parent != null; parent = parent.BaseType)
                            if (Identity(parent.OriginalDefinition) == "MemoryPack.MemoryPackFormatter<T>")
                                externalLeaves.Add(Identity(parent.TypeArguments[0]));
            foreach (var call in receiver.Calls)
            {
                var method = context.Compilation.GetSemanticModel(call.SyntaxTree).GetSymbolInfo(call).Symbol as IMethodSymbol;
                if (method != null && Identity(method.ContainingType) == "MemoryPack.MemoryPackFormatterProvider" &&
                    method.Name == "Register" && method.TypeArguments.Length == 1)
                    externalLeaves.Add(Identity(method.TypeArguments[0]));
            }
            bool valid = true;
            var preparedStorages = PreparedStorages(context.Compilation, receiver);
            foreach (var call in receiver.Calls)
            {
                var method = context.Compilation.GetSemanticModel(call.SyntaxTree).GetSymbolInfo(call).Symbol as IMethodSymbol;
                if (method == null || !IsMemoryPackValueCall(method)) continue;
                if (method.Name == "DeserializeInto" && method.TypeArguments.Length == 0)
                    Visit(method.ReturnType);
                for (int i = 0; i < method.TypeArguments.Length; i++)
                    if (method.OriginalDefinition.TypeParameters[i].Name != "TBufferWriter" &&
                        method.OriginalDefinition.TypeParameters[i].Name != "TStorage")
                        Visit(method.TypeArguments[i]);
            }
            INamedTypeSymbol contentContext = context.Compilation.GetTypeByMetadataName("ThirdPersonSimulation.Serialization.MemoryPackContentContext");
            if (contentContext != null && SymbolEqualityComparer.Default.Equals(contentContext.ContainingAssembly, context.Compilation.Assembly))
                foreach (INamedTypeSymbol logical in LogicalStorageTypes(context.Compilation)) Visit(logical);
            var preparedPlans = new Dictionary<string, PreparedReadPlan>(StringComparer.Ordinal);
            foreach (var logical in logicalStorages)
            {
                if (preparedStorages.TryGetValue(logical.Key, out INamedTypeSymbol storage) &&
                    !SymbolEqualityComparer.Default.Equals(storage, logical.Value.Type))
                {
                    valid = false;
                    context.ReportDiagnostic(Diagnostic.Create(InvalidMemoryPackAdapter,
                        logical.Value.Type.Locations.FirstOrDefault(), logical.Key, "逻辑存储必须接收原 owner 准备的同型页"));
                }
                preparedStorages[logical.Key] = logical.Value.Type;
            }
            foreach (var pair in preparedStorages.Where(pair => records.ContainsKey(pair.Key)))
            {
                var type = records[pair.Key];
                string reason = null;
                PreparedReadPlan plan = logicalStorages.TryGetValue(pair.Key, out LogicalStoragePlan logical)
                    ? new PreparedReadPlan { Value = type, Storage = pair.Value, Logical = logical }
                    : CreatePreparedReadPlan(type, pair.Value, ValueMembers(type, IsImplicitReference), IsAccessible,
                        preparedStorages, out reason);
                if (reason != null)
                {
                    valid = false;
                    context.ReportDiagnostic(Diagnostic.Create(InvalidMemoryPackAdapter, type.Locations.FirstOrDefault(), Identity(type), reason));
                }
                else preparedPlans.Add(pair.Key, plan);
            }
            foreach (var pair in preparedPlans)
            {
                string reason = ValidatePreparedMembers(records[pair.Key], pair.Value,
                    IsImplicitReference, externalLeaves, preparedPlans);
                if (reason == null) continue;
                valid = false;
                context.ReportDiagnostic(Diagnostic.Create(InvalidMemoryPackAdapter, records[pair.Key].Locations.FirstOrDefault(), pair.Key, reason));
            }
            if (!valid) return;
            ITypeSymbol[] adaptedSequences = sequences.Values.Where(type =>
                HasAdapter(SequenceElement(type))).ToArray();
            INamedTypeSymbol[] adaptedNullableValues = nullableValues.Values.Where(type =>
                HasAdapter(NullableElement(type))).ToArray();
            if (records.Count + adaptedSequences.Length + adaptedNullableValues.Length == 0) return;
            var payloadTypes = new HashSet<string>(StringComparer.Ordinal);
            bool changed;
            do
            {
                changed = false;
                foreach (var record in records.Values)
                    if ((logicalStorages.TryGetValue(Identity(record), out LogicalStoragePlan logical) ? logical.Entries :
                        ValueMembers(record, IsImplicitReference)).Any(member => member.IsCase || HasAttribute(member.Symbol, PayloadAttribute) || HasPayload(member.Type)))
                        changed |= payloadTypes.Add(Identity(record));
            } while (changed);
            var code = new StringBuilder();
            code.AppendLine("namespace ThirdPersonSimulation.Serialization {");
            code.AppendLine("internal static class MemoryPackGeneratedFormatters {");
            code.AppendLine("internal static void Register() {");
            int ordinal = 0;
            foreach (var record in records.Values)
                code.Append("global::MemoryPack.MemoryPackFormatterProvider.Register(new Formatter")
                    .Append(ordinal++).AppendLine("());");
            foreach (var sequence in adaptedSequences)
                code.Append("global::MemoryPack.MemoryPackFormatterProvider.Register(new Formatter")
                    .Append(ordinal++).AppendLine("());");
            foreach (var nullable in adaptedNullableValues)
                code.Append("global::MemoryPack.MemoryPackFormatterProvider.Register(new Formatter")
                    .Append(ordinal++).AppendLine("());");
            code.AppendLine("}");
            ordinal = 0;
            bool scopedReferences = (int)((Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)context.ParseOptions).LanguageVersion >= 1100;
            var schemas = new MemoryPackSchemas(context.Compilation, logicalStorages, IsImplicitReference);
            foreach (var record in records.Values)
                EmitMemoryPackFormatter(code, record, ValueMembers(record, IsImplicitReference),
                    StorageReadMembers(record, IsImplicitReference), ordinal++, scopedReferences, HasPayload, IsAccessible,
                    preparedPlans.TryGetValue(Identity(record), out var plan) ? plan : null,
                    schemas.Hash(record));
            foreach (var sequence in adaptedSequences)
                EmitMemoryPackSequenceFormatter(code, sequence, ordinal++, scopedReferences, schemas.Hash(sequence));
            foreach (var nullable in adaptedNullableValues)
                EmitMemoryPackNullableFormatter(code, nullable, ordinal++, scopedReferences, schemas.Hash(nullable));
            code.AppendLine("} }");
            context.AddSource("MemoryPackGeneratedFormatters.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));

            void Visit(ITypeSymbol type)
            {
                if (type is INamedTypeSymbol namedType) type = RecordType(namedType);
                if (ContainsParameter(type) || IsUnmanagedValue(type))
                    return;
                if (!visited.Add(Identity(type))) return;
                if (externalLeaves.Contains(Identity(type))) return;
                if (MemoryPackSequence(type))
                {
                    sequences.Add(Identity(type), type);
                    Visit(SequenceElement(type));
                    return;
                }
                if (!(type is INamedTypeSymbol named)) return;
                if (NullableElement(type) is ITypeSymbol nullable)
                {
                    nullableValues.Add(Identity(type), named);
                    Visit(nullable);
                    return;
                }
                if (named.SpecialType != SpecialType.None || named.Name != "ValueTuple" &&
                    (named.ContainingNamespace.ToDisplayString() == "System" ||
                    named.ContainingNamespace.ToDisplayString().StartsWith("System.", StringComparison.Ordinal)))
                {
                    foreach (var argument in named.TypeArguments) Visit(argument);
                    return;
                }
                if (HasLogicalStorage(named))
                {
                    LogicalStoragePlan logical = CreateLogicalStoragePlan(named, IsAccessible, out string logicalReason);
                    if (logicalReason != null)
                    {
                        valid = false;
                        context.ReportDiagnostic(Diagnostic.Create(InvalidMemoryPackAdapter, named.Locations.FirstOrDefault(),
                            Identity(named), logicalReason));
                        return;
                    }
                    records.Add(Identity(named), named);
                    logicalStorages.Add(Identity(named), logical);
                    foreach (Member entry in logical.Entries) Visit(entry.Type);
                    return;
                }
                Member[] members = ValueMembers(named, IsImplicitReference);
                Variant[] known = KnownTypes(named);
                if (known.Length != 0)
                {
                    records.Add(Identity(type), named);
                    foreach (Variant branch in known) Visit(branch.Type);
                    return;
                }
                if (HasAttribute(named, TextAttribute))
                {
                    records.Add(Identity(type), named);
                    return;
                }
                if (HasAttribute(named, "MemoryPack.MemoryPackableAttribute") && !HasValueStorageContract(named))
                {
                    foreach (Member member in members.Where(member => !HasAttribute(member.Symbol, "MemoryPack.MemoryPackIgnoreAttribute")))
                        Visit(member.Type);
                    return;
                }
                string reason = null;
                if (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Struct)
                    reason = "原类型不是可构造的值或记录；需要原字段用途合同";
                else if (StorageReadMembers(named, IsImplicitReference) == null && members.Any(member => !context.Compilation.IsSymbolAccessibleWithin(
                    named.GetMembers(member.Name).FirstOrDefault() ?? member.Symbol, context.Compilation.Assembly)))
                    reason = "原成员不可从消费者访问，须由原字段工具生成中立访问，不能手写镜像";
                else if (members.Length > 249)
                    reason = "原记录成员超过 MemoryPack 对象头上限";
                else if (!members.Any(member => member.IsCase) && StorageReadMembers(named, IsImplicitReference) == null &&
                    MemoryPackConstruction(named, members, IsAccessible) == null &&
                    !MemoryPackMutableRecord(named, members, IsAccessible))
                    reason = "原类型没有与真实成员一一对应的可访问构造入口，须由原 owner 提供中立构造";
                else if (!members.Any(member => member.IsCase) && MemoryPackConstruction(named, members, IsAccessible) is IMethodSymbol construction &&
                    construction.Parameters.Any(parameter => members.Count(member => string.Equals(MemberKey(member.Name),
                        MemberKey(parameter.Name), StringComparison.OrdinalIgnoreCase)) != 1))
                    reason = "中立构造参数与原存储成员不符：" + construction.ToDisplayString() + "；成员：" +
                        string.Join(", ", members.Select(member => member.Name));
                if (reason != null)
                {
                    valid = false;
                    context.ReportDiagnostic(Diagnostic.Create(InvalidMemoryPackAdapter, type.Locations.FirstOrDefault(), Identity(type), reason));
                    return;
                }
                records.Add(Identity(type), named);
                foreach (Member member in members) Visit(member.Type);
            }

            bool HasAdapter(ITypeSymbol type) => records.ContainsKey(Identity(type)) || externalLeaves.Contains(Identity(type)) ||
                SequenceElement(type) is ITypeSymbol element && HasAdapter(element) ||
                NullableElement(type) is ITypeSymbol nullable && HasAdapter(nullable);

            bool HasPayload(ITypeSymbol type) => SequenceElement(type) is ITypeSymbol element ? HasPayload(element) :
                NullableElement(type) is ITypeSymbol nullable ? HasPayload(nullable) : payloadTypes.Contains(Identity(type));

            bool IsAccessible(ISymbol symbol) => context.Compilation.IsSymbolAccessibleWithin(symbol, context.Compilation.Assembly);

            bool IsImplicitReference(ITypeSymbol value, ITypeSymbol parameter)
            {
                var conversion = ((Microsoft.CodeAnalysis.CSharp.CSharpCompilation)context.Compilation).ClassifyConversion(value, parameter);
                return conversion.IsImplicit && conversion.IsReference;
            }
        }

        static IEnumerable<INamedTypeSymbol> FormatterTypes(INamespaceOrTypeSymbol scope)
        {
            foreach (ISymbol member in scope.GetMembers())
                if (member is INamespaceSymbol childNamespace)
                    foreach (INamedTypeSymbol type in FormatterTypes(childNamespace)) yield return type;
                else if (member is INamedTypeSymbol named)
                {
                    yield return named;
                    foreach (INamedTypeSymbol type in FormatterTypes(named)) yield return type;
                }
        }

        static bool MemoryPackSequence(ITypeSymbol type) => type is IArrayTypeSymbol array ? array.Rank == 1 :
            type is INamedTypeSymbol named && (Identity(named.OriginalDefinition) == "System.Collections.Generic.List<T>" ||
                Identity(named.OriginalDefinition) == "System.Collections.Generic.IReadOnlyList<T>" ||
                Identity(named.OriginalDefinition) == "System.Collections.Generic.IEnumerable<T>" ||
                Identity(named.OriginalDefinition) == "System.Collections.ObjectModel.ReadOnlyCollection<T>" ||
                Identity(named.OriginalDefinition) == "System.ReadOnlyMemory<T>");

        static void EmitMemoryPackSerializeDeclaration(StringBuilder code, string name, bool scopedReferences)
        {
            code.Append("public override void Serialize<TBufferWriter>(ref global::MemoryPack.MemoryPackWriter<TBufferWriter> writer, ")
                .Append(scopedReferences ? "scoped ref " : "ref ").Append(name).AppendLine(" value)");
            code.AppendLine("#if !NET7_0_OR_GREATER");
            code.AppendLine("where TBufferWriter : class");
            code.AppendLine("#endif");
            code.AppendLine("{");
        }

        static void EmitMemoryPackSequenceFormatter(StringBuilder code, ITypeSymbol type, int ordinal, bool scopedReferences, string schemaHash)
        {
            string name = TypeName(type);
            string element = TypeName(SequenceElement(type));
            string shape = type is INamedTypeSymbol named ? Identity(named.OriginalDefinition) : "array";
            bool list = shape == "System.Collections.Generic.List<T>";
            bool readOnlyCollection = shape == "System.Collections.ObjectModel.ReadOnlyCollection<T>";
            bool memory = shape == "System.ReadOnlyMemory<T>";
            bool enumerable = shape == "System.Collections.Generic.IEnumerable<T>";
            string count = enumerable ? "items.Length" : type is IArrayTypeSymbol || memory ? "value.Length" : "value.Count";
            EmitMemoryPackFormatterHeader(code, "Formatter" + ordinal, name, schemaHash);
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            if (type.IsReferenceType)
                code.AppendLine("if (value == null) { writer.WriteNullCollectionHeader(); return; }");
            if (enumerable) code.AppendLine("var items = global::System.Linq.Enumerable.ToArray(value);");
            code.Append("writer.WriteCollectionHeader(").Append(count).AppendLine(");");
            code.Append("for (int index = 0; index < ").Append(count).AppendLine("; index++) {");
            EmitValueWrite(code, SequenceElement(type), (enumerable ? "items" : "value") +
                (memory ? ".Span" : "") + "[index]");
            code.AppendLine("}");
            code.AppendLine("}");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            code.AppendLine("if (!reader.TryReadCollectionHeader(out int count)) { value = default; return; }");
            code.AppendLine("var context = reader.Options.ServiceProvider as global::ThirdPersonSimulation.Serialization.MemoryPackContentContext;");
            code.AppendLine("bool merge = context != null && context.Part == global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Payload;");
            if (list)
            {
                code.AppendLine("var previous = value;");
                code.Append("value = new global::System.Collections.Generic.List<").Append(element).AppendLine(">(count);");
                code.Append("for (int index = 0; index < count; index++) { ").Append(element).AppendLine(" item = merge ? previous[index] : default;");
                EmitValueRead(code, SequenceElement(type), "item");
                code.AppendLine("value.Add(item); }");
            }
            else
            {
                if (enumerable) code.AppendLine("var previous = merge ? global::System.Linq.Enumerable.ToArray(value) : null;");
                code.Append("var items = new ").Append(element).AppendLine("[count];");
                code.AppendLine("for (int index = 0; index < count; index++) {");
                code.Append("if (merge) items[index] = ").Append(enumerable ? "previous" : "value")
                    .Append(memory ? ".Span" : "").AppendLine("[index];");
                EmitValueRead(code, SequenceElement(type), "items[index]");
                code.AppendLine("}");
                if (readOnlyCollection)
                    code.Append("value = new global::System.Collections.ObjectModel.ReadOnlyCollection<").Append(element).AppendLine(">(items);");
                else code.AppendLine("value = items;");
            }
            code.AppendLine("} }");
        }

        static void EmitMemoryPackNullableFormatter(StringBuilder code, INamedTypeSymbol type, int ordinal, bool scopedReferences, string schemaHash)
        {
            string name = TypeName(type);
            string element = TypeName(NullableElement(type));
            EmitMemoryPackFormatterHeader(code, "Formatter" + ordinal, name, schemaHash);
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            code.AppendLine("if (!value.HasValue) { writer.WriteNullObjectHeader(); return; }");
            code.AppendLine("writer.WriteObjectHeader(1);");
            EmitValueWrite(code, NullableElement(type), "value.Value");
            code.AppendLine("}");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            code.AppendLine("if (!reader.TryReadObjectHeader(out byte count)) { value = default; return; }");
            code.AppendLine("if (count != 1) throw new global::MemoryPack.MemoryPackSerializationException(\"可空值成员数与当前格式不符。\");");
            code.AppendLine("var item = value.GetValueOrDefault();");
            EmitValueRead(code, NullableElement(type), "item");
            code.AppendLine("value = item;");
            code.AppendLine("} }");
        }

        static bool IsMemoryPackValueCall(IMethodSymbol method)
        {
            string owner = Identity(method.ContainingType.OriginalDefinition);
            return owner == "MemoryPack.MemoryPackSerializer" &&
                (method.Name == "Serialize" || method.Name == "Deserialize") ||
                owner == "ThirdPersonSimulation.Serialization.MemoryPackContent" &&
                (method.Name == "Serialize" || method.Name == "SerializeInto" || method.Name == "Deserialize" ||
                    method.Name == "DeserializeParts" || method.Name == "DeserializeInto" ||
                    method.Name == "GetSchemaHash") ||
                owner == "MemoryPack.MemoryPackWriter<TBufferWriter>" && method.Name == "WriteValue" ||
                owner == "MemoryPack.MemoryPackReader" && method.Name == "ReadValue";
        }

        static IMethodSymbol MemoryPackConstruction(INamedTypeSymbol type, Member[] members, Func<ISymbol, bool> isAccessible)
        {
            IMethodSymbol storage = type.GetMembers("FromValueStorage").OfType<IMethodSymbol>().SingleOrDefault();
            if (storage != null) return storage;
            IMethodSymbol[] methods = type.InstanceConstructors.Concat(type.GetMembers().OfType<IMethodSymbol>()
                .Where(method => method.IsStatic && SymbolEqualityComparer.Default.Equals(method.ReturnType, type)))
                .Where(method => isAccessible(method) && method.Parameters.Length == members.Length &&
                method.Parameters.All(parameter => members.Count(member =>
                    string.Equals(MemberKey(member.Name), MemberKey(parameter.Name), StringComparison.OrdinalIgnoreCase) &&
                    CanPass(member.Type, parameter.Type)) == 1)).ToArray();
            IMethodSymbol[] constructors = methods.Where(method => method.MethodKind == MethodKind.Constructor).ToArray();
            return constructors.Length == 1 ? constructors[0] : constructors.Length == 0 && methods.Length == 1 ? methods[0] : null;
        }

        static bool MemoryPackMutableRecord(INamedTypeSymbol type, Member[] members, Func<ISymbol, bool> isAccessible) =>
            !type.IsReadOnly && (type.IsValueType || type.InstanceConstructors.Any(constructor =>
                isAccessible(constructor) && constructor.Parameters.Length == 0)) &&
            members.All(member => member.Symbol is IFieldSymbol field && !field.IsReadOnly ||
                member.Symbol is IPropertySymbol property && property.SetMethod != null && isAccessible(property.SetMethod));

        static void EmitMemoryPackFormatter(StringBuilder code, INamedTypeSymbol type, Member[] members, Member[] storage, int ordinal,
            bool scopedReferences, Func<ITypeSymbol, bool> hasPayload, Func<ISymbol, bool> isAccessible,
            PreparedReadPlan prepared, string schemaHash)
        {
            if (prepared?.Logical != null)
            {
                EmitLogicalStorageFormatter(code, prepared.Logical, ordinal, scopedReferences, hasPayload, schemaHash);
                return;
            }
            if (KnownTypes(type).Length != 0)
            {
                EmitMemoryPackKnownTypeFormatter(code, type, ordinal, scopedReferences, schemaHash);
                return;
            }
            if (HasAttribute(type, TextAttribute))
            {
                EmitMemoryPackTextFormatter(code, type, ordinal, scopedReferences, schemaHash);
                return;
            }
            string name = TypeName(type);
            ISymbol presence = type.GetMembers().SingleOrDefault(member => HasAttribute(member, PresenceAttribute));
            bool partitioned = hasPayload(type);
            EmitMemoryPackFormatterHeader(code, "Formatter" + ordinal, name, schemaHash,
                prepared == null ? null : TypeName(prepared.Storage));
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            if (type.IsReferenceType)
                code.AppendLine("if (value == null) { writer.WriteNullObjectHeader(); return; }");
            if (presence != null)
                code.Append("if (!value.").Append(Escape(presence.Name)).AppendLine(") { writer.WriteNullObjectHeader(); return; }");
            EmitPartContext(code, "writer", partitioned);
            EmitStorageRead(code, storage, "value", false, partitioned);
            if (members.Any(member => member.IsCase))
                EmitMemoryPackUnionWrite(code, type, members, storage, partitioned, hasPayload);
            else
            {
                EmitObjectHeader(code, "writer.WriteObjectHeader", members, partitioned, hasPayload);
                foreach (Member member in members)
                    EmitMemberWrite(code, member, MemberValue(type, member), partitioned, hasPayload);
            }
            code.AppendLine("}");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            if (ReadOnlyStorage(type))
            {
                code.AppendLine("throw new global::MemoryPack.MemoryPackSerializationException(\"只读状态须通过原 owner 的准备存储读取。\"); }");
                if (prepared != null) EmitPreparedRead(code, type, members, prepared, hasPayload);
                code.AppendLine("}");
                return;
            }
            EmitPartContext(code, "reader", partitioned);
            if (members.Any(member => member.IsCase))
                EmitMemoryPackUnionRead(code, type, members, storage, partitioned, hasPayload);
            else
            {
                code.AppendLine("if (!reader.TryReadObjectHeader(out byte count)) { value = default; return; }");
                code.Append("if (count != ");
                AppendMemberCount(code, members, partitioned, hasPayload);
                code.AppendLine(") throw new global::MemoryPack.MemoryPackSerializationException(\"记录成员数与当前格式不符。\");");
                EmitStorageRead(code, storage, "value", true, partitioned);
                foreach (Member member in members)
                {
                    if (storage == null)
                        EmitReadMemberInitial(code, member, partitioned);
                    EmitMemberRead(code, member, "member_" + member.Name, partitioned, hasPayload);
                }
                IMethodSymbol construction = MemoryPackConstruction(type, members, isAccessible);
                if (storage != null)
                    code.Append("value = ").Append(name).Append(".FromValueStorage(")
                        .Append(string.Join(", ", storage.Select(member => Escape(member.Name) + ": member_" + member.Name))).AppendLine(");");
                else if (construction == null)
                {
                    if (type.IsReferenceType)
                        code.Append("if (value == null) value = new ").Append(name).AppendLine("();");
                    foreach (Member member in members)
                        code.Append("value.").Append(Escape(member.Name)).Append(" = member_").Append(member.Name).AppendLine(";");
                }
                else
                    code.Append("value = ").Append(type.IsTupleType ? "" : construction.MethodKind == MethodKind.Constructor ? "new " + name :
                        name + "." + Escape(construction.Name)).Append('(')
                        .Append(string.Join(", ", construction.Parameters.Select(parameter =>
                            "member_" + members.Single(member => string.Equals(MemberKey(member.Name), MemberKey(parameter.Name),
                                StringComparison.OrdinalIgnoreCase)).Name))).AppendLine(");");
            }
            code.AppendLine("}");
            if (prepared != null) EmitPreparedRead(code, type, members, prepared, hasPayload);
            code.AppendLine("}");
        }

        readonly struct Variant
        {
            internal Variant(ushort discriminator, INamedTypeSymbol type)
            {
                Discriminator = discriminator;
                Type = type;
            }

            internal ushort Discriminator { get; }
            internal INamedTypeSymbol Type { get; }
        }

        static Variant[] KnownTypes(INamedTypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Class || type.IsSealed)
                return Array.Empty<Variant>();
            return FormatterTypes(type.ContainingAssembly.GlobalNamespace)
                .Where(candidate => DerivesFrom(candidate, type))
                .Select(candidate => (Type: candidate, Attribute: candidate.GetAttributes().SingleOrDefault(attribute =>
                    Identity(attribute.AttributeClass) == "ThirdPersonSimulation.ValueVariantAttribute")))
                .Where(candidate => candidate.Attribute != null)
                .Select(candidate => new Variant((ushort)candidate.Attribute.ConstructorArguments[0].Value, candidate.Type))
                .OrderBy(candidate => candidate.Discriminator).ToArray();
        }

        static bool DerivesFrom(INamedTypeSymbol candidate, INamedTypeSymbol type)
        {
            for (INamedTypeSymbol parent = candidate.BaseType; parent != null; parent = parent.BaseType)
                if (SymbolEqualityComparer.Default.Equals(parent, type))
                    return true;
            return false;
        }

        static void EmitMemoryPackKnownTypeFormatter(StringBuilder code, INamedTypeSymbol type, int ordinal, bool scopedReferences, string schemaHash)
        {
            string name = TypeName(type);
            Variant[] known = KnownTypes(type);
            EmitMemoryPackFormatterHeader(code, "Formatter" + ordinal, name, schemaHash);
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            code.AppendLine("if (value == null) { writer.WriteNullUnionHeader(); return; }");
            code.AppendLine("switch (value) {");
            foreach (Variant branch in known)
            {
                ushort discriminator = branch.Discriminator;
                code.Append("case ").Append(TypeName(branch.Type)).Append(" branch")
                    .Append(discriminator).Append(": writer.WriteUnionHeader(").Append(discriminator).Append("); writer.WriteValue(branch")
                    .Append(discriminator).AppendLine("); break;");
            }
            code.AppendLine("default: throw new global::MemoryPack.MemoryPackSerializationException(\"记录类型不属于当前正式多态合同。\"); } }");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            code.AppendLine("if (!reader.TryReadUnionHeader(out ushort discriminator)) { value = null; return; }");
            code.AppendLine("switch (discriminator) {");
            foreach (Variant branch in known)
            {
                ushort discriminator = branch.Discriminator;
                code.Append("case ").Append(discriminator).Append(": { ").Append(TypeName(branch.Type))
                    .AppendLine(" branch = default; reader.ReadValue(ref branch); value = branch; break; }");
            }
            code.AppendLine("default: throw new global::MemoryPack.MemoryPackSerializationException(\"记录判别不属于当前正式多态合同。\"); } } }");
        }

        static void EmitPartContext(StringBuilder code, string stream, bool partitioned)
        {
            if (!partitioned) return;
            code.Append("var context = ").Append(stream).AppendLine(".Options.ServiceProvider as global::ThirdPersonSimulation.Serialization.MemoryPackContentContext;");
            code.AppendLine("var part = context == null ? global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Full : context.Part;");
        }

        static void EmitMemoryPackTextFormatter(StringBuilder code, INamedTypeSymbol type, int ordinal, bool scopedReferences, string schemaHash)
        {
            string name = TypeName(type);
            IMethodSymbol factory = type.GetMembers().OfType<IMethodSymbol>().Single(method =>
                method.MethodKind == MethodKind.Ordinary && method.IsStatic &&
                method.DeclaredAccessibility == Accessibility.Public &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, type) && method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String);
            EmitMemoryPackFormatterHeader(code, "Formatter" + ordinal, name, schemaHash);
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            if (type.IsReferenceType)
                code.AppendLine("if (value == null) { writer.WriteNullCollectionHeader(); return; }");
            code.AppendLine("if (value.IsNull) { writer.WriteNullCollectionHeader(); return; }");
            code.AppendLine("using var read = value.AcquireRead();");
            code.AppendLine("writer.WriteUtf16(value.Span);");
            code.AppendLine("}");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            code.Append("value = ").Append(name).Append('.').Append(Escape(factory.Name)).AppendLine("(reader.ReadString());");
            code.AppendLine("} }");
        }

        static void EmitStorageRead(StringBuilder code, Member[] storage, string value, bool reading, bool partitioned)
        {
            if (storage == null) return;
            if (reading)
            {
                foreach (Member member in storage)
                    code.Append(TypeName(member.Type)).Append(" member_").Append(member.Name).AppendLine(" = default;");
                if (!partitioned) return;
                code.AppendLine("if (part == global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Payload)");
            }
            code.Append(value).Append(".ReadValueStorage(")
                .Append(string.Join(", ", storage.Select(member => Escape(member.Name) + ": " +
                    (reading ? "out member_" : "out var member_") + member.Name)))
                .AppendLine(");");
        }

        static void EmitReadMemberInitial(StringBuilder code, Member member, bool partitioned)
        {
            code.Append(TypeName(member.Type)).Append(" member_").Append(member.Name).AppendLine(" = default;");
            if (partitioned)
                code.Append("if (part == global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Payload) member_")
                    .Append(member.Name).Append(" = value.").Append(Escape(member.Name)).AppendLine(";");
        }

        static string MemberValue(INamedTypeSymbol type, Member member) =>
            HasAttribute(type, StorageAttribute) || type.GetMembers("ReadValueStorage").OfType<IMethodSymbol>().Any() ?
                "member_" + member.Name : "value." + Escape(member.Name);

        static string MemberCondition(Member member, bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            if (!partitioned) return null;
            if (HasAttribute(member.Symbol, PayloadAttribute))
                return "part != global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Layout";
            return hasPayload(member.Type) ? null :
                "part != global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Payload";
        }

        static void AppendMemberCount(StringBuilder code, Member[] members, bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            if (!partitioned) { code.Append(members.Length); return; }
            code.Append("(part == global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Layout ? ")
                .Append(members.Count(member => !HasAttribute(member.Symbol, PayloadAttribute)))
                .Append(" : part == global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Payload ? ")
                .Append(members.Count(member => HasAttribute(member.Symbol, PayloadAttribute) || hasPayload(member.Type)))
                .Append(" : ").Append(members.Length).Append(')');
        }

        static void EmitObjectHeader(StringBuilder code, string method, Member[] members, bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            code.Append(method).Append("((byte)");
            AppendMemberCount(code, members, partitioned, hasPayload);
            code.AppendLine(");");
        }

        static void EmitMemberWrite(StringBuilder code, Member member, string value, bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            string condition = MemberCondition(member, partitioned, hasPayload);
            if (condition != null) code.Append("if (").Append(condition).AppendLine(") {");
            EmitFullPart(code, member, "writer", partitioned, true);
            EmitValueWrite(code, member.Type, value);
            EmitFullPart(code, member, "writer", partitioned, false);
            if (condition != null) code.AppendLine("}");
        }

        static void EmitMemberRead(StringBuilder code, Member member, string value, bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            string condition = MemberCondition(member, partitioned, hasPayload);
            if (condition != null) code.Append("if (").Append(condition).AppendLine(") {");
            EmitFullPart(code, member, "reader", partitioned, true);
            EmitValueRead(code, member.Type, value);
            EmitFullPart(code, member, "reader", partitioned, false);
            if (condition != null) code.AppendLine("}");
        }

        static void EmitFullPart(StringBuilder code, Member member, string stream, bool partitioned, bool begin)
        {
            if (!partitioned || !HasAttribute(member.Symbol, PayloadAttribute)) return;
            code.Append("if (context != null) context.Part = ").Append(begin ?
                "global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Full" : "part").AppendLine(";");
        }

        static void EmitMemoryPackDeserializeDeclaration(StringBuilder code, string name, bool scopedReferences) =>
            code.Append("public override void Deserialize(ref global::MemoryPack.MemoryPackReader reader, ")
                .Append(scopedReferences ? "scoped ref " : "ref ").Append(name).AppendLine(" value) {");

        static void EmitValueWrite(StringBuilder code, ITypeSymbol type, string value)
        {
            if (RequiresValueFormatter(type))
            {
                code.Append("{ var encodedValue = ").Append(value).AppendLine(";");
                code.Append("writer.GetFormatter<").Append(TypeName(type))
                    .AppendLine(">().Serialize(ref writer, ref encodedValue); }");
            }
            else code.Append("writer.WriteValue(").Append(value).AppendLine(");");
        }

        static void EmitValueRead(StringBuilder code, ITypeSymbol type, string value)
        {
            if (RequiresValueFormatter(type))
                code.Append("reader.GetFormatter<").Append(TypeName(type))
                    .Append(">().Deserialize(ref reader, ref ").Append(value).AppendLine(");");
            else code.Append("reader.ReadValue(ref ").Append(value).AppendLine(");");
        }

        static void EmitMemoryPackUnionWrite(StringBuilder code, INamedTypeSymbol type, Member[] members, Member[] storage,
            bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            Member discriminator = UnionKindMember(type);
            string kind = TypeName(discriminator.Type);
            string kindValue = storage == null ? "value." + Escape(discriminator.Name) : "member_" + discriminator.Name;
            bool zeroCase = HasZeroUnionCase(discriminator.Type, members);
            if (storage != null && !zeroCase)
                code.Append("if ((ushort)").Append(kindValue).AppendLine(" == 0) { writer.WriteUnionHeader(0); return; }");
            else if (storage == null && !zeroCase && members.Any(member => !member.IsCase))
                code.Append("if ((ushort)").Append(kindValue).AppendLine(" == 0) { writer.WriteNullUnionHeader(); return; }");
            code.Append("writer.WriteUnionHeader((ushort)").Append(kindValue).AppendLine(");");
            foreach (Member member in members.Where(member => !member.IsCase))
                EmitMemberWrite(code, member, MemberValue(type, member), partitioned, hasPayload);
            code.AppendLine("if (part == global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Layout) return;");
            code.AppendLine("if (context != null) context.Part = global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Full;");
            code.Append("switch (").Append(kindValue).AppendLine(") {");
            foreach (Member member in members.Where(member => member.IsCase))
            {
                code.Append("case ").Append(kind).Append('.').Append(Escape(member.Name))
                    .AppendLine(":");
                EmitValueWrite(code, member.Type, MemberValue(type, member));
                code.AppendLine("break;");
            }
            if (storage == null && !zeroCase && members.All(member => member.IsCase))
                code.Append("case (").Append(kind).AppendLine(")0: break;");
            code.AppendLine("default: throw new global::MemoryPack.MemoryPackSerializationException(\"值联合分支不属于当前合同。\"); }");
            code.AppendLine("if (context != null) context.Part = part;");
        }

        static void EmitMemoryPackUnionRead(StringBuilder code, INamedTypeSymbol type, Member[] members, Member[] storage,
            bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            string name = TypeName(type);
            Member discriminator = UnionKindMember(type);
            string kind = TypeName(discriminator.Type);
            bool zeroCase = HasZeroUnionCase(discriminator.Type, members);
            code.AppendLine("if (!reader.TryReadUnionHeader(out ushort kind)) { value = default; return; }");
            if (storage != null && !zeroCase)
                code.AppendLine("if (kind == 0) { value = default; return; }");
            if (storage != null)
            {
                EmitStorageRead(code, storage, "value", true, partitioned);
                code.Append("member_").Append(discriminator.Name).Append(" = (").Append(kind).AppendLine(")kind;");
            }
            Member[] common = members.Where(member => !member.IsCase).ToArray();
            foreach (Member member in common)
            {
                if (storage == null) EmitReadMemberInitial(code, member, partitioned);
                EmitMemberRead(code, member, "member_" + member.Name, partitioned, hasPayload);
            }
            code.Append("switch ((").Append(kind).AppendLine(")kind) {");
            foreach (Member member in members.Where(member => member.IsCase))
            {
                code.Append("case ").Append(kind).Append('.').Append(Escape(member.Name)).AppendLine(": {");
                string branch = storage == null ? "branch" : "member_" + member.Name;
                if (storage == null) code.Append(TypeName(member.Type)).AppendLine(" branch = default;");
                code.AppendLine("if (part != global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Layout) {");
                code.AppendLine("if (context != null) context.Part = global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Full;");
                EmitValueRead(code, member.Type, branch);
                code.AppendLine("if (context != null) context.Part = part;");
                code.AppendLine("}");
                if (storage != null)
                    code.Append("value = ").Append(name).Append(".FromValueStorage(")
                        .Append(string.Join(", ", storage.Select(item => Escape(item.Name) + ": member_" + item.Name))).AppendLine("); return; }");
                else
                {
                    code.Append("value = ").Append(name).Append(".From").Append(member.Name).Append('(');
                    foreach (Member item in common) code.Append("member_").Append(item.Name).Append(", ");
                    code.AppendLine("branch); return; }");
                }
            }
            if (storage == null && !zeroCase && common.Length == 0)
                code.Append("case (").Append(kind).AppendLine(")0: value = default; return;");
            code.AppendLine("default: throw new global::MemoryPack.MemoryPackSerializationException(\"值联合分支不属于当前合同。\"); }");
        }
    }
}

