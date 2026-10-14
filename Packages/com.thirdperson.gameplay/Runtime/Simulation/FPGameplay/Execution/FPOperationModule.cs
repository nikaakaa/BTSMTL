using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPGameplayAbilityExecutionAccess
    {
        public FPGameplayAbilityExecutionAccess(
            FPGameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            FPGameplayAbilityExecutionServices services)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public FPGameplayAbilityExecutionData Data { get; }
        public GameplayAbilityExecutionLayout Layout { get; }
        public FPGameplayAbilityExecutionServices Services { get; }
        public OperationExecutionTopology Topology => Layout.Topology;

        public SimulationOperation Operation(OperationHandle handle)
        {
            Topology.RequireOperation(handle);
            return Data.Operations[handle.Value];
        }

        public IReadOnlyList<ProgramControlFlowEdge> Edges(OperationHandle source, ProgramControlFlowKind kind) =>
            Layout.Outgoing(source, kind);

        public IReadOnlyList<ProgramReference> References(OperationHandle source, ProgramReferenceKind kind) =>
            Layout.References(source, kind);

        public int FindOperationSlot(OperationHandle operation, ProgramStateSemantic semantic) =>
            Layout.FindOperationStateSlot(operation, semantic);

        public int RequireOperationSlot(OperationHandle operation, ProgramStateSemantic semantic)
        {
            int slot = FindOperationSlot(operation, semantic);
            if (slot < 0)
                throw new InvalidOperationException($"Operation '{operation}' has no '{semantic}' state slot.");
            return slot;
        }

        public int FindStateSlot(ProgramStateSemantic semantic, string ownerIdentity) =>
            Layout.FindStateSlot(semantic, ownerIdentity);

        public ProgramConstant FindConstant(SimulationOperation operation, OperationNamedConstant field) =>
            Layout.FindNamedConstant(operation.Handle, field);

        public ProgramConstant FindConstant(SimulationOperation operation, string field)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("Operation constant field is required.", nameof(field));
            string suffix = "/constant/" + field;
            ProgramConstant result = null;
            for (int i = 0; i < operation.ConstantReferences.Count; i++)
            {
                ProgramConstant candidate = Data.Constants[operation.ConstantReferences[i]];
                if (!candidate.Identity.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw new InvalidOperationException($"Operation '{operation.Handle}' constant '{field}' is duplicated.");
                result = candidate;
            }
            return result;
        }

        public ProgramCatalogEntry RequireCatalog(SimulationOperation operation, ProgramCatalogEntryKind kind) =>
            Layout.RequireCatalog(operation.Handle, kind);

        public ProgramCatalogEntry FindCatalog(SimulationOperation operation, ProgramCatalogEntryKind kind) =>
            Layout.FindCatalog(operation.Handle, kind);

        public ProgramCatalogEntry FindCatalog(ProgramCatalogEntryKind kind, string identity) =>
            Layout.FindCatalog(kind, identity);

        public ProgramCatalogField RequireCatalogField(ProgramCatalogEntry entry, ProgramCatalogFieldId field) =>
            Layout.RequireCatalogField(entry, field);

        public bool TryGetCatalogField(ProgramCatalogEntry entry, ProgramCatalogFieldId field, out ProgramCatalogField value) =>
            Layout.TryGetCatalogField(entry, field, out value);

        public bool TryGetCatalogIdentity(ProgramCatalogEntry entry, ProgramCatalogFieldId field, out string identity) =>
            Layout.TryGetCatalogIdentity(entry, field, out identity);

        public string GetStringConstant(SimulationOperation operation, OperationNamedConstant field, string fallback)
        {
            ProgramConstant constant = FindConstant(operation, field);
            return constant != null && constant.Kind == ProgramConstantKind.String ? constant.Text : fallback;
        }

        public string GetStringConstant(SimulationOperation operation, string field, string fallback)
        {
            ProgramConstant constant = FindConstant(operation, field);
            return constant != null && constant.Kind == ProgramConstantKind.String ? constant.Text : fallback;
        }

        public bool GetBooleanConstant(SimulationOperation operation, OperationNamedConstant field, bool fallback)
        {
            ProgramConstant constant = FindConstant(operation, field);
            return constant != null && constant.Kind == ProgramConstantKind.Boolean ? constant.Boolean : fallback;
        }

        public string SourcePath(SimulationOperation operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            return Services.SourcePath(operation.Handle);
        }

        public ProgramExecutionSource RequireOperationSource(SimulationOperation operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            var sources = Layout.OperationSources(operation.Handle);
            if (sources.Count > 1)
                throw new InvalidOperationException($"Operation '{operation.Handle}' has multiple source entries.");
            return sources.Source ?? throw new InvalidOperationException($"Operation '{operation.Handle}' has no source entry.");
        }
    }

    internal abstract class FPOperationModule
    {
        protected FPOperationModule(FPGameplayAbilityExecutionAccess access)
        {
            Access = access ?? throw new ArgumentNullException(nameof(access));
        }

        protected FPGameplayAbilityExecutionAccess Access { get; }
        protected FPGameplayAbilityExecutionData m_Ability => Access.Data;
        protected GameplayAbilityExecutionLayout m_Layout => Access.Layout;
        protected IReadOnlyList<ProgramControlFlowEdge> Edges(OperationHandle source, ProgramControlFlowKind kind) =>
            Access.Edges(source, kind);
        protected IReadOnlyList<ProgramReference> References(OperationHandle source, ProgramReferenceKind kind) =>
            Access.References(source, kind);
        protected int FindOperationSlot(SimulationOperation operation, ProgramStateSemantic semantic) =>
            Access.FindOperationSlot(operation.Handle, semantic);
        protected int RequireOperationSlot(SimulationOperation operation, ProgramStateSemantic semantic) =>
            Access.RequireOperationSlot(operation.Handle, semantic);
        protected int FindStateSlot(ProgramStateSemantic semantic, string ownerIdentity) =>
            Access.FindStateSlot(semantic, ownerIdentity);
        protected ProgramConstant FindConstant(SimulationOperation operation, OperationNamedConstant field) =>
            Access.FindConstant(operation, field);
        protected ProgramCatalogEntry RequireCatalog(SimulationOperation operation, ProgramCatalogEntryKind kind) =>
            Access.RequireCatalog(operation, kind);
        protected ProgramCatalogEntry FindCatalog(SimulationOperation operation, ProgramCatalogEntryKind kind) =>
            Access.FindCatalog(operation, kind);
        protected ProgramCatalogEntry FindCatalog(ProgramCatalogEntryKind kind, string identity) =>
            Access.FindCatalog(kind, identity);
        protected ProgramCatalogEntry RequireCatalog(ProgramCatalogEntryKind kind, string identity) =>
            Access.FindCatalog(kind, identity) ??
            throw new InvalidOperationException($"Catalog '{kind}' identity '{identity}' is missing.");
        protected bool TryGetCatalogIdentity(ProgramCatalogEntry entry, ProgramCatalogFieldId field, out string identity) =>
            Access.TryGetCatalogIdentity(entry, field, out identity);
        protected string GetStringConstant(SimulationOperation operation, OperationNamedConstant field, string fallback) =>
            Access.GetStringConstant(operation, field, fallback);
        protected bool GetBooleanConstant(SimulationOperation operation, OperationNamedConstant field, bool fallback) =>
            Access.GetBooleanConstant(operation, field, fallback);
        protected string SourcePath(SimulationOperation operation) => Access.SourcePath(operation);

        protected ProgramConstant CatalogConstant(ProgramCatalogEntry entry, ProgramCatalogFieldId field)
        {
            ProgramCatalogField value = Access.RequireCatalogField(entry, field);
            if (value.Kind != ProgramCatalogFieldKind.Constant)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{field}' is not Constant.");
            return m_Ability.Constants[value.ConstantIndex];
        }

        protected ProgramConstant CatalogConstant(ProgramCatalogEntry entry, string fieldName)
        {
            ProgramCatalogField value = FindCatalogField(entry, fieldName);
            if (value == null || value.Kind != ProgramCatalogFieldKind.Constant)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{fieldName}' is not Constant.");
            return m_Ability.Constants[value.ConstantIndex];
        }

        protected bool TryCatalogInt32(ProgramCatalogEntry entry, ProgramCatalogFieldId field, out int value)
        {
            value = 0;
            if (!Access.TryGetCatalogField(entry, field, out ProgramCatalogField catalogField))
                return false;
            if (catalogField.Kind != ProgramCatalogFieldKind.Constant)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{field}' is not Constant.");
            ProgramConstant constant = m_Ability.Constants[catalogField.ConstantIndex];
            if (constant.Kind != ProgramConstantKind.Int32)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{field}' is not Int32.");
            value = constant.Int32;
            return true;
        }

        protected int CatalogInt32(ProgramCatalogEntry entry, ProgramCatalogFieldId field)
        {
            ProgramConstant constant = CatalogConstant(entry, field);
            if (constant.Kind != ProgramConstantKind.Int32)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{field}' is not Int32.");
            return constant.Int32;
        }

        protected int CatalogInt32(ProgramCatalogEntry entry, string fieldName)
        {
            ProgramConstant constant = CatalogConstant(entry, fieldName);
            if (constant.Kind != ProgramConstantKind.Int32)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{fieldName}' is not Int32.");
            return constant.Int32;
        }

        protected ulong CatalogUInt64(ProgramCatalogEntry entry, ProgramCatalogFieldId field)
        {
            ProgramConstant constant = CatalogConstant(entry, field);
            if (constant.Kind != ProgramConstantKind.UInt64)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{field}' is not UInt64.");
            return constant.UInt64;
        }

        protected bool CatalogBoolean(ProgramCatalogEntry entry, ProgramCatalogFieldId field)
        {
            ProgramConstant constant = CatalogConstant(entry, field);
            if (constant.Kind != ProgramConstantKind.Boolean)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{field}' is not Boolean.");
            return constant.Boolean;
        }

        protected FP CatalogScalar(ProgramCatalogEntry entry, ProgramCatalogFieldId field)
        {
            ProgramConstant constant = CatalogConstant(entry, field);
            if (constant.Kind != ProgramConstantKind.Scalar)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{field}' is not Scalar.");
            return constant.Scalar;
        }

        protected string CatalogString(ProgramCatalogEntry entry, ProgramCatalogFieldId field)
        {
            ProgramConstant constant = CatalogConstant(entry, field);
            if (constant.Kind != ProgramConstantKind.String)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{field}' is not String.");
            if (string.IsNullOrWhiteSpace(constant.Text))
                throw new ArgumentException("Identity is required.", $"{entry.Identity}/{field}");
            return constant.Text.Trim();
        }

        protected string CatalogIdentity(ProgramCatalogEntry entry, ProgramCatalogFieldId field)
        {
            ProgramCatalogField value = Access.RequireCatalogField(entry, field);
            if (value.Kind != ProgramCatalogFieldKind.Identity)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{field}' is not Identity.");
            return value.Identity;
        }

        protected string CatalogIdentity(ProgramCatalogEntry entry, string fieldName)
        {
            ProgramCatalogField value = FindCatalogField(entry, fieldName);
            if (value == null || value.Kind != ProgramCatalogFieldKind.Identity)
                throw new InvalidOperationException($"Catalog field '{entry.Identity}/{fieldName}' is not Identity.");
            return value.Identity;
        }

        protected bool TryCatalogIdentity(ProgramCatalogEntry entry, string fieldName, out string identity)
        {
            ProgramCatalogField value = FindCatalogField(entry, fieldName);
            if (value != null && value.Kind == ProgramCatalogFieldKind.Identity)
            {
                identity = value.Identity;
                return true;
            }
            identity = string.Empty;
            return false;
        }

        static ProgramCatalogField FindCatalogField(ProgramCatalogEntry entry, string fieldName)
        {
            for (int i = 0; i < entry.Fields.Count; i++)
            {
                ProgramCatalogField field = entry.Fields[i];
                if (string.Equals(field.Name, fieldName, StringComparison.Ordinal))
                    return field;
            }
            return null;
        }
    }
}

