using System;
using BTSMTL.Authoring.Graph;
using BTSMTL.Diagnostics;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline
{
    [Serializable]
    public sealed partial class TimelineExternalBindingDeclaration
    {
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField, UnityEngine.HideInInspector]
#endif
        string m_AuthoringId;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        string m_BindingId;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        string m_DisplayName;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        string m_Domain;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        string m_ParameterId;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        TimelineBindingValueKind m_ValueKind;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        TimelineBindingAccess m_Access;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        TimelineBindingLifetime m_Lifetime;

        public string AuthoringId => m_AuthoringId ?? string.Empty;
        [GraphAuthoringField(nameof(m_BindingId), GraphAuthoringFieldValueKind.String)]
        public string BindingId => m_BindingId ?? string.Empty;
        [GraphAuthoringField(nameof(m_DisplayName), GraphAuthoringFieldValueKind.String)]
        public string DisplayName => m_DisplayName ?? string.Empty;
        [GraphAuthoringField(nameof(m_Domain), GraphAuthoringFieldValueKind.String)]
        public string Domain => m_Domain ?? string.Empty;
        [GraphAuthoringField(nameof(m_ParameterId), GraphAuthoringFieldValueKind.String)]
        public string ParameterId => m_ParameterId ?? string.Empty;
        [GraphAuthoringField(nameof(m_ValueKind), typeof(TimelineBindingValueKind))]
        public TimelineBindingValueKind ValueKind => m_ValueKind;
        [GraphAuthoringField(nameof(m_Access), typeof(TimelineBindingAccess))]
        public TimelineBindingAccess Access => m_Access;
        [GraphAuthoringField(nameof(m_Lifetime), typeof(TimelineBindingLifetime))]
        public TimelineBindingLifetime Lifetime => m_Lifetime;

        [GraphAuthoringConfigure]
        public void Configure(string bindingId, string displayName, string domain,
            TimelineBindingValueKind valueKind, TimelineBindingAccess access,
            TimelineBindingLifetime lifetime, string parameterId = null)
        {
            m_BindingId = Normalize(bindingId);
            m_DisplayName = Normalize(displayName);
            m_Domain = Normalize(domain);
            m_ParameterId = Normalize(parameterId);
            m_ValueKind = valueKind;
            m_Access = access;
            m_Lifetime = lifetime;
        }

        public bool Validate(out string error)
        {
            if (!AuthoringIdentity.IsValid(AuthoringId))
            {
                error = "authoring identity is invalid";
                return false;
            }
            if (string.IsNullOrEmpty(BindingId) || string.IsNullOrEmpty(DisplayName) || string.IsNullOrEmpty(Domain))
            {
                error = "binding identity, display name and domain are required";
                return false;
            }
            if (!Enum.IsDefined(typeof(TimelineBindingValueKind), ValueKind) ||
                !Enum.IsDefined(typeof(TimelineBindingAccess), Access) ||
                !Enum.IsDefined(typeof(TimelineBindingLifetime), Lifetime))
            {
                error = "binding type, access or lifetime is invalid";
                return false;
            }
            if (ValueKind != TimelineBindingValueKind.Target && string.IsNullOrEmpty(ParameterId))
            {
                error = "scalar and boolean bindings require a parameter identity";
                return false;
            }
            if (ValueKind == TimelineBindingValueKind.Target && !string.IsNullOrEmpty(ParameterId))
            {
                error = "target bindings cannot contain a parameter identity";
                return false;
            }
            if (Access == TimelineBindingAccess.Input && Lifetime != TimelineBindingLifetime.Call)
            {
                error = "input bindings must be captured for the call";
                return false;
            }
            if (Access != TimelineBindingAccess.Input && Lifetime != TimelineBindingLifetime.Tick)
            {
                error = "read and write bindings must use the current tick";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public void ConfigureAuthoringIdentity(string authoringId)
        {
            if (!AuthoringIdentity.IsValid(authoringId))
                throw new ArgumentException("Timeline binding authoring identity is invalid.", nameof(authoringId));
            m_AuthoringId = authoringId;
        }

#if UNITY_EDITOR
        public bool EnsureAuthoringIdentity()
        {
            if (AuthoringIdentity.IsValid(m_AuthoringId))
                return false;
            m_AuthoringId = AuthoringIdentity.Create();
            return true;
        }

        public void RegenerateAuthoringIdentity() => m_AuthoringId = AuthoringIdentity.Create();
#endif

        static string Normalize(string value) => value?.Trim() ?? string.Empty;
    }
}
