using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;
using System.Collections.Generic;

namespace VRContent.Circuit
{
    [System.Serializable]
    public class KVLObservation
    {
        public int trialNumber;
        public float supplyVoltage;
        public float r1;
        public float r2;
        public float current;
        public float v1;
        public float v2;
        public float voltageSum;
        public float residual;
        public bool isVerified;
        public string timestamp;
    }

    [System.Serializable]
    public class KCLObservation
    {
        public int trialNumber;
        public float supplyVoltage;
        public float r1;
        public float r2;
        public float i1;
        public float i2;
        public float iIn;
        public float outgoingSum;
        public float residual;
        public bool isVerified;
        public string timestamp;
    }

    public class KirchhoffDisplayUI : MonoBehaviour
    {
        [Header("Simulation Reference")]
        public KirchhoffSimulator simulator;

        [Header("Header & Status UI")]
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI theoryIntroText;
        public TextMeshProUGUI circuitStatusText;
        public Image statusIndicatorImage;
        public TextMeshProUGUI instructionsText;
        public TextMeshProUGUI powerStatusText;
        public TextMeshProUGUI modeStatusText;

        [Header("Data Cards UI")]
        public TextMeshProUGUI componentValuesText;
        public TextMeshProUGUI readingsComparisonText;
        public TextMeshProUGUI residualText;
        public TextMeshProUGUI verificationText;
        public Image verificationPanelImage;
        public TextMeshProUGUI observationTableText;

        [Header("Visual Comparison Chart")]
        public RectTransform chartPanel;
        public TextMeshProUGUI chartTitleText;
        public RectTransform bar1Rect;
        public RectTransform bar2Rect;
        public RectTransform bar3Rect;
        public RectTransform bar4Rect;
        public Image bar1Image;
        public Image bar2Image;
        public Image bar3Image;
        public Image bar4Image;
        public TextMeshProUGUI bar1Label;
        public TextMeshProUGUI bar2Label;
        public TextMeshProUGUI bar3Label;
        public TextMeshProUGUI bar4Label;

        [Header("Probe Controls")]
        public Button probe1Button;
        public Button probe2Button;
        public Button probe3Button;
        public TextMeshProUGUI probe1ButtonText;
        public TextMeshProUGUI probe2ButtonText;
        public TextMeshProUGUI probe3ButtonText;

        [Header("Action Controls")]
        public Button startButton;
        public Button checkCircuitButton;
        public Button recordReadingButton;
        public Button verifyResultsButton;
        public Button resetButton;

        [Header("Supply & Mode Controls")]
        public Button powerButton;
        public Button voltageToggleButton;
        public Button modeButton;
        public Button meterWireToggleButton;

        [Header("Observation Datasets")]
        public List<KVLObservation> kvlObservations = new List<KVLObservation>();
        public List<KCLObservation> kclObservations = new List<KCLObservation>();

        [Header("State")]
        public bool hasStarted = false;
        public int currentStep = 1;
        public bool hasValidatedCircuit = false;
        private float _lastModeClickTime = -1f;

        private void EnsureSimulator()
        {
            if (simulator == null)
            {
                simulator = Object.FindFirstObjectByType<KirchhoffSimulator>();
            }
        }

        private void Awake()
        {
            EnsureSimulator();
        }

        private void Start()
        {
            EnsureSimulator();

            if (simulator != null)
            {
                simulator.OnCircuitUpdated += RefreshUI;
            }

            if (CircuitManager.Instance != null)
            {
                CircuitManager.Instance.OnCircuitEvaluated += OnCircuitEvaluatedFromManager;
            }

            if (startButton != null) startButton.onClick.AddListener(StartExperiment);
            if (checkCircuitButton != null) checkCircuitButton.onClick.AddListener(CheckCircuit);
            if (recordReadingButton != null) recordReadingButton.onClick.AddListener(RecordReading);
            if (verifyResultsButton != null) verifyResultsButton.onClick.AddListener(VerifyResults);
            if (resetButton != null) resetButton.onClick.AddListener(ResetExperiment);

            if (powerButton != null) powerButton.onClick.AddListener(OnPowerClicked);
            if (voltageToggleButton != null) voltageToggleButton.onClick.AddListener(OnVoltageClicked);
            if (modeButton != null) modeButton.onClick.AddListener(OnModeClicked);
            if (meterWireToggleButton != null) meterWireToggleButton.onClick.AddListener(ToggleMeterWiring);

            if (probe1Button != null) probe1Button.onClick.AddListener(OnProbe1Clicked);
            if (probe2Button != null) probe2Button.onClick.AddListener(OnProbe2Clicked);
            if (probe3Button != null) probe3Button.onClick.AddListener(OnProbe3Clicked);

            EnsureUIRaycastClean();
            hasValidatedCircuit = false;
            if (verificationText != null)
            {
                verificationText.text = "<color=#00E5FF><b>[NOT STARTED]</b></color> All resistor slots are empty. Click <b>2. CHECK CIRCUIT</b> to begin resistor placement.";
            }
            RefreshUI();
        }

        private void EnsureUIRaycastClean()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.worldCamera == null)
            {
                canvas.worldCamera = Camera.main;
            }

            // Use TrackedDeviceGraphicRaycaster alone (supports both XR ray and PC mouse via XRUIInputModule)
            // and disable redundant GraphicRaycaster so clicks are never double-fired or conflicted.
            var trackedRaycaster = GetComponent<TrackedDeviceGraphicRaycaster>();
            var standardRaycaster = GetComponent<GraphicRaycaster>();
            if (trackedRaycaster != null)
            {
                trackedRaycaster.enabled = true;
                trackedRaycaster.ignoreReversedGraphics = false;
                trackedRaycaster.checkFor3DOcclusion = false;
                trackedRaycaster.checkFor2DOcclusion = false;
                if (standardRaycaster != null)
                    standardRaycaster.enabled = false;
            }

