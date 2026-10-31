using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BTSMTL.Orchestration;

namespace BTSMTL.Authoring.Graph
{
    // 节点端口的统一描述：Unity 编辑、JSON 读取、独立编译、网页投影与运行计划生成读取同一结果。
    // 值类型使用作者值类型身份（bool、float、vector3、"Enum:" 加程序集限定名等）；流程端口为 "flow"。
    public sealed class GraphNodePort
    {
        public GraphNodePort(GraphAuthoringPortDirection direction, bool isFlow, string id, string displayName,
            string valueTypeId, int order, GraphAuthoringPortCapacity capacity = 0, bool required = false,
            bool hasDefault = false, object defaultValue = null, string defaultFieldId = null,
            string interfacePortId = null, OrchestrationExtensionPort? contractPort = null, object annotation = null)
        {
            if (order < 0)
                throw new ArgumentOutOfRangeException(nameof(order));
            Direction = direction;
            IsFlow = isFlow;
            Id = GraphAuthoringIdentity.Require(id, nameof(id));
            DisplayName = displayName ?? string.Empty;
            ValueTypeId = isFlow ? Flow : GraphAuthoringIdentity.Require(valueTypeId, nameof(valueTypeId));
            Order = order;
            Capacity = capacity != 0 ? capacity
                : IsInput == isFlow ? GraphAuthoringPortCapacity.Multiple : GraphAuthoringPortCapacity.Single;
            Required = required;
            HasDefault = hasDefault;
            DefaultValue = defaultValue;
            DefaultFieldId = defaultFieldId ?? string.Empty;
            InterfacePortId = interfacePortId ?? string.Empty;
            ContractPort = contractPort;
            Annotation = annotation;
        }

        public const string Flow = "flow";

        public GraphAuthoringPortDirection Direction { get; }
        public bool IsFlow { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public string ValueTypeId { get; }
        public int Order { get; }
        public GraphAuthoringPortCapacity Capacity { get; }
        public bool Required { get; }
        public bool HasDefault { get; }
        public object DefaultValue { get; }
        // 非空时未连接输入取该作者字段的当前值。
        public string DefaultFieldId { get; }
        public string InterfacePortId { get; }
        // 绑定运行合同的端口；identity、值类型与方向取自合同。
        public OrchestrationExtensionPort? ContractPort { get; }
        // 领域补充语义（如 Pose 的空间、语义与阶段），共享层不解释。
        public object Annotation { get; }
        public bool IsInput => Direction == GraphAuthoringPortDirection.Input;

        public GraphNodePort WithOrder(int order) => order == Order ? this : new GraphNodePort(Direction, IsFlow, Id,
            DisplayName, ValueTypeId, order, Capacity, Required, HasDefault, DefaultValue, DefaultFieldId,
            InterfacePortId, ContractPort, Annotation);
    }

