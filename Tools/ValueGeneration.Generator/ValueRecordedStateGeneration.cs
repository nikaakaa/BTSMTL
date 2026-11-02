using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        const string RecordedStateConstructorAttribute = "ThirdPersonSimulation.ValueRecordedStateConstructorAttribute";
        const string RecordedStateAttribute = "ThirdPersonSimulation.ValueRecordedStateAttribute";
        const string RecordedStateContextAttribute = "ThirdPersonSimulation.ValueRecordedStateContextAttribute";

        enum RecordedStateKind { Text, Unmanaged, Record, Constructor, Context }

        sealed class RecordedStateValue
        {
            internal ITypeSymbol Type;
            internal RecordedStateKind Kind;
            internal Member[] Members;
            internal RecordedStateValue[] Inputs;
            internal string ContextName;
        }

        static bool EmitDeclaredRecordedStates(GeneratorExecutionContext context, StringBuilder code, INamedTypeSymbol type)
        {
            Member[] storage = StorageMembers(type).Where(member =>
                !HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueStorageIgnoreAttribute")).ToArray();
            var names = new HashSet<string>();
            foreach (IMethodSymbol constructor in type.InstanceConstructors.Where(candidate => HasAttribute(candidate, RecordedStateAttribute)))
            {
                AttributeData purpose = constructor.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == RecordedStateAttribute);
                string name = (string)purpose.ConstructorArguments[0].Value;
                bool readInputs = purpose.NamedArguments.Any(argument => argument.Key == "ReadInputs" && (bool)argument.Value.Value);
                Member[] members = ValueStorageConstructorInputs(context.Compilation, type, constructor, storage);
                if (members == null || !Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier("Read" + name + "RecordedState") ||
                    !names.Add(name))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, constructor.Locations.FirstOrDefault(), Identity(type),
                        "记录声明须有唯一合法名称，并对应原构造与原成员"));
                    return false;
                }
                if (!EmitRecordedState(context, code, type, constructor, members, name, readInputs)) return false;
            }
            return true;
        }

        static bool EmitRecordedState(GeneratorExecutionContext context, StringBuilder code, INamedTypeSymbol type,
            IMethodSymbol constructor, Member[] members, string name = "", bool readInputs = false)
        {
            var inputs = new RecordedStateValue[constructor.Parameters.Length];
            for (int i = 0; i < inputs.Length; i++)
                if (!TryRecordedStateValue(context, type, constructor.Parameters[i], out inputs[i], out string reason))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, members[i].Symbol.Locations.FirstOrDefault(),
                        Identity(type), "RecordedState 成员 '" + members[i].Symbol.Name + "': " + reason));
                    return false;
                }
            code.Append("public void Write").Append(name).Append("RecordedState<TWriter>(ref TWriter writer) where TWriter : struct, ")
                .Append(Runtime).AppendLine("IRecordedRuntimeStateWriter {");
            for (int i = 0; i < inputs.Length; i++)
                EmitRecordedStateWrite(code, inputs[i], "this." + Escape(members[i].Symbol.Name));
            code.AppendLine("}");
            if (readInputs)
            {
                code.Append("public static void Read").Append(name).Append("RecordedStateInputs(ref ")
                    .Append(Runtime).Append("RecordedRuntimeStateReader reader");
                for (int i = 0; i < inputs.Length; i++)
                    if (inputs[i].Kind != RecordedStateKind.Context)
                        code.Append(", out ").Append(TypeName(constructor.Parameters[i].Type)).Append(' ')
                            .Append(Escape(constructor.Parameters[i].Name));
                code.AppendLine(") {");
                for (int i = 0; i < inputs.Length; i++)
                    if (inputs[i].Kind != RecordedStateKind.Context)
                        code.Append(Escape(constructor.Parameters[i].Name)).Append(" = ").Append(RecordedStateRead(inputs[i])).AppendLine(";");
                code.AppendLine("}");
                return true;
            }
            code.Append("public static ").Append(TypeName(type)).Append(" Read").Append(name).Append("RecordedState(ref ")
                .Append(Runtime).Append("RecordedRuntimeStateReader reader");
            foreach (RecordedStateValue input in inputs.Where(input => input.Kind == RecordedStateKind.Context))
                code.Append(", ").Append(TypeName(input.Type)).Append(' ').Append(Escape(input.ContextName));
            code.Append(") => new ").Append(TypeName(type)).Append('(')
                .Append(string.Join(", ", inputs.Select(RecordedStateRead))).AppendLine(");");
            return true;
        }

        static bool TryRecordedStateValue(GeneratorExecutionContext context, INamedTypeSymbol owner, IParameterSymbol parameter,
            out RecordedStateValue value, out string reason)
        {
            ITypeSymbol type = parameter.Type;
            value = new RecordedStateValue { Type = type };
            reason = null;
            if (HasAttribute(parameter, RecordedStateContextAttribute))
            {
                value.Kind = RecordedStateKind.Context;
                value.ContextName = parameter.Name;
                return true;
            }
            if (HasAttribute(parameter, RecordedStateConstructorAttribute))
            {
                if (!(type is INamedTypeSymbol record) || record.TypeKind != TypeKind.Struct || record.IsRefLikeType)
                {
                    reason = "原构造展开须为普通值记录";
                    return false;
                }
                Member[] storage = StorageMembers(record).Where(member =>
                    !HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueStorageIgnoreAttribute")).ToArray();
                var constructors = record.InstanceConstructors.Where(candidate => !candidate.IsImplicitlyDeclared)
                    .Select(candidate => new { Constructor = candidate,
                        Inputs = ValueStorageConstructorInputs(context.Compilation, record, candidate, storage) })
                    .Where(candidate => candidate.Inputs != null).ToArray();
                if (constructors.Length != 1 || !context.Compilation.IsSymbolAccessibleWithin(constructors[0].Constructor, owner) ||
                    constructors[0].Inputs.Any(member => !context.Compilation.IsSymbolAccessibleWithin(
                        member.Symbol is IPropertySymbol property ? property.GetMethod : member.Symbol, owner)))
                {
                    reason = "原构造展开须唯一对应可访问的原构造与原成员";
                    return false;
                }
                value.Kind = RecordedStateKind.Constructor;
                value.Members = constructors[0].Inputs;
                value.Inputs = new RecordedStateValue[value.Members.Length];
                for (int i = 0; i < value.Inputs.Length; i++)
                    if (!TryRecordedStateValue(context, owner, constructors[0].Constructor.Parameters[i], out value.Inputs[i], out reason))
                        return false;
                return true;
            }
            AttributeData storagePurpose = type.GetAttributes().SingleOrDefault(attribute =>
                Identity(attribute.AttributeClass) == ValueStoragePurposeAttribute);
            if (storagePurpose != null && storagePurpose.NamedArguments.Any(argument => argument.Key == "RecordedState" && (bool)argument.Value.Value))
                value.Kind = RecordedStateKind.Record;
            else if (type.SpecialType == SpecialType.System_String)
                value.Kind = RecordedStateKind.Text;
            else if (type.IsUnmanagedType)
                value.Kind = RecordedStateKind.Unmanaged;
            else
            {
                reason = "须为原 string、unmanaged 值、同源 RecordedState 记录或显式原构造展开";
                return false;
            }
            return true;
        }

        static void EmitRecordedStateWrite(StringBuilder code, RecordedStateValue value, string access)
        {
            switch (value.Kind)
            {
                case RecordedStateKind.Context:
                    break;
                case RecordedStateKind.Constructor:
                    for (int i = 0; i < value.Inputs.Length; i++)
                        EmitRecordedStateWrite(code, value.Inputs[i], access + "." + Escape(value.Members[i].Symbol.Name));
                    break;
                case RecordedStateKind.Record:
                    code.Append(access).AppendLine(".WriteRecordedState(ref writer);");
                    break;
                default:
                    code.Append(value.Kind == RecordedStateKind.Text ? "writer.WriteText(" : "writer.Write(")
                        .Append(access).AppendLine(");");
                    break;
            }
        }

        static string RecordedStateRead(RecordedStateValue value)
        {
            switch (value.Kind)
            {
                case RecordedStateKind.Context:
                    return Escape(value.ContextName);
                case RecordedStateKind.Constructor:
                    return "new " + TypeName(value.Type) + "(" + string.Join(", ", value.Inputs.Select(RecordedStateRead)) + ")";
                case RecordedStateKind.Record:
                    return TypeName(value.Type) + ".ReadRecordedState(ref reader)";
                case RecordedStateKind.Text:
                    return "reader.ReadText()";
                default:
                    return "reader.Read<" + TypeName(value.Type) + ">()";
            }
        }
    }
}
