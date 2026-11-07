using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using DreamDustAutomation.Planning;
using DreamDustAutomation.UI;

namespace DreamDustAutomation
{
    internal sealed class MachineController
    {
        private PlannedInputHighlights plannedHighlights;
        private const string GradeTag = "DDA_GRADE", SourceTag = BatchInputReservation.SourceTag;
        private const string AssembledTag = "DDA_ASSEMBLED_V1";
        private const long ShellValue = 500;
        private const string ProductTag = "DDA_PRODUCT";
        private const string DirectTag = "DDA_DIRECT_FORM", LabelProductTag = "DDA_LABEL_PRODUCT";
        private const string DirectSolventsTag = "DDA_DIRECT_SOLVENTS";
        private const string AlcoholTag = "DDA_ALCOHOL_PERCENT", BufferTag = "DDA_MASH_BUFFER_V1";
        private static readonly ControllerRegistry<MachineController> instances = new ControllerRegistry<MachineController>();
        private static readonly string[] coreIds = { "blender", "heating_plate", "evaporator", "chem_finisher" };
        private static readonly int[] coreValues = { 200, 250, 300, 300 };
        private readonly GameItem machine;
        private readonly DeferredPanelRow layout;
        private readonly GameGridScrollableInventory raw = new GameGridScrollableInventory(12, 8, 24, 1f);
        // Keep the old second storage node in place for save compatibility. Its
        // contents are migrated into the common input bin after loading.
        private readonly GameGridScrollableInventory supplies = new GameGridScrollableInventory(12, 8, 12, 1f);
        private readonly ProductInventory output = new ProductInventory();
        private readonly Dictionary<string, GridShape> productShapes = new Dictionary<string, GridShape>();
        private readonly GameGridScrollableInventory waste = new GameGridScrollableInventory(16, 8, 16, 1f);
        private readonly HopperOutputGate[] hopperGates;
        private GridShape blockedOutputShape;
        private readonly MachineryText wasteStatus;
        private bool collectingWaste, wasteBlocked;
        private readonly GameSlotInventory[] coreSlots = new GameSlotInventory[4];
        private readonly GameItem[] cores = new GameItem[4];
        private readonly HardwareBinding[] coreBindings = new HardwareBinding[4];
        private readonly MachineryText hardwareHelp;
        private InventoryLock tankLock;
        private LiquidSourceBinding tankInteraction;
        private InventoryLock recoveryLock;
        private LiquidSourceBinding recoveryBinding;
        private Action<GameItem, GameInventory, SlotMarker> recoveryChanged;
        private GameSlotInventory recoverySlot;
        private GameItem recoveryContainer;
        private Action<GameItem, GameInventory, SlotMarker> tankTick;
        private Action<GameItem, GameInventory, SlotMarker> tankRemoved;
        private RunningBatch formingBatch;
        private readonly MachineryText status;
        private readonly MachineryText resultHeading;
        private readonly MachineryText previewText;
        private readonly MachineryText reagentText;
        private readonly MachineryText concentrationAdvice;
        private readonly MachineryText resultGrade;
        private readonly MachineryText resultBottles;
        private readonly MachineryText progressCaption;
        private readonly MachineryText progressValue;
        private readonly MachineRegion progressValueRegion;
        private readonly MachineryText progressDetails;
        private readonly MachineryProgressBar progressBar;
        private readonly MachineryStack progressDisplay;
        private readonly MachineryButton previewButton;
        private readonly MachineryButton startButton;
        private readonly MachineryButton stopButton;
        private readonly MachineryButton outputModeButton;
        private bool deferOutput;
        private int releaseQuantity, releaseStep = -1;
        private readonly MachineryText residueHeading;
        private readonly MachineryText residueDetails;
        private readonly MachineryText residueAdvice;
        private readonly MachineryStack residueDisplay;
        private readonly MachineryStack residueInformation;
        private readonly MachineryButton confirmResidueButton;
        private readonly ResidueStartConfirmation residueConfirmation = new ResidueStartConfirmation();
        private readonly TankCleanup tankCleanup = new TankCleanup();
        private readonly BatchInputReservation reservedInputs = new BatchInputReservation();
        private readonly MachineryButton cleanupButton;
        private readonly MachineryButton confirmCleanupButton;
        private readonly MachineryButton cancelCleanupButton;
        private readonly GradePicker grades;
        private readonly GradePicker alcoholOptions;
        private readonly MachineryButton alcoholButton;
        private readonly MachineryText alcoholAvailability;
        private string alcoholViewKey;
        private bool alcoholAvailable = true;
        private int alcoholPercent = 100;
        private GameSlotInventory bufferSlot;
        private GameItem mashBuffer;
        private InventoryLock bufferLock;
        private LiquidSourceBinding bufferBinding;
        private Action<GameItem, GameInventory, SlotMarker> bufferChanged;
        private readonly MachineryButton productButton;
        private readonly MachineryButton modeButton;
        private readonly MachineryText modeHint;
        private readonly MachineryText inputSummary, inputDetails;
        private readonly MachineryTabs helpTabs;
        private InputGuidance inputGuidance;
        private bool inputGuidanceDirty = true;
        private bool directForm;
        private bool directSolvents;
        private DirectDryingMethod directDrying = DirectDryingMethod.Evaporate;
        private DirectDryingMethod normalDrying = DirectDryingMethod.Evaporate;
        private readonly GradePicker normalDryingOptions;
        private string normalDryingViewKey, normalHeatFailure;
        private readonly GradePicker directDryingOptions;
        private readonly MachineryStack directDryingPanel;
        private readonly MachineryText directDryingTitle, directDryingHint;
        private string directDryingViewKey;
        private readonly GradePicker labelOptions;
        private readonly GradePicker labelPercentages;
        private readonly MachineryTabs labelTabs;
        private readonly MachineryButton labelProductButton;
        private readonly MachineryButton labelApply;
        private readonly MachineryButton labelApplyAll;
        private readonly MachineryText labelStatus;
        private readonly MachineryText labelPreview;
        private readonly string[] labelCaptions;
        private readonly List<MachineryButton> labelProductButtons = new List<MachineryButton>();
        private readonly Dictionary<string, MachineryText> labelCategorySummaries = new Dictionary<string, MachineryText>();
        private Dictionary<string, ProductLabelRule> labelRules;
        private string labelProductId = ChemicalRecipe.DefaultId;
        private bool labelsDirty = true;
        private bool labeling;
        private readonly List<MachineryButton> recipeButtons = new List<MachineryButton>();
        private readonly MachineryTabs controlTabs;
        private readonly MachineryTabs storageTabs;
        private readonly MachineryButton sortButton;
        private readonly PixelElement legacyInputsPage;
        private GameSlotInventory tankSlot;
        private GameItem tank;
        private GameItem finisherTank;
        private GameSlotInventory finisherSlot;
        private LiquidSourceBinding finisherBinding;
        private Action<GameItem, GameInventory, SlotMarker> finisherChanged;
        private const string FinisherSeedTag = "DDA_FINISHER_TANK_V1";
        private Action<GameItem, GameInventory, SlotMarker> evaporate;
        private RunningBatch running;
        private BatchPlan preview;
        private BatchPlan maximumPreview;
        private readonly QuantityControl quantityControl;
        private readonly MachineRegion quantityRegion, yieldMetricRegion;
        private int selectedQuantity;
        private string quantityViewKey;
        private bool quantitySearching;
        private BatchInputs previewInputs;
        private string[] previewFeedReagents = new string[0];
        private string previewFingerprint;
        private PlanningInputState previewState;
        private readonly PlanningDiagnostics lastDiagnostics = new PlanningDiagnostics();
        private int searchGeneration;
        private bool searching;
        private bool internalMove;
        private bool restored;
        private int requestedGrade = 99;
        private string selectedProductId = ChemicalRecipe.DefaultId;
        private int preparedHeatStep = -1;
        private string lastStatusKey = "idle";
        private object[] lastStatusArgs = new object[0];
        private GameItem residueViewTank;
        private string residueViewProduct;
        private int[] residueViewParts;
        private bool residueViewCleanup, residueViewValid;
        private bool Busy => searching || running != null;
        private bool RecoveryBusy => searching || (running != null && running.usesRecoveredSolvent);
        private bool WindowVisible => machine.contentWindow != null && machine.contentWindow.IsVisible();
        private static string T(string key, params object[] args) => ModEntry.Text(key, args).Replace("\\n", "\n");

        internal static GameItem Create()
        {
            var controller = new MachineController();
            instances.Add(controller);
            return controller.machine;
        }

