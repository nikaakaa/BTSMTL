using System;
using BTSMTL.Orchestration;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using BTSMTL.Diagnostics;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public sealed partial class TimelineData
    {
        public event Action OnValueChanged;
        public ulong AuthoringRevision { get; private set; }

        public int MaxFrame => TimelineTimeGrid.CeilingIndex(DurationTime, TimelineUtility.FrameRate);
        public OrchestrationTime DurationTime { get; private set; }
        public float Duration => DurationTime.AsSingle;

        public void Init()
        {
#if UNITY_EDITOR
            if (m_AuthoringBatchDepth != 0)
                return;
#endif
            DurationTime = OrchestrationTime.Zero;
            foreach (var track in m_Tracks)
            {
                track.Init(this);
                DurationTime = OrchestrationTime.Max(DurationTime, track.DurationTime);
            }
            for (int i = 0; i < m_Sections.Count; i++)
            {
                TimelineSection section = m_Sections[i];
                if (section != null)
                    DurationTime = OrchestrationTime.Max(DurationTime, section.Time);
            }
            AuthoringRevision++;
            OnValueChanged?.Invoke();
        }
    }

    [Serializable]
    public abstract partial class Track
    {
        [SerializeField, HideInInspector]
        string m_AuthoringId;

        public string Name;
        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public virtual string ContractKind => string.Empty;

        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        TimelineExecutionDomain m_ExecutionDomain;

        public TimelineExecutionDomain ExecutionDomain => m_ExecutionDomain;
        public bool HasExplicitExecutionDomain => TimelineExecutionDomains.IsValid(m_ExecutionDomain);

        [SerializeField]
        protected bool m_PersistentMuted;
        public bool PersistentMuted
        {
            get => m_PersistentMuted;
            set
            {
                if (m_PersistentMuted != value)
                {
                    m_PersistentMuted = value;
                    OnMutedStateChanged?.Invoke();
                }
            }
        }

        [SerializeReference]
        protected List<Clip> m_Clips = new List<Clip>();
        public List<Clip> Clips => m_Clips;

        [SerializeField]
        List<TimelineMarker> m_Markers = new List<TimelineMarker>();
        public List<TimelineMarker> Markers => m_Markers;

        public Action OnUpdateMix;
        public Action OnMutedStateChanged;

        public TimelineData Timeline { get; protected set; }
        public OrchestrationTime DurationTime { get; protected set; }

        internal void BindTimeline(TimelineData timeline) => Timeline = timeline;

        public virtual void Init(TimelineData timeline)
        {
            BindTimeline(timeline);

            DurationTime = OrchestrationTime.Zero;
            foreach (var clip in m_Clips)
            {
                clip.Init(this);
                DurationTime = OrchestrationTime.Max(DurationTime, clip.EndTime);
            }
            foreach (var marker in m_Markers)
            {
                marker.Init(this);
                DurationTime = OrchestrationTime.Max(DurationTime, OrchestrationTime.Max(OrchestrationTime.FromRaw(1), marker.Time));
            }

        }

#if UNITY_EDITOR
        public void ConfigureExecutionDomain(TimelineExecutionDomain executionDomain)
        {
            if (!TimelineExecutionDomains.IsValid(executionDomain))
                throw new ArgumentOutOfRangeException(nameof(executionDomain));
            m_ExecutionDomain = executionDomain;
        }

        public bool EnsureAuthoringIdentity()
        {
            if (AuthoringIdentity.IsValid(m_AuthoringId))
                return false;
            m_AuthoringId = AuthoringIdentity.Create();
            return true;
        }

        public void RegenerateAuthoringIdentity()
        {
            m_AuthoringId = AuthoringIdentity.Create();
        }

        public TimelineMarker EnsureMarker(string identity, OrchestrationTime time, ScriptableObject graph, bool forceTriggerOnInitialSeek = false)
        {
            TimelineMarker marker = m_Markers.SingleOrDefault(value => value.AuthoringId == identity);
            if (marker == null)
            {
                marker = new TimelineMarker();
                m_Markers.Add(marker);
            }
            marker.ConfigureAuthoringIdentity(identity);
            marker.Configure(time, graph, forceTriggerOnInitialSeek);
            marker.BindTrack(this);
            return marker;
        }

        public void RemoveMarker(TimelineMarker marker)
        {
            if (marker == null || !m_Markers.Remove(marker))
                throw new ArgumentException("Timeline Marker is not owned by this Track.", nameof(marker));
        }

        public void ConfigureAuthoringIdentity(string authoringId)
        {
            if (!AuthoringIdentity.IsValid(authoringId))
                throw new ArgumentException("Timeline Track authoring identity is invalid.", nameof(authoringId));
            m_AuthoringId = authoringId;
        }
#endif
    }

    [Serializable]
    public abstract partial class Clip
    {
        [SerializeField, HideInInspector]
        string m_AuthoringId;

        #region Time
        [SerializeField]
        long m_StartTimeFPRawValue;
        [SerializeField]
        long m_EndTimeFPRawValue;
        [SerializeField]
        long m_OtherEaseInTimeFPRawValue;
        [SerializeField]
        long m_OtherEaseOutTimeFPRawValue;
        [SerializeField]
        long m_SelfEaseInTimeFPRawValue;
        [SerializeField]
        long m_SelfEaseOutTimeFPRawValue;
        [SerializeField]
        long m_ClipInTimeFPRawValue;

        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public virtual string ContractKind => string.Empty;
        public TimelineExecutionDomain ExecutionDomain => Track != null
            ? Track.ExecutionDomain
            : throw new InvalidOperationException("Timeline Clip has no owning Track.");
        #endregion

        #region Time
        public OrchestrationTime StartTime => OrchestrationTime.FromRaw(m_StartTimeFPRawValue);
        public OrchestrationTime EndTime => OrchestrationTime.FromRaw(m_EndTimeFPRawValue);
        public OrchestrationTime SelfEaseInTime => OrchestrationTime.FromRaw(m_SelfEaseInTimeFPRawValue);
        public OrchestrationTime SelfEaseOutTime => OrchestrationTime.FromRaw(m_SelfEaseOutTimeFPRawValue);
        public OrchestrationTime OtherEaseInTime => OrchestrationTime.FromRaw(m_OtherEaseInTimeFPRawValue);
        public OrchestrationTime OtherEaseOutTime => OrchestrationTime.FromRaw(m_OtherEaseOutTimeFPRawValue);
        public OrchestrationTime EaseInTime => OtherEaseInTime.RawValue == 0 ? SelfEaseInTime : OtherEaseInTime;
        public OrchestrationTime EaseOutTime => OtherEaseOutTime.RawValue == 0 ? SelfEaseOutTime : OtherEaseOutTime;
        public OrchestrationTime ClipInTime => OrchestrationTime.FromRaw(m_ClipInTimeFPRawValue);
        public OrchestrationTime DurationTime => EndTime - StartTime;
        public abstract TimelineContentParameters CaptureContentParameters();
        public virtual IReadOnlyList<TimelineCurveChannelDescriptor> CurveChannels =>
            Array.Empty<TimelineCurveChannelDescriptor>();
        public virtual TimelineContentSampling CaptureSampling(int weightCurveIndex, int easeInCurveIndex,
            int easeOutCurveIndex) => default;

        #endregion

        [NonSerialized]
        public Track Track;
        public TimelineData Timeline => Track.Timeline;
        public virtual string Name => GetType().Name;

        public Action OnNameChanged;
        public Action OnInspectorRepaint;

        internal void BindTrack(Track track) => Track = track;

        public virtual void Init(Track track) => BindTrack(track);

        public virtual TimelineClipTimeRange CaptureTimeRange() => new TimelineClipTimeRange(
            StartTime, EndTime, SelfEaseInTime, SelfEaseOutTime, ClipInTime,
            0f, 0f, OtherEaseInTime, OtherEaseOutTime);

#if UNITY_EDITOR
        public virtual void ApplyTimeRange(in TimelineClipTimeRange range)
        {
            m_StartTimeFPRawValue = range.Start.RawValue;
            m_EndTimeFPRawValue = range.End.RawValue;
            m_SelfEaseInTimeFPRawValue = range.EaseIn.RawValue;
            m_SelfEaseOutTimeFPRawValue = range.EaseOut.RawValue;
            m_ClipInTimeFPRawValue = range.ClipIn.RawValue;
        }

        public void ConfigureTimeRange(OrchestrationTime startTime, OrchestrationTime endTime)
        {
            if (startTime < OrchestrationTime.Zero || endTime < startTime)
                throw new ArgumentOutOfRangeException(nameof(startTime));
            m_StartTimeFPRawValue = startTime.RawValue;
            m_EndTimeFPRawValue = endTime.RawValue;
        }

        public void ConfigureEase(OrchestrationTime easeIn, OrchestrationTime easeOut)
        {
            if (easeIn < OrchestrationTime.Zero || easeOut < OrchestrationTime.Zero)
                throw new ArgumentOutOfRangeException(nameof(easeIn));
            m_SelfEaseInTimeFPRawValue = easeIn.RawValue;
            m_SelfEaseOutTimeFPRawValue = easeOut.RawValue;
        }

        public void ConfigureClipIn(OrchestrationTime time)
        {
            if (time < OrchestrationTime.Zero)
                throw new ArgumentOutOfRangeException(nameof(time));
            m_ClipInTimeFPRawValue = time.RawValue;
        }

        public bool EnsureAuthoringIdentity()
        {
            if (AuthoringIdentity.IsValid(m_AuthoringId))
                return false;
            m_AuthoringId = AuthoringIdentity.Create();
            return true;
        }

        public void RegenerateAuthoringIdentity()
        {
            m_AuthoringId = AuthoringIdentity.Create();
        }

        public void ConfigureAuthoringIdentity(string authoringId)
        {
            if (!AuthoringIdentity.IsValid(authoringId))
                throw new ArgumentException("Timeline Clip authoring identity is invalid.", nameof(authoringId));
            m_AuthoringId = authoringId;
        }
#endif

    }

    public abstract partial class SignalClip : Clip { }

