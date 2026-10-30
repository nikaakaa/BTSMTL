using System.Collections;

namespace BTSMTL.Preview
{
    public readonly struct PreviewProjectFrame
    {
        public PreviewProjectFrame(int frame, string contentIdentity, bool prepared)
        {
            Frame = frame;
            ContentIdentity = contentIdentity;
            Prepared = prepared;
        }

        public int Frame { get; }
        public string ContentIdentity { get; }
        public bool Prepared { get; }
    }

    public interface IPreviewProjectAdapter<TCommand> where TCommand : PreviewAgentCommand
    {
        IEnumerator Load(TCommand command);
        void AdvanceFrame();
        void InjectInput(TCommand command);
        PreviewProjectFrame ReportFrame();
        IPreviewCheckpointAdapter Checkpoints { get; }
    }
}
