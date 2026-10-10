using System;
using System.Collections.Generic;

namespace BTSMTL.Diagnostics.Editor
{
    public interface IRuntimeDebugInstanceContext
    {
        RuntimeInstanceKey RuntimeInstance { get; }
    }

    public enum RuntimeDebugViewKind
    {
        Graph,
        Timeline
    }

    public enum RuntimeDebugViewBindingMode
    {
        None,
        Following,
        Pinned
    }

    enum RuntimeDebugViewBindingStatus
    {
        Ready,
        NoInstance,
        NoSelection,
        MultipleRuntimeInstances,
        PinnedInstanceMissing
    }

    public sealed class RuntimeDebugViewBinding
    {
        RuntimeDebugTargetRequest m_Request;
        RuntimeInstanceKey m_SelectedInstance;
        RuntimeDebugViewBindingMode m_Mode = RuntimeDebugViewBindingMode.Following;
        RuntimeDebugTargetResolution m_Resolution;
        RuntimeDebugViewBindingStatus m_Status;
        Guid m_BoundCharacterRuntimeId;
        readonly List<RuntimeInstanceKey> m_InstanceScratch = new List<RuntimeInstanceKey>();

        public RuntimeDebugViewBinding(RuntimeDebugViewKind kind)
        {
            Kind = kind;
        }

        public RuntimeDebugViewKind Kind { get; }
        public RuntimeDebugTargetRequest Request => m_Request;
        public RuntimeDebugViewBindingMode Mode => m_Mode;
        public RuntimeInstanceKey SelectedInstance => m_SelectedInstance;
        public RuntimeDebugTargetResolution Resolution => m_Resolution;
        public bool Following => m_Mode == RuntimeDebugViewBindingMode.Following;
        public bool Pinned => m_Mode == RuntimeDebugViewBindingMode.Pinned;
        public bool CanReadSelectedInstance => m_Resolution.CanReadSnapshot && m_Status == RuntimeDebugViewBindingStatus.Ready;

        public string StatusMessage
        {
            get
            {
                if (!m_Resolution.CanReadSnapshot)
                    return m_Resolution.Message;

                return m_Status switch
                {
                    RuntimeDebugViewBindingStatus.Ready => m_Resolution.Message,
                    RuntimeDebugViewBindingStatus.NoInstance => Kind == RuntimeDebugViewKind.Graph
                        ? "The current target has not executed this Graph."
                        : "The current target has not executed this Timeline.",
                    RuntimeDebugViewBindingStatus.NoSelection => "Enable Follow or select a runtime instance.",
                    RuntimeDebugViewBindingStatus.MultipleRuntimeInstances => "Multiple runtime instances are available. Pin one execution.",
                    RuntimeDebugViewBindingStatus.PinnedInstanceMissing => "The pinned runtime instance is absent from this snapshot.",
                    _ => string.Empty
                };
            }
        }

        public void Configure(RuntimeDebugTargetRequest request)
        {
            if (m_Request.Equals(request))
                return;

            bool preservePinnedInstance = Pinned && m_Request.Source.Equals(request.Source);
            m_Request = request;
            if (preservePinnedInstance)
                return;
            m_SelectedInstance = default;
            m_Mode = RuntimeDebugViewBindingMode.Following;
            m_BoundCharacterRuntimeId = Guid.Empty;
            m_Status = RuntimeDebugViewBindingStatus.NoInstance;
        }

        public void Follow()
        {
            m_Mode = RuntimeDebugViewBindingMode.Following;
            m_SelectedInstance = default;
        }

        public void Clear()
        {
            m_Mode = RuntimeDebugViewBindingMode.None;
            m_SelectedInstance = default;
            m_Status = RuntimeDebugViewBindingStatus.NoSelection;
        }

        public void Pin(RuntimeInstanceKey instance)
        {
            m_Mode = RuntimeDebugViewBindingMode.Pinned;
            m_SelectedInstance = instance;
            m_BoundCharacterRuntimeId = instance.CharacterRuntimeId;
        }

        public void AwaitInstance(Guid characterRuntimeId)
        {
            m_Mode = RuntimeDebugViewBindingMode.Pinned;
            m_SelectedInstance = default;
            m_BoundCharacterRuntimeId = characterRuntimeId;
            m_Status = RuntimeDebugViewBindingStatus.NoInstance;
        }

        public void Dispose(RuntimeDebugSession session)
        {
            session?.ReleaseLiveInterest(this);
        }

        public RuntimeDebugTargetResolution Refresh(RuntimeDebugSession session, RuntimeTraceChannel channels)
        {
            m_Resolution = m_Mode == RuntimeDebugViewBindingMode.Pinned
                ? session.ResolvePinnedTarget(m_BoundCharacterRuntimeId)
                : session.ResolveTarget(m_Request);
            if (!m_Resolution.CanReadSnapshot)
            {
                session.ReleaseLiveInterest(this);
                if (m_Mode != RuntimeDebugViewBindingMode.Pinned)
                    m_SelectedInstance = default;
                return m_Resolution;
            }

            if (session.CanControlLiveTarget)
                session.EnsureLiveInterest(this, channels);
            else
                session.ReleaseLiveInterest(this);

            RuntimeDebugViewModel view = session.ViewModel;
            if (m_BoundCharacterRuntimeId != view.Target.CharacterRuntimeId)
            {
                m_BoundCharacterRuntimeId = view.Target.CharacterRuntimeId;
                m_Mode = RuntimeDebugViewBindingMode.Following;
                m_SelectedInstance = default;
            }

            if (m_Mode == RuntimeDebugViewBindingMode.Following)
            {
                IReadOnlyList<RuntimeInstanceKey> instances = ResolveInstances(view);
                if (instances.Count > 1)
                {
                    m_SelectedInstance = default;
                    m_Status = RuntimeDebugViewBindingStatus.MultipleRuntimeInstances;
                    return m_Resolution;
                }

                m_SelectedInstance = instances.Count > 0 ? instances[0] : default;
                m_Status = m_SelectedInstance.IsValid
                    ? RuntimeDebugViewBindingStatus.Ready
                    : RuntimeDebugViewBindingStatus.NoInstance;
                return m_Resolution;
            }

            if (m_Mode == RuntimeDebugViewBindingMode.Pinned)
            {
                m_Status = !m_SelectedInstance.IsValid ? RuntimeDebugViewBindingStatus.NoInstance :
                    (Kind == RuntimeDebugViewKind.Graph
                        ? view.ContainsGraphInstance(m_Request.Source.GraphAuthoringId, m_SelectedInstance)
                        : view.TryGetTimelinePlaybackSummary(m_Request.Source.TimelineAuthoringId, m_SelectedInstance,
                            out _, m_Request.Source.GraphAuthoringId))
                    ? RuntimeDebugViewBindingStatus.Ready
                    : RuntimeDebugViewBindingStatus.PinnedInstanceMissing;
                return m_Resolution;
            }

            m_Status = RuntimeDebugViewBindingStatus.NoSelection;
            return m_Resolution;
        }

        IReadOnlyList<RuntimeInstanceKey> ResolveInstances(RuntimeDebugViewModel view)
        {
            if (Kind != RuntimeDebugViewKind.Graph)
                view.CopyTimelineInstances(
                    m_Request.Source.TimelineAuthoringId,
                    m_Request.Source.GraphAuthoringId,
                    m_InstanceScratch);

            else
                view.CopyGraphInstances(m_Request.Source.GraphAuthoringId, m_InstanceScratch);
            return m_InstanceScratch;
        }

    }
}