        private MachineController()
        {
            machine = ItemDirectory.CreateEmptyItem();
            hopperGates = new GameInventory[] { raw, supplies, output, waste }.Select(inv => new HopperOutputGate(inv)).ToArray();
            for (int i = 0; i < coreSlots.Length; i++) coreSlots[i] = new GameSlotInventory(3, 2);
            foreach (var inventory in VisualInventories()) MachineInventoryVisuals.Bind(inventory);
            status = new MachineryText(T("idle"), 7, uppercase: false);
            wasteStatus = new MachineryText(T("waste_hint"), 6, uppercase: false).SetColor(new Color32(185, 190, 177, 255));
            resultHeading = new MachineryText("", 7, uppercase: false);
            resultHeading.handler.gameObject.SetActive(false);
            previewText = new MachineryText(T("preview_hint"), 6, uppercase: false);
            reagentText = new MachineryText("");
            concentrationAdvice = new MachineryText("", 6, uppercase: false).SetColor(new Color32(255, 225, 138, 255));
            concentrationAdvice.handler.gameObject.SetActive(false);
            resultGrade = new MachineryText("—", 14, uppercase: false).SetColor(new Color32(166, 231, 207, 255));
            resultBottles = new MachineryText("—", 14, uppercase: false).SetColor(new Color32(245, 222, 167, 255));
            progressCaption = new MachineryText("", 6, uppercase: false);
            progressValue = new MachineryText("", 14, uppercase: false);
            progressDetails = new MachineryText("", 6, uppercase: false);
            progressDetails.handler.gameObject.SetActive(false);
            progressBar = new MachineryProgressBar().SetColor(new Color32(166, 231, 207, 255));
            progressValueRegion = new MachineRegion(progressValue, MachineGeometry.ControlWidth - 8, 24);
            progressDisplay = MachineryStack.Vertical(progressCaption, progressValueRegion, progressDetails, progressBar);
            progressDisplay.handler.gameObject.SetActive(false);
            previewButton = new MachineryButton(T("calculate"), () => TimedGuard(() =>
            { if (DeferredOutput.Waiting(running)) ReleaseProducts(true); else BeginPreview(); }, 4, "calculate_or_release"));
            startButton = new MachineryButton(T("start"), () => TimedGuard(Start, 4, "start"));
            quantityControl = new QuantityControl(MachineGeometry.ControlWidth, T("quantity_help"));
            quantityControl.Changed = SelectQuantity;
            quantityRegion = new MachineRegion(quantityControl, MachineGeometry.ControlWidth, 16);
            stopButton = new MachineryButton(T("stop"), () => TimedGuard(Stop, 4, "stop"));
            outputModeButton = new MachineryButton(T("defer_output_off"), () => TimedGuard(ToggleDeferredOutput, 4, "defer_output"));
            residueHeading = new MachineryText(T("residue_empty"), 7, uppercase: false);
            residueDetails = new MachineryText("", 6, uppercase: false);
            residueAdvice = new MachineryText("", 6, uppercase: false);
            cleanupButton = new MachineryButton(T("cleanup"), () => TimedGuard(RequestTankCleanup, 4, "cleanup_preview"));
            confirmCleanupButton = new MachineryButton(T("cleanup_confirm", 0), () => TimedGuard(ConfirmTankCleanup, 4, "cleanup_confirm"));
            cancelCleanupButton = new MachineryButton(T("cleanup_cancel"), () => TimedGuard(CancelTankCleanup, 4, "cleanup_cancel"));
            confirmCleanupButton.handler.gameObject.SetActive(false);
            cancelCleanupButton.handler.gameObject.SetActive(false);
            foreach (var text in new[] { resultHeading, previewText, reagentText, concentrationAdvice, resultGrade, resultBottles,
                residueHeading, residueDetails, residueAdvice }) text.SetAlignment(TMPro.HorizontalAlignmentOptions.Left);
            residueInformation = MachineryStack.Vertical(
                new MachineRegion(residueHeading, MachineGeometry.InfoCardWidth - 8, 14),
                new MachineRegion(residueDetails, MachineGeometry.InfoCardWidth - 8, 28),
                new MachineRegion(residueAdvice, MachineGeometry.InfoCardWidth - 8, 26))
                .SetMainAlign(MachineryStack.Align.Start).SetAlign(MachineryStack.Align.Start);
            residueInformation.gap = 1;
            residueDisplay = MachineryStack.Vertical(cleanupButton, confirmCleanupButton, cancelCleanupButton)
                .SetMainAlign(MachineryStack.Align.Start);
            residueDisplay.gap = 2;
            residueDisplay.handler.gameObject.SetActive(false);
            confirmResidueButton = new MachineryButton(T("residue_confirm", 99), () => TimedGuard(() => StartBatch(true), 4, "start_with_residue"));
            confirmResidueButton.handler.gameObject.SetActive(false);
            controlTabs = new MachineryTabs();
            productButton = new MachineryButton(T("selected_product", MachineUiNames.Product(selectedProductId)), () => TimedGuard(() => controlTabs.Select(1), 4, "choose_product"));
            modeButton = new MachineryButton(T("mode_normal"), () => TimedGuard(ToggleMode, 4, "select_mode"));
            modeHint = new MachineryText(T("normal_help"), 6, uppercase: false);
            inputSummary = new MachineryText(T("input_empty"), 6, uppercase: false);
            inputDetails = new MachineryText("", 6, uppercase: false);
            alcoholButton = new MachineryButton(T("alcohol_button"), () =>
            { if (directForm) TimedGuard(ToggleDirectSolvents, 4, "direct_solvents"); else controlTabs.Select(2); });
            alcoholAvailability = new MachineryText("", 7, uppercase: false);
            alcoholOptions = new GradePicker(AlcoholBudget.Percentages.Select(x => x + "%").ToArray(), index => TimedGuard(() =>
            {
                int choice = AlcoholBudget.Percentages[index];
                if (choice == alcoholPercent) return;
                if (Busy || directForm || !AlcoholBudget.Read(Inputs()).AllowsPlanning(choice))
                { alcoholOptions.Select(Array.IndexOf(AlcoholBudget.Percentages, alcoholPercent)); return; }
                alcoholPercent = choice;
                machine.ModifyTag(AlcoholTag, s => s.Enable().SetInt(choice));
                preview = null;
                ResetGradeResults();
                ClearPreviewText();
                RefreshAlcoholControls();
                SetStatus("idle");
            }, 4, "select_alcohol"));
            grades = new GradePicker(GradePolicy.Grades.Select(x => x + "%").ToArray(), index => TimedGuard(() =>
            {
                if (requestedGrade == GradePolicy.Grades[index]) return;
                if (Busy || directForm) { grades.Select(Array.IndexOf(GradePolicy.Grades, requestedGrade)); return; }
                requestedGrade = GradePolicy.Grades[index];
                machine.ModifyTag(GradeTag, s => s.Enable().SetInt(requestedGrade));
                preview = null;
                ResetGradeResults();
                ClearPreviewText();
            }, 4, "select_grade"), columns: 3);
            normalDryingOptions = new GradePicker(new[] { T("normal_evaporate_choice"), T("direct_heat_choice") },
                index => TimedGuard(() => SelectNormalDrying(index == 0 ? DirectDryingMethod.Evaporate : DirectDryingMethod.Heat), 4, "select_normal_drying"));
            directDryingOptions = new GradePicker(new[] { T("direct_evaporate_choice"), T("direct_heat_choice") },
                index => TimedGuard(() => SelectDirectDrying(index == 0 ? DirectDryingMethod.Evaporate : DirectDryingMethod.Heat), 4, "select_direct_drying"));
            directDryingTitle = new MachineryText(T("direct_drying_title"), 6, uppercase: false);
            directDryingHint = new MachineryText(T("direct_evaporate_help"), 6, uppercase: false);
            directDryingPanel = MachineryStack.Vertical(directDryingTitle, directDryingOptions, directDryingHint)
                .SetMainAlign(MachineryStack.Align.Start);
            directDryingPanel.gap = 2;
            directDryingPanel.handler.gameObject.SetActive(false);
            var gradeMetric = MachineryStack.Vertical(new MachineryText(T("metric_grade"), alignment: TMPro.HorizontalAlignmentOptions.Left), resultGrade)
                .SetMainAlign(MachineryStack.Align.Start).SetAlign(MachineryStack.Align.Start);
            var yieldMetric = MachineryStack.Vertical(new MachineryText(T("metric_bottles"), alignment: TMPro.HorizontalAlignmentOptions.Left), resultBottles)
                .SetMainAlign(MachineryStack.Align.Start).SetAlign(MachineryStack.Align.Start);
            gradeMetric.gap = yieldMetric.gap = 0;
            yieldMetricRegion = new MachineRegion(yieldMetric, 63, 30);
            var metrics = MachineryStack.Horizontal(new MachineRegion(gradeMetric, 63, 30), yieldMetricRegion);
            metrics.gap = 4;
            var resultCaption = MachineryStack.Vertical(resultHeading, previewText).SetMainAlign(MachineryStack.Align.Start);
            resultCaption.gap = 0;
            var resultContent = MachineryStack.Vertical(
                new MachineRegion(resultCaption, MachineGeometry.InfoCardWidth - 8, 20),
                metrics,
                new MachineRegion(reagentText, MachineGeometry.InfoCardWidth - 8, 33),
                new MachineRegion(concentrationAdvice, MachineGeometry.InfoCardWidth - 8, 16));
            resultContent.gap = 1;
            // Titled native frames consume 22 pixels vertically. Both cards use
            // the old combined information budget, so the window does not grow.
            var resultCard = MachineHelper.Panel(T("batch_plan"), new MachineRegion(resultContent
                .SetMainAlign(MachineryStack.Align.Start).SetAlign(MachineryStack.Align.Start),
                MachineGeometry.InfoCardWidth - 8, MachineGeometry.InfoCardHeight - 22), "preview");
            var residueCard = MachineHelper.Panel(T("residue_block"), new MachineRegion(residueInformation,
                MachineGeometry.InfoCardWidth - 8, MachineGeometry.InfoCardHeight - 22), "residue");
            var informationCards = MachineryStack.Horizontal(resultCard, residueCard).SetMainAlign(MachineryStack.Align.Start);
            informationCards.gap = 4;
            var dryingChoices = MachineryStack.Vertical(grades, normalDryingOptions, directDryingPanel).SetMainAlign(MachineryStack.Align.Start);
            dryingChoices.gap = 3;
            // Hidden mode/confirmation controls retain their slots. Information
            // lives on the left; these actions never move when notices change.
            var controls = MachineryStack.Vertical(
                new MachineRegion(productButton, MachineGeometry.ControlWidth, 16), new MachineRegion(modeButton, MachineGeometry.ControlWidth, 16),
                new MachineRegion(modeHint, MachineGeometry.ControlWidth, 20), new MachineRegion(alcoholButton, MachineGeometry.ControlWidth, 16),
                new MachineRegion(residueDisplay, MachineGeometry.ControlWidth, 30),
                new MachineRegion(dryingChoices, MachineGeometry.ControlWidth, 54),
                new MachineRegion(previewButton, MachineGeometry.ControlWidth, 16),
                new MachineRegion(MachineryStack.Vertical(quantityRegion, confirmResidueButton), MachineGeometry.ControlWidth, 16),
                new MachineRegion(startButton, MachineGeometry.ControlWidth, 16),
                new MachineRegion(MachineryStack.Horizontal(
                    new MachineRegion(outputModeButton, (MachineGeometry.ControlWidth - 4) / 2, 16),
                    new MachineRegion(stopButton, (MachineGeometry.ControlWidth - 4) / 2, 16)), MachineGeometry.ControlWidth, 16),
                MachineHelper.Panel(T("machine_status"), new MachineRegion(MachineryStack.Vertical(status, progressDisplay)
                    .SetMainAlign(MachineryStack.Align.Start), MachineGeometry.ControlWidth - 8, 68), "status"));
            controls.gap = 3;
            var recipeList = new MachineryStack();
            foreach (string id in ChemicalRecipe.Ids)
            {
                string productId = id;
                var button = new MachineryButton(MachineUiNames.Product(id), () => TimedGuard(() => SelectProduct(productId), 4, "select_product"))
                    .SetDescription(T("recipe_" + id));
                recipeButtons.Add(button);
                recipeList.Add(button);
            }
            controlTabs.AddTab(T("tab_production"), new MachineRegion(controls.SetMainAlign(MachineryStack.Align.Start), MachineGeometry.ControlWidth, MachineGeometry.ControlHeight));
            controlTabs.AddTab(T("tab_recipes"), new MachineRegion(recipeList.SetMainAlign(MachineryStack.Align.Start), MachineGeometry.ControlWidth, MachineGeometry.ControlHeight));
            controlTabs.AddTab(T("tab_alcohol"), new MachineRegion(MachineryStack.Vertical(new MachineryText(T("alcohol_help"), 7, uppercase: false),
                alcoholOptions, alcoholAvailability).SetMainAlign(MachineryStack.Align.Start), MachineGeometry.ControlWidth, MachineGeometry.ControlHeight));
            labelStatus = new MachineryText("", 7, uppercase: false);
            labelPreview = new MachineryText("", 7, uppercase: false);
            labelRules = ProductLabels.LoadRules(machine);
            labelTabs = new MachineryTabs();
            labelCaptions = new[] { T("label_actual") }.Concat(ProductLabels.Choices().Select(c => c.display)).Concat(new[] { T("label_keep") }).ToArray();
            labelProductButton = new MachineryButton(T("selected_product", MachineUiNames.Product(labelProductId)), () => labelTabs.Select(1));
            labelOptions = new GradePicker(labelCaptions, index =>
            {
                if (labeling) { RefreshLabelSelection(); return; }
                SetLabelRule(new ProductLabelRule(index == 8 ? -1 : index, labelRules[labelProductId].Percent));
            });
            labelPercentages = new GradePicker(ProductLabels.Percentages.Select(percent => percent + "%").ToArray(), index =>
            {
                if (labeling) { RefreshLabelSelection(); return; }
                SetLabelRule(new ProductLabelRule(labelRules[labelProductId].Grade, ProductLabels.Percentages[index]));
            });
            labelPercentages.Select(4);
            labelApply = new MachineryButton(T("labels_apply"), () => BeginLabeling(false));
            labelApplyAll = new MachineryButton(T("labels_apply_all"), () => BeginLabeling(true));
            labelTabs.AddTab(T("labels_settings"), MachineryStack.Vertical(labelProductButton,
                new MachineryText(T("labels_help"), 6, uppercase: false), labelOptions,
                new MachineryText(T("labels_percentage"), 7, uppercase: false), labelPercentages,
                labelPreview, labelApply, labelApplyAll, labelStatus));
            var labelCategories = new MachineryStack();
            foreach (string id in ChemicalRecipe.Ids)
            {
                string productId = id;
                var button = new MachineryButton(MachineUiNames.Product(id), () => SelectLabelProduct(productId));
                labelProductButtons.Add(button);
                var summary = new MachineryText("", 6, uppercase: false);
                labelCategorySummaries[id] = summary;
                labelCategories.Add(MachineryStack.Vertical(button, summary));
            }
            labelTabs.AddTab(T("labels_categories"), labelCategories);
            controlTabs.AddTab(T("tab_labels"), new MachineRegion(labelTabs, MachineGeometry.ControlWidth, MachineGeometry.ControlHeight));
            helpTabs = new MachineryTabs();
            helpTabs.AddTab(T("help_current"), MachineryStack.Vertical(
                new MachineryText(T("help_check_intro"), 7, uppercase: false), inputDetails));
            helpTabs.AddTab(T("help_modes"), new MachineryText(T("help_modes_body"), 6, uppercase: false));
            helpTabs.AddTab(T("help_materials"), new MachineryText(T("help_materials_body"), 6, uppercase: false));
            controlTabs.AddTab(T("tab_help"), new MachineRegion(helpTabs, MachineGeometry.ControlWidth, MachineGeometry.ControlHeight));
            controlTabs.onChanged += index => { if (index == 3) RefreshLabelPreview(); };
            controlTabs.onChanged += index => { if (index == 4) RefreshInputGuidance(); };
            hardwareHelp = new MachineryText(T("hardware_help"), 7, uppercase: false);
            var hardware = MachineryStack.Vertical(MachineryStack.Horizontal(
                MachineryStack.Vertical(MachineHelper.Panel(T("blender"), coreSlots[0]), MachineHelper.Panel(T("heater"), coreSlots[1])),
                MachineryStack.Vertical(MachineHelper.Panel(T("evaporator"), coreSlots[2]), MachineHelper.Panel(T("finisher"), coreSlots[3]))),
                hardwareHelp);
            // Tabs collect every page's storage, including hidden ones, in insertion
            // order. Preserve the old flat storage order for existing machine saves.
            storageTabs = new MachineryTabs();
            legacyInputsPage = StoragePage(supplies, T("legacy_inputs"));
            legacyInputsPage.handler.gameObject.SetActive(false);
            storageTabs.AddTab(T("tab_raw"), new MachineRegion(MachineryStack.Vertical(
                inputSummary, new MachineryButton(T("input_check_open"), () =>
                { RefreshInputGuidance(); helpTabs.Select(0); controlTabs.Select(4); })
                    .SetDescription(T("input_instructions") + "\n" + T("input_no_pitcher") + "\n" + T("recovery_hint")),
                StoragePage(raw, T("capacity_raw"), true), legacyInputsPage)
                    .SetMainAlign(MachineryStack.Align.Start), MachineGeometry.WorkspaceWidth, MachineGeometry.StorageHeight));
            storageTabs.AddTab(T("tab_hardware"), new MachineRegion(hardware.SetMainAlign(MachineryStack.Align.Start), MachineGeometry.WorkspaceWidth, MachineGeometry.StorageHeight));
            storageTabs.AddTab(T("tab_output"), new MachineRegion(MachineryStack.Vertical(
                new MachineryButton(T("labels_open"), () => controlTabs.Select(3)), StoragePage(output, T("capacity_output")))
                .SetMainAlign(MachineryStack.Align.Start), MachineGeometry.WorkspaceWidth, MachineGeometry.StorageHeight));
            // Append only: the existing seven storage indices are save data.
            storageTabs.AddTab(T("tab_waste"), new MachineRegion(MachineryStack.Vertical(StoragePage(waste, T("capacity_waste")), wasteStatus)
                .SetMainAlign(MachineryStack.Align.Start), MachineGeometry.WorkspaceWidth, MachineGeometry.StorageHeight));
            // Header action in the unused space after the four tabs. Attach to
            // the header, not its page container: no new tab or height allocation.
            sortButton = new MachineryButton(T("sort"), () => TimedGuard(SortInventories, 4, "sort_inventory"),
                MachineGeometry.SortWidth, 10).SetFillWidth(false).SetDescription(T("sort_help"));
            sortButton.handler.SetParent(storageTabs.handler);
            MachineryStyle.Place(sortButton.handler, MachineGeometry.SortX, 0, MachineGeometry.SortWidth, 10);
            layout = new DeferredPanelRow(
                MachineHelper.Panel(T("storage"), MachineryStack.Vertical(storageTabs, informationCards)
                    .SetMainAlign(MachineryStack.Align.Start), MachineUiNames.StorageRole),
                MachineHelper.Panel(T("control"), controlTabs, "control"));
            var window = new PixelWindow(true, null, WindowStyle.Machinery);
            window.Attach(layout);
            layout.AttachTo(window);
            plannedHighlights = layout.handler.gameObject.AddComponent<PlannedInputHighlights>();
            layout.handler.gameObject.AddComponent<MachineDragVisualPump>().owns = item =>
                item.parentInventory == raw || item.parentInventory == supplies || item.parentInventory == output || item.parentInventory == waste;
            machine.SetContentWindow(window);
            machine.contentWindow.closeCallback += () =>
            {
                if (tankCleanup.Pending) CancelTankCleanup();
            };
            machine.contentWindow.openCallback += () => TimedGuard(() =>
            {
                foreach (var inventory in VisualInventories()) MachineInventoryVisuals.Restore(inventory);
                RefreshInterfaceNames();
                RefreshInputGuidance();
                residueViewValid = false;
                RefreshResidueWarning();
                SetStatus(lastStatusKey, lastStatusArgs);
                RefreshLabelPreview();
                if (!Busy && preview != null) plannedHighlights.Show(PlannedInputs.Read(previewInputs, preview));
            }, 4, "open_machine");
            var storageOrder = ((MachineryPanelRow)machine.contentWindow.childElement).children;
            if (!storageOrder.SequenceEqual(new GraphNodeStorage[] { raw, supplies, coreSlots[0], coreSlots[1], coreSlots[2], coreSlots[3], output, waste }))
                throw new InvalidOperationException("Machine storage order must remain compatible with existing saves.");
            machine.SetSpriteAndShapeFromMod(ModEntry.Id, "synthesizer");
            machine.SetShape(new GridShapeBuilder().SetData(Enumerable.Repeat((byte)1, 16).ToArray(), 4));
            machine.name = T("name");
            machine.shortDescription = T("description");
            machine.unitValue = ShellValue;
            machine.SetGameItemType("MACHINE");
            machine.EnableTag("CONTAINER_TAG");
            AudioHelper.InitMetalMachine(machine);
            raw.mayInventoryAddItemFunc = (item, inv) => internalMove ||
                (!searching && GeneralHelper.IsItemOwned(item) && InventoryRules.Input(item));
            supplies.mayInventoryAddItemFunc = (item, inv) => internalMove;
            raw.mayInventoryRemoveItemFunc = supplies.mayInventoryRemoveItemFunc = (item, inv) =>
                internalMove || reservedInputs.CanRemove(item, searching, running != null);
            raw.maxSlotAddItemFunc = (item, inv, slot) =>
                internalMove || reservedInputs.CanStackInto(slot?.targetItem, running != null) ? int.MaxValue : 0;
            raw.onSlotAddItemEndFunc += (item, inv, slot) =>
            {
                if (!internalMove && running != null) reservedInputs.AddedDuringBatch(item);
                InvalidatePreview();
            };
            raw.onSlotRemoveItemEndFunc += (item, inv, slot) => InvalidatePreview();
            supplies.onSlotAddItemEndFunc += (item, inv, slot) => InvalidatePreview();
            supplies.onSlotRemoveItemEndFunc += (item, inv, slot) => InvalidatePreview();
            output.mayInventoryAddItemFunc = (item, inv) => InventoryRules.Product(item) &&
                (internalMove || GeneralHelper.IsItemOwned(item));
            output.onSlotAddItemEndFunc += (item, inv, slot) => { output.InvalidateCapacity(); labelsDirty = true; };
            output.onSlotRemoveItemEndFunc += (item, inv, slot) => { output.InvalidateCapacity(); labelsDirty = true; };
            waste.mayInventoryAddItemFunc = (item, inv) => WasteCollection.Accepts(item) &&
                (internalMove || GeneralHelper.IsItemOwned(item));
            for (int i = 0; i < coreSlots.Length; i++)
            {
                int index = i;
                coreSlots[i].mayInventoryAddItemFunc = (item, inv) => internalMove || (!Busy && item.identifier == coreIds[index]);
                // Cores can be removed while idle, including for safe uninstallation.
                coreSlots[i].mayInventoryRemoveItemFunc = (item, inv) => internalMove || !Busy;
                coreSlots[i].onSlotRemoveItemEndFunc += (item, inv, slot) => RestoreDetachedCore(item, index);
            }
            machine.onLoaded += item =>
            {
                // Native decoding overwrites factory values with the old save.
                item.unitValue = ShellValue;
                restored = false;
                RefreshInterfaceNames();
            };
            // DirectoryMaster.Item assigns ownership AFTER the factory returns and
            // destroys children found inside a factory result. Assemble only once
            // the finished shell has been placed, outside save decoding.
            machine.onSlotAddItemFunc += (item, inv, slot) =>
            {
                if (!SaveManager.IsDecoding && !machine.IsTag(AssembledTag)) Guard(Seed);
            };
            machine.onOneSecondItemFunc += (item, inv, slot) => TimedGuard(Tick, 0);
            machine.onCycleEndEarlySlotItemFunc += (item, inv, slot) => Guard(() =>
                AdjacentCharging.Apply(coreSlots[2].childItem, machine.FindAdjacentType<GameItem>(),
                    PlayerStore.Instance.isPowerOn, NetworkUpgrade.IsCommercialPowerUnlocked()));
            machine.onCycleEndSlotItemFunc += (item, inv, slot) => TimedGuard(Cycle, 1);
            RefreshButtons();
        }

        private IEnumerable<GameInventory> VisualInventories()
        {
            yield return raw;
            yield return supplies;
            yield return output;
            yield return waste;
            foreach (var slot in coreSlots) yield return slot;
        }

        private static PixelElement StoragePage(GameGridScrollableInventory inventory, string caption, bool showPlanHint = false)
        {
            MachineryInventoryStyle.Apply(inventory);
            return MachineryStack.Vertical(new MachineryText(caption, 7, uppercase: false),
                new MachineryIntake(inventory, vents: false), new MachineryText(T(showPlanHint ? "planned_input_hint" : inventory is ProductInventory ? "output_selection_hint" : "storage_hint"), 5, uppercase: false));
        }

        private static bool DraggingItems => (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem) ||
            (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging);

