using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [TrackGroup("Base"), ScriptGuid("31085f11443fe1347b871c5d69db3774"), IconGuid("c7e6321ad8b220a4aa9932b760affad7"), Ordered(3), Color(201, 060, 032)]
#if UNITY_EDITOR
    [TimelineAuthoringTrackField("executionDomain", "timeline_tree_execution_domain_missing", "Tree Track必须选择Logic或Presentation执行域。")]
#endif
    public class TreeTrack : Track
#if UNITY_EDITOR
        , ITimelineAuthoringTrackFieldSink
#endif
    {
        public override string ContractKind => TimelineContractKinds.TreeTrack;

#if UNITY_EDITOR
        public string ReadAuthoringField(string fieldId) => ExecutionDomain.ToString();

        public void ApplyAuthoringField(string fieldId, string value)
        {
            if (fieldId != "executionDomain" ||
                !Enum.TryParse(value, true, out TimelineExecutionDomain executionDomain) ||
                executionDomain != TimelineExecutionDomain.Logic && executionDomain != TimelineExecutionDomain.Presentation)
            {
                throw new ArgumentException("Tree Track执行域必须是Logic或Presentation。", nameof(value));
            }
            ConfigureExecutionDomain(executionDomain);
        }

        public override Type ClipType => typeof(TreeClip);

        public override Clip AddClip(OrchestrationTime time)
        {
            throw new InvalidOperationException("TreeClip必须绑定正式的Timeline节点图资产。");
        }

        public override Clip AddClip(UnityEngine.Object referenceObject, OrchestrationTime time)
        {
            if (referenceObject is not ScriptableObject asset || asset is not ITimelineTreeGraphAsset graph || !graph.IsTimelineTree)
                throw new ArgumentException("TreeClip必须绑定正式的Timeline节点图资产。", nameof(referenceObject));
            var clip = new TreeClip(this, time, asset);
            clip.RegenerateAuthoringIdentity();
            if (ExecutionDomain == TimelineExecutionDomain.Presentation)
                clip.SetExitSource(TimelineClipExitSource.FrameBoundary);
            m_Clips.Add(clip);
            return clip;
        }

        public override bool DragValid()
        {
            return UnityEditor.DragAndDrop.objectReferences.Length == 1 &&
                   UnityEditor.DragAndDrop.objectReferences[0] is ScriptableObject asset &&
                   asset is ITimelineTreeGraphAsset graph && graph.IsTimelineTree;
        }
#endif
    }

    [Serializable]
    [ScriptGuid("31085f11443fe1347b871c5d69db3774"), Color(201, 060, 032)]
    public partial class TreeClip : Clip, ITimelineOwnedAuthoringIdentity, ITimelineClipExecutionPhaseSource, ITimelineClipExitSource
#if UNITY_EDITOR
        , ITimelineTerminalTimeAlignedClip, ITimelineTreeClipAuthoringSource
