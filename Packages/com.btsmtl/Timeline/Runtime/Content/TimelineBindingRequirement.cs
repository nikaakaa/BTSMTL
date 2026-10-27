using System;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineBindingRequirement
    {
        public TimelineBindingRequirement(
            string bindingId,
            TimelineBindingValueKind valueKind,
            TimelineBindingAccess access,
            TimelineBindingLifetime lifetime)
        {
            BindingId = Require(bindingId, nameof(bindingId));
            ValueKind = valueKind;
            Access = access;
            Lifetime = lifetime;
        }

        public string BindingId { get; }
        public TimelineBindingValueKind ValueKind { get; }
        public TimelineBindingAccess Access { get; }
        public TimelineBindingLifetime Lifetime { get; }

        static string Require(string value, string name)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Timeline binding identity is required.", name)
                : value.Trim();
        }
    }
}
