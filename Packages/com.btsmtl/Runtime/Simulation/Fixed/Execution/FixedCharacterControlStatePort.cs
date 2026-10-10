using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal interface IFixedControlRuntimeStatePort
    {
        CharacterControlRuntimeStateTransaction BindControl(CharacterControlStateSchema schema);
    }
}
