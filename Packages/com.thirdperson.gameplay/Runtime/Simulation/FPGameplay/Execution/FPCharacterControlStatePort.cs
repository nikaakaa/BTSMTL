using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal interface IFPControlRuntimeStatePort
    {
        CharacterControlRuntimeStateTransaction BindControl(CharacterControlStateSchema schema);
    }
}