#if UNITY_EDITOR

    public partial class TimelineData
    {
        public UnityEditor.SerializedObject SerializedTimeline;
        public UnityEditor.SerializedProperty SerializedData;

        public Track EnsureTrack(Type type, TimelineContractCatalog catalog, string identity, string name,
            TimelineExecutionDomain? executionDomain = null, bool muted = false)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            Track track = m_Tracks.SingleOrDefault(value => value.AuthoringId == identity);
            if (track != null && track.GetType() != type)
                throw new InvalidOperationException($"Timeline track identity '{identity}' has a different type.");
            if (track == null)
            {
                track = (Track)Activator.CreateInstance(type);
                m_Tracks.Add(track);
            }
            TimelineTrackContract contract = catalog.RequireTrack(track.ContractKind);
            TimelineExecutionDomain domain = executionDomain ?? contract.ExecutionPolicy.Domain;
            if (!contract.SupportsExecutionDomain(domain))
                throw new InvalidOperationException($"Timeline track '{contract.Kind}' does not support execution domain '{domain}'.");
            track.ConfigureAuthoringIdentity(identity);
            track.ConfigureExecutionDomain(domain);
            track.Name = name ?? type.Name.Replace("Track", string.Empty);
            track.PersistentMuted = muted;
            track.BindTimeline(this);
            Init();
            return track;
        }
        public void RemoveTrack(Track track)
        {
            m_Tracks.Remove(track);
            Init();
        }
        public Clip EnsureClip(TimelineContractCatalog catalog, Track track, string identity,
            UnityEngine.Object referenceObject, OrchestrationTime startTime, OrchestrationTime? endTime = null,
            OrchestrationTime easeIn = default, OrchestrationTime easeOut = default, OrchestrationTime clipIn = default)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            Clip clip = m_Tracks.SelectMany(value => value.Clips).SingleOrDefault(value => value.AuthoringId == identity);
            if (clip != null && clip.GetType() != track.ClipType)
                throw new InvalidOperationException($"Timeline clip identity '{identity}' has a different type.");
            if (clip == null)
                clip = (Clip)Activator.CreateInstance(track.ClipType);
            PlaceClip(clip, track);
            clip.ConfigureAuthoringIdentity(identity);
            clip.ConfigureAuthoringReference(referenceObject);
            clip.ConfigureTimeRange(startTime, endTime ?? TimelineAuthoringClipBinding.DefaultEndTime(clip, startTime, referenceObject));
            clip.ConfigureEase(easeIn, easeOut);
            clip.ConfigureClipIn(clipIn);
            catalog.RequireClipPlacement(track, clip);
            track.UpdateMix();
            Init();
            return clip;
        }
        public void SeparateOverlappingTreeClips(TimelineContractCatalog catalog)
        {
            Track[] tracks = m_Tracks.ToArray();
            foreach (Track track in tracks)
            {
                int insertionIndex = m_Tracks.IndexOf(track) + 1;
                TimelineTrackContract contract = catalog.RequireTrack(track.ContractKind);
                if (contract.ContentKind != TimelineContentKind.Discrete || contract.OverlapPolicy != TimelineTrackOverlapPolicy.Reject)
                    continue;
                for (int clipIndex = 1; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    bool overlaps = false;
                    for (int previousIndex = 0; previousIndex < clipIndex; previousIndex++)
                    {
                        Clip previous = track.Clips[previousIndex];
                        overlaps |= clip.StartTime < previous.EndTime && previous.StartTime < clip.EndTime;
                    }
                    if (!overlaps)
                        continue;
                    Track destination = EnsureTrack(track.GetType(), catalog,
                        new Guid(StableHash.Compute("timeline.track", track.AuthoringId, clip.AuthoringId).Value.Substring(0, 32)).ToString("D"),
                        clip.Name, track.ExecutionDomain, track.PersistentMuted);
                    m_Tracks.Remove(destination);
                    m_Tracks.Insert(insertionIndex++, destination);
                    MoveClip(catalog, clip, destination);
                    clipIndex--;
                }
            }
        }

        public void MoveClip(TimelineContractCatalog catalog, Clip clip, Track destination)
        {
            if (clip.Track == destination)
                return;
            catalog.RequireClipPlacement(destination, clip);
            PlaceClip(clip, destination);
            destination.UpdateMix();
            Init();
        }

        void PlaceClip(Clip clip, Track destination)
        {
            Track source = clip.Track;
            if (!m_Tracks.Contains(destination) || source != null && source != destination &&
                (!m_Tracks.Contains(source) || source.ExecutionDomain != destination.ExecutionDomain))
                throw new InvalidOperationException("片段必须属于当前Timeline的轨道，跨轨道移动必须保持执行域。");
            if (source != destination)
            {
                if (source != null)
                    source.RemoveClip(clip);
                destination.Clips.Add(clip);
            }
            clip.BindTrack(destination);
        }

        public void RemoveClip(Clip clip)
        {
            clip.Track.RemoveClip(clip);

            Init();
        }
        public TimelineExternalBindingDeclaration EnsureExternalBinding(
            string identity,
            string bindingId,
            string displayName,
            string domain,
            TimelineBindingValueKind valueKind,
            TimelineBindingAccess access,
            TimelineBindingLifetime lifetime,
            string parameterId = null)
        {
            TimelineExternalBindingDeclaration binding = m_ExternalBindings.SingleOrDefault(value => value.AuthoringId == identity);
            if (binding == null)
            {
                binding = new TimelineExternalBindingDeclaration();
                m_ExternalBindings.Add(binding);
            }
            binding.ConfigureAuthoringIdentity(identity);
            binding.Configure(bindingId, displayName, domain, valueKind, access, lifetime, parameterId);
            if (m_ExternalBindings.Exists(value => value != binding && value.BindingId == binding.BindingId))
                throw new InvalidOperationException($"Timeline external binding '{binding.BindingId}' already exists.");
            AuthoringRevision++;
            OnValueChanged?.Invoke();
            return binding;
        }
        public void RemoveExternalBinding(TimelineExternalBindingDeclaration binding)
        {
            if (binding == null || !m_ExternalBindings.Remove(binding))
                throw new ArgumentException("Timeline external binding is not owned by this Timeline.", nameof(binding));
            AuthoringRevision++;
            OnValueChanged?.Invoke();
        }
