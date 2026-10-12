using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ThirdPerson.CanonicalSerialization.Generator
{
    [Generator]
    public sealed class CanonicalGenerator : ISourceGenerator
    {
        const string ContractAttribute = "ThirdPersonSimulation.CanonicalContractAttribute";
        const string UnionCaseAttribute = "ThirdPersonSimulation.CanonicalUnionCaseAttribute";
        const string PayloadAttribute = "ThirdPersonSimulation.CanonicalPayloadAttribute";
        const string UnionTemplateAttribute = "ThirdPersonSimulation.CanonicalUnionTemplateAttribute";
        const string UnionAttribute = "ThirdPersonSimulation.CanonicalUnionAttribute";
        const string ValidationAttribute = "ThirdPersonSimulation.CanonicalValidationAttribute";
        const string SchemaConstantAttribute = "ThirdPersonSimulation.CanonicalSchemaConstantAttribute";
        const string TextType = "ThirdPersonSimulation.CanonicalTextValue";
        const string BufferType = "ThirdPersonSimulation.CanonicalBuffer<T>";
        const string BorrowedSequenceAttribute = "ThirdPersonSimulation.CanonicalBorrowedSequenceAttribute";
        const string DeclaredTextAttribute = "ThirdPersonSimulation.CanonicalDeclaredTextAttribute";
        const string HashValueType = "ThirdPersonSimulation.ICanonicalHashValue";
        const string HashType = "ThirdPersonSimulation.StableHash";
        const string Runtime = "global::ThirdPersonSimulation.";

        static readonly DiagnosticDescriptor InvalidContract = new DiagnosticDescriptor(
            "CANON001", "正式数据无法生成", "{0}: {1}", "CanonicalSerialization",
            DiagnosticSeverity.Error, true);

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new Receiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var receiver = (Receiver)context.SyntaxReceiver;
            var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var templates = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var kindTrees = new List<SyntaxTree>();
            foreach (TypeDeclarationSyntax declaration in receiver.Types)
            {
                var type = (INamedTypeSymbol)context.Compilation.GetSemanticModel(declaration.SyntaxTree)
                    .GetDeclaredSymbol(declaration);
                if (HasAttribute(type, ContractAttribute))
                    types[Identity(type)] = type;
                if (HasAttribute(type, UnionTemplateAttribute))
                    templates[Identity(type)] = type;
            }
            foreach (INamedTypeSymbol template in templates.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
            {
                Member[] cases = Cases(StorageMembers(template));
                if (template.TypeKind != TypeKind.Struct || cases.Length == 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidContract, template.Locations.FirstOrDefault(),
                        Identity(template), "联合模板必须在正式 struct 成员声明闭合分支"));
                    continue;
                }
                var code = new StringBuilder();
                code.Append("namespace ").Append(template.ContainingNamespace.ToDisplayString()).AppendLine(" {");
                EmitKind(code, template, cases);
                code.AppendLine("}");
                context.AddSource(Identity(template).Replace('.', '_').Replace('<', '_').Replace('>', '_') + ".Kind.g.cs",
                    SourceText.From(code.ToString(), Encoding.UTF8));
                kindTrees.Add(CSharpSyntaxTree.ParseText(code.ToString(), (CSharpParseOptions)context.ParseOptions));
            }
            foreach (INamedTypeSymbol type in types.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
            {
                Member[] cases = Cases(StorageMembers(type));
                if (cases.Length == 0 || HasAttribute(type, UnionAttribute))
                    continue;
                var code = new StringBuilder();
                code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
                EmitKind(code, type, cases);
                code.AppendLine("}");
                context.AddSource(Identity(type).Replace('.', '_') + ".Kind.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));
                kindTrees.Add(CSharpSyntaxTree.ParseText(code.ToString(), (CSharpParseOptions)context.ParseOptions));
            }
            Compilation compilation = context.Compilation.AddSyntaxTrees(kindTrees);
            var planner = new Planner(context, compilation);
            foreach (INamedTypeSymbol type in types.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
            {
                SyntaxReference declaration = type.DeclaringSyntaxReferences[0];
                planner.Plan((INamedTypeSymbol)compilation.GetSemanticModel(declaration.SyntaxTree)
                    .GetDeclaredSymbol(declaration.GetSyntax()));
            }
            foreach (Plan plan in planner.Plans.OrderBy(p => Identity(p.Type), StringComparer.Ordinal))
            {
                if (!plan.Valid)
                    continue;
                context.AddSource(Identity(plan.Type).Replace('.', '_') + ".Canonical.g.cs",
                    SourceText.From(Emit(plan), Encoding.UTF8));
            }
        }

        static string Emit(Plan plan)
        {
            var code = new StringBuilder();
            INamedTypeSymbol type = plan.Type;
            string fullType = TypeName(type);
            string name = Escape(type.Name);
            bool union = plan.Cases.Length != 0;
            Member[] common = plan.Members.Where(m => !m.IsCase).ToArray();
            string accessibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            if (union)
                code.AppendLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Auto)]");
            code.Append(accessibility).Append(type.IsReadOnly ? " readonly" : "")
                .Append(" partial ").Append(type.TypeKind == TypeKind.Struct ? "struct " : "class ")
                .Append(name).AppendLine(" {");
            foreach (Member member in plan.Members.Where(m => m.Generated))
                code.Append("public ").Append(TypeName(member.Type)).Append(' ')
                    .Append(Escape(member.Symbol.Name)).AppendLine(" { get; }");
            code.Append("public const string CanonicalSchema = \"").Append(plan.Schema).AppendLine("\";");
            code.Append("public const int CanonicalMinimumSize = ").Append(plan.MinimumSize).AppendLine(";");
            code.Append("public const bool CanonicalHasResources = ").Append(plan.HasResources ? "true" : "false").AppendLine(";");
            code.Append("public const bool CanonicalHasTexts = ").Append(plan.HasTexts ? "true" : "false").AppendLine(";");
            code.Append("public const bool CanonicalHasPayload = ").Append(plan.HasPayload ? "true" : "false").AppendLine(";");
            code.Append("public static readonly ").Append(Runtime).Append("StableHash CanonicalSchemaHash = new ")
                .Append(Runtime).AppendLine("StableHash(CanonicalSchema);");
            code.Append("public static void CanonicalRequireSchema(").Append(Runtime)
                .Append("CanonicalReader reader) => reader.RequireSchema(CanonicalSchemaHash, ")
                .Append(SymbolDisplay.FormatLiteral(Identity(type), true)).AppendLine(");");
            code.Append("public const string CanonicalStructure = ")
                .Append(SymbolDisplay.FormatLiteral(plan.Descriptor, true)).AppendLine(";");
            if (plan.HashValue)
            {
                code.Append("public ").Append(Runtime).Append("StableHash CanonicalHashValue => this.")
                    .Append(Escape(plan.Members[0].Symbol.Name)).AppendLine(";");
                code.Append("public static ").Append(fullType).Append(" CanonicalFromHash(").Append(Runtime)
                    .Append("StableHash value) => new ").Append(fullType).Append("(default(")
                    .Append(Runtime).AppendLine("CanonicalConstruction), value);");
            }
            if (union)
                code.Append("public ").Append(plan.KindType).AppendLine(" Kind { get; }");

            if (type.TypeKind == TypeKind.Struct && (type.IsReadOnly || union || !plan.PreparedRead) ||
                type.TypeKind == TypeKind.Class && union)
            {
                code.Append("private ").Append(name).Append('(').Append(Runtime)
                    .Append("CanonicalConstruction construction");
                if (union)
                    code.Append(", ").Append(plan.KindType).Append(" kind");
                for (int i = 0; i < plan.Members.Length; i++)
                    code.Append(", ").Append(TypeName(plan.Members[i].Type)).Append(" value").Append(i);
                code.AppendLine(") {");
                if (union)
                    code.AppendLine("Kind = kind;");
                for (int i = 0; i < plan.Members.Length; i++)
                {
                    Member member = plan.Members[i];
                    string value = "value" + i;
                    code.Append("this.").Append(Escape(member.Symbol.Name)).Append(" = ")
                        .Append(Normalize(member, value)).AppendLine(";");
                }
                foreach (IMethodSymbol validation in plan.Validations)
                    code.Append(Escape(validation.Name)).AppendLine("();");
                code.AppendLine("}");
            }
            foreach (Member branch in plan.Cases)
            {
                code.Append("public static ").Append(fullType).Append(" From").Append(branch.Symbol.Name).Append('(');
                code.Append(string.Join(", ", common.Select(m => TypeName(m.Type) + " " + Parameter(m.Symbol.Name))));
                if (common.Length > 0)
                    code.Append(", ");
                code.Append(TypeName(branch.Type)).AppendLine(" value) {");
                if (!plan.PreparedRead && SequenceElement(branch.Type) != null)
                    code.Append("value = ").Append(FreezeSequence(branch.Type, "value")).AppendLine(";");
                code.Append("return new ").Append(fullType).Append("(default(").Append(Runtime)
                    .Append("CanonicalConstruction), ").Append(plan.KindType).Append('.').Append(Escape(branch.Symbol.Name));
                foreach (Member member in plan.Members)
                    code.Append(", ").Append(member == branch ? "value" : member.IsCase ? "default" :
                        !plan.PreparedRead && SequenceElement(member.Type) != null ? FreezeSequence(member.Type, Parameter(member.Symbol.Name)) : Parameter(member.Symbol.Name));
                code.AppendLine("); }");
            }
            if (union && type.TypeKind == TypeKind.Struct)
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalDefault(").Append(plan.KindType).Append(" kind");
                foreach (Member member in common)
                    code.Append(", ").Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name));
                code.AppendLine(") { switch (kind) {");
                foreach (Member member in plan.Cases)
                {
                    code.Append("case ").Append(plan.KindType).Append('.').Append(Escape(member.Symbol.Name))
                        .Append(": return From").Append(member.Symbol.Name).Append('(');
                    foreach (Member value in common)
                        code.Append(Parameter(value.Symbol.Name)).Append(", ");
                    code.Append("default(").Append(TypeName(member.Type)).AppendLine("));");
                }
                code.AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(kind)); } }");
            }

            code.Append("public static void CanonicalWrite<TWriter>(TWriter writer, in ")
                .Append(fullType).Append(" value) where TWriter : class, ").Append(Runtime).AppendLine("ICanonicalValueWriter {");
            foreach (Member member in common)
                Write(code, member.Type, "value." + Escape(member.Symbol.Name));
            if (union)
            {
                code.AppendLine("writer.WriteInt32((int)value.Kind);");
                Switch(code, plan, "value.Kind", member => Write(code, member.Type, "value." + Escape(member.Symbol.Name)));
            }
            code.AppendLine("}");

            if (union)
            {
                code.Append("public static void CanonicalWriteLayout<TWriter>(TWriter writer, in ").Append(fullType)
                    .Append(" value) where TWriter : class, ").Append(Runtime).AppendLine("ICanonicalValueWriter {");
                foreach (Member member in common)
                    Write(code, member.Type, "value." + Escape(member.Symbol.Name));
                code.AppendLine("writer.WriteInt32((int)value.Kind); }");
                code.Append("public static void CanonicalWritePayload<TWriter>(TWriter writer, in ").Append(fullType)
                    .Append(" value) where TWriter : class, ").Append(Runtime).AppendLine("ICanonicalValueWriter {");
                Switch(code, plan, "value.Kind", member => Write(code, member.Type, "value." + Escape(member.Symbol.Name)));
                code.AppendLine("}");
            }
            else if (plan.HasPayload && !plan.PreparedRead)
                EmitParts(code, plan);

            if (plan.PreparedRead)
            {
                code.Append("public static void CanonicalReadInto<TReader>(TReader reader, ")
                    .Append(type.TypeKind == TypeKind.Struct ? "ref " : "").Append(fullType).Append(" value")
                    .Append(plan.PreparedStorage == null ? "" : ", " + TypeName(plan.PreparedStorage) + " storage")
                    .Append(") where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                if (type.TypeKind == TypeKind.Struct && (type.IsReadOnly || union))
                {
                    if (union)
                    {
                        foreach (Member member in common)
                            code.Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name))
                                .Append(" = ").Append(ReadMember(member, plan)).AppendLine(";");
                        code.Append("value = CanonicalReadPayload(reader");
                        foreach (Member member in common)
                            code.Append(", ").Append(Parameter(member.Symbol.Name));
                        code.AppendLine(");");
                    }
                    else
                    {
                        code.Append("value = new ").Append(fullType).Append("(default(").Append(Runtime).Append("CanonicalConstruction)");
                        foreach (Member member in plan.Members)
                            code.Append(", ").Append(ReadMember(member, plan));
                        code.AppendLine(");");
                    }
                }
                else
                {
                    foreach (Member member in plan.Members)
                    {
                        string target = "value." + Escape(member.Symbol.Name);
                        ITypeSymbol element = BufferElement(member.Type);
                        if (element != null)
                        {
                            code.AppendLine("{ int count = reader.ReadInt32();");
                            code.Append(target).AppendLine(".PrepareRead(count);");
                            code.AppendLine("for (int i = 0; i < count; i++) {");
                            if (Primitive(element) != null || Identity(element) == TextType)
                                code.Append(target).Append(".Element(i) = ").Append(Read(element)).AppendLine(";");
                            else
                                code.Append(TypeName(element)).Append(".CanonicalReadInto(reader, ref ")
                                    .Append(target).AppendLine(".Element(i));");
                            Hold(code, "Retain", element, target + ".Element(i)", plan);
                            code.Append(target).AppendLine(".CompleteReadElement();");
                            code.AppendLine("} }");
                        }
                        else if (member.Type.TypeKind == TypeKind.Class && Identity(member.Type) != TextType)
                            code.Append(TypeName(member.Type)).Append(".CanonicalReadInto(reader, ").Append(target).AppendLine(");");
                        else if (Primitive(member.Type) == null && Identity(member.Type) != TextType && member.Symbol is IFieldSymbol)
                            code.Append(TypeName(member.Type)).Append(".CanonicalReadInto(reader, ref ").Append(target).AppendLine(");");
                        else
                            code.Append(target).Append(" = ").Append(ReadMember(member, plan)).AppendLine(";");
                    }
                    foreach (IMethodSymbol validation in plan.Validations)
                        code.Append("value.").Append(Escape(validation.Name)).AppendLine("();");
                }
                code.AppendLine("}");
            }
            if (type.TypeKind == TypeKind.Struct && plan.PreparedRead && (type.IsReadOnly || union || plan.MaterializedRead))
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalRead<TReader>(TReader reader")
                    .Append(plan.PreparedStorage == null ? "" : ", " + TypeName(plan.PreparedStorage) + " storage")
                    .Append(") where TReader : class, ")
                    .Append(Runtime).AppendLine("ICanonicalValueReader {");
                code.Append(fullType).AppendLine(" value = default;");
                code.Append("CanonicalReadInto(reader, ref value").Append(plan.PreparedStorage == null ? "" : ", storage")
                    .AppendLine("); return value; }");
            }
            else if (type.TypeKind == TypeKind.Class && plan.MaterializedRead)
            {
                code.Append("private ").Append(name).Append('(').Append(Runtime).Append("CanonicalConstruction construction, ")
                    .Append(Runtime).Append("ICanonicalValueReader reader");
                if (union)
                    code.Append(", ").Append(Runtime).Append("ICanonicalValueReader payloadReader");
                code.AppendLine(") {");
                foreach (Member member in union ? common : plan.Members)
                {
                    string target = "this." + Escape(member.Symbol.Name);
                    ITypeSymbol element = BufferElement(member.Type);
                    if (element != null)
                    {
                        code.AppendLine("{ int count = reader.ReadInt32();");
                        code.Append("if (count < 0 || count > reader.Remaining / ").Append(Minimum(element)).AppendLine(") throw new global::System.IO.InvalidDataException(\"Canonical collection length is invalid.\");");
                        code.Append(target).Append(" = new ").Append(TypeName(member.Type)).AppendLine("(count);");
                        code.AppendLine("for (int i = 0; i < count; i++) {");
                        code.Append(target).Append(".Element(i) = ").Append(Read(element, true)).AppendLine(";");
                        code.Append(target).AppendLine(".CompleteReadElement(); } }");
                    }
                    else
                        code.Append(target).Append(" = ").Append(Read(member.Type, true)).AppendLine(";");
                }
                if (union)
                {
                    code.AppendLine("switch (reader.ReadInt32()) {");
                    foreach (Member member in plan.Cases)
                    {
                        string kind = plan.KindType + "." + Escape(member.Symbol.Name);
                        code.Append("case (int)").Append(kind).Append(": Kind = ").Append(kind).AppendLine(";");
                        code.Append("this.").Append(Escape(member.Symbol.Name)).Append(" = ")
                            .Append(ReadMember(member, plan).Replace("(reader)", "(payloadReader)")
                                .Replace("reader.", "payloadReader.")).AppendLine("; break;");
                    }
                    code.AppendLine("default: throw new global::System.IO.InvalidDataException(\"Canonical union case is invalid.\"); }");
                }
                foreach (IMethodSymbol validation in plan.Validations)
                    code.Append(Escape(validation.Name)).AppendLine("();");
                code.AppendLine("}");
                code.Append("public static ").Append(fullType).Append(" CanonicalReadMaterialized<TReader>(TReader reader) where TReader : class, ")
                    .Append(Runtime).Append("ICanonicalValueReader => new ").Append(fullType)
                    .Append("(default(").Append(Runtime).Append("CanonicalConstruction), reader")
                    .AppendLine(union ? ", reader);" : ");");
                if (union)
                    code.Append("public static ").Append(fullType)
                        .Append(" CanonicalReadMaterialized<TLayoutReader, TPayloadReader>(TLayoutReader layoutReader, TPayloadReader payloadReader) ")
                        .Append("where TLayoutReader : class, ").Append(Runtime).Append("ICanonicalValueReader ")
                        .Append("where TPayloadReader : class, ").Append(Runtime).Append("ICanonicalValueReader => new ")
                        .Append(fullType).Append("(default(").Append(Runtime).AppendLine("CanonicalConstruction), layoutReader, payloadReader);");
            }
            else if (type.TypeKind == TypeKind.Struct && plan.MaterializedRead && !plan.PreparedRead)
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalReadMaterialized<TReader>(TReader reader) where TReader : class, ")
                    .Append(Runtime).AppendLine("ICanonicalValueReader {");
                if (union)
                    code.Append("return CanonicalReadPayload(reader");
                else
                    code.Append("return new ").Append(fullType).Append("(default(").Append(Runtime).Append("CanonicalConstruction)");
                foreach (Member member in union ? common : plan.Members)
                    code.Append(", ").Append(Read(member.Type, true));
                code.AppendLine("); }");
            }
            if (type.TypeKind == TypeKind.Struct && type.IsReadOnly && !union && plan.MaterializedRead && plan.PreparedRead)
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalReadMaterialized<TReader>(TReader reader) where TReader : class, ")
                    .Append(Runtime).Append("ICanonicalValueReader => new ").Append(fullType)
                    .Append("(default(").Append(Runtime).Append("CanonicalConstruction)");
                foreach (Member member in plan.Members)
                    code.Append(", ").Append(Read(member.Type, true));
                code.AppendLine(");");
            }
            foreach (Member member in plan.Cases.Where(m => CaseSemantic(m, "NormalizeZero") || CaseSemantic(m, "NormalizeNull")))
            {
                code.Append("private static ").Append(TypeName(member.Type)).Append(" __CanonicalRead_")
                    .Append(member.Symbol.Name).Append("<TReader>(TReader reader) where TReader : class, ")
                    .Append(Runtime).AppendLine("ICanonicalValueReader {");
                code.Append(TypeName(member.Type)).Append(" value = ").Append(Read(member.Type, !plan.PreparedRead)).AppendLine(";");
                if (CaseSemantic(member, "NormalizeZero"))
                    code.Append("if (value == 0 && global::System.BitConverter.")
                        .Append(member.Type.SpecialType == SpecialType.System_Single ? "SingleToInt32Bits" : "DoubleToInt64Bits")
                        .AppendLine("(value) != 0) throw new global::System.IO.InvalidDataException(\"Canonical zero must be positive.\");");
                if (CaseSemantic(member, "NormalizeNull"))
                    code.AppendLine("if (value.IsNull) throw new global::System.IO.InvalidDataException(\"Canonical normalized text cannot be null.\");");
                code.AppendLine("return value; }");
            }
            foreach (ITypeSymbol sequence in BorrowedSequences(plan.Members.Select(m => m.Type)))
            {
                ITypeSymbol element = SequenceElement(sequence);
                code.Append("private static ").Append(TypeName(sequence)).Append(' ').Append(SequenceReader(sequence))
                    .Append("<TReader>(TReader reader, ").Append(TypeName(BorrowedStorage(sequence)))
                    .Append(" storage) where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                code.AppendLine("int count = reader.ReadInt32();");
                code.AppendLine("var values = storage.Reserve(count, out var items);");
                code.Append("for (int i = 0; i < count; i++) values[i] = ").Append(Read(element)).AppendLine(";");
                code.AppendLine("return items; }");
            }
            foreach (ITypeSymbol sequence in InitializationSequences(plan.Members.Select(m => m.Type)))
            {
                ITypeSymbol element = SequenceElement(sequence);
                code.Append("private static ").Append(TypeName(sequence)).Append(' ')
                    .Append(SequenceReader(sequence)).Append("<TReader>(TReader reader) where TReader : class, ")
                    .Append(Runtime).AppendLine("ICanonicalValueReader {");
                if (IsByteSequence(sequence))
                {
                    code.AppendLine(IsReadOnlyMemory(sequence) ? "return new " + TypeName(sequence) + "(reader.ReadBytes()); }" :
                        "return reader.ReadBytes(); }");
                    continue;
                }
                code.AppendLine("int count = reader.ReadInt32();");
                code.Append("if (count < 0 || count > reader.Remaining / ").Append(Minimum(element))
                    .AppendLine(") throw new global::System.IO.InvalidDataException(\"Canonical collection length is invalid.\");");
                code.Append("var values = new ").Append(TypeName(element)).AppendLine("[count];");
                code.Append("for (int i = 0; i < count; i++) values[i] = ").Append(Read(element, true)).AppendLine(";");
                code.AppendLine(sequence is IArrayTypeSymbol ? "return values; }" : IsReadOnlyMemory(sequence) ?
                    "return new " + TypeName(sequence) + "(values); }" : "return global::System.Array.AsReadOnly(values); }");
            }
            if (union && type.TypeKind == TypeKind.Struct)
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalReadPayload<TReader>(TReader reader");
                foreach (Member member in common)
                    code.Append(", ").Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name));
                code.Append(") where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                code.Append("return CanonicalReadCase(reader, reader.ReadInt32()");
                foreach (Member member in common)
                    code.Append(", ").Append(Parameter(member.Symbol.Name));
                code.AppendLine("); }");
                code.Append("public static ").Append(fullType).Append(" CanonicalReadCase<TReader>(TReader reader, ")
                    .Append("int kind");
                foreach (Member member in common)
                    code.Append(", ").Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name));
                code.Append(") where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                code.AppendLine("switch (kind) {");
                foreach (Member member in plan.Cases)
                {
                    code.Append("case (int)").Append(plan.KindType).Append('.').Append(Escape(member.Symbol.Name))
                        .Append(": return new ").Append(fullType).Append("(default(").Append(Runtime)
                        .Append("CanonicalConstruction), ").Append(plan.KindType).Append('.').Append(Escape(member.Symbol.Name));
                    foreach (Member value in plan.Members)
                        code.Append(", ").Append(value == member ? ReadMember(member, plan) : value.IsCase ? "default" : Parameter(value.Symbol.Name));
                    code.AppendLine(");");
                }
                code.AppendLine("default: throw new global::System.IO.InvalidDataException(\"Canonical union case is invalid.\"); } }");
                if (plan.MaterializedRead && !plan.PreparedRead)
                {
                    code.Append("public static ").Append(fullType)
                        .Append(" CanonicalReadMaterialized<TLayoutReader, TPayloadReader>(TLayoutReader layoutReader, TPayloadReader payloadReader) ")
                        .Append("where TLayoutReader : class, ").Append(Runtime).Append("ICanonicalValueReader ")
                        .Append("where TPayloadReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                    foreach (Member member in common)
                        code.Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name)).Append(" = ")
                            .Append(Read(member.Type, true).Replace("(reader)", "(layoutReader)").Replace("reader.", "layoutReader.")).AppendLine(";");
                    code.AppendLine("switch (layoutReader.ReadInt32()) {");
                    foreach (Member member in plan.Cases)
                    {
                        code.Append("case (int)").Append(plan.KindType).Append('.').Append(Escape(member.Symbol.Name))
                            .Append(": return new ").Append(fullType).Append("(default(").Append(Runtime)
                            .Append("CanonicalConstruction), ").Append(plan.KindType).Append('.').Append(Escape(member.Symbol.Name));
                        foreach (Member value in plan.Members)
                            code.Append(", ").Append(value == member ? Read(member.Type, true).Replace("(reader)", "(payloadReader)")
                                .Replace("reader.", "payloadReader.") : value.IsCase ? "default" : Parameter(value.Symbol.Name));
                        code.AppendLine(");");
                    }
                    code.AppendLine("default: throw new global::System.IO.InvalidDataException(\"Canonical union case is invalid.\"); } }");
                }
            }
            code.Append("public static void CanonicalSkip<TReader>(TReader reader) where TReader : class, ")
                .Append(Runtime).AppendLine("ICanonicalValueReader {");
            foreach (Member member in common)
                Skip(code, member.Type);
            if (union)
                code.AppendLine("CanonicalSkipPayload(reader);");
            code.AppendLine("}");
            if (union)
            {
                code.Append("public static void CanonicalSkipPayload<TReader>(TReader reader) where TReader : class, ")
                    .Append(Runtime).AppendLine("ICanonicalValueReader {");
                code.AppendLine("CanonicalSkipCase(reader, reader.ReadInt32()); }");
                code.Append("public static void CanonicalSkipCase<TReader>(TReader reader, int kind)")
                    .Append(" where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                Switch(code, plan, "kind", member => Skip(code, member.Type), numeric: true);
                code.AppendLine("}");
            }
            code.Append("public static int CanonicalMeasure(in ").Append(fullType).AppendLine(" value) {");
            code.Append("int length = ").Append(union ? "4" : "0").AppendLine(";");
            foreach (Member member in common)
                MeasureMember(code, member.Type, "value." + Escape(member.Symbol.Name));
            if (union)
                Switch(code, plan, "value.Kind", member => MeasureMember(code, member.Type, "value." + Escape(member.Symbol.Name)));
            code.AppendLine("return length; }");
            foreach (string operation in new[] { "Retain", "Release" })
            {
                code.Append("public static void Canonical").Append(operation).Append("(in ").Append(fullType).AppendLine(" value) {");
                foreach (Member member in common)
                    Hold(code, operation, member.Type, "value." + Escape(member.Symbol.Name), plan);
                if (union && plan.Cases.Any(m => HasResources(plan, m.Type)))
                    Switch(code, plan, "value.Kind", member => Hold(code, operation, member.Type, "value." + Escape(member.Symbol.Name), plan), false);
                code.AppendLine("}");
                if (plan.PreparedStorage != null)
                {
                    code.Append("public static void Canonical").Append(operation).Append("StorageItems(")
                        .Append(TypeName(plan.PreparedStorage)).AppendLine(" storage) {");
                    foreach (Member member in plan.Members.Where(m => BorrowedStorage(m.Type) is INamedTypeSymbol))
                        code.Append("storage.").Append(Escape(member.Symbol.Name)).Append('.')
                            .Append(operation).AppendLine("Items();");
                    foreach (Member member in plan.Members.Where(m => PreparedStorage(m.Type) != null))
                        code.Append(TypeName(member.Type)).Append(".Canonical").Append(operation)
                            .AppendLine("StorageItems(storage);");
                    code.AppendLine("}");
                }
            }
            code.Append("public static void CanonicalVisitTexts<TVisitor>(TVisitor visitor, in ").Append(fullType)
                .Append(" value) where TVisitor : class, ").Append(Runtime).AppendLine("ICanonicalTextVisitor {");
            foreach (Member member in common)
                VisitTexts(code, member.Type, "value." + Escape(member.Symbol.Name), plan);
            if (union && plan.Cases.Any(m => HasTexts(plan, m.Type)))
                Switch(code, plan, "value.Kind", member => VisitTexts(code, member.Type, "value." + Escape(member.Symbol.Name), plan), false);
            code.AppendLine("}");
            code.AppendLine("} }");
            return code.ToString();
        }

        static bool HasPayload(Plan plan, ITypeSymbol type) => SequenceElement(type) is ITypeSymbol element
            ? HasPayload(plan, element) : plan.PayloadTypes.Contains(Identity(type));

        static void EmitParts(StringBuilder code, Plan plan)
        {
            string type = TypeName(plan.Type);
            foreach (bool payload in new[] { false, true })
            {
                code.Append("public static void CanonicalWrite").Append(payload ? "Payload" : "Layout")
                    .Append("<TWriter>(TWriter writer, in ").Append(type)
                    .Append(" value) where TWriter : class, ").Append(Runtime).AppendLine("ICanonicalValueWriter {");
                foreach (Member member in plan.Members)
                {
                    string value = "value." + Escape(member.Symbol.Name);
                    if (member.Payload)
                    {
                        if (payload)
                            Write(code, member.Type, value);
                    }
                    else if (HasPayload(plan, member.Type))
                        WritePart(code, plan, member.Type, value, payload);
                    else if (!payload)
                        Write(code, member.Type, value);
                }
                code.AppendLine("}");
            }
            code.Append("private ").Append(Escape(plan.Type.Name)).Append('(').Append(Runtime)
                .Append("CanonicalConstruction construction, ").Append(Runtime).Append("ICanonicalValueReader layoutReader, ")
                .Append(Runtime).AppendLine("ICanonicalValueReader payloadReader) {");
            foreach (Member member in plan.Members)
            {
                string read = member.Payload ? Read(member.Type).Replace("(reader)", "(payloadReader)")
                    .Replace("reader.", "payloadReader.") : ReadPart(plan, member.Type, "layoutReader", "payloadReader");
                code.Append("this.").Append(Escape(member.Symbol.Name)).Append(" = ").Append(read).AppendLine(";");
            }
            foreach (IMethodSymbol validation in plan.Validations)
                code.Append(Escape(validation.Name)).AppendLine("();");
            code.AppendLine("}");
            code.Append("public static ").Append(type)
                .Append(" CanonicalReadMaterialized<TLayoutReader, TPayloadReader>(TLayoutReader layoutReader, TPayloadReader payloadReader) ")
                .Append("where TLayoutReader : class, ").Append(Runtime).Append("ICanonicalValueReader ")
                .Append("where TPayloadReader : class, ").Append(Runtime).Append("ICanonicalValueReader => new ")
                .Append(type).Append("(default(").Append(Runtime).AppendLine("CanonicalConstruction), layoutReader, payloadReader);");
            foreach (ITypeSymbol sequence in InitializationSequences(plan.Members.Where(m => !m.Payload).Select(m => m.Type)))
            {
                if (!HasPayload(plan, sequence))
                    continue;
                ITypeSymbol element = SequenceElement(sequence);
                code.Append("private static ").Append(TypeName(sequence)).Append(' ').Append(SequenceReader(sequence))
                    .Append("Parts<TLayoutReader, TPayloadReader>(TLayoutReader layoutReader, TPayloadReader payloadReader) ")
                    .Append("where TLayoutReader : class, ").Append(Runtime).Append("ICanonicalValueReader ")
                    .Append("where TPayloadReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                code.AppendLine("int count = layoutReader.ReadInt32();");
                code.AppendLine("if (payloadReader.ReadInt32() != count) throw new global::System.IO.InvalidDataException(\"Canonical collection parts have different lengths.\");");
                code.Append("if (count < 0 || count > ((long)layoutReader.Remaining + payloadReader.Remaining) / ").Append(Minimum(element))
                    .AppendLine(") throw new global::System.IO.InvalidDataException(\"Canonical collection length is invalid.\");");
                code.Append("var values = new ").Append(TypeName(element)).AppendLine("[count];");
                code.Append("for (int i = 0; i < count; i++) values[i] = ")
                    .Append(ReadPart(plan, element, "layoutReader", "payloadReader")).AppendLine(";");
                code.AppendLine(sequence is IArrayTypeSymbol ? "return values; }" : IsReadOnlyMemory(sequence) ?
                    "return new " + TypeName(sequence) + "(values); }" : "return global::System.Array.AsReadOnly(values); }");
            }
        }

        static string ReadPart(Plan plan, ITypeSymbol type, string layout, string payload)
        {
            if (!HasPayload(plan, type))
                return Read(type).Replace("(reader)", "(" + layout + ")").Replace("reader.", layout + ".");
            if (SequenceElement(type) != null)
                return SequenceReader(type) + "Parts(" + layout + ", " + payload + ")";
            return TypeName(type) + ".CanonicalReadMaterialized(" + layout + ", " + payload + ")";
        }

        static void WritePart(StringBuilder code, Plan plan, ITypeSymbol type, string value, bool payload, int depth = 0)
        {
            ITypeSymbol element = SequenceElement(type);
            if (element != null)
            {
                code.Append("writer.WriteInt32(").Append(SequenceCount(type, value)).AppendLine(");");
                string index = "__canonicalPartIndex" + depth;
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(SequenceCount(type, value)).Append("; ").Append(index).AppendLine("++) {");
                WritePart(code, plan, element, SequenceValue(type, value, index), payload, depth + 1);
                code.AppendLine("}");
            }
            else
                code.Append(TypeName(type)).Append(payload ? ".CanonicalWritePayload(writer, " : ".CanonicalWriteLayout(writer, ")
                    .Append(value).AppendLine(");");
        }

        static void Switch(StringBuilder code, Plan plan, string expression, Action<Member> emit, bool reject = true, bool numeric = false)
        {
            code.Append("switch (").Append(expression).AppendLine(") {");
            foreach (Member member in plan.Cases)
            {
                code.Append(numeric ? "case (int)" : "case ").Append(plan.KindType).Append('.')
                    .Append(Escape(member.Symbol.Name)).AppendLine(":");
                emit(member);
                code.AppendLine("break;");
            }
            if (reject)
                code.AppendLine("default: throw new global::System.IO.InvalidDataException(\"Canonical union case is invalid.\");");
            code.AppendLine("}");
        }

        static void Write(StringBuilder code, ITypeSymbol type, string value, int depth = 0)
        {
            ITypeSymbol element = SequenceElement(type);
            if (element != null)
            {
                string index = "__canonicalIndex" + depth;
                string count = SequenceCount(type, value);
                code.Append("writer.WriteInt32(").Append(count).AppendLine(");");
                if (IsByteSequence(type))
                {
                    code.Append("writer.WriteRawBytes(").Append(value).Append(IsReadOnlyMemory(type) ? ".Span" : "").AppendLine(");");
                    return;
                }
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(count).Append("; ").Append(index).AppendLine("++) {");
                Write(code, element, SequenceValue(type, value, index), depth + 1);
                code.AppendLine("}");
                return;
            }
            string primitive = Primitive(type);
            if (primitive != null)
                code.Append("writer.Write").Append(primitive).Append('(').Append(CastEnum(type, value)).AppendLine(");");
            else if (Identity(type) == TextType || type.SpecialType == SpecialType.System_String)
                code.Append("writer.WriteText(").Append(value).AppendLine(");");
            else
                code.Append(TypeName(type)).Append(".CanonicalWrite(writer, ").Append(value).AppendLine(");");
        }

        static string ReadMember(Member member, Plan plan)
        {
            if (CaseSemantic(member, "NormalizeZero") || CaseSemantic(member, "NormalizeNull"))
                return "__CanonicalRead_" + member.Symbol.Name + "(reader)";
            if (BorrowedStorage(member.Type) != null)
                return SequenceReader(member.Type) + "(reader, storage." + Escape(member.Symbol.Name) + ")";
            if (member.Type.SpecialType == SpecialType.System_String && HasAttribute(member.Symbol, DeclaredTextAttribute) && plan.PreparedRead)
                return "reader.ReadDeclaredString()";
            if (plan.PreparedRead && PreparedStorage(member.Type) != null)
                return TypeName(member.Type) + ".CanonicalRead(reader, storage)";
            return Read(member.Type, !plan.PreparedRead);
        }

        static string Normalize(Member member, string value)
        {
            if (CaseSemantic(member, "NormalizeZero"))
                return value + " == 0 ? 0 : " + value;
            if (CaseSemantic(member, "NormalizeNull"))
                return value + ".IsNull ? " + Runtime + "CanonicalTextValue.Empty : " + value;
            return value;
        }

        static bool CaseSemantic(Member member, string name) => member.Symbol.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == UnionCaseAttribute).SelectMany(a => a.NamedArguments)
            .Any(a => a.Key == name && a.Value.Value is true);

        static string Read(ITypeSymbol type, bool materialized = false)
        {
            if (SequenceElement(type) != null && BufferElement(type) == null)
                return SequenceReader(type) + "(reader)";
            string primitive = Primitive(type);
            if (type is INamedTypeSymbol enumeration && type.TypeKind == TypeKind.Enum)
            {
                var constants = new HashSet<string>(StringComparer.Ordinal);
                IFieldSymbol[] values = enumeration.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
                    .OrderBy(f => f.Name, StringComparer.Ordinal)
                    .Where(f => constants.Add(Convert.ToString(f.ConstantValue, CultureInfo.InvariantCulture))).ToArray();
                string reject = "throw new global::System.IO.InvalidDataException(" +
                    SymbolDisplay.FormatLiteral("Canonical enum " + Identity(type) + " is invalid.", true) + ")";
                string arms;
                if (HasAttribute(enumeration, "System.FlagsAttribute"))
                {
                    string mask = values.Length == 0 ? "(" + TypeName(type) + ")0" :
                        string.Join(" | ", values.Select(f => TypeName(type) + "." + Escape(f.Name)));
                    arms = "var __canonicalEnumBits when (((" + TypeName(type) + ")__canonicalEnumBits & ~(" +
                        mask + ")) == 0) => (" + TypeName(type) + ")__canonicalEnumBits, ";
                }
                else
                    arms = string.Concat(values.Select(f => "(" + TypeName(enumeration.EnumUnderlyingType) + ")" +
                        TypeName(type) + "." + Escape(f.Name) + " => " + TypeName(type) + "." + Escape(f.Name) + ", "));
                return "reader.Read" + primitive + "() switch { " + arms + "_ => " + reject + " }";
            }
            if (type.SpecialType == SpecialType.System_String)
                return "reader.ReadString()";
            string value = primitive != null ? "reader.Read" + primitive + "()" :
                Identity(type) == TextType ? "reader.ReadText()" : TypeName(type) +
                (type.TypeKind == TypeKind.Class || !UsesPreparedRead(type) || materialized && HasMaterializedRead(type)
                    ? ".CanonicalReadMaterialized(reader)" : ".CanonicalRead(reader)");
            return value;
        }

        static void Skip(StringBuilder code, ITypeSymbol type, int depth = 0)
        {
            if (IsByteSequence(type))
            {
                code.AppendLine("reader.SkipBytes();");
                return;
            }
            ITypeSymbol element = SequenceElement(type);
            if (element != null)
            {
                string count = "__canonicalCount" + depth;
                string index = "__canonicalIndex" + depth;
                code.Append("{ int ").Append(count).AppendLine(" = reader.ReadInt32();");
                code.Append("if (").Append(count).AppendLine(" < 0) throw new global::System.IO.InvalidDataException(\"Canonical collection count is invalid.\");");
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(count).Append("; ").Append(index).AppendLine("++) {");
                Skip(code, element, depth + 1);
                code.AppendLine("} }");
                return;
            }
            if (type.TypeKind == TypeKind.Enum)
                code.Append("_ = ").Append(Read(type)).AppendLine(";");
            else if (Primitive(type) != null)
                code.Append("reader.Read").Append(Primitive(type)).AppendLine("();");
            else if (Identity(type) == TextType || type.SpecialType == SpecialType.System_String)
                code.AppendLine("reader.SkipText();");
            else
                code.Append(TypeName(type)).AppendLine(".CanonicalSkip(reader);");
        }

        static string Measure(ITypeSymbol type, string value)
        {
            string primitive = Primitive(type);
            if (primitive != null)
            {
                switch (primitive)
                {
                    case "Byte": case "SByte": case "Boolean": return "1";
                    case "Int16": case "UInt16": case "Char": return "2";
                    case "Int32": case "UInt32": case "SingleBits": return "4";
                    default: return "8";
                }
            }
            return type.SpecialType == SpecialType.System_String ? "(4 + (" + value + " == null ? 0 : checked(" + value + ".Length * 2)))" :
                Identity(type) == TextType ? "(4 + (" + value + ".IsNull ? 0 : checked(" + value + ".Length * 2)))" :
                TypeName(type) + ".CanonicalMeasure(" + value + ")";
        }

        static bool HasResources(Plan plan, ITypeSymbol type) => Identity(type) == TextType || BorrowedStorage(type) != null ||
            (SequenceElement(type) is ITypeSymbol element ? HasResources(plan, element) : plan.ResourceTypes.Contains(Identity(type)));

        static bool HasTexts(Plan plan, ITypeSymbol type) => Identity(type) == TextType || type.SpecialType == SpecialType.System_String ||
            (SequenceElement(type) is ITypeSymbol element ? HasTexts(plan, element) : plan.TextTypes.Contains(Identity(type)));

        static void VisitTexts(StringBuilder code, ITypeSymbol type, string value, Plan plan, int depth = 0)
        {
            if (!HasTexts(plan, type))
                return;
            ITypeSymbol element = SequenceElement(type);
            if (element != null)
            {
                string index = "__canonicalIndex" + depth;
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(SequenceCount(type, value)).Append("; ").Append(index).AppendLine("++) {");
                VisitTexts(code, element, SequenceValue(type, value, index), plan, depth + 1);
                code.AppendLine("}");
            }
            else if (Identity(type) == TextType || type.SpecialType == SpecialType.System_String)
                code.Append("visitor.Visit(").Append(value).AppendLine(");");
            else
                code.Append(TypeName(type)).Append(".CanonicalVisitTexts(visitor, ").Append(value).AppendLine(");");
        }

        static void Hold(StringBuilder code, string operation, ITypeSymbol type, string value, Plan plan, int depth = 0)
        {
            if (!HasResources(plan, type))
                return;
            if (BorrowedStorage(type) != null)
            {
                code.Append(value).Append('.').Append(operation).AppendLine("StateStorage();");
                return;
            }
            ITypeSymbol element = SequenceElement(type);
            if (element != null)
            {
                string index = "__canonicalIndex" + depth;
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(SequenceCount(type, value)).Append("; ").Append(index).AppendLine("++) {");
                Hold(code, operation, element, SequenceValue(type, value, index), plan, depth + 1);
                code.AppendLine("}");
                return;
            }
            if (Identity(type) == TextType)
                code.Append(value).Append('.').Append(operation).AppendLine("();");
            else if (Primitive(type) == null)
                code.Append(TypeName(type)).Append(".Canonical").Append(operation).Append('(').Append(value).AppendLine(");");
        }

        static string Primitive(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && type.TypeKind == TypeKind.Enum)
                return Primitive(named.EnumUnderlyingType);
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte: return "Byte";
                case SpecialType.System_SByte: return "SByte";
                case SpecialType.System_Boolean: return "Boolean";
                case SpecialType.System_Int16: return "Int16";
                case SpecialType.System_UInt16: return "UInt16";
                case SpecialType.System_Char: return "Char";
                case SpecialType.System_Int32: return "Int32";
                case SpecialType.System_UInt32: return "UInt32";
                case SpecialType.System_Int64: return "Int64";
                case SpecialType.System_UInt64: return "UInt64";
                case SpecialType.System_Single: return "SingleBits";
                case SpecialType.System_Double: return "DoubleBits";
                default: return null;
            }
        }

        static ITypeSymbol BufferElement(ITypeSymbol type) => type is INamedTypeSymbol named &&
            Identity(named.OriginalDefinition) == BufferType ? named.TypeArguments[0] : null;

        static ITypeSymbol SequenceElement(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array && array.Rank == 1)
                return array.ElementType;
            if (type is INamedTypeSymbol named)
            {
                if (HasAttribute(named.OriginalDefinition, BorrowedSequenceAttribute))
                    return named.TypeArguments[0];
                switch (Identity(named.OriginalDefinition))
                {
                    case BufferType:
                    case "System.ReadOnlyMemory<T>":
                    case "System.Collections.Generic.IReadOnlyList<T>":
                    case "System.Collections.ObjectModel.ReadOnlyCollection<T>":
                        return named.TypeArguments[0];
                }
            }
            return null;
        }

        static INamedTypeSymbol BorrowedStorage(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named))
                return null;
            AttributeData shape = named.OriginalDefinition.GetAttributes()
                .SingleOrDefault(a => Identity(a.AttributeClass) == BorrowedSequenceAttribute);
            return shape == null ? null : ((INamedTypeSymbol)shape.ConstructorArguments[0].Value)
                .OriginalDefinition.Construct(named.TypeArguments[0]);
        }

        static string SequenceCount(ITypeSymbol type, string value) =>
            value + (type is IArrayTypeSymbol || IsReadOnlyMemory(type) ? ".Length" : ".Count");

        static string SequenceValue(ITypeSymbol type, string value, string index) =>
            IsReadOnlyMemory(type) || BorrowedStorage(type) != null ? value + ".Span[" + index + "]" :
            BufferElement(type) == null ? value + "[" + index + "]" : value + ".Element(" + index + ")";

        static bool IsReadOnlyMemory(ITypeSymbol type) => type is INamedTypeSymbol named &&
            Identity(named.OriginalDefinition) == "System.ReadOnlyMemory<T>";

        static bool IsByteSequence(ITypeSymbol type) => (type is IArrayTypeSymbol || IsReadOnlyMemory(type)) &&
            SequenceElement(type)?.SpecialType == SpecialType.System_Byte;

        static string FreezeSequence(ITypeSymbol type, string value) => type is IArrayTypeSymbol ?
            "(" + TypeName(type) + ")" + value + ".Clone()" : IsReadOnlyMemory(type) ?
            "new " + TypeName(type) + "(" + value + ".ToArray())" :
            "global::System.Array.AsReadOnly(global::System.Linq.Enumerable.ToArray(" + value + "))";

        static string SequenceReader(ITypeSymbol type)
        {
            using (var sha = SHA256.Create())
                return "__CanonicalReadSequence_" + string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(Identity(type)))
                    .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        static IEnumerable<ITypeSymbol> InitializationSequences(IEnumerable<ITypeSymbol> types)
        {
            var sequences = new SortedDictionary<string, ITypeSymbol>(StringComparer.Ordinal);
            foreach (ITypeSymbol type in types)
                Add(type);
            return sequences.Values;

            void Add(ITypeSymbol type)
            {
                ITypeSymbol element = SequenceElement(type);
                if (element == null)
                    return;
                if (BufferElement(type) == null && BorrowedStorage(type) == null)
                    sequences[Identity(type)] = type;
                Add(element);
            }
        }

        static IEnumerable<ITypeSymbol> BorrowedSequences(IEnumerable<ITypeSymbol> types)
        {
            var sequences = new SortedDictionary<string, ITypeSymbol>(StringComparer.Ordinal);
            foreach (ITypeSymbol type in types)
                if (BorrowedStorage(type) is INamedTypeSymbol)
                    sequences[Identity(type)] = type;
            return sequences.Values;
        }

        static string Minimum(ITypeSymbol type) => SequenceElement(type) != null || Identity(type) == TextType || type.SpecialType == SpecialType.System_String ? "4" :
            Primitive(type) != null ? Measure(type, "") : TypeName(type) + ".CanonicalMinimumSize";

        static void MeasureMember(StringBuilder code, ITypeSymbol type, string value, int depth = 0)
        {
            if (IsByteSequence(type))
            {
                code.Append("length = checked(length + 4 + ").Append(SequenceCount(type, value)).AppendLine(");");
                return;
            }
            ITypeSymbol element = SequenceElement(type);
            if (element == null)
                code.Append("length = checked(length + ").Append(Measure(type, value)).AppendLine(");");
            else
            {
                code.AppendLine("length = checked(length + 4);");
                string index = "__canonicalIndex" + depth;
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(SequenceCount(type, value)).Append("; ").Append(index).AppendLine("++) {");
                MeasureMember(code, element, SequenceValue(type, value, index), depth + 1);
                code.AppendLine("}");
            }
        }

        static string CastEnum(ITypeSymbol type, string value) => type is INamedTypeSymbol named && type.TypeKind == TypeKind.Enum
            ? "(" + TypeName(named.EnumUnderlyingType) + ")" + value : value;
        static string Identity(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        static string Escape(string name) => "@" + name;
        static string Parameter(string name) => Escape(char.ToLowerInvariant(name[0]) + name.Substring(1));
        static bool HasAttribute(ISymbol symbol, string attribute) => symbol.GetAttributes().Any(a => Identity(a.AttributeClass) == attribute);
        static bool UsesPreparedRead(ITypeSymbol type) => !type.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == ContractAttribute)
            .SelectMany(a => a.NamedArguments).Any(a => a.Key == "PreparedRead" && a.Value.Value is false);
        static bool HasMaterializedRead(ITypeSymbol type) => type.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == ContractAttribute)
            .SelectMany(a => a.NamedArguments).Any(a => a.Key == "MaterializedRead" && a.Value.Value is true);
        static INamedTypeSymbol PreparedStorage(ITypeSymbol type) => (INamedTypeSymbol)type.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == ContractAttribute)
            .SelectMany(a => a.NamedArguments).Where(a => a.Key == "PreparedStorage")
            .Select(a => a.Value.Value).SingleOrDefault();

        sealed class Receiver : ISyntaxReceiver
        {
            public readonly List<TypeDeclarationSyntax> Types = new List<TypeDeclarationSyntax>();
            public void OnVisitSyntaxNode(SyntaxNode node)
            {
                if (node is TypeDeclarationSyntax type && type.AttributeLists.Count > 0)
                    Types.Add(type);
            }
        }

        sealed class Member
        {
            public ISymbol Symbol;
            public ITypeSymbol Type;
            public bool IsCase;
            public bool Payload;
            public bool Generated;
        }

        sealed class Plan
        {
            public INamedTypeSymbol Type;
            public Member[] Members;
            public Member[] Cases;
            public INamedTypeSymbol UnionDefinition;
            public string KindType;
            public IMethodSymbol[] Validations;
            public string Descriptor;
            public string Schema;
            public bool Valid = true;
            public bool Complete;
            public int MinimumSize;
            public bool MaterializedRead;
            public bool PreparedRead;
            public INamedTypeSymbol PreparedStorage;
            public bool HashValue;
            public bool HasResources;
            public HashSet<string> ResourceTypes;
            public bool HasTexts;
            public HashSet<string> TextTypes;
            public bool HasPayload;
            public HashSet<string> PayloadTypes;
        }

        sealed class Planner
        {
            readonly GeneratorExecutionContext m_Context;
            readonly Dictionary<string, Plan> m_Plans = new Dictionary<string, Plan>(StringComparer.Ordinal);
            readonly HashSet<string> m_ResourceTypes = new HashSet<string>(StringComparer.Ordinal);
            readonly HashSet<string> m_TextTypes = new HashSet<string>(StringComparer.Ordinal);
            readonly HashSet<string> m_PayloadTypes = new HashSet<string>(StringComparer.Ordinal);
            public IEnumerable<Plan> Plans => m_Plans.Values;
            readonly Compilation m_Compilation;
            public Planner(GeneratorExecutionContext context, Compilation compilation)
            {
                m_Context = context;
                m_Compilation = compilation;
            }

            public Plan Plan(INamedTypeSymbol type)
            {
                if (m_Plans.TryGetValue(Identity(type), out Plan existing))
                {
                    if (!existing.Complete)
                        Error(existing, type, "正式记录不能递归引用自身；页引用必须由所属存储显式承载");
                    return existing;
                }
                var plan = new Plan { Type = type, ResourceTypes = m_ResourceTypes, TextTypes = m_TextTypes, PayloadTypes = m_PayloadTypes, PreparedRead = UsesPreparedRead(type),
                    HashValue = type.AllInterfaces.Any(i => Identity(i) == HashValueType),
                    MaterializedRead = type.GetAttributes().Single(a => Identity(a.AttributeClass) == ContractAttribute)
                        .NamedArguments.Any(a => a.Key == "MaterializedRead" && a.Value.Value is true) };
                plan.PreparedStorage = (INamedTypeSymbol)type.GetAttributes()
                    .Single(a => Identity(a.AttributeClass) == ContractAttribute).NamedArguments
                    .Where(a => a.Key == "PreparedStorage").Select(a => a.Value.Value).SingleOrDefault();
                m_Plans.Add(Identity(type), plan);
                if (!plan.PreparedRead && !plan.MaterializedRead)
                    Error(plan, type, "初始化合同必须显式声明物化读取，不能缺少正式读取入口");
                if (type.ContainingType != null || type.Arity != 0 || type.IsAbstract ||
                    type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object && type.TypeKind == TypeKind.Class)
                    Error(plan, type, "正式记录必须是独立的非泛型类型，运行设施和继承服务需留在原 owner");
                foreach (SyntaxReference syntax in type.DeclaringSyntaxReferences)
                    if (syntax.GetSyntax() is TypeDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                        Error(plan, type, "类型必须声明为 partial，生成访问进入原正式类型");
                plan.Members = StorageMembers(type);
                AttributeData union = type.GetAttributes().SingleOrDefault(a => Identity(a.AttributeClass) == UnionAttribute);
                if (union != null)
                {
                    plan.UnionDefinition = (INamedTypeSymbol)union.ConstructorArguments[0].Value;
                    if (!HasAttribute(plan.UnionDefinition, UnionTemplateAttribute) || plan.UnionDefinition.IsUnboundGenericType)
                        Error(plan, type, "联合必须引用已声明模板的闭合正式 C# 类型");
                    Member[] generated = StorageMembers(plan.UnionDefinition);
                    foreach (Member member in generated)
                        member.Generated = true;
                    foreach (Member member in plan.Members)
                        if (generated.Any(m => m.Symbol.Name == member.Symbol.Name) || member.IsCase)
                            Error(plan, member.Symbol, "联合字段只在正式模板声明，不重复声明分支或成员");
                    plan.Members = plan.Members.Concat(generated).OrderBy(m => m.Symbol.Name, StringComparer.Ordinal).ToArray();
                }
                plan.Cases = Cases(plan.Members);
                if (plan.HashValue && (type.TypeKind != TypeKind.Struct || !type.IsReadOnly ||
                    plan.Cases.Length != 0 || plan.Members.Length != 1 || Identity(plan.Members[0].Type) != HashType))
                    Error(plan, type, "正式 Hash 包装必须由 readonly struct 的唯一 StableHash 存储成员持有");
                INamedTypeSymbol kindOwner = plan.UnionDefinition ?? type;
                plan.KindType = "global::" + kindOwner.ContainingNamespace.ToDisplayString() + "." + Escape(kindOwner.Name) + "Kind";
                plan.Validations = type.GetMembers().OfType<IMethodSymbol>()
                    .Where(m => HasAttribute(m, ValidationAttribute)).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
                foreach (IMethodSymbol validation in plan.Validations)
                    if (validation.IsStatic || !validation.ReturnsVoid || validation.Parameters.Length != 0)
                        Error(plan, validation, "正式数据规则必须由不带参数的实例 void 方法提供");
                if (plan.Cases.Length > 0 && type.TypeKind == TypeKind.Class && plan.PreparedRead)
                    Error(plan, type, "类联合只允许初始化物化读取；实时联合必须使用值或原预分配页");
                if (plan.Members.Any(m => m.Payload) && (plan.PreparedRead || plan.Cases.Length > 0))
                    Error(plan, type, "参数成员分块只用于初始化组合记录，联合激活载荷由正式分支声明确定");
                var descriptor = new StringBuilder("canonical-csharp/1|le|utf16-le/2|").Append(Identity(type));
                if (plan.UnionDefinition != null)
                    descriptor.Append("|union:").Append(Identity(plan.UnionDefinition));
                for (int i = 0; i < plan.Cases.Length; i++)
                    descriptor.Append("|case:").Append(i + 1).Append('=').Append(MemberIdentity(plan, plan.Cases[i]));
                foreach (IFieldSymbol constant in type.GetMembers().OfType<IFieldSymbol>()
                    .Where(f => HasAttribute(f, SchemaConstantAttribute)).OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    if (!constant.HasConstantValue)
                        Error(plan, constant, "结构语义必须由正式 const 声明提供");
                    descriptor.Append("|constant:").Append(Identity(constant)).Append(':').Append(Identity(constant.Type))
                        .Append('=').Append(constant.ConstantValue is string text ? SymbolDisplay.FormatLiteral(text, true) :
                            Convert.ToString(constant.ConstantValue, CultureInfo.InvariantCulture));
                }
                foreach (Member member in plan.Members)
                {
                    descriptor.Append('|').Append(member.IsCase ? "case:" : member.Payload ? "payload:" : "member:").Append(MemberIdentity(plan, member))
                        .Append(':').Append(Shape(plan, member.Type, member.Symbol));
                    if (CaseSemantic(member, "NormalizeZero"))
                    {
                        if (member.Type.SpecialType != SpecialType.System_Single && member.Type.SpecialType != SpecialType.System_Double)
                            Error(plan, member.Symbol, "零值规范只适用于正式浮点分支");
                        descriptor.Append(":zero-normalized");
                    }
                    if (CaseSemantic(member, "NormalizeNull"))
                    {
                        if (Identity(member.Type) != TextType)
                            Error(plan, member.Symbol, "空文本规范只适用于正式文本分支");
                        descriptor.Append(":null-normalized");
                    }
                    if (plan.PreparedRead && PreparedStorage(member.Type) is INamedTypeSymbol nestedStorage &&
                        !SymbolEqualityComparer.Default.Equals(plan.PreparedStorage, nestedStorage))
                        Error(plan, member.Symbol, "嵌套借用记录必须消费原同一 PreparedStorage");
                    if (BorrowedStorage(member.Type) is INamedTypeSymbol storageType && plan.PreparedRead)
                    {
                        ISymbol storageMember = plan.PreparedStorage?.GetMembers(member.Symbol.Name).SingleOrDefault();
                        ITypeSymbol actualStorage = storageMember is IPropertySymbol storageProperty ? storageProperty.Type :
                            storageMember is IFieldSymbol storageField ? storageField.Type : null;
                        if (!SymbolEqualityComparer.Default.Equals(actualStorage, storageType))
                            Error(plan, member.Symbol, "借用集合必须由正式 PreparedStorage 的同名原存储成员提供");
                    }
                    if (plan.PreparedRead && type.TypeKind == TypeKind.Class && BufferElement(member.Type) == null &&
                        member.Type.TypeKind != TypeKind.Class && (member.Symbol is IFieldSymbol field && field.IsReadOnly ||
                        member.Symbol is IPropertySymbol property && property.SetMethod == null))
                        Error(plan, member.Symbol, "类记录必须能直接写入原候选存储，不通过构造副本恢复");
                    if (plan.PreparedRead && type.TypeKind == TypeKind.Struct && !type.IsReadOnly && plan.Cases.Length == 0 &&
                        (member.Symbol is IFieldSymbol storedField && storedField.IsReadOnly ||
                         member.Symbol is IPropertySymbol storedProperty && storedProperty.SetMethod == null))
                        Error(plan, member.Symbol, "可变状态必须逐字段写入原存储，正式成员需要可写访问");
                    if (plan.PreparedRead && !type.IsReadOnly && plan.Cases.Length == 0 && member.Symbol is IPropertySymbol &&
                        member.Type is INamedTypeSymbol storedRecord && storedRecord.TypeKind == TypeKind.Struct &&
                        !storedRecord.IsReadOnly && Primitive(storedRecord) == null)
                        Error(plan, member.Symbol, "嵌套可变状态必须由真实字段提供 ref 访问，不能通过属性复制整份状态");
                }
                plan.HasResources = plan.Members.Any(m => HasResources(plan, m.Type));
                if (plan.HasResources)
                    m_ResourceTypes.Add(Identity(type));
                plan.HasTexts = plan.Members.Any(m => HasTexts(plan, m.Type));
                if (plan.HasTexts)
                    m_TextTypes.Add(Identity(type));
                plan.HasPayload = plan.Cases.Length > 0 || plan.Members.Any(m => m.Payload || HasPayload(plan, m.Type));
                if (plan.HasPayload)
                    m_PayloadTypes.Add(Identity(type));
                plan.Descriptor = descriptor.ToString();
                plan.MinimumSize = plan.Members.Where(m => !m.IsCase).Sum(m => MinimumSize(m.Type));
                if (plan.Cases.Length > 0)
                    plan.MinimumSize += 4 + plan.Cases.Min(m => MinimumSize(m.Type));
                using (var sha = SHA256.Create())
                    plan.Schema = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(plan.Descriptor))
                        .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
                plan.Complete = true;
                return plan;
            }

            int MinimumSize(ITypeSymbol type)
            {
                if (SequenceElement(type) != null || Identity(type) == TextType || type.SpecialType == SpecialType.System_String)
                    return 4;
                if (Primitive(type) != null)
                    return int.Parse(Measure(type, ""), CultureInfo.InvariantCulture);
                if (type is INamedTypeSymbol record)
                {
                    if (m_Plans.TryGetValue(Identity(record), out Plan plan))
                        return plan.MinimumSize;
                    var field = record.GetMembers("CanonicalMinimumSize").OfType<IFieldSymbol>().SingleOrDefault();
                    if (field != null && field.HasConstantValue)
                        return (int)field.ConstantValue;
                }
                return 0;
            }

            string Shape(Plan plan, ITypeSymbol type, ISymbol member)
            {
                if (type.SpecialType == SpecialType.System_String)
                {
                    if (plan.PreparedRead && !HasAttribute(member, DeclaredTextAttribute))
                        Error(plan, member, "程序字符串只允许初始化物化读取；实时文本必须由正式文本 owner 持有");
                    return Identity(type);
                }
                ITypeSymbol element = SequenceElement(type);
                if (element != null)
                {
                    bool prepared = BufferElement(type) != null;
                    bool borrowed = BorrowedStorage(type) != null;
                    if (prepared && (plan.Type.IsReadOnly || plan.Cases.Length != 0))
                        Error(plan, member, "预分配集合必须由原候选页持有，不能通过值构造复制");
                    if (!prepared && !borrowed && plan.PreparedRead)
                        Error(plan, member, "只读序列只允许初始化物化读取；实时集合必须由原预分配存储持有");
                    return (borrowed ? "borrowed-sequence<" : prepared ? "prepared-sequence<" : "initialization-sequence<")
                        + Shape(plan, element, member) + ">";
                }
                if (type.TypeKind == TypeKind.Enum)
                {
                    var named = (INamedTypeSymbol)type;
                    return Identity(type) + ":" + Identity(named.EnumUnderlyingType) + "{" + string.Join(",",
                        named.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
                            .OrderBy(f => f.Name, StringComparer.Ordinal)
                            .Select(f => f.Name + "=" + Convert.ToString(f.ConstantValue, CultureInfo.InvariantCulture))) + "}";
                }
                if (Primitive(type) != null || Identity(type) == TextType)
                    return Identity(type);
                if (type is INamedTypeSymbol record && HasAttribute(record, ContractAttribute))
                {
                    if (plan.PreparedRead && !UsesPreparedRead(type))
                        Error(plan, member, "实时合同不能调用初始化记录的物化读取，需在原 owner 分离状态和发布数据");
                    if (SymbolEqualityComparer.Default.Equals(record.ContainingAssembly, m_Compilation.Assembly))
                    {
                        Plan child = Plan(record);
                        if (!child.Valid)
                            plan.Valid = false;
                        return Identity(type) + "#" + child.Schema;
                    }
                    var schema = record.GetMembers("CanonicalSchema").OfType<IFieldSymbol>().SingleOrDefault();
                    var resources = record.GetMembers("CanonicalHasResources").OfType<IFieldSymbol>().SingleOrDefault();
                    var texts = record.GetMembers("CanonicalHasTexts").OfType<IFieldSymbol>().SingleOrDefault();
                    var payload = record.GetMembers("CanonicalHasPayload").OfType<IFieldSymbol>().SingleOrDefault();
                    if (schema != null && schema.HasConstantValue && resources != null && resources.HasConstantValue &&
                        texts != null && texts.HasConstantValue)
                    {
                        if ((bool)resources.ConstantValue)
                            m_ResourceTypes.Add(Identity(type));
                        if ((bool)texts.ConstantValue)
                            m_TextTypes.Add(Identity(type));
                        if (payload != null && payload.HasConstantValue && (bool)payload.ConstantValue)
                            m_PayloadTypes.Add(Identity(type));
                        return Identity(type) + "#" + schema.ConstantValue;
                    }
                }
                Error(plan, member, "成员类型 " + Identity(type) + " 尚未声明可直接重建的 canonical 合同或存储形状");
                return Identity(type);
            }

            void Error(Plan plan, ISymbol symbol, string reason)
            {
                plan.Valid = false;
                Location location = SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, m_Compilation.Assembly)
                    ? symbol.Locations.FirstOrDefault() : plan.Type.Locations.FirstOrDefault();
                m_Context.ReportDiagnostic(Diagnostic.Create(InvalidContract, location, Identity(symbol), reason));
            }

        }

        static Member[] StorageMembers(INamedTypeSymbol type) => type.GetMembers()
            .Where(s => !s.IsStatic && !s.IsImplicitlyDeclared)
            .Where(s => s is IFieldSymbol || s is IPropertySymbol p && IsAutoProperty(p))
            .OrderBy(Identity, StringComparer.Ordinal)
            .Select(s => new Member { Symbol = s, Type = s is IFieldSymbol f ? f.Type : ((IPropertySymbol)s).Type,
                IsCase = HasAttribute(s, UnionCaseAttribute), Payload = HasAttribute(s, PayloadAttribute) }).ToArray();

        static Member[] Cases(IEnumerable<Member> members) => members.Where(m => m.IsCase)
            .OrderBy(m => CaseOrder(m.Symbol.OriginalDefinition is IFieldSymbol field ? field.Type :
                ((IPropertySymbol)m.Symbol.OriginalDefinition).Type), StringComparer.Ordinal)
            .ThenBy(m => m.Symbol.Name, StringComparer.Ordinal).ToArray();

        static string CaseOrder(ITypeSymbol type) => Primitive(type) != null ? "0:" + Identity(type) :
            type is ITypeParameterSymbol parameter ? "1:" + parameter.Ordinal.ToString("D8", CultureInfo.InvariantCulture) :
            "2:" + Identity(type);

        static string MemberIdentity(Plan plan, Member member) => Identity(plan.Type) + "." + member.Symbol.Name;

        static void EmitKind(StringBuilder code, INamedTypeSymbol type, Member[] cases)
        {
            code.Append(type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal")
                .Append(" enum ").Append(Escape(type.Name)).Append("Kind : ")
                .AppendLine(cases.Length <= byte.MaxValue ? "byte {" : "int {");
            for (int i = 0; i < cases.Length; i++)
                code.Append(Escape(cases[i].Symbol.Name)).Append(" = ").Append(i + 1).AppendLine(",");
            code.AppendLine("}");
        }

        static bool IsAutoProperty(IPropertySymbol property) => property.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is PropertyDeclarationSyntax declaration && declaration.AccessorList != null &&
            declaration.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null));
    }
}
