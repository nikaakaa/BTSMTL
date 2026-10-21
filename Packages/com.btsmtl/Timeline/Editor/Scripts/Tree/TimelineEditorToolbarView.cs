using BTSMTL.Timeline;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    internal readonly struct TimelineEditorBindingState
    {
        public TimelineEditorBindingState(
            TimelineData timeline,
            UnityEngine.Object serializedOwner,
            string serializedPropertyPath,
            string ownershipLabel,
            string sourceNodeGuid,
            string sourceGraphAuthoringId)
        {
            Timeline = timeline;
            SerializedOwner = serializedOwner;
            SerializedPropertyPath = serializedPropertyPath ?? string.Empty;
            OwnershipLabel = ownershipLabel ?? string.Empty;
            SourceNodeGuid = sourceNodeGuid ?? string.Empty;
            SourceGraphAuthoringId = sourceGraphAuthoringId ?? string.Empty;
        }

        public TimelineData Timeline { get; }
        public UnityEngine.Object SerializedOwner { get; }
        public string SerializedPropertyPath { get; }
        public string OwnershipLabel { get; }
        public string SourceNodeGuid { get; }
        public string SourceGraphAuthoringId { get; }
        public bool IsBound => Timeline != null && SerializedOwner != null &&
            !string.IsNullOrEmpty(SerializedPropertyPath);

        public TimelineAsset Document => SerializedOwner as TimelineAsset;

        public string CreateSourceSummary()
        {
            if (!IsBound)
                return "Source: None";
            string ownership = string.IsNullOrWhiteSpace(OwnershipLabel) ? "Timeline" : OwnershipLabel;
            return $"Source: {ownership} / {Timeline.Name}";
        }
    }

    internal sealed class TimelineEditorToolbarControls
    {
        public TimelineEditorToolbarControls(Toolbar toolbar, ToolbarButton backButton, VisualElement subject, Label status)
        {
            Toolbar = toolbar;
            BackButton = backButton;
            Subject = subject;
            Status = status;
        }

        public Toolbar Toolbar { get; }
        public ToolbarButton BackButton { get; }
        public VisualElement Subject { get; }
        public Label Status { get; }

        public void SetBackVisible(bool visible)
        {
            BackButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetSourceSummary(string value)
        {
            Subject.tooltip = value;
        }

        public void SetStatus(string value)
        {
            Status.text = value ?? string.Empty;
            Status.tooltip = value ?? string.Empty;
            Status.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    // 编辑行：返回与主体（技能根）在前，保存与工作区工具、模式切换在后；预览控制在下一行。
    internal static class TimelineEditorToolbarView
    {
        public static TimelineEditorToolbarControls Create(
            TimelineEditorBindingState binding,
            bool hasNavigation,
            System.Action onBack,
            VisualElement subject,
            VisualElement documentControls,
            VisualElement workspaceControls)
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("timeline-editor-toolbar");
            toolbar.style.flexWrap = Wrap.Wrap;
            toolbar.style.height = StyleKeyword.Auto;
            toolbar.style.minHeight = 22f;
            toolbar.style.alignItems = Align.Center;

            var backButton = new ToolbarButton(onBack) { text = "‹ 返回", tooltip = "回到技能根路线" };
            backButton.style.display = hasNavigation ? DisplayStyle.Flex : DisplayStyle.None;
            backButton.style.flexShrink = 0f;
            toolbar.Add(backButton);

            subject.tooltip = binding.CreateSourceSummary();
            toolbar.Add(subject);
            toolbar.Add(new VisualElement { style = { flexGrow = 1f } });
            if (documentControls != null)
                toolbar.Add(documentControls);
            if (workspaceControls != null)
                toolbar.Add(workspaceControls);

            var status = new Label { style = { display = DisplayStyle.None } };
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.paddingLeft = 8f;
            status.style.paddingTop = 2f;
            status.style.paddingBottom = 2f;
            return new TimelineEditorToolbarControls(toolbar, backButton, subject, status);
        }
    }
}
