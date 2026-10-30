using System;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Preview
{
    public sealed class AnimatorPreviewState : IPreviewStateAdapter
    {
        struct State
        {
            public float Speed;
            public bool Enabled;
            public bool RootMotion;
            public AnimatorUpdateMode UpdateMode;
            public AnimatorCullingMode CullingMode;
        }

        struct Layer
        {
            public int Current;
            public float CurrentTime;
            public float Weight;
            public bool Transition;
            public int Next;
            public float NextTime;
            public float NextSeconds;
            public float TransitionTime;
            public float Duration;
            public DurationUnit DurationUnit;
        }

        readonly Animator m_Target;
        readonly AnimatorControllerParameter[] m_Parameters;
        readonly int m_LayerCount;

        public AnimatorPreviewState(string identity, Animator target)
        {
            Identity = identity;
            m_Target = target;
            m_Parameters = target.parameters;
            m_LayerCount = target.layerCount;
            int bytes = Unsafe.SizeOf<State>() + m_LayerCount * Unsafe.SizeOf<Layer>();
            RestoreSupport = PreviewRestoreSupport.SelfCheckRequired;
            Limitation = "保存各层状态、过渡、时间、权重及 Float/Int/Bool 参数；状态机回调副作用须由项目状态适配和恢复自检覆盖。";
            foreach (AnimatorControllerParameter parameter in m_Parameters)
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float: bytes += sizeof(float); break;
                    case AnimatorControllerParameterType.Int: bytes += sizeof(int); break;
                    case AnimatorControllerParameterType.Bool: bytes += sizeof(bool); break;
                    case AnimatorControllerParameterType.Trigger:
                        RestoreSupport = PreviewRestoreSupport.RestartOnly;
                        Limitation = "Animator Trigger 待消费状态没有正式读取接口，项目须替换为显式持有触发状态的存档适配器。";
                        break;
                    default: throw new InvalidOperationException("未支持的 Animator 参数类型：" + parameter.type);
                }
            Capacity = new PreviewStateCapacity(bytes);
        }

        public string Identity { get; }
        public PreviewRestoreStage RestoreStage => PreviewRestoreStage.Animation;
        public PreviewRestoreSupport RestoreSupport { get; }
        public string Limitation { get; }
        public PreviewStateCapacity Capacity { get; }

        public void Write<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(new State { Speed = m_Target.speed, Enabled = m_Target.enabled,
                RootMotion = m_Target.applyRootMotion, UpdateMode = m_Target.updateMode,
                CullingMode = m_Target.cullingMode });
            for (int i = 0; i < m_LayerCount; i++)
            {
                AnimatorStateInfo current = m_Target.GetCurrentAnimatorStateInfo(i);
                bool transitioning = m_Target.IsInTransition(i);
                AnimatorStateInfo next = transitioning ? m_Target.GetNextAnimatorStateInfo(i) : default;
                AnimatorTransitionInfo transition = transitioning ? m_Target.GetAnimatorTransitionInfo(i) : default;
                writer.Write(new Layer { Current = current.fullPathHash, CurrentTime = current.normalizedTime,
                    Weight = m_Target.GetLayerWeight(i), Transition = transitioning,
                    Next = next.fullPathHash, NextTime = next.normalizedTime,
                    NextSeconds = next.normalizedTime * next.length,
                    TransitionTime = transition.normalizedTime, Duration = transition.duration,
                    DurationUnit = transition.durationUnit });
            }
            foreach (AnimatorControllerParameter parameter in m_Parameters)
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float: writer.Write(m_Target.GetFloat(parameter.nameHash)); break;
                    case AnimatorControllerParameterType.Int: writer.Write(m_Target.GetInteger(parameter.nameHash)); break;
                    case AnimatorControllerParameterType.Bool: writer.Write(m_Target.GetBool(parameter.nameHash)); break;
                }
        }

        public void Restore(ref RecordedRuntimeStateReader reader)
        {
            State state = reader.Read<State>();
            ReadOnlySpan<Layer> layers = reader.ReadSpan<Layer>(m_LayerCount);
            m_Target.enabled = true;
            m_Target.applyRootMotion = state.RootMotion;
            m_Target.updateMode = state.UpdateMode;
            m_Target.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (AnimatorControllerParameter parameter in m_Parameters)
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float: m_Target.SetFloat(parameter.nameHash, reader.Read<float>()); break;
                    case AnimatorControllerParameterType.Int: m_Target.SetInteger(parameter.nameHash, reader.Read<int>()); break;
                    case AnimatorControllerParameterType.Bool: m_Target.SetBool(parameter.nameHash, reader.Read<bool>()); break;
                }
            for (int i = 0; i < layers.Length; i++)
            {
                ref readonly Layer layer = ref layers[i];
                m_Target.Play(layer.Current, i, layer.CurrentTime);
                m_Target.SetLayerWeight(i, layer.Weight);
            }
            m_Target.Update(0);
            for (int i = 0; i < layers.Length; i++)
            {
                ref readonly Layer layer = ref layers[i];
                if (!layer.Transition)
                    continue;
                if (layer.DurationUnit == DurationUnit.Fixed)
                    m_Target.CrossFadeInFixedTime(layer.Next, layer.Duration, i,
                        layer.NextSeconds, layer.TransitionTime);
                else
                    m_Target.CrossFade(layer.Next, layer.Duration, i, layer.NextTime, layer.TransitionTime);
            }
            m_Target.Update(0);
            m_Target.speed = state.Speed;
            m_Target.cullingMode = state.CullingMode;
            m_Target.enabled = state.Enabled;
        }
    }
}
