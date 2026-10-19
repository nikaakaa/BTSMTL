using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs.Editor
{
    public sealed class BtsmtlBlackboardDeclarationSnapshot
    {
        public BtsmtlBlackboardDeclarationSnapshot(string identity, string key, Type valueType,
            PipelineBlackboardVariableScope scope, PipelineBlackboardVariableLifetime lifetime, string category,
            object defaultValue, PipelineBlackboardInputBinding inputBinding)
        {
            DeclarationId = identity;
            BlackboardKey = key;
            ValueType = valueType;
            BlackboardScope = scope;
            BlackboardLifetime = lifetime;
            BlackboardCategoryPath = category;
            DefaultValue = defaultValue;
            InputBinding = inputBinding;
        }

        public string DeclarationId { get; }
        public string BlackboardKey { get; }
        public Type ValueType { get; }
        public PipelineBlackboardVariableScope BlackboardScope { get; }
        public PipelineBlackboardVariableLifetime BlackboardLifetime { get; }
        public string BlackboardCategoryPath { get; }
        public object DefaultValue { get; }
        public PipelineBlackboardInputBinding InputBinding { get; }
        public string InputValueId => InputBinding?.InputValueId ?? string.Empty;
    }

    public sealed class BtsmtlBlackboardDeclarationSource
    {
        public BtsmtlBlackboardDeclarationSource(string graphId, string sourceType, string contentHash,
            object authoringDeclaration, BtsmtlBlackboardDeclarationSnapshot declaration, string route)
        {
            GraphId = graphId;
            SourceType = sourceType;
            ContentHash = contentHash;
            AuthoringDeclaration = authoringDeclaration;
            Declaration = declaration;
            Route = route;
        }

        public string GraphId { get; }
        public string SourceType { get; }
        public string ContentHash { get; }
        public object AuthoringDeclaration { get; }
        public BtsmtlBlackboardDeclarationSnapshot Declaration { get; }
        public string Route { get; }
    }

    // 领域在声明目录的通用字段（Key 至 Category）之后、输入绑定字段之前追加自己的字段。
    public delegate void BtsmtlBlackboardCatalogFields(BtsmtlBlackboardDeclarationSource declaration,
        SimulationSourceLocation source, List<ProgramCatalogField> fields);

    public sealed class BtsmtlBlackboardCompilation
    {
        readonly IReadOnlyDictionary<string, BtsmtlBlackboardDeclarationSource> m_Declarations;
        readonly BtsmtlGraphSemanticBuilder m_Builder;
        readonly IReadOnlyCollection<PipelineBlackboardVariableScope> m_Scopes;
        readonly BtsmtlBlackboardCatalogFields m_DomainFields;
        readonly bool m_ContentDefaults;
        readonly Dictionary<string, int> m_ValueSlots = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, ScopeRecord> m_ScopeRecords = new Dictionary<string, ScopeRecord>(StringComparer.Ordinal);
        readonly List<GraphRoute> m_GraphStack = new List<GraphRoute>();

        // contentDefaults 为 true 时作者默认值写入编排内容；为 false 时默认值由宿主的生成数据提供。
        public BtsmtlBlackboardCompilation(
            IReadOnlyDictionary<string, BtsmtlBlackboardDeclarationSource> declarations,
            BtsmtlGraphSemanticBuilder builder,
            IReadOnlyCollection<PipelineBlackboardVariableScope> hostScopes,
            bool contentDefaults,
            BtsmtlBlackboardCatalogFields domainFields = null)
        {
            m_Declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
            m_Builder = builder ?? throw new ArgumentNullException(nameof(builder));
            m_Scopes = hostScopes ?? throw new ArgumentNullException(nameof(hostScopes));
            m_ContentDefaults = contentDefaults;
            m_DomainFields = domainFields;
        }

        SimulationCompileReport Report => m_Builder.Report;

        public void CompileDeclarations()
        {
            foreach (BtsmtlBlackboardDeclarationSource item in m_Declarations.Values)
            {
                BtsmtlBlackboardDeclarationSnapshot declaration = item.Declaration;
                SimulationSourceLocation source = DeclarationSource(item, item.Route);
                if (!m_Scopes.Contains(declaration.BlackboardScope))
                {
                    Report.Error("blackboard_scope_unavailable", source.Identity,
                        $"Blackboard '{declaration.DeclarationId}' uses scope '{declaration.BlackboardScope}', which the host does not provide.");
                    continue;
                }
                string identity = BtsmtlGraphVariableReference.CompileIdentity(item.GraphId, declaration.DeclarationId);
                var fields = new List<ProgramCatalogField>
                {
                    m_Builder.ConstantField(source, "Key", declaration.BlackboardKey),
                    m_Builder.ConstantField(source, "ValueType", declaration.ValueType?.FullName),
                    m_Builder.ConstantField(source, "Scope", declaration.BlackboardScope),
                    m_Builder.ConstantField(source, "Lifetime", declaration.BlackboardLifetime),
                    m_Builder.ConstantField(source, "Category", declaration.BlackboardCategoryPath)
                };
                m_DomainFields?.Invoke(item, source, fields);
                if (declaration.InputBinding != null)
                {
                    fields.Add(m_Builder.ConstantField(source, "InputValueId", declaration.InputBinding.InputValueId));
                    fields.Add(m_Builder.ConstantField(source, "InputProjection", declaration.InputBinding.Projection));
                }
                m_Builder.DeclareCatalogEntry(
                    new ProgramIdentityDependency("btsmtl.blackboard-declaration", identity),
                    3,
                    fields.ToArray(),
                    source);
                if (declaration.BlackboardScope == PipelineBlackboardVariableScope.Character ||
                    declaration.BlackboardScope == PipelineBlackboardVariableScope.Frame)
                    EnsureDeclarationState(item, item.Route, OperationHandle.Invalid);
            }
        }

        public void BeginGraph(string graphId, string route, IEnumerable<string> declarationIds, OperationHandle stateOwner)
        {
            m_GraphStack.Add(new GraphRoute(graphId, route));
            foreach (string declarationId in declarationIds)
                EnsureDeclarationState(m_Declarations[BtsmtlGraphVariableReference.CompileIdentity(graphId, declarationId)], route, stateOwner);
        }

        public void EndGraph()
        {
            m_GraphStack.RemoveAt(m_GraphStack.Count - 1);
        }

        public void CompleteGraph(string route, OperationHandle graphEntry)
        {
            if (m_ScopeRecords.TryGetValue(ScopeIdentity(PipelineBlackboardVariableScope.Graph, route), out ScopeRecord scope))
                scope.SetOwnerOperation(graphEntry);
        }

        public void DeclareScopes()
        {
            foreach (ScopeRecord scope in m_ScopeRecords.Values.OrderBy(value => value.Identity, StringComparer.Ordinal))
            {
                m_Builder.DeclareScope(
                    scope.Identity,
                    scope.Kind,
                    scope.OwnerIdentity,
                    scope.OwnerOperation,
                    scope.StateSlots,
                    scope.Source);
            }
        }

        public void Bind(
            OperationHandle operation,
            string route,
            string declarationOwnerId,
            string declarationId,
            Type expectedValueType,
            SimulationSourceLocation source,
            bool requireWritable = false, string binding = null)
        {
            string declarationIdentity = BtsmtlGraphVariableReference.CompileIdentity(declarationOwnerId, declarationId);
            if (string.IsNullOrEmpty(declarationOwnerId) || string.IsNullOrEmpty(declarationId) ||
                !m_Declarations.TryGetValue(declarationIdentity, out BtsmtlBlackboardDeclarationSource declaration))
            {
                Report.Error("blackboard_reference_invalid", source.Identity, $"Blackboard reference '{declarationIdentity}' does not resolve.");
                return;
            }

            if (expectedValueType != null && declaration.Declaration.ValueType != expectedValueType)
            {
                Report.Error("blackboard_value_type_mismatch", source.Identity,
                    $"Blackboard '{declarationIdentity}' is '{declaration.Declaration.ValueType}', expected '{expectedValueType}'.");
                return;
            }
            if (requireWritable && declaration.Declaration.BlackboardLifetime == PipelineBlackboardVariableLifetime.Config)
            {
                Report.Error("blackboard_config_read_only", source.Identity, $"Blackboard '{declarationIdentity}' is read-only configuration.");
                return;
            }

            string stateKey = ResolveDeclarationStateKey(declaration);
            if (!m_ValueSlots.TryGetValue(stateKey, out int stateSlot))
            {
                Report.Error("blackboard_state_address_missing", source.Identity, $"Blackboard state address '{stateKey}' was not declared.");
                return;
            }
            m_Builder.DeclareReference(
                $"{route}/node:{source.NodeId}/blackboard-state:{binding ?? declarationIdentity}",
                operation,
                ProgramReferenceKind.StateSlot,
                stateSlot,
                binding ?? declarationIdentity,
                source);
        }

        int EnsureDeclarationState(BtsmtlBlackboardDeclarationSource item, string route, OperationHandle stateScopeOwner)
        {
            string stateKey = item.Declaration.BlackboardScope == PipelineBlackboardVariableScope.Character
                ? BtsmtlGraphVariableReference.CompileIdentity(item.GraphId, item.Declaration.DeclarationId)
                : $"{route}/declaration:{item.Declaration.DeclarationId}";
            if (m_ValueSlots.TryGetValue(stateKey, out int existing))
                return existing;
            if (!m_Scopes.Contains(item.Declaration.BlackboardScope))
                return -1;

            Type valueType = item.Declaration.ValueType;
            ProgramStateValueKind valueKind = OrchestrationValuePortContracts.ToState(BtsmtlGraphValueTypes.Kind(valueType));
            SimulationSourceLocation source = DeclarationSource(item, route);
            int value = m_ContentDefaults
                ? m_Builder.DeclareStandaloneStateSlot(
                    source,
                    valueKind,
                    ProgramStateOwnerKind.Blackboard,
                    ProgramStateSemantic.BlackboardValue,
                    stateKey,
                    BtsmtlGraphValueTypes.AuthoringValue(valueType, item.Declaration.DefaultValue))
                : m_Builder.DeclareStandaloneStateSlot(
                    source,
                    valueKind,
                    ProgramStateOwnerKind.Blackboard,
                    ProgramStateSemantic.BlackboardValue,
                    stateKey);
            int owner = m_Builder.DeclareStandaloneStateSlot(
                source,
                ProgramStateValueKind.BlackboardOwnerToken,
                ProgramStateOwnerKind.Blackboard,
                ProgramStateSemantic.BlackboardOwnerToken,
                stateKey);
            int lifetime = m_Builder.DeclareStandaloneStateSlot(
                source,
                ProgramStateValueKind.Int32,
                ProgramStateOwnerKind.Blackboard,
                ProgramStateSemantic.BlackboardLifetime,
                stateKey,
                (int)item.Declaration.BlackboardLifetime);
            int provenance = m_Builder.DeclareStandaloneStateSlot(
                source,
                ProgramStateValueKind.BlackboardWriteStamp,
                ProgramStateOwnerKind.Blackboard,
                ProgramStateSemantic.BlackboardWriteStamp,
                stateKey);
            m_ValueSlots.Add(stateKey, value);
            m_Builder.DeclareReference(
                $"{stateKey}/blackboard-declaration",
                OperationHandle.Invalid,
                ProgramReferenceKind.StateSlot,
                value,
                BtsmtlGraphVariableReference.CompileIdentity(item.GraphId, item.Declaration.DeclarationId),
                source);

            string scopeIdentity = ScopeIdentity(item.Declaration.BlackboardScope, route);
            if (!m_ScopeRecords.TryGetValue(scopeIdentity, out ScopeRecord scope))
            {
                ProgramScopeKind kind = MapScope(item.Declaration.BlackboardScope);
                if (kind == ProgramScopeKind.State && !stateScopeOwner.IsValid)
                    Report.Error("blackboard_state_owner_missing", source.Identity, "State Blackboard declaration is not inside a compiled State activation owner.");
                scope = new ScopeRecord(scopeIdentity, kind, route, kind == ProgramScopeKind.State ? stateScopeOwner : OperationHandle.Invalid, source);
                m_ScopeRecords.Add(scopeIdentity, scope);
            }
            scope.StateSlots.Add(value);
            scope.StateSlots.Add(owner);
            scope.StateSlots.Add(lifetime);
            scope.StateSlots.Add(provenance);
            return value;
        }

        string ResolveDeclarationStateKey(BtsmtlBlackboardDeclarationSource declaration)
        {
            if (declaration.Declaration.BlackboardScope == PipelineBlackboardVariableScope.Character)
                return BtsmtlGraphVariableReference.CompileIdentity(declaration.GraphId, declaration.Declaration.DeclarationId);
            if (declaration.Declaration.BlackboardScope == PipelineBlackboardVariableScope.Frame)
                return $"{declaration.Route}/declaration:{declaration.Declaration.DeclarationId}";
            for (int i = m_GraphStack.Count - 1; i >= 0; i--)
            {
                if (string.Equals(m_GraphStack[i].GraphId, declaration.GraphId, StringComparison.Ordinal))
                    return $"{m_GraphStack[i].Route}/declaration:{declaration.Declaration.DeclarationId}";
            }
            return $"{declaration.Route}/declaration:{declaration.Declaration.DeclarationId}";
        }

        static ProgramScopeKind MapScope(PipelineBlackboardVariableScope scope)
        {
            return scope switch
            {
                PipelineBlackboardVariableScope.Character => ProgramScopeKind.Character,
                PipelineBlackboardVariableScope.Graph => ProgramScopeKind.Graph,
                PipelineBlackboardVariableScope.State => ProgramScopeKind.State,
                PipelineBlackboardVariableScope.ActionInstance => ProgramScopeKind.ActionInstance,
                PipelineBlackboardVariableScope.Frame => ProgramScopeKind.Frame,
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
        }

        static string ScopeIdentity(PipelineBlackboardVariableScope scope, string route) =>
            scope == PipelineBlackboardVariableScope.Character ? "scope:character" : $"scope:{scope}:{route}";

        static SimulationSourceLocation DeclarationSource(BtsmtlBlackboardDeclarationSource item, string route)
        {
            return new SimulationSourceLocation(
                item.SourceType,
                item.GraphId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                $"{route}/blackboard:{item.Declaration.DeclarationId}",
                declarationId: item.Declaration.DeclarationId,
                contentHash: item.ContentHash);
        }

        sealed class ScopeRecord
        {
            public ScopeRecord(
                string identity,
                ProgramScopeKind kind,
                string ownerIdentity,
                OperationHandle ownerOperation,
                SimulationSourceLocation source)
            {
                Identity = identity;
                Kind = kind;
                OwnerIdentity = ownerIdentity;
                OwnerOperation = ownerOperation;
                Source = source;
            }

            public string Identity { get; }
            public ProgramScopeKind Kind { get; }
            public string OwnerIdentity { get; }
            public OperationHandle OwnerOperation { get; private set; }
            public SimulationSourceLocation Source { get; }
            public List<int> StateSlots { get; } = new List<int>();

            public void SetOwnerOperation(OperationHandle ownerOperation)
            {
                OwnerOperation = ownerOperation;
            }
        }

        readonly struct GraphRoute
        {
            public GraphRoute(string graphId, string route)
            {
                GraphId = graphId;
                Route = route;
            }

            public string GraphId { get; }
            public string Route { get; }
        }
    }
}
