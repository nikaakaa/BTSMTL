using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [ScriptGuid("31085f11443fe1347b871c5d69db3774"), IconGuid("c7e6321ad8b220a4aa9932b760affad7"), Ordered(3), Color(201, 060, 032)]
    public partial class TreeTrack : Track
    {
        public override string ContractKind => TimelineContractKinds.TreeTrack;

#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("executionDomain", typeof(TimelineExecutionDomain),
            AllowedValues = new[] { "Logic", "Presentation" })]
        [TimelineAuthoringTrackField("timeline_tree_execution_domain_missing", "Tree Track必须选择Logic或Presentation执行域。")]
        public new TimelineExecutionDomain ExecutionDomain
        {
            get => base.ExecutionDomain;
            set => ConfigureExecutionDomain(value);
        }

        public override Type ClipType => typeof(TreeClip);

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
        TimelineTreeExecutionPhase m_ExecutionPhase = TreeTimelineDefinition.DefaultExecutionPhase;

        [SerializeField, ShowInInspector, OnValueChanged("OnClipChanged", "RepaintInspector")]
        TimelineClipExitSource m_ExitSource = TreeTimelineDefinition.DefaultExitSource;

#if UNITY_EDITOR
        [SerializeField]
        ScriptableObject m_AssetTree;


        [BTSMTL.Authoring.Graph.GraphAuthoringField("assetTree", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.AssetReference,
            AuthoringVisible = false)]
        [TimelineAuthoringProperty(IsSourceAsset = true)]
        public ScriptableObject AssetTree => m_AssetTree;
#endif

#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("executionPhase", typeof(TimelineTreeExecutionPhase), AuthoringVisible = false)]
#endif
        public TimelineTreeExecutionPhase ExecutionPhase => m_ExecutionPhase;
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("exitSource", typeof(TimelineClipExitSource), AuthoringVisible = false)]
#endif
        public TimelineClipExitSource ClipExitSource => m_ExitSource;
        [SerializeField] bool m_HasDeclaredEndTime;
        public bool HasDeclaredEndTime => m_HasDeclaredEndTime;
        public override TimelineContentParameters CaptureContentParameters() => new TimelineContentTree(HasDeclaredEndTime);

        public void SetExitSource(TimelineClipExitSource source)
        {
            TreeTimelineDefinition.RequireExitSource(ExecutionDomain, source);
            m_ExitSource = source;
            if (source == TimelineClipExitSource.FrameBoundary)
                m_HasDeclaredEndTime = false;
#if UNITY_EDITOR
            OnClipChanged();
#endif
        }
        public TimelineClipExecutionPhase TimelineExecutionPhase => TreeTimelineDefinition.ExecutionPhase(m_ExecutionPhase);

        public void SetExecutionPhase(TimelineTreeExecutionPhase phase)
        {
            TreeTimelineDefinition.RequireExecutionPhase(phase);
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
            if (Timeline.SerializedOwner && m_AssetTree is ITimelineTreeGraphAsset graph)
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

        public override void ConfigureAuthoringReference(UnityEngine.Object referenceObject)
        {
            SetAssetTree(referenceObject as ScriptableObject);
            SetExitSource(ExecutionDomain == TimelineExecutionDomain.Logic
                ? TimelineClipExitSource.TreeDecision
                : TimelineClipExitSource.FrameBoundary);
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

        [BTSMTL.Authoring.Graph.GraphAuthoringConfigure]
        public void ConfigureTree(ScriptableObject assetTree, TimelineTreeExecutionPhase executionPhase,
            TimelineClipExitSource exitSource)
        {
            SetAssetTree(assetTree);
            SetExecutionPhase(executionPhase);
            SetExitSource(ExecutionDomain == TimelineExecutionDomain.Logic ? TimelineClipExitSource.TreeDecision : exitSource);
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
            new[] { TreeTimelineDefinition.Track },
            new[] { new TimelineClipContract(TreeTimelineDefinition.Clip, ValidateClip) },
            new ITimelineContentParameterCodec[] { parameterCodec });

        static void ValidateClip(Clip clip, List<string> errors)
        {
            if (clip is not TreeClip treeClip)
            {
                errors?.Add($"Timeline TreeClip '{clip?.AuthoringId}' is invalid.");
                return;
            }
            TreeTimelineDefinition.Validate(treeClip.AuthoringId, treeClip.ExecutionDomain, treeClip.ClipExitSource,
                treeClip.ExecutionPhase, errors);
#if UNITY_EDITOR
            if (treeClip.ExecutionDomain == TimelineExecutionDomain.Logic)
                ((ITimelineTreeGraphAsset)treeClip.AssetTree).ValidateTimelineBinding(treeClip.Timeline, treeClip, errors, true);
#endif
        }
    }
}
