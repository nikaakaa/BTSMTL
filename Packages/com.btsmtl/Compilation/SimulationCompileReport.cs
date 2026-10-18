using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BTSMTL.Compilation
{
    public enum SimulationCompileStage
    {
        AuthoringDiscovery,
        SemanticEmission,
        ArtifactValidation
    }

    public enum SimulationCompileSeverity
    {
        Error
    }

    public sealed class SimulationCompileMessage
    {
        public SimulationCompileMessage(SimulationCompileStage stage, SimulationCompileSeverity severity, string code, string sourceIdentity, string message)
        {
            Stage = stage;
            Severity = severity;
            Code = code ?? string.Empty;
            SourceIdentity = sourceIdentity ?? string.Empty;
            Message = message ?? string.Empty;
        }
        public SimulationCompileStage Stage { get; }
        public SimulationCompileSeverity Severity { get; }
        public string Code { get; }
        public string SourceIdentity { get; }
        public string Message { get; }
        public override string ToString() => $"{Stage} {Severity} {Code} {SourceIdentity}: {Message}";
    }

    public sealed class SimulationCompileReport
    {
        readonly List<SimulationCompileMessage> m_Messages = new List<SimulationCompileMessage>();
        readonly ReadOnlyCollection<SimulationCompileMessage> m_ReadOnlyMessages;

        public SimulationCompileReport()
        {
            m_ReadOnlyMessages = m_Messages.AsReadOnly();
        }

        public IReadOnlyList<SimulationCompileMessage> Messages => m_ReadOnlyMessages;
        public bool IsValid
        {
            get
            {
                for (int i = 0; i < m_Messages.Count; i++)
                {
                    if (m_Messages[i].Severity == SimulationCompileSeverity.Error)
                        return false;
                }
                return true;
            }
        }

        public void Error(string code, string sourceIdentity, string message) => EmissionError(code, sourceIdentity, message);
        public void DiscoveryError(string code, string sourceIdentity, string message) => Add(SimulationCompileStage.AuthoringDiscovery, SimulationCompileSeverity.Error, code, sourceIdentity, message);
        public void EmissionError(string code, string sourceIdentity, string message) => Add(SimulationCompileStage.SemanticEmission, SimulationCompileSeverity.Error, code, sourceIdentity, message);
        public void ArtifactError(string code, string sourceIdentity, string message) => Add(SimulationCompileStage.ArtifactValidation, SimulationCompileSeverity.Error, code, sourceIdentity, message);

        void Add(SimulationCompileStage stage, SimulationCompileSeverity severity, string code, string sourceIdentity, string message)
        {
            m_Messages.Add(new SimulationCompileMessage(stage, severity, code, sourceIdentity, message));
        }
    }

}
