using BTSMTL.Timeline;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
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
        public TimelineEditorToolbarControls(
            Toolbar toolbar,
            ToolbarButton backButton,
            ObjectField documentField,
            Label status)
        {
            Toolbar = toolbar;
            BackButton = backButton;
            DocumentField = documentField;
            Status = status;
        }

        public Toolbar Toolbar { get; }
        public ToolbarButton BackButton { get; }
        public ObjectField DocumentField { get; }
        public Label Status { get; }

        public void SetBackVisible(bool visible)
        {
            BackButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetDocumentWithoutNotify(TimelineAsset document)
        {
            DocumentField.SetValueWithoutNotify(document);
        }

        public void SetSourceSummary(string value)
        {
            DocumentField.tooltip = value;
        }

        public void SetStatus(string value)
        {
            Status.text = value ?? string.Empty;
            Status.tooltip = value ?? string.Empty;
        }
    }

    internal static class TimelineEditorToolbarView
    {
        public static TimelineEditorToolbarControls Create(
            TimelineEditorBindingState binding,
            bool hasNavigation,
            System.Action onBack,
            EventCallback<ChangeEvent<UnityEngine.Object>> onDocumentChanged,
            VisualElement workspaceControls)
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("timeline-editor-toolbar");

            var documentGroup = new VisualElement();
            documentGroup.AddToClassList("timeline-editor-toolbar-group");
            documentGroup.style.flexDirection = FlexDirection.Row;
            documentGroup.style.alignItems = Align.Center;

            var backButton = new ToolbarButton(onBack) { text = "‹ Timeline" };
            backButton.style.display = hasNavigation ? DisplayStyle.Flex : DisplayStyle.None;
            documentGroup.Add(backButton);

            var documentField = new ObjectField("Timeline")
            {
                objectType = typeof(TimelineAsset),
                allowSceneObjects = false
            };
            documentField.style.width = 220f;
            documentField.style.flexShrink = 1f;
            documentField.labelElement.style.minWidth = 52f;
            documentField.labelElement.style.width = 52f;
            documentGroup.style.flexShrink = 1f;
            documentField.SetValueWithoutNotify(binding.Document);
            documentField.RegisterValueChangedCallback(onDocumentChanged);
            documentGroup.Add(documentField);

            documentField.tooltip = binding.CreateSourceSummary();

            var workspaceGroup = new VisualElement();
            workspaceGroup.AddToClassList("timeline-editor-toolbar-group");
            workspaceGroup.style.flexDirection = FlexDirection.Row;
            workspaceGroup.style.alignItems = Align.Center;
            if (workspaceControls != null)
                workspaceGroup.Add(workspaceControls);

            var status = new Label("作者编辑");
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.paddingLeft = 8f;
            status.style.paddingTop = 2f;
            status.style.paddingBottom = 2f;

            toolbar.Add(documentGroup);
            toolbar.Add(workspaceGroup);
            return new TimelineEditorToolbarControls(
                toolbar,
                backButton,
                documentField,
                status);
        }
    }
}
