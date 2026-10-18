#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace BTSMTL.Authoring.Graph
{
    public static class GraphAuthoringMemberBindings
    {
        sealed class MemberBinding
        {
            public MemberBinding(Type type, BTSMTL.Authoring.Graph.GraphAuthoringFieldAttribute field, PropertyInfo[] read)
            {
                Field = field;
                Read = read;
                ParameterExpression target = Expression.Parameter(typeof(object), "target");
                Value = Expression.Lambda<Func<object, object>>(
                    Expression.Convert(ReadExpression(type, this, target), typeof(object)), target).Compile();
            }

            public BTSMTL.Authoring.Graph.GraphAuthoringFieldAttribute Field { get; }
            public PropertyInfo[] Read { get; }
            public Delegate Reader;
            public Delegate Choices;
            public Func<object, UnityEngine.Object> ResourceOwner;
            public Func<object, UnityEngine.Object, object> BindResourceAsset;
            public Func<object, object> Value { get; }
        }

        sealed class OperationBinding
        {
            public Action<object, object[]> Invoke;
            public MemberBinding[] Fields;
            public Func<object, IReadOnlyDictionary<string, object>, object>[] Arguments;
        }

        sealed class TypeBinding
        {
            public readonly Dictionary<string, MemberBinding> Fields = new(StringComparer.Ordinal);
            public readonly List<OperationBinding> Operations = new();
        }

        static readonly Dictionary<Type, TypeBinding> s_Bindings = new();

        static TypeBinding Bindings(Type type)
        {
            if (s_Bindings.TryGetValue(type, out TypeBinding bindings))
                return bindings;
            bindings = new TypeBinding();
            foreach (BTSMTL.Authoring.Graph.GraphAuthoringFieldAttribute field in
                     type.GetCustomAttributes<BTSMTL.Authoring.Graph.GraphAuthoringFieldAttribute>(true))
            {
                var members = new List<PropertyInfo>();
                Type owner = type;
                foreach (string name in field.ReadMember.Split('.'))
                {
                    PropertyInfo member = owner.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                    if (member == null || member.GetMethod == null)
                        throw new InvalidOperationException($"节点'{type.FullName}'字段'{field.FieldId}'缺少公开读取成员'{field.ReadMember}'。");
                    members.Add(member);
                    owner = member.PropertyType;
                }
                if (!field.ReadOnly && string.IsNullOrEmpty(field.WriteMember))
                    throw new InvalidOperationException($"节点'{type.FullName}'字段'{field.FieldId}'的公开作者成员绑定不完整。");
                var binding = new MemberBinding(type, field, members.ToArray());
                if (!string.IsNullOrEmpty(field.ChoicesMember))
                {
                    var target = Expression.Parameter(typeof(object), "target");
                    Expression choices = Expression.Property(Expression.Convert(target, type), field.ChoicesMember);
                    binding.Choices = Expression.Lambda(choices, target).Compile();
                }
                if (!string.IsNullOrEmpty(field.ResourceAssetBindingMember))
                {
                    var target = Expression.Parameter(typeof(object), "target");
                    var source = Expression.Parameter(typeof(UnityEngine.Object), "source");
                    Expression node = Expression.Convert(target, type);
                    binding.ResourceOwner = Expression.Lambda<Func<object, UnityEngine.Object>>(
                        Expression.Convert(Expression.Property(node, field.ResourceContextMember),
                            typeof(UnityEngine.Object)), target).Compile();
                    binding.BindResourceAsset = Expression.Lambda<Func<object, UnityEngine.Object, object>>(
                        Expression.Convert(Expression.Call(node, field.ResourceAssetBindingMember, null, source),
                            typeof(object)), target, source).Compile();
                }
                bindings.Fields.Add(field.FieldId, binding);
            }
            foreach (var group in bindings.Fields.Values.Where(value => !value.Field.ReadOnly)
                         .GroupBy(value => value.Field.WriteMember, StringComparer.Ordinal))
            {
                MemberBinding[] fields = group.ToArray();
                MethodInfo method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .SingleOrDefault(value => value.Name == group.Key && value.ReturnType == typeof(void) &&
                        value.GetParameters().All(parameter => fields.Any(field =>
                            string.IsNullOrEmpty(field.Field.WriteParameter)
                                ? fields.Length == 1 && value.GetParameters().Length == 1
                                : field.Field.WriteParameter.Split('.')[0] == parameter.Name)) &&
                        fields.All(field => value.GetParameters().Any(parameter =>
                            string.IsNullOrEmpty(field.Field.WriteParameter) ||
                            field.Field.WriteParameter.Split('.')[0] == parameter.Name)));
                if (method == null)
                    throw new InvalidOperationException($"节点'{type.FullName}'的公开配置操作'{group.Key}'与字段参数声明不一致。");
                ParameterInfo[] parameters = method.GetParameters();
                bindings.Operations.Add(new OperationBinding
                {
                    Invoke = BindOperation(type, method),
                    Fields = fields,
                    Arguments = parameters.Select(parameter => Argument(
                        parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType,
                        parameter.Name, fields)).ToArray()
                });
            }
            s_Bindings.Add(type, bindings);
            return bindings;
        }

        static Action<object, object[]> BindOperation(Type type, MethodInfo method)
        {
            var target = Expression.Parameter(typeof(object), "target");
            var arguments = Expression.Parameter(typeof(object[]), "arguments");
            ParameterInfo[] parameters = method.GetParameters();
            var values = new ParameterExpression[parameters.Length];
            var expressions = new Expression[parameters.Length + 1];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                values[i] = Expression.Variable(parameterType.IsByRef ? parameterType.GetElementType() : parameterType);
                expressions[i] = Expression.Assign(values[i], Expression.Convert(
                    Expression.ArrayIndex(arguments, Expression.Constant(i)), values[i].Type));
            }
            expressions[parameters.Length] = Expression.Call(Expression.Convert(target, type), method, values);
            return Expression.Lambda<Action<object, object[]>>(
                Expression.Block(values, expressions), target, arguments).Compile();
        }

        static Func<object, IReadOnlyDictionary<string, object>, object> Argument(
            Type target, string path, MemberBinding[] fields)
        {
            MemberBinding direct = fields.SingleOrDefault(field =>
                string.IsNullOrEmpty(field.Field.WriteParameter) || field.Field.WriteParameter == path);
            if (direct != null)
            {
                Type source = direct.Read[direct.Read.Length - 1].PropertyType;
                ConstructorInfo converter = target.IsAssignableFrom(source) ? null : target.GetConstructor(new[] { source });
                if (!target.IsAssignableFrom(source) && converter == null)
                    throw new InvalidOperationException($"作者字段'{direct.Field.FieldId}'无法从{source}传入{target}。");
                return (node, values) =>
                {
                    object value = values.TryGetValue(direct.Field.FieldId, out object submitted) ? submitted : direct.Value(node);
                    return converter == null ? value : converter.Invoke(new[] { value });
                };
            }
            ConstructorInfo constructor = target.GetConstructors().Single(value =>
                value.GetParameters().All(parameter => fields.Any(field =>
                    field.Field.WriteParameter == path + "." + parameter.Name ||
                    field.Field.WriteParameter.StartsWith(path + "." + parameter.Name + ".", StringComparison.Ordinal))) &&
                fields.Where(field => field.Field.WriteParameter.StartsWith(path + ".", StringComparison.Ordinal))
                    .All(field => value.GetParameters().Any(parameter =>
                        field.Field.WriteParameter.Split('.')[path.Split('.').Length] == parameter.Name)));
            var children = constructor.GetParameters().Select(parameter =>
                Argument(parameter.ParameterType, path + "." + parameter.Name, fields)).ToArray();
            return (node, values) => constructor.Invoke(children.Select(child => child(node, values)).ToArray());
        }

        public static object DefaultValue(Type type, BTSMTL.Authoring.Graph.GraphAuthoringFieldAttribute field)
        {
            object defaults = Activator.CreateInstance(type);
            foreach (string name in field.DefaultMember.Split('.'))
                defaults = defaults.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).GetValue(defaults);
            return defaults;
        }

        public static Type ValueType(Type type, string fieldId)
        {
            PropertyInfo[] read = Bindings(type).Fields[fieldId].Read;
            return read[read.Length - 1].PropertyType;
        }

        public static Delegate Reader(Type type, string fieldId)
        {
            MemberBinding binding = Bindings(type).Fields[fieldId];
            if (binding.Reader == null)
            {
                ParameterExpression target = Expression.Parameter(typeof(object), "target");
                Expression value = ReadExpression(type, binding, target);
                binding.Reader = Expression.Lambda(
                    Expression.GetFuncType(typeof(object), value.Type), value, target).Compile();
            }
            return binding.Reader;
        }

        public static Func<object, IReadOnlyList<TChoice>> Choices<TChoice>(Type type, string fieldId) =>
            (Func<object, IReadOnlyList<TChoice>>)Bindings(type).Fields[fieldId].Choices;

        public static UnityEngine.Object ResourceOwner(object target, string fieldId) =>
            Bindings(target.GetType()).Fields[fieldId].ResourceOwner(target);

        public static object BindResourceAsset(object target, string fieldId, UnityEngine.Object source) =>
            Bindings(target.GetType()).Fields[fieldId].BindResourceAsset(target, source);

        public static Func<object, IReadOnlyDictionary<string, object>, bool> Visibility(
            Type type, string fieldId, string expected)
        {
            TypeBinding bindings = Bindings(type);
            MemberBinding binding = bindings.Fields[fieldId];
            Type valueType = binding.Read[binding.Read.Length - 1].PropertyType;
            object expectedValue = valueType.IsEnum
                ? Enum.Parse(valueType, expected)
                : Convert.ChangeType(expected, valueType, System.Globalization.CultureInfo.InvariantCulture);
            ParameterExpression target = Expression.Parameter(typeof(object), "target");
            ParameterExpression candidates = Expression.Parameter(typeof(IReadOnlyDictionary<string, object>), "candidates");
            Expression value;
            if (string.IsNullOrEmpty(binding.Field.ReadOperation))
                value = CandidateRead(type, binding, target, candidates);
            else
            {
                MethodInfo[] operations = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                    .Where(method => method.Name == binding.Field.ReadOperation).ToArray();
                if (operations.Length != 1 || operations[0].ReturnType != valueType)
                    throw new InvalidOperationException($"作者字段 '{type.FullName}:{fieldId}' 的公开读取操作 '{binding.Field.ReadOperation}' 与读取类型不一致。");
                ParameterInfo[] parameters = operations[0].GetParameters();
                var arguments = new Expression[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    MemberBinding[] fields = bindings.Fields.Values
                        .Where(field => field.Field.FieldId == parameters[i].Name).ToArray();
                    if (fields.Length != 1 || !parameters[i].ParameterType.IsAssignableFrom(fields[0].Read[fields[0].Read.Length - 1].PropertyType))
                        throw new InvalidOperationException($"作者字段 '{type.FullName}:{fieldId}' 的读取操作参数 '{parameters[i].Name}' 没有唯一匹配的字段声明。");
                    arguments[i] = CandidateRead(type, fields[0], target, candidates);
                }
                value = Expression.Call(operations[0], arguments);
            }
            return Expression.Lambda<Func<object, IReadOnlyDictionary<string, object>, bool>>(
                Expression.Equal(value, Expression.Constant(expectedValue, valueType)), target, candidates).Compile();
        }

        static Expression CandidateRead(Type type, MemberBinding binding,
            ParameterExpression target, ParameterExpression candidates)
        {
            Expression current = ReadExpression(type, binding, target);
            ParameterExpression candidate = Expression.Variable(typeof(object), "candidate");
            return Expression.Block(new[] { candidate }, Expression.Condition(
                Expression.Call(candidates, typeof(IReadOnlyDictionary<string, object>).GetMethod(
                        nameof(IReadOnlyDictionary<string, object>.TryGetValue)),
                    Expression.Constant(binding.Field.FieldId), candidate),
                Expression.Convert(candidate, current.Type), current));
        }

        static Expression ReadExpression(Type type, MemberBinding binding, ParameterExpression target)
        {
            Expression current = Expression.Convert(target, type);
            foreach (PropertyInfo member in binding.Read)
                current = Expression.Property(current, member);
            return current;
        }

        public static bool TryRead(object node, string fieldId, out object value)
        {
            if (Bindings(node.GetType()).Fields.TryGetValue(fieldId, out MemberBinding binding))
            {
                value = binding.Value(node);
                return true;
            }
            value = null;
            return false;
        }

        public static IReadOnlyDictionary<string, object> ReadDefaults(Type type)
        {
            object defaults = Activator.CreateInstance(type);
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (MemberBinding binding in Bindings(type).Fields.Values)
                if (!string.IsNullOrEmpty(binding.Field.DefaultMember))
                {
                    object value = defaults;
                    foreach (string name in binding.Field.DefaultMember.Split('.'))
                        value = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).GetValue(value);
                    values.Add(binding.Field.FieldId, value);
                }
            return values;
        }

        public static void Apply(object node, IReadOnlyDictionary<string, object> values)
        {
            TypeBinding bindings = Bindings(node.GetType());
            foreach (OperationBinding operation in bindings.Operations)
            {
                if (!operation.Fields.Any(field => values.ContainsKey(field.Field.FieldId)))
                    continue;
                operation.Invoke(node, operation.Arguments.Select(argument => argument(node, values)).ToArray());
            }
        }
    }
}
#endif
