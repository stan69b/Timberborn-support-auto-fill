using System;
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
        private PlatformAutofillService _service = null!;
        private EventBus _eventBus = null!;

        private const string UiCreatedKey = "PA.UICreated";

        private VisualElement? _root;
        private Button _toggleButton = null!;

        [Inject]
        public void InjectDependencies(PlatformAutofillService service, EventBus eventBus)
        {
            _service  = service;
            _eventBus = eventBus;
        }

        public VisualElement InitializeFragment()
        {
            if (AppDomain.CurrentDomain.GetData(UiCreatedKey) is true)
            {
                var placeholder = new VisualElement();
                placeholder.style.display = DisplayStyle.None;
                return placeholder;
            }
            AppDomain.CurrentDomain.SetData(UiCreatedKey, true);

            _root = BuildUI();
            _root.style.display = DisplayStyle.None;
            _eventBus.Register(this);
            return _root;
        }

        [OnEvent]
        public void OnToolEntered(ToolEnteredEvent evt)
        {
            _root!.style.display = evt.Tool is BlockObjectTool blockObjectTool
                && _service.SupportsAutofill(blockObjectTool.Template)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        [OnEvent]
        public void OnToolExited(ToolExitedEvent evt)
        {
            _root!.style.display = DisplayStyle.None;
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
        }
    }
}
