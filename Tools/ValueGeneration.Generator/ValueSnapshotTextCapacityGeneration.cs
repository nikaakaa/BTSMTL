using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        static bool SnapshotTextCapacitySupported(SnapshotShape shape)
        {
            if (shape.Kind == SnapshotKind.Source) return true;
            if (shape.Kind == SnapshotKind.Value)
                return TryMaximumTextReferences(shape.Type, new HashSet<string>(), out _) ||
                    shape.Type.GetMembers("TextReferenceCapacity").OfType<IPropertySymbol>().Any(property =>
                        !property.IsStatic && property.Type.SpecialType == SpecialType.System_Int32 &&
                        property.GetMethod?.DeclaredAccessibility == Accessibility.Public);
            return (shape.Key == null || TryMaximumTextReferences(shape.Key, new HashSet<string>(), out _)) &&
                SnapshotTextCapacitySupported(shape.Element);
        }

        static void EmitSnapshotTextCapacity(StringBuilder code, Member[] members, List<SnapshotShape> shapes)
        {
            code.AppendLine("public int TextReferenceCapacity { get { checked { int capacity = 0;");
            for (int index = 0; index < members.Length; index++)
                EmitSnapshotTextCapacity(code, shapes[index], "this." + Escape(members[index].Symbol.Name), 0);
            code.AppendLine("return capacity; } } }");
        }

        static void EmitSnapshotTextCapacity(StringBuilder code, SnapshotShape shape, string value, int depth)
        {
            if (shape.Kind == SnapshotKind.Value)
            {
                if (TryMaximumTextReferences(shape.Type, new HashSet<string>(), out int maximum))
                {
                    if (maximum != 0) code.Append("capacity += ").Append(maximum.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                }
                else
                    code.Append("capacity += ").Append(value).AppendLine(".TextReferenceCapacity;");
                return;
            }
            if (shape.Kind == SnapshotKind.Source)
            {
                code.Append("capacity += ((global::ThirdPersonSimulation.IValueTextCapacity)").Append(value)
                    .AppendLine(").TextReferenceCapacity;");
                return;
            }
            string bound = shape.Kind == SnapshotKind.Array || shape.Kind == SnapshotKind.Sequence && shape.Element.Kind == SnapshotKind.Source
                ? value + ".Length" : shape.Kind == SnapshotKind.Dictionary ?
                    value + (shape.FixedKeys ? ".Count" : ".EnsureCapacity(0)") : value + ".Capacity";
            int keyMaximum = 0;
            if (shape.Key != null) TryMaximumTextReferences(shape.Key, new HashSet<string>(), out keyMaximum);
            if (shape.Element.Kind == SnapshotKind.Value && TryMaximumTextReferences(shape.Element.Type, new HashSet<string>(), out int itemMaximum))
            {
                int total = checked(keyMaximum + itemMaximum);
                if (total != 0) code.Append("capacity += ").Append(bound).Append(" * ").Append(total.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                return;
            }
            if (keyMaximum != 0) code.Append("capacity += ").Append(bound).Append(" * ")
                .Append(keyMaximum.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            string index = "__capacityIndex" + depth;
            if (shape.Kind == SnapshotKind.Dictionary)
            {
                code.Append("foreach (var ").Append(index).Append(" in ").Append(value).AppendLine(") {");
                EmitSnapshotTextCapacity(code, shape.Element, index + ".Value", depth + 1);
            }
            else
            {
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(bound)
                    .Append("; ").Append(index).AppendLine("++) {");
                EmitSnapshotTextCapacity(code, shape.Element, value + "[" + index + "]", depth + 1);
            }
            code.AppendLine("}");
        }
    }
}
