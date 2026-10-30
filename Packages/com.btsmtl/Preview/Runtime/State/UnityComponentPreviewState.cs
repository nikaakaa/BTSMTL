using System.Runtime.CompilerServices;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Preview
{
    public sealed class TransformPreviewState : IPreviewStateAdapter
    {
        struct State
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public bool Active;
        }

        readonly Transform m_Target;

        public TransformPreviewState(string identity, Transform target)
        {
            Identity = identity;
            m_Target = target;
        }

        public string Identity { get; }
        public PreviewRestoreStage RestoreStage => PreviewRestoreStage.Transform;
        public PreviewRestoreSupport RestoreSupport => PreviewRestoreSupport.SelfCheckRequired;
        public string Limitation => "层级须保持同一布局，组件生命周期引发的项目状态由适配层声明并参加恢复自检。";
        public PreviewStateCapacity Capacity => new PreviewStateCapacity(Unsafe.SizeOf<State>());

        public void Write<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter =>
            writer.Write(new State { Position = m_Target.localPosition, Rotation = m_Target.localRotation,
                Scale = m_Target.localScale, Active = m_Target.gameObject.activeSelf });

        public void Restore(ref RecordedRuntimeStateReader reader)
        {
            State state = reader.Read<State>();
            m_Target.SetLocalPositionAndRotation(state.Position, state.Rotation);
            m_Target.localScale = state.Scale;
            m_Target.gameObject.SetActive(state.Active);
        }
    }

    public sealed class RigidbodyPreviewState : IPreviewStateAdapter
    {
        struct State
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
            public Vector3 AngularVelocity;
            public bool Kinematic;
            public bool DetectCollisions;
            public bool Sleeping;
        }

        readonly Rigidbody m_Target;

        public RigidbodyPreviewState(string identity, Rigidbody target)
        {
            Identity = identity;
            m_Target = target;
        }

        public string Identity { get; }
        public PreviewRestoreStage RestoreStage => PreviewRestoreStage.Physics;
        public PreviewRestoreSupport RestoreSupport => PreviewRestoreSupport.SelfCheckRequired;
        public string Limitation => "保存公开位姿、动态速度和休眠状态；PhysX 接触缓存须由实际恢复自检确认。";
        public PreviewStateCapacity Capacity => new PreviewStateCapacity(Unsafe.SizeOf<State>());

        public void Write<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter =>
            writer.Write(new State { Position = m_Target.position, Rotation = m_Target.rotation,
                Velocity = m_Target.velocity, AngularVelocity = m_Target.angularVelocity,
                Kinematic = m_Target.isKinematic, DetectCollisions = m_Target.detectCollisions,
                Sleeping = m_Target.IsSleeping() });

        public void Restore(ref RecordedRuntimeStateReader reader)
        {
            State state = reader.Read<State>();
            m_Target.isKinematic = state.Kinematic;
            m_Target.detectCollisions = state.DetectCollisions;
            m_Target.position = state.Position;
            m_Target.rotation = state.Rotation;
            if (!state.Kinematic)
            {
                m_Target.velocity = state.Velocity;
                m_Target.angularVelocity = state.AngularVelocity;
                if (state.Sleeping)
                    m_Target.Sleep();
                else
                    m_Target.WakeUp();
            }
        }
    }

    public sealed class CharacterControllerPreviewState : IPreviewStateAdapter
    {
        struct State
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Enabled;
        }

        readonly CharacterController m_Target;

        public CharacterControllerPreviewState(string identity, CharacterController target)
        {
            Identity = identity;
            m_Target = target;
        }

        public string Identity { get; }
        public PreviewRestoreStage RestoreStage => PreviewRestoreStage.Physics;
        public PreviewRestoreSupport RestoreSupport => PreviewRestoreSupport.RestartOnly;
        public string Limitation => "CharacterController.velocity 与接触状态不可写；项目须提供其移动状态适配器后才能声明检查点恢复。";
        public PreviewStateCapacity Capacity => new PreviewStateCapacity(Unsafe.SizeOf<State>());

        public void Write<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter =>
            writer.Write(new State { Position = m_Target.transform.position,
                Rotation = m_Target.transform.rotation, Enabled = m_Target.enabled });

        public void Restore(ref RecordedRuntimeStateReader reader)
        {
            State state = reader.Read<State>();
            m_Target.enabled = false;
            m_Target.transform.SetPositionAndRotation(state.Position, state.Rotation);
            m_Target.enabled = state.Enabled;
        }
    }

    public sealed class RandomPreviewState : IPreviewStateAdapter
    {
        public string Identity => "UnityEngine.Random";
        public PreviewRestoreStage RestoreStage => PreviewRestoreStage.Random;
        public PreviewRestoreSupport RestoreSupport => PreviewRestoreSupport.SelfCheckRequired;
        public string Limitation => "恢复 Unity 全局随机状态；项目持有的其它随机源需独立声明。";
        public PreviewStateCapacity Capacity => new PreviewStateCapacity(Unsafe.SizeOf<Random.State>());
        public void Write<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter =>
            writer.Write(Random.state);
        public void Restore(ref RecordedRuntimeStateReader reader) => Random.state = reader.Read<Random.State>();
    }
}
