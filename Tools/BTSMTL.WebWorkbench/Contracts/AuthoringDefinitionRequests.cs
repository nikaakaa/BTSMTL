namespace BTSMTL.WebWorkbench;

public sealed record AuthoringStateMachineValidationRequest(string Content, string[] RelatedGraphs,
    string DefinitionsRevision);
public sealed record AuthoringFlowGraphValidationRequest(string Content, string DefinitionsRevision);
