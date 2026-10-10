namespace ThirdPersonSimulation
{
    internal interface IFloat32ControlRuntimeStatePort
    {
        CharacterControlRuntimeStateTransaction BindControl(CharacterControlStateSchema schema);
    }
}