    // 节点固定端口：类级特性按书写顺序产出端口。EnabledBy 指向节点上返回 bool 的成员，为 false 时不产出。
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public abstract class GraphNodePortAttribute : Attribute
    {
        protected GraphNodePortAttribute(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string EnabledBy { get; set; }

        // 节点类型级解析；返回按节点数据产出端口的函数。声明错误直接抛出，由目录建立时报告。
        protected internal abstract Func<object, GraphNodePort> Bind(Type nodeType);

        // 领域特性按成员名（nameof）读取节点数据，成员类型须可赋给 expected。
        protected static Func<object, object> Member(Type nodeType, string name, Type expected) =>
            GraphNodeMembers.Reader(nodeType, name, expected);
    }

    public sealed class FlowInputAttribute : GraphNodePortAttribute
    {
        public FlowInputAttribute(string id, string displayName) : base(id, displayName) { }

        protected internal override Func<object, GraphNodePort> Bind(Type nodeType)
        {
            var port = new GraphNodePort(GraphAuthoringPortDirection.Input, true, Id, DisplayName, null, 0);
            return _ => port;
        }
    }

    public sealed class FlowOutputAttribute : GraphNodePortAttribute
    {
        public FlowOutputAttribute(string id, string displayName) : base(id, displayName) { }

        protected internal override Func<object, GraphNodePort> Bind(Type nodeType)
        {
            var port = new GraphNodePort(GraphAuthoringPortDirection.Output, true, Id, DisplayName, null, 0);
            return _ => port;
        }
    }

    // 值端口：ValueType 给出 CLR 类型；TypeFrom 指向节点上返回 CLR 类型或作者值类型身份的成员，二者其一。
    public abstract class GraphNodeValuePortAttribute : GraphNodePortAttribute
    {
        protected GraphNodeValuePortAttribute(string id, string displayName, Type valueType) : base(id, displayName) =>
            ValueType = valueType;

        public Type ValueType { get; }
        public string TypeFrom { get; set; }

        protected Func<object, string> BindValueType(Type nodeType)
        {
            if ((ValueType == null) == string.IsNullOrEmpty(TypeFrom))
                throw new InvalidOperationException($"端口 '{Id}' 必须且只能声明 ValueType 或 TypeFrom 之一。");
            if (ValueType != null)
            {
                string id = GraphNodeValueTypes.Of(ValueType);
                return _ => id;
            }
            Func<object, object> read = GraphNodeMembers.Reader(nodeType, TypeFrom, typeof(object));
            return node => read(node) switch
            {
                Type type => GraphNodeValueTypes.Of(type),
                string value => value,
                var value => throw new InvalidOperationException($"成员 '{TypeFrom}' 返回的 '{value}' 不是端口值类型。")
            };
        }
    }

    public sealed class ValueInputAttribute : GraphNodeValuePortAttribute
    {
        object m_Default;

        public ValueInputAttribute(string id, string displayName, Type valueType = null) : base(id, displayName, valueType) { }

        public object Default
        {
            get => m_Default;
            set
            {
                m_Default = value;
                HasDefault = true;
            }
        }

        public bool HasDefault { get; private set; }

        // 未连线时取该作者字段（节点成员名，用 nameof 引用）的当前值；省略值类型时取该成员的 CLR 类型。
        public string DefaultField { get; set; }

        protected internal override Func<object, GraphNodePort> Bind(Type nodeType)
        {
            if (!string.IsNullOrEmpty(DefaultField))
            {
                if (HasDefault || !string.IsNullOrEmpty(TypeFrom))
                    throw new InvalidOperationException($"端口 '{Id}' 的 DefaultField 不能与 Default 或 TypeFrom 同时声明。");
                (string fieldId, Type fieldType) = GraphNodeMembers.AuthoringField(nodeType, DefaultField);
                var fieldPort = new GraphNodePort(GraphAuthoringPortDirection.Input, false, Id, DisplayName,
                    GraphNodeValueTypes.Of(ValueType ?? fieldType), 0, defaultFieldId: fieldId);
                return _ => fieldPort;
            }
            Func<object, string> valueType = BindValueType(nodeType);
            if (ValueType != null)
            {
                var port = new GraphNodePort(GraphAuthoringPortDirection.Input, false, Id, DisplayName, valueType(null), 0,
                    hasDefault: HasDefault, defaultValue: GraphNodeValueTypes.Convert(Default, ValueType));
                return _ => port;
            }
            return node => new GraphNodePort(GraphAuthoringPortDirection.Input, false, Id, DisplayName, valueType(node), 0);
        }
    }

    public sealed class ValueOutputAttribute : GraphNodeValuePortAttribute
    {
        public ValueOutputAttribute(string id, string displayName, Type valueType = null) : base(id, displayName, valueType) { }

        protected internal override Func<object, GraphNodePort> Bind(Type nodeType)
        {
            Func<object, string> valueType = BindValueType(nodeType);
            if (ValueType != null)
            {
                var port = new GraphNodePort(GraphAuthoringPortDirection.Output, false, Id, DisplayName, valueType(null), 0);
                return _ => port;
            }
            return node => new GraphNodePort(GraphAuthoringPortDirection.Output, false, Id, DisplayName, valueType(node), 0);
        }
    }

    // 领域端口集合：一个类级声明按节点类型产出多个端口，用于由领域字段特性逐项展开的端口（如 Rig 参数）。
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public abstract class GraphNodePortSetAttribute : Attribute
    {
        protected internal abstract Func<object, IEnumerable<GraphNodePort>> Bind(Type nodeType);
    }

    // 节点执行的扩展操作：合同类型上类型为 IOrchestrationExtensionDefinition 的静态成员给出扩展定义。
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class GraphNodeExtensionAttribute : Attribute
    {
        public GraphNodeExtensionAttribute(Type contract) => Contract = contract;

        public Type Contract { get; }
    }

    // 绑定运行合同端口：Member 用 nameof 引用合同类型上的端口成员；identity、值类型与方向取自合同，节点只补显示名与默认值。
    public sealed class ContractPortAttribute : GraphNodePortAttribute
    {
        object m_Default;

        public ContractPortAttribute(string member, string displayName) : base(member, displayName) { }

        public object Default
        {
            get => m_Default;
            set
            {
                m_Default = value;
                HasDefault = true;
            }
        }

        public bool HasDefault { get; private set; }

        protected internal override Func<object, GraphNodePort> Bind(Type nodeType)
        {
            OrchestrationExtensionPort contract = GraphNodeContracts.Port(nodeType, Id);
            bool input = contract.Direction == OrchestrationExtensionPortDirection.Input;
            if (HasDefault && !input)
                throw new InvalidOperationException($"合同输出端口 '{Id}' 不能声明未连线默认值。");
            string valueType = GraphNodeValueTypes.Of(contract.ValueKind);
            var port = new GraphNodePort(input ? GraphAuthoringPortDirection.Input : GraphAuthoringPortDirection.Output,
                false, contract.Id, DisplayName, valueType, 0, hasDefault: HasDefault,
                defaultValue: HasDefault ? GraphNodeValueTypes.Convert(Default, GraphNodeValueTypes.Clr(valueType)) : null,
                contractPort: contract);
            return _ => port;
        }
    }

    // 列表成员逐项产出流程输出端口，identity 与显示名取自列表项；Numbered 时显示名前加序号。
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class FlowOutputListAttribute : Attribute
    {
        public bool Numbered { get; set; }
    }

    public interface IGraphNodePortItem
    {
        string Id { get; }
        string Name { get; }
    }

    // 子图引用成员：端口由被引用图的接口声明推导，调用端口 identity 与接口端口 identity 一致。
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class PortsFromGraphAttribute : Attribute { }

    // 接口声明节点的接口项列表：列表项自身给出作者编辑的端口项。
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class GraphInterfaceListAttribute : Attribute { }

    public interface IGraphNodeInterfaceItem
    {
        GraphNodePort Port { get; }
    }

    // 被引用图接口的解析由领域提供；未绑定被引用图时抛出并说明缺少的绑定。
    public interface IGraphNodeInterfaceResolver
    {
        IReadOnlyList<GraphNodePort> Interface(object reference);
    }

    // CLR 类型声明自身的作者值类型身份；基础类型由生成器内置。
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Enum, Inherited = false)]
    public sealed class GraphValueTypeAttribute : Attribute
    {
        public GraphValueTypeAttribute(string id) => Id = id;

