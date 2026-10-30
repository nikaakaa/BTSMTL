using System;
using System.Runtime.CompilerServices;
using Animancer;
using ThirdPersonSimulation;

namespace BTSMTL.Preview
{
    public sealed class AnimancerPreviewState : IPreviewStateAdapter
    {
        struct LayerState
        {
            public float Weight;
            public float Speed;
            public int Current;
        }

        struct ClipSnapshot
        {
            public float Time;
            public float Speed;
            public float Weight;
            public bool Playing;
        }

        readonly AnimancerGraph m_Graph;
        readonly AnimancerLayer[] m_Layers;
        readonly ClipState[][] m_States;

        public AnimancerPreviewState(string identity, AnimancerComponent target)
        {
            Identity = identity;
            m_Graph = target.Graph;
            m_Layers = new AnimancerLayer[m_Graph.Layers.Count];
            m_States = new ClipState[m_Layers.Length][];
            int bytes = sizeof(bool) + m_Layers.Length * Unsafe.SizeOf<LayerState>();
            for (int i = 0; i < m_Layers.Length; i++)
            {
                AnimancerLayer layer = m_Graph.Layers[i];
                m_Layers[i] = layer;
                m_States[i] = new ClipState[layer.ChildCount];
                for (int state = 0; state < layer.ChildCount; state++)
                {
                    if (!(layer.GetChild(state) is ClipState clip))
                        throw new NotSupportedException("Animancer 默认存档仅接收已准备的 ClipState；Mixer/Controller/自定义状态须由项目适配：" + identity);
                    m_States[i][state] = clip;
                }
                bytes = checked(bytes + layer.ChildCount * Unsafe.SizeOf<ClipSnapshot>());
            }
            Capacity = new PreviewStateCapacity(bytes);
        }

        public string Identity { get; }
        public PreviewRestoreStage RestoreStage => PreviewRestoreStage.Animation;
        public PreviewRestoreSupport RestoreSupport => PreviewRestoreSupport.SelfCheckRequired;
        public string Limitation => "恢复同一已准备层/片段布局的时间、速度、权重和播放状态；活动淡入淡出、事件及自定义节点须由项目适配。";
        public PreviewStateCapacity Capacity { get; }

        public void Write<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter
        {
            if (m_Graph.Layers.Count != m_Layers.Length)
                throw new InvalidOperationException("Animancer 存档的层布局已改变，需要重新准备：" + Identity);
            for (int i = 0; i < m_Layers.Length; i++)
            {
                AnimancerLayer layer = m_Layers[i];
                if (layer.ChildCount != m_States[i].Length)
                    throw new InvalidOperationException("Animancer 存档的片段布局已改变，需要重新准备：" + Identity);
                if (layer.FadeGroup != null)
                    throw new NotSupportedException("Animancer 活动层淡入淡出需要项目存档适配：" + Identity);
                foreach (ClipState state in m_States[i])
                    if (state.FadeGroup != null)
                        throw new NotSupportedException("Animancer 活动片段淡入淡出需要项目存档适配：" + Identity);
            }
            writer.Write(m_Graph.IsGraphPlaying);
            for (int i = 0; i < m_Layers.Length; i++)
            {
                AnimancerLayer layer = m_Layers[i];
                int current = Array.IndexOf(m_States[i], layer.CurrentState);
                writer.Write(new LayerState { Weight = layer.Weight, Speed = layer.Speed, Current = current });
                foreach (ClipState state in m_States[i])
                    writer.Write(new ClipSnapshot { Time = state.Time, Speed = state.Speed,
                        Weight = state.Weight, Playing = state.IsPlaying });
            }
        }

        public void Restore(ref RecordedRuntimeStateReader reader)
        {
            bool playing = reader.Read<bool>();
            for (int i = 0; i < m_Layers.Length; i++)
            {
                LayerState saved = reader.Read<LayerState>();
                AnimancerLayer layer = m_Layers[i];
                layer.Stop();
                if (saved.Current >= 0)
                    layer.Play(m_States[i][saved.Current]);
                layer.Weight = saved.Weight;
                layer.Speed = saved.Speed;
                foreach (ClipState state in m_States[i])
                {
                    ClipSnapshot clip = reader.Read<ClipSnapshot>();
                    state.Time = clip.Time;
                    state.Speed = clip.Speed;
                    state.Weight = clip.Weight;
                    state.IsPlaying = clip.Playing;
                }
            }
            m_Graph.Evaluate(0);
            m_Graph.IsGraphPlaying = playing;
        }
    }
}