        private void SortInventories()
        {
            // Geometry-only, atomic on the main thread. Running batches keep the
            // same reserved objects/source tags; previews keep the same input order.
            // Waste collection yields only between complete transfers and keeps
            // item references, not planned slots. Sorting can run atomically
            // between those transfers without blocking on each one-second scan.
            if (searching || labeling || DraggingItems || SaveManager.IsDecoding) return;
            try
            {
                var bins = new[] { raw, output, waste };
                var plans = new InventorySort[bins.Length];
                for (int i = 0; i < bins.Length; i++)
                    if (!InventorySort.TryPlan(bins[i], out plans[i]))
                    { SortFeedback("sort_no_space"); return; }
                if (!InventorySort.TryApply(plans, item =>
                {
                    if (item is GameItemElement element) element.UpdateRectTransform();
                    else item.Validate();
                }, bin => bin.Validate(false))) { SortFeedback("sort_changed"); return; }
                foreach (var gate in hopperGates) gate.Clear();
                output.InvalidateViewport();
                blockedOutputShape = null;
                labelsDirty = true;
                SortFeedback("sort_done");
            }
            catch (Exception error)
            {
                ModEntry.Log.Error("Inventory sorting rolled back: " + error);
                SortFeedback("sort_failed");
            }
        }

        private void SortFeedback(string key)
        {
            sortButton.SetDescription(T(key) + "\n" + T("sort_help"));
            if (running == null) SetStatus(key);
        }

        private void RefreshInterfaceNames()
        {
            RefreshTabNames(storageTabs, "tab_raw", "tab_hardware", "tab_output", "tab_waste");
            RefreshTabNames(controlTabs, "tab_production", "tab_recipes", "tab_alcohol", "tab_labels", "tab_help");
            RefreshTabNames(labelTabs, "labels_settings", "labels_categories");
            RefreshTabNames(helpTabs, "help_current", "help_modes", "help_materials");
            for (int i = 0; i < ChemicalRecipe.Ids.Length; i++)
            {
                string name = MachineUiNames.Product(ChemicalRecipe.Ids[i]);
                SetButtonTextIfChanged(recipeButtons[i], name);
                SetButtonTextIfChanged(labelProductButtons[i], name);
            }
            SetButtonTextIfChanged(productButton, T("selected_product", MachineUiNames.Product(selectedProductId)));
            SetButtonTextIfChanged(labelProductButton, T("selected_product", MachineUiNames.Product(labelProductId)));
        }

        private static void RefreshTabNames(MachineryTabs tabs, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string text = T(keys[i]);
                if (tabs.GetCaption(i) != text) tabs.SetCaption(i, text);
            }
        }

        private void RefreshButtons()
        {
            RefreshTurbo();
            if (!WindowVisible) return;
            RefreshHardwareHelp();
            sortButton.SetDisabled(searching || labeling);
            bool showAdvice = !Busy && preview != null &&
                (!string.IsNullOrEmpty(preview.concentrationHint) || !string.IsNullOrEmpty(preview.quantityHint) || previewFeedReagents.Length > 0);
            if (concentrationAdvice.handler.gameObject.activeSelf != showAdvice)
            {
                concentrationAdvice.handler.gameObject.SetActive(showAdvice);
                MachineryContainerUtils.NotifySizeChanged(concentrationAdvice);
            }
            productButton.SetDisabled(Busy);
            modeButton.SetDisabled(Busy);
            foreach (var button in recipeButtons) button.SetDisabled(Busy);
            RefreshAlcoholControls();
            bool waitingOutput = DeferredOutput.Waiting(running);
            bool protectedOutput = DeferredOutput.Protected(running);
            SetButtonTextIfChanged(outputModeButton, T((running?.deferOutput ?? deferOutput) ? "defer_output_on" : "defer_output_off"));
            outputModeButton.SetDescription(T("defer_output_help"));
            outputModeButton.SetDisabled(Busy);
            SetButtonTextIfChanged(previewButton, T(waitingOutput ? "defer_output_all" : "calculate"));
            previewButton.SetDisabled(!waitingOutput && Busy);
            if (waitingOutput) previewButton.SetDescription(T("defer_output_wait_help"));
            else if (preview == null) previewButton.SetDescription(null);
            RefreshQuantityControl();
            if (waitingOutput) SetButtonTextIfChanged(startButton, T("defer_output_selected", releaseQuantity));
            else if (running != null) SetButtonTextIfChanged(startButton, T(running.plan.directForm ? "direct_start" : "start_grade", running.plan.actualGrade));
            if (protectedOutput && running.failed) SetButtonTextIfChanged(startButton, T("defer_output_retry"));
            startButton.SetDisabled(!waitingOutput && !(protectedOutput && running.failed) && (Busy || !alcoholAvailable || preview == null || residueConfirmation.Pending || tankCleanup.Pending));
            confirmResidueButton.SetDisabled(Busy || preview == null || !residueConfirmation.Pending || tankCleanup.Pending);
            bool canFinish = DeferredOutput.CanFinish(running);
            SetButtonTextIfChanged(stopButton, T(canFinish ? "batch_finish" : residueConfirmation.Pending ? "residue_cancel" : "stop"));
            stopButton.SetDescription(canFinish ? T("batch_finish_help") : null);
            stopButton.SetDisabled(!Busy && preview == null && !tankCleanup.Pending);
            for (int i = 0; i < GradePolicy.Grades.Length; i++) grades.Get(i).SetDisabled(Busy || directForm);
            RefreshCleanupButtons();
        }

        private void ToggleDeferredOutput()
        {
            if (Busy) return;
            deferOutput = !deferOutput;
            machine.ModifyTag(DeferredOutput.PreferenceTag, tag => tag.Enable().SetInt(deferOutput ? 1 : 0));
            if (previewInputs != null) previewInputs.deferOutput = deferOutput;
            RefreshButtons();
        }

        private void ToggleMode()
        {
            if (Busy) return;
            directForm = !directForm;
            machine.ModifyTag(DirectTag, tag => tag.Enable().SetInt(directForm ? 1 : 0));
            preview = null;
            alcoholViewKey = null;
            RefreshMode();
            ResetGradeResults();
            ClearPreviewText();
            SetStatus("idle");
        }

        private void ToggleDirectSolvents()
        {
            if (Busy || !directForm) return;
            directSolvents = !directSolvents;
            machine.ModifyTag(DirectSolventsTag, tag => tag.Enable().SetInt(directSolvents ? 1 : 0));
            preview = null;
            ResetGradeResults();
            ClearPreviewText();
            RefreshAlcoholControls();
            RefreshInputGuidance();
            SetStatus("idle");
        }

        private void SelectDirectDrying(DirectDryingMethod method)
        {
            int modules = cores[1] == null ? 0 : MachineBlender.GetUpgradeCount(cores[1]);
            if (Busy || !directForm || (method == DirectDryingMethod.Heat && !DirectDryingPolicy.CanSelectHeat(modules)))
            {
                directDryingViewKey = null;
                RefreshDirectDryingControls();
                return;
            }
            if (method == directDrying) return;
            directDrying = method;
            machine.ModifyTag(DirectDryingPolicy.PreferenceTag, tag => tag.Enable().SetInt((int)method));
            preview = null;
            ClearPreviewText();
            ResetGradeResults();
            SetStatus("direct_drying_changed");
        }

        private void SelectNormalDrying(DirectDryingMethod method)
        {
            if (Busy || directForm || method == normalDrying ||
                (method == DirectDryingMethod.Heat && (preview == null || preview.normalHeatBottles <= 0)))
            { normalDryingViewKey = null; RefreshNormalDryingControls(); return; }
            normalDrying = method;
            machine.ModifyTag(NormalDrying.PreferenceTag, tag => tag.Enable().SetInt((int)method));
            preview = null;
            ClearPreviewText();
            ResetGradeResults();
            // Switching methods changes the actual yield and metered doses.
            // Recalculate before enabling start; never reuse the other preview.
            BeginPreview();
        }

        private string NormalDryingSummary(BatchPlan plan)
        {
            if (plan == null || plan.directForm) return "";
            if (plan.normalHeatBottles <= 0) return T(plan.normalHeatReason ?? "normal_heat_check_first");
            string summary = T("normal_drying_compare", plan.evaporationBottles, plan.normalHeatBottles);
            if (plan.normalDrying == DirectDryingMethod.Heat)
                summary += "\n" + T(plan.actions.Any(a => a.operation == Operation.Dry && a.evaporationVolume > 0) ? "normal_heat_mixed" : "normal_heat_selected");
            else summary += "\n" + T("normal_evap_selected");
            return summary;
        }

        private void RefreshNormalDryingControls()
        {
            if (directForm) return;
            var plan = running?.plan ?? preview;
            var selected = plan?.normalDrying ?? normalDrying;
            bool canHeat = plan != null && plan.normalHeatBottles > 0;
            string summary = plan == null ? T(normalHeatFailure ?? "normal_heat_check_first") : NormalDryingSummary(plan);
            string key = selected + "/" + Busy + "/" + canHeat + "/" + summary;
            if (key == normalDryingViewKey) return;
            normalDryingViewKey = key;
            normalDryingOptions.Select(selected == DirectDryingMethod.Heat ? 1 : 0);
            normalDryingOptions.Get(0).SetText(T("normal_evaporate_choice")).SetDisabled(Busy);
            normalDryingOptions.Get(1).SetText(T("direct_heat_choice")).SetDisabled(Busy || !canHeat);
            string details = summary + "\n" + T("normal_drying_help");
            if (canHeat) details += "\n" + T("normal_heat_details", plan.normalHeatMinimumActive * 100.0 / ChemicalRecipe.Get(plan.productId).UnitVolume,
                plan.normalHeatSeconds, plan.boiledAlcohol / 1000.0);
            normalDryingOptions.Get(0).SetDescription(T("normal_evaporate_help"));
            normalDryingOptions.Get(1).SetDescription(details);
            SetTextIfChanged(modeHint, summary);
            RequestLayout();
        }

        private void RefreshDirectDryingControls()
        {
            int modules = cores[1] == null ? 0 : MachineBlender.GetUpgradeCount(cores[1]);
            bool canHeat = DirectDryingPolicy.CanSelectHeat(modules);
            var active = running?.plan;
            bool legacy = active != null && active.directDrying == DirectDryingMethod.LegacyAutomatic;
            var selected = active == null ? directDrying : legacy ?
                (active.actions.Any(a => a.operation == Operation.BoilDry) ? DirectDryingMethod.Heat : DirectDryingMethod.Evaporate) : active.directDrying;
            string hint = T(legacy ? "direct_drying_legacy" : selected == DirectDryingMethod.Heat ?
                (canHeat ? "direct_heat_help" : "direct_heat_modules") : canHeat ? "direct_evaporate_help" : "direct_evaporate_no_module");
            string evaporation = T("direct_evaporate_choice"), heating = T("direct_heat_choice"), title = T("direct_drying_title");
            string key = selected + "/" + Busy + "/" + canHeat + "/" + hint + "/" + evaporation + "/" + heating + "/" + title;
            if (key == directDryingViewKey) return;
            directDryingViewKey = key;
            directDryingOptions.Select(selected == DirectDryingMethod.Heat ? 1 : 0);
            directDryingOptions.Get(0).SetText(evaporation).SetDisabled(Busy);
            directDryingOptions.Get(1).SetText(heating).SetDisabled(Busy || !canHeat);
            directDryingOptions.Get(0).SetDescription(T("direct_evaporate_help"));
            directDryingOptions.Get(1).SetDescription(T(canHeat ? "direct_heat_help" : "direct_heat_modules"));
            SetTextIfChanged(directDryingTitle, title);
            SetTextIfChanged(directDryingHint, hint);
            RequestLayout();
        }

        private void RefreshMode()
        {
            SetButtonTextIfChanged(modeButton, T(directForm ? "mode_direct" : "mode_normal"));
            SetTextIfChanged(modeHint, T(directForm ? "direct_help" : "normal_help"));
            modeHint.handler.gameObject.SetActive(true);
            grades.handler.gameObject.SetActive(!directForm);
            normalDryingOptions.handler.gameObject.SetActive(!directForm);
            normalDryingViewKey = null;
            directDryingPanel.handler.gameObject.SetActive(directForm);
            directDryingViewKey = null;
            alcoholButton.handler.gameObject.SetActive(true);
            RefreshAlcoholControls();
            RequestLayout();
            RefreshInputGuidance();
        }

        private string InputHint(string key)
        {
            if (key != "input_other_product" && key != "input_other_kept") return T(key);
            string names = string.Join(" / ", inputGuidance.OtherProducts.Take(2).Select(id => T("product_" + id)));
            if (inputGuidance.OtherProducts.Length > 2) names += T("input_more_products");
            return T(key, names);
        }

        private void RefreshInputGuidance()
        {
            if (running != null || searching)
            {
                string busy = T(running != null ? "input_batch_locked" : "input_calculating");
                SetTextIfChanged(inputSummary, busy);
                SetTextIfChanged(inputDetails, (running?.failed == true ? T(running.failureKey ?? "failed") : busy) + "\n\n" + T(directForm ? "direct_help" : "normal_help"));
                inputSummary.SetColor(MachineryStyle.BODY_TEXT);
                inputGuidanceDirty = true;
                return;
            }
            inputGuidance = InputGuidance.Read(selectedProductId, directForm, AllInputs(), tank, mashBuffer,
                recoveryContainer, output.childItems.Count > 0, waste.childItems.Count > 0, directSolvents, finisherTank);
            string primary = preview != null && previewFeedReagents.Length > 0 ? "direct_solvent_preview" :
                preview != null && preview.directForm && preview.minimumActive == 0 ?
                "input_zero_preview" : inputGuidance.Summary;
            SetTextIfChanged(inputSummary, InputHint(primary));
            inputSummary.SetColor(inputGuidance.MissingReason != null ? new Color32(255, 170, 155, 255) :
                primary == "input_recognized" ? new Color32(185, 240, 150, 255) : new Color32(245, 222, 167, 255));
            var hints = inputGuidance.Hints.Select(InputHint).ToList();
            if (lastDiagnostics.Primary != null)
            {
                hints = hints.Take(2).ToList();
                var issue = lastDiagnostics.Primary;
                hints.Insert(0, (issue.Context == null ? "" : T(issue.Context) + "\n") + T(issue.Key, issue.SummaryArgs) + (issue.Detail == null ? "" : "\n" + T(issue.Detail, issue.Args)));
                hints.Insert(1, T("diag_more_help"));
                var advice = lastDiagnostics.Issues.FirstOrDefault(x => x.Key.StartsWith("purity_"));
                if (advice != null) hints.Insert(1, T(advice.Key) + "\n" + T(advice.Detail));
            }
            if (preview != null && previewFeedReagents.Length > 0)
                hints.Insert(0, T("direct_solvent_detail", string.Join("、", previewFeedReagents.Select(id => Liquid.GetLiquidFromId(id).displayName))));
            if (!string.IsNullOrEmpty(preview?.quantityHint))
                hints.Insert(0, T("partial_stock_limit", T(preview.quantityHint)) + "\n" + T(preview.quantityHint + "_action"));
            if (primary == "input_zero_preview") hints.Insert(0, T(primary));
            if (cores.All(core => core != null) && tank != null)
            {
                var use = NeutralizerUsage.Read(preview != null && previewInputs != null ? previewInputs : Inputs(), preview);
                if (use.Acid + use.Base > 0 || (!directForm && inputGuidance.HasFeedstock))
                {
                    var detail = T("neutralizer_stock", use.Acid / 1000.0, use.Base / 1000.0);
                    if (!directForm && preview != null)
                        detail += "\n" + T("neutralizer_draw", use.DrawnAcid / 1000.0, use.DrawnBase / 1000.0);
                    hints.Add(detail + "\n" + T(directForm ? "neutralizer_direct" : "neutralizer_rule"));
                }
            }
            SetTextIfChanged(inputDetails, string.Join("\n\n", hints));
            inputGuidanceDirty = false;
        }

        private void SetLabelRule(ProductLabelRule rule)
        {
            labelRules[labelProductId] = rule;
            ProductLabels.SaveRule(machine, labelProductId, rule);
            SetTextIfChanged(labelStatus, "");
            RefreshLabelPreview();
        }

        private void SelectLabelProduct(string productId)
        {
            if (labeling) return;
            labelProductId = productId;
            machine.ModifyTag(LabelProductTag, tag => tag.Enable().SetString(productId));
            RefreshLabelSelection();
            SetTextIfChanged(labelStatus, "");
            RefreshLabelPreview();
            labelTabs.Select(0);
        }

        private void RefreshLabelSelection()
        {
            var rule = labelRules[labelProductId];
            labelOptions.Select(rule.Grade < 0 ? 8 : rule.Grade);
            labelPercentages.Select(Array.IndexOf(ProductLabels.Percentages, rule.Percent));
            SetButtonTextIfChanged(labelProductButton, T("selected_product", MachineUiNames.Product(labelProductId)));
        }

        private GameItem[] OrderedLabelOutput() => output.childItems.OrderBy(item => item.modifiedShape.minY)
            .ThenBy(item => item.modifiedShape.minX).ToArray();

        private void RefreshLabelPreview()
        {
            if (labeling) return;
            var groups = ProductLabels.Plan(OrderedLabelOutput(), labelRules);
            foreach (var group in groups)
            {
                string caption = labelCaptions[group.Rule.Grade < 0 ? 8 : group.Rule.Grade];
                SetTextIfChanged(labelCategorySummaries[group.ProductId], caption + " · " + group.Rule.Percent + "%\n" +
                    T("labels_count", group.Selected.Length, group.Total, group.Total - group.Selected.Length));
            }
            var current = groups.First(group => group.ProductId == labelProductId);
            SetTextIfChanged(labelPreview, T("labels_count", current.Selected.Length, current.Total, current.Total - current.Selected.Length) +
                "\n" + T("labels_order"));
            labelApply.SetDisabled(current.Selected.Length == 0);
            labelApplyAll.SetDisabled(groups.All(group => group.Selected.Length == 0));
            for (int i = 0; i < ProductLabels.Percentages.Length; i++)
                labelPercentages.Get(i).SetDisabled(current.Rule.Grade < 0);
            labelsDirty = false;
        }