        public string Id { get; }
    }

    public sealed class GraphNodePortDeclarationException : InvalidOperationException
    {
        public GraphNodePortDeclarationException(Type nodeType, string declaration, Exception inner)
            : base($"节点 '{nodeType.FullName}' 的端口声明 '{declaration}' 无效：{inner.Message}", inner)
        {
            NodeType = nodeType;
            Declaration = declaration;
        }

        public Type NodeType { get; }
        public string Declaration { get; }
    }

#if UNITY_EDITOR || BTSMTL_AUTHORING
    // 唯一端口生成器：节点类型级声明在首次读取时解析并缓存，按节点数据与被引用接口产出完整端口。
    public static class GraphNodePortShapes
    {
        static readonly ConcurrentDictionary<Type, Shape> s_Shapes = new();

        // 目录建立时调用，声明错误在此报告节点类型与具体声明。
        public static void Require(Type nodeType) => Shape.Of(nodeType);

        public static bool Declares(Type nodeType) => Shape.Of(nodeType).Sources.Length != 0;

        public static IReadOnlyList<GraphNodePort> Of(object node, IGraphNodeInterfaceResolver resolver = null)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node));
            Shape shape = Shape.Of(node.GetType());
            var ports = new List<GraphNodePort>();
            foreach (Func<object, IGraphNodeInterfaceResolver, IEnumerable<GraphNodePort>> source in shape.Sources)
                foreach (GraphNodePort port in source(node, resolver))
                {
                    foreach (GraphNodePort existing in ports)
                        if (existing.Id == port.Id && existing.Direction == port.Direction)
                            throw new InvalidOperationException(
                                $"节点 '{shape.NodeType.FullName}' 产出重复端口 '{port.Id}'（{port.Direction}）。");
                    ports.Add(port.WithOrder(ports.Count));
                }
            return ports;
        }

        public static GraphNodePort Input(IReadOnlyList<GraphNodePort> ports, string id) => Find(ports, id, true);
        public static GraphNodePort Output(IReadOnlyList<GraphNodePort> ports, string id) => Find(ports, id, false);

        static GraphNodePort Find(IReadOnlyList<GraphNodePort> ports, string id, bool input)
        {
            foreach (GraphNodePort port in ports)
                if (port.IsInput == input && port.Id == id)
                    return port;
            return null;
        }

        sealed class Shape
        {
            Shape(Type nodeType, Func<object, IGraphNodeInterfaceResolver, IEnumerable<GraphNodePort>>[] sources)
            {
                NodeType = nodeType;
                Sources = sources;
            }

            public Type NodeType { get; }
            public Func<object, IGraphNodeInterfaceResolver, IEnumerable<GraphNodePort>>[] Sources { get; }

            public static Shape Of(Type nodeType) => s_Shapes.GetOrAdd(nodeType ?? throw new ArgumentNullException(nameof(nodeType)), Create);

            static Shape Create(Type nodeType)
            {
                var sources = new List<Func<object, IGraphNodeInterfaceResolver, IEnumerable<GraphNodePort>>>();
                foreach (GraphNodePortAttribute attribute in nodeType.GetCustomAttributes<GraphNodePortAttribute>(true))
                    sources.Add(Declare(nodeType, $"{attribute.GetType().Name}({attribute.Id})", () =>
                    {
                        Func<object, GraphNodePort> port = attribute.Bind(nodeType);
                        if (string.IsNullOrEmpty(attribute.EnabledBy))
                            return (node, _) => new[] { port(node) };
                        Func<object, object> enabled = GraphNodeMembers.Reader(nodeType, attribute.EnabledBy, typeof(bool));
                        return (node, _) => (bool)enabled(node) ? new[] { port(node) } : Array.Empty<GraphNodePort>();
                    }));
                foreach (GraphNodePortSetAttribute set in nodeType.GetCustomAttributes<GraphNodePortSetAttribute>(true))
                    sources.Add(Declare(nodeType, set.GetType().Name, () =>
                    {
                        Func<object, IEnumerable<GraphNodePort>> ports = set.Bind(nodeType);
                        return (node, _) => ports(node);
                    }));
                foreach (MemberInfo member in GraphNodeMembers.Ordered(nodeType))
                {
                    if (member.GetCustomAttribute<FlowOutputListAttribute>(true) is { } list)
                        sources.Add(Declare(nodeType, $"FlowOutputList({member.Name})", () => FlowOutputs(nodeType, member, list.Numbered)));
                    if (member.IsDefined(typeof(PortsFromGraphAttribute), true))
                        sources.Add(Declare(nodeType, $"PortsFromGraph({member.Name})", () => FromGraph(nodeType, member)));
                    if (member.IsDefined(typeof(GraphInterfaceListAttribute), true))
                        sources.Add(Declare(nodeType, $"GraphInterfaceList({member.Name})", () => InterfaceItems(nodeType, member)));
                }
                if (nodeType.GetCustomAttribute<GraphNodeExtensionAttribute>(true) is { } extension)
                    Declare(nodeType, $"GraphNodeExtension({extension.Contract?.Name})", () =>
                    {
                        GraphNodeContracts.Definition(nodeType);
                        return null;
                    });
                return new Shape(nodeType, sources.ToArray());
            }

            static Func<object, IGraphNodeInterfaceResolver, IEnumerable<GraphNodePort>> Declare(Type nodeType, string declaration,
                Func<Func<object, IGraphNodeInterfaceResolver, IEnumerable<GraphNodePort>>> bind)
            {
                try
                {
                    return bind();
                }
                catch (Exception exception) when (exception is not GraphNodePortDeclarationException)
                {
                    throw new GraphNodePortDeclarationException(nodeType, declaration, exception);
                }
            }

            static Func<object, IGraphNodeInterfaceResolver, IEnumerable<GraphNodePort>> FlowOutputs(Type nodeType,
                MemberInfo member, bool numbered)
            {
                Func<object, object> read = GraphNodeMembers.Reader(nodeType, member.Name, typeof(IEnumerable));
                return (node, _) => Items(read(node)).Select((item, index) => new GraphNodePort(GraphAuthoringPortDirection.Output,
                    true, item.Id, numbered ? $"{index + 1}. {item.Name}" : item.Name, null, 0));

                IEnumerable<IGraphNodePortItem> Items(object value) => ((IEnumerable)value ?? Array.Empty<object>())
                    .Cast<object>().Select(item => item as IGraphNodePortItem ?? throw new InvalidOperationException(
                        $"成员 '{member.Name}' 的列表项未实现 {nameof(IGraphNodePortItem)}。"));
            }

            static Func<object, IGraphNodeInterfaceResolver, IEnumerable<GraphNodePort>> FromGraph(Type nodeType, MemberInfo member)
            {
                Func<object, object> read = GraphNodeMembers.Reader(nodeType, member.Name, typeof(object));
                return (node, resolver) => (resolver ?? throw new InvalidOperationException(
                    $"节点 '{nodeType.FullName}' 的端口来自被引用图，读取时必须提供接口解析。")).Interface(read(node));
            }

            static Func<object, IGraphNodeInterfaceResolver, IEnumerable<GraphNodePort>> InterfaceItems(Type nodeType, MemberInfo member)
            {
                Func<object, object> read = GraphNodeMembers.Reader(nodeType, member.Name, typeof(IEnumerable));
                return (node, _) => ((IEnumerable)read(node) ?? Array.Empty<object>()).Cast<object>().Select(item =>
                    (item as IGraphNodeInterfaceItem ?? throw new InvalidOperationException(
                        $"成员 '{member.Name}' 的列表项未实现 {nameof(IGraphNodeInterfaceItem)}。")).Port);
            }
        }
    }
