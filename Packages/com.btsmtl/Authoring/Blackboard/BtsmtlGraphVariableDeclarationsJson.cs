using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using Newtonsoft.Json.Linq;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs
{
    public static class BtsmtlGraphVariableDeclarationsJson
    {
        public const string ContentDomain = "graph-variables";

        static JToken Required(JObject value, string member) => value.Property(member)?.Value ??
            throw new InvalidDataException($"图变量作者数据缺少正式成员 '{member}'。");

        public static BtsmtlGraphVariableDeclarations Read(JObject root)
        {
            if ((string)Required(root, "m_ContentDomain") != ContentDomain)
                throw new InvalidDataException("图变量文档必须声明 graph-variables 领域。");
            return new BtsmtlGraphVariableDeclarations((string)Required(root, "m_OwnerId"), (string)Required(root, "m_Revision"),
                ((JArray)Required(root, "m_Variables")).Cast<JObject>().Select(value =>
                new BtsmtlGraphVariableDeclaration(ReadDeclaration((JObject)Required(value, "m_Declaration")),
                    (string)Required(value, "m_Name"), (string)Required(value, "m_TypeId"), Required(value, "m_DefaultValue"),
                    (bool)Required(value, "m_IsPublic"), (int)Required(value, "m_DeclarationOrder"))).ToArray());
        }

        public static JObject Write(BtsmtlGraphVariableDeclarations source) => new()
        {
            ["m_ContentDomain"] = ContentDomain, ["m_OwnerId"] = source.OwnerId, ["m_Revision"] = source.Revision,
            ["m_Variables"] = new JArray(source.Variables.Select(variable => new JObject
            {
                ["m_Declaration"] = WriteDeclaration(variable.Declaration), ["m_Name"] = variable.Name,
                ["m_TypeId"] = variable.TypeId, ["m_DefaultValue"] = variable.DefaultValue.DeepClone(),
                ["m_IsPublic"] = variable.IsPublic, ["m_DeclarationOrder"] = variable.DeclarationOrder
            }))
        };

        static BtsmtlBlackboardDeclaration ReadDeclaration(JObject value)
        {
            JToken binding = Required(value, "m_InputBinding");
            PipelineBlackboardInputBinding input = binding.Type == JTokenType.Null ? null :
                PipelineBlackboardInputBinding.FromStored((string)Required((JObject)binding, "m_InputValueId"),
                    (PipelineBlackboardInputProjection)(int)Required((JObject)binding, "m_Projection"));
            return BtsmtlBlackboardDeclaration.FromStored((string)Required(value, "m_VariableId"),
                (PipelineBlackboardVariableScope)(int)Required(value, "m_Scope"),
                (PipelineBlackboardVariableLifetime)(int)Required(value, "m_Lifetime"),
                (string)Required(value, "m_Category"), input);
        }

        static JObject WriteDeclaration(BtsmtlBlackboardDeclaration declaration)
        {
            PipelineBlackboardInputBinding binding = declaration.StoredInputBinding;
            return new JObject
            {
                ["m_VariableId"] = declaration.VariableId, ["m_Scope"] = (int)declaration.Scope,
                ["m_Lifetime"] = (int)declaration.Lifetime, ["m_Category"] = declaration.Category,
                ["m_InputBinding"] = binding == null ? JValue.CreateNull() : new JObject
                {
                    ["m_InputValueId"] = binding.StoredInputValueId, ["m_Projection"] = (int)binding.Projection
                }
            };
        }

        public static object ReadDefault(CompileValueType type, JToken value)
        {
            if (type.IsEnum) return (int)value;
            return CompileValueTypes.Id(type) switch
            {
                "bool" => (bool)value, "int" => (int)value, "uint" => (uint)value, "ulong" => (ulong)value,
                "float" => (float)value, "double" => (double)value, "string" => (string)value,
                "object-handle" => new ObjectHandle(ulong.Parse((string)value, NumberStyles.None, CultureInfo.InvariantCulture)),
                "vector2" => new SemanticVector2Value((float)value["x"], (float)value["y"]),
                "vector3" => new SemanticVector3Value((float)value["x"], (float)value["y"], (float)value["z"]),
                "quaternion" => new SemanticQuaternionValue((float)value["x"], (float)value["y"], (float)value["z"], (float)value["w"]),
                _ => throw new InvalidDataException($"图变量值类型 '{type.FullName}' 没有默认值协议。")
            };
        }

        public static JToken CreateDefault(CompileValueType type) => WriteDefault(type, type.DefaultValue switch
        {
            SemanticVector2Value value => new System.Numerics.Vector2((float)value.X, (float)value.Y),
            SemanticVector3Value value => new System.Numerics.Vector3((float)value.X, (float)value.Y, (float)value.Z),
            SemanticQuaternionValue value => new System.Numerics.Quaternion((float)value.X, (float)value.Y, (float)value.Z, (float)value.W),
            _ => type.DefaultValue
        });

        public static JToken WriteDefault(CompileValueType type, object value)
        {
            if (value is JToken token)
                return token.DeepClone();
            if (value == null)
                return JValue.CreateNull();
            if (type.IsEnum)
                return new JValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
            if (type.Kind == SemanticValueKind.Vector2)
            {
                var vector = (System.Numerics.Vector2)value;
                return new JObject { ["x"] = vector.X, ["y"] = vector.Y };
            }
            if (type.Kind == SemanticValueKind.Vector3)
            {
                var vector = (System.Numerics.Vector3)value;
                return new JObject { ["x"] = vector.X, ["y"] = vector.Y, ["z"] = vector.Z };
            }
            if (type.Kind == SemanticValueKind.Quaternion)
            {
                var rotation = (System.Numerics.Quaternion)value;
                return new JObject { ["x"] = rotation.X, ["y"] = rotation.Y, ["z"] = rotation.Z, ["w"] = rotation.W };
            }
            if (type.Kind == SemanticValueKind.ObjectHandle)
                return new JValue(((ObjectHandle)value).Value.ToString(CultureInfo.InvariantCulture));
            return new JValue(value);
        }

        internal static void RequireDefault(CompileValueType type, JToken value)
        {
            bool numeric = value.Type == JTokenType.Integer || value.Type == JTokenType.Float;
            bool valid = type.IsEnum ? value.Type == JTokenType.Integer : CompileValueTypes.Id(type) switch
            {
                "bool" => value.Type == JTokenType.Boolean,
                "int" or "uint" or "ulong" => value.Type == JTokenType.Integer,
                "float" or "double" => numeric,
                "string" => value.Type == JTokenType.String || value.Type == JTokenType.Null,
                "object-handle" => value.Type == JTokenType.String,
                "vector2" => Components(value, "x", "y"),
                "vector3" => Components(value, "x", "y", "z"),
                "quaternion" => Components(value, "x", "y", "z", "w"),
                _ => false
            };
            if (!valid)
                throw new InvalidDataException($"图变量 '{CompileValueTypes.Id(type)}' 必须提供对应类型的完整 JSON 默认值。");
            if (type.Kind == SemanticValueKind.Number)
                ValueValidation.RequireFinite(type.FullName == "System.Single" ? (double)(float)value : (double)value, nameof(value));
            ReadDefault(type, value);
        }

        static bool Components(JToken value, params string[] names) => value is JObject vector &&
            vector.Count == names.Length && names.All(name => vector[name] is JValue component &&
                (component.Type == JTokenType.Integer || component.Type == JTokenType.Float));

        public static TypeCode TypeCode(CompileValueType type) => type.Kind switch
        {
            SemanticValueKind.Boolean => System.TypeCode.Boolean,
            SemanticValueKind.Int32 => System.TypeCode.Int32,
            SemanticValueKind.Number => type.FullName == "System.Single" ? System.TypeCode.Single : System.TypeCode.Double,
            SemanticValueKind.UInt64 => type.FullName == "System.UInt32" ? System.TypeCode.UInt32 : System.TypeCode.UInt64,
            SemanticValueKind.Identity => System.TypeCode.String,
            _ => System.TypeCode.Object
        };

        public static bool SameDeclaration(BtsmtlBlackboardDeclaration left, BtsmtlBlackboardDeclaration right)
        {
            PipelineBlackboardInputBinding a = left.StoredInputBinding;
            PipelineBlackboardInputBinding b = right.StoredInputBinding;
            return left.VariableId == right.VariableId && left.Scope == right.Scope && left.Lifetime == right.Lifetime &&
                left.Category == right.Category && (a == null && b == null || a != null && b != null &&
                    a.StoredInputValueId == b.StoredInputValueId && a.Projection == b.Projection);
        }

        public static bool SameVariable(BtsmtlGraphVariableDeclaration left, BtsmtlGraphVariableDeclaration right) =>
            left.Name == right.Name && left.TypeId == right.TypeId && left.IsPublic == right.IsPublic &&
            left.DeclarationOrder == right.DeclarationOrder && SameDeclaration(left.Declaration, right.Declaration) &&
            SameDefault(left.ValueType, left.DefaultValue, right.DefaultValue);

        public static bool SameDefault(CompileValueType type, JToken left, JToken right)
        {
            if (type.Kind == SemanticValueKind.Number)
                return (left.Type == JTokenType.Integer || left.Type == JTokenType.Float) &&
                    (right.Type == JTokenType.Integer || right.Type == JTokenType.Float) && (type.FullName == "System.Single"
                    ? BitConverter.SingleToInt32Bits((float)left) == BitConverter.SingleToInt32Bits((float)right)
                    : BitConverter.DoubleToInt64Bits((double)left) == BitConverter.DoubleToInt64Bits((double)right));
            if (type.Kind == SemanticValueKind.Vector2 || type.Kind == SemanticValueKind.Vector3 || type.Kind == SemanticValueKind.Quaternion)
                return left is JObject a && right is JObject b && a.Count == b.Count && a.Properties().All(property =>
                    b.TryGetValue(property.Name, out JToken value) &&
                    (value.Type == JTokenType.Float || value.Type == JTokenType.Integer) &&
                    BitConverter.SingleToInt32Bits((float)property.Value) == BitConverter.SingleToInt32Bits((float)value));
            return JToken.DeepEquals(left, right);
        }
    }
}
