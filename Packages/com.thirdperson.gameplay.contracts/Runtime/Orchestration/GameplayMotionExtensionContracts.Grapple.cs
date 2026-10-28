using System;
using BTSMTL.Orchestration;
using Field = BTSMTL.Orchestration.OrchestrationExtensionContentField;
using Parameter = BTSMTL.Orchestration.OrchestrationExtensionParameter;
using Presentation = BTSMTL.Orchestration.OrchestrationExtensionParameterPresentation;

namespace ThirdPersonGameplay.Orchestration
{
    public static partial class GameplayMotionExtensionContracts
    {
        // 声明顺序即内容字段顺序，改动会改变已发布内容布局。
        public static class GrappleFields
        {
            static readonly OrchestrationExtensionParameterList s_Declared = new();

            public static readonly Parameter MaximumLength = Scalar("MaximumLength", "起手与飞钩", "最大绳长", "米", 1d, 150d, "限制飞钩和含拐点的实际绳长；调短到当前长度以下会断钩。选点范围仍由技能准入配置提供。");
            public static readonly Parameter HookSpeed = Scalar("HookSpeed", "起手与飞钩", "钩头飞行／收回速度", "米/秒", 1d, 200d, "调钩头到达与收回速度，不是角色牵引速度。");
            public static readonly Parameter PreparationTime = Scalar("PreparationTime", "起手与飞钩", "地面准备停顿", "秒", 0.01d, 1.5d, "地面起手停止平移并转向，之后发射。空中换钩沿现有空中规则。");
            public static readonly Parameter TakeoffDelay = Scalar("TakeoffDelay", "起手与飞钩", "准备期起跳时刻", "秒", 0d, 1.5d, "相对准备开始的起跳时刻，不能晚于准备结束。");
            public static readonly Parameter PullStartSpeed = Scalar("PullStartSpeed", "牵引", "初始牵引目标速度", "米/秒", 0d, 60d, "牵引加速曲线的起点。");
            public static readonly Parameter PullSpeed = Scalar("PullSpeed", "牵引", "稳定牵引目标速度", "米/秒", 0d, 100d, "朝钩点的目标速度，不会清掉已有绕行惯性。");
            public static readonly Parameter PullRampTime = Scalar("PullRampTime", "牵引", "目标速度展开时间", "秒", 0.01d, 3d, "从初始速度展开到稳定速度的时长，也供钩锁稳态动画条件读取。");
            public static readonly Parameter PullAcceleration = Scalar("PullAcceleration", "牵引", "钩点牵引加速度", "米/秒²", 0d, 200d, "持续朝绳路方向补速度。过大会抵消 S 的反向操控。");
            public static readonly Parameter InitialImpulse = Scalar("InitialImpulse", "牵引", "挂上瞬间速度增量", "米/秒", 0d, 20d, "钩头挂上时，沿钩点方向增加的速度。");
            public static readonly Parameter AttachVerticalSpeed = Scalar("AttachVerticalSpeed", "牵引", "表面挂接最小上升速度", "米/秒", 0d, 20d, "普通表面挂接时托起身体；固定点按自己的进场轨迹。");
            public static readonly Parameter ArrivalVerticalBoost = Scalar("ArrivalVerticalBoost", "抵达与受阻", "表面抵达向上借力", "米/秒", 0d, 20d, "普通表面自动松钩时增加的上升速度，用于越过平台边缘。");
            public static readonly Parameter ArrivalVerticalMaxSpeed = Scalar("ArrivalVerticalMaxSpeed", "抵达与受阻", "抵达借力上升速度上限", "米/秒", 0d, 30d, "只限制本次抵达借力，不截掉更高的已有上升速度。");
            public static readonly Parameter AirAcceleration = Scalar("AirAcceleration", "W/S 与空中操控", "方向操控加速度", "米/秒²", 0d, 150d, "W/S 沿相机水平前后方向加速或减速，A/D 沿相机水平左右方向施力。");
            public static readonly Parameter AirSteeringAcceleration = Scalar("AirSteeringAcceleration", "W/S 与空中操控", "达到目标后的改向强度", "米/秒²", 0d, 20d, "输入方向已达到目标时，以保速改向分支调整方向；原值很小，可用数值框精调。");
            public static readonly Parameter AirSpeed = Scalar("AirSpeed", "W/S 与空中操控", "输入方向目标速度", "米/秒", 0d, 50d, "控制输入方向还能补多少速度，不是整体硬截速上限。");
            public static readonly Parameter MinimumGravityScale = Scalar("MinimumGravityScale", "W/S 与空中操控", "最小重力倍率", "倍", 0d, 3d, "绳向、视线和操控共同决定当前重力，不能大于最大重力。");
            public static readonly Parameter GravityScale = Scalar("GravityScale", "W/S 与空中操控", "最大重力倍率", "倍", 0d, 3d, "牵引中使用的重力上界。普通自由落体参数不在这里修改。");
            public static readonly Parameter TangentialDeceleration = Scalar("TangentialDeceleration", "牵引", "绕行速度衰减", "米/秒²", 0d, 80d, "与拉绳方向垂直的速度衰减；降低可保留更多绕行惯性。");
            public static readonly Parameter LaunchHeight = Scalar("LaunchHeight", "起手与飞钩", "绳路起点高度", "米", 0d, 3d, "运动计算的出绳起点，相对角色身体位置。");
            public static readonly Parameter SurfaceMask = s_Declared.Add(Field.Value("SurfaceMask", OrchestrationValueKind.Int32),
                new Presentation("起手与飞钩", "可附着场景层", string.Empty, "选点与绳路检测使用的场景层。"));
            public static readonly Parameter ReleaseRadius = Scalar("ReleaseRadius", "抵达与受阻", "表面自动松钩距离", "米", 0.05d, 8d, "普通表面抵达半径。固定点使用场景点自己的进入范围和指定收势轨迹。");
            public static readonly Parameter BlockedSpeed = Scalar("BlockedSpeed", "抵达与受阻", "受阻判定速度", "米/秒", 0d, 5d, "发生碰撞且实际速度低于此值时，累计受阻时间。");
            public static readonly Parameter BlockTimeout = Scalar("BlockTimeout", "抵达与受阻", "受阻后松钩等待", "秒", 0d, 2d, "持续受阻达到该时间后结束钩锁。");
            public static readonly Parameter TurnSpeed = Scalar("TurnSpeed", "身体转向与断钩", "基础转向速度", "度/秒", 0d, 1080d, "准备和发射时朝钩点、牵引时追随实际飞行方向的基础转速，整个过程均限速。");
            public static readonly Parameter InputTurnSpeedBonus = Scalar("InputTurnSpeedBonus", "身体转向与断钩", "同侧 A/D 转向加成", "度/秒", 0d, 1080d, "按下转向同侧的 A/D 时，增加身体转向速度。");
            public static readonly Parameter DetachAngle = Scalar("DetachAngle", "身体转向与断钩", "人物／绳方向断钩夹角", "度", 1d, 120d, "起手前检查人物逻辑朝向与钩点方向；已超角就拒绝。发射和牵引在转向前检查实际钩锁方向，超角先断钩。与相机选点吸附框独立。");

