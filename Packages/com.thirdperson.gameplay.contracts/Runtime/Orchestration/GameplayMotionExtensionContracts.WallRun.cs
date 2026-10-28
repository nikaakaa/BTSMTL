using System;
using BTSMTL.Orchestration;
using Parameter = BTSMTL.Orchestration.OrchestrationExtensionParameter;
using Presentation = BTSMTL.Orchestration.OrchestrationExtensionParameterPresentation;

namespace ThirdPersonGameplay.Orchestration
{
    public static partial class GameplayMotionExtensionContracts
    {
        public static class WallRunFields
        {
            static readonly OrchestrationExtensionParameterList s_Declared = new();

            public static readonly Parameter ContactDistance = Scalar("ContactDistance", "墙面接触", "接墙距离", "米", 0.01d, 0.5d,
                "胶囊外扩的墙面探测距离，也是贴墙修正的最大范围。");
            public static readonly Parameter MaximumSurfaceAngle = Scalar("MaximumSurfaceAngle", "墙面接触", "墙面倾斜容差", "度", 0d, 44d,
                "相对竖直墙面的最大倾斜角度，不把地面和天花板当作跑墙面。");
            public static readonly Parameter MaximumCornerAngle = Scalar("MaximumCornerAngle", "墙面接触", "连续墙面最大转角", "度", 1d, 89d,
                "一帧内允许接续的墙面方向变化，超过后离墙。");
            public static readonly Parameter GravityScale = Scalar("GravityScale", "沿墙运动", "重力倍率", "倍", 0d, 1d,
                "自由墙跑使用此重力；钩锁牵引时取钩锁与墙跑倍率中较小的一个，只计算一次重力。");
            public static readonly Parameter TargetSpeed = Scalar("TargetSpeed", "沿墙运动", "输入目标速度", "米/秒", 0.01d, 60d,
                "沿墙输入加速的目标，接墙时高于此值的已有惯性继续保留。");
            public static readonly Parameter Acceleration = Scalar("Acceleration", "沿墙运动", "输入加速度", "米/秒²", 0.01d, 150d,
                "自由墙跑的加速与反向制动强度；牵引中继续使用钩锁的输入加速度。");
            public static readonly Parameter DetachInputThreshold = Scalar("DetachInputThreshold", "离墙", "向外输入阈值", "比例", 0d, 1d,
                "向墙外的输入分量超过该比例时主动离墙。");
            public static readonly Parameter DetachSpeed = Scalar("DetachSpeed", "离墙", "向外脱离速度", "米/秒", 0.01d, 30d,
                "向墙外的实际速度超过此值时离墙，允许钩锁将角色拉离墙面。");
            public static readonly Parameter JumpOutwardSpeed = Scalar("JumpOutwardSpeed", "蹬墙", "向外起跳速度", "米/秒", 0.01d, 30d,
                "蹬墙时补足的向外速度，保留沿墙速度和更高的已有向外速度。");
            public static readonly Parameter JumpUpwardSpeed = Scalar("JumpUpwardSpeed", "蹬墙", "向上起跳速度", "米/秒", 0.01d, 30d,
                "蹬墙时补足的上升速度，不截掉更高的已有上升速度。");
            public static readonly Parameter TurnSpeed = Scalar("TurnSpeed", "沿墙运动", "身体转向速度", "度/秒", 1d, 1440d,
                "身体朝向跟随沿墙速度的最大角速度。");

            public static readonly ReadOnlyMemory<Parameter> Parameters = s_Declared.ToParameters();
            public static readonly int ScalarCount = s_Declared.ScalarCount;

            static Parameter Scalar(string id, string group, string label, string unit, double minimum, double maximum,
                string description) => s_Declared.Scalar(id, new Presentation(group, label, unit, description, minimum, maximum));
        }
    }
}
