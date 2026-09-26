using System;
using Bindito.Core;
using Timberborn.BlockObjectTools;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using Timberborn.ToolPanelSystem;
using Timberborn.ToolSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlatformAutofill
{
    public class PlatformAutofillFragment : IToolFragment
    {
        private PlatformAutofillService _service = null!;
        private EventBus _eventBus = null!;
        private ILoc _loc = null!;

        private VisualElement? _root;
        private Button _toggleButton = null!;
        private Button _maxSizeButton = null!;
        private Label _summaryLabel = null!;

        [Inject]
        public void InjectDependencies(PlatformAutofillService service, EventBus eventBus, ILoc loc)
        {
            _service  = service;
            _eventBus = eventBus;
            _loc      = loc;
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
            _service.PreviewSummaryChanged += RefreshSummary;
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
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingTop    = 4;
            root.style.paddingBottom = 4;
            root.style.paddingLeft   = 8;
            root.style.paddingRight  = 8;
            root.style.marginTop     = 2;
            root.style.marginBottom  = 2;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            root.Add(row);

            var label = new Label(Text("PlatformAutofill.Label", "Platform Autofill"));
            label.style.fontSize  = 11;
            label.style.color     = new Color(0.9f, 0.85f, 0.7f);
            label.style.flexGrow  = 1;
            row.Add(label);

            // Largest support piece allowed (Triple/Double/Single platform).
            _maxSizeButton = new Button(OnMaxSizeClicked);
            _maxSizeButton.style.width    = 52;
            _maxSizeButton.style.height   = 22;
            _maxSizeButton.style.fontSize = 11;
            _maxSizeButton.style.marginRight = 4;
            _maxSizeButton.tooltip = Text(
                "PlatformAutofill.MaxTooltip",
                "Largest support piece used: 3 = TriplePlatform, 2 = DoublePlatform, 1 = Platform");
            row.Add(_maxSizeButton);

            _toggleButton = new Button(OnToggleClicked);
            _toggleButton.style.width    = 52;
            _toggleButton.style.height   = 22;
            _toggleButton.style.fontSize = 11;
            _toggleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toggleButton.tooltip = Text(
                "PlatformAutofill.ToggleTooltip",
                "When ON, dragging over a drop automatically builds platform columns underneath.");
            row.Add(_toggleButton);

            // What the current drag will add; hidden when there is nothing to say.
            _summaryLabel = new Label();
            _summaryLabel.style.fontSize = 10;
            _summaryLabel.style.color    = new Color(0.8f, 0.8f, 0.75f);
            _summaryLabel.style.marginTop = 2;
            _summaryLabel.style.display  = DisplayStyle.None;
            root.Add(_summaryLabel);

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
                _toggleButton.text         = Text("PlatformAutofill.On", "ON");
                _toggleButton.style.color  = new Color(0.3f, 0.95f, 0.4f);
            }
            else
            {
                _toggleButton.text         = Text("PlatformAutofill.Off", "OFF");
                _toggleButton.style.color  = new Color(0.65f, 0.65f, 0.65f);
            }

            int maxPieceHeight = PlatformAutofillRules.ClampSupportIndex(_service.MaxSupportIndex) + 1;
            _maxSizeButton.text = string.Format(Text("PlatformAutofill.Max", "Max {0}"), maxPieceHeight);
            RefreshSummary();
        }

        private void RefreshSummary()
        {
            if (_summaryLabel == null)
            {
                return;
            }

            int supports = _service.PreviewSupportCount;
            int unsupported = _service.PreviewUnsupportedCount;
            if (!_service.IsEnabled || (supports == 0 && unsupported == 0))
            {
                _summaryLabel.style.display = DisplayStyle.None;
                return;
            }

            string text = string.Format(Text("PlatformAutofill.Summary", "+{0} supports"), supports);
            if (unsupported > 0)
            {
                text += " · " + string.Format(
                    Text("PlatformAutofill.Unsupported", "{0} spot(s) can't be supported"),
                    unsupported);
                _summaryLabel.style.color = new Color(1f, 0.6f, 0.4f);
            }
            else
            {
                _summaryLabel.style.color = new Color(0.8f, 0.8f, 0.75f);
            }

            _summaryLabel.text = text;
            _summaryLabel.style.display = DisplayStyle.Flex;
        }

        // Falls back to English when the key is missing from the game's
        // localization (e.g. the Localizations folder was not installed).
        private string Text(string key, string fallback)
        {
            try
            {
                string text = _loc.T(key);
                return string.IsNullOrEmpty(text) || text == key || text.Contains(key) ? fallback : text;
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
