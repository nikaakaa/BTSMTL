using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
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

        public BtsmtlGraphVariableDeclaration() { }

        public BtsmtlGraphVariableDeclaration(BtsmtlBlackboardDeclaration declaration, string name,
            string typeId, JToken defaultValue, bool isPublic, int declarationOrder)
        {
            m_Declaration = declaration; m_Name = name; m_TypeId = typeId;
            m_DefaultValue = defaultValue.DeepClone(); m_IsPublic = isPublic; m_DeclarationOrder = declarationOrder;
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

        public string VariableId => Declaration.VariableId;
        public CompileValueType ValueType => CompileValueTypes.FromId(TypeId);
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

        public BtsmtlGraphVariableDeclaration Create(string typeId)
        {
            CompileValueType type = CompileValueTypes.FromId(typeId);
            string label = "New " + type.FullName.Substring(type.FullName.LastIndexOf('.') + 1);
            string name = label;
            int suffix = 2;
            while (m_Variables.Any(variable => variable.Name == name))
                name = label + " " + suffix++;
            return Create(typeId, name, BtsmtlGraphVariableDeclarationsJson.CreateDefault(type));
        }

        public BtsmtlGraphVariableDeclaration Create(string typeId, string name) =>
            Create(typeId, name, BtsmtlGraphVariableDeclarationsJson.CreateDefault(CompileValueTypes.FromId(typeId)));

        public BtsmtlGraphVariableDeclaration Create(string typeId, string name, JToken defaultValue)
        {
            RequireName(name, null);
            BtsmtlGraphVariableDeclarationsJson.RequireDefault(CompileValueTypes.FromId(typeId), defaultValue);
            string id = Guid.NewGuid().ToString("N");
            int order = m_Variables.Count == 0 ? 0 : m_Variables.Max(variable => variable.DeclarationOrder) + 1;
            var declaration = new BtsmtlBlackboardDeclaration(id, PipelineBlackboardVariableScope.Graph,
                PipelineBlackboardVariableLifetime.GraphInstance);
            var variable = new BtsmtlGraphVariableDeclaration(declaration, name, typeId, defaultValue, false, order);
            Commit(m_Variables.Append(variable).ToArray());
            return variable;
        }

        public void Rename(string id, string name)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            if (variable.Name == name)
                return;
            RequireName(name, id);
            ReplaceVariable(variable, new BtsmtlGraphVariableDeclaration(variable.Declaration, name, variable.TypeId,
                variable.DefaultValue, variable.IsPublic, variable.DeclarationOrder));
        }

        public void SetDefaultValue(string id, JToken defaultValue)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            BtsmtlGraphVariableDeclarationsJson.RequireDefault(variable.ValueType, defaultValue);
            if (BtsmtlGraphVariableDeclarationsJson.SameDefault(variable.ValueType, variable.DefaultValue, defaultValue))
                return;
            ReplaceVariable(variable, new BtsmtlGraphVariableDeclaration(variable.Declaration, variable.Name,
                variable.TypeId, defaultValue, variable.IsPublic, variable.DeclarationOrder));
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
            ReplaceVariable(variable, new BtsmtlGraphVariableDeclaration(variable.Declaration, variable.Name,
                variable.TypeId, variable.DefaultValue, isPublic, variable.DeclarationOrder));
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
            ReplaceVariable(variable, new BtsmtlGraphVariableDeclaration(declaration, variable.Name,
                variable.TypeId, variable.DefaultValue, variable.IsPublic, variable.DeclarationOrder));
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
            }
        }
    }
}
