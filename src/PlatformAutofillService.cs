using System;
using System.Collections.Generic;
using System.Linq;
using Bindito.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockObjectTools;
using Timberborn.BlockSystem;
using Timberborn.BlueprintSystem;
using Timberborn.Coordinates;
using Timberborn.FactionSystem;
using Timberborn.GameFactionSystem;
using Timberborn.Buildings;
using Timberborn.PathSystem;
using Timberborn.SingletonSystem;
using Timberborn.TerrainSystem;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace PlatformAutofill
{
    public class PlatformAutofillService : ILoadableSingleton, IUpdatableSingleton
    {
        private static readonly bool DiagnosticLogging = true;
        private static readonly string[] SupportTemplatePrefixes = { "Platform", "DoublePlatform", "TriplePlatform" };
        private static readonly Action<BaseComponent> NoOpPlacedCallback = _ => { };

        public int MaxSupportIndex { get; private set; } = 2;
        public bool IsEnabled { get; private set; } = false;
        public bool IsPlacingSupports { get; private set; } = false;

        public static PlatformAutofillService? Instance { get; private set; }

        private readonly Dictionary<string, BlockObjectSpec> _blockSpecByName = new();
        private readonly Dictionary<string, PlaceableBlockObjectSpec> _placeableSpecByName = new();
        private readonly Dictionary<BlockObjectSpec, string> _templateNameByRuntimeSpec = new();
        private readonly Dictionary<PlaceableBlockObjectSpec, string> _templateNameByPlaceableSpec = new();
        private readonly List<PendingSupportPlacement> _pendingSupportPlacements = new();
        private readonly List<Preview> _supportPreviews = new();
        private readonly HashSet<Vector3Int> _pendingSupportCoords = new();
        private Vector3Int _lastPlacedCoord = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        private bool _placeableSpecsLoaded;
        private IBlockService _blockService = null!;
        private FactionService _factionService = null!;
        private ITerrainService _terrainService = null!;
        private BlockObjectPlacerService _placerService = null!;
        private BlockObjectValidationService _validationService = null!;
        private BlockObjectToolGroupSpecService _blockObjectToolGroupSpecService = null!;
        private PlaceableBlockObjectSpecService _placeableBlockObjectSpecService = null!;
        private PreviewBlockService _previewBlockService = null!;
        private PreviewFactory _previewFactory = null!;
        private PreviewShower _previewShower = null!;
        private TemplateNameRetriever _nameRetriever = null!;

        [Inject]
        public void InjectDependencies(
            IBlockService blockService,
            FactionService factionService,
            ITerrainService terrainService,
            BlockObjectPlacerService placerService,
            BlockObjectValidationService validationService,
            BlockObjectToolGroupSpecService blockObjectToolGroupSpecService,
            PlaceableBlockObjectSpecService placeableBlockObjectSpecService,
            PreviewBlockService previewBlockService,
            PreviewFactory previewFactory,
            PreviewShower previewShower,
            TemplateNameRetriever nameRetriever)
        {
            _blockService = blockService;
            _factionService = factionService;
            _terrainService = terrainService;
            _placerService  = placerService;
            _validationService = validationService;
            _blockObjectToolGroupSpecService = blockObjectToolGroupSpecService;
            _placeableBlockObjectSpecService = placeableBlockObjectSpecService;
            _previewBlockService = previewBlockService;
            _previewFactory = previewFactory;
            _previewShower = previewShower;
            _nameRetriever  = nameRetriever;
        }

        public void Load()
        {
            Instance = this;
            RefreshPlaceableBlockSpecs();
        }

        public void UpdateSingleton()
        {
            if (_pendingSupportPlacements.Count == 0 || IsPlacingSupports)
            {
                return;
            }

            IsPlacingSupports = true;
            try
            {
                foreach (PendingSupportPlacement pendingSupport in _pendingSupportPlacements)
                {
                    string supportBlocks = FormatBlocks(pendingSupport.SupportSpec, pendingSupport.Placement);
                    string validationSummary = BuildValidationSummary(pendingSupport.SupportName, pendingSupport.Placement);
                    LogDiagnostic(
                        $"support '{pendingSupport.SupportName}' processing placementZ={pendingSupport.Placement.Coordinates.z} " +
                        $"blocks={supportBlocks} validation={validationSummary}");

                    try
                    {
                        IBlockObjectPlacer supportPlacer = _placerService.GetMatchingPlacer(pendingSupport.SupportSpec);
                        supportPlacer.Place(pendingSupport.SupportSpec, pendingSupport.Placement, NoOpPlacedCallback);
                        LogDiagnostic($"support '{pendingSupport.SupportName}' placed at {pendingSupport.Placement.Coordinates}");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError(
                            $"[PlatformAutofill] support placement failed for '{pendingSupport.SupportName}' at {pendingSupport.Placement.Coordinates}: {ex}");
                    }
                }
            }
            finally
            {
                _pendingSupportPlacements.Clear();
                _pendingSupportCoords.Clear();
                IsPlacingSupports = false;
            }
        }

        // -----------------------------------------------------------------------
        // UI helpers
        // -----------------------------------------------------------------------

        public void Toggle()
        {
            IsEnabled = !IsEnabled;
            if (!IsEnabled)
            {
                ClearSupportPreviews();
            }
        }

        public void SetMaxSupport(int index) => MaxSupportIndex = index;

        public bool SupportsAutofill(PlaceableBlockObjectSpec? template)
        {
            return TryGetAutofillTarget(template, out _);
        }

        public bool CanBypassPlacementValidation(BlockObject blockObject)
        {
            if (!IsEnabled || IsPlacingSupports || blockObject == null) return false;
            if (blockObject.IsFinished) return false;

            string templateName = _nameRetriever.GetTemplateName(blockObject);
            if (!SupportsAutofill(templateName))
            {
                return false;
            }

            if (!blockObject.IsAlmostValid() && !IsPathTemplate(templateName))
            {
                return false;
            }

            // Validate the dragged top block against the world state, not against
            // the support previews we inject afterwards, otherwise the original
            // preview can disappear on the next refresh.
            return CanResolveSupportsForPlacement(templateName, blockObject.Placement);
        }

        public bool CanBypassPlacementValidation(BaseComponent component)
        {
            if (!TryGetBlockObject(component, out BlockObject? blockObject) || blockObject == null)
            {
                return false;
            }

            return CanBypassPlacementValidation(blockObject);
        }

        public bool CanBypassPlacementValidation(IReadOnlyList<BaseComponent> components)
        {
            if (!IsEnabled || IsPlacingSupports || components.Count == 0) return false;

            foreach (BaseComponent component in components)
            {
                if (!TryGetBlockObject(component, out BlockObject? blockObject) || blockObject == null)
                {
                    return false;
                }

                if (!_validationService.IsValid(blockObject))
                {
                    return false;
                }
            }

            return true;
        }

        public void OnBeforePlace(BlockObjectSpec blockSpec, Placement placement)
        {
            // Deliberately left empty. Support placement is queued after the top
            // block is created and processed on the next update tick to avoid
            // conflicting with Timberborn's active drag/preview state.
        }

        public void UpdateSupportPreviews(PlaceableBlockObjectSpec template, IEnumerable<Placement> placements)
        {
            ClearSupportPreviews();

            if (!IsEnabled || IsPlacingSupports)
            {
                return;
            }

            if (!TryGetAutofillTarget(template, out AutofillTarget target))
            {
                return;
            }

            List<PendingSupportPlacement> previewPlacements = new();
            HashSet<Vector3Int> previewCoords = new();

            foreach (Placement placement in placements)
            {
                AppendSupportPlacements(
                    target.TemplateName,
                    target.Faction,
                    target.RuntimeSpec,
                    placement,
                    includePreviews: true,
                    previewPlacements,
                    previewCoords);
            }

            if (previewPlacements.Count == 0)
            {
                return;
            }

            foreach (PendingSupportPlacement pendingSupport in previewPlacements)
            {
                if (!TryGetSupportPlaceableSpec(pendingSupport.SupportName, out PlaceableBlockObjectSpec? placeableSpec)
                    || placeableSpec == null)
                {
                    continue;
                }

                Preview preview = _previewFactory.Create(placeableSpec);
                preview.Reposition(pendingSupport.Placement);
                _supportPreviews.Add(preview);
            }

            foreach (Preview supportPreview in _supportPreviews)
            {
                supportPreview.AddToPreviewServices();
            }

            List<Preview> buildableSupportPreviews = new();
            List<Preview> unbuildableSupportPreviews = new();

            foreach (Preview supportPreview in _supportPreviews)
            {
                var previewComponent = new List<BaseComponent> { supportPreview };
                bool isValid = _validationService.AreValid(previewComponent, out _);
                if (isValid)
                {
                    buildableSupportPreviews.Add(supportPreview);
                }
                else
                {
                    unbuildableSupportPreviews.Add(supportPreview);
                }
            }

            if (buildableSupportPreviews.Count > 0)
            {
                _previewShower.ShowBuildablePreviews(buildableSupportPreviews, out _);
            }

            if (unbuildableSupportPreviews.Count > 0)
            {
                _previewShower.ShowUnbuildablePreviews(unbuildableSupportPreviews);
            }
        }

        public void ClearSupportPreviews()
        {
            if (_supportPreviews.Count == 0)
            {
                return;
            }

            foreach (Preview supportPreview in _supportPreviews)
            {
                supportPreview.Hide();
                supportPreview.RemoveFromPreviewServices();
            }

            _supportPreviews.Clear();
        }

        // -----------------------------------------------------------------------
        // Called from the Harmony-wrapped placedCallback.
        // -----------------------------------------------------------------------

        public void OnBlockPlaced(BaseComponent component, Placement placement, BlockObjectSpec blockSpec)
        {
            string name = _nameRetriever.GetTemplateName(component);
            var coords = placement.Coordinates;

            if (!string.IsNullOrEmpty(name) && !_templateNameByRuntimeSpec.ContainsKey(blockSpec))
            {
                _templateNameByRuntimeSpec[blockSpec] = name;
            }

            if (coords == _lastPlacedCoord)
            {
                return;
            }

            _lastPlacedCoord = coords;

            if (!IsEnabled || IsPlacingSupports) return;
            if (!TryResolveAutofillTarget(name, out string? faction) || faction == null) return;

            TryQueueSupports(name, faction, blockSpec, placement);
        }

        private bool TryQueueSupports(
            string name,
            string faction,
            BlockObjectSpec blockSpec,
            Placement placement)
        {
            try
            {
                AppendSupportPlacements(name, faction, blockSpec, placement, includePreviews: false, _pendingSupportPlacements, _pendingSupportCoords);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[PlatformAutofill] support queueing failed for '{name}' at {placement.Coordinates}: {ex}");
                return false;
            }
        }

        private void AppendSupportPlacements(
            string name,
            string faction,
            BlockObjectSpec blockSpec,
            Placement placement,
            bool includePreviews,
            ICollection<PendingSupportPlacement> supportPlacements,
            ISet<Vector3Int> knownSupportCoords)
        {
            var coords = placement.Coordinates;
            int terrainTop = _terrainService.GetTerrainHeight(coords);
            if (!TryGetOccupiedZRange(blockSpec, placement, out int placedBottomZ, out int placedTopZ))
            {
                placedBottomZ = coords.z;
                placedTopZ = coords.z;
            }

            int gapBottom = GetGapBottom(coords.x, coords.y, terrainTop, placedBottomZ, includePreviews);
            int gapTop = placedBottomZ - 1;
            LogDiagnostic(
                $"place '{name}' at {coords} placementZ={placement.Coordinates.z} occupiedZ={placedBottomZ}..{placedTopZ} " +
                $"terrainTop={terrainTop} gap={gapBottom}..{gapTop} orientation={placement.Orientation} flip={placement.FlipMode} " +
                $"specType={blockSpec.GetType().FullName}");
            if (gapTop < gapBottom) return;

            int currentTopZ = gapTop;
            while (currentTopZ >= gapBottom)
            {
                bool placed = false;
                for (int sizeIdx = MaxSupportIndex; sizeIdx >= 0; sizeIdx--)
                {
                    string supportName = $"{SupportTemplatePrefixes[sizeIdx]}.{faction}";
                    if (!TryGetSupportSpec(supportName, out BlockObjectSpec? supportSpec)
                        || supportSpec == null)
                    {
                        LogDiagnostic(
                            $"support '{supportName}' unavailable for top '{name}'");
                        continue;
                    }

                    if (!TryCreateSupportPlacement(
                            supportSpec,
                            coords.x,
                            coords.y,
                            gapBottom,
                            currentTopZ,
                            placement.Orientation,
                            placement.FlipMode,
                            out Placement supportPlacement,
                            out int supportBottomZ,
                            out int supportTopZ,
                            out string searchSummary))
                    {
                        LogDiagnostic(
                            $"support '{supportName}' no fit for gapBottom={gapBottom} desiredTop={currentTopZ}: {searchSummary}");
                        continue;
                    }

                    var supportCoord = supportPlacement.Coordinates;
                    LogDiagnostic(
                        $"support '{supportName}' queued placementZ={supportCoord.z} occupiedZ={supportBottomZ}..{supportTopZ} " +
                        $"for desiredTop={currentTopZ}: {searchSummary} specType={supportSpec.GetType().FullName}");

                    if (knownSupportCoords.Add(supportCoord))
                    {
                        supportPlacements.Add(new PendingSupportPlacement(supportName, supportSpec, supportPlacement));
                    }

                    currentTopZ = supportBottomZ - 1;
                    placed = true;
                    break;
                }

                if (!placed) break;
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static bool TryExtractFaction(string templateName, out string? faction)
        {
            int dot = templateName.LastIndexOf('.');
            if (dot < 0 || dot >= templateName.Length - 1) { faction = null; return false; }
            faction = templateName.Substring(dot + 1);
            return true;
        }

        private bool SupportsAutofill(string templateName)
        {
            return TryGetSupportPlaceableSpec(templateName, out PlaceableBlockObjectSpec? supportSpec)
                && supportSpec != null
                && SupportsAutofill(supportSpec);
        }

        private bool IsPathTemplate(string templateName)
        {
            return TryGetSupportPlaceableSpec(templateName, out PlaceableBlockObjectSpec? placeableSpec)
                && placeableSpec?.Blueprint?.GetSpec<PathSpec>() != null;
        }

        private bool TryResolveAutofillTarget(string templateName, out string? faction)
        {
            faction = null;
            return SupportsAutofill(templateName)
                && TryResolveFaction(templateName, out faction)
                && !string.IsNullOrEmpty(faction);
        }

        private bool TryGetSupportSpec(string templateName, out BlockObjectSpec? supportSpec)
        {
            RefreshPlaceableBlockSpecs();

            if (_blockSpecByName.TryGetValue(templateName, out supportSpec) && supportSpec != null)
            {
                return true;
            }

            supportSpec = null;
            return false;
        }

        private bool TryGetSupportPlaceableSpec(string templateName, out PlaceableBlockObjectSpec? supportSpec)
        {
            RefreshPlaceableBlockSpecs();

            if (_placeableSpecByName.TryGetValue(templateName, out supportSpec) && supportSpec != null)
            {
                return true;
            }

            supportSpec = null;
            return false;
        }

        private bool TryGetTemplateName(PlaceableBlockObjectSpec placeableSpec, out string? templateName)
        {
            RefreshPlaceableBlockSpecs();

            if (_templateNameByPlaceableSpec.TryGetValue(placeableSpec, out string? knownTemplateName))
            {
                templateName = knownTemplateName;
                return true;
            }

            templateName = null;
            return false;
        }

        private bool TryGetTemplateName(BlockObjectSpec blockSpec, out string? templateName)
        {
            RefreshPlaceableBlockSpecs();

            if (_templateNameByRuntimeSpec.TryGetValue(blockSpec, out string? knownTemplateName))
            {
                templateName = knownTemplateName;
                return true;
            }

            templateName = null;
            return false;
        }

        private static bool TryCreateSupportPlacement(
            BlockObjectSpec supportSpec,
            int x,
            int y,
            int gapBottomZ,
            int desiredTopZ,
            Orientation orientation,
            FlipMode flipMode,
            out Placement placement,
            out int supportBottomZ,
            out int supportTopZ,
            out string searchSummary)
        {
            int searchMinZ = gapBottomZ - supportSpec.Size.z - 2;
            int searchMaxZ = desiredTopZ + supportSpec.Size.z + 2;
            int bestBottomZ = int.MinValue;
            int bestTopZ = int.MinValue;
            Placement bestPlacement = default;
            bool found = false;
            List<string> candidateSummaries = new();

            for (int candidateZ = searchMinZ; candidateZ <= searchMaxZ; candidateZ++)
            {
                var candidatePlacement = new Placement(new Vector3Int(x, y, candidateZ), orientation, flipMode);
                if (!TryGetOccupiedZRange(supportSpec, candidatePlacement, out int minZ, out int maxZ))
                {
                    continue;
                }

                bool reachesTop = maxZ == desiredTopZ;
                bool staysAboveFloor = minZ >= gapBottomZ;
                candidateSummaries.Add(
                    $"{candidateZ}->{minZ}..{maxZ}" +
                    (reachesTop ? " top" : "") +
                    (staysAboveFloor ? " floor" : "") +
                    (reachesTop && staysAboveFloor ? " fit" : ""));

                if (maxZ != desiredTopZ || minZ < gapBottomZ)
                {
                    continue;
                }

                if (!found || minZ < bestBottomZ)
                {
                    bestPlacement = candidatePlacement;
                    bestBottomZ = minZ;
                    bestTopZ = maxZ;
                    found = true;
                }
            }

            searchSummary =
                $"sizeZ={supportSpec.Size.z} search={searchMinZ}..{searchMaxZ} candidates=[{string.Join(", ", candidateSummaries)}]";
            placement = bestPlacement;
            supportBottomZ = bestBottomZ;
            supportTopZ = bestTopZ;
            return found;
        }

        private static bool TryGetOccupiedZRange(
            BlockObjectSpec blockSpec,
            Placement placement,
            out int minZ,
            out int maxZ)
        {
            var blocks = blockSpec.GetBlocks(placement).ToList();
            if (blocks.Count == 0)
            {
                minZ = 0;
                maxZ = 0;
                return false;
            }

            minZ = blocks.Min(block => block.Coordinates.z);
            maxZ = blocks.Max(block => block.Coordinates.z);
            return true;
        }

        private int GetGapBottom(int x, int y, int terrainTop, int placedBottomZ, bool includePreviews)
        {
            for (int z = placedBottomZ - 1; z >= terrainTop; z--)
            {
                var coordinates = new Vector3Int(x, y, z);
                if (HasExistingSupportBaseAt(coordinates, includePreviews))
                {
                    return z + 1;
                }
            }

            return terrainTop;
        }

        private bool HasExistingSupportBaseAt(Vector3Int coordinates, bool includePreviews)
        {
            if (_blockService.AnyObjectAt(coordinates))
            {
                return true;
            }

            return includePreviews && _previewBlockService.GetPreviewsAt(coordinates).Any();
        }

        private string BuildValidationSummary(string templateName, Placement placement)
        {
            if (!TryGetSupportPlaceableSpec(templateName, out PlaceableBlockObjectSpec? placeableSpec)
                || placeableSpec == null)
            {
                return "no-placeable-spec";
            }

            Preview? preview = null;
            try
            {
                preview = _previewFactory.Create(placeableSpec);
                preview.Reposition(placement);

                var previews = new List<BaseComponent> { preview };
                bool isValid = _validationService.AreValid(previews, out string errorMessage);
                return $"isValid={isValid} error='{errorMessage}'";
            }
            catch (System.Exception ex)
            {
                return $"validation-exception={ex.GetType().Name}:{ex.Message}";
            }
            finally
            {
                if (preview != null)
                {
                    preview.Hide();
                    preview.RemoveFromPreviewServices();
                }
            }
        }

        private static string FormatBlocks(BlockObjectSpec blockSpec, Placement placement)
        {
            var blocks = blockSpec.GetBlocks(placement)
                .Select(block => block.Coordinates.ToString())
                .ToList();

            return "[" + string.Join(", ", blocks) + "]";
        }

        private readonly struct PendingSupportPlacement
        {
            public PendingSupportPlacement(string supportName, BlockObjectSpec supportSpec, Placement placement)
            {
                SupportName = supportName;
                SupportSpec = supportSpec;
                Placement = placement;
            }

            public string SupportName { get; }
            public BlockObjectSpec SupportSpec { get; }
            public Placement Placement { get; }
        }

        private static void LogDiagnostic(string message)
        {
            if (!DiagnosticLogging)
            {
                return;
            }

            Debug.Log($"[PlatformAutofill] {message}");
        }

        private void RefreshPlaceableBlockSpecs()
        {
            if (_placeableSpecsLoaded)
            {
                return;
            }

            foreach (BlockObjectToolGroupSpec groupSpec in _blockObjectToolGroupSpecService.AllSpecs)
            {
                foreach (PlaceableBlockObjectSpec spec in _placeableBlockObjectSpecService.GetBlockObjects(groupSpec))
                {
                    CachePlaceableSpec(spec);
                }
            }

            foreach (PlaceableBlockObjectSpec spec in _placeableBlockObjectSpecService.GetBlockObjectsWithoutValidGroup())
            {
                CachePlaceableSpec(spec);
            }

            _placeableSpecsLoaded = true;
        }

        private void CachePlaceableSpec(PlaceableBlockObjectSpec spec)
        {
            Preview? preview = null;
            try
            {
                preview = _previewFactory.Create(spec);
                BlockObject? blockObject = preview.BlockObject;
                if (blockObject == null)
                {
                    return;
                }

                string templateName = _nameRetriever.GetTemplateName(blockObject);
                if (string.IsNullOrEmpty(templateName) || _blockSpecByName.ContainsKey(templateName))
                {
                    return;
                }

                _placeableSpecByName[templateName] = spec;
                _templateNameByPlaceableSpec[spec] = templateName;
                BlockObjectSpec? runtimeBlockSpec = spec.Blueprint.GetSpec<BlockObjectSpec>();
                if (runtimeBlockSpec == null)
                {
                    return;
                }

                _blockSpecByName[templateName] = runtimeBlockSpec;
                if (!_templateNameByRuntimeSpec.ContainsKey(runtimeBlockSpec))
                {
                    _templateNameByRuntimeSpec[runtimeBlockSpec] = templateName;
                }
            }
            finally
            {
                if (preview != null)
                {
                    preview.Hide();
                    preview.RemoveFromPreviewServices();
                }
            }
        }

        private static bool TryGetBlockObject(BaseComponent component, out BlockObject? blockObject)
        {
            if (component is BlockObject directBlockObject)
            {
                blockObject = directBlockObject;
                return true;
            }

            if (component is Preview preview && preview.BlockObject != null)
            {
                blockObject = preview.BlockObject;
                return true;
            }

            blockObject = null;
            return false;
        }

        private bool TryGetAutofillTarget(PlaceableBlockObjectSpec? template, out AutofillTarget target)
        {
            target = default;
            if (template == null)
            {
                return false;
            }

            if (!TryGetTemplateName(template, out string? templateName) || string.IsNullOrEmpty(templateName))
            {
                return false;
            }

            Blueprint? blueprint = template.Blueprint;
            if (blueprint == null)
            {
                return false;
            }

            bool isPathTemplate = blueprint.GetSpec<PathSpec>() != null;
            if (!IsDragLayout(template.Layout) && !isPathTemplate)
            {
                return false;
            }

            string resolvedTemplateName = templateName!;
            BlockObjectSpec? runtimeSpec = blueprint.GetSpec<BlockObjectSpec>();
            if (runtimeSpec == null)
            {
                return false;
            }

            if (!TryResolveFaction(resolvedTemplateName, out string? faction) || string.IsNullOrEmpty(faction))
            {
                return false;
            }

            string resolvedFaction = faction!;
            if (!HasSupportTemplateForFaction(resolvedFaction))
            {
                return false;
            }

            target = new AutofillTarget(resolvedTemplateName, resolvedFaction, runtimeSpec);
            return true;
        }

        private bool CanResolveSupportsForPlacement(string templateName, Placement placement)
        {
            if (!TryResolveAutofillTarget(templateName, out string? faction) || string.IsNullOrEmpty(faction))
            {
                return false;
            }

            if (!TryGetSupportSpec(templateName, out BlockObjectSpec? runtimeSpec) || runtimeSpec == null)
            {
                return false;
            }

            string resolvedFaction = faction!;
            try
            {
                List<PendingSupportPlacement> supportPlacements = new();
                HashSet<Vector3Int> knownSupportCoords = new();
                AppendSupportPlacements(
                    templateName,
                    resolvedFaction,
                    runtimeSpec,
                    placement,
                    includePreviews: false,
                    supportPlacements,
                    knownSupportCoords);
                return supportPlacements.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[PlatformAutofill] support dry-run failed for '{templateName}' at {placement.Coordinates}: {ex}");
                return false;
            }
        }

        private static bool IsDragLayout(BlockObjectLayout layout)
        {
            return layout != BlockObjectLayout.Single;
        }

        private bool TryResolveFaction(string templateName, out string? faction)
        {
            if (TryExtractFaction(templateName, out faction) && !string.IsNullOrEmpty(faction))
            {
                return true;
            }

            faction = _factionService.Current?.Id;
            return !string.IsNullOrEmpty(faction);
        }

        private bool HasSupportTemplateForFaction(string faction)
        {
            foreach (string prefix in SupportTemplatePrefixes)
            {
                if (TryGetSupportSpec($"{prefix}.{faction}", out BlockObjectSpec? supportSpec) && supportSpec != null)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct AutofillTarget
        {
            public AutofillTarget(string templateName, string faction, BlockObjectSpec runtimeSpec)
            {
                TemplateName = templateName;
                Faction = faction;
                RuntimeSpec = runtimeSpec;
            }

            public string TemplateName { get; }
            public string Faction { get; }
            public BlockObjectSpec RuntimeSpec { get; }
        }
    }
}
