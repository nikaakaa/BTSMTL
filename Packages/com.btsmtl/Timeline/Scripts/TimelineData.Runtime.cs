using System;
using ThirdPersonSimulation.Fixed;
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
        public FixedScalar DurationTime { get; private set; }
        public float Duration => DurationTime.ToSingle();

        public void Init()
        {
#if UNITY_EDITOR
            if (m_AuthoringBatchDepth != 0)
                return;
#endif
            DurationTime = FixedScalar.Zero;
            foreach (var track in m_Tracks)
            {
                track.Init(this);
                DurationTime = FixedScalar.Max(DurationTime, track.DurationTime);
            }
            for (int i = 0; i < m_Sections.Count; i++)
            {
                TimelineSection section = m_Sections[i];
                if (section != null)
                    DurationTime = FixedScalar.Max(DurationTime, section.Time);
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
        public FixedScalar DurationTime { get; protected set; }

        public virtual void Init(TimelineData timeline)
        {
            Timeline = timeline;

            DurationTime = FixedScalar.Zero;
            foreach (var clip in m_Clips)
            {
                clip.Init(this);
                DurationTime = FixedScalar.Max(DurationTime, clip.EndTime);
            }
            foreach (var marker in m_Markers)
            {
                marker.Init(this);
                DurationTime = FixedScalar.Max(DurationTime, FixedScalar.Max(FixedScalar.FromRaw(1), marker.Time));
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

        public TimelineMarker AddMarker(FixedScalar time, ScriptableObject graph, bool forceTriggerOnInitialSeek = false)
        {
            if (time < FixedScalar.Zero)
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
        long m_StartTimeRaw;
        [SerializeField]
        long m_EndTimeRaw;
        [SerializeField]
        long m_OtherEaseInTimeRaw;
        [SerializeField]
        long m_OtherEaseOutTimeRaw;
        [SerializeField]
        long m_SelfEaseInTimeRaw;
        [SerializeField]
        long m_SelfEaseOutTimeRaw;
        [SerializeField]
        long m_ClipInTimeRaw;

        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public virtual string ContractKind => string.Empty;
        public TimelineExecutionDomain ExecutionDomain => Track != null
            ? Track.ExecutionDomain
            : throw new InvalidOperationException("Timeline Clip has no owning Track.");
        #endregion

        #region Time
        public FixedScalar StartTime => FixedScalar.FromRaw(m_StartTimeRaw);
        public FixedScalar EndTime => FixedScalar.FromRaw(m_EndTimeRaw);
        public FixedScalar SelfEaseInTime => FixedScalar.FromRaw(m_SelfEaseInTimeRaw);
        public FixedScalar SelfEaseOutTime => FixedScalar.FromRaw(m_SelfEaseOutTimeRaw);
        public FixedScalar OtherEaseInTime => FixedScalar.FromRaw(m_OtherEaseInTimeRaw);
        public FixedScalar OtherEaseOutTime => FixedScalar.FromRaw(m_OtherEaseOutTimeRaw);
        public FixedScalar EaseInTime => OtherEaseInTime.Raw == 0 ? SelfEaseInTime : OtherEaseInTime;
        public FixedScalar EaseOutTime => OtherEaseOutTime.Raw == 0 ? SelfEaseOutTime : OtherEaseOutTime;
        public FixedScalar ClipInTime => FixedScalar.FromRaw(m_ClipInTimeRaw);
        public FixedScalar DurationTime => EndTime - StartTime;
        public abstract TimelineContentParameters CaptureContentParameters();

        #endregion

        [NonSerialized]
        public Track Track;
        public TimelineData Timeline => Track.Timeline;
        public virtual string Name => GetType().Name;

        public Action OnNameChanged;
        public Action OnInspectorRepaint;

        public virtual void Init(Track track)
        {
            Track = track;
        }

        public TimelineClipTimeRange CaptureTimeRange() => new TimelineClipTimeRange(
            StartTime, EndTime, SelfEaseInTime, SelfEaseOutTime, ClipInTime,
            this is MotionCurveClip motion ? motion.SourceStartTime : this is CameraEffectClip camera ? camera.SourceStartTime : 0f,
            this is MotionCurveClip source ? source.SourceEndTime : this is CameraEffectClip effect ? effect.SourceEndTime : 0f,
            OtherEaseInTime, OtherEaseOutTime);

#if UNITY_EDITOR
        public void ApplyTimeRange(in TimelineClipTimeRange range)
        {
            m_StartTimeRaw = range.Start.Raw;
            m_EndTimeRaw = range.End.Raw;
            m_SelfEaseInTimeRaw = range.EaseIn.Raw;
            m_SelfEaseOutTimeRaw = range.EaseOut.Raw;
            m_ClipInTimeRaw = range.ClipIn.Raw;
            if (this is MotionCurveClip motion)
                motion.ApplySourceTimeRange(range.SourceStart, range.SourceEnd);
            if (this is CameraEffectClip camera)
                camera.ConfigureSourceRange(range.SourceStart, range.SourceEnd);
        }

        public void ConfigureTimeRange(FixedScalar startTime, FixedScalar endTime)
        {
            if (startTime < FixedScalar.Zero || endTime < startTime)
                throw new ArgumentOutOfRangeException(nameof(startTime));
            m_StartTimeRaw = startTime.Raw;
            m_EndTimeRaw = endTime.Raw;
        }

        public void ConfigureEase(FixedScalar easeIn, FixedScalar easeOut)
        {
            if (easeIn < FixedScalar.Zero || easeOut < FixedScalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(easeIn));
            m_SelfEaseInTimeRaw = easeIn.Raw;
            m_SelfEaseOutTimeRaw = easeOut.Raw;
        }

        public void ConfigureClipIn(FixedScalar time)
        {
            if (time < FixedScalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(time));
            m_ClipInTimeRaw = time.Raw;
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
        public Clip AddClip(TimelineContractCatalog catalog, Track track, FixedScalar time)
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
        public Clip AddClip(TimelineContractCatalog catalog, UnityEngine.Object referenceObject, Track track, FixedScalar time)
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
        public TimelineSection AddSection(string name, FixedScalar time)
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
        public TimelineSection EnsureSection(string authoringId, string name, FixedScalar time)
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
        public void ConfigureSection(TimelineSection section, string name, FixedScalar time)
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
                int frame = (left?.Time ?? FixedScalar.MaxValue).CompareTo(right?.Time ?? FixedScalar.MaxValue);
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
            FixedScalar terminalTime = FixedScalar.Zero;
            for (int trackIndex = 0; trackIndex < m_Tracks.Count; trackIndex++)
            {
                Track track = m_Tracks[trackIndex];
                if (track == null)
                    continue;
                foreach (TimelineMarker marker in track.Markers)
                    terminalTime = FixedScalar.Max(terminalTime, FixedScalar.Max(FixedScalar.FromRaw(1), marker.Time));
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    Clip clip = track.Clips[clipIndex];
                    if (clip == null)
                        continue;
                    if (clip is ITimelineTerminalTimeAlignedClip terminalClip)
                    {
                        terminalTime = FixedScalar.Max(terminalTime, clip.StartTime + FixedScalar.FromRaw(1));
                    }
                    else
                        terminalTime = FixedScalar.Max(terminalTime, clip.EndTime);
                }
            }
            for (int sectionIndex = 0; sectionIndex < m_Sections.Count; sectionIndex++)
            {
                TimelineSection section = m_Sections[sectionIndex];
                if (section != null)
                    terminalTime = FixedScalar.Max(terminalTime, section.Time);
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
            DurationTime = FixedScalar.Zero;
            for (int i = 0; i < m_Tracks.Count; i++)
            {
                Track track = m_Tracks[i];
                track.UpdateMix();
                track.RefreshDuration();
                DurationTime = FixedScalar.Max(DurationTime, track.DurationTime);
            }
            for (int i = 0; i < m_Sections.Count; i++)
                DurationTime = FixedScalar.Max(DurationTime, m_Sections[i].Time);
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
            DurationTime = FixedScalar.Zero;
            for (int i = 0; i < Clips.Count; i++)
                DurationTime = FixedScalar.Max(DurationTime, Clips[i].EndTime);
            for (int i = 0; i < Markers.Count; i++)
                DurationTime = FixedScalar.Max(DurationTime, FixedScalar.Max(FixedScalar.FromRaw(1), Markers[i].Time));
        }

        public virtual Type ClipType => typeof(Clip);

        public virtual Clip AddClip(FixedScalar time)
        {
            Clip clip = Activator.CreateInstance(ClipType, this, time) as Clip;
            clip.RegenerateAuthoringIdentity();
            m_Clips.Add(clip);
            return clip;
        }
        public virtual Clip AddClip(UnityEngine.Object referenceObject, FixedScalar time)
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

        public virtual FixedScalar Length => DurationTime;
        public virtual ClipCapabilities Capabilities => ClipCapabilities.None;

        public Clip() { }
        public Clip(Track track, FixedScalar time)
        {
            Track = track;
            ConfigureTimeRange(time, time + FixedScalar.FromDecimal(0.05m));
        }

        public void UpdateMix()
        {
            m_OtherEaseInTimeRaw = 0;
            m_OtherEaseOutTimeRaw = 0;

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
                        m_OtherEaseInTimeRaw = (clip.EndTime - StartTime).Raw;
                    }
                    if (clip.StartTime > StartTime && clip.StartTime < EndTime)
                    {
                        m_OtherEaseOutTimeRaw = (EndTime - clip.StartTime).Raw;
                    }
                    if (clip.StartTime == StartTime)
                    {
                        if (clip.EndTime < EndTime)
                        {
                            m_OtherEaseInTimeRaw = (clip.EndTime - StartTime).Raw;
                        }
                        else if (clip.EndTime > EndTime)
                        {
                            m_OtherEaseOutTimeRaw = (EndTime - StartTime).Raw;
                        }
                    }
                    m_SelfEaseInTimeRaw = FixedScalar.Min(SelfEaseInTime, DurationTime - OtherEaseOutTime).Raw;
                    m_SelfEaseOutTimeRaw = FixedScalar.Min(SelfEaseOutTime, DurationTime - OtherEaseInTime).Raw;
                }
            }
        }
        public bool Contains(FixedScalar time)
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
            return $"StartTime:  {StartTime.ToDouble().ToString("0.00")}S";
        }
        public string EndTimeText()
        {
            return $"EndTime:  {EndTime.ToDouble().ToString("0.00")}S";
        }
        public string DurationText()
        {
            return $"Duration:  {DurationTime.ToDouble().ToString("0.00")}S";
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
        protected SignalClip(Track track, FixedScalar time) : base(track, time)
        {
            ConfigureTimeRange(time, time + FixedScalar.FromRatio(1, TimelineUtility.FrameRate));
        }
    } 
#endif
}
