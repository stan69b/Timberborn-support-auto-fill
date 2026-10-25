using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockObjectTools;
using Timberborn.BlockSystem;
using Timberborn.BuildingTools;
using Timberborn.Coordinates;
using Timberborn.EntitySystem;

namespace PlatformAutofill
{
    // Both DefaultBlockObjectPlacer and BuildingPlacer can be called for the same
    // placement. We patch both so we never miss a block, and rely on the service's
    // per-component dedup guard to ignore the second call for the same object.
    // Since Timberborn 1.1 the placers no longer take a placed callback: they
    // create the entity synchronously through BlockObjectFactory, so we capture
    // the created BlockObject while Place runs and report it once it returns.
    // Supports are queued from here and built on the next tick, to avoid
    // conflicting with Timberborn's active drag/preview state.

    [HarmonyPatch(typeof(DefaultBlockObjectPlacer), nameof(DefaultBlockObjectPlacer.Place))]
    public static class DefaultBlockObjectPlacerPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            PlacedObjectCapture.Begin();
        }

        [HarmonyFinalizer]
        public static void Finalizer(EntitySetup.Builder entitySetupBuilder, Placement placement, Exception? __exception)
        {
            PatchHelper.OnPlaced(entitySetupBuilder, placement, PlacedObjectCapture.End(), __exception);
        }
    }

    [HarmonyPatch(typeof(BuildingPlacer), nameof(BuildingPlacer.Place))]
    public static class BuildingPlacerPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            PlacedObjectCapture.Begin();
        }

        [HarmonyFinalizer]
        public static void Finalizer(EntitySetup.Builder entitySetupBuilder, Placement placement, Exception? __exception)
        {
            PatchHelper.OnPlaced(entitySetupBuilder, placement, PlacedObjectCapture.End(), __exception);
        }
    }

    // BlockObjectFactory.Create is the single private path through which both
    // CreateFinished and CreateUnfinished instantiate a block object.
    [HarmonyPatch(typeof(BlockObjectFactory), "Create")]
    public static class BlockObjectFactoryCreatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockObject __result)
        {
            PlacedObjectCapture.Record(__result);
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

        internal static void OnPlaced(
            EntitySetup.Builder entitySetupBuilder,
            Placement placement,
            BlockObject? placed,
            Exception? exception)
        {
            if (exception != null || placed == null) return;
            var service = PlatformAutofillService.Instance;
            if (service == null || service.IsPlacingSupports) return;
            TryRun("placer finalizer", () =>
            {
                BlockObjectSpec? template = entitySetupBuilder.Template.GetSpec<BlockObjectSpec>();
                if (template != null)
                {
                    service.OnBlockPlaced(placed, placement, template);
                }
            });
        }
    }

    // Records the BlockObject created while a placer call is in progress.
    // Begin/End nest, so the service can wrap its own placer call around the
    // patched one and still read the captured object.
    internal static class PlacedObjectCapture
    {
        private static int _depth;
        private static BlockObject? _captured;

        internal static void Begin()
        {
            if (_depth++ == 0)
            {
                _captured = null;
            }
        }

        internal static void Record(BlockObject? blockObject)
        {
            if (_depth > 0 && _captured == null)
            {
                _captured = blockObject;
            }
        }

        internal static BlockObject? End()
        {
            BlockObject? captured = _captured;
            if (_depth > 0 && --_depth == 0)
            {
                _captured = null;
            }
            return captured;
        }
    }
}
