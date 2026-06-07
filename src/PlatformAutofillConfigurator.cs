using System;
using System.Reflection;
using Bindito.Core;
using HarmonyLib;
using Timberborn.ToolPanelSystem;
using UnityEngine;

namespace PlatformAutofill
{
    /// <summary>
    /// Registered automatically by Timberborn's mod loader (Bindito) for the in-game scene.
    /// Initialises Harmony patches and registers the mod's services and UI fragment.
    /// </summary>
    [Context("Game")]
    public class PlatformAutofillConfigurator : IConfigurator
    {
        static PlatformAutofillConfigurator()
        {
            try
            {
                new Harmony("com.stan.platform-autofill")
                    .PatchAll(Assembly.GetExecutingAssembly());
                Debug.Log("[PlatformAutofill] Harmony patches applied OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlatformAutofill] Harmony patch FAILED: {ex}");
            }
        }

        public void Configure(IContainerDefinition containerDefinition)
        {
            containerDefinition.Bind<PlatformAutofillService>().AsSingleton();
            containerDefinition.Bind<PlatformAutofillFragment>().AsSingleton();
            containerDefinition.MultiBind<ToolPanelModule>()
                .ToProvider<ToolPanelModuleProvider>()
                .AsSingleton();
        }

        // Provides a ToolPanelModule so Timberborn adds our fragment to every tool panel.
        private class ToolPanelModuleProvider : IProvider<ToolPanelModule>
        {
            private readonly PlatformAutofillFragment _fragment;

            [Inject]
            public ToolPanelModuleProvider(PlatformAutofillFragment fragment)
            {
                _fragment = fragment;
            }

            public ToolPanelModule Get()
            {
                ToolPanelModule.Builder builder = new ToolPanelModule.Builder();
                builder.AddFragment(_fragment, 0);
                return builder.Build();
            }
        }
    }
}
