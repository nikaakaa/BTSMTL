using System;

namespace BTSMTL.Timeline
{
    public readonly struct TimelinePreviewRootIdentity : IEquatable<TimelinePreviewRootIdentity>
    {
        public TimelinePreviewRootIdentity(string owner, string timeline)
        {
            Owner = owner ?? string.Empty;
            Timeline = timeline ?? string.Empty;
            if (Owner.Length == 0 == (Timeline.Length == 0))
                throw new ArgumentException("预览根必须恰有根程序身份或独立 Timeline 身份之一。");
        }

        public string Owner { get; }
        public string Timeline { get; }

        public bool Equals(TimelinePreviewRootIdentity other) =>
            string.Equals(Owner, other.Owner, StringComparison.Ordinal) &&
            string.Equals(Timeline, other.Timeline, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TimelinePreviewRootIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Owner, Timeline);
    }
}