            // Ensure non-interactive texts/backgrounds never block raycasts to buttons
            foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                tmp.raycastTarget = false;
            }
            foreach (var img in GetComponentsInChildren<Image>(true))
            {
                bool isButton = img.GetComponent<Button>() != null || img.GetComponentInParent<Button>() != null;
                img.raycastTarget = isButton;
            }

            if (modeButton != null)
            {
                modeButton.interactable = true;
                var rt = modeButton.GetComponent<RectTransform>();
                if (rt != null && rt.sizeDelta.x < 195f)
                {
                    rt.sizeDelta = new Vector2(195f, Mathf.Max(rt.sizeDelta.y, 34f));
                }
            }
        }

        private void OnCircuitEvaluatedFromManager(CircuitValidationResult val, CircuitCalculationResult calc)
        {
            RefreshUI();
        }

        private void OnDestroy()
        {
            if (simulator != null)
            {
                simulator.OnCircuitUpdated -= RefreshUI;
            }
            if (CircuitManager.Instance != null)
            {
                CircuitManager.Instance.OnCircuitEvaluated -= OnCircuitEvaluatedFromManager;
            }
        }

        public void StartExperiment()
        {
            EnsureSimulator();
            hasStarted = true;
            currentStep = 1;

            if (simulator != null)
            {
                if (simulator.currentMode == CircuitMode.KVL_Series)
                {
                    simulator.SelectKVLExperiment();
                }
                else
                {
                    simulator.SelectKCLExperiment();
                }
                simulator.SetPower(true);

                var ccm = CircuitConnectionManager.Instance;
                if (ccm != null)
                {
                    ccm.SetStandardValidWiring(true, true);
                }
            }

            if (verificationText != null)
            {
                if (simulator != null && simulator.currentMode == CircuitMode.KVL_Series)
                {
                    verificationText.text = "<color=#00E5FF><b>[KVL EXPERIMENT STARTED]</b></color> 6V DC Supply selected. Follow the 10-step student workflow to construct the series circuit, measure drops, and verify KVL.";
                }
                else
                {
                    verificationText.text = "<color=#00E5FF><b>[KCL EXPERIMENT STARTED]</b></color> 12V DC Supply selected. Follow the 10-step student workflow to construct the parallel circuit, measure currents, and verify KCL.";
                }
            }

            RefreshUI();
        }

        public void CheckCircuit()
        {
            EnsureSimulator();
            if (simulator == null) return;

            bool isKVL = simulator.currentMode == CircuitMode.KVL_Series;
            if (!AreRequiredResistorsPlaced(isKVL))
            {
                hasValidatedCircuit = false;
                string placementMsg = GuidedExperimentManager.Instance != null
                    ? GuidedExperimentManager.Instance.BeginComponentPlacementMode()
                    : (isKVL
                        ? "STEP 1: Grab the 100Ω resistor and throw it into the highlighted R1 slot."
                        : "STEP 1: Grab the 200Ω resistor and throw it into the highlighted KCL Branch A slot.");

                if (verificationText != null)
                {
                    verificationText.text = "<color=#00E5FF><b>[COMPONENT PLACEMENT]</b></color>\n" + placementMsg;
                }
                RefreshUI();
                return;
            }

            ValidateCircuitConnectionsAfterPlacement();
        }

        public bool AreRequiredResistorsPlaced(bool isKVL)
        {
            if (simulator == null) return false;
            if (isKVL)
            {
                bool r1Ok = simulator.kvlSocket1 != null && simulator.kvlSocket1.hasResistor &&
                            Mathf.Approximately(simulator.kvlSocket1.currentResistor.resistance, 100f);
                bool r2Ok = simulator.kvlSocket2 != null && simulator.kvlSocket2.hasResistor &&
                            Mathf.Approximately(simulator.kvlSocket2.currentResistor.resistance, 200f);
                return r1Ok && r2Ok;
            }
            else
            {
                bool raOk = simulator.kclSocketA != null && simulator.kclSocketA.hasResistor &&
                            Mathf.Approximately(simulator.kclSocketA.currentResistor.resistance, 200f);
                bool rbOk = simulator.kclSocketB != null && simulator.kclSocketB.hasResistor &&
                            Mathf.Approximately(simulator.kclSocketB.currentResistor.resistance, 300f);
                return raOk && rbOk;
            }
        }

        public void ValidateCircuitConnectionsAfterPlacement()
        {
            EnsureSimulator();
            if (simulator == null) return;
            simulator.RecalculateCircuit();

            if (CircuitManager.Instance != null)
            {
                CircuitManager.Instance.EvaluateCircuit();
            }

            var ccm = CircuitConnectionManager.Instance;
            var val = CircuitManager.Instance != null ? CircuitManager.Instance.lastValidation : null;

            bool isPowerOn = val != null ? val.isPowerOn : simulator.isPowerOn;
            bool hasShort = val != null ? val.hasShortCircuit : (ccm != null && ccm.hasShortCircuit);
            bool ammeterInSeries = val != null ? val.isAmmeterInSeries : (ccm != null && ccm.isAmmeterInSeries);
            bool voltmeterInParallel = val != null ? val.isVoltmeterInParallel : (ccm != null && ccm.isVoltmeterInParallel);

            bool isValid = val != null ? val.isValid : ((simulator.currentMode == CircuitMode.KVL_Series)
                ? simulator.isKVLClosed
                : simulator.isKCLActive);

            string diagnostic = "";
            hasValidatedCircuit = false;
            if (!AreRequiredResistorsPlaced(simulator.currentMode == CircuitMode.KVL_Series))
            {
                diagnostic = "<color=#FFAA00><b>[CHECK FAILED] REQUIRED RESISTORS MISSING!</b></color>\n" +
                    "Place all required resistors into their highlighted slots first.";
            }
            else if (!isPowerOn)
            {
                diagnostic = "<color=#FF4444><b>[CHECK FAILED] Power Supply is switched OFF!</b></color>\n" +
                    (val != null ? val.diagnosticMessage : "Turn ON the DC supply switch to energize the circuit.");
            }
            else if (hasShort)
            {
                diagnostic = "<color=#FF0000><b>[CHECK FAILED] CRITICAL SHORT CIRCUIT DETECTED!</b></color>\n" +
                    (val != null ? val.diagnosticMessage : "Direct low-resistance path between (+) and (-). Remove shorting wire immediately.");
            }
            else if (!isValid)
            {
                string openReason = val != null ? val.diagnosticMessage : (simulator.currentMode == CircuitMode.KVL_Series ? simulator.kvlGuidanceMessage : simulator.kclGuidanceMessage);
                diagnostic = "<color=#FFAA00><b>[CHECK FAILED] OPEN CIRCUIT DETECTED!</b></color>\n" + openReason;
            }
            else if (!ammeterInSeries)
            {
                diagnostic = "<color=#FFAA00><b>[CHECK FAILED] AMMETER NOT IN SERIES!</b></color>\n" +
                    "Connect the Ammeter in SERIES with the incoming current before validating.";
            }
            else
            {
                hasValidatedCircuit = true;
                if (GuidedExperimentManager.Instance != null)
                    GuidedExperimentManager.Instance.SetWorkflowState(ExperimentWorkflowState.VALIDATED);
                diagnostic = "<color=#00FF66><b>[CHECK PASSED] VALID - CLOSED CIRCUIT!</b></color>\n" +
                    "• Resistors Placed & Topology Validated\n" +
                    "• Ammeter Placement: <color=#00FF66>[OK: Series Connected]</color>\n" +
                    "Click <b>Record Reading</b> to capture observation row, then <b>Verify Results</b>.";
                currentStep = 3;
            }

            if (verificationText != null)
            {
                verificationText.text = diagnostic;
            }

            RefreshUI();
        }

        public void RecordReading()
        {
            EnsureSimulator();
            if (simulator == null) return;
            simulator.RecalculateCircuit();

            var ccm = CircuitConnectionManager.Instance;
            bool isCircuitValid = (simulator.currentMode == CircuitMode.KVL_Series)
                ? simulator.isKVLClosed
                : simulator.isKCLActive;

            bool ammeterInSeries = ccm != null && ccm.isAmmeterInSeries;
            bool voltmeterInParallel = ccm != null && ccm.isVoltmeterInParallel;

            if (!simulator.isPowerOn || !isCircuitValid)
            {
                if (verificationText != null)
                {
                    verificationText.text = "<color=#FF4444><b>[RECORDING BLOCKED]</b> Cannot record data from an open, shorted, or de-energized circuit.\n" +
                        "Ensure circuit is fully wired, energized, and valid before recording observations.</color>";
                }
                return;
            }

            string timeStr = System.DateTime.Now.ToString("HH:mm:ss");

            if (simulator.currentMode == CircuitMode.KVL_Series)
            {
                kvlObservations.Clear();
                var row = new KVLObservation
                {
                    trialNumber = 1,
                    supplyVoltage = simulator.sourceVoltage,
                    r1 = simulator.kvlR1,
                    r2 = simulator.kvlR2,
                    current = simulator.kvlLoopCurrent,
                    v1 = simulator.kvlV1,
                    v2 = simulator.kvlV2,
                    voltageSum = simulator.kvlSumDrops,
                    residual = simulator.kvlResidual,
                    isVerified = (simulator.kvlResidual <= 0.005f),
                    timestamp = timeStr
                };
                kvlObservations.Add(row);

                if (verificationText != null)
                {
                    verificationText.text = string.Format(
                        "<color=#00FF66><b>[RECORDED KVL TRIAL #{0}]</b></color>\n" +
                        "Vs={1:F1}V | R1={2:F0}Ω, R2={3:F0}Ω | I={4:F4}A | V1={5:F2}V, V2={6:F2}V | Sum={7:F2}V | Res={8:F4}V\n" +
                        "Row added to KVL Observation Table. Click <b>Verify Results</b> to evaluate law.",
                        row.trialNumber, row.supplyVoltage, row.r1, row.r2, row.current, row.v1, row.v2, row.voltageSum, row.residual);
                }
            }
            else // KCL
            {
                kclObservations.Clear();
                var row = new KCLObservation
                {
                    trialNumber = 1,
                    supplyVoltage = simulator.sourceVoltage,
                    r1 = simulator.kclResistanceA,
                    r2 = simulator.kclResistanceB,
                    i1 = simulator.kclCurrentA,
                    i2 = simulator.kclCurrentB,
                    iIn = simulator.kclTotalCurrentIn,
                    outgoingSum = simulator.kclCurrentA + simulator.kclCurrentB,
                    residual = simulator.kclDeltaI,
                    isVerified = (simulator.kclDeltaI <= 0.005f),
                    timestamp = timeStr
                };
                kclObservations.Add(row);

                if (verificationText != null)
                {
                    verificationText.text = string.Format(
                        "<color=#00FF66><b>[RECORDED KCL TRIAL #{0}]</b></color>\n" +
                        "Vs={1:F1}V | R1={2:F0}Ω, R2={3:F0}Ω | I1={4:F4}A, I2={5:F4}A | Iin={6:F4}A | OutSum={7:F4}A | Res={8:F4}A\n" +
                        "Row added to KCL Observation Table. Click <b>Verify Results</b> to evaluate law.",
                        row.trialNumber, row.supplyVoltage, row.r1, row.r2, row.i1, row.i2, row.iIn, row.outgoingSum, row.residual);
                }
            }

            currentStep = 4;
            if (GuidedExperimentManager.Instance != null)
                GuidedExperimentManager.Instance.SetWorkflowState(ExperimentWorkflowState.READING_RECORDED);
            RefreshUI();
        }

        public void VerifyResults()
        {
            EnsureSimulator();
            if (simulator == null) return;
            simulator.RecalculateCircuit();

            var ccm = CircuitConnectionManager.Instance;
            bool isPowerOn = simulator.isPowerOn;
            bool hasShort = ccm != null && ccm.hasShortCircuit;
            bool ammeterInSeries = ccm != null && ccm.isAmmeterInSeries;
            bool voltmeterInParallel = ccm != null && ccm.isVoltmeterInParallel;

            bool isCircuitValid = (simulator.currentMode == CircuitMode.KVL_Series)
                ? simulator.isKVLClosed
                : simulator.isKCLActive;

            // Handle Failures with clear explanations
            if (!isPowerOn)
            {
                if (verificationText != null)
                {
                    verificationText.text = "<color=#FF4444><b>[VERIFICATION FAILED: POWER OFF]</b></color>\n" +
                        "• Root Cause: The DC Power Supply is switched OFF.\n" +
                        "• Explanation: Kirchhoff's laws require active electromotive force (EMF) to generate current and potential drops. Switch ON the power supply.";
                }
                return;
            }

            if (hasShort)
            {
                if (verificationText != null)
                {
                    verificationText.text = "<color=#FF2222><b>[VERIFICATION FAILED: SHORT CIRCUIT]</b></color>\n" +
                        "• Root Cause: Supply (+) connects directly to Supply (-).\n" +
                        "• Explanation: Near-zero circuit impedance causes safety shutdown. Potential across load drops to zero, violating steady-state testing conditions.";
                }
                return;
            }

            if (!isCircuitValid)
            {
                if (verificationText != null)
                {
                    verificationText.text = "<color=#FFAA00><b>[VERIFICATION FAILED: OPEN CIRCUIT]</b></color>\n" +
                        "• Root Cause: Discontinuous electrical pathway.\n" +
                        "• Explanation: A complete closed path is mandatory for current flow. Check resistor seating and ensure wire banana plugs are snapped in.";
                }
                return;
            }

            // Handle Successful Verification
            if (simulator.currentMode == CircuitMode.KVL_Series)
            {
                float vs = simulator.sourceVoltage;
                float v1 = simulator.kvlV1;
                float v2 = simulator.kvlV2;
                float sum = simulator.kvlSumDrops;
                float residual = simulator.kvlResidual;

                if (residual <= 0.005f)
                {
                    if (verificationText != null)
                    {
                        verificationText.text = "<color=#00FF66><b>[KVL VERIFIED - PASSED] VALID CLOSED LOOP Confirmed!</b></color>\n" +
                            string.Format("• KVL Loop Equation: <b>Vs - V1 - V2 = 0</b>  =>  {0:F2} V - {1:F2} V - {2:F2} V = {3:F4} V\n", vs, v1, v2, residual) +
                            string.Format("• Total Voltage Drop: V1 ({0:F2} V) + V2 ({1:F2} V) = {2:F2} V (Matches Supply Vs = {3:F2} V)\n", v1, v2, sum, vs) +
                            string.Format("• Loop Current: I = {0:F4} A ({1:F1} mA) through R_total = {2:F0} Ω\n", simulator.kvlLoopCurrent, simulator.kvlLoopCurrent * 1000f, simulator.kvlTotalResistance) +
                            "• Scientific Principle: Electric potential difference around any closed loop must sum to zero. Conservation of energy verified.";
                    }
                }
                else
                {
                    if (verificationText != null)
                    {
                        verificationText.text = string.Format(
                            "<color=#FF4444><b>[KVL FAILED: RESIDUAL ERROR]</b></color>\n" +
                            "Calculated residual {0:F4} V exceeds acceptable tolerance (0.005 V). Check component values and socket seating.", residual);
                    }
                }
            }
            else // KCL
            {
                float iIn = simulator.kclTotalCurrentIn;
                float i1 = simulator.kclCurrentA;
                float i2 = simulator.kclCurrentB;
                float sumOut = i1 + i2;
                float residual = simulator.kclDeltaI;

                if (residual <= 0.005f)
                {
                    if (verificationText != null)
                    {
                        verificationText.text = "<color=#00FF66><b>[KCL VERIFIED - PASSED] Conservation of Charge Confirmed!</b></color>\n" +
                            string.Format("• KCL Node Equation: <b>Iin = I1 + I2</b>  =>  {0:F4} A = {1:F4} A + {2:F4} A  (Residual: {3:F4} A)\n", iIn, i1, i2, residual) +
                            string.Format("• Current Balance: Incoming Inflow ({0:F4} A) = Outgoing Branch Sum ({1:F4} A)\n", iIn, sumOut) +
                            "• Scientific Principle: Electric charge cannot accumulate or be destroyed at a circuit node. Total current entering equals total current leaving.";
                    }
                }
                else
                {
                    if (verificationText != null)
                    {
                        verificationText.text = string.Format(
                            "<color=#FF4444><b>[KCL FAILED: RESIDUAL ERROR]</b></color>\n" +
                            "Calculated current residual {0:F4} A exceeds tolerance (0.005 A). Check branch resistances and node connections.", residual);
                    }
                }
            }

            currentStep = 5;
            if (GuidedExperimentManager.Instance != null)
                GuidedExperimentManager.Instance.SetWorkflowState(ExperimentWorkflowState.VERIFIED);
            RefreshUI();
        }

        public void ResetExperiment()
        {
            EnsureSimulator();
            hasStarted = false;
            hasValidatedCircuit = false;
            currentStep = 1;

            kvlObservations.Clear();
            kclObservations.Clear();

            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                ccm.ResetConnections();
            }

            if (simulator != null)
            {
                simulator.RecalculateCircuit();
            }

            if (verificationText != null)
            {
                verificationText.text = "<color=#FFAA00><b>[RESET COMPLETE]</b> Observations, active readings, and wire connections have been cleared.\n" +
                    "Click <b>1. START</b> or wire circuit to begin a new laboratory experiment.</color>";
            }

            if (GuidedExperimentManager.Instance != null)
            {
                GuidedExperimentManager.Instance.ResetExperiment();
            }

            RefreshUI();
        }

        public void OnPowerClicked()
        {
            EnsureSimulator();
            if (simulator != null)
            {
                simulator.TogglePower();
            }
            RefreshUI();
        }

        public void OnVoltageClicked()
        {
            EnsureSimulator();
            if (simulator != null)
            {
                simulator.ToggleVoltage();
            }
            RefreshUI();
        }

        public void OnModeClicked()
        {
            if (Time.unscaledTime - _lastModeClickTime < 0.15f) return;
            _lastModeClickTime = Time.unscaledTime;

            EnsureSimulator();
            if (simulator != null)
            {
                hasValidatedCircuit = false;
                kvlObservations.Clear();
                kclObservations.Clear();

                simulator.ToggleMode();
                bool isKVL = simulator.currentMode == CircuitMode.KVL_Series;
                if (GuidedExperimentManager.Instance != null)
                {
                    GuidedExperimentManager.Instance.SwitchMode(isKVL);
                }

                if (verificationText != null)
                {
                    verificationText.text = isKVL
                        ? "<color=#00E5FF><b>[MODE SWITCHED: KVL — 6V SERIES]</b></color>\nPrevious placement reset. Place 100Ω (R1) and 200Ω (R2) in series."
                        : "<color=#00E5FF><b>[MODE SWITCHED: KCL — 12V PARALLEL]</b></color>\nPrevious placement reset. Place 200Ω (Branch A) and 300Ω (Branch B) in parallel.";
                }
            }
            RefreshUI();
        }

        public void ToggleMeterWiring()
        {
            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                bool wantMeters = !ccm.isAmmeterInSeries || !ccm.isVoltmeterInParallel;
                ccm.SetStandardValidWiring(wantMeters, wantMeters);

                if (verificationText != null)
                {
                    verificationText.text = wantMeters
                        ? "<color=#00FF66><b>[METERS WIRED]</b> Ammeter placed in series; Voltmeter placed in parallel.</color>"
                        : "<color=#FFAA00><b>[METERS DISCONNECTED]</b> Meters removed. Readings are now unavailable (---).</color>";
                }
            }
        }

        public void OnProbe1Clicked()
        {
            EnsureSimulator();
            if (simulator == null) return;
            if (simulator.currentMode == CircuitMode.KVL_Series)
                simulator.MeasureVoltageAcrossR1();
            else
                simulator.MeasureCurrentMainInflow();
            RefreshUI();
        }

        public void OnProbe2Clicked()
        {
            EnsureSimulator();
            if (simulator == null) return;
            if (simulator.currentMode == CircuitMode.KVL_Series)
                simulator.MeasureVoltageAcrossR2();
            else
                simulator.MeasureCurrentBranch1();
            RefreshUI();
        }

        public void OnProbe3Clicked()
        {
            EnsureSimulator();
            if (simulator == null) return;
            if (simulator.currentMode == CircuitMode.KVL_Series)
                simulator.MeasureVoltageAcrossTotal();
            else
                simulator.MeasureCurrentBranch2();
            RefreshUI();
        }

        public void RefreshUI()
        {
            EnsureSimulator();
            if (simulator == null) return;

            var ccm = CircuitConnectionManager.Instance;
            bool isPowerOn = simulator.isPowerOn;
            bool hasShort = ccm != null && ccm.hasShortCircuit;
            bool ammeterInSeries = ccm != null && ccm.isAmmeterInSeries;
            bool voltmeterInParallel = ccm != null && ccm.isVoltmeterInParallel;

            bool isKVL = simulator.currentMode == CircuitMode.KVL_Series;
            bool isCircuitValid = isKVL ? simulator.isKVLClosed : simulator.isKCLActive;
            bool resistorsPlaced = AreRequiredResistorsPlaced(isKVL);
            bool allConnectedAndVerified = isPowerOn && !hasShort && isCircuitValid && resistorsPlaced && ammeterInSeries;
            if (!allConnectedAndVerified) hasValidatedCircuit = false;

            if (recordReadingButton != null)
                recordReadingButton.interactable = hasValidatedCircuit && allConnectedAndVerified;
            if (verifyResultsButton != null)
                verifyResultsButton.interactable = hasValidatedCircuit && allConnectedAndVerified;

            // 1. Title
            if (titleText != null)
            {
                titleText.text = isKVL
                    ? "KIRCHHOFF'S VOLTAGE LAW (KVL) EXPERIMENT"
                    : "KIRCHHOFF'S CURRENT LAW (KCL) EXPERIMENT";
            }

            // 2. Short Theoretical Introduction
            if (theoryIntroText != null)
            {
                if (isKVL)
                {
                    theoryIntroText.text =
                        "<b>Theoretical Principle (KVL):</b> The algebraic sum of all potential differences (voltages) around any closed loop must equal zero: " +
                        "<color=#00E5FF><b>Vs - V1 - V2 = 0</b></color>. This directly manifests the <i>Conservation of Energy</i>.";
                }
                else
                {
                    theoryIntroText.text =
                        "<b>Theoretical Principle (KCL):</b> The algebraic sum of currents entering and leaving any electrical node must equal zero: " +
                        "<color=#00E5FF><b>Iin = I1 + I2</b></color> (or <b>|Iin - (I1 + I2)| = 0</b>). This directly manifests the <i>Conservation of Electric Charge</i>.";
                }
            }

            // 3. Circuit Status Banner & Indicators
            if (circuitStatusText != null)
            {
                if (!isPowerOn)
                {
                    circuitStatusText.text = "<color=#888888>[CIRCUIT STATUS: POWER OFF - STANDBY]</color>";
                }
                else if (hasShort)
                {
                    circuitStatusText.text = "<color=#FF2222>[CIRCUIT STATUS: CRITICAL SHORT CIRCUIT DETECTED]</color>";
                }
                else if (!resistorsPlaced || !isCircuitValid || !ammeterInSeries)
                {
                    circuitStatusText.text = "<color=#FF9900>[CIRCUIT STATUS: OPEN / INCOMPLETE CIRCUIT]</color>";
                }
                else
                {
                    circuitStatusText.text = "<color=#00FF66><b>[CIRCUIT STATUS: VALID - CLOSED CIRCUIT]</b></color>";
                }
            }

            if (statusIndicatorImage != null)
            {
                if (!isPowerOn)
                    statusIndicatorImage.color = new Color(0.5f, 0.5f, 0.5f);
                else if (hasShort || !isCircuitValid)
                    statusIndicatorImage.color = new Color(1f, 0.2f, 0.2f);
                else
                    statusIndicatorImage.color = new Color(0.1f, 1f, 0.4f);
            }

            // 4. Power & Mode text
            if (powerStatusText != null)
            {
                string pwr = isPowerOn ? "<color=#00FF66>ON</color>" : "<color=#FF3333>OFF</color>";
                powerStatusText.text = string.Format("Supply: <b>{0}</b> | Voltage: <b>{1:F0} V DC</b>", pwr, simulator.sourceVoltage);
            }

            if (modeStatusText != null)
            {
                string modeStr = isKVL ? "KVL (Series — 6V)" : "KCL (Parallel — 12V)";
                modeStatusText.text = "<color=#00E5FF><b>ACTIVE MODE: " + modeStr + "</b></color>";
            }

            if (modeButton != null)
            {
                var btnTmp = modeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnTmp != null)
                    btnTmp.text = isKVL ? "SWITCH TO KCL (12V)" : "SWITCH TO KVL (6V)";
            }

            // 5. Short Numbered Steps + Current Step & Next Action
            if (instructionsText != null)
            {
                string nextAction = GetNextActionText(isKVL, isPowerOn, isCircuitValid);
                if (isKVL)
                {
                    instructionsText.text =
                        "<color=#00E5FF><b>KVL Steps (6V Series):</b></color>\n" +
                        "1. Grab (G) & throw (W) R1 (100 Ohm) and R2 (200 Ohm) into series slots.\n" +
                        "2. Connect circuit wires & Ammeter in series.\n" +
                        "3. Switch ON 6V Supply.\n" +
                        "4. Click Check Circuit -> Record Reading -> Verify Results.\n" +
                        "<color=#FFD54F><b>Next Action:</b> " + nextAction + "</color>";
                }
                else
                {
                    instructionsText.text =
                        "<color=#00E5FF><b>KCL Steps (12V Parallel):</b></color>\n" +
                        "1. Grab (G) & throw (W) 200 Ohm (Branch A) & 300 Ohm (Branch B).\n" +
                        "2. Connect parallel junction wires & Ammeter in series.\n" +
                        "3. Switch ON 12V Supply.\n" +
                        "4. Click Check Circuit -> Record Reading -> Verify Results.\n" +
                        "<color=#FFD54F><b>Next Action:</b> " + nextAction + "</color>";
                }
            }

            // 6. Selected Voltage & Resistor Parameters Card
            if (componentValuesText != null)
            {
                if (isKVL)
                {
                    componentValuesText.text =
                        "<color=#00E5FF><b>[COMPONENT PARAMETERS]</b></color>\n" +
                        string.Format("• Source Voltage (Vs): <b>{0:F1} V</b>\n", simulator.sourceVoltage) +
                        string.Format("• Resistor R1: <b>{0:F0} Ohm</b> | R2: <b>{1:F0} Ohm</b>\n", simulator.kvlR1, simulator.kvlR2) +
                        string.Format("• Total Series Resistance (R1 + R2): <b>{0:F1} Ohm</b>", simulator.kvlTotalResistance);
                }
                else
                {
                    componentValuesText.text =
                        "<color=#00E5FF><b>[COMPONENT PARAMETERS]</b></color>\n" +
                        string.Format("• Source Voltage (Vs): <b>{0:F1} V</b>\n", simulator.sourceVoltage) +
                        string.Format("• Branch R1: <b>{0:F0} Ohm</b> | Branch R2: <b>{1:F0} Ohm</b>\n", simulator.kclResistanceA, simulator.kclResistanceB) +
                        string.Format("• Equivalent Resistance (R1 || R2): <b>{0:F1} Ohm</b>", simulator.kclEquivalentResistance);
                }
            }

            // 7. Measurement Displays (hide final readings until resistors are placed and circuit is valid)
            if (readingsComparisonText != null)
            {
                if (!resistorsPlaced || !isCircuitValid)
                {
                    readingsComparisonText.text =
                        "<color=#00E5FF><b>[THEORETICAL vs CALCULATED READINGS]</b></color>\n" +
                        "• Place all required resistors and validate the circuit to compute readings.\n" +
                        "• Current: <b>---</b> | Voltage Drops: <b>---</b>";
                }
                else if (isKVL)
                {
                    string ammStr = (isCircuitValid && ammeterInSeries)
                        ? string.Format("{0:F4} A ({1:F1} mA)", simulator.kvlLoopCurrent, simulator.kvlLoopCurrent * 1000f)
                        : "--- A (Not in series)";

                    readingsComparisonText.text =
                        "<color=#00E5FF><b>[THEORETICAL vs CALCULATED READINGS]</b></color>\n" +
                        string.Format("• Theo Loop Current: <b>{0:F4} A</b> | Ammeter: <b>{1}</b>\n", simulator.kvlLoopCurrent, ammStr) +
                        string.Format("• Theo Drop V1: <b>{0:F2} V</b> | Drop V2: <b>{1:F2} V</b>\n", simulator.kvlV1, simulator.kvlV2) +
                        string.Format("• Calculated Drops: V1=<b>{0:F2} V</b>, V2=<b>{1:F2} V</b> | Sum=<b>{2:F2} V</b>", simulator.kvlV1, simulator.kvlV2, simulator.kvlSumDrops);
                }
                else
                {
                    string ammStr = (isCircuitValid && ammeterInSeries)
                        ? string.Format("{0:F4} A ({1:F1} mA)", simulator.kclTotalCurrentIn, simulator.kclTotalCurrentIn * 1000f)
                        : "--- A (Not in series)";

                    readingsComparisonText.text =
                        "<color=#00E5FF><b>[THEORETICAL vs CALCULATED READINGS]</b></color>\n" +
                        string.Format("• Theo Inflow (Iin): <b>{0:F4} A</b> | Ammeter: <b>{1}</b>\n", simulator.kclTotalCurrentIn, ammStr) +
                        string.Format("• Theo I1: <b>{0:F4} A</b> | Theo I2: <b>{1:F4} A</b>\n", simulator.kclCurrentA, simulator.kclCurrentB) +
                        string.Format("• Calc Branches: I1=<b>{0:F4} A</b>, I2=<b>{1:F4} A</b> | Vs=<b>{2:F1} V</b>", simulator.kclCurrentA, simulator.kclCurrentB, simulator.sourceVoltage);
                }
            }

            // 8. Residual & Equation Card
            if (residualText != null)
            {
                if (isKVL)
                {
                    string resVal = (resistorsPlaced && isCircuitValid)
                        ? string.Format("<color=#00FF66><b>{0:F4} V</b></color>", simulator.kvlResidual)
                        : "<color=#888888><b>--- (Pending validation)</b></color>";
                    residualText.text =
                        "<color=#00E5FF><b>[RESIDUAL CALCULATION (KVL)]</b></color>\n" +
                        "Equation: <b>Vs - (V1 + V2) = 0</b>\n" +
                        string.Format("Residual Value: {0} (Ideal: 0.00 V)", resVal);
                }
                else
                {
                    string resVal = (resistorsPlaced && isCircuitValid)
                        ? string.Format("<color=#00FF66><b>{0:F4} A</b></color>", simulator.kclDeltaI)
                        : "<color=#888888><b>--- (Pending validation)</b></color>";
                    residualText.text =
                        "<color=#00E5FF><b>[RESIDUAL CALCULATION (KCL)]</b></color>\n" +
                        "Equation: <b>|Iin - (I1 + I2)| = 0</b>\n" +
                        string.Format("Residual Value: {0} (Ideal: 0.00 A)", resVal);
                }
            }

            // 9. Update Probe Button Labels
            if (probe1ButtonText != null)
            {
                probe1ButtonText.text = isKVL ? "MEASURE V1 (ACROSS R1)" : "MEASURE Iin (MAIN INFLOW)";
            }
            if (probe2ButtonText != null)
            {
                probe2ButtonText.text = isKVL ? "MEASURE V2 (ACROSS R2)" : "MEASURE I1 (BRANCH 1 - 200 Ohm)";
            }
            if (probe3ButtonText != null)
            {
                probe3ButtonText.text = isKVL ? "MEASURE VS (TOTAL LOOP)" : "MEASURE I2 (BRANCH 2 - 300 Ohm)";
            }

            // 10. Update Visual Comparison Chart
            UpdateComparisonChart(isKVL, isCircuitValid);

            // 11. Update Observation Table View (Guaranteed no duplicate rows)
            UpdateObservationTableView(isKVL);
        }

        private void UpdateComparisonChart(bool isKVL, bool isCircuitValid)
        {
            float maxBarWidth = 240f;

            if (isKVL)
            {
                if (chartTitleText != null) chartTitleText.text = "VOLTAGE COMPARISON CHART (KVL: Vs vs Drops)";

                float vs = isCircuitValid ? simulator.sourceVoltage : 6f;
                float v1 = isCircuitValid ? simulator.kvlV1 : 0f;
                float v2 = isCircuitValid ? simulator.kvlV2 : 0f;
                float sum = isCircuitValid ? simulator.kvlSumDrops : 0f;
                float maxV = Mathf.Max(vs, 1f);

                SetBar(bar1Rect, bar1Label, "Vs (Supply)", vs, maxV, maxBarWidth, string.Format("{0:F1}V", vs));
                SetBar(bar2Rect, bar2Label, "V1 (Drop 1)", v1, maxV, maxBarWidth, string.Format("{0:F1}V", v1));
                SetBar(bar3Rect, bar3Label, "V2 (Drop 2)", v2, maxV, maxBarWidth, string.Format("{0:F1}V", v2));
                SetBar(bar4Rect, bar4Label, "V1+V2 (Sum)", sum, maxV, maxBarWidth, string.Format("{0:F1}V", sum));
            }
            else // KCL
            {
                if (chartTitleText != null) chartTitleText.text = "BRANCH-CURRENT CHART (KCL: Iin vs Branches)";

                float iIn = isCircuitValid ? simulator.kclTotalCurrentIn : 0.1f;
                float i1 = isCircuitValid ? simulator.kclCurrentA : 0f;
                float i2 = isCircuitValid ? simulator.kclCurrentB : 0f;
                float sumOut = isCircuitValid ? (i1 + i2) : 0f;
                float maxI = Mathf.Max(iIn, 0.01f);

                SetBar(bar1Rect, bar1Label, "Iin (Node In)", iIn * 1000f, maxI * 1000f, maxBarWidth, string.Format("{0:F1}mA", iIn * 1000f));
                SetBar(bar2Rect, bar2Label, "I1 (Branch 1)", i1 * 1000f, maxI * 1000f, maxBarWidth, string.Format("{0:F1}mA", i1 * 1000f));
                SetBar(bar3Rect, bar3Label, "I2 (Branch 2)", i2 * 1000f, maxI * 1000f, maxBarWidth, string.Format("{0:F1}mA", i2 * 1000f));
                SetBar(bar4Rect, bar4Label, "I1+I2 (Sum)", sumOut * 1000f, maxI * 1000f, maxBarWidth, string.Format("{0:F1}mA", sumOut * 1000f));
            }
        }

        private string GetNextActionText(bool isKVL, bool isPowerOn, bool isCircuitValid)
        {
            var mgr = GuidedExperimentManager.Instance;
            if (mgr != null)
            {
                string stepHint = mgr.GetCurrentActionSummary();
                if (!string.IsNullOrEmpty(stepHint)) return stepHint;
            }

            if (!isPowerOn)
                return isKVL ? "Switch ON 6V Power Supply." : "Switch ON 12V Power Supply.";
            if (!isCircuitValid)
                return isKVL ? "Complete series resistors/wires & click Check Circuit." : "Complete parallel resistors/junction & click Check Circuit.";
            if ((isKVL ? kvlObservations.Count : kclObservations.Count) == 0)
                return "Click [3. RECORD READING] to capture observation.";
            return "Click [4. VERIFY RESULTS] to confirm the law.";
        }

        private void SetBar(RectTransform rect, TextMeshProUGUI label, string name, float val, float maxVal, float maxW, string valStr)
        {
            if (rect != null)
            {
                float frac = Mathf.Clamp01(val / Mathf.Max(maxVal, 0.001f));
                rect.sizeDelta = new Vector2(Mathf.Max(frac * maxW, 6f), rect.sizeDelta.y);
            }
            if (label != null)
            {
                label.text = string.Format("{0}: <b>{1}</b>", name, valStr);
            }
        }

        private void UpdateObservationTableView(bool isKVL)
        {
            if (observationTableText == null) return;

            if (isKVL)
            {
                string header = "<color=#00E5FF><b>[KVL OBSERVATION TABLE]</b></color>\n" +
                    "<color=#00E5FF><size=13># | Supply Voltage | R1 | R2 | Current | V1 | V2 | Voltage Sum | Residual | Status</size></color>\n";

                if (kvlObservations.Count == 0)
                {
                    observationTableText.text = header + "<color=#888888><i>No KVL rows recorded. Complete series circuit and click 'Record Reading'.</i></color>";
                    return;
                }

                string rows = header;
                for (int i = 0; i < kvlObservations.Count; i++)
                {
                    var o = kvlObservations[i];
                    string status = o.isVerified ? "<color=#00FF66><b>PASS</b></color>" : "<color=#FF4444><b>FAIL</b></color>";
                    rows += string.Format("<size=13>#{0} | {1:F1} V | {2:F0} Ω | {3:F0} Ω | {4:F4} A | {5:F2} V | {6:F2} V | {7:F2} V | {8:F4} V | {9}</size>\n",
                        o.trialNumber, o.supplyVoltage, o.r1, o.r2, o.current, o.v1, o.v2, o.voltageSum, o.residual, status);
                }
                observationTableText.text = rows;
            }
            else // KCL
            {
                string header = "<color=#00E5FF><b>[KCL OBSERVATION TABLE]</b></color>\n" +
                    "<color=#00E5FF><size=13># | Supply Voltage | R1 | R2 | I1 | I2 | Iin | Outgoing Current | Residual | Status</size></color>\n";

                if (kclObservations.Count == 0)
                {
                    observationTableText.text = header + "<color=#888888><i>No KCL rows recorded. Complete parallel branches and click 'Record Reading'.</i></color>";
                    return;
                }

                string rows = header;
                for (int i = 0; i < kclObservations.Count; i++)
                {
                    var o = kclObservations[i];
                    string status = o.isVerified ? "<color=#00FF66><b>PASS</b></color>" : "<color=#FF4444><b>FAIL</b></color>";
                    rows += string.Format("<size=13>#{0} | {1:F1} V | {2:F0} Ω | {3:F0} Ω | {4:F4} A | {5:F4} A | {6:F4} A | {7:F4} A | {8:F4} A | {9}</size>\n",
                        o.trialNumber, o.supplyVoltage, o.r1, o.r2, o.i1, o.i2, o.iIn, o.outgoingSum, o.residual, status);
                }
                observationTableText.text = rows;
            }
        }
    }
}
