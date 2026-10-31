using System.Collections.Generic;
using System.Collections.ObjectModel;
using BTSMTL.Compilation;

namespace ThirdPersonSimulation
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class SemanticGraphStructure
    {
        public SemanticGraphStructure(IReadOnlyList<SemanticOperationLayout> operationLayouts,
            ReadOnlyCollection<SemanticConstantInputBinding> constantInputBindings,
            ReadOnlyCollection<ProgramControlFlowEdge> controlFlow, ReadOnlyCollection<ProgramReference> references,
            SemanticStateTables stateTables, ReadOnlyCollection<ProgramCatalogEntry> catalogEntries)
        {
            OperationLayouts = operationLayouts;
            ConstantInputBindings = constantInputBindings;
            ControlFlow = controlFlow;
            References = references;
            StateTables = stateTables;
            CatalogEntries = catalogEntries;
        }

        public IReadOnlyList<SemanticOperationLayout> OperationLayouts { get; }
        public ReadOnlyCollection<SemanticConstantInputBinding> ConstantInputBindings { get; }
        public ReadOnlyCollection<ProgramControlFlowEdge> ControlFlow { get; }
        public ReadOnlyCollection<ProgramReference> References { get; }
        public SemanticStateTables StateTables { get; }
        public ReadOnlyCollection<ProgramCatalogEntry> CatalogEntries { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class SemanticGraphTables
    {
        public SemanticGraphTables(SemanticGraphStructure structure, SemanticGraphParameters parameters)
        {
            Structure = structure;
            Parameters = parameters;
        }

        public SemanticGraphStructure Structure { get; }
        public SemanticGraphParameters Parameters { get; }
    }
}
