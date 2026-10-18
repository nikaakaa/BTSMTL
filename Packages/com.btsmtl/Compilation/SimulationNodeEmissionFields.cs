using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public static class SimulationNodeEmissionFields
    {
        public static KeyValuePair<string, object>[] ExtensionFields(in OrchestrationExtensionDefinition extension,
            IEnumerable<KeyValuePair<string, object>> fields)
        {
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            ReadOnlySpan<OrchestrationExtensionContentField> declarations = extension.Contract.Fields.Span;
            for (int i = 0; i < declarations.Length; i++)
                order.Add(declarations[i].Id, i);
            KeyValuePair<string, object>[] result = fields.ToArray();
            Array.Sort(result, (left, right) => order[left.Key].CompareTo(order[right.Key]));
            for (int i = 0; i < result.Length; i++)
                if (result[i].Value is Enum value)
                    result[i] = new KeyValuePair<string, object>(result[i].Key,
                        new SemanticEnumValue(value.GetType().AssemblyQualifiedName,
                            Convert.ToInt32(value, CultureInfo.InvariantCulture)));
            return result;
        }

        public static KeyValuePair<string, object>[] Fields(params (string Name, object Value)[] values)
        {
            KeyValuePair<string, object>[] result = new KeyValuePair<string, object>[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = new KeyValuePair<string, object>(values[i].Name, values[i].Value);
            return result;
        }
    }
}
