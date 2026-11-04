using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace BTSMTL.Timeline
{
#if UNITY_5_3_OR_NEWER
    [ScriptGuid("31085f11443fe1347b871c5d69db3774"), IconGuid("c7e6321ad8b220a4aa9932b760affad7"), Ordered(3), Color(201, 060, 032)]
#endif
    public partial class TreeTrack : Track
    {
        public override Type ClipType => typeof(TreeClip);
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("executionDomain", typeof(TimelineExecutionDomain),
            AllowedValues = new[] { "Logic", "Presentation" })]
        [TimelineAuthoringTrackField("timeline_tree_execution_domain_missing", "Tree Track必须选择Logic或Presentation执行域。")]
        public new TimelineExecutionDomain ExecutionDomain
        {
            get => base.ExecutionDomain;
            set => ConfigureExecutionDomain(value);
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
#if UNITY_5_3_OR_NEWER
    [ScriptGuid("31085f11443fe1347b871c5d69db3774"), Color(201, 060, 032)]
#endif
    public partial class TreeClip : Clip, ITimelineOwnedAuthoringIdentity, ITimelineClipExecutionPhaseSource, ITimelineClipExitSource
#if UNITY_EDITOR
        , ITimelineTerminalTimeAlignedClip
#endif
#if UNITY_EDITOR || BTSMTL_AUTHORING
        , ITimelineTreeClipAuthoringSource
#endif
    {
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("assetTree", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.AssetReference,
            AuthoringVisible = false)]
        [TimelineAuthoringProperty(IsSourceAsset = true)]
        public ScriptableObject AssetTree => m_AssetTree.Length == 0 ? null : Timeline.AuthoringDocument.ResolveGraph(m_AssetTree);
#endif
#if UNITY_EDITOR || BTSMTL_AUTHORING
        public string AssetTreeId => m_AssetTree;
#endif

#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("executionPhase", typeof(TimelineTreeExecutionPhase), AuthoringVisible = false)]
#endif
        public TimelineTreeExecutionPhase ExecutionPhase => m_ExecutionPhase;
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("exitSource", typeof(TimelineClipExitSource), AuthoringVisible = false)]
#endif
        public TimelineClipExitSource ClipExitSource => m_ExitSource;
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
            OrchestrationTime declaredEnd = default;
            bool declared = AssetTree is ITimelineTreeGraphAsset graph && graph.TryGetDeclaredEndTime(out declaredEnd);
            OrchestrationTime endTime = TreeTimelineDefinition.TerminalTime(AuthoringId, StartTime, terminalTime,
                declared ? declaredEnd : null);
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
            if (Timeline.AuthoringDocument != null && m_AssetTree.Length != 0)
                ((ITimelineTreeGraphAsset)AssetTree).BindTimelineAuthoringContext(Timeline, this);
#endif
        }

#if UNITY_EDITOR
        public override string Name => $"{ExecutionDomain} / {m_ExecutionPhase} / {(m_AssetTree.Length == 0 ? "Unbound" : AssetTree.name)}";
#else
        public override string Name => $"{ExecutionDomain} / {m_ExecutionPhase}";
#endif
#if UNITY_EDITOR
        public override void ConfigureAuthoringReference(UnityEngine.Object referenceObject)
        {
            SetAssetTree(referenceObject as ScriptableObject);
            SetExitSource(TreeTimelineDefinition.ConfiguredExitSource(ExecutionDomain, TimelineClipExitSource.FrameBoundary));
        }

        public void RegenerateOwnedAuthoringIdentity()
        {
        }

        public void SetAssetTree(ScriptableObject asset)
        {
            if (asset is not ITimelineTreeGraphAsset graph || !graph.IsTimelineTree)
                throw new ArgumentException("TreeClip需要支持Timeline生命周期入口的正式节点图资产。", nameof(asset));
            m_AssetTree = graph.AuthoringId;
            if (Track != null && Timeline.AuthoringDocument != null)
                TimelineAuthoringGraphBindings.Prepare(Timeline, asset);
            OnClipChanged();
        }

        [BTSMTL.Authoring.Graph.GraphAuthoringConfigure]
        public void ConfigureTree(ScriptableObject assetTree, TimelineTreeExecutionPhase executionPhase,
            TimelineClipExitSource exitSource)
        {
            SetAssetTree(assetTree);
            SetExecutionPhase(executionPhase);
            SetExitSource(TreeTimelineDefinition.ConfiguredExitSource(ExecutionDomain, exitSource));
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
