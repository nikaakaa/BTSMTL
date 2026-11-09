using BTSMTL.Orchestration;

[assembly: OrchestrationObjectKindSet(typeof(ThirdPersonGameplay.Orchestration.GameplayObjectKinds))]

namespace ThirdPersonGameplay.Orchestration
{
    // 玩法对象世界的句柄种类：角色与载体互不兼容，都兼容于“任意对象”。
    public static class GameplayObjectKinds
    {
        public static readonly OrchestrationObjectKind Actor =
            new OrchestrationObjectKind("gameplay.object/actor", "角色", OrchestrationObjectKind.Any);
        public static readonly OrchestrationObjectKind Carrier =
            new OrchestrationObjectKind("gameplay.object/carrier", "载体", OrchestrationObjectKind.Any);
    }
}
