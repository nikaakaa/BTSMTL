using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        const string RecordedStateConstructorAttribute = "ThirdPersonSimulation.ValueRecordedStateConstructorAttribute";

        enum RecordedStateKind { Text, Unmanaged, Record, Constructor }

        sealed class RecordedStateValue
        {
            internal ITypeSymbol Type;
            internal RecordedStateKind Kind;
            internal Member[] Members;
            internal RecordedStateValue[] Inputs;
        }

        static bool EmitRecordedState(GeneratorExecutionContext context, StringBuilder code, INamedTypeSymbol type,
            IMethodSymbol constructor, Member[] members)
        {
            var inputs = new RecordedStateValue[constructor.Parameters.Length];
            for (int i = 0; i < inputs.Length; i++)
                if (!TryRecordedStateValue(context, type, constructor.Parameters[i], out inputs[i], out string reason))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, members[i].Symbol.Locations.FirstOrDefault(),
                        Identity(type), "RecordedState 成员 '" + members[i].Symbol.Name + "': " + reason));
                    return false;
                }
            code.Append("public void WriteRecordedState<TWriter>(ref TWriter writer) where TWriter : struct, ")
                .Append(Runtime).AppendLine("IRecordedRuntimeStateWriter {");
            for (int i = 0; i < inputs.Length; i++)
                EmitRecordedStateWrite(code, inputs[i], "this." + Escape(members[i].Symbol.Name));
            code.AppendLine("}");
            code.Append("public static ").Append(TypeName(type)).Append(" ReadRecordedState(ref ")
                .Append(Runtime).Append("RecordedRuntimeStateReader reader) => new ").Append(TypeName(type)).Append('(')
                .Append(string.Join(", ", inputs.Select(RecordedStateRead))).AppendLine(");");
            return true;
        }

        static bool TryRecordedStateValue(GeneratorExecutionContext context, INamedTypeSymbol owner, IParameterSymbol parameter,
            out RecordedStateValue value, out string reason)
        {
            ITypeSymbol type = parameter.Type;
            value = new RecordedStateValue { Type = type };
            reason = null;
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
                    constructors[0].Inputs.Any(member => !context.Compilation.IsSymbolAccessibleWithin(member.Symbol, owner)))
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
