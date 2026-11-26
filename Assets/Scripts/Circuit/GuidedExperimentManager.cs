using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using TMPro;
using System.Collections.Generic;

namespace VRContent.Circuit
{
    public enum ExperimentWorkflowState
    {
        NOT_STARTED,
        PLACING_RESISTORS,
        READY_TO_VALIDATE,
        VALIDATED,
        READING_RECORDED,
        VERIFIED
    }

    /// <summary>
    /// Central guided-experiment manager.
    /// Drives a step-by-step instruction panel, highlights the needed
    /// grab component and destination slot, validates placement, and
    /// tracks overall progress. Preserves all existing circuit logic.
    /// </summary>
    public class GuidedExperimentManager : MonoBehaviour
    {
        // ─────────────────────────── Singleton ────────────────────────────
        private static GuidedExperimentManager _instance;
        public static GuidedExperimentManager Instance => _instance;

        // ─────────────────────────── References ───────────────────────────
        [Header("Core References")]
        public KirchhoffSimulator simulator;
        public CircuitConnectionManager connectionManager;
        public KirchhoffDisplayUI displayUI;

        // ─────────────────────────── Slots ────────────────────────────────
        [Header("Snap Slot Highlights")]
        public SnapSlotHighlight slotR1;         // KVL R1 socket
        public SnapSlotHighlight slotR2;         // KVL R2 socket
        public SnapSlotHighlight slotRA;         // KCL Branch A socket
        public SnapSlotHighlight slotRB;         // KCL Branch B socket
        public SnapSlotHighlight slotAmmeter;    // Meter series slot
        public SnapSlotHighlight slotVoltmeter;  // Meter parallel slot

        // ─────────────────────────── Grabbables ───────────────────────────
        [Header("Grabbable Components")]
        public GrabbableLabComponent grabbable100Ohm;
        public GrabbableLabComponent grabbable200Ohm;
        public GrabbableLabComponent grabbable300Ohm;
        public GrabbableLabComponent grabbable200OhmBranch;
        public GrabbableLabComponent grabbableAmmeter;
        public GrabbableLabComponent grabbableVoltmeter;

        // ─────────────────────────── Circuit Sockets ──────────────────────
        [Header("Circuit Sockets (for validation)")]
        public CircuitSocket circuitSocketR1;
        public CircuitSocket circuitSocketR2;
        public CircuitSocket circuitSocketRA;
        public CircuitSocket circuitSocketRB;

        // ─────────────────────────── Instruction Panel ─────────────────────
        [Header("Instruction Panel (World Space Canvas)")]
        public Canvas instructionCanvas;
        public TextMeshProUGUI stepNumberText;      // "Step 2 / 7"
        public TextMeshProUGUI stepTitleText;
        public TextMeshProUGUI stepBodyText;
        public TextMeshProUGUI controlHintText;
        public TextMeshProUGUI progressText;        // "3/7 steps completed"
        public TextMeshProUGUI feedbackText;        // Green/red feedback line
        public Button nextButton;
        public Button prevButton;
        public Button skipToVerifyButton;
        public Image progressBarFill;

        // ─────────────────────────── Mode Switch ──────────────────────────
        [Header("Experiment Mode")]
        public bool startInKVL = true;

        // ─────────────────────────── Steps ────────────────────────────────
        private List<GuidedExperimentStep> _currentSteps = new List<GuidedExperimentStep>();
        private int _currentStepIndex = 0;
        private int _totalStepsCompleted = 0;
        private bool _isKVL = true;
        public bool isComponentPlacementMode { get; private set; } = false;
        public ExperimentWorkflowState workflowState { get; private set; } = ExperimentWorkflowState.NOT_STARTED;

        // Track which steps are done
        private bool[] _stepDone;

        // ─────────────────────────── Interaction Layers ───────────────────
        private const int ResistorLayerMask = 1 << 0; // Default
        private const int WireLayerMask     = 1 << 1;
        private const int MeterLayerMask    = 1 << 2;

        // ─────────────────────────── Feedback timing ──────────────────────
        private float _feedbackClearTimer = 0f;

        // ══════════════════════════════════════════════════════════════════
        //  UNITY LIFECYCLE
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            // Auto-find references
            if (simulator == null)
                simulator = FindFirstObjectByType<KirchhoffSimulator>();
            if (connectionManager == null)
                connectionManager = FindFirstObjectByType<CircuitConnectionManager>();
            if (displayUI == null)
                displayUI = FindFirstObjectByType<KirchhoffDisplayUI>();

            // Wire buttons
            if (nextButton != null) nextButton.onClick.AddListener(OnNextClicked);
            if (prevButton != null) prevButton.onClick.AddListener(OnPrevClicked);
            if (skipToVerifyButton != null) skipToVerifyButton.onClick.AddListener(SkipToVerify);

            _isKVL = startInKVL;
            workflowState = ExperimentWorkflowState.NOT_STARTED;
            if (slotVoltmeter != null)
            {
                slotVoltmeter.gameObject.SetActive(false);
                slotVoltmeter = null;
            }
            // Start with all resistor slots EMPTY
            if (circuitSocketR1 != null) circuitSocketR1.ClearResistor(true);
            if (circuitSocketR2 != null) circuitSocketR2.ClearResistor(true);
            if (circuitSocketRA != null) circuitSocketRA.ClearResistor(true);
            if (circuitSocketRB != null) circuitSocketRB.ClearResistor(true);

