using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockObjectTools;
using Timberborn.BlockSystem;
using Timberborn.BuildingTools;
using Timberborn.Coordinates;

namespace PlatformAutofill
{
    // Both DefaultBlockObjectPlacer and BuildingPlacer can be called for the same
    // placement. We patch both so we never miss a block, and rely on the service's
    // coordinate-dedup guard to ignore the second call for the same spot.

    [HarmonyPatch(typeof(DefaultBlockObjectPlacer), nameof(DefaultBlockObjectPlacer.Place))]
    public static class DefaultBlockObjectPlacerPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            BlockObjectSpec template,
            Placement placement,
            ref Action<BaseComponent> placedCallback)
        {
            PlatformAutofillService.Instance?.OnBeforePlace(template, placement);
            PatchHelper.Wrap(template, placement, ref placedCallback);
        }
    }

    [HarmonyPatch(typeof(BuildingPlacer), nameof(BuildingPlacer.Place))]
    public static class BuildingPlacerPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            BlockObjectSpec template,
            Placement placement,
            ref Action<BaseComponent> placedCallback)
        {
            PlatformAutofillService.Instance?.OnBeforePlace(template, placement);
            PatchHelper.Wrap(template, placement, ref placedCallback);
        }
    }

    [HarmonyPatch(typeof(BlockObjectTool), "ShowPreviews")]
    public static class BlockObjectToolPreviewPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockObjectTool __instance, IEnumerable<Placement> placements)
        {
            PatchHelper.TryRun(
                "BlockObjectTool.ShowPreviews",
                () => PlatformAutofillService.Instance?.UpdateSupportPreviews(__instance.Template, placements));
        }
    }

    [HarmonyPatch(typeof(PreviewPlacer), nameof(PreviewPlacer.HideAllPreviews))]
    public static class PreviewPlacerHidePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            PatchHelper.TryRun(
                "PreviewPlacer.HideAllPreviews",
                () => PlatformAutofillService.Instance?.ClearSupportPreviews());
        }
    }

    [HarmonyPatch(typeof(BlockObjectTool), nameof(BlockObjectTool.Exit))]
    public static class BlockObjectToolExitPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            PatchHelper.TryRun(
                "BlockObjectTool.Exit",
                () => PlatformAutofillService.Instance?.ClearSupportPreviews());
        }
    }

    [HarmonyPatch(typeof(BlockObject), nameof(BlockObject.IsValid))]
    public static class BlockObjectValidityPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockObject __instance, ref bool __result)
        {
            if (__result) return;
            var service = PlatformAutofillService.Instance;
            if (service?.CanBypassPlacementValidation(__instance) == true)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(BlockObjectValidationService), nameof(BlockObjectValidationService.IsValid))]
    public static class BlockObjectValidationServicePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockObject blockObject, ref bool __result)
        {
            if (__result) return;
            var service = PlatformAutofillService.Instance;
            if (service?.CanBypassPlacementValidation(blockObject) == true)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class BlockObjectValidationServiceWithMessagePatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(BlockObjectValidationService),
                nameof(BlockObjectValidationService.AreValid),
                new[] { typeof(IReadOnlyList<BaseComponent>), typeof(string).MakeByRefType() });
        }

        [HarmonyPostfix]
        public static void Postfix(IReadOnlyList<BaseComponent> previews, ref string errorMessage, ref bool __result)
        {
            if (__result) return;
            var service = PlatformAutofillService.Instance;
            if (service?.CanBypassPlacementValidation(previews) == true)
            {
                errorMessage = string.Empty;
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class BlockObjectValidationServiceWithoutMessagePatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(BlockObjectValidationService),
                nameof(BlockObjectValidationService.AreValid),
                new[] { typeof(IReadOnlyList<BaseComponent>) });
        }

        [HarmonyPostfix]
        public static void Postfix(IReadOnlyList<BaseComponent> previews, ref bool __result)
        {
            if (__result) return;
            var service = PlatformAutofillService.Instance;
            if (service?.CanBypassPlacementValidation(previews) == true)
            {
                __result = true;
            }
        }
    }

    internal static class PatchHelper
    {
        internal static void TryRun(string context, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PlatformAutofill] {context} patch failed: {ex}");
            }
        }

        internal static void Wrap(
            BlockObjectSpec template,
            Placement placement,
            ref Action<BaseComponent> placedCallback)
        {
            var service = PlatformAutofillService.Instance;
            if (service == null || service.IsPlacingSupports) return;
            var original = placedCallback;
            var capturedPlacement = placement;
            var capturedTemplate = template;
            placedCallback = component =>
            {
                original?.Invoke(component);
                TryRun(
                    "placedCallback",
                    () => service.OnBlockPlaced(component, capturedPlacement, capturedTemplate));
            };
        }
    }
}