#if UNITY_EDITOR
        public TimelineSection EnsureSection(string authoringId, string name, OrchestrationTime time)
        {
            TimelineSection section = m_Sections.SingleOrDefault(value =>
                value != null && string.Equals(value.AuthoringId, authoringId, StringComparison.Ordinal));
            if (section == null)
            {
                section = new TimelineSection();
                m_Sections.Add(section);
            }
            section.ConfigureAuthoringIdentity(authoringId);
            ConfigureSection(section, name, time);
            return section;
        }
#endif
        public void ConfigureSection(TimelineSection section, string name, OrchestrationTime time)
        {
            if (section == null || !m_Sections.Contains(section))
                throw new ArgumentException("Timeline Section is not owned by this Timeline.", nameof(section));
            string value = name?.Trim() ?? string.Empty;
            if (m_Sections.Exists(candidate => candidate != null && !ReferenceEquals(candidate, section) &&
                string.Equals(candidate.Name, value, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Timeline Section '{value}' already exists.");
            section.Configure(value, time);
            SortSections();
            Init();
        }

        public void ConfigureSectionNext(TimelineSection section, string nextSectionId)
        {
            if (section == null || !m_Sections.Contains(section))
                throw new ArgumentException("Timeline Section is not owned by this Timeline.", nameof(section));
            string value = nextSectionId?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(value) &&
                !m_Sections.Exists(candidate => candidate != null && candidate.AuthoringId == value))
                throw new InvalidOperationException($"Timeline Section '{section.Name}' references unknown next Section '{value}'.");
            section.SetNextSection(value);
            Init();
        }
        public void RemoveSection(TimelineSection section)
        {
            if (section == null || !m_Sections.Remove(section))
                throw new ArgumentException("Timeline Section is not owned by this Timeline.", nameof(section));
            Init();
        }
        void SortSections()
        {
            m_Sections.Sort((left, right) =>
            {
                int frame = (left?.Time ?? OrchestrationTime.MaxValue).CompareTo(right?.Time ?? OrchestrationTime.MaxValue);
                return frame != 0 ? frame : string.CompareOrdinal(left?.AuthoringId, right?.AuthoringId);
            });
        }
        public void UpdateMix()
        {
            for (int i = 0; i < m_Tracks.Count; i++)
                m_Tracks[i].UpdateMix();
        }
        public void Resort()
        {
            AuthoringRevision++;
            OnValueChanged?.Invoke();
        }

        public void ConfigureTrackAuthoring(Track track, string name, bool muted,
            TimelineExecutionDomain domain, TimelineContractCatalog catalog)
        {
            int trackIndex = m_Tracks.IndexOf(track);
            if (trackIndex < 0)
                throw new ArgumentException("轨道不属于当前 Timeline。", nameof(track));
            TimelineData candidate = Clone();
            Track candidateTrack = candidate.Tracks[trackIndex];
            candidateTrack.Name = name;
            candidateTrack.PersistentMuted = muted;
            candidateTrack.ConfigureExecutionDomain(domain);
            candidate.Init();
            TimelineContentDiscoveryResult discovery = TimelineContentDiscovery.Discover(candidate, catalog);
            if (!discovery.IsValid)
                throw new InvalidOperationException($"轨道 '{track.Name}' ({track.AuthoringId}) 无法提交为 {domain}：\n{string.Join("\n", discovery.Errors)}");
            ApplyModify(() =>
            {
                track.Name = name;
                track.PersistentMuted = muted;
                track.ConfigureExecutionDomain(domain);
            }, "修改轨道配置");
        }

        int m_AuthoringBatchDepth;
        public bool IsAuthoringBatch => m_AuthoringBatchDepth != 0;

        public IDisposable BeginAuthoringBatch()
        {
            UpdateSerializedTimeline();
            m_AuthoringBatchDepth++;
            return new AuthoringBatch(this);
        }

        sealed class AuthoringBatch : IDisposable
        {
            readonly TimelineData m_Timeline;
            public AuthoringBatch(TimelineData timeline) => m_Timeline = timeline;
            public void Dispose() => m_Timeline.m_AuthoringBatchDepth--;
        }

        public void CommitAuthoringBatch()
        {
            AlignTerminalTimeClips();
            m_AuthoringBatchDepth--;
            UpdateMix();
            Init();
            UnityEditor.EditorUtility.SetDirty(SerializedOwner);
            m_AuthoringBatchDepth++;
        }

        public void ApplyModify(Action action, string name)
        {
            if (m_AuthoringBatchDepth != 0)
            {
                action();
                return;
            }
            if (!SerializedOwner || string.IsNullOrEmpty(SerializedPropertyPath))
                throw new InvalidOperationException($"TimelineData {Name} is missing serialized owner/path.");
            UnityEditor.Undo.IncrementCurrentGroup();
            int undoGroup = UnityEditor.Undo.GetCurrentGroup();
            string undoName = $"Timeline: {name}";
            UnityEditor.Undo.SetCurrentGroupName(undoName);
            UnityEditor.Undo.RegisterCompleteObjectUndo(SerializedOwner, undoName);
            try
            {
                UpdateSerializedTimeline();
                action?.Invoke();
                Init();
                if (AlignTerminalTimeClips())
                    Init();
                UnityEditor.EditorUtility.SetDirty(SerializedOwner);
                UnityEditor.Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                UnityEditor.Undo.FlushUndoRecordObjects();
                UnityEditor.Undo.RevertAllDownToGroup(undoGroup);
                UpdateSerializedTimeline();
                throw;
            }
        }

        public bool AlignTerminalTimeClips()
        {
            OrchestrationTime terminalTime = OrchestrationTime.Zero;
            for (int trackIndex = 0; trackIndex < m_Tracks.Count; trackIndex++)
            {
                Track track = m_Tracks[trackIndex];
                if (track == null)
                    continue;
                foreach (TimelineMarker marker in track.Markers)
                    terminalTime = OrchestrationTime.Max(terminalTime, OrchestrationTime.Max(OrchestrationTime.FromRaw(1), marker.Time));
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    if (clip == null)
                        continue;
                    if (clip is ITimelineTerminalTimeAlignedClip terminalClip)
                    {
                        terminalTime = OrchestrationTime.Max(terminalTime, clip.StartTime + OrchestrationTime.FromRaw(1));
                    }
                    else
                        terminalTime = OrchestrationTime.Max(terminalTime, clip.EndTime);
                }
            }
            for (int sectionIndex = 0; sectionIndex < m_Sections.Count; sectionIndex++)
            {
                TimelineSection section = m_Sections[sectionIndex];
                if (section != null)
                    terminalTime = OrchestrationTime.Max(terminalTime, section.Time);
            }
            bool changed = false;
            for (int trackIndex = 0; trackIndex < m_Tracks.Count; trackIndex++)
                for (int clipIndex = 0; clipIndex < m_Tracks[trackIndex].Clips.Count; clipIndex++)
                    if (m_Tracks[trackIndex].Clips[clipIndex] is ITimelineTerminalTimeAlignedClip terminalClip)
                        changed |= terminalClip.AlignTerminalTime(terminalTime);
            return changed;
        }

        public void RefreshTimeRanges()
        {
            AlignTerminalTimeClips();
            DurationTime = OrchestrationTime.Zero;
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                Track track = m_Tracks[i];
                track.UpdateMix();
                track.RefreshDuration();
                DurationTime = OrchestrationTime.Max(DurationTime, track.DurationTime);
            }
            for (int i = 0; i < m_Sections.Count; i++)
                DurationTime = OrchestrationTime.Max(DurationTime, m_Sections[i].Time);
            AuthoringRevision++;
            OnValueChanged?.Invoke();
        }

        public void UpdateSerializedTimeline()
        {
            if (!SerializedOwner || string.IsNullOrEmpty(SerializedPropertyPath))
                throw new InvalidOperationException($"TimelineData {Name} is missing serialized owner/path.");
            SerializedTimeline = new UnityEditor.SerializedObject(SerializedOwner);
            SerializedData = SerializedTimeline.FindProperty(SerializedPropertyPath);
            if (SerializedData == null)
                throw new InvalidOperationException($"TimelineData {Name} serialized path is invalid: {SerializedPropertyPath}");
        }
    }

    public abstract partial class Track
    {
        internal void RefreshDuration()
        {
            DurationTime = OrchestrationTime.Zero;
            for (int i = 0; i < Clips.Count; i++)
                DurationTime = OrchestrationTime.Max(DurationTime, Clips[i].EndTime);
            for (int i = 0; i < Markers.Count; i++)
                DurationTime = OrchestrationTime.Max(DurationTime, OrchestrationTime.Max(OrchestrationTime.FromRaw(1), Markers[i].Time));
        }

        public virtual Type ClipType => typeof(Clip);

        public void RemoveClip(Clip clip)
        {
            m_Clips.Remove(clip);
            UpdateMix();
        }
        public void UpdateMix()
        {
#if UNITY_EDITOR
            if (Timeline.IsAuthoringBatch)
                return;
#endif
            for (int i = 0; i < Clips.Count; i++)
                Clips[i].UpdateMix();
            OnUpdateMix?.Invoke();
        }
        public Color Color()
        {
            var colorAttributes = GetType().GetCustomAttributes<ColorAttribute>().ToArray();
            return colorAttributes[colorAttributes.Length - 1].Color / 255;
        }
        public virtual bool DragValid()
        {
            return false;
        }

        public void RebindTimeline()
        {
            Timeline.Init();
        }
    }

    public abstract partial class Clip
    {
        [NonSerialized]
        public bool Invalid;

        public virtual OrchestrationTime Length => DurationTime;
        public virtual ClipCapabilities Capabilities => ClipCapabilities.None;

        public virtual void ConfigureAuthoringReference(UnityEngine.Object referenceObject)
        {
            if (referenceObject)
                throw new ArgumentException($"Timeline clip '{ContractKind}' 不接受素材引用。", nameof(referenceObject));
        }

        public void UpdateMix()
        {
            m_OtherEaseInTimeFPRawValue = 0;
            m_OtherEaseOutTimeFPRawValue = 0;

            if (Invalid || !IsMixable())
                return;

            foreach (var clip in Track.Clips)
            {
                if (clip != this && !clip.Invalid)
                {
                    if (clip.StartTime < StartTime && clip.EndTime > EndTime)
                    {
                        return;
                    }
                    else if (clip.StartTime > StartTime && clip.EndTime < EndTime)
                    {
                        return;
                    }

                    if (clip.StartTime < StartTime && clip.EndTime > StartTime)
                    {
                        m_OtherEaseInTimeFPRawValue = (clip.EndTime - StartTime).RawValue;
                    }
                    if (clip.StartTime > StartTime && clip.StartTime < EndTime)
                    {
                        m_OtherEaseOutTimeFPRawValue = (EndTime - clip.StartTime).RawValue;
                    }
                    if (clip.StartTime == StartTime)
                    {
                        if (clip.EndTime < EndTime)
                        {
                            m_OtherEaseInTimeFPRawValue = (clip.EndTime - StartTime).RawValue;
                        }
                        else if (clip.EndTime > EndTime)
                        {
                            m_OtherEaseOutTimeFPRawValue = (EndTime - StartTime).RawValue;
                        }
                    }
                    m_SelfEaseInTimeFPRawValue = OrchestrationTime.Min(SelfEaseInTime, DurationTime - OtherEaseOutTime).RawValue;
                    m_SelfEaseOutTimeFPRawValue = OrchestrationTime.Min(SelfEaseOutTime, DurationTime - OtherEaseInTime).RawValue;
                }
            }
        }
        public bool Contains(OrchestrationTime time)
        {
            return StartTime < time && time < EndTime;
        }

        public Color Color()
        {
            var colorAttributes = GetType().GetCustomAttributes<ColorAttribute>().ToArray();
            return colorAttributes[colorAttributes.Length - 1].Color / 255;
        }

        public string StartTimeText()
        {
            return $"StartTime:  {StartTime.AsSingle.ToString("0.00")}S";
        }
        public string EndTimeText()
        {
            return $"EndTime:  {EndTime.AsSingle.ToString("0.00")}S";
        }
        public string DurationText()
        {
            return $"Duration:  {DurationTime.AsSingle.ToString("0.00")}S";
        }

        public virtual void RebindTimeline()
        {
            Track.RebindTimeline();
        }
        public virtual void RepaintInspector()
        {
            OnInspectorRepaint?.Invoke();
        }

        public virtual bool IsResizable()
        {
            return (Capabilities & ClipCapabilities.Resizable) == ClipCapabilities.Resizable;
        }
        public virtual bool IsMixable()
        {
            return (Capabilities & ClipCapabilities.Mixable) == ClipCapabilities.Mixable;
        }
        public virtual bool IsClipInable()
        {
            return (Capabilities & ClipCapabilities.ClipInable) == ClipCapabilities.ClipInable;
        }
        public bool IsTickQuantized()
        {
            return (Capabilities & ClipCapabilities.TickQuantized) == ClipCapabilities.TickQuantized;
        }
    }

#endif
}
