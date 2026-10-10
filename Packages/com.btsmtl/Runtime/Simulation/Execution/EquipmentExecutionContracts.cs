namespace ThirdPersonSimulation
{
    internal interface IEquipmentActionContextReader
    {
        bool HasActionRoute(EquipmentActionRouteId routeId);
        bool TryReadActionContext(EquipmentActionRouteId routeId, out EquipmentActionContext context);
    }

    internal interface IEquipmentActionContextProvider : IEquipmentActionContextReader
    {
        bool IsAbilityBinding(EquipmentActionContext context, CharacterSkillId abilityId);
        bool IsCurrentActionContext(EquipmentActionContext context);
    }
}
