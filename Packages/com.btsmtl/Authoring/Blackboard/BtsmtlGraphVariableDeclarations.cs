using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Graphs
{
    public sealed partial class BtsmtlGraphVariableDeclaration
    {
        readonly BtsmtlBlackboardDeclaration m_Declaration;
        readonly string m_Name;
        readonly string m_TypeId;
        readonly JToken m_DefaultValue;
        readonly bool m_IsPublic;
        readonly int m_DeclarationOrder;
        readonly OrchestrationObjectKind m_ObjectKind;

        public BtsmtlGraphVariableDeclaration() { }

        // 句柄变量必须给出对象种类，非句柄变量不能给出。
        public BtsmtlGraphVariableDeclaration(BtsmtlBlackboardDeclaration declaration, string name,
            string typeId, JToken defaultValue, bool isPublic, int declarationOrder, OrchestrationObjectKind objectKind)
        {
            bool handle = CompileValueTypes.FromId(typeId).Kind == SemanticValueKind.ObjectHandle;
            if (handle != (objectKind != null))
                throw new InvalidDataException(handle ? $"对象句柄变量 '{name}' 必须声明对象种类。"
                    : $"变量 '{name}' 不是对象句柄，不能声明对象种类。");
            m_Declaration = declaration; m_Name = name; m_TypeId = typeId;
            m_DefaultValue = defaultValue.DeepClone(); m_IsPublic = isPublic; m_DeclarationOrder = declarationOrder;
            m_ObjectKind = objectKind;
        }

        [GraphAuthoringField("declaration", GraphAuthoringFieldValueKind.Object, ReadOnly = true)]
        public BtsmtlBlackboardDeclaration Declaration => m_Declaration;
        [GraphAuthoringField("name", GraphAuthoringFieldValueKind.String, ReadOnly = true)]
        public string Name => m_Name;
        [GraphAuthoringField("type", GraphAuthoringFieldValueKind.String, ReadOnly = true)]
        public string TypeId => m_TypeId;
        [GraphAuthoringField("default", GraphAuthoringFieldValueKind.Object, ReadOnly = true)]
        public JToken DefaultValue => m_DefaultValue.DeepClone();
        [GraphAuthoringField("public", GraphAuthoringFieldValueKind.Boolean, ReadOnly = true)]
        public bool IsPublic => m_IsPublic;
        [GraphAuthoringField("declarationOrder", GraphAuthoringFieldValueKind.Integer, ReadOnly = true)]
        public int DeclarationOrder => m_DeclarationOrder;
        public OrchestrationObjectKind ObjectKind => m_ObjectKind;
        [GraphAuthoringField("objectKind", GraphAuthoringFieldValueKind.String, ReadOnly = true, Optional = true)]
        public string ObjectKindId => m_ObjectKind?.Id;

        public string VariableId => Declaration.VariableId;
        public CompileValueType ValueType => CompileValueTypes.FromId(TypeId).WithObjectKind(m_ObjectKind);

        // 按值类型重建声明时的种类：投影固定种类优先，其次保留原种类，新句柄为任意对象；非句柄为 null。
        public static OrchestrationObjectKind ObjectKindFor(string typeId, BtsmtlBlackboardDeclaration declaration,
            OrchestrationObjectKind current)
        {
            if (CompileValueTypes.FromId(typeId).Kind != SemanticValueKind.ObjectHandle)
                return null;
            return (declaration.StoredInputBinding is { } binding
                ? PipelineBlackboardInputProjections.ObjectKind(binding.Projection) : null) ?? current ?? OrchestrationObjectKind.Any;
        }

        internal BtsmtlGraphVariableDeclaration With(BtsmtlBlackboardDeclaration declaration = null, string name = null,
            JToken defaultValue = null, bool? isPublic = null, OrchestrationObjectKind objectKind = null) =>
            new(declaration ?? m_Declaration, name ?? m_Name, m_TypeId, defaultValue ?? m_DefaultValue,
                isPublic ?? m_IsPublic, m_DeclarationOrder, objectKind ?? m_ObjectKind);
    }

    // 按所属身份读取变量声明，供端口形状解析变量访问节点的句柄种类。
    public sealed class BtsmtlGraphVariableKinds : IGraphNodeVariableResolver
    {
        readonly Func<string, BtsmtlGraphVariableDeclarations> m_Variables;

        public BtsmtlGraphVariableKinds(Func<string, BtsmtlGraphVariableDeclarations> variables) =>
            m_Variables = variables ?? throw new ArgumentNullException(nameof(variables));

        public OrchestrationObjectKind VariableObjectKind(string ownerId, string variableId) =>
            string.IsNullOrEmpty(ownerId) ? null : m_Variables(ownerId).Variables
                .FirstOrDefault(variable => variable.VariableId == variableId)?.ObjectKind;
    }

    public sealed partial class BtsmtlGraphVariableDeclarations
    {
        string m_OwnerId;
        string m_Revision;
        IReadOnlyList<BtsmtlGraphVariableDeclaration> m_Variables = Array.Empty<BtsmtlGraphVariableDeclaration>();

        public BtsmtlGraphVariableDeclarations() { }

        public BtsmtlGraphVariableDeclarations(string ownerId, string revision,
            IReadOnlyList<BtsmtlGraphVariableDeclaration> variables)
        {
            ConfigureIdentity(ownerId, revision);
            RequireVariables(variables);
            m_Variables = Array.AsReadOnly(variables.ToArray());
        }

        [GraphAuthoringField("ownerId", GraphAuthoringFieldValueKind.IdentityReference, ReadOnly = true)]
        public string OwnerId => m_OwnerId;
        [GraphAuthoringField("revision", GraphAuthoringFieldValueKind.String, ReadOnly = true)]
        public string Revision => m_Revision;
        public GraphExecutionDependency CompilationDependency => new(OwnerId, Revision, GraphExecutionDependencyKind.GraphVariables);
        [GraphAuthoringField("variables", GraphAuthoringFieldValueKind.Object, ReadOnly = true)]
        public IReadOnlyList<BtsmtlGraphVariableDeclaration> Variables => m_Variables;
        public IReadOnlyList<BtsmtlBlackboardDeclaration> BlackboardDeclarations => m_Variables
            .OrderBy(variable => variable.DeclarationOrder).Select(variable => variable.Declaration).ToArray();

        public BtsmtlGraphVariableDeclaration RequireVariable(string variableId) =>
            m_Variables.Single(variable => variable.VariableId == variableId);
        public BtsmtlBlackboardDeclaration RequireDeclaration(string variableId) => RequireVariable(variableId).Declaration;

        public void ConfigureIdentity(string id, string revision)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(revision))
                throw new ArgumentException("变量声明必须指定所属身份与修订。");
            m_OwnerId = id;
            m_Revision = revision;
        }

        public void Replace(IReadOnlyList<BtsmtlGraphVariableDeclaration> variables)
        {
            if (m_Variables.Count == variables.Count &&
                m_Variables.Zip(variables, BtsmtlGraphVariableDeclarationsJson.SameVariable).All(same => same))
                return;
            RequireVariables(variables);
            Commit(variables);
        }

        void Commit(IReadOnlyList<BtsmtlGraphVariableDeclaration> variables)
        {
            m_Variables = Array.AsReadOnly(variables.ToArray());
            m_Revision = Guid.NewGuid().ToString("N");
        }

        // 句柄变量未指定种类时取“任意对象”，作者随后可收窄。
        public BtsmtlGraphVariableDeclaration Create(string typeId, OrchestrationObjectKind objectKind = null)
        {
            CompileValueType type = CompileValueTypes.FromId(typeId);
            string label = "New " + type.FullName.Substring(type.FullName.LastIndexOf('.') + 1);
            string name = label;
            int suffix = 2;
            while (m_Variables.Any(variable => variable.Name == name))
                name = label + " " + suffix++;
            return Create(typeId, name, BtsmtlGraphVariableDeclarationsJson.CreateDefault(type), objectKind);
        }

        public BtsmtlGraphVariableDeclaration Create(string typeId, string name, OrchestrationObjectKind objectKind = null) =>
            Create(typeId, name, BtsmtlGraphVariableDeclarationsJson.CreateDefault(CompileValueTypes.FromId(typeId)), objectKind);

        public BtsmtlGraphVariableDeclaration Create(string typeId, string name, JToken defaultValue,
            OrchestrationObjectKind objectKind = null)
        {
            RequireName(name, null);
            CompileValueType type = CompileValueTypes.FromId(typeId);
            BtsmtlGraphVariableDeclarationsJson.RequireDefault(type, defaultValue);
            string id = Guid.NewGuid().ToString("N");
            int order = m_Variables.Count == 0 ? 0 : m_Variables.Max(variable => variable.DeclarationOrder) + 1;
            var declaration = new BtsmtlBlackboardDeclaration(id, PipelineBlackboardVariableScope.Graph,
                PipelineBlackboardVariableLifetime.GraphInstance);
            var variable = new BtsmtlGraphVariableDeclaration(declaration, name, typeId, defaultValue, false, order,
                type.Kind == SemanticValueKind.ObjectHandle ? objectKind ?? OrchestrationObjectKind.Any : objectKind);
            Commit(m_Variables.Append(variable).ToArray());
            return variable;
        }

        public void Rename(string id, string name)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            if (variable.Name == name)
                return;
            RequireName(name, id);
            ReplaceVariable(variable, variable.With(name: name));
        }

        public void SetDefaultValue(string id, JToken defaultValue)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            BtsmtlGraphVariableDeclarationsJson.RequireDefault(variable.ValueType, defaultValue);
            if (BtsmtlGraphVariableDeclarationsJson.SameDefault(variable.ValueType, variable.DefaultValue, defaultValue))
                return;
            ReplaceVariable(variable, variable.With(defaultValue: defaultValue));
        }

        public void SetObjectKind(string id, OrchestrationObjectKind objectKind)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            if (variable.ObjectKind == null)
                throw new InvalidDataException($"变量 '{variable.Name}' 不是对象句柄，不能声明对象种类。");
            if (ReferenceEquals(variable.ObjectKind, objectKind ?? throw new ArgumentNullException(nameof(objectKind))))
                return;
            BtsmtlGraphVariableDeclaration next = variable.With(objectKind: objectKind);
            RequireProjectedKind(next);
            ReplaceVariable(variable, next);
        }

        public void Delete(string id)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            Commit(m_Variables.Where(current => current != variable).ToArray());
        }

        void RequireName(string name, string exceptId)
        {
            if (string.IsNullOrWhiteSpace(name) || m_Variables.Any(variable => variable.Name == name && variable.VariableId != exceptId))
                throw new InvalidDataException("变量名称必须非空，且在所属来源中唯一。");
        }

        void ReplaceVariable(BtsmtlGraphVariableDeclaration previous, BtsmtlGraphVariableDeclaration next) =>
            Commit(m_Variables.Select(variable => variable == previous ? next : variable).ToArray());

        public void ConfigureVariable(string id, bool isPublic)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            if (variable.IsPublic == isPublic)
                return;
            ReplaceVariable(variable, variable.With(isPublic: isPublic));
        }

        public void ConfigureBlackboard(string id, JObject changes)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            BtsmtlBlackboardDeclaration previous = variable.Declaration;
            BtsmtlBlackboardDeclaration declaration = BtsmtlGraphVariableDeclarationsJson.ConfigureDeclaration(previous, changes);
            if (BtsmtlGraphVariableDeclarationsJson.SameDeclaration(previous, declaration))
                return;
            bool bindingChanged = !BtsmtlGraphVariableDeclarationsJson.SameInputBinding(previous.StoredInputBinding, declaration.StoredInputBinding);
            if (previous.Scope != declaration.Scope || previous.Lifetime != declaration.Lifetime || bindingChanged)
                BtsmtlBlackboardDeclaration.ValidateDefinition(variable.ValueType.Kind,
                    BtsmtlGraphVariableDeclarationsJson.TypeCode(variable.ValueType), declaration.Scope, declaration.Lifetime,
                    bindingChanged ? declaration.StoredInputBinding : declaration.InputBinding);
            // 句柄投影固定变量种类。
            OrchestrationObjectKind projected = declaration.StoredInputBinding is { } binding
                ? PipelineBlackboardInputProjections.ObjectKind(binding.Projection) : null;
            ReplaceVariable(variable, variable.With(declaration: declaration, objectKind: projected));
        }

        public void Prune(IEnumerable<string> ids)
        {
            var keep = new HashSet<string>(ids, StringComparer.Ordinal);
            BtsmtlGraphVariableDeclaration[] retained = m_Variables.Where(variable => keep.Contains(variable.VariableId)).ToArray();
            if (retained.Length != m_Variables.Count)
                Commit(retained);
        }

        static void RequireVariables(IReadOnlyList<BtsmtlGraphVariableDeclaration> variables)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var declarationOrders = new HashSet<int>();
            foreach (BtsmtlGraphVariableDeclaration variable in variables)
            {
                if (!ids.Add(variable.VariableId) || string.IsNullOrWhiteSpace(variable.Name) ||
                    !names.Add(variable.Name) || variable.DeclarationOrder < 0 ||
                    !declarationOrders.Add(variable.DeclarationOrder))
                    throw new InvalidDataException("变量身份、名称与声明顺序必须唯一，名称不能为空。");
                CompileValueType type = variable.ValueType;
                BtsmtlGraphVariableDeclarationsJson.RequireDefault(type, variable.DefaultValue);
                BtsmtlBlackboardDeclaration declaration = variable.Declaration;
                BtsmtlBlackboardDeclaration.ValidateDefinition(type.Kind,
                    BtsmtlGraphVariableDeclarationsJson.TypeCode(type),
                    declaration.Scope, declaration.Lifetime, declaration.InputBinding);
                RequireProjectedKind(variable);
            }
        }

        static void RequireProjectedKind(BtsmtlGraphVariableDeclaration variable)
        {
            if (variable.Declaration.StoredInputBinding is not { } binding ||
                PipelineBlackboardInputProjections.ObjectKind(binding.Projection) is not { } projected ||
                ReferenceEquals(projected, variable.ObjectKind))
                return;
            throw new InvalidDataException($"变量 '{variable.Name}' 绑定输入投影 {binding.Projection}，" +
                $"对象种类固定为{projected.DisplayName}，实际为{variable.ObjectKind?.DisplayName ?? "未声明"}。");
        }
    }
}