        private void SetLabelControlsDisabled(bool disabled)
        {
            labelApply.SetDisabled(disabled);
            labelApplyAll.SetDisabled(disabled);
            labelProductButton.SetDisabled(disabled);
            foreach (var button in labelProductButtons) button.SetDisabled(disabled);
            for (int i = 0; i < labelCaptions.Length; i++) labelOptions.Get(i).SetDisabled(disabled);
            for (int i = 0; i < ProductLabels.Percentages.Length; i++)
                labelPercentages.Get(i).SetDisabled(disabled || labelRules[labelProductId].Grade < 0);
        }

        private void BeginLabeling(bool all)
        {
            if (labeling) return;
            var plan = ProductLabels.Plan(OrderedLabelOutput(), labelRules, all ? null : labelProductId);
            if (plan.All(group => group.Selected.Length == 0)) { RefreshLabelPreview(); return; }
            labeling = true;
            sortButton.SetDisabled(true);
            SetLabelControlsDisabled(true);
            SetTextIfChanged(labelStatus, T("labels_working"));
            EmporiumEntry.Instance.StartCoroutine(LabelProducts(plan));
        }

        private IEnumerator LabelProducts(ProductLabelGroup[] plan)
        {
            yield return null;
            int count = 0;
            var slice = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                foreach (var group in plan)
                foreach (var item in group.Selected)
                {
                    try
                    {
                        // Only products still in this output bin; later output is
                        // excluded. Labeling must not interfere with production.
                        if (ProductLabels.ApplyPlanned(group, item, output.childItems)) count++;
                    }
                    catch (Exception error)
                    {
                        ModEntry.Log.Error("Output labeling failed: " + error);
                        SetTextIfChanged(labelStatus, T("labels_failed", count));
                        yield break;
                    }
                    if (slice.Elapsed.TotalMilliseconds >= 3) { yield return null; slice.Restart(); }
                }
                SetTextIfChanged(labelStatus, T("labels_done", count));
            }
            finally
            {
                labeling = false;
                sortButton.SetDisabled(searching);
                SetLabelControlsDisabled(false);
                RefreshLabelPreview();
                RequestLayout();
            }
        }

        private void SelectProduct(string productId)
        {
            if (Busy) return;
            if (selectedProductId == ChemicalRecipe.Get(productId).Id) { controlTabs.Select(0); return; }
            selectedProductId = ChemicalRecipe.Get(productId).Id;
            machine.ModifyTag(ProductTag, s => s.Enable().SetString(selectedProductId));
            SetButtonTextIfChanged(productButton, T("selected_product", MachineUiNames.Product(selectedProductId)));
            preview = null;
            ResetGradeResults();
            ClearPreviewText();
            controlTabs.Select(0);
            RefreshResidueWarning();
            RefreshInputGuidance();
            SetStatus(tank != null && ChemistryOperations.Total(tank) > 0 ? "residual_included" : "idle");
        }

        private void RefreshAlcoholControls()
        {
            RefreshNormalDryingControls();
            if (directForm)
            {
                RefreshDirectDryingControls();
                alcoholAvailable = true;
                alcoholViewKey = null;
                SetButtonTextIfChanged(alcoholButton, T(directSolvents ? "direct_solvents_on" : "direct_solvents_off"));
                alcoholButton.SetDisabled(Busy);
                alcoholButton.SetDescription(T("direct_solvents_help"));
                for (int i = 0; i < AlcoholBudget.Percentages.Length; i++) alcoholOptions.Get(i).SetDisabled(true);
                SetTextIfChanged(alcoholAvailability, T(directSolvents ? "direct_solvents_on" : "direct_solvents_off") + "\n" + T("direct_solvents_help"));
                return;
            }
            alcoholButton.SetDisabled(false);
            SetButtonTextIfChanged(alcoholButton, T("alcohol_button"));
            alcoholButton.SetDescription(null);
            if (cores.Any(x => x == null) || tank == null) return;
            if (running != null)
            {
                alcoholAvailable = true;
                string activeKey = "running:" + running.plan.alcoholPercent;
                if (alcoholViewKey == activeKey) return;
                alcoholViewKey = activeKey;
                alcoholOptions.Select(Array.IndexOf(AlcoholBudget.Percentages, running.plan.alcoholPercent));
                for (int i = 0; i < AlcoholBudget.Percentages.Length; i++) alcoholOptions.Get(i).SetDisabled(true);
                SetTextIfChanged(alcoholAvailability, T("alcohol_locked"));
                return;
            }
            var budget = AlcoholBudget.Read(Inputs());
            alcoholAvailable = budget.AllowsPlanning(alcoholPercent);
            string key = budget.Full + ":" + budget.Available + ":" + budget.WaterAvailable + ":" + budget.Coffee + ":" + budget.NoExtractionMessage + ":" + alcoholPercent + ":" + Busy + ":" +
                (mashBuffer == null ? -1 : ChemistryOperations.Total(mashBuffer));
            if (key == alcoholViewKey) return;
            alcoholViewKey = key;
            alcoholOptions.Select(Array.IndexOf(AlcoholBudget.Percentages, alcoholPercent));
            var lines = new List<string>();
            if (budget.Applicable)
            {
                lines.Add(T("alcohol_stock", budget.Available / 1000.0));
                foreach (int percent in AlcoholBudget.Percentages)
                    lines.Add(T("alcohol_need", percent, budget.Needed(percent) / 1000.0));
                if (!alcoholAvailable) lines.Add(T("alcohol_unavailable"));
                if (budget.Coffee) lines.Add(T("coffee_water_help"));
            }
            else lines.Add(T(budget.NoExtractionMessage));
            if (mashBuffer != null && ChemistryOperations.Total(mashBuffer) > 0)
                lines.Add(T("buffer_residue", ChemistryOperations.Total(mashBuffer) / 1000.0));
            SetTextIfChanged(alcoholAvailability, string.Join("\n", lines));
            alcoholAvailability.SetColor(alcoholAvailable ? (Color)new Color32(185, 240, 150, 255) : new Color32(255, 170, 155, 255));
            for (int i = 0; i < AlcoholBudget.Percentages.Length; i++)
            {
                int percent = AlcoholBudget.Percentages[i];
                alcoholOptions.Get(i).SetDisabled(Busy || !budget.Applicable || !budget.AllowsPlanning(percent));
                alcoholOptions.Get(i).SetDescription(T("alcohol_need", percent, budget.Needed(percent) / 1000.0) +
                    (budget.WaterFallback(percent) ? "\n" + T("coffee_water_fallback") : ""));
            }
            if (WindowVisible) RequestLayout();
        }

        private void InvalidatePreview()
        {
            inputGuidanceDirty = true;
            if (!internalMove && !Busy) { alcoholViewKey = null; RefreshAlcoholControls(); }
            if (internalMove || Busy || preview == null) return;
            preview = null;
            ClearPreviewText();
            ResetGradeResults();
            SetStatus("changed");
        }

        private void SetStatus(string key, params object[] args)
        {
            lastStatusKey = key;
            lastStatusArgs = args;
            if (!WindowVisible) return;
            string message = T(key, args);
            if (status.GetText() != message)
            {
                status.SetText(message);
                bool problem = key == "failed" || key == "restore_incomplete" || key == "preview_failed" || key == "no_grade" || key == "no_alcohol" ||
                    key == "heater_upgrade_required" || key.StartsWith("missing_") || key == "no_raw" || key == "alcohol_inhibits_recipe" ||
                    key.StartsWith("diag_") || key.StartsWith("runtime_") || key.StartsWith("changed_") ||
                    lastDiagnostics.Issues.Any(x => x.Key == key);
                bool waiting = key == "no_power" || key.StartsWith("output_") || key == "complete_limited" || key == "fallback" || key == "changed" ||
                    key == "residue_confirmation" || key == "direct_solvent_confirmation" || key == "direct_solvent_residue_confirmation" ||
                    key == "residual_included" || key == "cleanup_review" || key == "cleanup_changed";
                status.SetColor(problem ? (Color)new Color32(255, 170, 155, 255) : waiting ?
                    (Color)new Color32(245, 222, 167, 255) : new Color32(190, 223, 208, 255));
            }
            RefreshProgress(key);
            RefreshButtons();
        }

        private static void SetTextIfChanged(MachineryText text, string value)
        {
            if (text.GetText() != value) text.SetText(value);
        }

        private static void SetButtonTextIfChanged(MachineryButton button, string value)
        {
            if (button.text != value) button.SetText(value);
        }

        private void RequestLayout() => layout?.Validate();

        private void ClearResidueConfirmation()
        {
            residueConfirmation.Clear();
            if (confirmResidueButton.handler.gameObject.activeSelf)
            {
                confirmResidueButton.handler.gameObject.SetActive(false);
                RequestLayout();
            }
        }

        private void RefreshCleanupButtons()
        {
            if (!WindowVisible) return;
            bool pending = tankCleanup.Pending;
            bool changed = cleanupButton.handler.gameObject.activeSelf == pending ||
                confirmCleanupButton.handler.gameObject.activeSelf != pending || cancelCleanupButton.handler.gameObject.activeSelf != pending;
            if (changed)
            {
                cleanupButton.handler.gameObject.SetActive(!pending);
                confirmCleanupButton.handler.gameObject.SetActive(pending);
                cancelCleanupButton.handler.gameObject.SetActive(pending);
            }
            cleanupButton.SetDisabled(Busy || !ResidueTanks().Any(t => ChemistryOperations.Total(t) > 0));
            confirmCleanupButton.SetDisabled(Busy || !pending);
            cancelCleanupButton.SetDisabled(!pending);
            if (changed) RequestLayout();
        }

        private void RequestTankCleanup()
        {
            Guard(() =>
            {
                if (Busy || !BindHardware()) return;
                ClearResidueConfirmation();
                if (!tankCleanup.RequestMany(ResidueTanks(), Busy)) { RefreshResidueWarning(); return; }
                RefreshResidueWarning();
                SetStatus("cleanup_review");
            });
        }

        private void ConfirmTankCleanup()
        {
            Guard(() =>
            {
                if (Busy || !BindHardware()) return;
                if (!tankCleanup.ConfirmMany(ResidueTanks(), Busy))
                {
                    RefreshResidueWarning();
                    SetStatus("cleanup_changed");
                    return;
                }
                preview = null;
                previewInputs = null;
                previewFingerprint = null;
                ClearPreviewText();
                ResetGradeResults();
                RefreshResidueWarning();
                SetStatus("cleanup_done");
            });
        }

        private void CancelTankCleanup()
        {
            tankCleanup.Cancel();
            RefreshResidueWarning();
            SetStatus(preview == null ? "idle" : preview.actualGrade < preview.requestedGrade ? "fallback" : "ready");
        }

        private void RefreshResidueWarning()
        {
            if (!WindowVisible) return;
            var currentTank = running == null ? tank : null;
            // Read only component integers until something actually changes.
            // Avoid sorting, recipe scans, localization and string allocation on
            // every idle tick, especially the 360 ticks during nightly catch-up.
            bool dirty = !residueViewValid || residueViewTank != currentTank || residueViewProduct != selectedProductId ||
                residueViewCleanup != tankCleanup.Pending;
            if (residueViewParts == null || residueViewParts.Length != Liquid.Liquids.Count)
            { residueViewParts = new int[Liquid.Liquids.Count]; dirty = true; }
            for (int i = 0; i < residueViewParts.Length; i++)
            {
                int amount = running != null ? 0 : ResidueTanks().Sum(t => ChemistryOperations.Amount(t, Liquid.Liquids[i].identifier));
                if (residueViewParts[i] != amount) { residueViewParts[i] = amount; dirty = true; }
            }
            RefreshCleanupButtons();
            if (!dirty) return;
            residueViewValid = true;
            residueViewTank = currentTank;
            residueViewProduct = selectedProductId;
            residueViewCleanup = tankCleanup.Pending;
            var residue = TankResidue.ReadAll(running == null ? ResidueTanks() : new GameItem[0], selectedProductId);
            bool visible = residue.TotalVolume > 0;
            if (!visible && tankCleanup.Pending)
            {
                tankCleanup.Cancel();
                RefreshButtons();
            }
            bool changed = residueDisplay.handler.gameObject.activeSelf != visible;
            if (changed) residueDisplay.handler.gameObject.SetActive(visible);
            if (visible)
            {
                var color = residue.HasForeignCompounds ? new Color32(255, 170, 155, 255) : new Color32(245, 222, 167, 255);
                SetTextIfChanged(residueHeading, T("residue_total", residue.TotalVolume / 1000.0));
                residueHeading.SetColor(color);
                // Keep the warning compact; foreign compounds take priority over
                // water/solvent even when present in small amounts.
                var shown = residue.Components.Take(2).ToArray();
                string describe(ResidueComponent part) => T("residue_component", Liquid.GetLiquidFromId(part.Id).displayName, part.Volume / 1000.0);
                var lines = shown.Select(describe).ToList();
                if (residue.Components.Length > shown.Length)
                    lines.Add(T("residue_other", (residue.TotalVolume - shown.Sum(part => part.Volume)) / 1000.0));
                SetTextIfChanged(residueDetails, string.Join("\n", lines));
                SetTextIfChanged(residueAdvice, tankCleanup.Pending ? T("cleanup_discard") + "\n" + T("cleanup_irreversible") :
                    T(residue.HasForeignCompounds ? "residue_foreign" : "residue_reuse") + "\n" + T("residue_help"));
                if (tankCleanup.Pending) color = new Color32(255, 170, 155, 255);
                residueAdvice.SetColor(color);
                string composition = string.Join("\n", residue.Components.Select(describe)) + "\n" + T("residue_vessels");
                confirmResidueButton.SetDescription(composition);
                cleanupButton.SetDescription(T("cleanup_discard") + "\n" + composition);
                confirmCleanupButton.SetDescription(T("cleanup_irreversible") + "\n" + composition);
                if (tankCleanup.Pending) SetButtonTextIfChanged(confirmCleanupButton, T("cleanup_confirm", residue.TotalVolume / 1000.0));
            }
            else
            {
                SetTextIfChanged(residueHeading, T("residue_empty"));
                residueHeading.SetColor(MachineryStyle.BODY_TEXT);
                SetTextIfChanged(residueDetails, "");
                SetTextIfChanged(residueAdvice, "");
            }
            RefreshCleanupButtons();
            if (changed) RequestLayout();
        }

        private IEnumerable<GameItem> ResidueTanks() => new[] { tank, mashBuffer, finisherTank }.Where(t => t != null).Distinct();

        private void RefreshProgress(string statusKey = null)
        {
            if (!WindowVisible) return;
            var plan = running?.plan ?? preview;
            bool visible = plan != null;
            bool visibilityChanged = progressDisplay.handler.gameObject.activeSelf != visible;
            if (visibilityChanged) progressDisplay.handler.gameObject.SetActive(visible);
            if (visible)
            {
                int total = ReactionProgress.RemainingSeconds(plan, 0, 0);
                int remaining = running == null ? total : ReactionProgress.RemainingSeconds(plan, running.step, running.elapsed);
                string caption = running == null ? "reaction_estimate" : "reaction_remaining";
                if (plan.actions.Any(a => a.operation == Operation.BoilDry))
                    caption = running == null ? "boiling_estimate" : "boiling_remaining";
                string value = T("seconds_value", remaining);
                var forecast = EvaporationEstimate.Forecast(plan, EvaporationEstimate.Read(null, cores[2]).DailyVolume,
                    running?.step ?? 0, running == null ? null : tank);
                string forecastText = !forecast.Complete ? T("evaporation_forecast_unknown") :
                    !forecast.Days.HasValue ? T("evaporation_zero_rate") : forecast.Days.Value == 0 ?
                    T("evaporation_not_needed") : T("evaporation_batch_days", forecast.Days.Value);
                string details = forecastText + (forecast.Stages > 0 ? "\n" + T("evaporation_forecast_assumption") : "");
                int heatedLots = plan.actions.Count(a => a.operation == Operation.BoilDry);
                if (running == null && heatedLots > 0)
                    details = T("boiling_lots", heatedLots) + "\n" + forecastText;
                if (running == null && forecast.Stages > 0)
                {
                    caption = "processing_estimate";
                    value = forecast.Days.HasValue ? T("processing_preview_value", remaining, forecast.Days.Value) : T("seconds_value", remaining);
                    details = forecast.Days.HasValue ? T("evaporation_forecast_stages", forecast.Stages, forecast.DailyVolume / 1000.0) +
                        "\n" + T("evaporation_forecast_assumption") : forecastText;
                }
                float fraction = total > 0 ? (total - remaining) / (float)total : 0f;
                bool showBar = true;
                Color color = new Color32(166, 231, 207, 255);
                if (running != null && (running.step < plan.actions.Length || DeferredOutput.Ready(running)))
                {
                    var action = running.step < plan.actions.Length ? plan.actions[running.step] : new BatchAction(Operation.Form);
                    if (action.operation == Operation.Dry)
                    {
                        var estimate = EvaporationEstimate.Read(tank, cores[2]);
                        bool powered = cores[2] != null && MachineHelper.CanPower(cores[2]);
                        caption = !estimate.Days.HasValue ? "evaporation_zero_rate" :
                            powered ? "evaporation_eta" : "evaporation_no_power";
                        value = !estimate.Days.HasValue ? T("evaporation_unavailable") :
                            T(powered ? "evaporation_days" : "evaporation_days_after_power", estimate.Days.Value);
                        details = forecastText + "\n" + T("evaporation_stage_volume", estimate.Volume / 1000.0, estimate.DailyVolume / 1000.0);
                        color = new Color32(245, 222, 167, 255);
                        // This is the current drying stage, not total completion
                        // of a recipe with another evaporation later in its plan.
                        showBar = false;
                    }
                    else if (action.operation == Operation.Form)
                    {
                        caption = DeferredOutput.Waiting(running) ? "defer_output_stock" : "bottling_progress";
                        value = T("bottles_progress", running.produced, plan.expectedBottles);
                        fraction = plan.expectedBottles > 0 ? running.produced / (float)plan.expectedBottles : 0f;
                        if (DeferredOutput.Waiting(running))
                        {
                            value = QuantityValue(DeferredOutput.Remaining(running), plan.productId);
                            details = T("defer_output_wait_help");
                            showBar = false;
                        }
                        else if (running.deferOutput) details = T("defer_output_releasing", Math.Max(0, running.releaseTarget - running.produced));
                    }
                    if (running.failed)
                    {
                        caption = "progress_stopped";
                        color = new Color32(255, 170, 155, 255);
                    }
                    else if (statusKey == "no_power" || statusKey == "output_full")
                        color = new Color32(245, 222, 167, 255);
                }
                string captionText = T(caption);
                bool layoutChanged = visibilityChanged || progressCaption.GetText() != captionText ||
                    progressBar.handler.gameObject.activeSelf != showBar;
                SetTextIfChanged(progressCaption, captionText);
                if (progressValue.GetText() != value)
                {
                    if (layout == null) SetTextIfChanged(progressValue, value);
                    else layout.UpdateFixedRegion(progressValueRegion, () => SetTextIfChanged(progressValue, value));
                }
                bool showDetails = details.Length > 0;
                SetTextIfChanged(progressDetails, details);
                if (progressDetails.handler.gameObject.activeSelf != showDetails)
                {
                    progressDetails.handler.gameObject.SetActive(showDetails);
                    visibilityChanged = true;
                }
                progressValue.SetColor(color);
                progressBar.SetColor(color);
                if (progressBar.value != Mathf.Clamp01(fraction)) progressBar.SetValue(fraction);
                if (progressBar.handler.gameObject.activeSelf != showBar)
                {
                    progressBar.handler.gameObject.SetActive(showBar);
                    visibilityChanged = true;
                }
                if (layoutChanged) RequestLayout();
            }
            if (visibilityChanged) RequestLayout();
        }

        private void ResetGradeResults()
        {
            bool changed = false;
            for (int i = 0; i < GradePolicy.Grades.Length; i++)
            {
                var button = grades.Get(i);
                string caption = GradePolicy.Grades[i] + "%";
                if (button.text != caption) { button.SetText(caption); changed = true; }
                button.SetDescription("").SetTextColor(null);
            }
            if (changed) RequestLayout();
        }

        private void DisplayGradeResult(GradeAttempt attempt)
        {
            int index = Array.IndexOf(GradePolicy.Grades, attempt.Grade);
            var outcome = attempt.BestOutcome;
            grades.Get(index).SetText(attempt.Grade + "%").SetDescription(outcome == null ? T("grade_unavailable", attempt.Grade) :
                T("grade_selected", attempt.Grade, outcome.Bottles)).SetTextColor(outcome == null ?
                    (Color)new Color32(255, 205, 140, 255) : new Color32(185, 240, 150, 255));
            RequestLayout();
        }

        private void ClearPreviewText(string message = "preview_hint")
        {
            normalHeatFailure = null;
            normalDryingViewKey = null;
            plannedHighlights?.Clear();
            lastDiagnostics.Clear();
            maximumPreview = null;
            selectedQuantity = 0;
            quantitySearching = false;
            previewFeedReagents = new string[0];
            inputGuidanceDirty = true;
            ClearResidueConfirmation();
            tankCleanup.Cancel();
            RefreshResidueWarning();
            SetTextIfChanged(resultHeading, "");
            resultHeading.handler.gameObject.SetActive(false);
            previewText.handler.gameObject.SetActive(true);
            SetTextIfChanged(previewText, T(directForm && message == "preview_hint" ? "direct_preview_hint" : message));
            previewText.SetColor(MachineryStyle.BODY_TEXT);
            SetTextIfChanged(resultGrade, "—");
            SetTextIfChanged(resultBottles, "—");
            SetTextIfChanged(reagentText, "");
            previewButton.SetDescription(null);
            startButton.SetDescription(null);
            SetTextIfChanged(concentrationAdvice, "");
            if (concentrationAdvice.handler.gameObject.activeSelf) RequestLayout();
            concentrationAdvice.handler.gameObject.SetActive(false);
            SetButtonTextIfChanged(startButton, T("start"));
            RefreshProgress();
            RefreshButtons();
        }
        private void ShowPlanningFailure(string key, PlanningDiagnostics report)
        {
            lastDiagnostics.CopyFrom(report);
            if (lastDiagnostics.Primary == null) lastDiagnostics.Add(key);
            var summaryArgs = lastDiagnostics.Issues.FirstOrDefault(x => x.Key == key)?.SummaryArgs ?? new object[0];
            SetTextIfChanged(previewText, T(key, summaryArgs));
            previewText.SetColor(new Color32(255, 170, 155, 255));
            previewButton.SetDescription(T("diag_alternatives") + "\n\n" + lastDiagnostics.Describe((k, args) => T(k, args)));
            RefreshInputGuidance();
            if ((key == "no_raw" || key == "direct_no_raw") && inputGuidance.MissingReason != null)
            {
                SetTextIfChanged(previewText, InputHint(inputGuidance.MissingReason));
                SetStatus(inputGuidance.MissingReason, string.Join(" / ", inputGuidance.OtherProducts.Select(MachineUiNames.Product)));
            }
            else SetStatus(key, summaryArgs);
        }
        private void ExplainChanged(BatchInputs current)
        {
            var changes = previewState?.Changes(PlanningInputState.Capture(current)) ?? new PlanningDiagnostics();
            if (changes.Primary == null) changes.Add("changed");
            preview = null;
            ClearPreviewText();
            ResetGradeResults();
            ShowPlanningFailure(changes.Primary.Key, changes);
        }

        private void ShowPreview(BatchPlan plan)
        {
            if (!plan.directForm)
            {
                ResetGradeResults();
                foreach (int grade in GradePolicy.Grades.Where(g => g <= plan.requestedGrade && g >= plan.actualGrade))
                {
                    var outcome = grade == plan.actualGrade ? new ProductionOutcome(plan.minimumActive * plan.expectedBottles,
                        ChemicalRecipe.Get(plan.productId).UnitVolume * plan.expectedBottles, plan.expectedBottles, plan.minimumActive,
                        unitVolume: ChemicalRecipe.Get(plan.productId).UnitVolume) : null;
                    DisplayGradeResult(new GradeAttempt(grade, outcome));
                }
            }
            if (running == null && !searching) plannedHighlights?.Show(PlannedInputs.Read(previewInputs, plan));
            // Build source details once per plan, never rerun chemistry while
            // refreshing the countdown. The status card retains its fixed size.
            var heatDetails = new List<string>();
            string lotName = T("boiling_tank");
            int lotNumber = 0;
            foreach (var action in plan.actions)
            {
                if (action.operation == Operation.SwapTanks) lotName = T("boiling_tank");
                bool solid = action.operation == Operation.Blend || action.operation == Operation.Dissolve;
                if (plan.separateDirect && (solid || action.operation == Operation.Pour) && previewInputs != null)
                {
                    var sources = solid ? previewInputs.raw : previewInputs.supplies;
                    lotName = action.source >= 0 && action.source < sources.Length ? sources[action.source].name : null;
                }
                if (action.operation != Operation.BoilDry) continue;
                lotNumber++;
                if (lotNumber <= 8) heatDetails.Add(T("boiling_lot_detail", lotNumber,
                    string.IsNullOrEmpty(lotName) ? T("boiling_feed") : lotName, action.amount));
            }
            if (lotNumber > 8) heatDetails.Add(T("boiling_more_lots", lotNumber - 8));
            if (lotNumber > 0) heatDetails.Add(T("boiling_native_rule"));
            if (!plan.directForm) heatDetails.Add(NormalDryingSummary(plan));
            startButton.SetDescription(string.Join("\n", heatDetails));
            lastDiagnostics.Clear();
            previewFeedReagents = DirectFormInputs.ReagentsInPlan(previewInputs, plan);
            bool fallback = plan.actualGrade < plan.requestedGrade;
            SetTextIfChanged(resultHeading, plan.directForm ? T("direct_title").Replace(" · ", "\n") : T(fallback ? "fallback_title" : "ready_title"));
            resultHeading.handler.gameObject.SetActive(true);
            previewText.handler.gameObject.SetActive(!plan.directForm);
            resultHeading.SetColor(fallback ? (Color)new Color32(255, 225, 138, 255) : new Color32(185, 240, 150, 255));
            SetTextIfChanged(previewText, plan.directForm ? T(plan.separateDirect ? "direct_preview" : "direct_legacy_preview") : T("preview_target", plan.requestedGrade));
            // The selected tier is a threshold, not the measured composition.
            // E.g. choosing 90% must not label an already 99% batch as 90%.
            SetTextIfChanged(resultGrade,
                (plan.minimumActive * 100.0 / ChemicalRecipe.Get(plan.productId).UnitVolume).ToString("0.###") + "%");
            string productId = ChemicalRecipe.Get(plan.productId).Id;
            SetTextIfChanged(resultBottles, T(productId == "pink_alt_injector" ? "injector_value" :
                productId.EndsWith("_pill") ? "pill_value" : "bottle_value", plan.expectedBottles));
            string liquidDetails = plan.directForm ?
                (plan.reagents > 0 ? T("reagents_volume", plan.reagents / 1000.0) + "\n" : "") + T("direct_no_reagents") : T("reagents_volume", plan.reagents / 1000.0) +
                (plan.recoveredVolume > 0 ? "\n" + T("recovered_volume", plan.recoveredVolume / 1000.0) : "") +
                (plan.extractionPortions > 0 && AlcoholBudget.Supports(ChemicalRecipe.Get(plan.productId)) ?
                    "\n" + T(productId == "caffeine_pill" && plan.extractionAlcohol == 0 ? "coffee_water_extraction" : "extraction_summary", plan.alcoholPercent, plan.extractionAlcohol / 1000.0, plan.extractionPortions) : "");
            if (!plan.directForm) liquidDetails += "\n" + NormalDryingSummary(plan);
            if (plan.fineFilterWear > 0 || plan.coarseFilterWear > 0)
                liquidDetails += "\n" + T("filter_wear_preview", plan.fineFilterWear, plan.coarseFilterWear);
            previewButton.SetDescription((plan.directForm ? T(plan.separateDirect ? "direct_preview" : "direct_legacy_preview") : T("selection_hint")) + "\n" + liquidDetails);
            if (plan.requestedBottles > 0)
                previewButton.SetDescription(T("quantity_ready", plan.expectedBottles) + "\n" + liquidDetails + "\n" + T("quantity_help"));
            SetTextIfChanged(reagentText, plan.directForm ? liquidDetails : T("reagents_volume", plan.reagents / 1000.0) +
                (plan.recoveredVolume > 0 ? "\n" + T("recovered_short", plan.recoveredVolume / 1000.0) : "") +
                (plan.extractionPortions > 0 && AlcoholBudget.Supports(ChemicalRecipe.Get(plan.productId)) ?
                    "\n" + T(productId == "caffeine_pill" && plan.extractionAlcohol == 0 ? "coffee_water_extraction" : "extraction_compact", plan.alcoholPercent, plan.extractionAlcohol / 1000.0, plan.extractionPortions) : ""));
            string hint = plan.quantityHint ?? plan.concentrationHint;
            SetTextIfChanged(concentrationAdvice, string.IsNullOrEmpty(hint) ? "" :
                plan.directForm ? T(hint) : T(plan.quantityHint != null ? "partial_stock_limit" : "purity_hint", T(hint)) + "\n" + T(hint + "_action"));
            // Keep the actual drying route / missing heater module visible.
            // The generic solvent warning must not hide the reason for a wait.
            // Solvent use still requires the existing start confirmation.
            if (previewFeedReagents.Length > 0 && string.IsNullOrEmpty(hint))
                SetTextIfChanged(concentrationAdvice, T("direct_solvent_preview"));
            if (plan.outputLimited)
            {
                SetTextIfChanged(resultHeading, T("output_limit_title"));
                resultHeading.SetColor(new Color32(255, 225, 138, 255));
                SetTextIfChanged(reagentText, T("output_limit_detail", plan.expectedBottles));
                previewButton.SetDescription(T("output_limit_ready", plan.expectedBottles) + "\n" + liquidDetails);
            }
            SetButtonTextIfChanged(startButton, T(plan.directForm ? "direct_start" : "start_grade", plan.actualGrade));
            RefreshProgress();
            RefreshButtons();
            RefreshInputGuidance();
        }

        private string QuantityValue(int count, string id) => T(id == "pink_alt_injector" ? "injector_value" :
            id.EndsWith("_pill") ? "pill_value" : "bottle_value", count);

        private void RefreshQuantityControl()
        {
            var active = running?.plan;
            bool waitingOutput = DeferredOutput.Waiting(running);
            int maximum = waitingOutput ? DeferredOutput.Remaining(running) : active != null ? Math.Max(active.availableBottles, active.expectedBottles) : maximumPreview?.expectedBottles ?? 0;
            if (waitingOutput && releaseStep != running.step) { releaseStep = running.step; releaseQuantity = maximum; }
            if (waitingOutput) releaseQuantity = Math.Max(1, Math.Min(maximum, releaseQuantity));
            int count = waitingOutput ? releaseQuantity : active?.expectedBottles ?? selectedQuantity;
            bool visible = !residueConfirmation.Pending;
            if (quantityRegion.handler.gameObject.activeSelf != visible)
            { quantityRegion.handler.gameObject.SetActive(visible); RequestLayout(); }
            string caption = waitingOutput ? T("defer_output_quantity", count, maximum) : maximum <= 0 ? T("quantity_wait") : count == maximum ? T("quantity_all", maximum) : T("quantity_selected", count, maximum);
            bool disabled = waitingOutput ? running.failed : running != null || (searching && !quantitySearching) || maximum <= 0 || residueConfirmation.Pending || tankCleanup.Pending;
            string key = caption + "|" + disabled;
            if (quantityViewKey == key) return;
            quantityViewKey = key;
            Action update = () => { quantityControl.SetHelp(T(waitingOutput ? "defer_output_slider_help" : "quantity_help")); quantityControl.Show(count, maximum, caption, disabled); };
            if (layout == null) update(); else layout.UpdateFixedRegion(quantityRegion, update);
        }

        private void SelectQuantity(int count)
        {
            if (DeferredOutput.Waiting(running))
            {
                releaseQuantity = Math.Max(1, Math.Min(DeferredOutput.Remaining(running), count));
                RefreshButtons();
                return;
            }
            if (running != null || maximumPreview == null || (searching && !quantitySearching) || tankCleanup.Pending) return;
            count = Math.Max(1, Math.Min(maximumPreview.expectedBottles, count));
            if (count == selectedQuantity) return;
            selectedQuantity = count;
            plannedHighlights?.Clear();
            ClearResidueConfirmation();
            preview = null;
            quantitySearching = searching = true;
            int generation = ++searchGeneration;
            RefreshQuantityControl();
            layout.UpdateFixedRegion(yieldMetricRegion, () => SetTextIfChanged(resultBottles, QuantityValue(count, selectedProductId)));
            startButton.SetDisabled(true);
            confirmResidueButton.SetDisabled(true);
            EmporiumEntry.Instance.StartCoroutine(GuardSearch(generation, SearchQuantity(generation, count)));
        }

        private IEnumerator SearchQuantity(int generation, int count)
        {
            // Dragging updates the readout immediately. Replan only after the
            // pointer/wheel settles, and cancel an older search on the next move.
            var delay = System.Diagnostics.Stopwatch.StartNew();
            do { yield return null; if (generation != searchGeneration) yield break; }
            while (delay.Elapsed.TotalMilliseconds < 180);
            var current = Inputs();
            if (maximumPreview == null || Fingerprint(current) != previewFingerprint)
            {
                quantitySearching = searching = false;
                ExplainChanged(current); yield break;
            }
            SetStatus("quantity_calculating", count);
            var planner = new QuantityPlanning(current, maximumPreview, count);
            var work = planner.Search();
            var slice = System.Diagnostics.Stopwatch.StartNew();
            while (generation == searchGeneration)
            {
                bool more;
                long started = ModPerformance.Begin();
                try { more = work.MoveNext(); }
                finally { ModPerformance.Record(2, started, "quantity_plan"); }
                if (!more) break;
                if (slice.Elapsed.TotalMilliseconds >= 3)
                { yield return null; slice.Restart(); }
            }
            if (generation != searchGeneration) yield break;
            quantitySearching = searching = false;
            if (Fingerprint(Inputs()) != previewFingerprint)
            { ExplainChanged(Inputs()); yield break; }
            preview = planner.Result;
            if (preview == null)
            {
                SetTextIfChanged(resultGrade, "—");
                SetTextIfChanged(reagentText, "");
                SetTextIfChanged(resultHeading, T("quantity_retry"));
                ShowPlanningFailure(planner.Error ?? "quantity_unavailable", planner.Diagnostics); RefreshButtons(); yield break;
            }
            previewInputs = current;
            ShowPreview(preview);
            SetStatus(preview.requestedBottles > 0 ? "quantity_ready" : preview.outputLimited ? "output_limit_ready" : "ready", preview.expectedBottles);
        }

        private void Seed()
        {
            if (SaveManager.IsDecoding || machine.IsTag(AssembledTag)) return;
            if (coreSlots.Any(slot => slot.childItem != null))
                throw new InvalidOperationException("Uninitialized machine already contains equipment; existing items preserved.");
            bool wasOwned = GeneralHelper.IsItemOwned(machine);
            var created = new List<GameItem>();
            internalMove = true;
            try
            {
                // The shop may already have marked the shell as for sale. Native
                // ownership checks require its owned components to enter an owned
                // parent; restore the shell's actual ownership in finally.
                GeneralHelper.SetItemOwned(machine, true);
                for (int i = 0; i < 4; i++)
                {
                    var core = ModHelper.SetItemOwnerShip(DirectoryMaster.Item(coreIds[i]), true);
                    created.Add(core);
                    if (GraphUtils.TryAcceptAll(coreSlots[i], core) != 0)
                        throw new InvalidOperationException("Core installation failed: " + coreIds[i]);
                }
                var nativeTankSlot = (GameSlotInventory)MachineHelper.GetBodyStorage(coreSlots[2].childItem, 2);
                var emptyTank = ModHelper.SetItemOwnerShip(DirectoryMaster.Item("water_tank"), true);
                created.Add(emptyTank);
                ChemicalHelper.RemoveAllLiquid(emptyTank);
                if (GraphUtils.TryAcceptAll(nativeTankSlot, emptyTank) != 0) throw new InvalidOperationException("Tank installation failed.");
                var collector = ModHelper.SetItemOwnerShip(DirectoryMaster.Item("water_tank"), true);
                created.Add(collector);
                ChemicalHelper.RemoveAllLiquid(collector);
                if (GraphUtils.TryAcceptAll(MachineHelper.GetBodyStorage(coreSlots[2].childItem, 3), collector) != 0)
                    throw new InvalidOperationException("Collector installation failed.");
                var battery = ModHelper.SetItemOwnerShip(PreBuiltItemHelper.EnergyCredit(100), true);
                created.Add(battery);
                if (GraphUtils.TryAcceptAll(MachineHelper.GetBatterySlot(coreSlots[2].childItem), battery) != 0)
                    throw new InvalidOperationException("Battery installation failed.");
                machine.EnableTag(AssembledTag);
            }
            catch
            {
                // These are newly created defaults only, never player-supplied items.
                for (int i = created.Count - 1; i >= 0; i--) created[i].Destroy();
                throw;
            }
            finally
            {
                GeneralHelper.SetItemOwned(machine, wasOwned);
                internalMove = false;
            }
            Restore();
        }

        internal static void RestoreAll()
        {
            var live = instances.Live(c =>
            {
                if (!(c.machine is GameItemElement e) || !e.IsDestroyed()) return true;
                foreach (var gate in c.hopperGates) gate.Dispose();
                c.plannedHighlights?.Clear();
                return false;
            });
            foreach (var controller in live) controller.Guard(controller.Restore);
        }

        private void Restore()
        {
            plannedHighlights?.Clear();
            blockedOutputShape = null;
            foreach (var gate in hopperGates) gate.Clear();
            machine.unitValue = ShellValue;
            // Price every present stage even if another stage is missing and
            // the machine cannot bind its processing callbacks yet.
            for (int i = 0; i < coreSlots.Length; i++)
                if (coreSlots[i].childItem is GameItem core && core.identifier == coreIds[i])
                    HardwareBinding.RestoreValue(core, coreValues[i]);
            if (!machine.IsTag(AssembledTag))
            {
                Seed();
                return;
            }
            string savedProduct = machine.GetTagReadonly(ProductTag).GetString();
            selectedProductId = ChemicalRecipe.Ids.Contains(savedProduct) ? savedProduct : ChemicalRecipe.DefaultId;
            int savedGrade = machine.GetTagReadonly(GradeTag).GetInt();
            requestedGrade = GradePolicy.Grades.Contains(savedGrade) ? savedGrade : 99;
            grades.Select(Array.IndexOf(GradePolicy.Grades, requestedGrade));
            alcoholPercent = AlcoholBudget.Percent(machine.GetTagReadonly(AlcoholTag).GetInt());
            directForm = machine.GetTagReadonly(DirectTag).GetInt() == 1;
            directSolvents = machine.GetTagReadonly(DirectSolventsTag).GetInt() == 1;
            directDrying = DirectDryingPolicy.Preference(machine.GetTagReadonly(DirectDryingPolicy.PreferenceTag).GetInt());
            normalDrying = DirectDryingPolicy.Preference(machine.GetTagReadonly(NormalDrying.PreferenceTag).GetInt());
            deferOutput = machine.GetTagReadonly(DeferredOutput.PreferenceTag).GetInt() == 1;
            releaseStep = -1;
            labelRules = ProductLabels.LoadRules(machine);
            string savedLabelProduct = machine.GetTagReadonly(LabelProductTag).GetString();
            labelProductId = ChemicalRecipe.Ids.Contains(savedLabelProduct) ? savedLabelProduct : ChemicalRecipe.DefaultId;
            RefreshInterfaceNames();
            RefreshLabelSelection();
            labelsDirty = true;
            // UI settings and captions can recover even with a stage removed.
            EnsureFinisherTank();
            if (!BindHardware()) return;
            running = BatchPersistence.Load(machine, out string restoreError);
            if (restoreError != null) ModEntry.Log.Warning("Batch save could not be resumed; materials kept. Recalculate from the remaining contents. " + restoreError);
            formingBatch = null;
            reservedInputs.Clear();
            if (running != null) reservedInputs.Restore(ExecutionSources(), running.plan.separateDirect);
            restored = true;
            preparedHeatStep = -1;
            ClearResidueConfirmation();
            tankCleanup.Cancel();
            if (running != null) selectedProductId = ChemicalRecipe.Get(running.plan.productId).Id;
            if (running != null) alcoholPercent = AlcoholBudget.Percent(running.plan.alcoholPercent);
            if (running != null) { directForm = running.plan.directForm; if (directForm) directSolvents = running.plan.directSolvents; }
            if (running?.plan.directForm == true && running.plan.directDrying != DirectDryingMethod.LegacyAutomatic)
                directDrying = running.plan.directDrying;
            if (running?.plan.directForm == false) normalDrying = running.plan.normalDrying;
            RefreshMode();
            alcoholViewKey = null;
            SetButtonTextIfChanged(productButton, T("selected_product", MachineUiNames.Product(selectedProductId)));
            MigrateLegacyInputs();
            if (running != null)
            {
                ShowPreview(running.plan);
                SetStatus(running.failed ? running.failureKey ?? "failed" : DeferredOutput.Waiting(running) ? "defer_output_ready" : "resumed");
            }
            else if (machine.IsTag(BatchPersistence.RecoveryTag))
            {
                preview = null;
                string reason = machine.GetTagReadonly(BatchPersistence.RecoveryReasonTag).GetString();
                if (string.IsNullOrEmpty(reason)) reason = "restore_incomplete";
                ClearPreviewText(reason);
                SetStatus(reason);
            }
            RefreshResidueWarning();
        }

        private void MigrateLegacyInputs()
        {
            // A running legacy plan addresses source items by their saved tags.
            // Moving/stacking them before completion can change those identities.
            if (!Busy)
            {
                bool previousMove = internalMove;
                internalMove = true;
                try { foreach (var item in supplies.childItems.ToArray()) GraphUtils.TryAcceptAll(raw, item); }
                finally { internalMove = previousMove; }
            }
            legacyInputsPage.handler.gameObject.SetActive(supplies.childItems.Count > 0);
            RequestLayout();
        }

        private bool BindHardware()
        {
            for (int i = 0; i < 4; i++)
            {
                var core = coreSlots[i].childItem;
                if (core == null || core.identifier != coreIds[i]) { SetStatus("missing_core_" + coreIds[i]); return false; }
                if (cores[i] != core)
                {
                    coreBindings[i]?.Dispose();
                    cores[i] = core;
                    int moduleIndex = i == 2 ? 1 : 0;
                    var modules = (GameInventory)MachineHelper.GetBodyStorage(core, moduleIndex);
                    coreBindings[i] = new HardwareBinding(core, modules, () => Busy, i == 2, coreValues[i],
                        () => searching, () => Guard(HardwareModulesChanged));
                    if (i == 2)
                    {
                        evaporate = coreBindings[i].Cycle;
                        coreBindings[i].BindBoost(() => machine.IsTag(MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG),
                            () => TimedGuard(Cycle, 1, "turbo_boost"));
                    }
                }
                coreBindings[i]?.Refresh();
            }
            BindRecoverySlot((GameSlotInventory)MachineHelper.GetBodyStorage(cores[2], 3));
            BindMashBuffer((GameSlotInventory)MachineHelper.GetBodyStorage(cores[0], 2));
            BindFinisherTank((GameSlotInventory)MachineHelper.GetBodyStorage(cores[3], 1));
            var nextTankSlot = (GameSlotInventory)MachineHelper.GetBodyStorage(cores[2], 2);
            if (tankSlot != nextTankSlot)
            {
                if (tankSlot != null) tankSlot.onSlotRemoveItemEndFunc -= tankRemoved;
                tankLock?.Dispose();
                tankSlot = nextTankSlot;
                tankLock = new InventoryLock(tankSlot, () => Busy);
                tankRemoved = (item, inv, slot) => { if (item == tank) ReleaseTank(); };
                tankSlot.onSlotRemoveItemEndFunc += tankRemoved;
            }
            if (tankSlot.childItem != tank)
            {
                ReleaseTank();
                tank = tankSlot.childItem;
                if (tank != null)
                {
                    tankTick = tank.onOneSecondItemFunc;
                    tank.onOneSecondItemFunc = null;
                    tankInteraction = new LiquidSourceBinding(tank, () => true, () => Busy);
                }
            }
            if (tank == null) { SetStatus("missing_tank"); return false; }
            if (finisherTank == null && (running == null || running.useFinisherTank)) { SetStatus("missing_finisher_tank"); return false; }
            return true;
        }

        private void EnsureFinisherTank()
        {
            if (SaveManager.IsDecoding || machine.IsTag(FinisherSeedTag)) return;
            var core = coreSlots[3].childItem;
            if (core == null || core.identifier != coreIds[3]) return;
            var slot = (GameSlotInventory)MachineHelper.GetBodyStorage(core, 1);
            if (slot.childItem == null)
            {
                var created = ModHelper.SetItemOwnerShip(DirectoryMaster.Item("water_tank"), true);
                ChemicalHelper.RemoveAllLiquid(created);
                bool owned = GeneralHelper.IsItemOwned(machine);
                try
                {
                    GeneralHelper.SetItemOwned(machine, true);
                    if (GraphUtils.TryAcceptAll(slot, created) != 0) { created.Destroy(); throw new MachineFault("missing_finisher_tank"); }
                }
                finally { GeneralHelper.SetItemOwned(machine, owned); }
            }
            machine.EnableTag(FinisherSeedTag);
        }

        private void BindFinisherTank(GameSlotInventory slot)
        {
            if (finisherSlot != slot)
            {
                if (finisherSlot != null)
                {
                    finisherSlot.onSlotAddItemEndFunc -= finisherChanged;
                    finisherSlot.onSlotRemoveItemEndFunc -= finisherChanged;
                }
                finisherSlot = slot;
                finisherChanged = (item, inv, marker) => { BindFinisherTank(slot); InvalidatePreview(); };
                slot.onSlotAddItemEndFunc += finisherChanged;
                slot.onSlotRemoveItemEndFunc += finisherChanged;
            }
            if (finisherTank == slot.childItem) return;
            finisherBinding?.Dispose();
            finisherTank = slot.childItem;
            // Finished lots must not react together while waiting for output.
            // The controlled vessel stays real and inspectable in the native UI.
            finisherBinding = finisherTank == null ? null : new LiquidSourceBinding(finisherTank,
                () => Busy || tankCleanup.Holds(finisherTank), () => Busy);
            residueViewValid = false;
        }

        private void HardwareModulesChanged()
        {
            if (!restored || SaveManager.IsDecoding) return;
            // Idle previews must be recalculated, but a running plan retains its
            // original loss/heat settings. Evaporation reads live native effects.
            InvalidatePreview();
            RefreshHardwareHelp();
            RefreshProgress();
            RefreshTurbo();
        }

        private void RefreshHardwareHelp()
        {
            if (hardwareHelp == null) return;
            string text = T(searching ? "hardware_calculating" : running != null ? "hardware_running" : "hardware_help");
            if (running != null)
            {
                var pending = new List<string>();
                if (cores[0] != null && MachineBlender.GetLoss(cores[0]) != running.blenderLoss) pending.Add(T("blender"));
                if (cores[1] != null && MachineBlender.GetUpgradeCount(cores[1]) != running.heaterUpgrades) pending.Add(T("heater"));
                if (cores[3] != null && MachineChemFinisher.GetLoss(cores[3]) != running.finisherLoss) pending.Add(T("finisher"));
                if (pending.Count > 0) text += "\n" + T("hardware_pending", string.Join(" / ", pending));
            }
            SetTextIfChanged(hardwareHelp, text);
        }

        private void BindMashBuffer(GameSlotInventory slot)
        {
            // One-time empty buffer for old/new shells. Never overwrite a player
            // container or replenish one that was subsequently removed.
            if (!SaveManager.IsDecoding && !machine.IsTag(BufferTag))
            {
                if (slot.childItem == null)
                {
                    var buffer = ModHelper.SetItemOwnerShip(DirectoryMaster.Item("water_tank"), true);
                    ChemicalHelper.RemoveAllLiquid(buffer);
                    bool owned = GeneralHelper.IsItemOwned(machine);
                    try
                    {
                        GeneralHelper.SetItemOwned(machine, true);
                        if (GraphUtils.TryAcceptAll(slot, buffer) != 0) { buffer.Destroy(); throw new InvalidOperationException("Mash buffer installation failed."); }
                    }
                    finally { GeneralHelper.SetItemOwned(machine, owned); }
                }
                machine.EnableTag(BufferTag);
            }
            if (bufferSlot != slot)
            {
                ReleaseMashBuffer();
                bufferSlot = slot;
                bufferLock = new InventoryLock(slot, () => Busy);
                bufferChanged = (item, inv, marker) => { BindMashBuffer(slot); InvalidatePreview(); };
                slot.onSlotAddItemEndFunc += bufferChanged;
                slot.onSlotRemoveItemEndFunc += bufferChanged;
            }
            if (mashBuffer != slot.childItem)
            {
                bufferBinding?.Dispose();
                mashBuffer = slot.childItem;
                // The controller advances chemistry once alongside the reactor,
                // including while idle. Native callbacks must not tick it twice.
                bufferBinding = mashBuffer == null ? null : new LiquidSourceBinding(mashBuffer, () => true, () => Busy);
            }
        }

        private void ReleaseMashBuffer()
        {
            if (bufferSlot != null)
            {
                bufferSlot.onSlotAddItemEndFunc -= bufferChanged;
                bufferSlot.onSlotRemoveItemEndFunc -= bufferChanged;
            }
            bufferBinding?.Dispose(); bufferLock?.Dispose();
            bufferBinding = null; bufferLock = null; bufferSlot = null; mashBuffer = null; bufferChanged = null;
        }

        private void BindRecoverySlot(GameSlotInventory slot)
        {
            if (recoverySlot != slot)
            {
                ReleaseRecoverySlot();
                recoverySlot = slot;
                // The collector stays installed during every active batch, even
                // when this plan does not draw recovered solvent from it.
                recoveryLock = new InventoryLock(slot, () => Busy);
                recoveryChanged = (item, inv, marker) => { BindRecoveryContainer(); InvalidatePreview(); };
                slot.onSlotAddItemEndFunc += recoveryChanged;
                slot.onSlotRemoveItemEndFunc += recoveryChanged;
            }
            BindRecoveryContainer();
        }

        private void BindRecoveryContainer()
        {
            var next = recoverySlot?.childItem;
            if (next == recoveryContainer) return;
            recoveryBinding?.Dispose();
            recoveryContainer = next;
            recoveryBinding = next == null ? null : new LiquidSourceBinding(next, () => RecoveryBusy);
        }

        private void ReleaseRecoverySlot()
        {
            if (recoverySlot != null)
            {
                recoverySlot.onSlotAddItemEndFunc -= recoveryChanged;
                recoverySlot.onSlotRemoveItemEndFunc -= recoveryChanged;
            }
            recoveryBinding?.Dispose();
            recoveryLock?.Dispose();
            recoveryBinding = null;
            recoveryLock = null;
            recoverySlot = null;
            recoveryContainer = null;
            recoveryChanged = null;
        }

        private void RestoreDetachedCore(GameItem core, int index)
        {
            coreBindings[index]?.Dispose();
            coreBindings[index] = null;
            if (index == 0) ReleaseMashBuffer();
            if (index == 2)
            {
                ReleaseRecoverySlot();
                ReleaseTank();
                if (tankSlot != null) tankSlot.onSlotRemoveItemEndFunc -= tankRemoved;
                tankLock?.Dispose();
                tankLock = null;
                tankSlot = null;
                evaporate = null;
            }
            // Removing hardware invalidates the preview; reinsertion rebinds it.
            cores[index] = null;
            preview = null;
            ClearPreviewText();
            ResetGradeResults();
        }

        private void ReleaseTank()
        {
            tankInteraction?.Dispose();
            tankInteraction = null;
            if (tank != null) tank.onOneSecondItemFunc += tankTick;
            tank = null;
            tankTick = null;
            ClearResidueConfirmation();
            tankCleanup.Cancel();
            RefreshResidueWarning();
        }

        private IEnumerable<GameItem> AllInputs() => raw.childItems.Concat(supplies.childItems);
        private IEnumerable<GameItem> ExecutionSources() => RecoveredSolvent.Sources(AllInputs(),
            running != null && running.usesRecoveredSolvent ? recoveryContainer : null);
        private static bool IsSolid(GameItem item) => !item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) && !item.IsTag("WATER_FILTER");

        private BatchInputs Inputs()
        {
            int recoveredSource = -1;
            var stock = directForm ? AllInputs().Where(item => DirectFormInputs.CanUseLiquid(item, directSolvents)).Distinct().ToArray() :
                RecoveredSolvent.Supplies(AllInputs(), recoveryContainer, tank, out recoveredSource);
            return new BatchInputs
            {
                productId = selectedProductId, directForm = directForm, separateDirect = directForm, directDrying = directForm ? directDrying : DirectDryingMethod.LegacyAutomatic,
                normalDrying = normalDrying,
                directSolvents = directSolvents, maxProducts = RemainingOutputCapacity(), finisher = finisherTank, deferOutput = deferOutput,
                alcoholPercent = alcoholPercent, mashBuffer = mashBuffer,
                raw = AllInputs().Where(item => InventoryRules.Solid(item) && (directForm ?
                    DirectFormInputs.CanUse(item) : ChemicalRecipe.Get(selectedProductId).FeedstockAmount(item) > 0)).ToArray(),
                supplies = stock, recoveredSource = recoveredSource, chamber = FinishingStorage.PreviewStart(tank, finisherTank),
                blenderLoss = MachineBlender.GetLoss(cores[0]), heaterUpgrades = MachineBlender.GetUpgradeCount(cores[1]),
                finisherLoss = MachineChemFinisher.GetLoss(cores[3]), requestedGrade = requestedGrade
            };
        }

        private int RemainingOutputCapacity()
        {
            if (!productShapes.TryGetValue(selectedProductId, out var shape))
            {
                var product = DirectoryMaster.Item(selectedProductId);
                try { productShapes[selectedProductId] = shape = product.modifiedShape.Clone(); }
                finally { product.Destroy(); }
            }
            return output.Remaining(selectedProductId, shape);
        }

        private static string Fingerprint(BatchInputs input)
        {
            return PlanningInputState.Capture(input).Combined;
        }

        private void BeginPreview()
        {
            Guard(() =>
            {
                if (!Busy) RefreshInputGuidance();
                if (Busy || !BindHardware()) return;
                RefreshAlcoholControls();
                if (!alcoholAvailable)
                {
                    var budget = AlcoholBudget.Read(Inputs());
                    var report = new PlanningDiagnostics();
                    report.Add("alcohol_unavailable", 0, "diag_budget_detail", budget.Needed(alcoholPercent) / 1000.0, budget.Available / 1000.0);
                    preview = null; previewInputs = null; maximumPreview = null; selectedQuantity = 0;
                    ResetGradeResults();
                    ClearPreviewText(); ShowPlanningFailure("alcohol_unavailable", report); return;
                }
                RefreshResidueWarning();
                preview = null;
                previewInputs = Inputs();
                previewState = PlanningInputState.Capture(previewInputs);
                previewFingerprint = previewState.Combined;
                searching = true;
                RefreshInputGuidance();
                foreach (var binding in coreBindings) binding?.Refresh();
                int generation = ++searchGeneration;
                ClearPreviewText("calculating");
                ResetGradeResults();
                SetStatus("calculating");
                EmporiumEntry.Instance.StartCoroutine(GuardSearch(generation, Search(generation, new BatchPlanner(previewInputs))));
            });
        }

        private IEnumerator GuardSearch(int generation, IEnumerator work)
        {
            // A search also reads inventory and refreshes the UI between planner
            // steps. Guard the entire coroutine so those failures cannot leave
            // the machine permanently marked as calculating.
            try
            {
                while (generation == searchGeneration)
                {
                    bool more; object next;
                    try { more = work.MoveNext(); next = more ? work.Current : null; }
                    catch (Exception error)
                    {
                        if (generation == searchGeneration)
                        { quantitySearching = searching = false; Fail(error); }
                        yield break;
                    }
                    if (!more) yield break;
                    yield return next;
                }
            }
            finally
            {
                try { (work as IDisposable)?.Dispose(); }
                finally
                {
                    // A superseded slider search must not unlock its successor.
                    if (generation == searchGeneration) quantitySearching = searching = false;
                }
            }
        }

        private IEnumerator Search(int generation, BatchPlanner planner)
        {
            // StartCoroutine runs synchronously until the first yield. Let the
            // click and calculating-state repaint finish before doing chemistry.
            yield return null;
            var work = planner.Search();
            int displayedGrades = 0;
            var slice = System.Diagnostics.Stopwatch.StartNew();
            while (generation == searchGeneration)
            {
                bool more;
                long started = ModPerformance.Begin();
                try
                {
                    more = work.MoveNext();
                }
                finally { ModPerformance.Record(2, started); }
                if (!more) break;
                // Yield between small numerical steps, batching cheap steps into
                // a short frame slice rather than adding one frame per candidate.
                if (slice.Elapsed.TotalMilliseconds >= 3)
                {
                    UpdateSearchDisplay(planner, ref displayedGrades);
                    yield return null;
                    slice.Restart();
                }
            }
            if (generation != searchGeneration) yield break;
            UpdateSearchDisplay(planner, ref displayedGrades);
            searching = false;
            foreach (var binding in coreBindings) binding?.Refresh();
            preview = planner.Result;
            if (preview == null)
            {
                ClearPreviewText("preview_failed");
                if (!directForm && normalDrying == DirectDryingMethod.Heat) normalHeatFailure = planner.Error;
                ShowPlanningFailure(planner.Error ?? "preview_failed", planner.Diagnostics);
                yield break;
            }
            if (Fingerprint(Inputs()) != previewFingerprint) { ExplainChanged(Inputs()); yield break; }
            var stability = new PlanningDiagnostics();
            if (!SourceStability.Check(Inputs(), preview, stability))
            {
                preview = null;
                ClearPreviewText("preview_failed");
                ShowPlanningFailure("diag_stock_reacting", stability);
                yield break;
            }
            maximumPreview = preview.Copy();
            selectedQuantity = preview.expectedBottles;
            ShowPreview(preview);
            SetStatus(preview.outputLimited ? "output_limit_ready" : preview.actualGrade == preview.requestedGrade ? "ready" : "fallback", preview.expectedBottles);
        }

        private void UpdateSearchDisplay(BatchPlanner planner, ref int displayedGrades)
        {
            int lastGrade = 0;
            while (displayedGrades < planner.GradeAttempts.Count)
            {
                var attempt = planner.GradeAttempts[displayedGrades++];
                DisplayGradeResult(attempt);
                lastGrade = attempt.Grade;
            }
            if (lastGrade > 0) SetStatus("checked_grade", lastGrade);
            else SetStatus(planner.Stage);
        }

        private void Start()
        {
            if (DeferredOutput.Waiting(running)) ReleaseProducts(false);
            else if (DeferredOutput.Protected(running) && running.failed)
            { running.failed = false; running.failureKey = null; Save(); AdvanceImmediate(); }
            else StartBatch(false);
        }

        private void ReleaseProducts(bool all)
        {
            if (!DeferredOutput.Waiting(running) || !BindHardware()) return;
            int count = all ? DeferredOutput.Remaining(running) : releaseQuantity;
            if (!DeferredOutput.Release(running, count)) return;
            running.failed = false; running.failureKey = null;
            Save();
            AdvanceImmediate();
        }

        private void StartBatch(bool confirmResidue)
        {
            Guard(() =>
            {
                if (Busy || tankCleanup.Pending || preview == null || !BindHardware()) return;
                var current = Inputs();
                string currentFingerprint = Fingerprint(current);
                if (currentFingerprint != previewFingerprint) { ExplainChanged(current); RefreshResidueWarning(); return; }
                RefreshResidueWarning();
                bool foreignResidue = TankResidue.Read(tank, selectedProductId).HasForeignCompounds ||
                    (mashBuffer != null && TankResidue.Read(mashBuffer, selectedProductId).HasForeignCompounds) ||
                    TankResidue.Read(finisherTank, selectedProductId).HasForeignCompounds;
                var feedReagents = DirectFormInputs.ReagentsInPlan(current, preview);
                if (foreignResidue || feedReagents.Length > 0)
                {
                    if (!confirmResidue || !residueConfirmation.Consume(currentFingerprint, tank))
                    {
                        residueConfirmation.Request(currentFingerprint, tank);
                        confirmResidueButton.SetText(T(feedReagents.Length > 0 ?
                            (foreignResidue ? "direct_solvent_residue_confirm" : "direct_solvent_confirm") :
                            preview.directForm ? "direct_residue_confirm" : "residue_confirm", preview.actualGrade));
                        if (feedReagents.Length > 0)
                            confirmResidueButton.SetDescription(T("direct_solvent_detail",
                                string.Join("、", feedReagents.Select(id => Liquid.GetLiquidFromId(id).displayName))));
                        confirmResidueButton.handler.gameObject.SetActive(true);
                        SetStatus(feedReagents.Length > 0 ?
                            (foreignResidue ? "direct_solvent_residue_confirmation" : "direct_solvent_confirmation") : "residue_confirmation");
                        RequestLayout();
                        return; // No source tags, reactions or materials touched.
                    }
                }
                ClearResidueConfirmation();
                var validation = new PlanningDiagnostics();
                preview = BatchPlanner.Revalidate(current, preview, validation);
                if (preview == null) { ClearPreviewText(); ResetGradeResults(); ShowPlanningFailure(validation.Primary?.Key ?? "runtime_recheck", validation); return; }
                // Match the preview's existing-residue merge before the first
                // process action; all final stock returns to the finisher.
                FinishingStorage.MoveRemainder(finisherTank, tank);
                ShowPreview(preview);
                previewInputs = current;
                bool usesRecovery = preview.recoveredVolume > 0;
                foreach (var item in RecoveredSolvent.Sources(AllInputs(), recoveryContainer)) item.DisableTag(SourceTag);
                var selectedRaw = new HashSet<int>(preview.actions.Where(a => a.operation == Operation.Blend || a.operation == Operation.Dissolve).Select(a => a.source));
                var selectedSupplies = new HashSet<int>(preview.actions.Where(a => a.operation == Operation.Pour || a.operation == Operation.Filter).Select(a => a.source));
                selectedSupplies.UnionWith(preview.actions.Where(a => a.operation == Operation.Filter).SelectMany(a => a.FilterSources));
                for (int i = 0; i < previewInputs.raw.Length; i++)
                    if ((!preview.directForm && preview.requestedBottles == 0) || selectedRaw.Contains(i)) SetSource(previewInputs.raw[i], i);
                var batchSupplies = previewInputs.supplies.Where((item, index) =>
                    (index != previewInputs.recoveredSource || usesRecovery) && ((!preview.directForm && preview.requestedBottles == 0) || selectedSupplies.Contains(index))).ToArray();
                for (int i = 0; i < previewInputs.supplies.Length; i++)
                    if ((i != previewInputs.recoveredSource || usesRecovery) && ((!preview.directForm && preview.requestedBottles == 0) || selectedSupplies.Contains(i))) SetSource(previewInputs.supplies[i], i);
                reservedInputs.Capture(previewInputs.raw.Where((item, index) => (!preview.directForm && preview.requestedBottles == 0) || selectedRaw.Contains(index)).Concat(batchSupplies), preview.separateDirect);
                plannedHighlights?.Clear();
                running = new RunningBatch { schema = 2, useFinisherTank = true, plan = preview, blenderLoss = previewInputs.blenderLoss,
                    heaterUpgrades = previewInputs.heaterUpgrades, finisherLoss = previewInputs.finisherLoss, usesRecoveredSolvent = usesRecovery,
                    deferOutput = deferOutput };
                releaseStep = -1;
                RefreshInputGuidance();
                blockedOutputShape = null;
                preview = null;
                preparedHeatStep = -1;
                RefreshResidueWarning();
                Save();
                AdvanceImmediate();
            });
        }

        private static void SetSource(GameItem item, int index) => item.ModifyTag(SourceTag, s => s.Enable().SetInt(index));
        private GameItem Source(bool solid, int index) => ExecutionSources().FirstOrDefault(i => reservedInputs.Contains(i) && IsSolid(i) == solid &&
            i.IsTag(SourceTag) && i.GetTagReadonly(SourceTag).GetInt() == index) ?? throw new MachineFault("runtime_source_missing");

        private void Tick()
        {
            if (!restored) Restore();
            // Native Hide()/close-all bypasses the title-bar close callback.
            if (tankCleanup.Pending && !WindowVisible) CancelTankCleanup();
            // Coalesce inventory notifications; never scan supplies per frame or
            // run another chemistry search just to show a hint.
            if (inputGuidanceDirty && WindowVisible && !Busy) RefreshInputGuidance();
            ScheduleWasteCollection();
            if (labelsDirty && WindowVisible && controlTabs.selected == 3) RefreshLabelPreview();
            if (!BindHardware()) return;
            if (running == null || running.failed)
            {
                if (DeferredOutput.Protected(running)) return;
                tankCleanup.TickIdle(mashBuffer);
                tankCleanup.TickIdle(tank);
                RefreshResidueWarning();
                if (WindowVisible) { RefreshAlcoholControls(); previewButton.SetDisabled(Busy); }
                return;
            }
            AdvanceImmediate();
            if (running == null || running.failed) return;
            if (DeferredOutput.Ready(running)) return;
            var action = running.plan.actions[running.step];
            // Finished stock waits in the native vessel. No extra heating,
            // chemistry or evaporation ticks while awaiting/dispensing output.
            if (running.deferOutput && action.operation == Operation.Form) return;
            if (!running.plan.separateDirect && mashBuffer != null) ChemistryOperations.ReactionTick(mashBuffer);
            if (ReactionProgress.IsHeated(action.operation))
            {
                int desired = action.targetTemperature > 0 ? action.targetTemperature : 45;
                int target = ChemistryOperations.ExtractionTargetTemperature(running.heaterUpgrades, desired);
                cores[1].ModifyTag(MachineHeatingPlate.CURRENT_TARGET_TEMP, s => s.Enable().SetInt(target));
                if (ChemistryOperations.ApplyBatchHeating(tank, running.heaterUpgrades, desired))
                    cores[1].EnableTag("HEATING_TAG", skipValidate: true);
            }
            if (!running.plan.separateDirect || ReactionProgress.IsHeated(action.operation)) ChemistryOperations.ReactionTick(tank);
            if (ReactionProgress.IsTimed(action.operation))
            {
                running.elapsed++;
                if (running.elapsed >= action.amount) { running.step++; running.elapsed = 0; }
                Save();
                AdvanceImmediate();
            }
        }

        private void Cycle()
        {
            if (!restored) Restore();
            if (!BindHardware() || running == null || running.failed) return;
            // Vanilla advances 360 seconds at night. Turbo uses this same callback,
            // so only actual overnight processing gets the extra reaction time.
            if (CurrentConverterHelper.isNightCycle) CompleteNightReactions();
            AdvanceImmediate();
            if (running == null || running.failed || running.step >= running.plan.actions.Length || running.plan.actions[running.step].operation != Operation.Dry) return;
            evaporate?.Invoke(cores[2], cores[2].parentInventory, null);
            AdvanceImmediate();
            if (CurrentConverterHelper.isNightCycle) CompleteNightReactions();
            RefreshTurbo();
        }

        private void CompleteNightReactions()
        {
            while (running != null && !running.failed)
            {
                AdvanceImmediate();
                if (!NightReactions.CanAdvance(running)) return;
                NightReactions.CompleteStep(running, tank, mashBuffer);
                Save();
            }
        }

        private void RefreshTurbo()
        {
            bool ready = running != null && !running.failed && running.step < running.plan.actions.Length &&
                running.plan.actions[running.step].operation == Operation.Dry && tank != null &&
                ChemistryOperations.VolatileVolume(tank) > 0 && cores[2] != null && MachineHelper.CanPower(cores[2]);
            const string tag = MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG;
            if (machine.IsTag(tag) != ready)
            {
                if (ready) machine.EnableTag(tag, skipValidate: true);
                else machine.DisableTag(tag, skipValidate: true);
            }
            coreBindings[2]?.Refresh();
        }

        private void ScheduleWasteCollection()
        {
            // Do not change supply indices while a preview is being calculated
            // or awaiting confirmation. Production resolves saved source tags.
            if (!restored || searching || preview != null || collectingWaste || SaveManager.IsDecoding) return;
            collectingWaste = true;
            EmporiumEntry.Instance.StartCoroutine(CollectWaste());
        }

        private IEnumerator CollectWaste()
        {
            // Native placement/layout can be expensive with many empty bottles.
            // Keep it off the button callback and spread work across frames.
            yield return null;
            bool blocked = false, returnBlocked = false;
            var slice = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // 0.3.11 could collect regenerating filters at zero durability.
                // Return those same objects before collecting disposable waste.
                foreach (var item in waste.childItems.Concat(AllInputs()).ToArray())
                {
                    if (SaveManager.IsDecoding || searching || preview != null ||
                        (machine is GameItemElement element && element.IsDestroyed())) yield break;
                    try
                    {
                        if (item.parentInventory == waste && WasteCollection.ProtectedFilter(item))
                        {
                            bool previousMove = internalMove;
                            internalMove = true;
                            try
                            {
                                reservedInputs.Release(item);
                                if (GraphUtils.TryAcceptAll(raw, item) != 0) returnBlocked = true;
                                alcoholViewKey = null;
                            }
                            finally { internalMove = previousMove; }
                        }
                        else if ((item.parentInventory == raw || item.parentInventory == supplies) &&
                            WasteCollection.CanCollect(item, reservedInputs, running))
                        {
                            bool previousMove = internalMove;
                            internalMove = true;
                            try
                            {
                                // Release only sources with no remaining actions. Clear the
                                // old index before native stacking can copy it to a new item.
                                reservedInputs.Release(item);
                                if (GraphUtils.TryAcceptAll(waste, item) != 0) blocked = true;
                                alcoholViewKey = null;
                            }
                            finally { internalMove = previousMove; }
                        }
                    }
                    catch (Exception error)
                    {
                        // Collection failure must never mark the chemistry batch failed.
                        ModEntry.Log.Error("Waste collection: " + error);
                        blocked = true;
                    }
                    if (slice.Elapsed.TotalMilliseconds >= 3)
                    {
                        yield return null;
                        slice.Restart();
                    }
                }
                SetTextIfChanged(wasteStatus, T(returnBlocked ? "waste_return_full" : blocked ? "waste_full" : "waste_hint"));
                blocked |= returnBlocked;
                if (wasteBlocked != blocked)
                {
                    wasteBlocked = blocked;
                    wasteStatus.SetColor(blocked ? (Color)new Color32(255, 225, 138, 255) : new Color32(185, 190, 177, 255));
                }
            }
            finally { collectingWaste = false; }
        }

        private void SwapProcessingTanks()
        {
            var reactionSlot = tankSlot;
            var holdingSlot = bufferSlot;
            var reaction = tank;
            var holding = mashBuffer;
            if (reaction == null || holding == null) throw new MachineFault("missing_buffer");
            // Move the real native vessels, including their temperature, residue
            // and identity. Detach our callbacks before changing either parent.
            ReleaseTank();
            ReleaseMashBuffer();
            try
            {
                if (!reactionSlot.Expel(reaction) || !holdingSlot.Expel(holding) ||
                    !reactionSlot.UncheckedAccept(holding) || !holdingSlot.UncheckedAccept(reaction))
                    throw new MachineFault("runtime_tank_swap");
            }
            catch
            {
                if (holding.parentInventory == reactionSlot) reactionSlot.Expel(holding);
                if (reaction.parentInventory == holdingSlot) holdingSlot.Expel(reaction);
                if (reaction.parentInventory == null) reactionSlot.UncheckedAccept(reaction);
                if (holding.parentInventory == null) holdingSlot.UncheckedAccept(holding);
                throw;
            }
            finally { BindHardware(); }
        }

        private void AdvanceImmediate()
        {
            while (running != null && !running.failed)
            {
                if (running.step >= running.plan.actions.Length)
                {
                    if (running.deferOutput && running.produced < running.plan.expectedBottles)
                    { AdvanceOutput(); return; }
                    int made = running.produced;
                    int originalExpected = running.originalExpectedBottles;
                    bool limited = running.plan.outputLimited;
                    bool quantityLimited = running.plan.requestedBottles > 0;
                    running = null;
                    maximumPreview = null;
                    selectedQuantity = 0;
                    reservedInputs.Clear();
                    Save();
                    MigrateLegacyInputs();
                    RefreshResidueWarning();
                    if (originalExpected > 0) SetStatus("complete_rounding_tail", originalExpected, made);
                    else SetStatus(quantityLimited ? "quantity_complete" : limited ? "complete_limited" : "complete", made);
                    inputGuidanceDirty = true;
                    return;
                }
                var action = running.plan.actions[running.step];
                // Older saved direct-form plans may still reference medicinal
                // solids. Pause before consuming them; already processed residue
                // stays in the tank and can be used after replanning.
                if (running.plan.directForm &&
                    (((action.operation == Operation.Blend || action.operation == Operation.Dissolve) &&
                      !DirectFormInputs.CanUse(Source(true, action.source))) ||
                     (action.operation == Operation.Pour && !DirectFormInputs.CanUseLiquid(Source(false, action.source), running.plan.directSolvents))))
                {
                    running.failed = true;
                    running.failureKey = DirectFormInputs.ProtectionReason(Source(action.operation != Operation.Pour, action.source));
                    Save();
                    SetStatus(running.failureKey);
                    return;
                }
                if (action.operation == Operation.Extract && running.plan.extractionPortions > 1)
                    SetStatus("stage_extract_portion", running.plan.actions.Take(running.step + 1).Count(a => a.operation == Operation.Extract),
                        running.plan.extractionPortions);
                else if (action.operation != Operation.Form) SetStatus("stage_" + action.operation.ToString().ToLowerInvariant());
                internalMove = true;
                try
                {
                    switch (action.operation)
                    {
                        case Operation.Blend:
                            var ingredient = Source(true, action.source);
                            if (ChemicalHelper.GetTotalVolume(ingredient) > WaterHelper.GetFreeCapacity(tank)) throw new MachineFault("runtime_tank_capacity");
                            ChemicalHelper.BlendInto(ingredient, tank, running.blenderLoss);
                            break;
                        case Operation.Dissolve:
                            ChemistryOperations.Dissolve(Source(true, action.source), tank);
                            break;
                        case Operation.Pour:
                            ChemistryOperations.Pour(Source(false, action.source), tank, action.amount);
                            break;
                        case Operation.ReturnFeed:
                            ChemistryOperations.Pour(tank, Source(false, action.source), ChemistryOperations.Total(tank));
                            break;
                        case Operation.SwapTanks:
                            SwapProcessingTanks();
                            break;
                        case Operation.StoreMash:
                            if (mashBuffer == null) throw new MachineFault("missing_buffer");
                            ChemistryOperations.Pour(tank, mashBuffer, action.amount);
                            break;
                        case Operation.FeedMash:
                            if (mashBuffer == null) throw new MachineFault("missing_buffer");
                            ChemistryOperations.Pour(mashBuffer, tank, action.amount);
                            break;
                        case Operation.Filter:
                            FiltrationPolicy.Apply(tank, action, i => Source(false, i));
                            break;
                        case Operation.Extract:
                        case Operation.Synthesize:
                        case Operation.BoilDry:
                            if (running.elapsed == 0 && preparedHeatStep != running.step)
                            {
                                int duration = action.operation == Operation.BoilDry ?
                                    (running.plan.directForm ? HeatedDrying.HeatUntilDry(new SimulationItem(tank), running.heaterUpgrades, running.plan.separateDirect || mashBuffer == null ? null : new SimulationItem(mashBuffer)) :
                                        NormalDrying.HeatUntilDry(new SimulationItem(tank), running.heaterUpgrades, action.targetTemperature, mashBuffer == null ? null : new SimulationItem(mashBuffer))) :
                                    BatchPlanner.HeatUntil(new SimulationItem(tank), running.heaterUpgrades, action,
                                        ChemicalRecipe.Get(running.plan.productId), mashBuffer == null ? null : new SimulationItem(mashBuffer));
                                if (duration < 0) throw new MachineFault(action.operation == Operation.BoilDry ?
                                    (running.plan.directForm ? "direct_boil_failed" : "normal_heat_recheck") : "runtime_reaction_endpoint");
                                running.plan.reactionSeconds += duration - action.amount;
                                action.amount = duration;
                                preparedHeatStep = running.step;
                                Save();
                            }
                            if (action.amount == 0) break;
                            return;
                        case Operation.React:
                            return;
                        case Operation.Dry:
                            if (ChemistryOperations.VolatileVolume(tank) > 0)
                            {
                                if (!MachineHelper.CanPower(cores[2])) SetStatus("no_power");
                                else if (EvaporationEstimate.Read(tank, cores[2]).DailyVolume <= 0) SetStatus("runtime_evaporation_rate");
                                return;
                            }
                            break;
                        case Operation.Form:
                            if (FinishingStorage.ReconcileRoundingTail(running, tank)) Save();
                            if (running.useFinisherTank && running.prepared < running.plan.FormTarget(running.step))
                            {
                                int count = running.plan.FormTarget(running.step) - running.prepared;
                                FinishingStorage.Stage(tank, finisherTank, count, ChemicalRecipe.Get(running.plan.productId).UnitVolume, running.plan.separateDirect);
                                running.prepared += count;
                                running.outputCommitted = running.deferOutput;
                                Save();
                            }
                            if (running.deferOutput && running.plan.separateDirect) break;
                            if (running.produced < running.plan.FormTarget(running.step))
                            {
                                AdvanceOutput();
                                return;
                            }
                            break;
                        default:
                            throw new MachineFault("runtime_saved_operation");
                    }
                    running.step++;
                    running.elapsed = 0;
                    Save();
                }
                finally { internalMove = false; }
            }
        }

        private void AdvanceOutput()
        {
            if (DeferredOutput.Waiting(running)) { SetStatus("defer_output_ready"); return; }
            if (blockedOutputShape != null)
            {
                var space = OutputSpace.Capture(output);
                if (space != null && !space.Fits(blockedOutputShape.width, blockedOutputShape.height))
                { SetStatus("output_full"); return; }
                blockedOutputShape = null;
            }
            SetStatus("stage_form");
            if (formingBatch == running) return;
            formingBatch = running;
            EmporiumEntry.Instance.StartCoroutine(FormProducts(running));
        }

        private IEnumerator FormProducts(RunningBatch batch)
        {
            // Hundreds of native item factories/layout updates in one frame can
            // freeze the game even though numerical planning is quick. Yield by
            // wall-clock budget without inventing an extra in-game reaction wait.
            yield return null;
            var slice = System.Diagnostics.Stopwatch.StartNew();
            OutputPlacement placements = null;
            bool checkpointPending = false;
            int sliceCount = 0;
            try
            {
                placements = OutputPlacement.Capture(output);
                while (running == batch && !batch.failed && batch.produced < DeferredOutput.Target(batch))
                {
                    bool made;
                    internalMove = true;
                    long started = ModPerformance.Begin();
                    try { made = FormOne(placements); }
                    catch (Exception error) { Fail(error); yield break; }
                    finally { internalMove = false; ModPerformance.Record(3, started); }
                    if (!made) yield break;
                    checkpointPending = true;
                    // Include downstream Canvas cost by bounding the number of
                    // newly created native objects, even on a fast CPU.
                    if (++sliceCount >= 8 || slice.Elapsed.TotalMilliseconds >= 3)
                    {
                        Save(); checkpointPending = false;
                        RefreshProgress();
                        yield return null;
                        slice.Restart();
                        placements = OutputPlacement.Capture(output);
                        sliceCount = 0;
                    }
                }
            }
            finally
            {
                // Every yielded frame, full-bin pause, completion or cancellation
                // has a complete checkpoint matching the physical inventory.
                if (checkpointPending && running == batch) Save();
                if (formingBatch == batch) formingBatch = null;
            }
            if (running == batch && !batch.failed) Guard(AdvanceImmediate);
        }

        private bool FormOne(OutputPlacement placements)
        {
            var recipe = ChemicalRecipe.Get(running.plan.productId);
            var sourceTank = running.useFinisherTank ? finisherTank : tank;
            if (sourceTank == null) throw new MachineFault("missing_finisher_tank");
            if (WaterHelper.IsWet(sourceTank)) throw new MachineFault("runtime_still_wet");
            if (ChemistryOperations.Total(sourceTank) < recipe.UnitVolume)
                throw new MachineFault("runtime_less_than_one", recipe.Id + "; produced " + running.produced + "/" +
                    running.plan.expectedBottles + "; prepared " + running.prepared + "; stored " +
                    ChemistryOperations.Total(sourceTank) + " uL; unit " + recipe.UnitVolume + " uL");
            var trialTank = new SimulationItem(sourceTank);
            var trialProduct = new SimulationItem();
            ChemistryOperations.TransferToProduct(trialTank, trialProduct, recipe.UnitVolume);
            MachineChemFinisher.IntroduceImpurity(trialProduct, running.finisherLoss);
            if ((long)ChemistryOperations.Amount(trialProduct, recipe.Active) * 100 < (long)running.plan.actualGrade * recipe.UnitVolume)
                throw new MachineFault("runtime_purity_changed");
            var product = ModHelper.SetItemOwnerShip(DirectoryMaster.Item(recipe.Id), true);
            BottlingOperations.WriteParts(product, ChemistryOperations.Parts(trialProduct));
            ChemicalProductHelper.GradeProduct(product);
            var destination = placements == null ? OutputSpace.FindSlot(output, product) : placements.FindSlot(output, product);
            if (destination == null)
            {
                if (product.mayStackItemFunc == null && OutputSpace.Rectangle(product.modifiedShape))
                    blockedOutputShape = product.modifiedShape.Clone();
                product.Destroy();
                SetStatus("output_full");
                return false;
            }
            // Reuse the native-validated slot instead of searching the full grid twice.
            if (destination.TryAcceptOnce(1) != 1)
            {
                product.Destroy();
                throw new MachineFault("runtime_output_rejected");
            }
            BottlingOperations.Consume(sourceTank, recipe.UnitVolume);
            running.produced++;
            placements?.Add(product.modifiedShape);
            AchievementTracker.Instance.CheckLabPure(product);
            return true;
        }

        private void Stop()
        {
            if (tankCleanup.Pending) { CancelTankCleanup(); return; }
            if (residueConfirmation.Pending)
            {
                ClearResidueConfirmation();
                SetStatus(preview != null && preview.actualGrade < preview.requestedGrade ? "fallback" : "ready");
                return;
            }
            bool keptBatch = running != null;
            searchGeneration++;
            quantitySearching = searching = false;
            preview = null;
            previewInputs = null;
            maximumPreview = null;
            selectedQuantity = 0;
            releaseStep = -1;
            running = null;
            blockedOutputShape = null;
            reservedInputs.Clear();
            ClearPreviewText();
            ResetGradeResults();
            Save();
            MigrateLegacyInputs();
            RefreshResidueWarning();
            SetStatus(keptBatch ? "batch_kept" : "stopped");
            RefreshInputGuidance();
        }
        private void Save()
        {
            BatchPersistence.Save(machine, running);
            foreach (var binding in coreBindings) binding?.Refresh();
            RefreshTurbo();
        }
        private void Guard(Action action) { try { action(); } catch (Exception error) { Fail(error); } }
        private void TimedGuard(Action action, int category, string operation = null)
        {
            long started = ModPerformance.Begin();
            try { Guard(action); }
            finally { ModPerformance.Record(category, started, operation); }
        }
        private void Fail(Exception error)
        {
            quantitySearching = searching = false;
            string key = error is MachineFault known ? known.Key : "runtime_unknown";
            if (running != null)
            {
                running.failed = true; running.failureKey = key;
                if (DeferredOutput.Ready(running)) DeferredOutput.Pause(running);
                Save();
            }
            preview = null;
            if (running == null) { ClearPreviewText(key); previewText.SetColor(new Color32(255, 170, 155, 255)); }
            SetStatus(key);
            ModEntry.Log.Error(error + "\n" + BatchFailureSnapshot.Describe(running, tank, mashBuffer, finisherTank));
        }
    }
}
