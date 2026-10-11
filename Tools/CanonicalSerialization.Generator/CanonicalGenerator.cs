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
        const string UnionTemplateAttribute = "ThirdPersonSimulation.CanonicalUnionTemplateAttribute";
        const string UnionAttribute = "ThirdPersonSimulation.CanonicalUnionAttribute";
        const string ValidationAttribute = "ThirdPersonSimulation.CanonicalValidationAttribute";
        const string SchemaConstantAttribute = "ThirdPersonSimulation.CanonicalSchemaConstantAttribute";
        const string TextType = "ThirdPersonSimulation.CanonicalTextValue";
        const string BufferType = "ThirdPersonSimulation.CanonicalBuffer<T>";
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
            }
            var planner = new Planner(context);
            foreach (INamedTypeSymbol type in types.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
                planner.Plan(type);
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
            if (union && plan.UnionDefinition == null)
                EmitKind(code, type, plan.Cases);
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

            if (type.TypeKind == TypeKind.Struct && (type.IsReadOnly || union || !plan.PreparedRead))
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
                    code.Append("this.").Append(Escape(plan.Members[i].Symbol.Name)).Append(" = value").Append(i).AppendLine(";");
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
                code.Append("return new ").Append(fullType).Append("(default(").Append(Runtime)
                    .Append("CanonicalConstruction), ").Append(plan.KindType).Append('.').Append(Escape(branch.Symbol.Name));
                foreach (Member member in plan.Members)
                    code.Append(", ").Append(member == branch ? "value" : member.IsCase ? "default" : Parameter(member.Symbol.Name));
                code.AppendLine("); }");
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

            if (plan.PreparedRead)
            {
                code.Append("public static void CanonicalReadInto(").Append(Runtime).Append("CanonicalReader reader, ")
                    .Append(type.TypeKind == TypeKind.Struct ? "ref " : "").Append(fullType).AppendLine(" value) {");
                if (type.TypeKind == TypeKind.Struct && (type.IsReadOnly || union))
                {
                    if (union)
                    {
                        foreach (Member member in common)
                            code.Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name))
                                .Append(" = ").Append(Read(member.Type)).AppendLine(";");
                        code.Append("value = CanonicalReadPayload(reader");
                        foreach (Member member in common)
                            code.Append(", ").Append(Parameter(member.Symbol.Name));
                        code.AppendLine(");");
                    }
                    else
                    {
                        code.Append("value = new ").Append(fullType).Append("(default(").Append(Runtime).Append("CanonicalConstruction)");
                        foreach (Member member in plan.Members)
                            code.Append(", ").Append(Read(member.Type));
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
                            code.Append(target).Append(" = ").Append(Read(member.Type)).AppendLine(";");
                    }
                    foreach (IMethodSymbol validation in plan.Validations)
                        code.Append("value.").Append(Escape(validation.Name)).AppendLine("();");
                }
                code.AppendLine("}");
            }
            if (type.TypeKind == TypeKind.Struct && plan.PreparedRead && (type.IsReadOnly || union || plan.MaterializedRead))
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalRead(").Append(Runtime).AppendLine("CanonicalReader reader) {");
                code.Append(fullType).AppendLine(" value = default;");
                code.AppendLine("CanonicalReadInto(reader, ref value); return value; }");
            }
            else if (type.TypeKind == TypeKind.Class && plan.MaterializedRead)
            {
                code.Append("private ").Append(name).Append('(').Append(Runtime).Append("CanonicalConstruction construction, ")
                    .Append(Runtime).AppendLine("CanonicalReader reader) {");
                foreach (Member member in plan.Members)
                {
                    string target = "this." + Escape(member.Symbol.Name);
                    ITypeSymbol element = BufferElement(member.Type);
                    if (element != null)
                    {
                        code.AppendLine("{ int count = reader.ReadInt32();");
                        code.Append("if (count < 0 || count > reader.Remaining / ").Append(Minimum(element)).AppendLine(") throw new global::System.IO.InvalidDataException(\"Canonical collection length is invalid.\");");
                        code.Append(target).Append(" = new ").Append(TypeName(member.Type)).AppendLine("(count);");
                        code.AppendLine("for (int i = 0; i < count; i++) {");
                        code.Append(target).Append(".Element(i) = ").Append(Read(element)).AppendLine(";");
                        code.Append(target).AppendLine(".CompleteReadElement(); } }");
                    }
                    else
                        code.Append(target).Append(" = ").Append(Read(member.Type)).AppendLine(";");
                }
                foreach (IMethodSymbol validation in plan.Validations)
                    code.Append(Escape(validation.Name)).AppendLine("();");
                code.AppendLine("}");
                code.Append("public static ").Append(fullType).Append(" CanonicalReadMaterialized(")
                    .Append(Runtime).Append("CanonicalReader reader) => new ").Append(fullType)
                    .Append("(default(").Append(Runtime).AppendLine("CanonicalConstruction), reader);");
            }
            else if (type.TypeKind == TypeKind.Struct && plan.MaterializedRead && !plan.PreparedRead)
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalReadMaterialized(")
                    .Append(Runtime).AppendLine("CanonicalReader reader) {");
                if (union)
                    code.Append("return CanonicalReadPayload(reader");
                else
                    code.Append("return new ").Append(fullType).Append("(default(").Append(Runtime).Append("CanonicalConstruction)");
                foreach (Member member in union ? common : plan.Members)
                    code.Append(", ").Append(Read(member.Type));
                code.AppendLine("); }");
            }
            if (union)
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalReadPayload(").Append(Runtime)
                    .Append("CanonicalReader reader");
                foreach (Member member in common)
                    code.Append(", ").Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name));
                code.AppendLine(") {");
                code.AppendLine("switch (reader.ReadInt32()) {");
                foreach (Member member in plan.Cases)
                {
                    code.Append("case (int)").Append(plan.KindType).Append('.').Append(Escape(member.Symbol.Name))
                        .Append(": return From").Append(member.Symbol.Name).Append('(');
                    code.Append(string.Join(", ", common.Select(m => Parameter(m.Symbol.Name))));
                    if (common.Length > 0)
                        code.Append(", ");
                    code.Append(Read(member.Type)).AppendLine(");");
                }
                code.AppendLine("default: throw new global::System.IO.InvalidDataException(\"Canonical union case is invalid.\"); } }");
            }
            code.Append("public static void CanonicalSkip(").Append(Runtime).AppendLine("CanonicalReader reader) {");
            foreach (Member member in common)
                Skip(code, member.Type);
            if (union)
                code.AppendLine("CanonicalSkipPayload(reader);");
            code.AppendLine("}");
            if (union)
            {
                code.Append("public static void CanonicalSkipPayload(").Append(Runtime).AppendLine("CanonicalReader reader) {");
                Switch(code, plan, "reader.ReadInt32()", member => Skip(code, member.Type), numeric: true);
                code.AppendLine("}");
            }
            code.Append("public static int CanonicalMeasure(in ").Append(fullType).AppendLine(" value) {");
            code.Append("int length = ").Append(union ? "4" : "0").AppendLine(";");
            foreach (Member member in common)
                MeasureMember(code, member.Type, "value." + Escape(member.Symbol.Name));
            if (union)
                Switch(code, plan, "value.Kind", member => code.Append("length = checked(length + ")
                    .Append(Measure(member.Type, "value." + Escape(member.Symbol.Name))).AppendLine(");"));
            code.AppendLine("return length; }");
            foreach (string operation in new[] { "Retain", "Release" })
            {
                code.Append("public static void Canonical").Append(operation).Append("(in ").Append(fullType).AppendLine(" value) {");
                foreach (Member member in common)
                    Hold(code, operation, member.Type, "value." + Escape(member.Symbol.Name), plan);
                if (union && plan.Cases.Any(m => HasResources(plan, m.Type)))
                    Switch(code, plan, "value.Kind", member => Hold(code, operation, member.Type, "value." + Escape(member.Symbol.Name), plan), false);
                code.AppendLine("}");
            }
            code.AppendLine("} }");
            return code.ToString();
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

        static void Write(StringBuilder code, ITypeSymbol type, string value)
        {
            ITypeSymbol element = BufferElement(type);
            if (element != null)
            {
                code.Append("writer.WriteInt32(").Append(value).AppendLine(".Count);");
                code.Append("for (int i = 0; i < ").Append(value).AppendLine(".Count; i++) {");
                Write(code, element, value + ".Element(i)");
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

        static string Read(ITypeSymbol type)
        {
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
                return Runtime + "CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())";
            string value = primitive != null ? "reader.Read" + primitive + "()" :
                Identity(type) == TextType ? "reader.ReadText()" : TypeName(type) +
                (type.TypeKind == TypeKind.Class || !UsesPreparedRead(type) ? ".CanonicalReadMaterialized(reader)" : ".CanonicalRead(reader)");
            return value;
        }

        static void Skip(StringBuilder code, ITypeSymbol type)
        {
            ITypeSymbol element = BufferElement(type);
            if (element != null)
            {
                code.AppendLine("{ int count = reader.ReadInt32();");
                code.AppendLine("if (count < 0) throw new global::System.IO.InvalidDataException(\"Canonical collection count is invalid.\");");
                code.AppendLine("for (int i = 0; i < count; i++) {");
                Skip(code, element);
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

        static bool HasResources(Plan plan, ITypeSymbol type) => Identity(type) == TextType ||
            (BufferElement(type) is ITypeSymbol element ? HasResources(plan, element) : plan.ResourceTypes.Contains(Identity(type)));

        static void Hold(StringBuilder code, string operation, ITypeSymbol type, string value, Plan plan)
        {
            if (!HasResources(plan, type))
                return;
            ITypeSymbol element = BufferElement(type);
            if (element != null)
            {
                code.Append("for (int i = 0; i < ").Append(value).AppendLine(".Count; i++) {");
                Hold(code, operation, element, value + ".Element(i)", plan);
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

        static string Minimum(ITypeSymbol type) => BufferElement(type) != null || Identity(type) == TextType || type.SpecialType == SpecialType.System_String ? "4" :
            Primitive(type) != null ? Measure(type, "") : TypeName(type) + ".CanonicalMinimumSize";

        static void MeasureMember(StringBuilder code, ITypeSymbol type, string value)
        {
            ITypeSymbol element = BufferElement(type);
            if (element == null)
                code.Append("length = checked(length + ").Append(Measure(type, value)).AppendLine(");");
            else
            {
                code.AppendLine("length = checked(length + 4);");
                code.Append("for (int i = 0; i < ").Append(value).AppendLine(".Count; i++) {");
                MeasureMember(code, element, value + ".Element(i)");
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
            public bool HashValue;
            public bool HasResources;
            public HashSet<string> ResourceTypes;
        }

        sealed class Planner
        {
            readonly GeneratorExecutionContext m_Context;
            readonly Dictionary<string, Plan> m_Plans = new Dictionary<string, Plan>(StringComparer.Ordinal);
            readonly HashSet<string> m_ResourceTypes = new HashSet<string>(StringComparer.Ordinal);
            public IEnumerable<Plan> Plans => m_Plans.Values;
            public Planner(GeneratorExecutionContext context) => m_Context = context;

            public Plan Plan(INamedTypeSymbol type)
            {
                if (m_Plans.TryGetValue(Identity(type), out Plan existing))
                {
                    if (!existing.Complete)
                        Error(existing, type, "正式记录不能递归引用自身；页引用必须由所属存储显式承载");
                    return existing;
                }
                var plan = new Plan { Type = type, ResourceTypes = m_ResourceTypes, PreparedRead = UsesPreparedRead(type),
                    HashValue = type.AllInterfaces.Any(i => Identity(i) == HashValueType),
                    MaterializedRead = type.GetAttributes().Single(a => Identity(a.AttributeClass) == ContractAttribute)
                        .NamedArguments.Any(a => a.Key == "MaterializedRead" && a.Value.Value is true) };
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
                if (plan.Cases.Length > 0 && type.TypeKind != TypeKind.Struct)
                    Error(plan, type, "值联合必须声明为 struct；页联合使用所属预分配存储");
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
                    descriptor.Append('|').Append(member.IsCase ? "case:" : "member:").Append(MemberIdentity(plan, member))
                        .Append(':').Append(Shape(plan, member.Type, member.Symbol));
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
                if (BufferElement(type) != null || Identity(type) == TextType || type.SpecialType == SpecialType.System_String)
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
                    if (plan.PreparedRead)
                        Error(plan, member, "程序字符串只允许初始化物化读取；实时文本必须由正式文本 owner 持有");
                    return Identity(type);
                }
                ITypeSymbol element = BufferElement(type);
                if (element != null)
                {
                    if (plan.Type.IsReadOnly || plan.Cases.Length != 0)
                        Error(plan, member, "预分配集合必须由原候选页持有，不能通过值构造复制");
                    return "prepared-sequence<" + Shape(plan, element, member) + ">";
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
                    if (SymbolEqualityComparer.Default.Equals(record.ContainingAssembly, m_Context.Compilation.Assembly))
                    {
                        Plan child = Plan(record);
                        if (!child.Valid)
                            plan.Valid = false;
                        return Identity(type) + "#" + child.Schema;
                    }
                    var schema = record.GetMembers("CanonicalSchema").OfType<IFieldSymbol>().SingleOrDefault();
                    var resources = record.GetMembers("CanonicalHasResources").OfType<IFieldSymbol>().SingleOrDefault();
                    if (schema != null && schema.HasConstantValue && resources != null && resources.HasConstantValue)
                    {
                        if ((bool)resources.ConstantValue)
                            m_ResourceTypes.Add(Identity(type));
                        return Identity(type) + "#" + schema.ConstantValue;
                    }
                }
                Error(plan, member, "成员类型 " + Identity(type) + " 尚未声明可直接重建的 canonical 合同或存储形状");
                return Identity(type);
            }

            void Error(Plan plan, ISymbol symbol, string reason)
            {
                plan.Valid = false;
                Location location = SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, m_Context.Compilation.Assembly)
                    ? symbol.Locations.FirstOrDefault() : plan.Type.Locations.FirstOrDefault();
                m_Context.ReportDiagnostic(Diagnostic.Create(InvalidContract, location, Identity(symbol), reason));
            }

        }

        static Member[] StorageMembers(INamedTypeSymbol type) => type.GetMembers()
            .Where(s => !s.IsStatic && !s.IsImplicitlyDeclared)
            .Where(s => s is IFieldSymbol || s is IPropertySymbol p && IsAutoProperty(p))
            .OrderBy(Identity, StringComparer.Ordinal)
            .Select(s => new Member { Symbol = s, Type = s is IFieldSymbol f ? f.Type : ((IPropertySymbol)s).Type,
                IsCase = HasAttribute(s, UnionCaseAttribute) }).ToArray();

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