            ConfigureInteractionLayers();
            LoadSteps(_isKVL);
            ShowCurrentStep();
        }

        private void Update()
        {
            // Auto-advance for steps that complete themselves (power-on, etc.)
            if (_currentSteps != null && _currentStepIndex < _currentSteps.Count)
            {
                var step = _currentSteps[_currentStepIndex];
                if (step.autoAdvance && !_stepDone[_currentStepIndex] && IsStepComplete(_currentStepIndex))
                {
                    CompleteCurrentStep();
                }
            }

            // Clear feedback label after timeout
            if (_feedbackClearTimer > 0f)
            {
                _feedbackClearTimer -= Time.deltaTime;
                if (_feedbackClearTimer <= 0f && feedbackText != null)
                    feedbackText.text = "";
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PUBLIC API — called by KirchhoffDisplayUI / buttons
        // ══════════════════════════════════════════════════════════════════

        public void SwitchMode(bool kvl)
        {
            _isKVL = kvl;
            isComponentPlacementMode = false;
            workflowState = ExperimentWorkflowState.NOT_STARTED;

            // Reset previous experiment's placement progress & unlock sockets
            if (circuitSocketR1 != null) circuitSocketR1.ClearResistor();
            if (circuitSocketR2 != null) circuitSocketR2.ClearResistor();
            if (circuitSocketRA != null) circuitSocketRA.ClearResistor();
            if (circuitSocketRB != null) circuitSocketRB.ClearResistor();

            if (slotR1 != null) slotR1.SetFilled(false);
            if (slotR2 != null) slotR2.SetFilled(false);
            if (slotRA != null) slotRA.SetFilled(false);
            if (slotRB != null) slotRB.SetFilled(false);
            if (slotAmmeter != null) slotAmmeter.SetFilled(false);
            if (slotVoltmeter != null) slotVoltmeter.SetFilled(false);

            LoadSteps(kvl);
            ClearAllHighlights();
            ShowCurrentStep();
        }

        public void OnNextClicked()
        {
            if (_currentSteps == null || _currentStepIndex >= _currentSteps.Count) return;

            // Validate current step first
            if (!_stepDone[_currentStepIndex])
            {
                if (!IsStepComplete(_currentStepIndex))
                {
                    ShowBlockedFeedback(_currentStepIndex);
                    return;
                }
                CompleteCurrentStep();
                return;
            }

            // Already done — advance
            AdvanceToStep(_currentStepIndex + 1);
        }

        public void OnPrevClicked()
        {
            if (_currentStepIndex > 0)
                AdvanceToStep(_currentStepIndex - 1);
        }

        public void SkipToVerify()
        {
            // Allow jump to verify step if circuit is valid
            int verifyIdx = _currentSteps.FindIndex(s => s.stepType == StepType.VerifyResults);
            if (verifyIdx >= 0)
            {
                ClearAllHighlights();
                _currentStepIndex = verifyIdx;
                ShowCurrentStep();
            }
        }

        /// <summary>Called by GrabbableLabComponent when the user grabs a component.</summary>
        public void OnComponentGrabbed(GrabbableLabComponent comp)
        {
            if (comp == null) return;

            SnapSlotHighlight targetSlot = null;
            switch (comp.componentType)
            {
                case GrabbableLabComponent.LabComponentType.Resistor100:
                    targetSlot = slotR1;
                    break;
                case GrabbableLabComponent.LabComponentType.Resistor200:
                    targetSlot = slotR2;
                    break;
                case GrabbableLabComponent.LabComponentType.Resistor200Branch:
                    targetSlot = slotRA;
                    break;
                case GrabbableLabComponent.LabComponentType.Resistor300:
                    targetSlot = slotRB;
                    break;
                case GrabbableLabComponent.LabComponentType.Ammeter:
                    targetSlot = slotAmmeter;
                    break;
                case GrabbableLabComponent.LabComponentType.Voltmeter:
                    targetSlot = slotVoltmeter;
                    break;
            }

            // Highlight ONLY the currently required slot (never multiple slots at once)
            if (targetSlot != null && !targetSlot.isFilled && _currentSteps != null && _currentStepIndex < _currentSteps.Count)
            {
                var step = _currentSteps[_currentStepIndex];
                if (GetSlotHighlight(step.targetSlotName) == targetSlot)
                {
                    targetSlot.SetHighlight(true);
                }
            }
        }

        public void OnWrongResistorRejected(string socketID, float actualResistance, string customReason = null)
        {
            string expected = "the matching resistor";
            string idUpper = socketID != null ? socketID.ToUpperInvariant() : "";
            if (idUpper.Contains("R1")) expected = "100Ω resistor";
            else if (idUpper.Contains("R2")) expected = "200Ω resistor";
            else if (idUpper.Contains("RA")) expected = "200Ω resistor";
            else if (idUpper.Contains("RB")) expected = "300Ω resistor";

            string msg = !string.IsNullOrEmpty(customReason)
                ? customReason
                : $"Rejected {actualResistance:F0}Ω in [{socketID}]! Only {expected} is accepted here.";

            SetFeedback("✗ " + msg, new Color(1f, 0.3f, 0.2f));
            if (displayUI != null && displayUI.verificationText != null)
            {
                displayUI.verificationText.text = "<color=#FF4444><b>[INVALID PLACEMENT REJECTED]</b> " + msg + "</color>";
            }
        }

        /// <summary>Enters component placement mode when CHECK CIRCUIT is pressed with missing resistors.</summary>
        public string BeginComponentPlacementMode()
        {
            isComponentPlacementMode = true;
            workflowState = ExperimentWorkflowState.PLACING_RESISTORS;
            _isKVL = simulator == null || simulator.currentMode == CircuitMode.KVL_Series;
            if (_currentSteps == null || _currentSteps.Count == 0)
                LoadSteps(_isKVL);

            if (_isKVL)
            {
                bool r1Ok = IsResistorInSocket("Socket_KVL_R1", 100f);
                _currentStepIndex = r1Ok ? 1 : 0;
                ShowCurrentStep();
                return r1Ok
                    ? "Place the 200Ω resistor in R2."
                    : "Place the 100Ω resistor in R1.";
            }
            else
            {
                bool raOk = IsResistorInSocket("Socket_KCL_RA", 200f);
                _currentStepIndex = raOk ? 1 : 0;
                ShowCurrentStep();
                return raOk
                    ? "Place the 300Ω resistor in Branch B."
                    : "Place the 200Ω resistor in Branch A.";
            }
        }

        /// <summary>Called by CircuitSocket when a resistor is seated.</summary>
        public void OnResistorPlaced(string socketID, float resistance)
        {
            if (_currentSteps == null || _currentSteps.Count == 0) return;

            var placedSocket = GetCircuitSocket(socketID);
            if (placedSocket == null) return;

            if (_isKVL)
            {
                bool r1Ok = IsResistorInSocket("Socket_KVL_R1", 100f);
                bool r2Ok = IsResistorInSocket("Socket_KVL_R2", 200f);

                if (placedSocket == circuitSocketR1 && Mathf.Approximately(resistance, 100f))
                {
                    if (_stepDone.Length > 0) _stepDone[0] = true;
                    slotR1?.SetFilled(true);
                    grabbable100Ohm?.SetNeeded(false);
                    SetFeedback("✓ 100Ω resistor placed in R1!", Color.green);
                }
                else if (placedSocket == circuitSocketR2 && Mathf.Approximately(resistance, 200f))
                {
                    if (_stepDone.Length > 1) _stepDone[1] = true;
                    slotR2?.SetFilled(true);
                    grabbable200Ohm?.SetNeeded(false);
                    SetFeedback("✓ 200Ω resistor placed in R2!", Color.green);
                }

                if (r1Ok && r2Ok)
                {
                    isComponentPlacementMode = false;
                    workflowState = ExperimentWorkflowState.READY_TO_VALIDATE;
                    _currentStepIndex = Mathf.Min(2, _currentSteps.Count - 1);
                    ShowCurrentStep();
                    if (displayUI != null && displayUI.verificationText != null)
                    {
                        displayUI.verificationText.text =
                            "<color=#00FF66><b>RESISTORS PLACED SUCCESSFULLY</b></color>\n" +
                            "<color=#00E5FF>Click [CHECK CIRCUIT] to validate the series circuit.</color>";
                    }
                    if (displayUI != null) displayUI.RefreshUI();
                }
                else
                {
                    workflowState = ExperimentWorkflowState.PLACING_RESISTORS;
                    _currentStepIndex = r1Ok ? 1 : 0;
                    ShowCurrentStep();
                    if (displayUI != null && displayUI.verificationText != null)
                    {
                        displayUI.verificationText.text = r1Ok
                            ? "<color=#00FF66><b>[R1 PLACED] 100Ω resistor snapped and locked!</b></color>\n<color=#00E5FF>Place the 200Ω resistor in R2.</color>"
                            : "<color=#00FF66><b>[R2 PLACED] 200Ω resistor snapped and locked!</b></color>\n<color=#00E5FF>Place the 100Ω resistor in R1.</color>";
                    }
                }
            }
            else // KCL
            {
                bool raOk = IsResistorInSocket("Socket_KCL_RA", 200f);
                bool rbOk = IsResistorInSocket("Socket_KCL_RB", 300f);

                if (placedSocket == circuitSocketRA && Mathf.Approximately(resistance, 200f))
                {
                    if (_stepDone.Length > 0) _stepDone[0] = true;
                    slotRA?.SetFilled(true);
                    grabbable200OhmBranch?.SetNeeded(false);
                    SetFeedback("✓ 200Ω resistor placed in Branch A!", Color.green);
                }
                else if (placedSocket == circuitSocketRB && Mathf.Approximately(resistance, 300f))
                {
                    if (_stepDone.Length > 1) _stepDone[1] = true;
                    slotRB?.SetFilled(true);
                    grabbable300Ohm?.SetNeeded(false);
                    SetFeedback("✓ 300Ω resistor placed in Branch B!", Color.green);
                }

                if (raOk && rbOk)
                {
                    isComponentPlacementMode = false;
                    workflowState = ExperimentWorkflowState.READY_TO_VALIDATE;
                    _currentStepIndex = Mathf.Min(2, _currentSteps.Count - 1);
                    ShowCurrentStep();
                    if (displayUI != null && displayUI.verificationText != null)
                    {
                        displayUI.verificationText.text =
                            "<color=#00FF66><b>RESISTORS PLACED SUCCESSFULLY</b></color>\n" +
                            "<color=#00E5FF>Click [CHECK CIRCUIT] to validate the parallel circuit.</color>";
                    }
                    if (displayUI != null) displayUI.RefreshUI();
                }
                else
                {
                    workflowState = ExperimentWorkflowState.PLACING_RESISTORS;
                    _currentStepIndex = raOk ? 1 : 0;
                    ShowCurrentStep();
                    if (displayUI != null && displayUI.verificationText != null)
                    {
                        displayUI.verificationText.text = raOk
                            ? "<color=#00FF66><b>[BRANCH A PLACED] 200Ω resistor snapped and locked!</b></color>\n<color=#00E5FF>Place the 300Ω resistor in Branch B.</color>"
                            : "<color=#00FF66><b>[BRANCH B PLACED] 300Ω resistor snapped and locked!</b></color>\n<color=#00E5FF>Place the 200Ω resistor in Branch A.</color>";
                    }
                }
            }
        }

        /// <summary>Called by meter zone triggers when ammeter/voltmeter is placed.</summary>
        public void OnMeterPlaced(bool isAmmeter, bool correctPosition)
        {
            int idx = _currentStepIndex;
            if (_currentSteps == null || idx >= _currentSteps.Count) return;

            var step = _currentSteps[idx];
            if (step.stepType != StepType.PlaceMeter) return;

            bool wantAmmeter = step.targetSlotName.ToLower().Contains("ammeter") || step.targetSlotName.ToLower().Contains("series");
            bool rightMeter = (isAmmeter == wantAmmeter);

            if (rightMeter && correctPosition)
            {
                CompleteCurrentStep();
                SetFeedback("✓ Meter placed correctly!", Color.green);
            }
            else
            {
                string expected = wantAmmeter ? "Ammeter (series)" : "Voltmeter (parallel)";
                SetFeedback("✗ Wrong meter or position! Required: " + expected + ".", new Color(1f, 0.4f, 0f));
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  STEP MANAGEMENT
        // ══════════════════════════════════════════════════════════════════

        private void LoadSteps(bool kvl)
        {
            _currentSteps.Clear();
            _currentStepIndex = 0;
            _totalStepsCompleted = 0;

            if (kvl)
                BuildKVLSteps();
            else
                BuildKCLSteps();

            _stepDone = new bool[_currentSteps.Count];
        }

        private void BuildKVLSteps()
        {
            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 1 — Place R1 (100 Ω)",
                instructions =
                    "Grab the YELLOW 100 Ω resistor from the component tray.\n\n" +
                    "Drop it into the highlighted R1 slot on the circuit board.\n\n" +
                    "The slot will glow CYAN. Release when you see it snap into place.",
                stepType = StepType.PlaceResistor,
                targetObjectName = "Resistor_100Ohm",
                targetSlotName = "Socket_KVL_R1",
                requiredResistance = 100f,
                controlHint = "PC: Left-click + drag   |   VR: Grip button to grab",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 2 — Place R2 (200 Ω)",
                instructions =
                    "Grab the ORANGE 200 Ω resistor from the component tray.\n\n" +
                    "Drop it into the highlighted R2 slot on the circuit board.\n\n" +
                    "The slot will glow CYAN. Release when it snaps in.",
                stepType = StepType.PlaceResistor,
                targetObjectName = "Resistor_200Ohm",
                targetSlotName = "Socket_KVL_R2",
                requiredResistance = 200f,
                controlHint = "PC: Left-click + drag   |   VR: Grip button to grab",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 3 — Place Ammeter (Series)",
                instructions =
                    "Grab the AMMETER from the bench.\n\n" +
                    "Place it in the SERIES position (between the supply and R1).\n" +
                    "The ammeter slot will glow CYAN.\n\n" +
                    "IMPORTANT: Ammeters must always be in series — never in parallel.",
                stepType = StepType.PlaceMeter,
                targetObjectName = "Digital_Ammeter",
                targetSlotName = "Slot_Ammeter_Series",
                controlHint = "PC: Left-click + drag   |   VR: Grip button to grab",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 4 — Connect Wires",
                instructions =
                    "Connect the circuit wires to the highlighted terminals:\n\n" +
                    "• Supply (+) → Ammeter (+) terminal\n" +
                    "• Ammeter (−) → R1 input terminal\n" +
                    "• R1 output → R2 input terminal\n" +
                    "• R2 output → Supply (−) / GND\n\n" +
                    "Grab each wire endpoint and drag it to the glowing terminal.",
                stepType = StepType.ConnectWire,
                controlHint = "PC: Left-click wire endpoint + drag to terminal   |   VR: Grip",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 5 — Switch ON Power",
                instructions =
                    "Press the POWER ON/OFF button on the control panel (or the button above).\n\n" +
                    "The DC supply display should show 6.0 V.\n" +
                    "The circuit status should change to VALID – CLOSED LOOP.\n\n" +
                    "If you see OPEN CIRCUIT, check all wire connections first.",
                stepType = StepType.TogglePower,
                controlHint = "Click [POWER ON/OFF] button on the board",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 7 — Check, Record & Verify",
                instructions =
                    "Now take your readings:\n\n" +
                    "1. Press [CHECK CIRCUIT] — confirms the loop is complete.\n" +
                    "2. Press [RECORD READING] — saves the row to the observation table.\n" +
                    "3. Press [VERIFY RESULTS] — confirms KVL: Vs − V1 − V2 = 0\n\n" +
                    "Expected readings:\n" +
                    "  I = 0.0200 A  (20.0 mA)\n" +
                    "  V1 = 2.00 V   V2 = 4.00 V\n" +
                    "  Residual = 0.0000 V  → PASS",
                stepType = StepType.VerifyResults,
                controlHint = "Use the 5 action buttons on the board",
                autoAdvance = false
            });
        }

        private void BuildKCLSteps()
        {
            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 1 — Place R1 (200 Ω) in Branch A",
                instructions =
                    "Grab the RED 200 Ω resistor (Branch A) from the tray.\n\n" +
                    "Drop it into the highlighted BRANCH A slot on the circuit board.\n\n" +
                    "Branch A is the LEFT parallel branch.",
                stepType = StepType.PlaceResistor,
                targetObjectName = "Resistor_200OhmBranch",
                targetSlotName = "Socket_KCL_RA",
                requiredResistance = 200f,
                controlHint = "PC: Left-click + drag   |   VR: Grip button",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 2 — Place R2 (300 Ω) in Branch B",
                instructions =
                    "Grab the BROWN 300 Ω resistor (Branch B) from the tray.\n\n" +
                    "Drop it into the highlighted BRANCH B slot on the circuit board.\n\n" +
                    "Branch B is the RIGHT parallel branch.",
                stepType = StepType.PlaceResistor,
                targetObjectName = "Resistor_300Ohm",
                targetSlotName = "Socket_KCL_RB",
                requiredResistance = 300f,
                controlHint = "PC: Left-click + drag   |   VR: Grip button",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 3 — Connect the Junction",
                instructions =
                    "Connect the wire from the supply (+) to the JUNCTION NODE.\n\n" +
                    "The junction node is the gold node at the centre of the board.\n" +
                    "From the junction, two wires should run:\n" +
                    "  • One to Branch A (200 Ω)\n" +
                    "  • One to Branch B (300 Ω)\n\n" +
                    "Return wire: Both branches rejoin at the bottom → Supply (−).",
                stepType = StepType.ConnectWire,
                controlHint = "PC: Left-click wire endpoint + drag   |   VR: Grip",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 4 — Place Ammeters",
                instructions =
                    "Place the AMMETER in the MAIN inflow path (before the junction).\n\n" +
                    "This measures the total incoming current Iin = 0.10 A.\n\n" +
                    "The ammeter must be in series with the main supply wire.",
                stepType = StepType.PlaceMeter,
                targetObjectName = "Digital_Ammeter",
                targetSlotName = "Slot_Ammeter_Series",
                controlHint = "PC: Left-click + drag   |   VR: Grip button",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 5 — Switch ON Power (12 V)",
                instructions =
                    "Press [TOGGLE 6V / 12V] to select 12 V.\n\n" +
                    "Then press [POWER ON/OFF] to energize the circuit.\n\n" +
                    "The supply display should show 12.0 V.\n" +
                    "Circuit status should show: VALID – PARALLEL JUNCTION.",
                stepType = StepType.TogglePower,
                controlHint = "Click toggle then power button on the board",
                autoAdvance = true
            });

            _currentSteps.Add(new GuidedExperimentStep
            {
                stepTitle = "Step 7 — Check, Record & Verify",
                instructions =
                    "Take your readings:\n\n" +
                    "1. Press [CHECK CIRCUIT] — verifies the parallel topology.\n" +
                    "2. Press [RECORD READING] — saves the observation row.\n" +
                    "3. Press [VERIFY RESULTS] — confirms KCL: Iin = I1 + I2\n\n" +
                    "Expected readings:\n" +
                    "  I1 = 0.0600 A   I2 = 0.0400 A\n" +
                    "  Iin = 0.1000 A\n" +
                    "  Residual = 0.0000 A  → PASS",
                stepType = StepType.VerifyResults,
                controlHint = "Use the 5 action buttons on the board",
                autoAdvance = false
            });
        }