            public static readonly ReadOnlyMemory<Parameter> Parameters = s_Declared.ToParameters();
            public static readonly ReadOnlyMemory<Field> All = s_Declared.ToFields();
            public static readonly int ScalarCount = s_Declared.ScalarCount;

            static Parameter Scalar(string id, string group, string label, string unit, double minimum, double maximum,
                string description) => s_Declared.Scalar(id, new Presentation(group, label, unit, description, minimum, maximum));
        }

        public readonly struct BeginGrapple
        {
            public static readonly OrchestrationExtensionDefinition<BeginGrapple> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "begin-grapple"), 8,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, Array.Empty<OrchestrationExtensionPort>(), GrappleFields.All));
        }

        public readonly struct ReadGrapple
        {
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Ended = new("ended", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Reason = new("reason", OrchestrationValueKind.Int32, 1);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Direction = new("direction", OrchestrationValueKind.Vector3, 2);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Attached = new("attached", OrchestrationValueKind.Boolean, 3);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> InFlight = new("in-flight", OrchestrationValueKind.Boolean, 4);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> GroundHandoff = new("ground-handoff", OrchestrationValueKind.Boolean, 5);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Steady = new("steady", OrchestrationValueKind.Boolean, 6);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Active = new("active", OrchestrationValueKind.Boolean, 7);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Approaching = new("approaching", OrchestrationValueKind.Boolean, 8);
            public static readonly OrchestrationExtensionDefinition<ReadGrapple> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "read-grapple"), 4,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[] { Ended.Declaration, Reason.Declaration, Direction.Declaration, Attached.Declaration,
                        InFlight.Declaration, GroundHandoff.Declaration, Steady.Declaration, Active.Declaration,
                        Approaching.Declaration }, Array.Empty<Field>()));
        }

        public readonly struct EndGrapple
        {
            public static readonly Field RequestId = Field.Text("RequestId");
            public static readonly OrchestrationExtensionDefinition<EndGrapple> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "end-grapple"), 3,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, Array.Empty<OrchestrationExtensionPort>(), new[] { RequestId }));
        }
    }
}
