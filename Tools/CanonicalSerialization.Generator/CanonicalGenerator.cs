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
        const string UnavailableType = "ThirdPersonSimulation.CanonicalUnavailable";
        const string BorrowedSequenceAttribute = "ThirdPersonSimulation.CanonicalBorrowedSequenceAttribute";
        const string BorrowedObjectAttribute = "ThirdPersonSimulation.CanonicalBorrowedObjectAttribute";
        const string DeclaredTextAttribute = "ThirdPersonSimulation.CanonicalDeclaredTextAttribute";
        const string TextRulesAttribute = "ThirdPersonSimulation.CanonicalTextRulesAttribute";
        const string PublishedAttribute = "ThirdPersonSimulation.CanonicalPublishedAttribute";
        const string BoundAttribute = "ThirdPersonSimulation.CanonicalBoundAttribute";
        const string ReferenceAccessAttribute = "ThirdPersonSimulation.CanonicalReferenceAccessAttribute";
        const string DefaultValueAttribute = "ThirdPersonSimulation.CanonicalDefaultValueAttribute";
        const string SetAttribute = "ThirdPersonSimulation.CanonicalSetAttribute";
        const string StorageOwnerAttribute = "ThirdPersonSimulation.CanonicalStorageOwnerAttribute";
        const string StorageAttribute = "ThirdPersonSimulation.CanonicalStorageAttribute";
        const string PublishedReferenceAttribute = "ThirdPersonSimulation.CanonicalPublishedReferenceAttribute";
        const string ReferenceKeyAttribute = "ThirdPersonSimulation.CanonicalReferenceKeyAttribute";
        const string PresenceAttribute = "ThirdPersonSimulation.CanonicalPresenceAttribute";
        const string RequiredAttribute = "ThirdPersonSimulation.CanonicalRequiredAttribute";
        const string RecordType = "ThirdPersonSimulation.ICanonicalRecord<T>";
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
            var storageOwners = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var kindTrees = new List<SyntaxTree>();
            foreach (BaseTypeDeclarationSyntax declaration in receiver.Types)
            {
                var type = (INamedTypeSymbol)context.Compilation.GetSemanticModel(declaration.SyntaxTree)
                    .GetDeclaredSymbol(declaration);
                if (HasAttribute(type, ContractAttribute))
                    types[Identity(type)] = type;
                if (HasAttribute(type, UnionTemplateAttribute))
                    templates[Identity(type)] = type;
                if (HasAttribute(type, StorageOwnerAttribute))
                    storageOwners[Identity(type)] = type;
            }
            foreach (INamedTypeSymbol template in templates.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
                if (!ValidateTemplate(context, template)) return;
            foreach (INamedTypeSymbol template in templates.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
            {
                Member[] cases = Cases(UnionMembers(template));
                if (cases.Length == 0 || template.TypeKind != TypeKind.Struct && template.TypeKind != TypeKind.Enum)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidContract, template.Locations.FirstOrDefault(),
                        Identity(template), "联合模板必须在正式 struct 成员或 enum 分支声明闭合载荷"));
                    continue;
                }
                if (template.TypeKind == TypeKind.Enum) continue;
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
            foreach (INamedTypeSymbol owner in storageOwners.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
            {
                SyntaxReference declaration = owner.DeclaringSyntaxReferences[0];
                EmitStorageOwner(context, (INamedTypeSymbol)compilation.GetSemanticModel(declaration.SyntaxTree)
                    .GetDeclaredSymbol(declaration.GetSyntax()), planner);
            }
            foreach (Plan plan in planner.Plans.OrderBy(p => Identity(p.Type), StringComparer.Ordinal))
            {
                if (!plan.Valid)
                    continue;
                context.AddSource(Identity(plan.Type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Canonical.g.cs",
                    SourceText.From(Emit(plan), Encoding.UTF8));
            }
        }

        static void EmitStorageOwner(GeneratorExecutionContext context, INamedTypeSymbol owner, Planner planner)
        {
            IFieldSymbol[] fields = owner.GetMembers().OfType<IFieldSymbol>()
                .Where(field => HasAttribute(field, StorageAttribute)).ToArray();
            string reason = null;
            if (owner.ContainingType != null || fields.Length != 1 || fields[0].IsStatic ||
                !(fields[0].Type is INamedTypeSymbol data) || !HasAttribute(data, ContractAttribute))
                reason = "状态 owner 必须由唯一真实字段持有正式数据，设施不进入该数据记录";
            else if (owner.DeclaringSyntaxReferences.Any(syntax =>
                !(syntax.GetSyntax() is TypeDeclarationSyntax declaration) || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
                reason = "状态 owner 必须声明 partial，访问直接进入原类型";
            else if (StorageMembers((INamedTypeSymbol)fields[0].Type).Any(member =>
                owner.GetMembers(member.Symbol.Name).Length != 0))
                reason = "状态成员由唯一正式数据生成访问，owner 不能重复声明同名成员";
            if (reason != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidContract, owner.Locations.FirstOrDefault(), Identity(owner), reason));
                return;
            }
            IFieldSymbol storage = fields[0];
            Plan plan = planner.StoragePlan((INamedTypeSymbol)storage.Type);
            if (!plan.Valid) return;
            var code = new StringBuilder();
            code.Append("namespace ").Append(owner.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            code.Append(owner.DeclaredAccessibility == Accessibility.Public ? "public" : "internal")
                .Append(owner.IsReadOnly ? " readonly" : "").Append(" partial ")
                .Append(owner.TypeKind == TypeKind.Struct ? "struct " : "class ").Append(Escape(owner.Name));
            if (owner.Arity != 0)
                code.Append('<').Append(string.Join(", ", owner.TypeParameters.Select(parameter => Escape(parameter.Name)))).Append('>');
            foreach (ITypeParameterSymbol parameter in owner.TypeParameters)
            {
                var constraints = new List<string>();
                if (parameter.HasValueTypeConstraint) constraints.Add("struct");
                else if (parameter.HasReferenceTypeConstraint) constraints.Add("class");
                constraints.AddRange(parameter.ConstraintTypes.Select(TypeName));
                if (parameter.HasConstructorConstraint) constraints.Add("new()");
                if (constraints.Count != 0)
                    code.Append(" where ").Append(Escape(parameter.Name)).Append(" : ").Append(string.Join(", ", constraints));
            }
            code.AppendLine(" {");
            foreach (Member member in plan.Members)
            {
                string target = Escape(storage.Name) + "." + Escape(member.Symbol.Name);
                if (HasAttribute(member.Symbol, ReferenceAccessAttribute))
                {
                    code.Append("public ref readonly ").Append(TypeName(member.Type)).Append(" ").Append(Escape(member.Symbol.Name))
                        .Append(" => ref ").Append(target).AppendLine(";");
                    code.Append("public void Set").Append(member.Symbol.Name).Append("(in ").Append(TypeName(member.Type)).AppendLine(" value) {");
                    if (plan.OwnsMemberResources)
                    {
                        Hold(code, "Retain", member.Type, "value", plan);
                        Hold(code, "Release", member.Type, target, plan);
                    }
                    code.Append(target).AppendLine(" = value; }");
                    continue;
                }
                code.Append(member.Symbol.DeclaredAccessibility == Accessibility.Public ? "public " : "internal ")
                    .Append(TypeName(member.Type)).Append(" ").Append(Escape(member.Symbol.Name)).Append(" { get => ")
                    .Append(target).Append(";");
                if (!owner.IsReadOnly && !IsPreparedCollection(member.Type) && !(storage.IsReadOnly && storage.Type.IsValueType) &&
                    !(member.Symbol is IFieldSymbol field && field.IsReadOnly) &&
                    !(member.Symbol is IPropertySymbol property && property.SetMethod == null))
                {
                    if (plan.OwnsMemberResources && HasResources(plan, member.Type))
                    {
                        code.AppendLine(" set {");
                        Hold(code, "Retain", member.Type, "value", plan);
                        Hold(code, "Release", member.Type, target, plan);
                        code.Append(target).AppendLine(" = value; }");
                    }
                    else code.Append(" set => ").Append(target).Append(" = value;");
                }
                code.AppendLine(" }");
            }
            code.AppendLine("} }");
            context.AddSource(Identity(owner).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Storage.g.cs",
                SourceText.From(code.ToString(), Encoding.UTF8));
        }

        static void EmitBorrowedSequenceReader(StringBuilder code, ITypeSymbol sequence, Plan plan, bool root = false)
        {
            ITypeSymbol element = SequenceElement(sequence);
            code.Append(root ? "public static " : "private static ").Append(TypeName(sequence)).Append(' ').Append(root ? "CanonicalRead" : SequenceReader(sequence))
                .Append("<TReader>(TReader reader, ").Append(TypeName(BorrowedStorage(sequence)))
                .Append(" storage) where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
            code.AppendLine("int count = reader.ReadInt32();");
            code.AppendLine("var values = storage.Reserve(count, out var items);");
            bool retainStorage = BorrowedSemantic(sequence, "RetainStorageOnRead", false);
            if (retainStorage) code.AppendLine("items.RetainStateStorage(); try {");
            code.AppendLine("for (int i = 0; i < count; i++) {");
            if (BorrowedObjectStorage(element) != null)
                code.Append(TypeName(element)).AppendLine(".CanonicalReadInto(reader, values[i], storage);");
            else
                code.Append("values[i] = ").Append(element.SpecialType == SpecialType.System_String &&
                    BorrowedSemantic(sequence, "DeclaredText", false) ? "reader.ReadDeclaredString()" : Read(element)).AppendLine(";");
            if (BorrowedSemantic(sequence, "SortedUnique", false))
                code.Append("if (i > 0 && ").Append(Compare(element, "values[i - 1]", "values[i]"))
                    .AppendLine(" >= 0) throw new global::System.IO.InvalidDataException(\"Canonical set elements are not strictly ordered.\");");
            if (BorrowedSemantic(sequence, "RetainElementsOnRead", false))
            {
                Hold(code, "Retain", element, "values[i]", plan);
                code.AppendLine("storage.CompleteReadElement();");
            }
            code.AppendLine("}");
            code.AppendLine("return items;");
            if (retainStorage) code.AppendLine("} catch { items.ReleaseStateStorage(); throw; }");
            code.AppendLine("}");
}

        static string EmitSequenceRoot(Plan plan)
        {
            INamedTypeSymbol type = plan.Type;
            ITypeSymbol view = type.AllInterfaces.Single(item => Identity(item.OriginalDefinition) == "System.Collections.Generic.IReadOnlyList<T>");
            string fullType = TypeName(type);
            var code = new StringBuilder();
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            code.Append(type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal")
                .Append(" partial ").Append(type.TypeKind == TypeKind.Struct ? "struct " : "class ")
                .Append(Escape(type.Name)).AppendLine(" {");
            code.Append("public const string CanonicalSchema = ").Append(SymbolDisplay.FormatLiteral(plan.Schema, true)).AppendLine(";");
            code.Append("public const string CanonicalStructure = ").Append(SymbolDisplay.FormatLiteral(plan.Descriptor, true)).AppendLine(";");
            code.Append("public static readonly ").Append(Runtime).Append("StableHash CanonicalSchemaHash = new ")
                .Append(Runtime).AppendLine("StableHash(CanonicalSchema);");
            code.Append("public static void CanonicalRequireSchema(").Append(Runtime)
                .Append("CanonicalReader reader) => reader.RequireSchema(CanonicalSchemaHash, ")
                .Append(SymbolDisplay.FormatLiteral(Identity(type), true)).AppendLine(");");
            code.AppendLine("public const int CanonicalMinimumSize = 4;");
            code.AppendLine("public const bool CanonicalHasFiniteSize = false;");
            code.Append("public const bool CanonicalHasResources = ").Append(plan.HasResources ? "true" : "false").AppendLine(";");
            code.Append("public const bool CanonicalHasTexts = ").Append(plan.HasTexts ? "true" : "false").AppendLine(";");
            code.AppendLine("public const bool CanonicalHasPayload = false;");
            code.Append("public static void CanonicalWrite<TWriter>(TWriter writer, ").Append(TypeName(view))
                .Append(" value) where TWriter : class, ").Append(Runtime).AppendLine("ICanonicalValueWriter {");
            Write(code, view, "value");
            code.AppendLine("}");
            EmitBorrowedSequenceReader(code, type, plan, true);
            code.Append("public static void CanonicalReadInto<TReader>(TReader reader, ref ").Append(fullType)
                .Append(" value, ").Append(TypeName(plan.PreparedStorage)).Append(" storage) where TReader : class, ")
                .Append(Runtime).AppendLine("ICanonicalValueReader {");
            code.AppendLine("var next = CanonicalRead(reader, storage);");
            if (!BorrowedSemantic(type, "RetainStorageOnRead", false)) Hold(code, "Retain", type, "next", plan);
            Hold(code, "Release", type, "value", plan);
            code.AppendLine("value = next; }");
            code.Append("public static void CanonicalSkip<TReader>(TReader reader) where TReader : class, ")
                .Append(Runtime).AppendLine("ICanonicalValueReader {");
            Skip(code, type);
            code.AppendLine("}");
            code.Append("public static int CanonicalMeasure(").Append(TypeName(view)).AppendLine(" value) { int length = 0;");
            MeasureMember(code, view, "value");
            code.AppendLine("return length; }");
            code.Append("public static int CanonicalMeasureCapacity(").Append(TypeName(view))
                .AppendLine(" value, int maximumTextCharacters) { int length = 0;");
            MeasureCapacity(code, plan, view, "value");
            code.AppendLine("return length; }");
            code.Append("public static void CanonicalVisitTexts<TVisitor>(TVisitor visitor, ").Append(TypeName(view))
                .Append(" value) where TVisitor : class, ").Append(Runtime).AppendLine("ICanonicalTextVisitor {");
            VisitTexts(code, view, "value", plan);
            code.AppendLine("}");
            foreach (string action in new[] { "Retain", "Release" })
            {
                code.Append("public static void Canonical").Append(action).Append("(in ").Append(fullType).AppendLine(" value) {");
                Hold(code, action, type, "value", plan);
                code.AppendLine("}");
            }
            code.AppendLine("} }");
            return code.ToString();
        }

        static string Emit(Plan plan)
        {
            if (plan.SequenceRoot) return EmitSequenceRoot(plan);
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
                .Append(name);
            if (type.Arity != 0)
                code.Append('<').Append(string.Join(", ", type.TypeParameters.Select(p => Escape(p.Name)))).Append('>');
            bool recordProtocol = type.TypeKind == TypeKind.Struct && type.IsReadOnly && plan.PreparedRead &&
                plan.PreparedStorage == null && plan.TextCapacity != null && plan.FiniteSize;
            if (recordProtocol)
                code.Append(" : ").Append(Runtime).Append("ICanonicalRecord<").Append(fullType).Append('>');
            foreach (ITypeParameterSymbol parameter in type.TypeParameters)
            {
                var constraints = new List<string>();
                if (parameter.HasValueTypeConstraint) constraints.Add("struct");
                else if (parameter.HasReferenceTypeConstraint) constraints.Add("class");
                constraints.AddRange(parameter.ConstraintTypes.Select(TypeName));
                if (parameter.HasConstructorConstraint) constraints.Add("new()");
                if (constraints.Count != 0)
                    code.Append(" where ").Append(Escape(parameter.Name)).Append(" : ").Append(string.Join(", ", constraints));
            }
            code.AppendLine(" {");
            foreach (Member member in plan.Members.Where(m => m.Generated))
                code.Append("public ").Append(TypeName(member.Type)).Append(' ')
                    .Append(Escape(member.Symbol.Name)).AppendLine(" { get; }");
            if (type.Arity == 0)
                code.Append("public const string CanonicalSchema = \"").Append(plan.Schema).AppendLine("\";");
            else
            {
                code.Append("public const string CanonicalTemplateSchema = \"").Append(plan.Schema).AppendLine("\";");
                code.Append("public static readonly string CanonicalSchema = ").Append(Runtime)
                    .Append("CanonicalContractProtocol.ComposeSchema(CanonicalTemplateSchema");
                foreach (ITypeParameterSymbol parameter in type.TypeParameters)
                    code.Append(", default(").Append(TypeName(parameter)).Append(").CanonicalSchemaIdentity");
                code.AppendLine(");");
            }
            if (plan.PublishedRead)
            {
                code.Append("public static ").Append(fullType)
                    .Append(" CanonicalReadPublished<TReader>(TReader reader, in ").Append(fullType)
                    .Append(" published) where TReader : class, ").Append(Runtime)
                    .AppendLine("ICanonicalValueReader { CanonicalWrite(reader.MatchWriter, in published); return published; }");
            }
            code.Append("public const int CanonicalMinimumSize = ").Append(plan.MinimumSize).AppendLine(";");
            code.Append("public const bool CanonicalHasFiniteSize = ").Append(plan.FiniteSize ? "true" : "false").AppendLine(";");
            code.Append("public const bool CanonicalHasResources = ").Append(plan.HasResources ? "true" : "false").AppendLine(";");
            code.Append("public const bool CanonicalHasTexts = ").Append(plan.HasTexts ? "true" : "false").AppendLine(";");
            code.Append("public const bool CanonicalHasPayload = ").Append(plan.HasPayload ? "true" : "false").AppendLine(";");
            if (plan.TextCapacity != null)
            {
                code.Append("public static readonly int CanonicalMaximumTextReferences = checked(")
                    .Append(plan.TextCapacity).AppendLine(");");
                if (plan.FiniteSize)
                    code.Append("public static int CanonicalMaximumEncodedSize(int maximumTextCharacters) => checked(")
                        .Append(MaximumSize(plan)).AppendLine(");");
            }
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
                if (union)
                {
                    foreach (List<Member> branches in CaseGroups(plan.Cases))
                    {
                        code.Append("private ").Append(name).Append('(').Append(Runtime)
                            .Append("CanonicalConstruction construction, ").Append(plan.KindType).Append(" kind");
                        foreach (Member member in common)
                            code.Append(", in ").Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name));
                        code.Append(", in ").Append(TypeName(branches[0].Type)).AppendLine(" value) {");
                        if (type.TypeKind == TypeKind.Struct) code.AppendLine("this = default;");
                        code.AppendLine("Kind = kind;");
                        foreach (Member member in common)
                            code.Append("this.").Append(Escape(member.Symbol.Name)).Append(" = ")
                                .Append(Normalize(member, Parameter(member.Symbol.Name))).AppendLine(";");
                        if (branches.Count == 1)
                            code.Append("this.").Append(Escape(branches[0].Symbol.Name)).Append(" = ")
                                .Append(Normalize(branches[0], "value")).AppendLine(";");
                        else
                        {
                            code.AppendLine("switch (kind) {");
                            foreach (Member member in branches)
                                code.Append("case ").Append(plan.KindType).Append('.').Append(Escape(member.Symbol.Name))
                                    .Append(": this.").Append(Escape(member.Symbol.Name)).Append(" = ")
                                    .Append(Normalize(member, "value")).AppendLine("; break;");
                            code.AppendLine("}");
                        }
                        foreach (IMethodSymbol validation in plan.Validations)
                            code.Append(Escape(validation.Name)).AppendLine("();");
                        code.AppendLine("}");
                    }
                }
                else
                {
                    code.Append("private ").Append(name).Append('(').Append(Runtime)
                        .Append("CanonicalConstruction construction");
                    for (int i = 0; i < plan.Members.Length; i++)
                        code.Append(", in ").Append(TypeName(plan.Members[i].Type)).Append(" value").Append(i);
                    code.AppendLine(") {");
                    for (int i = 0; i < plan.Members.Length; i++)
                        code.Append("this.").Append(Escape(plan.Members[i].Symbol.Name)).Append(" = ")
                            .Append(Normalize(plan.Members[i], "value" + i)).AppendLine(";");
                    foreach (IMethodSymbol validation in plan.Validations)
                        code.Append(Escape(validation.Name)).AppendLine("();");
                    code.AppendLine("}");
                }
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
                foreach (Member member in common)
                    code.Append(", ").Append(!plan.PreparedRead && SequenceElement(member.Type) != null ?
                        FreezeSequence(member.Type, Parameter(member.Symbol.Name)) : Parameter(member.Symbol.Name));
                code.Append(", value");
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
                    string defaultMember = CaseDefaultMember(member);
                    code.Append(defaultMember == null ? "default(" + TypeName(member.Type) + ")" :
                        TypeName(member.Type) + "." + Escape(defaultMember)).AppendLine(");");
                }
                code.AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(kind)); } }");
            }

            code.Append("public static void CanonicalWrite<TWriter>(TWriter writer, in ")
                .Append(fullType).Append(" value) where TWriter : class, ").Append(Runtime).AppendLine("ICanonicalValueWriter {");
            if (plan.Presence != null)
            {
                code.Append("bool present = value.").Append(Escape(plan.Presence.Name))
                    .AppendLine("; writer.WriteBoolean(present); if (!present) return;");
                code.AppendLine("CanonicalWriteContent(writer, in value); }");
                code.Append("public static void CanonicalWriteRequired<TWriter>(TWriter writer, in ")
                    .Append(fullType).Append(" value) where TWriter : class, ").Append(Runtime).AppendLine("ICanonicalValueWriter {");
                code.Append("if (!value.").Append(Escape(plan.Presence.Name))
                    .AppendLine(") throw new global::System.IO.InvalidDataException(\"Required canonical record is absent.\");");
                code.AppendLine("writer.WriteBoolean(true); CanonicalWriteContent(writer, in value); }");
                code.Append("private static void CanonicalWriteContent<TWriter>(TWriter writer, in ")
                    .Append(fullType).Append(" value) where TWriter : class, ").Append(Runtime).AppendLine("ICanonicalValueWriter {");
            }
            if (plan.PreparedObject)
                code.AppendLine("writer.WriteBoolean(value != null); if (value == null) return;");
            foreach (Member member in common)
                WriteMember(code, member, "value." + Escape(member.Symbol.Name));
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
                if (plan.Presence != null)
                {
                    code.AppendLine(type.TypeKind == TypeKind.Class ?
                        "if (!reader.ReadBoolean()) { CanonicalClear(value); return; }" :
                        "if (!reader.ReadBoolean()) { value = default; return; }");
                    code.Append("CanonicalReadContent(reader, ref value")
                        .Append(plan.PreparedStorage == null ? "" : ", storage").AppendLine("); }");
                    code.Append("private static void CanonicalReadContent<TReader>(TReader reader, ref ")
                        .Append(fullType).Append(" value")
                        .Append(plan.PreparedStorage == null ? "" : ", " + TypeName(plan.PreparedStorage) + " storage")
                        .Append(") where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                }
                if (plan.PreparedObject)
                {
                    code.AppendLine("if (!reader.ReadBoolean()) throw new global::System.IO.InvalidDataException(\"Required prepared object is absent.\");");
                    code.AppendLine("CanonicalReadContent(reader, value, storage); }");
                    code.Append("public static ").Append(fullType).Append(" CanonicalRead<TReader>(TReader reader, ")
                        .Append(TypeName(plan.PreparedStorage)).Append(" storage) where TReader : class, ")
                        .Append(Runtime).AppendLine("ICanonicalValueReader {");
                    code.AppendLine("if (!reader.ReadBoolean()) return null;");
                    code.AppendLine("var value = storage.Reserve(); CanonicalReadContent(reader, value, storage); return value; }");
                    code.Append("private static void CanonicalReadContent<TReader>(TReader reader, ")
                        .Append(fullType).Append(" value, ").Append(TypeName(plan.PreparedStorage))
                        .Append(" storage) where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                    foreach (Member member in plan.Members)
                    {
                        string target = "value." + Escape(member.Symbol.Name);
                        code.Append("{ var memberValue = ").Append(ReadMember(member, plan)).AppendLine(";");
                        Hold(code, "Retain", member.Type, "memberValue", plan);
                        Hold(code, "Release", member.Type, target, plan);
                        code.Append(target).AppendLine(" = memberValue; }");
                    }
                    foreach (IMethodSymbol validation in plan.Validations)
                        code.Append("value.").Append(Escape(validation.Name)).AppendLine(validation.Parameters.Length == 0 ? "();" : "(reader);");
                }
                else if (type.TypeKind == TypeKind.Struct && (type.IsReadOnly || union))
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
                        code.Append("value = new ").Append(fullType).Append("(").Append(Runtime).Append("CanonicalConstruction.Encoded");
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
                        if (HasAttribute(member.Symbol, BoundAttribute))
                        {
                            code.AppendLine("{ var writer = reader.MatchWriter;");
                            WriteMember(code, member, target);
                            code.AppendLine("}");
                        }
                        else if (IsPreparedCollection(member.Type))
                            ReadPreparedCollection(code, member, target, plan);
                        else if (OwnsSequenceStorage(member.Type))
                            code.Append("_ = ").Append(ReadMember(member, plan)).AppendLine(";");
                        else if (BorrowedStorage(member.Type) != null || BorrowedObjectStorage(member.Type) != null)
                        {
                            code.Append("{ var memberValue = ").Append(ReadMember(member, plan)).AppendLine(";");
                            if (plan.OwnsMemberResources)
                            {
                                if (BorrowedStorage(member.Type) == null || !BorrowedSemantic(member.Type, "RetainStorageOnRead", false))
                                    Hold(code, "Retain", member.Type, "memberValue", plan);
                                Hold(code, "Release", member.Type, target, plan);
                            }
                            code.Append(target).AppendLine(" = memberValue; }");
                        }
                        else if (member.Type is IArrayTypeSymbol array)
                        {
                            code.AppendLine("{ int count = reader.ReadInt32();");
                            code.Append("if (count != ").Append(target).AppendLine(".Length) throw new global::System.IO.InvalidDataException(\"Canonical array does not match prepared storage.\");");
                            code.AppendLine("for (int i = 0; i < count; i++) {");
                            if (array.ElementType.TypeKind == TypeKind.Class && array.ElementType.SpecialType != SpecialType.System_String)
                                code.Append(TypeName(array.ElementType)).Append(".CanonicalReadInto(reader, ").Append(target)
                                    .Append("[i]").Append(PreparedStorage(array.ElementType) == null ? "" : ", storage").AppendLine(");");
                            else if (array.ElementType is INamedTypeSymbol mutable && mutable.TypeKind == TypeKind.Struct &&
                                !mutable.IsReadOnly && Primitive(mutable) == null && Identity(mutable) != TextType)
                                code.Append(TypeName(mutable)).Append(".CanonicalReadInto(reader, ref ").Append(target).AppendLine("[i]);");
                            else
                            {
                                code.Append("var memberValue = ").Append(Read(array.ElementType)).AppendLine(";");
                                if (plan.OwnsMemberResources)
                                {
                                    Hold(code, "Retain", array.ElementType, "memberValue", plan);
                                    Hold(code, "Release", array.ElementType, target + "[i]", plan);
                                }
                                code.Append(target).AppendLine("[i] = memberValue;");
                            }
                            code.AppendLine("} }");
                        }
                        else if (plan.OwnsMemberResources && HasResources(plan, member.Type))
                        {
                            code.Append("{ var memberValue = ").Append(ReadMember(member, plan)).AppendLine(";");
                            Hold(code, "Retain", member.Type, "memberValue", plan);
                            Hold(code, "Release", member.Type, target, plan);
                            code.Append(target).AppendLine(" = memberValue; }");
                        }
                        else if (element != null)
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
                        else if (ReferenceKey(member.Type) != null || NullableElement(member.Type) != null || HasAttribute(member.Symbol, PublishedAttribute) ||
                            member.Type.TypeKind == TypeKind.TypeParameter || HasAttribute(member.Symbol, RequiredAttribute))
                            code.Append(target).Append(" = ").Append(ReadMember(member, plan)).AppendLine(";");
                        else if (member.Type.TypeKind == TypeKind.Class && Identity(member.Type) != TextType)
                            code.Append(TypeName(member.Type)).Append(".CanonicalReadInto(reader, ").Append(target).AppendLine(");");
                        else if (Primitive(member.Type) == null && Identity(member.Type) != TextType && member.Symbol is IFieldSymbol)
                            code.Append(TypeName(member.Type)).Append(".CanonicalReadInto(reader, ref ").Append(target).AppendLine(");");
                        else
                            code.Append(target).Append(" = ").Append(ReadMember(member, plan)).AppendLine(";");
                    }
                    foreach (IMethodSymbol validation in plan.Validations)
                        code.Append("value.").Append(Escape(validation.Name)).AppendLine(validation.Parameters.Length == 0 ? "();" : "(reader);");
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
                if (plan.Presence != null)
                {
                    code.Append("public static ").Append(fullType).Append(" CanonicalReadRequired<TReader>(TReader reader")
                        .Append(plan.PreparedStorage == null ? "" : ", " + TypeName(plan.PreparedStorage) + " storage")
                        .Append(") where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                    code.AppendLine("if (!reader.ReadBoolean()) throw new global::System.IO.InvalidDataException(\"Required canonical record is absent.\");");
                    code.Append(fullType).AppendLine(" value = default;");
                    code.Append("CanonicalReadContent(reader, ref value")
                        .Append(plan.PreparedStorage == null ? "" : ", storage").AppendLine(");");
                    code.AppendLine("return value; }");
                }
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
                        code.Append(target).Append(" = ").Append(Normalize(member, Read(member.Type, true))).AppendLine(";");
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
                    .Append("(").Append(Runtime).Append("CanonicalConstruction.Encoded, reader")
                    .AppendLine(union ? ", reader);" : ");");
                if (union)
                    code.Append("public static ").Append(fullType)
                        .Append(" CanonicalReadMaterialized<TLayoutReader, TPayloadReader>(TLayoutReader layoutReader, TPayloadReader payloadReader) ")
                        .Append("where TLayoutReader : class, ").Append(Runtime).Append("ICanonicalValueReader ")
                        .Append("where TPayloadReader : class, ").Append(Runtime).Append("ICanonicalValueReader => new ")
                        .Append(fullType).Append("(").Append(Runtime).AppendLine("CanonicalConstruction.Encoded, layoutReader, payloadReader);");
            }
            else if (type.TypeKind == TypeKind.Struct && plan.MaterializedRead && !plan.PreparedRead)
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalReadMaterialized<TReader>(TReader reader) where TReader : class, ")
                    .Append(Runtime).AppendLine("ICanonicalValueReader {");
                if (union)
                    code.Append("return CanonicalReadPayload(reader");
                else
                    code.Append("return new ").Append(fullType).Append("(").Append(Runtime).Append("CanonicalConstruction.Encoded");
                foreach (Member member in union ? common : plan.Members)
                    code.Append(", ").Append(Read(member.Type, true));
                code.AppendLine("); }");
            }
            if (type.TypeKind == TypeKind.Struct && type.IsReadOnly && !union && plan.MaterializedRead && plan.PreparedRead)
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalReadMaterialized<TReader>(TReader reader) where TReader : class, ")
                    .Append(Runtime).AppendLine("ICanonicalValueReader {");
                if (plan.Presence != null)
                    code.AppendLine("if (!reader.ReadBoolean()) return default;");
                code.Append("return new ").Append(fullType)
                    .Append("(").Append(Runtime).Append("CanonicalConstruction.Encoded");
                foreach (Member member in plan.Members)
                    code.Append(", ").Append(Read(member.Type, true));
                code.AppendLine("); }");
            }
            Member[] boundMembers = plan.Members.Where(m => HasAttribute(m.Symbol, BoundAttribute)).ToArray();
            if (boundMembers.Length != 0 && type.IsReadOnly)
            {
                code.Append("public static ").Append(fullType).Append(" CanonicalReadBound<TReader>(TReader reader");
                foreach (Member member in boundMembers)
                    code.Append(", in ").Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name));
                code.Append(") where TReader : class, ").Append(Runtime).AppendLine("ICanonicalValueReader {");
                for (int i = 0; i < plan.Members.Length; i++)
                {
                    Member member = plan.Members[i];
                    if (HasAttribute(member.Symbol, BoundAttribute))
                    {
                        code.AppendLine("{ var writer = reader.MatchWriter;");
                        WriteMember(code, member, Parameter(member.Symbol.Name));
                        code.AppendLine("}");
                    }
                    code.Append("var member").Append(i).Append(" = ").Append(HasAttribute(member.Symbol, BoundAttribute)
                        ? Parameter(member.Symbol.Name) : ReadMember(member, plan)).AppendLine(";");
                }
                code.Append("return new ").Append(fullType).Append("(").Append(Runtime).Append("CanonicalConstruction.Encoded");
                for (int i = 0; i < plan.Members.Length; i++)
                    code.Append(", member").Append(i);
                code.AppendLine("); }");
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
            foreach (ITypeSymbol nullable in NullableTypes(plan.Members.Select(m => m.Type)))
            {
                code.Append("private static ").Append(TypeName(nullable)).Append(' ').Append(SequenceReader(nullable))
                    .Append("<TReader>(TReader reader) where TReader : class, ").Append(Runtime)
                    .AppendLine("ICanonicalValueReader => reader.ReadBoolean() ? new " + TypeName(nullable) + "(" +
                        Read(NullableElement(nullable), !plan.PreparedRead) + ") : default;");
            }
            foreach (ITypeSymbol sequence in BorrowedSequences(plan.Members.Select(m => m.Type)))
                if (!HasAttribute(sequence, ContractAttribute)) EmitBorrowedSequenceReader(code, sequence, plan);

            foreach (ITypeSymbol sequence in InitializationSequences(plan.Members.Select(m => m.Type))
                .Where(_ => !plan.PreparedRead || plan.MaterializedRead))
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
                        .Append(": return new ").Append(fullType).Append("(").Append(Runtime)
                        .Append("CanonicalConstruction.Encoded, ").Append(plan.KindType).Append('.').Append(Escape(member.Symbol.Name));
                    foreach (Member value in common)
                        code.Append(", ").Append(Parameter(value.Symbol.Name));
                    code.Append(", ").Append(ReadMember(member, plan));
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
                            .Append(": return new ").Append(fullType).Append("(").Append(Runtime)
                            .Append("CanonicalConstruction.Encoded, ").Append(plan.KindType).Append('.').Append(Escape(member.Symbol.Name));
                        foreach (Member value in common)
                            code.Append(", ").Append(Parameter(value.Symbol.Name));
                        code.Append(", ").Append(Read(member.Type, true).Replace("(reader)", "(payloadReader)")
                            .Replace("reader.", "payloadReader."));
                        code.AppendLine(");");
                    }
                    code.AppendLine("default: throw new global::System.IO.InvalidDataException(\"Canonical union case is invalid.\"); } }");
                }
            }
            code.Append("public static void CanonicalSkip<TReader>(TReader reader) where TReader : class, ")
                .Append(Runtime).AppendLine("ICanonicalValueReader {");
            if (plan.Presence != null || plan.PreparedObject)
                code.AppendLine("if (!reader.ReadBoolean()) return;");
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
            if (plan.Presence != null)
                code.Append("if (!value.").Append(Escape(plan.Presence.Name)).AppendLine(") return 1;");
            if (plan.PreparedObject) code.AppendLine("if (value == null) return 1;");
            code.Append("int length = ").Append(union ? "4" : plan.Presence != null || plan.PreparedObject ? "1" : "0").AppendLine(";");
            foreach (Member member in common)
                MeasureMember(code, member.Type, "value." + Escape(member.Symbol.Name));
            if (union)
                Switch(code, plan, "value.Kind", member => MeasureMember(code, member.Type, "value." + Escape(member.Symbol.Name)));
            code.AppendLine("return length; }");
            code.Append("public static int CanonicalMeasureCapacity(in ").Append(fullType)
                .AppendLine(" value, int maximumTextCharacters) {");
            if (plan.FiniteSize)
                code.AppendLine("return CanonicalMaximumEncodedSize(maximumTextCharacters); }");
            else
            {
                code.Append("int length = ").Append(union ? "4" : plan.Presence != null || plan.PreparedObject ? "1" : "0").AppendLine(";");
                foreach (Member member in common)
                    MeasureCapacity(code, plan, member.Type, "value." + Escape(member.Symbol.Name));
                if (union)
                    Switch(code, plan, "value.Kind", member => MeasureCapacity(code, plan, member.Type, "value." + Escape(member.Symbol.Name)));
                code.AppendLine("return length; }");
            }
            foreach (string operation in new[] { "Retain", "Release" })
            {
                code.Append("public static void Canonical").Append(operation).Append("(in ").Append(fullType).AppendLine(" value) {");
                if (plan.PreparedObject) code.AppendLine("if (value == null) return;");
                foreach (Member member in common)
                    Hold(code, operation, member.Type, "value." + Escape(member.Symbol.Name), plan);
                if (union && plan.Cases.Any(m => HasResources(plan, m.Type)))
                    Switch(code, plan, "value.Kind", member => Hold(code, operation, member.Type, "value." + Escape(member.Symbol.Name), plan), false);
                code.AppendLine("}");
                if (plan.PreparedStorage != null)
                {
                    code.Append("public static void Canonical").Append(operation).Append("StorageItems(")
                        .Append(TypeName(plan.PreparedStorage)).AppendLine(" storage) {");
                    foreach (Member member in plan.Members.Where(m => BorrowedStorage(m.Type) is INamedTypeSymbol &&
                        BorrowedSemantic(m.Type, "HoldStorage", true) && HasResources(plan, SequenceElement(m.Type))))
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
            if (plan.PreparedObject) code.AppendLine("if (value == null) return;");
            if (plan.Presence != null)
                code.Append("if (!value.").Append(Escape(plan.Presence.Name)).AppendLine(") return;");
            foreach (Member member in common)
                VisitTexts(code, member.Type, "value." + Escape(member.Symbol.Name), plan);
            if (union && plan.Cases.Any(m => HasTexts(plan, m.Type)))
                Switch(code, plan, "value.Kind", member => VisitTexts(code, member.Type, "value." + Escape(member.Symbol.Name), plan), false);
            code.AppendLine("}");
            if (plan.OwnsMemberResources)
            {
                code.Append("public static void CanonicalCopyInto(")
                    .Append(type.TypeKind == TypeKind.Struct ? "in " : "").Append(fullType).Append(" source, ")
                    .Append(type.TypeKind == TypeKind.Struct ? "ref " : "").Append(fullType).AppendLine(" target) {");
                if (plan.Presence != null)
                    code.Append("if (!source.").Append(Escape(plan.Presence.Name)).AppendLine(") { CanonicalClear(target); return; }");
                foreach (Member member in plan.Members)
                {
                    string source = "source." + Escape(member.Symbol.Name);
                    string target = "target." + Escape(member.Symbol.Name);
                    if (IsPreparedCollection(member.Type))
                    {
                        CopyPreparedCollection(code, member.Type, source, target, plan);
                        continue;
                    }
                    if (OwnsSequenceStorage(member.Type))
                    {
                        code.Append(target).Append(".CopyFrom(").Append(source).AppendLine(");");
                        continue;
                    }
                    ITypeSymbol element = (member.Type as IArrayTypeSymbol)?.ElementType;
                    if (element != null)
                    {
                        code.Append("for (int i = 0; i < ").Append(source).AppendLine(".Length; i++) {");
                        source += "[i]";
                        target += "[i]";
                    }
                    if (element?.TypeKind == TypeKind.Class && element.SpecialType != SpecialType.System_String)
                        code.Append(TypeName(element)).Append(".CanonicalCopyInto(").Append(source).Append(", ").Append(target).AppendLine(");");
                    else
                    {
                        Hold(code, "Retain", element ?? member.Type, source, plan);
                        Hold(code, "Release", element ?? member.Type, target, plan);
                        code.Append(target).Append(" = ").Append(source).AppendLine(";");
                    }
                    if (element != null) code.AppendLine("}");
                }
                code.AppendLine("}");
                code.Append("public static void CanonicalClear(")
                    .Append(type.TypeKind == TypeKind.Struct ? "ref " : "").Append(fullType).AppendLine(" value) {");
                foreach (Member member in plan.Members)
                {
                    string target = "value." + Escape(member.Symbol.Name);
                    if (IsPreparedCollection(member.Type))
                    {
                        Hold(code, "Release", member.Type, target, plan);
                        code.Append(target).AppendLine(".Clear();");
                        continue;
                    }
                    if (OwnsSequenceStorage(member.Type))
                    {
                        code.Append(target).AppendLine(".Clear();");
                        continue;
                    }
                    ITypeSymbol element = (member.Type as IArrayTypeSymbol)?.ElementType;
                    if (element != null)
                    {
                        code.Append("for (int i = 0; i < ").Append(target).AppendLine(".Length; i++) {");
                        target += "[i]";
                    }
                    if (element?.TypeKind == TypeKind.Class && element.SpecialType != SpecialType.System_String)
                        code.Append(TypeName(element)).Append(".CanonicalClear(").Append(target).AppendLine(");");
                    else
                    {
                        Hold(code, "Release", element ?? member.Type, target, plan);
                        code.Append(target).Append(" = ").Append(DefaultValue(element ?? member.Type)).AppendLine(";");
                    }
                    if (element != null) code.AppendLine("}");
                }
                code.AppendLine("}");
            }
            if (recordProtocol)
            {
                code.Append("public string CanonicalSchemaIdentity => CanonicalSchema;").AppendLine();
                code.Append("public int CanonicalRecordMinimumSize => CanonicalMinimumSize;").AppendLine();
                code.AppendLine("public int CanonicalRecordMaximumTextReferences => CanonicalMaximumTextReferences;");
                code.AppendLine("public int MaximumCanonicalEncodedSize(int maximumTextCharacters) => CanonicalMaximumEncodedSize(maximumTextCharacters);");
                code.Append("public void WriteCanonical<TWriter>(TWriter writer) where TWriter : class, ").Append(Runtime)
                    .AppendLine("ICanonicalValueWriter => CanonicalWrite(writer, in this);");
                code.Append("public ").Append(fullType).Append(" ReadCanonical<TReader>(TReader reader) where TReader : class, ")
                    .Append(Runtime).AppendLine("ICanonicalValueReader => CanonicalRead(reader);");
                code.Append("public void SkipCanonical<TReader>(TReader reader) where TReader : class, ").Append(Runtime)
                    .AppendLine("ICanonicalValueReader => CanonicalSkip(reader);");
                code.AppendLine("public int MeasureCanonical() => CanonicalMeasure(in this);");
                code.AppendLine("public void RetainCanonical() => CanonicalRetain(in this);");
                code.AppendLine("public void ReleaseCanonical() => CanonicalRelease(in this);");
                code.Append("public void VisitCanonicalTexts<TVisitor>(TVisitor visitor) where TVisitor : class, ").Append(Runtime)
                    .AppendLine("ICanonicalTextVisitor => CanonicalVisitTexts(visitor, in this);");
            }
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
                code.Append("this.").Append(Escape(member.Symbol.Name)).Append(" = ").Append(Normalize(member, read)).AppendLine(";");
            }
            foreach (IMethodSymbol validation in plan.Validations)
                code.Append(Escape(validation.Name)).AppendLine("();");
            code.AppendLine("}");
            code.Append("public static ").Append(type)
                .Append(" CanonicalReadMaterialized<TLayoutReader, TPayloadReader>(TLayoutReader layoutReader, TPayloadReader payloadReader) ")
                .Append("where TLayoutReader : class, ").Append(Runtime).Append("ICanonicalValueReader ")
                .Append("where TPayloadReader : class, ").Append(Runtime).Append("ICanonicalValueReader => new ")
                .Append(type).Append("(").Append(Runtime).AppendLine("CanonicalConstruction.Encoded, layoutReader, payloadReader);");
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
            if (MapTypes(type) is ITypeSymbol[] map)
            {
                string index = "__canonicalIndex" + depth;
                code.Append("writer.WriteInt32(").Append(value).AppendLine(".Count);");
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(value).Append(".Count; ").Append(index).AppendLine("++) {");
                Write(code, map[0], value + ".Keys[" + index + "]", depth + 1);
                Write(code, map[1], value + ".Values[" + index + "]", depth + 1);
                code.AppendLine("}");
                return;
            }
            if (ReferenceKey(type) is Member reference)
            {
                Write(code, reference.Type, value + "." + Escape(reference.Symbol.Name), depth);
                return;
            }
            if (NullableElement(type) is ITypeSymbol nullable)
            {
                code.Append("writer.WriteBoolean(").Append(value).AppendLine(".HasValue);");
                code.Append("if (").Append(value).AppendLine(".HasValue) {");
                Write(code, nullable, value + ".Value", depth);
                code.AppendLine("}");
                return;
            }
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
            if (type.TypeKind == TypeKind.TypeParameter)
            {
                code.Append(value).AppendLine(".WriteCanonical(writer);");
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

        static void WriteMember(StringBuilder code, Member member, string value)
        {
            if (HasAttribute(member.Symbol, SetAttribute))
                WriteSet(code, member.Type, value);
            else if (HasAttribute(member.Symbol, RequiredAttribute))
                code.Append(TypeName(member.Type)).Append(".CanonicalWriteRequired(writer, ").Append(value).AppendLine(");");
            else
                Write(code, member.Type, value);
        }

        static void WriteSet(StringBuilder code, ITypeSymbol type, string value)
        {
            ITypeSymbol element = SequenceElement(type);
            code.AppendLine("{");
            code.Append("writer.WriteInt32(").Append(SequenceCount(type, value)).AppendLine(");");
            code.AppendLine("int previousIndex = -1;");
            code.Append("for (int i = 0; i < ").Append(SequenceCount(type, value)).AppendLine("; i++) {");
            code.AppendLine("int selectedIndex = -1;");
            code.Append("for (int j = 0; j < ").Append(SequenceCount(type, value)).AppendLine("; j++) {");
            code.Append("if (previousIndex >= 0 && ").Append(Compare(element,
                SequenceValue(type, value, "j"), SequenceValue(type, value, "previousIndex"))).AppendLine(" <= 0) continue;");
            code.Append("if (selectedIndex < 0 || ").Append(Compare(element,
                SequenceValue(type, value, "j"), SequenceValue(type, value, "selectedIndex"))).AppendLine(" < 0) selectedIndex = j;");
            code.AppendLine("}");
            Write(code, element, SequenceValue(type, value, "selectedIndex"));
            code.AppendLine("previousIndex = selectedIndex; } }");
        }

        static string ReadMember(Member member, Plan plan)
        {
            if (HasAttribute(member.Symbol, PublishedAttribute))
                return TypeName(member.Type) + ".CanonicalReadPublished(reader, reader.PublishedValues.Require<" +
                    TypeName(member.Type) + ">())";
            if (HasAttribute(member.Symbol, RequiredAttribute))
                return TypeName(member.Type) + ".CanonicalReadRequired(reader" +
                    (PreparedStorage(member.Type) == null ? "" : ", storage") + ")";
            if (CaseSemantic(member, "NormalizeZero") || CaseSemantic(member, "NormalizeNull"))
                return "__CanonicalRead_" + member.Symbol.Name + "(reader)";
            if (BorrowedStorage(member.Type) != null)
            {
                return (HasAttribute(member.Type, ContractAttribute) ? TypeName(member.Type) + ".CanonicalRead" : SequenceReader(member.Type)) +
                    "(reader, " + (SymbolEqualityComparer.Default.Equals(plan.PreparedStorage, BorrowedStorage(member.Type))
                    ? "storage" : (OwnsSequenceStorage(member.Type) ? "value." : "storage.") + Escape(member.Symbol.Name)) + ")";
            }
            if (member.Type.SpecialType == SpecialType.System_String && HasAttribute(member.Symbol, DeclaredTextAttribute) && plan.PreparedRead)
                return "reader.ReadDeclaredString()";
            if (plan.PreparedRead && PreparedStorage(member.Type) != null)
                return TypeName(member.Type) + ".CanonicalRead(reader, storage)";
            return Read(member.Type, !plan.PreparedRead);
        }

        static bool SupportsBoundValue(ITypeSymbol type) => ReferenceKey(type) != null ? true : NullableElement(type) is ITypeSymbol nullable
            ? SupportsBoundValue(nullable)
            : Primitive(type) != null || Identity(type) == TextType || type.TypeKind == TypeKind.TypeParameter ||
                type is INamedTypeSymbol record && record.TypeKind == TypeKind.Struct && record.IsReadOnly &&
                HasAttribute(record, ContractAttribute) && UsesPreparedRead(record) && PreparedStorage(record) == null;

        static string Normalize(Member member, string value)
        {
            if (HasAttribute(member.Symbol, TextRulesAttribute))
                return Runtime + "CanonicalTextNormalization.Apply(" + value + ", " +
                    (TextRule(member, "Required") ? "true" : "false") + ", " +
                    (TextRule(member, "Trim") ? "true" : "false") + ", " +
                    (TextRule(member, "NullToEmpty") ? "true" : "false") + ", construction.IsEncoded, " +
                    SymbolDisplay.FormatLiteral(member.Symbol.Name, true) + ")";
            if (CaseSemantic(member, "NormalizeZero"))
                return value + " == 0 ? 0 : " + value;
            if (CaseSemantic(member, "NormalizeNull"))
                return value + ".IsNull ? " + Runtime + "CanonicalTextValue.Empty : " + value;
            return value;
        }

        static bool TextRule(Member member, string name) => member.Symbol.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == TextRulesAttribute).SelectMany(a => a.NamedArguments)
            .Any(a => a.Key == name && a.Value.Value is true);

        static bool CaseSemantic(Member member, string name) => member.Symbol.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == UnionCaseAttribute).SelectMany(a => a.NamedArguments)
            .Any(a => a.Key == name && a.Value.Value is true);

        static string CaseDefaultMember(Member member) => member.Symbol.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == UnionCaseAttribute).SelectMany(a => a.NamedArguments)
            .Where(a => a.Key == "DefaultMember").Select(a => (string)a.Value.Value).SingleOrDefault();

        static string Read(ITypeSymbol type, bool materialized = false)
        {
            if (ReferenceKey(type) != null)
                return "reader.PublishedValues.RequireReference<" + TypeName(type) + ">(reader.ReadInt32())";
            if (type.TypeKind == TypeKind.TypeParameter)
                return "default(" + TypeName(type) + ").ReadCanonical(reader)";
            if (NullableElement(type) != null)
                return SequenceReader(type) + "(reader)";
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
            if (MapTypes(type) is ITypeSymbol[] map)
            {
                string count = "__canonicalCount" + depth, index = "__canonicalIndex" + depth;
                code.Append("{ int ").Append(count).AppendLine(" = reader.ReadInt32();");
                code.Append("if (").Append(count).AppendLine(" < 0) throw new global::System.IO.InvalidDataException(\"Canonical collection count is invalid.\");");
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(count)
                    .Append("; ").Append(index).AppendLine("++) {");
                Skip(code, map[0], depth + 1);
                Skip(code, map[1], depth + 1);
                code.AppendLine("} }");
                return;
            }
            if (ReferenceKey(type) is Member reference)
            {
                Skip(code, reference.Type, depth);
                return;
            }
            if (NullableElement(type) is ITypeSymbol nullable)
            {
                code.AppendLine("if (reader.ReadBoolean()) {");
                Skip(code, nullable, depth);
                code.AppendLine("}");
                return;
            }
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
            if (type.TypeKind == TypeKind.TypeParameter)
                code.Append("default(").Append(TypeName(type)).AppendLine(").SkipCanonical(reader);");
            else if (type.TypeKind == TypeKind.Enum)
                code.Append("_ = ").Append(Read(type)).AppendLine(";");
            else if (Primitive(type) != null)
                code.Append("reader.Read").Append(Primitive(type)).AppendLine("();");
            else if (Identity(type) == TextType || type.SpecialType == SpecialType.System_String)
                code.AppendLine("reader.SkipText();");
            else
                code.Append(TypeName(type)).AppendLine(".CanonicalSkip(reader);");
        }

        static void MeasureCapacity(StringBuilder code, Plan plan, ITypeSymbol type, string value, int depth = 0)
        {
            if (MapTypes(type) is ITypeSymbol[] map)
            {
                if (map.All(item => FiniteSize(plan, item)))
                    code.Append("length = checked(length + 4 + ").Append(value).Append(".Capacity * (")
                        .Append(MaximumSize(map[0])).Append(" + ").Append(MaximumSize(map[1])).AppendLine("));");
                else
                {
                    string index = "__capacityIndex" + depth;
                    code.AppendLine("length = checked(length + 4);");
                    code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(value)
                        .Append(".Count; ").Append(index).AppendLine("++) {");
                    MeasureCapacity(code, plan, map[0], value + ".Keys[" + index + "]", depth + 1);
                    MeasureCapacity(code, plan, map[1], value + ".Values[" + index + "]", depth + 1);
                    code.AppendLine("}");
                }
            }
            else if (ReferenceKey(type) is Member reference)
                MeasureCapacity(code, plan, reference.Type, value + "." + Escape(reference.Symbol.Name), depth);
            else if (SequenceElement(type) is ITypeSymbol element)
            {
                string index = "__capacityIndex" + depth;
                if (FiniteSize(plan, element))
                {
                    code.Append("length = checked(length + 4 + ").Append(OwnsSequenceStorage(type) || IsPreparedCollection(type) ? value + ".Capacity" : SequenceCount(type, value))
                        .Append(" * (").Append(MaximumSize(element)).AppendLine("));");
                    return;
                }
                code.AppendLine("length = checked(length + 4);");
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(SequenceCount(type, value)).Append("; ").Append(index).AppendLine("++) {");
                MeasureCapacity(code, plan, element, SequenceValue(type, value, index), depth + 1);
                code.AppendLine("}");
            }
            else if (NullableElement(type) is ITypeSymbol nullable)
            {
                code.AppendLine("length = checked(length + 1);");
                code.Append("if (").Append(value).AppendLine(".HasValue) {");
                MeasureCapacity(code, plan, nullable, value + ".Value", depth);
                code.AppendLine("}");
            }
            else
                code.Append("length = checked(length + ").Append(
                    Primitive(type) != null || Identity(type) == TextType || type.SpecialType == SpecialType.System_String ||
                    type.TypeKind == TypeKind.TypeParameter ? MaximumSize(type) :
                    TypeName(type) + ".CanonicalMeasureCapacity(" + value + ", maximumTextCharacters)").AppendLine(");");
        }

        static bool FiniteSize(Plan plan, ITypeSymbol type) => ReferenceKey(type) is Member reference
            ? FiniteSize(plan, reference.Type)
            : NullableElement(type) is ITypeSymbol nullable
            ? FiniteSize(plan, nullable)
            : Primitive(type) != null || Identity(type) == TextType || type.SpecialType == SpecialType.System_String ||
                type.TypeKind == TypeKind.TypeParameter || plan.FiniteTypes.Contains(Identity(type));

        static string MaximumSize(Plan plan)
        {
            string common = string.Join(" + ", plan.Members.Where(m => !m.IsCase).Select(m => MaximumSize(m.Type)));
            if (common.Length == 0) common = "0";
            if (plan.Cases.Length != 0)
            {
                string maximum = MaximumSize(plan.Cases[0].Type);
                foreach (Member member in plan.Cases.Skip(1))
                    maximum = "global::System.Math.Max(" + maximum + ", " + MaximumSize(member.Type) + ")";
                common += " + 4 + " + maximum;
            }
            return (plan.Presence != null || plan.PreparedObject ? "1 + " : "") + common;
        }

        static string MaximumSize(ITypeSymbol type) => ReferenceKey(type) is Member reference
            ? MaximumSize(reference.Type)
            : NullableElement(type) is ITypeSymbol nullable
            ? "1 + " + MaximumSize(nullable)
            : Primitive(type) != null ? Measure(type, "")
            : type.SpecialType == SpecialType.System_String || Identity(type) == TextType
                ? "(4 + maximumTextCharacters * 2)"
                : type.TypeKind == TypeKind.TypeParameter ? "default(" + TypeName(type) + ").MaximumCanonicalEncodedSize(maximumTextCharacters)"
                : TypeName(type) + ".CanonicalMaximumEncodedSize(maximumTextCharacters)";

        static string Measure(ITypeSymbol type, string value)
        {
            if (ReferenceKey(type) is Member reference)
                return Measure(reference.Type, value + "." + Escape(reference.Symbol.Name));
            if (NullableElement(type) is ITypeSymbol nullable)
                return "(1 + (" + value + ".HasValue ? " + Measure(nullable, value + ".Value") + " : 0))";
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
            return type.TypeKind == TypeKind.TypeParameter ? value + ".MeasureCanonical()" :
                type.SpecialType == SpecialType.System_String ? "(4 + (" + value + " == null ? 0 : checked(" + value + ".Length * 2)))" :
                Identity(type) == TextType ? "(4 + (" + value + ".IsNull ? 0 : checked(" + value + ".Length * 2)))" :
                TypeName(type) + ".CanonicalMeasure(" + value + ")";
        }

        static bool HasResources(Plan plan, ITypeSymbol type) => MapTypes(type) is ITypeSymbol[] map ?
            map.Any(item => HasResources(plan, item)) : BorrowedObjectStorage(type) == null && (type.TypeKind == TypeKind.TypeParameter || Identity(type) == TextType ||
            BorrowedStorage(type) != null && BorrowedSemantic(type, "HoldStorage", true) ||
            (NullableElement(type) is ITypeSymbol nullable ? HasResources(plan, nullable) :
                SequenceElement(type) is ITypeSymbol element ? HasResources(plan, element) : plan.ResourceTypes.Contains(Identity(type))));

        static bool HasTexts(Plan plan, ITypeSymbol type) => MapTypes(type) is ITypeSymbol[] map ?
            map.Any(item => HasTexts(plan, item)) : type.TypeKind == TypeKind.TypeParameter || Identity(type) == TextType || type.SpecialType == SpecialType.System_String ||
            (NullableElement(type) is ITypeSymbol nullable ? HasTexts(plan, nullable) :
                SequenceElement(type) is ITypeSymbol element ? HasTexts(plan, element) : plan.TextTypes.Contains(Identity(type)));

        static void VisitTexts(StringBuilder code, ITypeSymbol type, string value, Plan plan, int depth = 0)
        {
            if (!HasTexts(plan, type))
                return;
            if (MapTypes(type) is ITypeSymbol[] map)
            {
                string index = "__canonicalIndex" + depth;
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(value)
                    .Append(".Count; ").Append(index).AppendLine("++) {");
                VisitTexts(code, map[0], value + ".Keys[" + index + "]", plan, depth + 1);
                VisitTexts(code, map[1], value + ".Values[" + index + "]", plan, depth + 1);
                code.AppendLine("}");
                return;
            }
            if (NullableElement(type) is ITypeSymbol nullable)
            {
                code.Append("if (").Append(value).AppendLine(".HasValue) {");
                VisitTexts(code, nullable, value + ".Value", plan, depth);
                code.AppendLine("}");
                return;
            }
            ITypeSymbol element = SequenceElement(type);
            if (element != null)
            {
                string index = "__canonicalIndex" + depth;
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(SequenceCount(type, value)).Append("; ").Append(index).AppendLine("++) {");
                VisitTexts(code, element, SequenceValue(type, value, index), plan, depth + 1);
                code.AppendLine("}");
            }
            else if (type.TypeKind == TypeKind.TypeParameter)
                code.Append(value).AppendLine(".VisitCanonicalTexts(visitor);");
            else if (Identity(type) == TextType || type.SpecialType == SpecialType.System_String)
                code.Append("visitor.Visit(").Append(value).AppendLine(");");
            else
                code.Append(TypeName(type)).Append(".CanonicalVisitTexts(visitor, ").Append(value).AppendLine(");");
        }

        static void Hold(StringBuilder code, string operation, ITypeSymbol type, string value, Plan plan, int depth = 0)
        {
            if (!HasResources(plan, type))
                return;
            if (MapTypes(type) is ITypeSymbol[] map)
            {
                string index = "__canonicalIndex" + depth;
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(value)
                    .Append(".Count; ").Append(index).AppendLine("++) {");
                Hold(code, operation, map[0], value + ".Keys[" + index + "]", plan, depth + 1);
                Hold(code, operation, map[1], value + ".Values[" + index + "]", plan, depth + 1);
                code.AppendLine("}");
                return;
            }
            if (NullableElement(type) is ITypeSymbol nullable)
            {
                code.Append("if (").Append(value).AppendLine(".HasValue) {");
                Hold(code, operation, nullable, value + ".Value", plan, depth);
                code.AppendLine("}");
                return;
            }
            if (BorrowedStorage(type) != null && BorrowedSemantic(type, "HoldStorage", true))
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
            if (type.TypeKind == TypeKind.TypeParameter)
                code.Append(value).Append('.').Append(operation).AppendLine("Canonical();");
            else if (Identity(type) == TextType)
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

        static string DefaultValue(ITypeSymbol type)
        {
            ISymbol value = type.GetMembers().SingleOrDefault(member => member.IsStatic && HasAttribute(member, DefaultValueAttribute));
            return value == null ? "default" : TypeName(type) + "." + Escape(value.Name);
        }

        static ITypeSymbol[] MapTypes(ITypeSymbol type) => type is INamedTypeSymbol named &&
            Identity(named.OriginalDefinition) == "System.Collections.Generic.SortedList<TKey, TValue>"
                ? named.TypeArguments.ToArray() : null;

        static bool IsPreparedCollection(ITypeSymbol type) => MapTypes(type) != null ||
            type is INamedTypeSymbol named && Identity(named.OriginalDefinition) == "System.Collections.Generic.List<T>";

        static bool SupportsComparison(ITypeSymbol type) => type.SpecialType == SpecialType.System_String ||
            Primitive(type) != null || type.AllInterfaces.Any(item =>
                Identity(item.OriginalDefinition) == "System.IComparable<T>");

        static string Compare(ITypeSymbol type, string left, string right) => type.SpecialType == SpecialType.System_String
            ? "global::System.String.CompareOrdinal(" + left + ", " + right + ")"
            : "global::System.Collections.Generic.Comparer<" + TypeName(type) + ">.Default.Compare(" + left + ", " + right + ")";

        static string ReadCollectionValue(ITypeSymbol type, Member member, Plan plan) =>
            type.SpecialType == SpecialType.System_String && HasAttribute(member.Symbol, DeclaredTextAttribute)
                ? "reader.ReadDeclaredString()" : Read(type, !plan.PreparedRead);

        static void ReadPreparedCollection(StringBuilder code, Member member, string target, Plan plan)
        {
            ITypeSymbol[] map = MapTypes(member.Type);
            ITypeSymbol item = map == null ? SequenceElement(member.Type) : map[1];
            if (plan.OwnsMemberResources) Hold(code, "Release", member.Type, target, plan);
            code.Append(target).AppendLine(".Clear();");
            code.AppendLine("{ int count = reader.ReadInt32();");
            code.Append("if (count < 0 || count > ").Append(target)
                .AppendLine(".Capacity) throw new global::System.IO.InvalidDataException(\"Canonical collection exceeds prepared capacity.\");");
            code.AppendLine("for (int i = 0; i < count; i++) {");
            if (map != null)
            {
                code.Append("var key = ").Append(ReadCollectionValue(map[0], member, plan)).AppendLine(";");
                code.Append("if (i > 0 && ").Append(Compare(map[0], target + ".Keys[i - 1]", "key"))
                    .AppendLine(" >= 0) throw new global::System.IO.InvalidDataException(\"Canonical map keys are not strictly ordered.\");");
            }
            code.Append("var item = ").Append(ReadCollectionValue(item, member, plan)).AppendLine(";");
            if (map == null && HasAttribute(member.Symbol, SetAttribute))
                code.Append("if (i > 0 && ").Append(Compare(item, target + "[i - 1]", "item"))
                    .AppendLine(" >= 0) throw new global::System.IO.InvalidDataException(\"Canonical set elements are not strictly ordered.\");");
            if (plan.OwnsMemberResources)
            {
                if (map != null) Hold(code, "Retain", map[0], "key", plan);
                Hold(code, "Retain", item, "item", plan);
            }
            code.Append(target).Append(map == null ? ".Add(item);" : ".Add(key, item);").AppendLine();
            code.AppendLine("} }");
        }

        static void CopyPreparedCollection(StringBuilder code, ITypeSymbol type, string source, string target, Plan plan)
        {
            Hold(code, "Release", type, target, plan);
            code.Append(target).AppendLine(".Clear();");
            ITypeSymbol[] map = MapTypes(type);
            code.Append("for (int i = 0; i < ").Append(source).AppendLine(".Count; i++) {");
            if (map != null)
            {
                Hold(code, "Retain", map[0], source + ".Keys[i]", plan);
                Hold(code, "Retain", map[1], source + ".Values[i]", plan);
                code.Append(target).Append(".Add(").Append(source).Append(".Keys[i], ").Append(source).AppendLine(".Values[i]);");
            }
            else
            {
                Hold(code, "Retain", SequenceElement(type), source + "[i]", plan);
                code.Append(target).Append(".Add(").Append(source).AppendLine("[i]);");
            }
            code.AppendLine("}");
        }

        static ITypeSymbol NullableElement(ITypeSymbol type) => type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? named.TypeArguments[0] : null;

        static ITypeSymbol BufferElement(ITypeSymbol type) => type is INamedTypeSymbol named &&
            Identity(named.OriginalDefinition) == BufferType ? named.TypeArguments[0] : null;

        static ITypeSymbol SequenceElement(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array && array.Rank == 1)
                return array.ElementType;
            if (type is INamedTypeSymbol named)
            {
                if (HasAttribute(named.OriginalDefinition, BorrowedSequenceAttribute))
                    return named.IsGenericType ? named.TypeArguments[0] :
                        ((INamedTypeSymbol)named.GetMembers("Span").OfType<IPropertySymbol>().Single().Type).TypeArguments[0];
                switch (Identity(named.OriginalDefinition))
                {
                    case BufferType:
                    case "System.ReadOnlyMemory<T>":
                    case "System.Collections.Generic.List<T>":
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
            if (shape == null)
                return null;
            INamedTypeSymbol storage = (INamedTypeSymbol)shape.ConstructorArguments[0].Value;
            return storage.IsGenericType ? storage.OriginalDefinition.Construct(SequenceElement(type)) : storage;
        }

        static bool OwnsSequenceStorage(ITypeSymbol type) => type.TypeKind == TypeKind.Class &&
            SymbolEqualityComparer.Default.Equals(type, BorrowedStorage(type));

        static bool BorrowedSemantic(ITypeSymbol type, string name, bool defaultValue)
        {
            AttributeData shape = ((INamedTypeSymbol)type).OriginalDefinition.GetAttributes()
                .Single(a => Identity(a.AttributeClass) == BorrowedSequenceAttribute);
            return shape.NamedArguments.Where(a => a.Key == name).Select(a => (bool)a.Value.Value)
                .DefaultIfEmpty(defaultValue).Single();
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

        static IEnumerable<ITypeSymbol> NullableTypes(IEnumerable<ITypeSymbol> types)
        {
            var values = new SortedDictionary<string, ITypeSymbol>(StringComparer.Ordinal);
            foreach (ITypeSymbol type in types)
                Add(type);
            return values.Values;

            void Add(ITypeSymbol type)
            {
                if (NullableElement(type) != null)
                    values[Identity(type)] = type;
                if (SequenceElement(type) is ITypeSymbol element)
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
            Primitive(type) != null ? Measure(type, "") : type.TypeKind == TypeKind.TypeParameter ?
            "default(" + TypeName(type) + ").CanonicalRecordMinimumSize" : TypeName(type) + ".CanonicalMinimumSize";

        static void MeasureMember(StringBuilder code, ITypeSymbol type, string value, int depth = 0)
        {
            if (MapTypes(type) is ITypeSymbol[] map)
            {
                string index = "__canonicalIndex" + depth;
                code.AppendLine("length = checked(length + 4);");
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(value)
                    .Append(".Count; ").Append(index).AppendLine("++) {");
                MeasureMember(code, map[0], value + ".Keys[" + index + "]", depth + 1);
                MeasureMember(code, map[1], value + ".Values[" + index + "]", depth + 1);
                code.AppendLine("}");
                return;
            }
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
        static bool HasPublishedRead(ITypeSymbol type) => type.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == ContractAttribute)
            .SelectMany(a => a.NamedArguments).Any(a => a.Key == "PublishedRead" && a.Value.Value is true);
        static bool HasMaterializedRead(ITypeSymbol type) => type.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == ContractAttribute)
            .SelectMany(a => a.NamedArguments).Any(a => a.Key == "MaterializedRead" && a.Value.Value is true);
        static INamedTypeSymbol BorrowedObjectStorage(ITypeSymbol type) => (INamedTypeSymbol)type.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == BorrowedObjectAttribute)
            .Select(a => a.ConstructorArguments[0].Value).SingleOrDefault();

        static INamedTypeSymbol PreparedStorage(ITypeSymbol type) => BorrowedObjectStorage(type) ?? (INamedTypeSymbol)type.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == ContractAttribute)
            .SelectMany(a => a.NamedArguments).Where(a => a.Key == "PreparedStorage")
            .Select(a => a.Value.Value).SingleOrDefault();

        sealed class Receiver : ISyntaxReceiver
        {
            public readonly List<BaseTypeDeclarationSyntax> Types = new List<BaseTypeDeclarationSyntax>();
            public void OnVisitSyntaxNode(SyntaxNode node)
            {
                if (node is BaseTypeDeclarationSyntax type && type.AttributeLists.Count > 0)
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
            public bool PublishedRead;
            public bool PreparedObject;
            public bool SequenceRoot;
            public bool OwnsMemberResources;
            public string TextCapacity;
            public bool FiniteSize;
            public INamedTypeSymbol PreparedStorage;
            public IPropertySymbol Presence;
            public bool HashValue;
            public bool HasResources;
            public HashSet<string> ResourceTypes;
            public HashSet<string> FiniteTypes;
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
            readonly HashSet<string> m_FiniteTypes = new HashSet<string>(StringComparer.Ordinal);
            readonly HashSet<string> m_TextTypes = new HashSet<string>(StringComparer.Ordinal);
            readonly HashSet<string> m_PayloadTypes = new HashSet<string>(StringComparer.Ordinal);
            public IEnumerable<Plan> Plans => m_Plans.Values;
            readonly Compilation m_Compilation;
            public Planner(GeneratorExecutionContext context, Compilation compilation)
            {
                m_Context = context;
                m_Compilation = compilation;
            }

            public Plan StoragePlan(INamedTypeSymbol type)
            {
                if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, m_Compilation.Assembly))
                    return Plan(type);
                var plan = new Plan { Type = type, Members = StorageMembers(type), ResourceTypes = m_ResourceTypes,
                    OwnsMemberResources = type.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == ContractAttribute)
                        .NamedArguments.Any(argument => argument.Key == "OwnsMemberResources" && argument.Value.Value is true) };
                foreach (Member member in plan.Members)
                    Shape(plan, member.Type, member.Symbol);
                return plan;
            }

            public Plan Plan(INamedTypeSymbol type)
            {
                if (m_Plans.TryGetValue(Identity(type), out Plan existing))
                {
                    if (!existing.Complete)
                        Error(existing, type, "正式记录不能递归引用自身；页引用必须由所属存储显式承载");
                    return existing;
                }
                var plan = new Plan { Type = type, ResourceTypes = m_ResourceTypes, FiniteTypes = m_FiniteTypes, TextTypes = m_TextTypes, PayloadTypes = m_PayloadTypes, PreparedRead = UsesPreparedRead(type),
                    HashValue = type.AllInterfaces.Any(i => Identity(i) == HashValueType),
                    PublishedRead = type.GetAttributes().Single(a => Identity(a.AttributeClass) == ContractAttribute)
                        .NamedArguments.Any(a => a.Key == "PublishedRead" && a.Value.Value is true),
                    MaterializedRead = type.GetAttributes().Single(a => Identity(a.AttributeClass) == ContractAttribute)
                        .NamedArguments.Any(a => a.Key == "MaterializedRead" && a.Value.Value is true) };
                plan.PreparedStorage = (INamedTypeSymbol)type.GetAttributes()
                    .Single(a => Identity(a.AttributeClass) == ContractAttribute).NamedArguments
                    .Where(a => a.Key == "PreparedStorage").Select(a => a.Value.Value).SingleOrDefault();
                plan.PreparedObject = BorrowedObjectStorage(type) != null;
                plan.SequenceRoot = BorrowedStorage(type) != null;
                if (plan.SequenceRoot)
                {
                    plan.PreparedStorage = BorrowedStorage(type);
                    if (!plan.PreparedRead || plan.MaterializedRead)
                        Error(plan, type, "借用序列直接使用原Bank读取，不能初始化物化");
                }
                if (plan.PreparedObject)
                {
                    plan.PreparedStorage = BorrowedObjectStorage(type);
                    if (type.TypeKind != TypeKind.Class || !plan.PreparedRead || plan.MaterializedRead)
                        Error(plan, type, "借用对象必须直接恢复原预分配 class，不能物化或构造替代对象");
                }
                plan.OwnsMemberResources = plan.PreparedObject || plan.SequenceRoot || type.GetAttributes()
                    .Single(a => Identity(a.AttributeClass) == ContractAttribute).NamedArguments
                    .Any(a => a.Key == "OwnsMemberResources" && a.Value.Value is true);
                if (plan.OwnsMemberResources && (type.IsReadOnly || !plan.PreparedRead))
                    Error(plan, type, "成员资源所有权只用于直接恢复的原可变候选对象");
                m_Plans.Add(Identity(type), plan);
                if (!plan.PreparedRead && !plan.MaterializedRead)
                    Error(plan, type, "初始化合同必须显式声明物化读取，不能缺少正式读取入口");
                if (type.ContainingType != null || type.IsAbstract ||
                    type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object && type.TypeKind == TypeKind.Class)
                    Error(plan, type, "正式记录必须是独立类型，运行设施和继承服务需留在原 owner");
                if (type.Arity != 0 && (type.TypeKind != TypeKind.Struct ||
                    !plan.PreparedRead || plan.PreparedStorage != null))
                    Error(plan, type, "泛型记录必须由实时值持有，服务设施保留在原 owner");
                foreach (SyntaxReference syntax in type.DeclaringSyntaxReferences)
                    if (syntax.GetSyntax() is TypeDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                        Error(plan, type, "类型必须声明为 partial，生成访问进入原正式类型");
                if (plan.SequenceRoot)
                {
                    plan.Members = Array.Empty<Member>();
                    plan.Cases = Array.Empty<Member>();
                    plan.Validations = Array.Empty<IMethodSymbol>();
                    plan.Descriptor = "canonical-sequence/1|" + Identity(type) + "|" + Shape(plan, type, type);
                    plan.MinimumSize = 4;
                    plan.HasResources = HasResources(plan, type);
                    plan.HasTexts = HasTexts(plan, type);
                    using (var sha = SHA256.Create())
                        plan.Schema = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(plan.Descriptor))
                            .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
                    plan.Complete = true;
                    return plan;
                }
                plan.Members = StorageMembers(type);
                AttributeData union = type.GetAttributes().SingleOrDefault(a => Identity(a.AttributeClass) == UnionAttribute);
                if (union != null)
                {
                    plan.UnionDefinition = (INamedTypeSymbol)union.ConstructorArguments[0].Value;
                    if (!HasAttribute(plan.UnionDefinition, UnionTemplateAttribute) || plan.UnionDefinition.IsUnboundGenericType)
                        Error(plan, type, "联合必须引用已声明模板的闭合正式 C# 类型");
                    Member[] generated = UnionMembers(plan.UnionDefinition)
                        .Where(m => Identity(m.Type) != UnavailableType).ToArray();
                    foreach (Member member in generated)
                        member.Generated = true;
                    foreach (Member member in plan.Members)
                        if (generated.Any(m => m.Symbol.Name == member.Symbol.Name) || member.IsCase)
                            Error(plan, member.Symbol, "联合字段只在正式模板声明，不重复声明分支或成员");
                    plan.Members = plan.Members.Concat(generated).OrderBy(m => m.Symbol.Name, StringComparer.Ordinal).ToArray();
                }
                plan.Cases = Cases(plan.Members);
                IPropertySymbol[] presence = type.GetMembers().OfType<IPropertySymbol>()
                    .Where(p => HasAttribute(p, PresenceAttribute)).ToArray();
                if (presence.Length > 1)
                    Error(plan, type, "可选记录只能声明一个存在条件");
                plan.Presence = presence.FirstOrDefault();
                if (plan.Presence != null && (plan.Presence.IsStatic || plan.Presence.IsIndexer ||
                    plan.Presence.Type.SpecialType != SpecialType.System_Boolean ||
                    plan.Presence.GetMethod == null || IsAutoProperty(plan.Presence) ||
                    !(type.TypeKind == TypeKind.Struct && type.IsReadOnly || type.TypeKind == TypeKind.Class && plan.OwnsMemberResources) ||
                    !plan.PreparedRead || plan.Cases.Length != 0))
                    Error(plan, plan.Presence, "存在条件必须是实时 readonly 记录或原可变资源 owner 的计算 bool 属性，不重复声明存储字段");
                if (plan.HashValue && (type.TypeKind != TypeKind.Struct || !type.IsReadOnly ||
                    plan.Cases.Length != 0 || plan.Members.Length != 1 || Identity(plan.Members[0].Type) != HashType))
                    Error(plan, type, "正式 Hash 包装必须由 readonly struct 的唯一 StableHash 存储成员持有");
                INamedTypeSymbol kindOwner = plan.UnionDefinition ?? type;
                plan.KindType = kindOwner.TypeKind == TypeKind.Enum ? TypeName(kindOwner) :
                    "global::" + kindOwner.ContainingNamespace.ToDisplayString() + "." + Escape(kindOwner.Name) + "Kind";
                plan.Validations = type.GetMembers().OfType<IMethodSymbol>()
                    .Where(m => HasAttribute(m, ValidationAttribute)).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
                foreach (IMethodSymbol validation in plan.Validations)
                    if (validation.IsStatic || !validation.ReturnsVoid || validation.Parameters.Length != 0 &&
                        !(plan.PreparedRead && !plan.MaterializedRead && !type.IsReadOnly && validation.Parameters.Length == 1 &&
                            Identity(validation.Parameters[0].Type) == "ThirdPersonSimulation.ICanonicalValueReader"))
                        Error(plan, validation, "正式数据规则由实例void提供；原可变候选接收可以直接消费同一Reader");
                if (plan.Cases.Length > 0 && type.TypeKind == TypeKind.Class && plan.PreparedRead)
                    Error(plan, type, "类联合只允许初始化物化读取；实时联合必须使用值或原预分配页");
                if (plan.Members.Any(m => m.Payload) && (plan.PreparedRead || plan.Cases.Length > 0))
                    Error(plan, type, "参数成员分块只用于初始化组合记录，联合激活载荷由正式分支声明确定");
                var descriptor = new StringBuilder("canonical-csharp/1|le|utf16-le/2|").Append(Identity(type));
                if (plan.PreparedObject) descriptor.Append("|borrowed-object");
                if (plan.Presence != null)
                    descriptor.Append("|presence:").Append(Identity(plan.Presence));
                if (plan.UnionDefinition != null)
                    descriptor.Append("|union:").Append(Identity(plan.UnionDefinition));
                Member[] declaredCases = Cases(UnionMembers(plan.UnionDefinition ?? type));
                foreach (Member member in plan.Cases)
                    descriptor.Append("|case:").Append(plan.UnionDefinition?.TypeKind == TypeKind.Enum ?
                        Convert.ToInt32(((IFieldSymbol)member.Symbol).ConstantValue, CultureInfo.InvariantCulture) :
                        Array.FindIndex(declaredCases, m => m.Symbol.Name == member.Symbol.Name) + 1)
                        .Append('=').Append(MemberIdentity(plan, member));
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
                    if (HasAttribute(member.Symbol, SetAttribute)) descriptor.Append("|unordered-set");
                    descriptor.Append('|').Append(member.IsCase ? "case:" : member.Payload ? "payload:" : "member:").Append(MemberIdentity(plan, member))
                        .Append(':').Append(Shape(plan, member.Type, member.Symbol));
                    if (HasAttribute(member.Symbol, TextRulesAttribute))
                    {
                        descriptor.Append(":text-rules:").Append(TextRule(member, "Required")).Append(':')
                            .Append(TextRule(member, "Trim")).Append(':').Append(TextRule(member, "NullToEmpty"));
                        if ((!type.IsReadOnly && !(type.TypeKind == TypeKind.Class && plan.MaterializedRead && !plan.PreparedRead)) ||
                            Identity(member.Type) != TextType && member.Type.SpecialType != SpecialType.System_String)
                            Error(plan, member.Symbol, "文本规范化声明在readonly记录或初始化class的真实文本成员");
                    }
                    if (HasAttribute(member.Symbol, BoundAttribute) && (!plan.PreparedRead && !type.IsReadOnly ||
                        plan.PreparedStorage != null || plan.Presence != null || plan.Cases.Length != 0))
                        Error(plan, member.Symbol, "已绑定成员直接匹配原数据，不允许联合、可选或外部存储构造");
                    if (type.IsReadOnly && plan.Members.Any(m => HasAttribute(m.Symbol, BoundAttribute)) &&
                        !HasAttribute(member.Symbol, BoundAttribute) && !HasAttribute(member.Symbol, PublishedAttribute) &&
                        !SupportsBoundValue(member.Type))
                        Error(plan, member.Symbol, "已绑定读取的其余成员必须使用实时值合同，不能物化集合、对象或字符串");
                    if (HasAttribute(member.Symbol, PublishedAttribute))
                    {
                        descriptor.Append(":published");
                        if (!HasPublishedRead(member.Type))
                            Error(plan, member.Symbol, "已发布成员必须由正式合同提供同源匹配读取");
                    }
                    if (HasAttribute(member.Symbol, RequiredAttribute))
                    {
                        descriptor.Append(":required");
                        if (!plan.PreparedRead || !(member.Type is INamedTypeSymbol required) ||
                            !required.GetMembers().Any(m => HasAttribute(m, PresenceAttribute)))
                            Error(plan, member.Symbol, "必需记录只能引用带存在条件的实时正式记录");
                    }
                    string defaultMember = CaseDefaultMember(member);
                    if (defaultMember != null && (!(member.Type is INamedTypeSymbol defaultType) ||
                        !defaultType.GetMembers(defaultMember).Any(m => m.IsStatic &&
                            (m is IPropertySymbol property && SymbolEqualityComparer.Default.Equals(property.Type, member.Type) ||
                             m is IFieldSymbol field && SymbolEqualityComparer.Default.Equals(field.Type, member.Type)))))
                        Error(plan, member.Symbol, "联合默认必须引用载荷类型的同类型静态成员");
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
                        if (!OwnsSequenceStorage(member.Type) && !SymbolEqualityComparer.Default.Equals(plan.PreparedStorage, storageType) &&
                            !SymbolEqualityComparer.Default.Equals(actualStorage, storageType))
                            Error(plan, member.Symbol, "借用集合必须由正式 PreparedStorage 的同名原存储成员提供");
                    }
                    if (plan.PreparedRead && type.TypeKind == TypeKind.Class && BufferElement(member.Type) == null &&
                        !(member.Type is IArrayTypeSymbol) &&
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
                plan.TextCapacity = TextCapacity(plan);
                plan.FiniteSize = plan.Members.All(m => FiniteSize(m.Type));
                if (plan.FiniteSize) m_FiniteTypes.Add(Identity(type));
                plan.HasPayload = plan.Cases.Length > 0 || plan.Members.Any(m => m.Payload || HasPayload(plan, m.Type));
                if (plan.HasPayload)
                    m_PayloadTypes.Add(Identity(type));
                plan.Descriptor = descriptor.ToString();
                plan.MinimumSize = plan.Members.Where(m => !m.IsCase).Sum(m => MinimumSize(m.Type));
                if (plan.Cases.Length > 0)
                    plan.MinimumSize += 4 + plan.Cases.Min(m => MinimumSize(m.Type));
                if (plan.Presence != null || plan.PreparedObject)
                    plan.MinimumSize = 1;
                using (var sha = SHA256.Create())
                    plan.Schema = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(plan.Descriptor))
                        .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
                plan.Complete = true;
                return plan;
            }

            string TextCapacity(Plan plan)
            {
                string[] common = plan.Members.Where(m => !m.IsCase).Select(m => TextCapacity(m.Type)).ToArray();
                string[] cases = plan.Cases.Select(m => TextCapacity(m.Type)).ToArray();
                if (common.Any(value => value == null) || cases.Any(value => value == null))
                    return null;
                string capacity = string.Join(" + ", common.Where(value => value != "0"));
                if (capacity.Length == 0) capacity = "0";
                if (cases.Length != 0)
                {
                    string maximum = cases[0];
                    foreach (string value in cases.Skip(1))
                        maximum = "global::System.Math.Max(" + maximum + ", " + value + ")";
                    capacity += " + " + maximum;
                }
                return capacity;
            }

            string TextCapacity(ITypeSymbol type)
            {
                if (ReferenceKey(type) is Member reference) return TextCapacity(reference.Type);
                if (Identity(type) == TextType) return "1";
                if (Primitive(type) != null || type.SpecialType == SpecialType.System_String) return "0";
                if (type.TypeKind == TypeKind.TypeParameter)
                    return "default(" + TypeName(type) + ").CanonicalRecordMaximumTextReferences";
                if (NullableElement(type) is ITypeSymbol nullable) return TextCapacity(nullable);
                if (MapTypes(type) is ITypeSymbol[] map)
                    return map.All(item => TextCapacity(item) == "0") ? "0" : null;
                if (SequenceElement(type) is ITypeSymbol element)
                    return TextCapacity(element) == "0" ? "0" : null;
                if (type is INamedTypeSymbol record)
                {
                    if (m_Plans.TryGetValue(Identity(record.OriginalDefinition), out Plan child))
                        return child.TextCapacity == null ? null : TypeName(type) + ".CanonicalMaximumTextReferences";
                    if (record.GetMembers("CanonicalMaximumTextReferences").OfType<IFieldSymbol>().Any())
                        return TypeName(type) + ".CanonicalMaximumTextReferences";
                }
                return null;
            }

            bool FiniteSize(ITypeSymbol type)
            {
                if (ReferenceKey(type) is Member reference) return FiniteSize(reference.Type);
                if (NullableElement(type) is ITypeSymbol nullable) return FiniteSize(nullable);
                if (Primitive(type) != null || Identity(type) == TextType || type.SpecialType == SpecialType.System_String ||
                    type.TypeKind == TypeKind.TypeParameter) return true;
                if (SequenceElement(type) != null || MapTypes(type) != null) return false;
                if (type is INamedTypeSymbol record)
                {
                    if (m_Plans.TryGetValue(Identity(record.OriginalDefinition), out Plan child))
                        return child.FiniteSize;
                    bool finite = record.GetMembers("CanonicalMaximumEncodedSize").OfType<IMethodSymbol>().Any();
                    if (finite) m_FiniteTypes.Add(Identity(type));
                    return finite;
                }
                return false;
            }

            int MinimumSize(ITypeSymbol type)
            {
                if (ReferenceKey(type) is Member reference) return MinimumSize(reference.Type);
                if (NullableElement(type) != null) return 1;
                if (SequenceElement(type) != null || MapTypes(type) != null || Identity(type) == TextType || type.SpecialType == SpecialType.System_String)
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
                if (HasAttribute(type, PublishedReferenceAttribute))
                {
                    Member[] keys = StorageMembers((INamedTypeSymbol)type)
                        .Where(key => HasAttribute(key.Symbol, ReferenceKeyAttribute)).ToArray();
                    if (keys.Length != 1 || keys[0].Type.SpecialType != SpecialType.System_Int32 ||
                        keys[0].Symbol.DeclaredAccessibility != Accessibility.Public &&
                        keys[0].Symbol.DeclaredAccessibility != Accessibility.Internal)
                    {
                        Error(plan, member, "已发布索引引用必须由唯一实际Int32键成员提供，读取使用原发布表");
                        return Identity(type);
                    }
                    return "published-index:" + Identity(type) + ":" + keys[0].Symbol.Name + ":" + Shape(plan, keys[0].Type, keys[0].Symbol);
                }
                if (NullableElement(type) is ITypeSymbol nullable)
                    return "nullable<" + Shape(plan, nullable, member) + ">";
                if (type is ITypeParameterSymbol parameter)
                {
                    if (!parameter.HasValueTypeConstraint || !parameter.ConstraintTypes.OfType<INamedTypeSymbol>()
                        .Any(t => Identity(t.OriginalDefinition) == RecordType &&
                            SymbolEqualityComparer.Default.Equals(t.TypeArguments[0], parameter)))
                        Error(plan, member, "泛型值必须以 ICanonicalRecord<T> 约束消费同源生成合同");
                    return "record-parameter:" + parameter.Ordinal;
                }
                if (type.SpecialType == SpecialType.System_String)
                {
                    if (plan.PreparedRead && !HasAttribute(member, DeclaredTextAttribute) &&
                        !(plan.SequenceRoot && BorrowedSemantic(plan.Type, "DeclaredText", false)))
                        Error(plan, member, "程序字符串只允许初始化物化读取；实时文本必须由正式文本 owner 持有");
                    return Identity(type);
                }
                if (MapTypes(type) is ITypeSymbol[] map)
                {
                    if (!plan.PreparedRead || plan.Type.IsReadOnly || plan.Cases.Length != 0 ||
                        map.Any(item => item.SpecialType != SpecialType.System_String && !SupportsBoundValue(item)))
                        Error(plan, member, "有序字典由原可变候选存储持有，键和值使用正式实时值合同");
                    if (!SupportsComparison(map[0])) Error(plan, member, "字典键必须提供稳定的标准值比较");
                    return "prepared-sorted-map<" + Shape(plan, map[0], member) + "," + Shape(plan, map[1], member) + ">";
                }
                ITypeSymbol element = SequenceElement(type);
                if (element != null)
                {
                    bool prepared = BufferElement(type) != null || IsPreparedCollection(type) || plan.PreparedRead && type is IArrayTypeSymbol &&
                        !plan.Type.IsReadOnly && plan.Cases.Length == 0;
                    bool borrowed = BorrowedStorage(type) != null;
                    if (borrowed && BorrowedSemantic(type, "RetainStorageOnRead", false) && !plan.OwnsMemberResources)
                        Error(plan, member, "读取持有的借用页直接采用到原资源owner，不能丢失所有权");
                    if (IsPreparedCollection(type) && !plan.PreparedRead)
                        Error(plan, member, "原可变集合使用准备存储读取，不进行初始化物化");
                    if (HasAttribute(member, SetAttribute) && !SupportsComparison(element))
                        Error(plan, member, "有序唯一序列元素必须提供稳定的标准值比较");
                    if (prepared && (plan.Type.IsReadOnly || plan.Cases.Length != 0))
                        Error(plan, member, "预分配集合必须由原候选页持有，不能通过值构造复制");
                    if (!prepared && !borrowed && plan.PreparedRead)
                        Error(plan, member, "只读序列只允许初始化物化读取；实时集合必须由原预分配存储持有");
                    return (borrowed ? "borrowed-sequence:" + BorrowedSemantic(type, "HoldStorage", true) + ":" +
                        (BorrowedSemantic(type, "RetainStorageOnRead", false) ? "retained-read:" : "") +
                        (BorrowedSemantic(type, "DeclaredText", false) ? "declared-text:" : "") +
                        (BorrowedSemantic(type, "SortedUnique", false) ? "sorted-unique:" : "") +
                        BorrowedSemantic(type, "RetainElementsOnRead", false) + "<" : prepared ? "prepared-sequence<" : "initialization-sequence<")
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
                    if (plan.PreparedRead && !UsesPreparedRead(type) && !HasAttribute(member, PublishedAttribute))
                        Error(plan, member, "实时合同不能调用初始化记录的物化读取，需在原 owner 分离状态和发布数据");
                    if (record.Arity != 0)
                    {
                        INamedTypeSymbol definition = record.OriginalDefinition;
                        string templateSchema;
                        if (SymbolEqualityComparer.Default.Equals(record.ContainingAssembly, m_Compilation.Assembly))
                        {
                            Plan child = Plan(definition);
                            if (!child.Valid) plan.Valid = false;
                            templateSchema = child.Schema;
                        }
                        else
                            templateSchema = (string)definition.GetMembers("CanonicalTemplateSchema")
                                .OfType<IFieldSymbol>().Single().ConstantValue;
                        string[] arguments = record.TypeArguments.Select(argument => Shape(plan, argument, member)).ToArray();
                        m_ResourceTypes.Add(Identity(record));
                        m_TextTypes.Add(Identity(record));
                        return Identity(type) + "#generic:" + templateSchema + "<" + string.Join(",", arguments) + ">";
                    }
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

        static Member ReferenceKey(ITypeSymbol type) => type is INamedTypeSymbol named && HasAttribute(type, PublishedReferenceAttribute)
            ? StorageMembers(named).FirstOrDefault(member => HasAttribute(member.Symbol, ReferenceKeyAttribute)) : null;

        static Member[] StorageMembers(INamedTypeSymbol type) => type.GetMembers()
            .Where(s => !s.IsStatic && !s.IsImplicitlyDeclared)
            .Where(s => s is IFieldSymbol || s is IPropertySymbol p && IsAutoProperty(p))
            .OrderBy(Identity, StringComparer.Ordinal)
            .Select(s => new Member { Symbol = s, Type = s is IFieldSymbol f ? f.Type : ((IPropertySymbol)s).Type,
                IsCase = HasAttribute(s, UnionCaseAttribute), Payload = HasAttribute(s, PayloadAttribute) }).ToArray();

        static bool ValidateTemplate(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Enum) return true;
            var codes = new HashSet<int>();
            bool valid = true;
            foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(member => member.HasConstantValue))
            {
                AttributeData branch = field.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == UnionCaseAttribute);
                string reason = null;
                if (branch == null || branch.ConstructorArguments.Length != 1 ||
                    !(branch.ConstructorArguments[0].Value is ITypeSymbol))
                    reason = "每个正式 enum 分支必须在原成员声明唯一载荷类型";
                decimal code = Convert.ToDecimal(field.ConstantValue, CultureInfo.InvariantCulture);
                if (code < int.MinValue || code > int.MaxValue || !codes.Add((int)code))
                    reason = "正式联合分支必须使用唯一 Int32 判别值";
                if (reason == null) continue;
                context.ReportDiagnostic(Diagnostic.Create(InvalidContract, field.Locations.FirstOrDefault(), Identity(field), reason));
                valid = false;
            }
            return valid;
        }

        static Member[] UnionMembers(INamedTypeSymbol type) => type.TypeKind == TypeKind.Enum ?
            type.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue && HasAttribute(field, UnionCaseAttribute))
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .Select(field => new Member
                {
                    Symbol = field,
                    Type = (ITypeSymbol)field.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == UnionCaseAttribute)
                        .ConstructorArguments.Single().Value,
                    IsCase = true
                }).ToArray() : StorageMembers(type);

        static IEnumerable<List<Member>> CaseGroups(IEnumerable<Member> cases)
        {
            var groups = new SortedDictionary<string, List<Member>>(StringComparer.Ordinal);
            foreach (Member member in cases)
            {
                string identity = Identity(member.Type);
                if (!groups.TryGetValue(identity, out List<Member> group))
                    groups.Add(identity, group = new List<Member>());
                group.Add(member);
            }
            return groups.Values;
        }

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
