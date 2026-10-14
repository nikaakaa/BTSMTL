using System;
using Photon.Deterministic;
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
        public FP DurationTime { get; private set; }
        public float Duration => DurationTime.AsFloat;

        public void Init()
        {
#if UNITY_EDITOR
            if (m_AuthoringBatchDepth != 0)
                return;
#endif
            DurationTime = FP._0;
            foreach (var track in m_Tracks)
            {
                track.Init(this);
                DurationTime = FPMath.Max(DurationTime, track.DurationTime);
            }
            for (int i = 0; i < m_Sections.Count; i++)
            {
                TimelineSection section = m_Sections[i];
                if (section != null)
                    DurationTime = FPMath.Max(DurationTime, section.Time);
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
        public FP DurationTime { get; protected set; }

        internal void BindTimeline(TimelineData timeline) => Timeline = timeline;

        public virtual void Init(TimelineData timeline)
        {
            BindTimeline(timeline);

            DurationTime = FP._0;
            foreach (var clip in m_Clips)
            {
                clip.Init(this);
                DurationTime = FPMath.Max(DurationTime, clip.EndTime);
            }
            foreach (var marker in m_Markers)
            {
                marker.Init(this);
                DurationTime = FPMath.Max(DurationTime, FPMath.Max(FP.FromRaw(1), marker.Time));
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

        public TimelineMarker AddMarker(FP time, ScriptableObject graph, bool forceTriggerOnInitialSeek = false)
        {
            if (time < FP._0)
                throw new ArgumentOutOfRangeException(nameof(time));
            var marker = new TimelineMarker();
            marker.EnsureAuthoringIdentity();
            marker.Configure(time, graph, forceTriggerOnInitialSeek);
            marker.Init(this);
            m_Markers.Add(marker);
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
        public FP StartTime => FP.FromRaw(m_StartTimeFPRawValue);
        public FP EndTime => FP.FromRaw(m_EndTimeFPRawValue);
        public FP SelfEaseInTime => FP.FromRaw(m_SelfEaseInTimeFPRawValue);
        public FP SelfEaseOutTime => FP.FromRaw(m_SelfEaseOutTimeFPRawValue);
        public FP OtherEaseInTime => FP.FromRaw(m_OtherEaseInTimeFPRawValue);
        public FP OtherEaseOutTime => FP.FromRaw(m_OtherEaseOutTimeFPRawValue);
        public FP EaseInTime => OtherEaseInTime.RawValue == 0 ? SelfEaseInTime : OtherEaseInTime;
        public FP EaseOutTime => OtherEaseOutTime.RawValue == 0 ? SelfEaseOutTime : OtherEaseOutTime;
        public FP ClipInTime => FP.FromRaw(m_ClipInTimeFPRawValue);
        public FP DurationTime => EndTime - StartTime;
        public abstract TimelineContentParameters CaptureContentParameters();

        #endregion

        [NonSerialized]
        public Track Track;
        public TimelineData Timeline => Track.Timeline;
        public virtual string Name => GetType().Name;

        public Action OnNameChanged;
        public Action OnInspectorRepaint;

        internal void BindTrack(Track track) => Track = track;

        public virtual void Init(Track track) => BindTrack(track);

        public TimelineClipTimeRange CaptureTimeRange() => new TimelineClipTimeRange(
            StartTime, EndTime, SelfEaseInTime, SelfEaseOutTime, ClipInTime,
            this is MotionCurveClip motion ? motion.SourceStartTime : this is CameraEffectClip camera ? camera.SourceStartTime : 0f,
            this is MotionCurveClip source ? source.SourceEndTime : this is CameraEffectClip effect ? effect.SourceEndTime : 0f,
            OtherEaseInTime, OtherEaseOutTime);

#if UNITY_EDITOR
        public void ApplyTimeRange(in TimelineClipTimeRange range)
        {
            m_StartTimeFPRawValue = range.Start.RawValue;
            m_EndTimeFPRawValue = range.End.RawValue;
            m_SelfEaseInTimeFPRawValue = range.EaseIn.RawValue;
            m_SelfEaseOutTimeFPRawValue = range.EaseOut.RawValue;
            m_ClipInTimeFPRawValue = range.ClipIn.RawValue;
            if (this is MotionCurveClip motion)
                motion.ApplySourceTimeRange(range.SourceStart, range.SourceEnd);
            if (this is CameraEffectClip camera)
                camera.ConfigureSourceRange(range.SourceStart, range.SourceEnd);
        }

        public void ConfigureTimeRange(FP startTime, FP endTime)
        {
            if (startTime < FP._0 || endTime < startTime)
                throw new ArgumentOutOfRangeException(nameof(startTime));
            m_StartTimeFPRawValue = startTime.RawValue;
            m_EndTimeFPRawValue = endTime.RawValue;
        }

        public void ConfigureEase(FP easeIn, FP easeOut)
        {
            if (easeIn < FP._0 || easeOut < FP._0)
                throw new ArgumentOutOfRangeException(nameof(easeIn));
            m_SelfEaseInTimeFPRawValue = easeIn.RawValue;
            m_SelfEaseOutTimeFPRawValue = easeOut.RawValue;
        }

        public void ConfigureClipIn(FP time)
        {
            if (time < FP._0)
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

        public void AddTrack(Type type, TimelineContractCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            Track track = Activator.CreateInstance(type) as Track;
            TimelineTrackContract contract = catalog.RequireTrack(track?.ContractKind);
            track.RegenerateAuthoringIdentity();
            track.ConfigureExecutionDomain(contract.ExecutionPolicy.Domain);
            track.Name = type.Name.Replace("Track", string.Empty);
            m_Tracks.Add(track);
            track.Init(this);
            Init();
        }
        public void RemoveTrack(Track track)
        {
            m_Tracks.Remove(track);
            Init();
        }
        public Clip AddClip(TimelineContractCatalog catalog, Track track, FP time)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            Clip clip = track.AddClip(time);
            try
            {
                catalog.RequireClipPlacement(track, clip);
            }
            catch
            {
                track.RemoveClip(clip);
                throw;
            }
            Init();
            return clip;
        }
        public Clip AddClip(TimelineContractCatalog catalog, UnityEngine.Object referenceObject, Track track, FP time)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            Clip clip = track.AddClip(referenceObject, time);
            try
            {
                catalog.RequireClipPlacement(track, clip);
            }
            catch
            {
                if (clip != null)
                    track.RemoveClip(clip);
                throw;
            }
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
                if ((contract.Capabilities & TimelineCapability.Tree) == 0 || contract.OverlapPolicy != TimelineTrackOverlapPolicy.Reject)
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
                    AddTrack(track.GetType(), catalog);
                    Track destination = m_Tracks[m_Tracks.Count - 1];
                    m_Tracks.RemoveAt(m_Tracks.Count - 1);
                    m_Tracks.Insert(insertionIndex++, destination);
                    destination.ConfigureAuthoringIdentity(new Guid(StableHash.Compute("timeline.track", track.AuthoringId, clip.AuthoringId).Value.Substring(0, 32)).ToString("D"));
                    destination.Name = clip.Name;
                    destination.ConfigureExecutionDomain(track.ExecutionDomain);
                    destination.PersistentMuted = track.PersistentMuted;
                    MoveClip(catalog, clip, destination);
                    clipIndex--;
                }
            }
        }

        public void MoveClip(TimelineContractCatalog catalog, Clip clip, Track destination)
        {
            Track source = clip.Track;
            if (source == destination)
                return;
            if (!m_Tracks.Contains(source) || !m_Tracks.Contains(destination) || source.ExecutionDomain != destination.ExecutionDomain)
                throw new InvalidOperationException("移动片段必须使用同一Timeline内、执行域相同的轨道。");
            catalog.RequireClipPlacement(destination, clip);
            source.RemoveClip(clip);
            destination.Clips.Add(clip);
            clip.Init(destination);
            destination.UpdateMix();
            Init();
        }

        public void RemoveClip(Clip clip)
        {
            clip.Track.RemoveClip(clip);

            Init();
        }
        public TimelineSection AddSection(string name, FP time)
        {
            TimelineSection section = TimelineSection.Create(name, time);
            if (m_Sections.Exists(value => value != null && string.Equals(value.Name, section.Name, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Timeline Section '{section.Name}' already exists.");
            m_Sections.Add(section);
            SortSections();
            Init();
            return section;
        }
        public TimelineExternalBindingDeclaration AddExternalBinding(
            string bindingId,
            string displayName,
            string domain,
            TimelineBindingValueKind valueKind,
            TimelineBindingAccess access,
            TimelineBindingLifetime lifetime,
            string parameterId = null)
        {
            TimelineExternalBindingDeclaration binding = TimelineExternalBindingDeclaration.Create(
                bindingId,
                displayName,
                domain,
                valueKind,
                access,
                lifetime,
                parameterId);
            if (m_ExternalBindings.Exists(value => value != null && string.Equals(value.BindingId, binding.BindingId, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Timeline external binding '{binding.BindingId}' already exists.");
            m_ExternalBindings.Add(binding);
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
        public TimelineSection EnsureSection(string authoringId, string name, FP time)
        {
            TimelineSection section = m_Sections.SingleOrDefault(value =>
                value != null && string.Equals(value.AuthoringId, authoringId, StringComparison.Ordinal));
            if (section == null)
            {
                section = TimelineSection.Create(authoringId, name, time);
                m_Sections.Add(section);
            }
            ConfigureSection(section, name, time);
            return section;
        }
#endif
        public void ConfigureSection(TimelineSection section, string name, FP time)
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
                int frame = (left?.Time ?? FP.MaxValue).CompareTo(right?.Time ?? FP.MaxValue);
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
            FP terminalTime = FP._0;
            for (int trackIndex = 0; trackIndex < m_Tracks.Count; trackIndex++)
            {
                Track track = m_Tracks[trackIndex];
                if (track == null)
                    continue;
                foreach (TimelineMarker marker in track.Markers)
                    terminalTime = FPMath.Max(terminalTime, FPMath.Max(FP.FromRaw(1), marker.Time));
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    if (clip == null)
                        continue;
                    if (clip is ITimelineTerminalTimeAlignedClip terminalClip)
                    {
                        terminalTime = FPMath.Max(terminalTime, clip.StartTime + FP.FromRaw(1));
                    }
                    else
                        terminalTime = FPMath.Max(terminalTime, clip.EndTime);
                }
            }
            for (int sectionIndex = 0; sectionIndex < m_Sections.Count; sectionIndex++)
            {
                TimelineSection section = m_Sections[sectionIndex];
                if (section != null)
                    terminalTime = FPMath.Max(terminalTime, section.Time);
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
            DurationTime = FP._0;
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                Track track = m_Tracks[i];
                track.UpdateMix();
                track.RefreshDuration();
                DurationTime = FPMath.Max(DurationTime, track.DurationTime);
            }
            for (int i = 0; i < m_Sections.Count; i++)
                DurationTime = FPMath.Max(DurationTime, m_Sections[i].Time);
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
            DurationTime = FP._0;
            for (int i = 0; i < Clips.Count; i++)
                DurationTime = FPMath.Max(DurationTime, Clips[i].EndTime);
            for (int i = 0; i < Markers.Count; i++)
                DurationTime = FPMath.Max(DurationTime, FPMath.Max(FP.FromRaw(1), Markers[i].Time));
        }

        public virtual Type ClipType => typeof(Clip);

        public virtual Clip AddClip(FP time)
        {
            Clip clip = Activator.CreateInstance(ClipType, this, time) as Clip;
            clip.RegenerateAuthoringIdentity();
            m_Clips.Add(clip);
            return clip;
        }
        public virtual Clip AddClip(UnityEngine.Object referenceObject, FP time)
        {
            return null;
        }

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

        public virtual FP Length => DurationTime;
        public virtual ClipCapabilities Capabilities => ClipCapabilities.None;

        public Clip() { }
        public Clip(Track track, FP time)
        {
            Track = track;
            ConfigureTimeRange(time, time + FP._0_05);
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
                    m_SelfEaseInTimeFPRawValue = FPMath.Min(SelfEaseInTime, DurationTime - OtherEaseOutTime).RawValue;
                    m_SelfEaseOutTimeFPRawValue = FPMath.Min(SelfEaseOutTime, DurationTime - OtherEaseInTime).RawValue;
                }
            }
        }
        public bool Contains(FP time)
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
            return $"StartTime:  {StartTime.AsDouble.ToString("0.00")}S";
        }
        public string EndTimeText()
        {
            return $"EndTime:  {EndTime.AsDouble.ToString("0.00")}S";
        }
        public string DurationText()
        {
            return $"Duration:  {DurationTime.AsDouble.ToString("0.00")}S";
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

    public abstract partial class SignalClip
    {
        protected SignalClip(Track track, FP time) : base(track, time)
        {
            ConfigureTimeRange(time, time + TimelineTimeGrid.Position(1, TimelineUtility.FrameRate));
        }
    } 
#endif
}
