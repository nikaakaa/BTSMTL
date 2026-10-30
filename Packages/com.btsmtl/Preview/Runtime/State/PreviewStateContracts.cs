using ThirdPersonSimulation;

namespace BTSMTL.Preview
{
    public enum PreviewRestoreSupport : byte
    {
        RestartOnly,
        SelfCheckRequired,
        Verified
    }

    public enum PreviewRestoreStage : byte
    {
        Animation,
        Transform,
        Physics,
        Project,
        Random
    }

    public readonly struct PreviewStateCapacity
    {
        public PreviewStateCapacity(int bytes, int textReferences = 0)
        {
            Bytes = bytes;
            TextReferences = textReferences;
        }

        public int Bytes { get; }
        public int TextReferences { get; }
    }

    public interface IPreviewStateAdapter
    {
        string Identity { get; }
        PreviewRestoreStage RestoreStage { get; }
        PreviewRestoreSupport RestoreSupport { get; }
        string Limitation { get; }
        PreviewStateCapacity Capacity { get; }
        void Write<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter;
        void Restore(ref RecordedRuntimeStateReader reader);
    }
}
