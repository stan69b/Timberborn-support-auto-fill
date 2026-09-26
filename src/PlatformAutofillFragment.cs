using Bindito.Core;
using Timberborn.BlockObjectTools;
using Timberborn.SingletonSystem;
using Timberborn.ToolPanelSystem;
using Timberborn.ToolSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlatformAutofill
{
    public class PlatformAutofillFragment : IToolFragment
    {
        private static readonly string[] SupportSizeLabels = { "Max 1", "Max 2", "Max 3" };

        private PlatformAutofillService _service = null!;
        private EventBus _eventBus = null!;

        private VisualElement? _root;
        private Button _toggleButton = null!;
        private Button _maxSizeButton = null!;

        [Inject]
        public void InjectDependencies(PlatformAutofillService service, EventBus eventBus)
        {
            _service  = service;
            _eventBus = eventBus;
        }

        public VisualElement InitializeFragment()
        {
            // The fragment is shared by every tool panel module, so the UI must
            // only be built once. This used to be a process-wide flag, which left
            // the button missing after loading a second save in the same session;
            // the fragment is a per-game singleton, so an instance check is enough.
            if (_root != null)
            {
                var placeholder = new VisualElement();
                placeholder.style.display = DisplayStyle.None;
                return placeholder;
            }

            _root = BuildUI();
            _root.style.display = DisplayStyle.None;
            _eventBus.Register(this);
            return _root;
        }

        [OnEvent]
        public void OnToolEntered(ToolEnteredEvent evt)
        {
            if (_root == null)
            {
                return;
            }

            bool supported = evt.Tool is BlockObjectTool blockObjectTool
                && _service.SupportsAutofill(blockObjectTool.Template);
            _root.style.display = supported ? DisplayStyle.Flex : DisplayStyle.None;
            if (supported)
            {
                // Settings are loaded after the UI is built, so refresh on show.
                RefreshButtonState();
            }
        }

        [OnEvent]
        public void OnToolExited(ToolExitedEvent evt)
        {
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        private VisualElement BuildUI()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems    = Align.Center;
            root.style.paddingTop    = 4;
            root.style.paddingBottom = 4;
            root.style.paddingLeft   = 8;
            root.style.paddingRight  = 8;
            root.style.marginTop     = 2;
            root.style.marginBottom  = 2;

            var label = new Label("Platform Autofill");
            label.style.fontSize  = 11;
            label.style.color     = new Color(0.9f, 0.85f, 0.7f);
            label.style.flexGrow  = 1;
            root.Add(label);

            // Largest support piece allowed (Triple/Double/Single platform).
            _maxSizeButton = new Button(OnMaxSizeClicked);
            _maxSizeButton.style.width    = 52;
            _maxSizeButton.style.height   = 22;
            _maxSizeButton.style.fontSize = 11;
            _maxSizeButton.style.marginRight = 4;
            _maxSizeButton.tooltip = "Largest support piece used: 3 = TriplePlatform, 2 = DoublePlatform, 1 = Platform";
            root.Add(_maxSizeButton);

            _toggleButton = new Button(OnToggleClicked);
            _toggleButton.style.width    = 52;
            _toggleButton.style.height   = 22;
            _toggleButton.style.fontSize = 11;
            _toggleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(_toggleButton);

            RefreshButtonState();
            return root;
        }

        private void OnToggleClicked()
        {
            _service.Toggle();
            RefreshButtonState();
        }

        private void OnMaxSizeClicked()
        {
            _service.CycleMaxSupport();
            RefreshButtonState();
        }

        private void RefreshButtonState()
        {
            if (_service.IsEnabled)
            {
                _toggleButton.text         = "ON";
                _toggleButton.style.color  = new Color(0.3f, 0.95f, 0.4f);
            }
            else
            {
                _toggleButton.text         = "OFF";
                _toggleButton.style.color  = new Color(0.65f, 0.65f, 0.65f);
            }

            int index = PlatformAutofillRules.ClampSupportIndex(_service.MaxSupportIndex);
            _maxSizeButton.text = SupportSizeLabels[index];
        }
    }
}
