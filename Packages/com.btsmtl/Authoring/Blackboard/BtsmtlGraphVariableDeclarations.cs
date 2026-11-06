using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Graphs
{
    public sealed partial class BtsmtlGraphVariableDeclaration
    {
        public BtsmtlGraphVariableDeclaration() { }

        public BtsmtlGraphVariableDeclaration(BtsmtlBlackboardDeclaration declaration, string name,
            string typeId, JToken defaultValue, bool isPublic, int declarationOrder)
        {
            Declaration = declaration; Name = name; TypeId = typeId;
            DefaultValue = defaultValue.DeepClone(); IsPublic = isPublic; DeclarationOrder = declarationOrder;
        }

        [GraphAuthoringField("declaration", GraphAuthoringFieldValueKind.Object, ReadOnly = true)]
        public BtsmtlBlackboardDeclaration Declaration;
        [GraphAuthoringField("name", GraphAuthoringFieldValueKind.String, ReadOnly = true)]
        public string Name;
        [GraphAuthoringField("type", GraphAuthoringFieldValueKind.String, ReadOnly = true)]
        public string TypeId;
        [GraphAuthoringField("default", GraphAuthoringFieldValueKind.Object, ReadOnly = true)]
        public JToken DefaultValue;
        [GraphAuthoringField("public", GraphAuthoringFieldValueKind.Boolean, ReadOnly = true)]
        public bool IsPublic;
        [GraphAuthoringField("declarationOrder", GraphAuthoringFieldValueKind.Integer, ReadOnly = true)]
        public int DeclarationOrder;

        public string VariableId => Declaration.VariableId;
        public CompileValueType ValueType => CompileValueTypes.FromId(TypeId);
    }

    public sealed partial class BtsmtlGraphVariableDeclarations
    {
        string m_OwnerId;
        string m_Revision;
        BtsmtlGraphVariableDeclaration[] m_Variables = Array.Empty<BtsmtlGraphVariableDeclaration>();

        public BtsmtlGraphVariableDeclarations() { }

        public BtsmtlGraphVariableDeclarations(string ownerId, string revision,
            IReadOnlyList<BtsmtlGraphVariableDeclaration> variables)
        {
            ConfigureIdentity(ownerId, revision);
            RequireVariables(variables);
            m_Variables = variables.ToArray();
        }

        [GraphAuthoringField("ownerId", GraphAuthoringFieldValueKind.IdentityReference, ReadOnly = true)]
        public string OwnerId => m_OwnerId;
        [GraphAuthoringField("revision", GraphAuthoringFieldValueKind.String, ReadOnly = true)]
        public string Revision => m_Revision;
        [GraphAuthoringField("variables", GraphAuthoringFieldValueKind.Object, ReadOnly = true)]
        public BtsmtlGraphVariableDeclaration[] Variables => m_Variables;
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
            if (m_Variables.Length == variables.Count &&
                m_Variables.Zip(variables, BtsmtlGraphVariableDeclarationsJson.SameVariable).All(same => same))
                return;
            RequireVariables(variables);
            m_Variables = variables.ToArray();
            m_Revision = Guid.NewGuid().ToString("N");
        }

        public void ConfigureVariable(string id, bool isPublic)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            if (variable.IsPublic == isPublic)
                return;
            variable.IsPublic = isPublic;
            m_Revision = Guid.NewGuid().ToString("N");
        }

        public void ConfigureBlackboard(string id, PipelineBlackboardVariableScope scope,
            PipelineBlackboardVariableLifetime lifetime, string category = "", PipelineBlackboardInputBinding inputBinding = null)
        {
            BtsmtlGraphVariableDeclaration variable = RequireVariable(id);
            var declaration = new BtsmtlBlackboardDeclaration(id, scope, lifetime, category, inputBinding);
            BtsmtlBlackboardDeclaration.ValidateDefinition(variable.ValueType.Kind,
                BtsmtlGraphVariableDeclarationsJson.TypeCode(variable.ValueType), scope, lifetime, inputBinding);
            if (BtsmtlGraphVariableDeclarationsJson.SameDeclaration(variable.Declaration, declaration))
                return;
            variable.Declaration = declaration;
            m_Revision = Guid.NewGuid().ToString("N");
        }

        public void Prune(IEnumerable<string> ids)
        {
            var keep = new HashSet<string>(ids, StringComparer.Ordinal);
            Replace(m_Variables.Where(variable => keep.Contains(variable.VariableId)).ToArray());
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
