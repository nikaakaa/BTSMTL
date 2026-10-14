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
            var snapshots = MemoryPackSnapshots(context, externalLeaves, IsImplicitReference, ref valid);
            foreach (NativeSnapshotPlan snapshot in snapshots.Values)
                foreach (ITypeSymbol leaf in snapshot.Shapes.SelectMany(SnapshotLeafTypes)) Visit(leaf);
            var preparedStorages = PreparedStorages(context.Compilation);
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
            var preparedPlans = new Dictionary<string, PreparedReadPlan>(StringComparer.Ordinal);
            ITypeSymbol preparedText = context.Compilation.GetTypeByMetadataName("ThirdPersonSimulation.Serialization.MemoryPackContentContext")?
                .GetMembers("ReadText").OfType<IMethodSymbol>().SingleOrDefault()?.ReturnType;
            foreach (var pair in preparedStorages.Where(pair => records.ContainsKey(pair.Key)))
            {
                var type = records[pair.Key];
                PreparedReadPlan plan = CreatePreparedReadPlan(type, pair.Value, ValueMembers(type, IsImplicitReference), IsAccessible, out string reason);
                if (reason == null)
                    reason = ValidatePreparedMembers(type, plan, preparedText, IsImplicitReference, externalLeaves);
                if (reason != null)
                {
                    valid = false;
                    context.ReportDiagnostic(Diagnostic.Create(InvalidMemoryPackAdapter, type.Locations.FirstOrDefault(), Identity(type), reason));
                }
                else preparedPlans.Add(pair.Key, plan);
            }
            if (!valid) return;
            ITypeSymbol[] adaptedSequences = sequences.Values.Where(type =>
                HasAdapter(SequenceElement(type))).ToArray();
            INamedTypeSymbol[] adaptedNullableValues = nullableValues.Values.Where(type =>
                HasAdapter(NullableElement(type))).ToArray();
            if (records.Count + adaptedSequences.Length + adaptedNullableValues.Length + snapshots.Count == 0) return;
            var payloadTypes = new HashSet<string>(StringComparer.Ordinal);
            bool changed;
            do
            {
                changed = false;
                foreach (var record in records.Values)
                    if (ValueMembers(record, IsImplicitReference).Any(member => member.IsCase || HasAttribute(member.Symbol, PayloadAttribute) || HasPayload(member.Type)))
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
            int snapshotOrdinal = 0;
            foreach (var snapshot in snapshots.Values)
                code.Append("global::MemoryPack.MemoryPackFormatterProvider.Register(new SnapshotFormatter")
                    .Append(snapshotOrdinal++).AppendLine("());");
            code.AppendLine("}");
            ordinal = 0;
            bool scopedReferences = (int)((Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)context.ParseOptions).LanguageVersion >= 1100;
            foreach (var record in records.Values)
                EmitMemoryPackFormatter(code, record, ValueMembers(record, IsImplicitReference),
                    StorageReadMembers(record, IsImplicitReference), ordinal++, scopedReferences, HasPayload, IsAccessible,
                    preparedPlans.TryGetValue(Identity(record), out var plan) ? plan : null,
                    SymbolEqualityComparer.Default.Equals(preparedText, record));
            foreach (var sequence in adaptedSequences)
                EmitMemoryPackSequenceFormatter(code, sequence, ordinal++, scopedReferences);
            foreach (var nullable in adaptedNullableValues)
                EmitMemoryPackNullableFormatter(code, nullable, ordinal++, scopedReferences);
            snapshotOrdinal = 0;
            foreach (var snapshot in snapshots.Values)
                EmitMemoryPackSnapshotFormatter(code, snapshot, snapshotOrdinal++, scopedReferences);
            code.AppendLine("} }");
            context.AddSource("MemoryPackGeneratedFormatters.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));

            void Visit(ITypeSymbol type)
            {
                if (type is INamedTypeSymbol namedType) type = RecordType(namedType);
                if (type is ITypeParameterSymbol || type.IsUnmanagedType)
                    return;
                if (!visited.Add(Identity(type))) return;
                if (snapshots.ContainsKey(Identity(type)) || Identity(type) == SnapshotCopyContract) return;
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
                Member[] members = ValueMembers(named, IsImplicitReference);
                if (HasAttribute(named, TextAttribute))
                {
                    records.Add(Identity(type), named);
                    return;
                }
                if (HasAttribute(named, "MemoryPack.MemoryPackableAttribute"))
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

        static void EmitMemoryPackSequenceFormatter(StringBuilder code, ITypeSymbol type, int ordinal, bool scopedReferences)
        {
            string name = TypeName(type);
            string element = TypeName(SequenceElement(type));
            string shape = type is INamedTypeSymbol named ? Identity(named.OriginalDefinition) : "array";
            bool list = shape == "System.Collections.Generic.List<T>";
            bool readOnlyCollection = shape == "System.Collections.ObjectModel.ReadOnlyCollection<T>";
            bool memory = shape == "System.ReadOnlyMemory<T>";
            bool enumerable = shape == "System.Collections.Generic.IEnumerable<T>";
            string count = enumerable ? "items.Length" : type is IArrayTypeSymbol || memory ? "value.Length" : "value.Count";
            code.Append("sealed class Formatter").Append(ordinal).Append(" : global::MemoryPack.MemoryPackFormatter<")
                .Append(name).AppendLine("> {");
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            if (type.IsReferenceType)
                code.AppendLine("if (value == null) { writer.WriteNullCollectionHeader(); return; }");
            if (enumerable) code.AppendLine("var items = global::System.Linq.Enumerable.ToArray(value);");
            code.Append("writer.WriteCollectionHeader(").Append(count).AppendLine(");");
            code.Append("for (int index = 0; index < ").Append(count).Append("; index++) writer.WriteValue(")
                .Append(enumerable ? "items" : "value")
                .Append(memory ? ".Span" : "").AppendLine("[index]);");
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
                code.AppendLine("reader.ReadValue(ref item); value.Add(item); }");
            }
            else
            {
                if (enumerable) code.AppendLine("var previous = merge ? global::System.Linq.Enumerable.ToArray(value) : null;");
                code.Append("var items = new ").Append(element).AppendLine("[count];");
                code.AppendLine("for (int index = 0; index < count; index++) {");
                code.Append("if (merge) items[index] = ").Append(enumerable ? "previous" : "value")
                    .Append(memory ? ".Span" : "").AppendLine("[index];");
                code.AppendLine("reader.ReadValue(ref items[index]); }");
                if (readOnlyCollection)
                    code.Append("value = new global::System.Collections.ObjectModel.ReadOnlyCollection<").Append(element).AppendLine(">(items);");
                else code.AppendLine("value = items;");
            }
            code.AppendLine("} }");
        }

        static void EmitMemoryPackNullableFormatter(StringBuilder code, INamedTypeSymbol type, int ordinal, bool scopedReferences)
        {
            string name = TypeName(type);
            string element = TypeName(NullableElement(type));
            code.Append("sealed class Formatter").Append(ordinal).Append(" : global::MemoryPack.MemoryPackFormatter<")
                .Append(name).AppendLine("> {");
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            code.AppendLine("if (!value.HasValue) { writer.WriteNullObjectHeader(); return; }");
            code.AppendLine("writer.WriteObjectHeader(1);");
            code.AppendLine("writer.WriteValue(value.Value);");
            code.AppendLine("}");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            code.AppendLine("if (!reader.TryReadObjectHeader(out byte count)) { value = default; return; }");
            code.AppendLine("if (count != 1) throw new global::MemoryPack.MemoryPackSerializationException(\"可空值成员数与当前格式不符。\");");
            code.AppendLine("var item = value.GetValueOrDefault();");
            code.AppendLine("reader.ReadValue(ref item); value = item;");
            code.AppendLine("} }");
        }

        static bool IsMemoryPackValueCall(IMethodSymbol method)
        {
            string owner = Identity(method.ContainingType.OriginalDefinition);
            return owner == "MemoryPack.MemoryPackSerializer" &&
                (method.Name == "Serialize" || method.Name == "Deserialize") ||
                owner == "ThirdPersonSimulation.Serialization.MemoryPackContent" &&
                (method.Name == "Serialize" || method.Name == "SerializeInto" || method.Name == "Deserialize" ||
                    method.Name == "DeserializeParts" || method.Name == "DeserializeInto") ||
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
            PreparedReadPlan prepared, bool preparedText)
        {
            if (HasAttribute(type, TextAttribute))
            {
                EmitMemoryPackTextFormatter(code, type, ordinal, scopedReferences, preparedText);
                return;
            }
            string name = TypeName(type);
            ISymbol presence = type.GetMembers().SingleOrDefault(member => HasAttribute(member, PresenceAttribute));
            bool partitioned = hasPayload(type);
            code.Append("sealed class Formatter").Append(ordinal).Append(" : global::MemoryPack.MemoryPackFormatter<")
                .Append(name).AppendLine("> {");
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            if (type.IsReferenceType)
                code.AppendLine("if (value == null) { writer.WriteNullObjectHeader(); return; }");
            if (presence != null)
                code.Append("if (!value.").Append(Escape(presence.Name)).AppendLine(") { writer.WriteNullObjectHeader(); return; }");
            EmitPartContext(code, "writer", partitioned);
            EmitStorageRead(code, storage, "value", false, partitioned);
            if (members.Any(member => member.IsCase))
                EmitMemoryPackUnionWrite(code, type, members, partitioned, hasPayload);
            else
            {
                EmitObjectHeader(code, "writer.WriteObjectHeader", members, partitioned, hasPayload);
                foreach (Member member in members)
                    EmitMemberWrite(code, member, MemberValue(type, member), partitioned, hasPayload);
            }
            code.AppendLine("}");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            if (prepared != null)
            {
                code.AppendLine("var preparedContext = reader.Options.ServiceProvider as global::ThirdPersonSimulation.Serialization.MemoryPackContentContext;");
                code.AppendLine("if (preparedContext != null && preparedContext.IsPrepared) {");
                code.Append("value = ReadInto(ref reader, (").Append(TypeName(prepared.Storage))
                    .AppendLine(")preparedContext.PreparedStorage); return; }");
            }
            EmitPartContext(code, "reader", partitioned);
            if (members.Any(member => member.IsCase))
                EmitMemoryPackUnionRead(code, type, members, partitioned, hasPayload);
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
                        .Append(string.Join(", ", storage.Select(member => "member_" + member.Name))).AppendLine(");");
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

        static void EmitPartContext(StringBuilder code, string stream, bool partitioned)
        {
            if (!partitioned) return;
            code.Append("var context = ").Append(stream).AppendLine(".Options.ServiceProvider as global::ThirdPersonSimulation.Serialization.MemoryPackContentContext;");
            code.AppendLine("var part = context == null ? global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Full : context.Part;");
        }

        static void EmitMemoryPackTextFormatter(StringBuilder code, INamedTypeSymbol type, int ordinal, bool scopedReferences, bool prepared)
        {
            string name = TypeName(type);
            IMethodSymbol factory = type.GetMembers().OfType<IMethodSymbol>().Single(method =>
                method.MethodKind == MethodKind.Ordinary && method.IsStatic &&
                method.DeclaredAccessibility == Accessibility.Public &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, type) && method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String);
            code.Append("sealed class Formatter").Append(ordinal).Append(" : global::MemoryPack.MemoryPackFormatter<")
                .Append(name).AppendLine("> {");
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            if (type.IsReferenceType)
                code.AppendLine("if (value == null) { writer.WriteNullCollectionHeader(); return; }");
            code.AppendLine("if (value.IsNull) { writer.WriteNullCollectionHeader(); return; }");
            code.AppendLine("using var read = value.AcquireRead();");
            code.AppendLine("writer.WriteUtf16(value.Span);");
            code.AppendLine("}");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            if (prepared)
            {
                code.AppendLine("var context = reader.Options.ServiceProvider as global::ThirdPersonSimulation.Serialization.MemoryPackContentContext;");
                code.AppendLine("if (context != null && context.IsPrepared) { value = context.ReadText(ref reader); return; }");
            }
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
                .Append(string.Join(", ", storage.Select(member => (reading ? "out member_" : "out var member_") + member.Name)))
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
            code.Append("writer.WriteValue(").Append(value).AppendLine(");");
            EmitFullPart(code, member, "writer", partitioned, false);
            if (condition != null) code.AppendLine("}");
        }

        static void EmitMemberRead(StringBuilder code, Member member, string value, bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            string condition = MemberCondition(member, partitioned, hasPayload);
            if (condition != null) code.Append("if (").Append(condition).AppendLine(") {");
            EmitFullPart(code, member, "reader", partitioned, true);
            if (member.Type.SpecialType == SpecialType.System_String && DeclaredText(member.Symbol))
            {
                code.AppendLine("{");
                code.AppendLine("var textContext = reader.Options.ServiceProvider as global::ThirdPersonSimulation.Serialization.MemoryPackContentContext;");
                code.Append("if (textContext != null && textContext.IsPrepared) ").Append(value)
                    .AppendLine(" = textContext.ReadDeclaredText(ref reader);");
                code.Append("else reader.ReadValue(ref ").Append(value).AppendLine(");");
                code.AppendLine("}");
            }
            else code.Append("reader.ReadValue(ref ").Append(value).AppendLine(");");
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

        static void EmitMemoryPackUnionWrite(StringBuilder code, INamedTypeSymbol type, Member[] members,
            bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            string kind = TypeName(((IPropertySymbol)type.GetMembers("Kind").Single()).Type);
            code.AppendLine("writer.WriteUnionHeader((ushort)value.Kind);");
            foreach (Member member in members.Where(member => !member.IsCase))
                EmitMemberWrite(code, member, "value." + Escape(member.Name), partitioned, hasPayload);
            code.AppendLine("if (part == global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Layout) return;");
            code.AppendLine("if (context != null) context.Part = global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Full;");
            code.AppendLine("switch (value.Kind) {");
            foreach (Member member in members.Where(member => member.IsCase))
                code.Append("case ").Append(kind).Append('.').Append(Escape(member.Name))
                    .Append(": writer.WriteValue(value.").Append(Escape(member.Name)).AppendLine("); break;");
            if (members.All(member => member.IsCase))
                code.Append("case (").Append(kind).AppendLine(")0: break;");
            code.AppendLine("default: throw new global::MemoryPack.MemoryPackSerializationException(\"值联合分支不属于当前合同。\"); }");
            code.AppendLine("if (context != null) context.Part = part;");
        }

        static void EmitMemoryPackUnionRead(StringBuilder code, INamedTypeSymbol type, Member[] members,
            bool partitioned, Func<ITypeSymbol, bool> hasPayload)
        {
            string name = TypeName(type);
            string kind = TypeName(((IPropertySymbol)type.GetMembers("Kind").Single()).Type);
            code.AppendLine("if (!reader.TryReadUnionHeader(out ushort kind)) { value = default; return; }");
            Member[] common = members.Where(member => !member.IsCase).ToArray();
            foreach (Member member in common)
            {
                EmitReadMemberInitial(code, member, partitioned);
                EmitMemberRead(code, member, "member_" + member.Name, partitioned, hasPayload);
            }
            code.Append("switch ((").Append(kind).AppendLine(")kind) {");
            foreach (Member member in members.Where(member => member.IsCase))
            {
                code.Append("case ").Append(kind).Append('.').Append(Escape(member.Name)).AppendLine(": {");
                code.Append(TypeName(member.Type)).AppendLine(" branch = default;");
                code.AppendLine("if (part != global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Layout) {");
                code.AppendLine("if (context != null) context.Part = global::ThirdPersonSimulation.Serialization.MemoryPackContentPart.Full;");
                code.AppendLine("reader.ReadValue(ref branch);");
                code.AppendLine("if (context != null) context.Part = part;");
                code.AppendLine("}");
                code.Append("value = ").Append(name).Append(".From").Append(member.Name).Append('(');
                foreach (Member item in common) code.Append("member_").Append(item.Name).Append(", ");
                code.AppendLine("branch); return; }");
            }
            if (common.Length == 0) code.Append("case (").Append(kind).AppendLine(")0: value = default; return;");
            code.AppendLine("default: throw new global::MemoryPack.MemoryPackSerializationException(\"值联合分支不属于当前合同。\"); }");
        }
    }
}