        // ══════════════════════════════════════════════════════════════════
        //  STEP DISPLAY
        // ══════════════════════════════════════════════════════════════════

        private void ShowCurrentStep()
        {
            if (_currentSteps == null || _currentStepIndex >= _currentSteps.Count) return;

            ClearAllHighlights();

            var step = _currentSteps[_currentStepIndex];
            int total = _currentSteps.Count;
            int stepNum = _currentStepIndex + 1;

            // Update panel texts
            if (stepNumberText != null)
                stepNumberText.text = string.Format("Step {0} of {1}", stepNum, total);

            if (stepTitleText != null)
            {
                stepTitleText.text = step.stepTitle;
                stepTitleText.color = new Color(0f, 0.898f, 1f); // cyan
            }

            if (stepBodyText != null)
                stepBodyText.text = step.instructions;

            if (controlHintText != null)
            {
                controlHintText.text = BuildControlHint(step);
                controlHintText.color = new Color(1f, 0.85f, 0.3f); // yellow
            }

            // Progress bar
            int completed = CountCompletedSteps();
            if (progressText != null)
                progressText.text = string.Format("{0}/{1} steps completed", completed, total);

            if (progressBarFill != null)
                progressBarFill.fillAmount = total > 0 ? (float)completed / total : 0f;

            // Prev/Next button states
            if (prevButton != null) prevButton.interactable = (_currentStepIndex > 0);
            if (nextButton != null)
            {
                bool isLast = (_currentStepIndex >= _currentSteps.Count - 1);
                var btnTmp = nextButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnTmp != null) btnTmp.text = isLast ? "FINISH" : "NEXT →";
                nextButton.interactable = !isLast || _stepDone[_currentStepIndex];
            }