#endif
    {
        public override string ContractKind => TimelineContractKinds.TreeClip;

        [SerializeField, ShowInInspector, OnValueChanged("OnClipChanged", "RepaintInspector")]
        TimelineTreeExecutionPhase m_ExecutionPhase = TimelineTreeExecutionPhase.Commit;

        [SerializeField, ShowInInspector, OnValueChanged("OnClipChanged", "RepaintInspector")]
        TimelineClipExitSource m_ExitSource = TimelineClipExitSource.TreeDecision;

#if UNITY_EDITOR
        [SerializeField]
        ScriptableObject m_AssetTree;


        public ScriptableObject AssetTree => m_AssetTree;
#endif

        public TimelineTreeExecutionPhase ExecutionPhase => m_ExecutionPhase;
        public TimelineClipExitSource ClipExitSource => m_ExitSource;
        [SerializeField] bool m_HasDeclaredEndTime;
        public bool HasDeclaredEndTime => m_HasDeclaredEndTime;
        public override TimelineContentParameters CaptureContentParameters() => new TimelineContentTree(HasDeclaredEndTime);

        public void SetExitSource(TimelineClipExitSource source)
        {
            if (!Enum.IsDefined(typeof(TimelineClipExitSource), source))
                throw new ArgumentOutOfRangeException(nameof(source), source, "TimelineTree exit source is invalid.");
            if (ExecutionDomain == TimelineExecutionDomain.Logic && source != TimelineClipExitSource.TreeDecision)
                throw new InvalidOperationException("Logic TreeClip必须由节点图决定退出。");
            m_ExitSource = source;
            if (source == TimelineClipExitSource.FrameBoundary)
                m_HasDeclaredEndTime = false;
#if UNITY_EDITOR
            OnClipChanged();
#endif
        }
        public TimelineClipExecutionPhase TimelineExecutionPhase =>
            m_ExecutionPhase == TimelineTreeExecutionPhase.Decision
                ? TimelineClipExecutionPhase.Decision
                : TimelineClipExecutionPhase.Commit;

        public void SetExecutionPhase(TimelineTreeExecutionPhase phase)
        {
            if (!Enum.IsDefined(typeof(TimelineTreeExecutionPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase), phase, "TimelineTree execution phase is invalid.");
            m_ExecutionPhase = phase;
#if UNITY_EDITOR
            OnClipChanged();
#endif
        }

#if UNITY_EDITOR
        public bool AlignTerminalTime(OrchestrationTime terminalTime)
        {
            if (m_ExitSource != TimelineClipExitSource.TreeDecision)
            {
                return false;
            }
            OrchestrationTime endTime;
            OrchestrationTime declaredEnd = default;
            bool declared = m_AssetTree is ITimelineTreeGraphAsset graph && graph.TryGetDeclaredEndTime(out declaredEnd);
            if (declared)
            {
                if (declaredEnd <= StartTime)
                    throw new InvalidOperationException($"TreeClip '{AuthoringId}' 的结束声明必须晚于起点 {StartTime.AsSingle}。");
                endTime = declaredEnd;
            }
            else
            {
                endTime = OrchestrationTime.Max(StartTime + OrchestrationTime.FromRaw(1), terminalTime);
            }
            bool changed = m_HasDeclaredEndTime != declared;
            m_HasDeclaredEndTime = declared;
            if (EndTime == endTime && !changed)
                return false;
            ConfigureTimeRange(StartTime, endTime);
            return true;
        }
#endif

        public override void Init(Track track)
        {
            base.Init(track);
#if UNITY_EDITOR
            if (!Timeline.IsTimeRangeWorkspace && Timeline.SerializedOwner && m_AssetTree is ITimelineTreeGraphAsset graph)
                graph.BindTimelineAuthoringContext(Timeline, this);
#endif
        }

#if UNITY_EDITOR
        public override string Name => $"{ExecutionDomain} / {m_ExecutionPhase} / {(m_AssetTree ? m_AssetTree.name : "Unbound")}";
#else
        public override string Name => $"{ExecutionDomain} / {m_ExecutionPhase}";
#endif
#if UNITY_EDITOR
        public override ClipCapabilities Capabilities =>
            m_ExitSource == TimelineClipExitSource.TreeDecision
                ? ClipCapabilities.TickQuantized
                : ClipCapabilities.Resizable | ClipCapabilities.TickQuantized;

        public TreeClip(Track track, OrchestrationTime time) : base(track, time)
        {
        }

        public TreeClip(Track track, OrchestrationTime time, ScriptableObject assetTree) : base(track, time)
        {
            SetAssetTree(assetTree);
        }

        public void RegenerateOwnedAuthoringIdentity()
        {
        }

        public void SetAssetTree(ScriptableObject asset)
        {
            if (asset is not ITimelineTreeGraphAsset graph || !graph.IsTimelineTree)
                throw new ArgumentException("TreeClip需要支持Timeline生命周期入口的正式节点图资产。", nameof(asset));
            m_AssetTree = asset;
            OnClipChanged();
        }

        void OnClipChanged()
        {
            OnNameChanged?.Invoke();
        }
#endif
    }

    public static class TreeTimelineContracts
    {
        public static ITimelineContractProvider CreateProvider(
            TimelineContentParameterCodec<TimelineContentTree> parameterCodec) => new TimelineContractProvider(
            new[]
            {
                new TimelineTrackContract(
                    TimelineContractKinds.TreeTrack,
                    TimelineTrackOverlapPolicy.Reject,
                    TimelineContentKind.Discrete,
                    TimelineExecutionDomain.Logic,
                    TimelineOutputKind.LogicFact,
                    TimelineExecutionDomainMask.Logic |
                    TimelineExecutionDomainMask.Presentation,
                    null,
                    TimelineContractKinds.TreeClip)
            },
            new[]
            {
                new TimelineClipContract(
                    TimelineContractKinds.TreeClip,
                    TimelineContractKinds.TreeTrack,
                    TimelineClipExecutionPhase.DecisionAndCommit,
                    TimelineContentKind.Discrete,
                    true,
                    true,
                    TimelineExecutionDomain.Logic,
                    TimelineOutputKind.LogicFact,
                    TimelineExecutionDomainMask.Logic | TimelineExecutionDomainMask.Presentation,
                    ValidateClip)
            },
            new ITimelineContentParameterCodec[] { parameterCodec });

        static void ValidateClip(Clip clip, List<string> errors)
        {
            if (clip is not TreeClip treeClip)
            {
                errors?.Add($"Timeline TreeClip '{clip?.AuthoringId}' is invalid.");
                return;
            }
            switch (treeClip.ExecutionDomain)
            {
                case TimelineExecutionDomain.Logic:
                    if (treeClip.ClipExitSource != TimelineClipExitSource.TreeDecision)
                        errors?.Add($"Timeline Logic TreeClip '{treeClip.AuthoringId}' must use TreeDecision exit.");
#if UNITY_EDITOR
                    ((ITimelineTreeGraphAsset)treeClip.AssetTree).ValidateTimelineBinding(treeClip.Timeline, treeClip, errors, true);
#endif
                    break;
                case TimelineExecutionDomain.Presentation:
                    if (treeClip.ClipExitSource != TimelineClipExitSource.FrameBoundary && treeClip.ClipExitSource != TimelineClipExitSource.TreeDecision)
                        errors?.Add($"Timeline Presentation TreeClip '{treeClip.AuthoringId}' has an invalid exit source.");
                    if (treeClip.ExecutionPhase != TimelineTreeExecutionPhase.Commit)
                        errors?.Add($"Timeline Presentation TreeClip '{treeClip.AuthoringId}' must use Commit phase.");
                    break;
                default:
                    errors?.Add($"Timeline TreeClip '{treeClip.AuthoringId}' has an invalid execution domain.");
                    break;
            }
        }
    }
}
