using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bindito.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockObjectAccesses;
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
        // Verbose per-placement logging; flip to true when debugging.
        private static readonly bool DiagnosticLogging = false;
        private const string EnabledPrefKey = "PlatformAutofill.Enabled";
        private const string MaxSupportPrefKey = "PlatformAutofill.MaxSupportIndex";
        private static readonly MethodInfo? AddAccessesAboveGroundMethod =
            typeof(HighBlockObjectAccessesAdder).GetMethod(
                "AddAccessesAboveGround",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo? UpdateBoundsMethod =
            typeof(BlockObjectAccessible).GetMethod(
                "UpdateBounds",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo? UpdateAccessesMethod =
            typeof(BlockObjectAccessible).GetMethod(
                "UpdateAccesses",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
        // Both placer patches can wrap the same callback, so the same placed
        // component may be reported twice. Dedup by instance (cleared every
        // tick) rather than by coordinates, which wrongly skipped legitimate
        // re-placements at the same spot and left them without supports.
        private readonly HashSet<BaseComponent> _handledPlacedComponents = new();
        // Previews are Unity objects; creating new ones on every validation or
        // mouse move leaked GameObjects until the game slowed down or crashed.
        private readonly Dictionary<PlaceableBlockObjectSpec, Stack<Preview>> _previewPool = new();
        private readonly Dictionary<Preview, PlaceableBlockObjectSpec> _pooledPreviewSpecs = new();
        private int _supportValidationDepth;
        private bool _strictValidation;
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
            LoadSettings();
            RefreshPlaceableBlockSpecs();
        }

        // Settings are stored per player (not per save), so the toggle and the
        // chosen support size survive loading another game or restarting.
        private void LoadSettings()
        {
            try
            {
                IsEnabled = PlayerPrefs.GetInt(EnabledPrefKey, 0) == 1;
                MaxSupportIndex = PlatformAutofillRules.ClampSupportIndex(
                    PlayerPrefs.GetInt(MaxSupportPrefKey, PlatformAutofillRules.SupportTemplatePrefixes.Length - 1));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlatformAutofill] loading settings failed: {ex}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                PlayerPrefs.SetInt(EnabledPrefKey, IsEnabled ? 1 : 0);
                PlayerPrefs.SetInt(MaxSupportPrefKey, MaxSupportIndex);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlatformAutofill] saving settings failed: {ex}");
            }
        }

        public void UpdateSingleton()
        {
            _handledPlacedComponents.Clear();

            if (_pendingSupportPlacements.Count == 0 || IsPlacingSupports)
            {
                return;
            }

            IsPlacingSupports = true;
            HashSet<Vector2Int> failedColumns = new();
            try
            {
                foreach (PendingSupportPlacement pendingSupport in PlatformAutofillRules.OrderSupportPlacements(
                             _pendingSupportPlacements,
                             GetSupportBottomZ,
                             pendingSupport => pendingSupport.Placement.Coordinates.x,
                             pendingSupport => pendingSupport.Placement.Coordinates.y))
                {
                    Vector3Int supportCoords = pendingSupport.Placement.Coordinates;
                    Vector2Int column = new Vector2Int(supportCoords.x, supportCoords.y);

                    // Supports are placed bottom-up. If a lower piece of this
                    // column failed, anything above it would float in mid-air.
                    if (failedColumns.Contains(column))
                    {
                        LogDiagnostic(
                            $"support '{pendingSupport.SupportName}' skipped at {supportCoords}: lower support in column failed");
                        continue;
                    }

                    // The world may have changed since the support was planned
                    // (e.g. a path or building placed in the same tick). Never
                    // place into an occupied cell: overlapping objects make one
                    // of them vanish or crash the game.
                    if (AnyWorldObjectAt(pendingSupport.SupportSpec, pendingSupport.Placement, ignore: null))
                    {
                        failedColumns.Add(column);
                        LogDiagnostic(
                            $"support '{pendingSupport.SupportName}' skipped at {supportCoords}: cell already occupied");
                        continue;
                    }

                    string supportBlocks = FormatBlocks(pendingSupport.SupportSpec, pendingSupport.Placement);
                    string validationSummary = BuildValidationSummary(pendingSupport.SupportName, pendingSupport.Placement);
                    LogDiagnostic(
                        $"support '{pendingSupport.SupportName}' processing placementZ={pendingSupport.Placement.Coordinates.z} " +
                        $"blocks={supportBlocks} validation={validationSummary}");

                    try
                    {
                        IBlockObjectPlacer supportPlacer = _placerService.GetMatchingPlacer(pendingSupport.SupportSpec);
                        supportPlacer.Place(
                            pendingSupport.SupportSpec,
                            pendingSupport.Placement,
                            component => OnSupportPlaced(component, pendingSupport));
                        LogDiagnostic($"support '{pendingSupport.SupportName}' placed at {pendingSupport.Placement.Coordinates}");
                    }
                    catch (System.Exception ex)
                    {
                        failedColumns.Add(column);
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

            SaveSettings();
        }

        public void SetMaxSupport(int index)
        {
            MaxSupportIndex = PlatformAutofillRules.ClampSupportIndex(index);
            SaveSettings();
        }

        // Cycles Triple -> Double -> Single -> Triple.
        public void CycleMaxSupport()
        {
            int next = MaxSupportIndex - 1;
            SetMaxSupport(next < 0 ? PlatformAutofillRules.SupportTemplatePrefixes.Length - 1 : next);
        }

        public bool SupportsAutofill(PlaceableBlockObjectSpec? template)
        {
            return TryGetAutofillTarget(template, out _);
        }

        public bool CanBypassPlacementValidation(BlockObject blockObject)
        {
            if (!IsEnabled || IsPlacingSupports || _strictValidation || blockObject == null) return false;
            if (blockObject.IsFinished) return false;

            string templateName = _nameRetriever.GetTemplateName(blockObject);
            if (PlatformAutofillRules.IsSupportTemplateName(templateName))
            {
                return false;
            }

            if (!SupportsAutofill(templateName))
            {
                return false;
            }

            if (!blockObject.IsAlmostValid() && !IsPathTemplate(templateName))
            {
                return false;
            }

            return IsAutofillCandidate(blockObject, templateName);
        }

        private bool IsAutofillCandidate(BlockObject blockObject, string templateName)
        {
            // Never let an object into cells that already hold something else.
            // Paths skip IsAlmostValid above, so without this they could be
            // dropped on top of existing objects, which then disappeared.
            if (TryGetSupportSpec(templateName, out BlockObjectSpec? ownSpec)
                && ownSpec != null
                && AnyWorldObjectAt(ownSpec, blockObject.Placement, ignore: blockObject))
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
            if (!IsEnabled || IsPlacingSupports || _strictValidation || components.Count == 0) return false;

            // Previously this overrode AreValid for *every* tool while the toggle
            // was on (buildings, floors, ...), letting invalid placements through.
            // Now it only applies to our own support checks or to a list that
            // actually contains an autofill placement with a complete support stack.
            bool inSupportValidation = _supportValidationDepth > 0;
            bool hasAutofillCandidate = false;

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

                if (!inSupportValidation && !hasAutofillCandidate && !blockObject.IsFinished)
                {
                    string templateName = _nameRetriever.GetTemplateName(blockObject);
                    hasAutofillCandidate = SupportsAutofill(templateName)
                        && IsAutofillCandidate(blockObject, templateName);
                }
            }

            return inSupportValidation || hasAutofillCandidate;
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
                // Only show a column when the whole stack reaches the ground or
                // an existing base; partial stacks rendered as floating pieces.
                TryAppendCompleteSupportStack(
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

                Preview preview = RentPreview(placeableSpec);
                preview.Reposition(pendingSupport.Placement);
                _supportPreviews.Add(preview);
            }

            foreach (Preview supportPreview in _supportPreviews)
            {
                supportPreview.AddToPreviewServices();
            }

            List<Preview> buildableSupportPreviews = new();
            List<Preview> unbuildableSupportPreviews = new();

            _supportValidationDepth++;
            try
            {
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
            }
            finally
            {
                _supportValidationDepth--;
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
                ReturnPreview(supportPreview);
            }

            _supportPreviews.Clear();
        }

        private Preview RentPreview(PlaceableBlockObjectSpec spec)
        {
            if (_previewPool.TryGetValue(spec, out Stack<Preview>? pool) && pool.Count > 0)
            {
                return pool.Pop();
            }

            Preview preview = _previewFactory.Create(spec);
            _pooledPreviewSpecs[preview] = spec;
            return preview;
        }

        private void ReturnPreview(Preview preview)
        {
            if (!_pooledPreviewSpecs.TryGetValue(preview, out PlaceableBlockObjectSpec? spec) || spec == null)
            {
                return;
            }

            if (!_previewPool.TryGetValue(spec, out Stack<Preview>? pool))
            {
                pool = new Stack<Preview>();
                _previewPool[spec] = pool;
            }

            pool.Push(preview);
        }

        // -----------------------------------------------------------------------
        // Called from the Harmony-wrapped placedCallback.
        // -----------------------------------------------------------------------

        public void OnBlockPlaced(BaseComponent component, Placement placement, BlockObjectSpec blockSpec)
        {
            string name = _nameRetriever.GetTemplateName(component);

            if (!string.IsNullOrEmpty(name) && !_templateNameByRuntimeSpec.ContainsKey(blockSpec))
            {
                _templateNameByRuntimeSpec[blockSpec] = name;
            }

            if (component == null || !_handledPlacedComponents.Add(component))
            {
                return;
            }

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
                return TryAppendCompleteSupportStack(
                    name, faction, blockSpec, placement, includePreviews: false, _pendingSupportPlacements, _pendingSupportCoords);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[PlatformAutofill] support queueing failed for '{name}' at {placement.Coordinates}: {ex}");
                return false;
            }
        }

        // Plans one support column and only commits it when it fully closes the
        // gap. A partial column used to be placed anyway, leaving the top block
        // (and the upper supports) floating above an empty cell.
        private bool TryAppendCompleteSupportStack(
            string name,
            string faction,
            BlockObjectSpec blockSpec,
            Placement placement,
            bool includePreviews,
            ICollection<PendingSupportPlacement> supportPlacements,
            ISet<Vector3Int> knownSupportCoords)
        {
            List<PendingSupportPlacement> column = new();
            if (!AppendSupportPlacements(name, faction, blockSpec, placement, includePreviews, column))
            {
                return false;
            }

            foreach (PendingSupportPlacement pendingSupport in column)
            {
                if (knownSupportCoords.Add(pendingSupport.Placement.Coordinates))
                {
                    supportPlacements.Add(pendingSupport);
                }
            }

            return column.Count > 0;
        }

        // Returns true when the planned column reaches the gap bottom.
        private bool AppendSupportPlacements(
            string name,
            string faction,
            BlockObjectSpec blockSpec,
            Placement placement,
            bool includePreviews,
            ICollection<PendingSupportPlacement> supportPlacements)
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
            if (gapTop < gapBottom) return true;

            bool complete = PlatformAutofillRules.TryPlanSupportColumn<PendingSupportPlacement>(
                gapBottom,
                gapTop,
                (int desiredTopZ, out PendingSupportPlacement piece, out int bottomZ) =>
                    TryFitSupportPiece(name, faction, coords.x, coords.y, gapBottom, desiredTopZ, placement, out piece, out bottomZ),
                supportPlacements);

            if (!complete)
            {
                LogDiagnostic(
                    $"place '{name}' at {coords}: support column incomplete (gap={gapBottom}..{gapTop})");
            }

            return complete;
        }

        // Tries each allowed support size, largest first, for a piece whose top
        // sits exactly at desiredTopZ.
        private bool TryFitSupportPiece(
            string name,
            string faction,
            int x,
            int y,
            int gapBottom,
            int desiredTopZ,
            Placement topPlacement,
            out PendingSupportPlacement piece,
            out int bottomZ)
        {
            foreach (string supportName in PlatformAutofillRules.EnumerateSupportTemplateNames(MaxSupportIndex, faction))
            {
                if (!TryGetSupportSpec(supportName, out BlockObjectSpec? supportSpec)
                    || supportSpec == null)
                {
                    LogDiagnostic(
                        $"support '{supportName}' unavailable for top '{name}'");
                    continue;
                }

                if (!TryCreateSupportPlacement(
                        supportName,
                        supportSpec,
                        x,
                        y,
                        gapBottom,
                        desiredTopZ,
                        topPlacement.Orientation,
                        topPlacement.FlipMode,
                        out Placement supportPlacement,
                        out int supportBottomZ,
                        out int supportTopZ,
                        out string searchSummary))
                {
                    LogDiagnostic(
                        $"support '{supportName}' no fit for gapBottom={gapBottom} desiredTop={desiredTopZ}: {searchSummary}");
                    continue;
                }

                LogDiagnostic(
                    $"support '{supportName}' queued placementZ={supportPlacement.Coordinates.z} occupiedZ={supportBottomZ}..{supportTopZ} " +
                    $"for desiredTop={desiredTopZ}: {searchSummary}");

                piece = new PendingSupportPlacement(supportName, supportSpec, supportPlacement);
                bottomZ = supportBottomZ;
                return true;
            }

            piece = default;
            bottomZ = int.MinValue;
            return false;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

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

        private bool TryCreateSupportPlacement(
            string supportName,
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
            bool found = PlatformAutofillRules.TrySelectSupportPlacement(
                gapBottomZ,
                desiredTopZ,
                supportSpec.Size.z,
                candidateZ =>
                {
                    Placement candidatePlacement = new Placement(new Vector3Int(x, y, candidateZ), orientation, flipMode);
                    return TryGetOccupiedZRange(supportSpec, candidatePlacement, out int minZ, out int maxZ)
                        ? new PlatformAutofillRules.OccupiedZRange(minZ, maxZ)
                        : null;
                },
                candidateZ =>
                {
                    Placement candidatePlacement = new Placement(new Vector3Int(x, y, candidateZ), orientation, flipMode);
                    // The bottom piece rests on terrain or an existing object, so
                    // it must pass the game's own validation. Otherwise supports
                    // got stacked on things that cannot carry them (e.g.
                    // impermeable floors), which broke or crashed the game.
                    bool isBottomPiece = TryGetOccupiedZRange(supportSpec, candidatePlacement, out int minZ, out _)
                        && minZ == gapBottomZ;
                    return IsSupportPlacementValid(supportName, candidatePlacement, strict: isBottomPiece);
                },
                out PlatformAutofillRules.SupportPlacementSelection selection,
                out searchSummary);

            if (!found)
            {
                placement = default;
                supportBottomZ = int.MinValue;
                supportTopZ = int.MinValue;
                return false;
            }

            placement = new Placement(new Vector3Int(x, y, selection.CandidateZ), orientation, flipMode);
            supportBottomZ = selection.BottomZ;
            supportTopZ = selection.TopZ;
            return true;
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
            // Existing world objects are treated as hard blockers/bases so we
            // never auto-inject supports through an occupied cell and end up
            // creating a structure that becomes invalid on reload.
            return PlatformAutofillRules.FindGapBottom(
                terrainTop,
                placedBottomZ,
                z =>
                {
                    Vector3Int coordinates = new Vector3Int(x, y, z);
                    return _blockService.AnyObjectAt(coordinates)
                        || (includePreviews && _previewBlockService.GetPreviewsAt(coordinates).Any());
                });
        }

        private string BuildValidationSummary(string templateName, Placement placement)
        {
            return TryValidateSupportPlacement(templateName, placement, strict: false, out bool isValid, out string errorMessage)
                ? $"isValid={isValid} error='{errorMessage}'"
                : "no-placeable-spec";
        }

        private static string FormatBlocks(BlockObjectSpec blockSpec, Placement placement)
        {
            var blocks = blockSpec.GetBlocks(placement)
                .Select(block => block.Coordinates.ToString())
                .ToList();

            return "[" + string.Join(", ", blocks) + "]";
        }

        private bool IsSupportPlacementValid(string templateName, Placement placement, bool strict)
        {
            return TryValidateSupportPlacement(templateName, placement, strict, out bool isValid, out _)
                && isValid;
        }

        private bool TryValidateSupportPlacement(
            string templateName,
            Placement placement,
            bool strict,
            out bool isValid,
            out string errorMessage)
        {
            isValid = false;
            errorMessage = "no-placeable-spec";

            if (!TryGetSupportPlaceableSpec(templateName, out PlaceableBlockObjectSpec? placeableSpec)
                || placeableSpec == null)
            {
                return false;
            }

            Preview? preview = null;
            bool previousStrict = _strictValidation;
            // Our own shown support previews sit exactly where the probe goes;
            // keep them out of the game's collision checks while probing so the
            // dragged block isn't wrongly rejected (it vanished/turned red).
            List<Preview>? suspendedPreviews = null;
            if (strict && _supportPreviews.Count > 0)
            {
                suspendedPreviews = new List<Preview>(_supportPreviews);
                foreach (Preview shownPreview in suspendedPreviews)
                {
                    shownPreview.RemoveFromPreviewServices();
                }
            }

            _supportValidationDepth++;
            _strictValidation = previousStrict || strict;
            try
            {
                preview = RentPreview(placeableSpec);
                preview.Reposition(placement);

                var previews = new List<BaseComponent> { preview };
                isValid = _validationService.AreValid(previews, out errorMessage);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"validation-exception={ex.GetType().Name}:{ex.Message}";
                return true;
            }
            finally
            {
                _strictValidation = previousStrict;
                _supportValidationDepth--;
                if (preview != null)
                {
                    preview.Hide();
                    preview.RemoveFromPreviewServices();
                    ReturnPreview(preview);
                }

                if (suspendedPreviews != null)
                {
                    foreach (Preview shownPreview in suspendedPreviews)
                    {
                        shownPreview.AddToPreviewServices();
                    }
                }
            }
        }

        private bool AnyWorldObjectAt(BlockObjectSpec blockSpec, Placement placement, BlockObject? ignore)
        {
            foreach (var block in blockSpec.GetBlocks(placement))
            {
                foreach (BlockObject existing in _blockService.GetObjectsAt(block.Coordinates))
                {
                    if (existing != null && existing != ignore)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int GetSupportBottomZ(PendingSupportPlacement pendingSupport)
        {
            return TryGetOccupiedZRange(pendingSupport.SupportSpec, pendingSupport.Placement, out int minZ, out _)
                ? minZ
                : pendingSupport.Placement.Coordinates.z;
        }

        private void OnSupportPlaced(BaseComponent component, PendingSupportPlacement pendingSupport)
        {
            if (!TryGetBlockObject(component, out BlockObject? blockObject) || blockObject == null)
            {
                LogDiagnostic(
                    $"support '{pendingSupport.SupportName}' callback did not yield a BlockObject at {pendingSupport.Placement.Coordinates}");
                return;
            }

            RefreshSupportAccesses(blockObject, pendingSupport);
        }

        private static void RefreshSupportAccesses(BlockObject blockObject, PendingSupportPlacement pendingSupport)
        {
            try
            {
                HighBlockObjectAccessesAdder? accessesAdder = blockObject.GetComponent<HighBlockObjectAccessesAdder>();
                AddAccessesAboveGroundMethod?.Invoke(accessesAdder, Array.Empty<object>());

                BlockObjectAccessible? blockObjectAccessible = blockObject.GetComponent<BlockObjectAccessible>();
                UpdateBoundsMethod?.Invoke(blockObjectAccessible, Array.Empty<object>());
                UpdateAccessesMethod?.Invoke(blockObjectAccessible, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[PlatformAutofill] support access refresh failed for '{pendingSupport.SupportName}' at {pendingSupport.Placement.Coordinates}: {ex}");
            }
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
                preview = RentPreview(spec);
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
                    ReturnPreview(preview);
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
                return TryAppendCompleteSupportStack(
                    templateName,
                    resolvedFaction,
                    runtimeSpec,
                    placement,
                    includePreviews: false,
                    supportPlacements,
                    knownSupportCoords);
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
            if (PlatformAutofillRules.TryExtractFaction(templateName, out faction) && !string.IsNullOrEmpty(faction))
            {
                return true;
            }

            faction = _factionService.Current?.Id;
            return !string.IsNullOrEmpty(faction);
        }

        private bool HasSupportTemplateForFaction(string faction)
        {
            foreach (string prefix in PlatformAutofillRules.SupportTemplatePrefixes)
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