            // Refresh main board UI whenever step changes
            if (displayUI != null) displayUI.RefreshUI();

            // Disable wire grabbing during resistor placement steps; re-enable for wiring/meter stages
            SetWireGrabbingEnabled(step.stepType != StepType.PlaceResistor);

            // Activate highlights for this step
            ActivateHighlightsForStep(step);
        }

        private void ConfigureInteractionLayers()
        {
            foreach (var r in FindObjectsByType<ResistorComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var grab = r.GetComponent<XRGrabInteractable>();
                if (grab != null) grab.interactionLayers = ResistorLayerMask;
            }
            foreach (var cs in FindObjectsByType<CircuitSocket>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cs.socketInteractor != null) cs.socketInteractor.interactionLayers = ResistorLayerMask;
            }
            foreach (var p in FindObjectsByType<WirePlug>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (p.grabInteractable != null) p.grabInteractable.interactionLayers = WireLayerMask;
            }
            foreach (var t in FindObjectsByType<CircuitTerminal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.socketInteractor != null) t.socketInteractor.interactionLayers = WireLayerMask;
            }
            foreach (var m in FindObjectsByType<LabMeter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var grab = m.GetComponent<XRGrabInteractable>();
                if (grab != null) grab.interactionLayers = MeterLayerMask;
            }
            foreach (var ms in FindObjectsByType<MeterSnapSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var sock = ms.GetComponent<XRSocketInteractor>();
                if (sock != null) sock.interactionLayers = MeterLayerMask;
            }

            foreach (var ray in FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None))
            {
                if (ray.name.Contains("Teleport")) continue;
                ray.hitClosestOnly = true;
                ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
                ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            }
        }

        public void SetWireGrabbingEnabled(bool enabled)
        {
            int mask = enabled
                ? (ResistorLayerMask | WireLayerMask | MeterLayerMask)
                : (ResistorLayerMask | MeterLayerMask);

            foreach (var ray in FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None))
            {
                if (ray.name.Contains("Teleport")) continue;
                ray.interactionLayers = mask;
            }
            foreach (var dir in FindObjectsByType<XRDirectInteractor>(FindObjectsSortMode.None))
            {
                dir.interactionLayers = mask;
            }
        }

        public string GetCurrentActionSummary()
        {
            if (_currentSteps == null || _currentStepIndex >= _currentSteps.Count)
                return "Experiment complete.";
            var step = _currentSteps[_currentStepIndex];
            int total = _currentSteps.Count;
            if (_stepDone != null && _currentStepIndex < _stepDone.Length && _stepDone[_currentStepIndex])
                return $"Step {_currentStepIndex + 1}/{total} complete!";
            return $"[{_currentStepIndex + 1}/{total}] {step.stepTitle}";
        }

        private void ActivateHighlightsForStep(GuidedExperimentStep step)
        {
            switch (step.stepType)
            {
                case StepType.PlaceResistor:
                    // Highlight ONLY the currently required slot and resistor
                    var grabComp = GetGrabbableForStep(step);
                    if (grabComp != null) grabComp.SetNeeded(true);
                    var slotHL = GetSlotHighlight(step.targetSlotName);
                    if (slotHL != null) slotHL.SetHighlight(true);
                    break;

                case StepType.PlaceMeter:
                    bool wantAmmeter = step.targetSlotName.ToLower().Contains("ammeter") || step.targetSlotName.ToLower().Contains("series");
                    var meterComp = wantAmmeter ? grabbableAmmeter : grabbableVoltmeter;
                    if (meterComp != null) meterComp.SetNeeded(true);
                    var meterSlot = GetSlotHighlight(step.targetSlotName);
                    if (meterSlot != null) meterSlot.SetHighlight(true);
                    break;

                case StepType.ConnectWire:
                    // No specific component highlight — instructions describe which terminals
                    break;

                case StepType.TogglePower:
                case StepType.CheckCircuit:
                case StepType.RecordReading:
                case StepType.VerifyResults:
                case StepType.Info:
                    // No grab highlight needed
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  STEP COMPLETION
        // ══════════════════════════════════════════════════════════════════

        private bool IsStepComplete(int idx)
        {
            if (idx < 0 || idx >= _currentSteps.Count) return false;
            var step = _currentSteps[idx];

            switch (step.stepType)
            {
                case StepType.Info:
                    return true; // Always passable

                case StepType.PlaceResistor:
                    return IsResistorInSocket(step.targetSlotName, step.requiredResistance);

                case StepType.PlaceMeter:
                    bool wantAmmeter = step.targetSlotName.ToLower().Contains("ammeter") || step.targetSlotName.ToLower().Contains("series");
                    var mSlot = wantAmmeter ? slotAmmeter : slotVoltmeter;
                    if (mSlot != null && mSlot.isFilled) return true;
                    return wantAmmeter
                        ? (connectionManager != null && connectionManager.isAmmeterInSeries)
                        : (connectionManager != null && connectionManager.isVoltmeterInParallel);

                case StepType.ConnectWire:
                    // Wire step passes when the circuit is detected as having supply + ground paths
                    if (simulator == null) return false;
                    return _isKVL ? simulator.isKVLClosed : simulator.isKCLActive;

                case StepType.TogglePower:
                    return simulator != null && simulator.isPowerOn;

                case StepType.CheckCircuit:
                    return simulator != null && (_isKVL ? simulator.isKVLClosed : simulator.isKCLActive);

                case StepType.RecordReading:
                    return displayUI != null && (_isKVL
                        ? displayUI.kvlObservations.Count > 0
                        : displayUI.kclObservations.Count > 0);

                case StepType.VerifyResults:
                    if (simulator == null) return false;
                    bool hasObservation = displayUI != null && (_isKVL
                        ? displayUI.kvlObservations.Count > 0
                        : displayUI.kclObservations.Count > 0);
                    if (!hasObservation) return false;
                    return _isKVL
                        ? (simulator.kvlResidual <= 0.005f && simulator.isKVLClosed)
                        : (simulator.kclDeltaI <= 0.005f && simulator.isKCLActive);

                default:
                    return false;
            }
        }

        private void CompleteCurrentStep()
        {
            if (_currentStepIndex >= _stepDone.Length) return;
            _stepDone[_currentStepIndex] = true;

            // Mark slot as filled
            var step = _currentSteps[_currentStepIndex];
            var slot = GetSlotHighlight(step.targetSlotName);
            if (slot != null) slot.SetFilled(true);

            var grabComp = GetGrabbableForStep(step);
            if (grabComp != null) grabComp.SetNeeded(false);

            _totalStepsCompleted = CountCompletedSteps();

            SetFeedback("✓ Step complete!", Color.green);

            // Auto-advance to next
            if (_currentStepIndex < _currentSteps.Count - 1)
            {
                _currentStepIndex++;
                ShowCurrentStep();
            }
            else
            {
                // All done
                if (feedbackText != null)
                    feedbackText.text = "<color=#00FF66><b>🎉 Experiment Complete! All steps done.</b></color>";
                _feedbackClearTimer = 10f;
                ShowCurrentStep();
            }
        }

        private void AdvanceToStep(int idx)
        {
            if (idx < 0 || idx >= _currentSteps.Count) return;
            _currentStepIndex = idx;
            ShowCurrentStep();
        }

        private void ShowBlockedFeedback(int idx)
        {
            if (idx < 0 || idx >= _currentSteps.Count) return;
            var step = _currentSteps[idx];
            string msg = GetBlockedMessage(step);
            SetFeedback("✗ " + msg, new Color(1f, 0.5f, 0.1f));
        }

        private string GetBlockedMessage(GuidedExperimentStep step)
        {
            switch (step.stepType)
            {
                case StepType.PlaceResistor:
                    return string.Format("Place the {0} Ω resistor in slot [{1}] to continue.", step.requiredResistance, step.targetSlotName);
                case StepType.PlaceMeter:
                    bool wantAmm = step.targetSlotName.ToLower().Contains("ammeter") || step.targetSlotName.ToLower().Contains("series");
                    return wantAmm ? "Connect the Ammeter in SERIES before proceeding." : "Connect the Voltmeter in PARALLEL before proceeding.";
                case StepType.ConnectWire:
                    return _isKVL
                        ? "Complete all wire connections to close the series loop."
                        : "Connect the junction node and both branch wires to complete the parallel circuit.";
                case StepType.TogglePower:
                    return "Switch ON the DC Power Supply (press [POWER ON/OFF]).";
                case StepType.CheckCircuit:
                    return "Press [CHECK CIRCUIT] to validate the circuit.";
                case StepType.RecordReading:
                    return "Press [RECORD READING] to save the observation.";
                case StepType.VerifyResults:
                    if (simulator == null || !simulator.isPowerOn)
                        return "Power supply is OFF. Switch ON the power supply first.";
                    if (_isKVL && !simulator.isKVLClosed)
                        return "Circuit is OPEN. Check R1 (100 Ω), R2 (200 Ω), and wire connections.";
                    if (!_isKVL && !simulator.isKCLActive)
                        return "Parallel circuit is OPEN. Check Branch A (200 Ω), Branch B (300 Ω), and junction wires.";
                    if (displayUI == null || (_isKVL ? displayUI.kvlObservations.Count == 0 : displayUI.kclObservations.Count == 0))
                        return "Press [CHECK CIRCUIT] and then [RECORD READING] before verifying.";
                    return _isKVL
                        ? "Press [VERIFY RESULTS] to confirm Vs − V1 − V2 = 0."
                        : "Press [VERIFY RESULTS] to confirm Iin = I1 + I2.";
                default:
                    return "Complete this step to proceed.";
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════

        private bool IsResistorInSocket(string slotName, float requiredRes)
        {
            var socket = GetCircuitSocket(slotName);
            if (socket == null || !socket.hasResistor) return false;
            return Mathf.Approximately(socket.currentResistor.resistance, requiredRes);
        }

        private CircuitSocket GetCircuitSocket(string slotName)
        {
            if (slotName.Contains("R1") || slotName.Contains("KVL_R1")) return circuitSocketR1;
            if (slotName.Contains("R2") || slotName.Contains("KVL_R2")) return circuitSocketR2;
            if (slotName.Contains("RA") || slotName.Contains("KCL_RA")) return circuitSocketRA;
            if (slotName.Contains("RB") || slotName.Contains("KCL_RB")) return circuitSocketRB;
            return null;
        }

        private SnapSlotHighlight GetSlotHighlight(string slotName)
        {
            if (string.IsNullOrEmpty(slotName)) return null;
            if (slotName.Contains("R1") || slotName.Contains("KVL_R1")) return slotR1;
            if (slotName.Contains("R2") || slotName.Contains("KVL_R2")) return slotR2;
            if (slotName.Contains("RA") || slotName.Contains("KCL_RA")) return slotRA;
            if (slotName.Contains("RB") || slotName.Contains("KCL_RB")) return slotRB;
            if (slotName.ToLower().Contains("ammeter") || slotName.ToLower().Contains("series")) return slotAmmeter;
            if (slotName.ToLower().Contains("voltmeter") || slotName.ToLower().Contains("parallel")) return slotVoltmeter;
            return null;
        }

        private GrabbableLabComponent GetGrabbableForStep(GuidedExperimentStep step)
        {
            if (step.stepType == StepType.PlaceResistor)
            {
                if (Mathf.Approximately(step.requiredResistance, 100f)) return grabbable100Ohm;
                if (step.targetSlotName.Contains("KCL_RA") || step.targetObjectName.Contains("Branch"))
                    return grabbable200OhmBranch;
                if (Mathf.Approximately(step.requiredResistance, 200f)) return grabbable200Ohm;
                if (Mathf.Approximately(step.requiredResistance, 300f)) return grabbable300Ohm;
            }
            else if (step.stepType == StepType.PlaceMeter)
            {
                bool wantAmmeter = step.targetSlotName.ToLower().Contains("ammeter") || step.targetSlotName.ToLower().Contains("series");
                return wantAmmeter ? grabbableAmmeter : grabbableVoltmeter;
            }
            return null;
        }

        private void ClearAllHighlights()
        {
            if (slotR1 != null && !slotR1.isFilled) slotR1.SetHighlight(false);
            if (slotR2 != null && !slotR2.isFilled) slotR2.SetHighlight(false);
            if (slotRA != null && !slotRA.isFilled) slotRA.SetHighlight(false);
            if (slotRB != null && !slotRB.isFilled) slotRB.SetHighlight(false);
            if (slotAmmeter != null && !slotAmmeter.isFilled) slotAmmeter.SetHighlight(false);
            if (slotVoltmeter != null && !slotVoltmeter.isFilled) slotVoltmeter.SetHighlight(false);

            if (grabbable100Ohm != null) grabbable100Ohm.SetNeeded(false);
            if (grabbable200Ohm != null) grabbable200Ohm.SetNeeded(false);
            if (grabbable300Ohm != null) grabbable300Ohm.SetNeeded(false);
            if (grabbable200OhmBranch != null) grabbable200OhmBranch.SetNeeded(false);
            if (grabbableAmmeter != null) grabbableAmmeter.SetNeeded(false);
            if (grabbableVoltmeter != null) grabbableVoltmeter.SetNeeded(false);
        }

        private int CountCompletedSteps()
        {
            if (_stepDone == null) return 0;
            int c = 0;
            for (int i = 0; i < _stepDone.Length; i++)
                if (_stepDone[i]) c++;
            return c;
        }

        private void SetFeedback(string msg, Color col)
        {
            if (feedbackText == null) return;
            feedbackText.text = msg;
            feedbackText.color = col;
            _feedbackClearTimer = 4f;
        }

        private string BuildControlHint(GuidedExperimentStep step)
        {
            var sim = FindFirstObjectByType<XRDeviceSimulator>();
            if (sim != null && sim.gameObject.activeInHierarchy && !XRSettings.isDeviceActive)
            {
                string gripKey = sim.gripAction.action != null && sim.gripAction.action.bindings.Count > 0
                    ? sim.gripAction.action.bindings[0].path.Replace("<Keyboard>/", "").ToUpperInvariant()
                    : "G";
                string trigKey = sim.triggerAction.action != null && sim.triggerAction.action.bindings.Count > 0
                    ? sim.triggerAction.action.bindings[0].path.Replace("<Mouse>/", "Mouse ").Replace("leftButton", "Left Click")
                    : "Left Click";
                string manipKey = sim.manipulateRightAction.action != null && sim.manipulateRightAction.action.bindings.Count > 0
                    ? sim.manipulateRightAction.action.bindings[0].path.Replace("<Keyboard>/", "").ToUpperInvariant()
                    : "Space";
                return $"PC Sim: [{gripKey}] Grab/Release | [{trigKey}] UI/Select | [{manipKey}] Hand Mode | [WASD] Move";
            }

            if (XRSettings.isDeviceActive)
            {
                return "VR: [Grip] Grab/Release component | [Trigger] Click UI buttons | [Thumbstick] Move";
            }

            return step.controlHint;
        }

        public void SetWorkflowState(ExperimentWorkflowState newState)
        {
            workflowState = newState;
        }

        public void ResetExperiment()
        {
            isComponentPlacementMode = false;
            workflowState = ExperimentWorkflowState.NOT_STARTED;
            if (circuitSocketR1 != null) circuitSocketR1.ClearResistor();
            if (circuitSocketR2 != null) circuitSocketR2.ClearResistor();
            if (circuitSocketRA != null) circuitSocketRA.ClearResistor();
            if (circuitSocketRB != null) circuitSocketRB.ClearResistor();

            if (slotR1 != null) slotR1.SetFilled(false);
            if (slotR2 != null) slotR2.SetFilled(false);
            if (slotRA != null) slotRA.SetFilled(false);
            if (slotRB != null) slotRB.SetFilled(false);
            if (slotAmmeter != null) slotAmmeter.SetFilled(false);
            if (slotVoltmeter != null) slotVoltmeter.SetFilled(false);
            LoadSteps(_isKVL);
            ClearAllHighlights();
            ShowCurrentStep();
        }
    }
}