#endif

    // 节点的扩展操作合同：由 GraphNodeExtension 特性指向的合同类型解析，按节点类型缓存。
    public static class GraphNodeContracts
    {
        static readonly ConcurrentDictionary<Type, IOrchestrationExtensionDefinition> s_Definitions = new();

        public static bool TryGetDefinition(Type nodeType, out IOrchestrationExtensionDefinition definition)
        {
            definition = nodeType?.GetCustomAttribute<GraphNodeExtensionAttribute>(true) != null ? Definition(nodeType) : null;
            return definition != null;
        }

        public static IOrchestrationExtensionDefinition Definition(Type nodeType) => s_Definitions.GetOrAdd(nodeType, type =>
        {
            Type contract = type.GetCustomAttribute<GraphNodeExtensionAttribute>(true)?.Contract ??
                throw new InvalidOperationException($"节点 '{type.FullName}' 没有声明扩展合同。");
            IOrchestrationExtensionDefinition[] definitions = contract
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => typeof(IOrchestrationExtensionDefinition).IsAssignableFrom(field.FieldType))
                .Select(field => (IOrchestrationExtensionDefinition)field.GetValue(null)).ToArray();
            return definitions.Length == 1 ? definitions[0]
                : throw new InvalidOperationException($"合同类型 '{contract.FullName}' 必须恰有一个扩展定义静态字段。");
        });

        internal static OrchestrationExtensionPort Port(Type nodeType, string member)
        {
            Definition(nodeType);
            Type contract = nodeType.GetCustomAttribute<GraphNodeExtensionAttribute>(true).Contract;
            FieldInfo field = contract.GetField(member, BindingFlags.Public | BindingFlags.Static) ??
                throw new InvalidOperationException($"合同类型 '{contract.FullName}' 没有端口成员 '{member}'。");
            return field.GetValue(null) is IOrchestrationExtensionPort port ? port.Declaration
                : throw new InvalidOperationException($"合同成员 '{contract.FullName}.{member}' 不是扩展端口。");
        }
    }

    public static class GraphNodeValueTypes
    {
        public static string Of(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (type.GetCustomAttribute<GraphValueTypeAttribute>(false) is { } declared)
                return declared.Id;
            if (type.IsEnum)
                return "Enum:" + type.AssemblyQualifiedName;
            return type.FullName switch
            {
                "System.Boolean" => "bool",
                "System.Int32" => "int",
                "System.UInt32" => "uint",
                "System.UInt64" => "ulong",
                "System.Single" => "float",
                "System.Double" => "double",
                "System.String" => "string",
                "UnityEngine.Vector2" or "System.Numerics.Vector2" => "vector2",
                "UnityEngine.Vector3" or "System.Numerics.Vector3" => "vector3",
                "UnityEngine.Quaternion" or "System.Numerics.Quaternion" => "quaternion",
                _ => throw new InvalidOperationException($"CLR 类型 '{type.FullName}' 没有作者值类型身份。")
            };
        }

        public static string Of(OrchestrationValueKind kind) => kind switch
        {
            OrchestrationValueKind.Boolean => "bool",
            OrchestrationValueKind.Int32 => "int",
            OrchestrationValueKind.UInt64 => "ulong",
            OrchestrationValueKind.Scalar or OrchestrationValueKind.Yaw => "float",
            OrchestrationValueKind.Vector2 => "vector2",
            OrchestrationValueKind.Vector3 => "vector3",
            OrchestrationValueKind.Identity => "string",
            OrchestrationValueKind.Quaternion => "quaternion",
            _ => throw new InvalidOperationException($"编排值种类 '{kind}' 没有作者值类型身份。")
        };

        internal static Type Clr(string valueTypeId) => valueTypeId switch
        {
            "bool" => typeof(bool),
            "int" => typeof(int),
            "uint" => typeof(uint),
            "ulong" => typeof(ulong),
            "float" => typeof(float),
            "double" => typeof(double),
            "string" => typeof(string),
            _ => null
        };

        // 特性参数只能写基础常量；默认值按端口 CLR 类型转换，避免 1 与 1f、0 与 0UL 的装箱类型不一致。
        internal static object Convert(object value, Type type)
        {
            if (value == null || type == null || type.IsInstanceOfType(value))
                return value;
            return System.Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    static class GraphNodeMembers
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // 自基类到派生类、同一类型内按声明顺序（元数据标记）排列的实例字段与属性。
        public static IEnumerable<MemberInfo> Ordered(Type type)
        {
            var chain = new Stack<Type>();
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                chain.Push(current);
            foreach (Type current in chain)
                foreach (MemberInfo member in current.GetMembers(Instance | BindingFlags.DeclaredOnly)
                             .Where(member => member is FieldInfo or PropertyInfo).OrderBy(member => member.MetadataToken))
                    yield return member;
        }

        // 节点成员上的作者字段：省略字段身份时按成员名首字母小写得出，与值生成器一致。
        public static (string FieldId, Type ValueType) AuthoringField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MemberInfo member = (MemberInfo)current.GetProperty(name, Instance | BindingFlags.DeclaredOnly) ??
                    current.GetField(name, Instance | BindingFlags.DeclaredOnly);
                if (member == null)
                    continue;
                GraphAuthoringFieldAttribute field = member.GetCustomAttribute<GraphAuthoringFieldAttribute>(true) ??
                    throw new InvalidOperationException($"成员 '{name}' 不是作者字段。");
                string id = field.FieldId.Length != 0 ? field.FieldId : char.ToLowerInvariant(name[0]) + name.Substring(1);
                return (id, member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType);
            }
            throw new InvalidOperationException($"节点没有成员 '{name}'。");
        }

        public static Func<object, object> Reader(Type type, string name, Type expected)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                if (current.GetProperty(name, Instance | BindingFlags.DeclaredOnly) is { } property)
                {
                    Require(property.PropertyType);
                    return property.GetValue;
                }
                if (current.GetField(name, Instance | BindingFlags.DeclaredOnly) is { } field)
                {
                    Require(field.FieldType);
                    return field.GetValue;
                }
            }
            throw new InvalidOperationException($"节点没有成员 '{name}'。");

            void Require(Type actual)
            {
                if (!expected.IsAssignableFrom(actual))
                    throw new InvalidOperationException($"成员 '{name}' 的类型 '{actual.Name}' 不是 '{expected.Name}'。");
            }
        }
    }
}
