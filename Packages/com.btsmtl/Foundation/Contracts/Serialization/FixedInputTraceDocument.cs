using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ThirdPersonSimulation
{
    [Serializable]
    public sealed class FixedInputTraceDocument
    {
        public const string CurrentSchema = "character-fixed-input-trace/5";

        public string schema;
        public string trace_id;
        public string created_utc;
        public string actor_id;
        public int tick_rate;
        public ulong first_tick;
        public int frame_count;
        public bool has_camera_basis_yaw;
        public float camera_basis_yaw_degrees;
        public FixedInputTraceFrameDocument[] frames;
        public string content_hash;

        public string ComputeContentHash()
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendHash(hash, schema);
            AppendHash(hash, trace_id);
            AppendHash(hash, created_utc);
            AppendHash(hash, actor_id);
            AppendHash(hash, tick_rate.ToString(CultureInfo.InvariantCulture));
            AppendHash(hash, first_tick.ToString(CultureInfo.InvariantCulture));
            AppendHash(hash, frame_count.ToString(CultureInfo.InvariantCulture));
            if (schema != "character-fixed-input-trace/3")
            {
                AppendHash(hash, has_camera_basis_yaw ? "1" : "0");
                AppendHash(hash, has_camera_basis_yaw
                    ? camera_basis_yaw_degrees.ToString("R", CultureInfo.InvariantCulture)
                    : string.Empty);
            }
            for (int i = 0; i < frames.Length; i++)
            {
                AppendHash(hash, frames[i].simulation_tick.ToString(CultureInfo.InvariantCulture));
                AppendHash(hash, frames[i].input_payload_base64);
            }
            byte[] bytes = hash.GetHashAndReset();
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        static void AppendHash(IncrementalHash hash, string value)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value ?? string.Empty));
            hash.AppendData(new byte[] { 0x1f });
        }
    }

    [Serializable]
    public sealed class FixedInputTraceFrameDocument
    {
        public ulong simulation_tick;
        public string input_payload_base64;
    }
}
