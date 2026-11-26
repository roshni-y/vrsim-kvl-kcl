using UnityEngine;
using System.Collections.Generic;
using System;

namespace VRContent.Circuit
{
    [DisallowMultipleComponent]
    public class CircuitManager : MonoBehaviour
    {
        private static CircuitManager _instance;
        public static CircuitManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = UnityEngine.Object.FindFirstObjectByType<CircuitManager>();
                }
                return _instance;
            }
        }

        [Header("Mode & Power")]
        public CircuitMode currentMode = CircuitMode.KVL_Series;

        [Header("System Submodules")]
        public CircuitValidator validator;
        public ElectricalCalculator calculator;

        [Header("Discovered Scene Elements")]
        public ElectricalComponent powerSupply;
        public List<ElectricalComponent> allComponents = new List<ElectricalComponent>();
        public List<ElectricalTerminal> allTerminals = new List<ElectricalTerminal>();
        public List<WireConnection> allConnections = new List<WireConnection>();
        public List<CircuitSocket> kvlSockets = new List<CircuitSocket>();
        public List<CircuitSocket> kclSockets = new List<CircuitSocket>();

        [Header("Latest Results")]
        public CircuitValidationResult lastValidation;
        public CircuitCalculationResult lastCalculation;

        public event Action<CircuitValidationResult, CircuitCalculationResult> OnCircuitEvaluated;

        private void Awake()
        {
            _instance = this;
            if (validator == null) validator = GetComponent<CircuitValidator>();
            if (validator == null) validator = gameObject.AddComponent<CircuitValidator>();

            if (calculator == null) calculator = GetComponent<ElectricalCalculator>();
            if (calculator == null) calculator = gameObject.AddComponent<ElectricalCalculator>();
        }

        private void Start()
        {
            DiscoverSceneElements();
            EvaluateCircuit();
        }

        public void DiscoverSceneElements()
        {
            allComponents.Clear();
            allComponents.AddRange(UnityEngine.Object.FindObjectsByType<ElectricalComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            foreach (var comp in allComponents)
            {
                if (comp != null) comp.CollectTerminals();
            }

            allTerminals.Clear();
            allTerminals.AddRange(UnityEngine.Object.FindObjectsByType<ElectricalTerminal>(FindObjectsInactive.Include, FindObjectsSortMode.None));

            allConnections.Clear();
            allConnections.AddRange(UnityEngine.Object.FindObjectsByType<WireConnection>(FindObjectsInactive.Include, FindObjectsSortMode.None));

            // Find power supply
            powerSupply = null;
            foreach (var comp in allComponents)
            {
                if (comp.componentType == ElectricalComponentType.DCPowerSource)
                {
                    powerSupply = comp;
                    break;
                }
            }

            // Find board sockets
            kvlSockets.Clear();
            kclSockets.Clear();
            var sockets = UnityEngine.Object.FindObjectsByType<CircuitSocket>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var s in sockets)
            {
                if (s.name.Contains("KVL") || s.socketID.Contains("R1") || s.socketID.Contains("R2") || s.socketID.Contains("R3"))
                {
                    kvlSockets.Add(s);
                }
                else if (s.name.Contains("KCL") || s.socketID.Contains("RA") || s.socketID.Contains("RB"))
                {
                    kclSockets.Add(s);
                }
            }

            // Sort KVL sockets: R1, R2, R3
            kvlSockets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            kclSockets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        }

        public void SetMode(CircuitMode mode)
        {
            currentMode = mode;
            EvaluateCircuit();
        }

        public void ToggleMode()
        {
            SetMode(currentMode == CircuitMode.KVL_Series ? CircuitMode.KCL_Parallel : CircuitMode.KVL_Series);
        }

        public void OnConnectionChanged()
        {
            EvaluateCircuit();
        }

        public void EvaluateCircuit()
        {
            if (validator == null || calculator == null)
            {
                Awake();
            }

            var sim = UnityEngine.Object.FindFirstObjectByType<KirchhoffSimulator>();
            if (sim != null)
            {
                currentMode = sim.currentMode;
            }

            DiscoverSceneElements();

            if (powerSupply != null && sim != null)
            {
                powerSupply.voltage = sim.sourceVoltage;
                powerSupply.isEnergized = sim.isPowerOn;
            }

            // 1. Validate electrical topology
            lastValidation = validator.Validate(
                currentMode,
                allComponents,
                allConnections,
                kvlSockets,
                kclSockets,
                powerSupply);

            // 2. Calculate actual parameters
            lastCalculation = calculator.Calculate(lastValidation);

            // 3. Update all physical meters
            UpdateMeterDevices();

            // 4. Update wire visual appearances
            UpdateWireAppearances();

            // 5. Update old simulator/legacy UI if present
            SyncWithLegacySimulator();

            // 6. Dispatch event
            OnCircuitEvaluated?.Invoke(lastValidation, lastCalculation);
        }

        private void UpdateMeterDevices()
        {
            var meters = UnityEngine.Object.FindObjectsByType<LabMeter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var m in meters)
            {
                if (m == null) continue;
                if (m.meterType == MeterType.Ammeter)
                {
                    UpdateAmmeterDevice(m);
                }
                else
                {
                    UpdateVoltmeterDevice(m);
                }
            }
        }

        private void UpdateAmmeterDevice(LabMeter ammeter)
        {
            if (ammeter == null) return;

            string displayStr = "--- A";
            string statusStr = "STANDBY";
            Color textColor = Color.yellow;

            if (lastValidation == null || !lastValidation.isPowerOn)
            {
                displayStr = "0.000 A";
                statusStr = "STANDBY (PWR OFF)";
                textColor = new Color(0.6f, 0.6f, 0.6f);
            }
            else if (lastValidation.hasShortCircuit)
            {
                displayStr = "ERR A";
                statusStr = "SHORT CIRCUIT";
                textColor = Color.red;
            }
            else if (lastValidation.hasSeriesVoltmeterFault)
            {
                displayStr = "--- A";
                statusStr = "VOLTMETER IN SERIES";
                textColor = new Color(1f, 0.4f, 0.2f);
            }
            else if (!lastValidation.isAmmeterInSeries)
            {
                displayStr = "--- A";
                statusStr = "NOT IN SERIES";
                textColor = new Color(1f, 0.4f, 0.2f);
            }
            else if (!lastValidation.isValid)
            {
                displayStr = "--- A";
                statusStr = "OPEN CIRCUIT";
                textColor = new Color(1f, 0.7f, 0.2f);
            }
            else
            {
                // Valid readings!
                float current = lastCalculation != null ? lastCalculation.ammeterReading : 0f;
                if (current < 0.1f && current > 0f)
                {
                    displayStr = string.Format("{0:F1} mA", current * 1000f);
                }
                else
                {
                    displayStr = string.Format("{0:F3} A", current);
                }
                statusStr = "SERIES OK";
                textColor = new Color(0.2f, 1f, 0.5f);
            }

            if (ammeter.readoutText != null)
            {
                ammeter.readoutText.text = displayStr;
                ammeter.readoutText.color = textColor;
            }
            if (ammeter.statusSubText != null)
            {
                ammeter.statusSubText.text = statusStr;
                ammeter.statusSubText.color = textColor;
            }
        }

        private void UpdateVoltmeterDevice(LabMeter voltmeter)
        {
            if (voltmeter == null) return;

            string displayStr = "--- V";
            string statusStr = "STANDBY";
            Color textColor = Color.yellow;

            if (lastValidation == null || !lastValidation.isPowerOn)
            {
                displayStr = "0.00 V";
                statusStr = "STANDBY (PWR OFF)";
                textColor = new Color(0.6f, 0.6f, 0.6f);
            }
            else if (lastValidation.hasShortCircuit)
            {
                displayStr = "ERR V";
                statusStr = "SHORT CIRCUIT";
                textColor = Color.red;
            }
            else if (lastValidation.hasSeriesVoltmeterFault)
            {
                displayStr = "ERR V";
                statusStr = "BLOCKS LOOP";
                textColor = Color.red;
            }
            else if (!lastValidation.isVoltmeterInParallel)
            {
                displayStr = "--- V";
                statusStr = "NOT IN PARALLEL";
                textColor = new Color(1f, 0.4f, 0.2f);
            }
            else if (!lastValidation.isValid)
            {
                displayStr = "--- V";
                statusStr = "OPEN CIRCUIT";
                textColor = new Color(1f, 0.7f, 0.2f);
            }
            else
            {
                float v = lastCalculation != null ? lastCalculation.voltmeterReading : 0f;
                displayStr = string.Format("{0:F2} V", v);
                if (lastValidation.voltmeterProbeTarget == VoltmeterProbeTarget.Resistor1)
                    statusStr = "PARALLEL R1 (V1)";
                else if (lastValidation.voltmeterProbeTarget == VoltmeterProbeTarget.Resistor2)
                    statusStr = "PARALLEL R2 (V2)";
                else
                    statusStr = "PARALLEL OK (Vs)";
                textColor = new Color(0.2f, 1f, 0.5f);
            }

            if (voltmeter.readoutText != null)
            {
                voltmeter.readoutText.text = displayStr;
                voltmeter.readoutText.color = textColor;
            }
            if (voltmeter.statusSubText != null)
            {
                voltmeter.statusSubText.text = statusStr;
                voltmeter.statusSubText.color = textColor;
            }
        }

        private void UpdateWireAppearances()
        {
            bool isEnergized = lastValidation != null && lastValidation.isValid && lastValidation.isPowerOn;
            bool isShort = lastValidation != null && lastValidation.hasShortCircuit;

            foreach (var conn in allConnections)
            {
                if (conn == null) continue;
                var flexWire = conn.GetComponent<FlexibleWire>();
                if (flexWire != null)
                {
                    bool activeThisWire = conn.isConfirmed && isEnergized;
                    flexWire.UpdateWireAppearance(activeThisWire, isShort);
                }
            }
        }

        private void SyncWithLegacySimulator()
        {
            var sim = UnityEngine.Object.FindFirstObjectByType<KirchhoffSimulator>();
            if (sim == null || lastValidation == null) return;

            sim.isPowerOn = lastValidation.isPowerOn;
            // Retain simulator's selected voltage (e.g. 6V for KVL, 12V for KCL)
            if (powerSupply != null) powerSupply.voltage = sim.sourceVoltage;

            if (currentMode == CircuitMode.KVL_Series)
            {
                sim.isKVLClosed = lastValidation.isValid;
                sim.kvlGuidanceMessage = lastValidation.diagnosticMessage;
                if (lastCalculation != null && lastValidation.isValid)
                {
                    sim.kvlLoopCurrent = lastCalculation.mainCurrent;
                    sim.kvlTotalResistance = lastCalculation.totalResistance;
                    if (lastCalculation.componentVoltages.Length >= 2)
                    {
                        sim.kvlV1 = lastCalculation.componentVoltages[0];
                        sim.kvlV2 = lastCalculation.componentVoltages[1];
                        sim.kvlR1 = lastCalculation.componentResistances[0];
                        sim.kvlR2 = lastCalculation.componentResistances[1];
                        sim.kvlSumDrops = lastCalculation.kvlSumDrops;
                        sim.kvlResidual = lastCalculation.kvlResidual;
                    }
                }
            }
            else
            {
                sim.isKCLActive = lastValidation.isValid;
                sim.kclGuidanceMessage = lastValidation.diagnosticMessage;
                if (lastCalculation != null && lastValidation.isValid)
                {
                    sim.kclTotalCurrentIn = lastCalculation.kclTotalInflow;
                    sim.kclEquivalentResistance = lastCalculation.totalResistance;
                    if (lastCalculation.branchCurrents.Length >= 2)
                    {
                        sim.kclCurrentA = lastCalculation.branchCurrents[0];
                        sim.kclCurrentB = lastCalculation.branchCurrents[1];
                        sim.kclResistanceA = lastCalculation.componentResistances[0];
                        sim.kclResistanceB = lastCalculation.componentResistances[1];
                        sim.kclDeltaI = lastCalculation.kclResidual;
                    }
                }
            }

            // Sync legacy CircuitConnectionManager state
            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                ccm.hasShortCircuit = lastValidation.hasShortCircuit;
                ccm.isPowerSuppliedToBoard = lastValidation.isValid;
                ccm.isGroundConnected = lastValidation.isValid;
                ccm.isAmmeterInSeries = lastValidation.isAmmeterInSeries;
                ccm.isVoltmeterInParallel = lastValidation.isVoltmeterInParallel;
                ccm.hasSeriesVoltmeterFault = lastValidation.hasSeriesVoltmeterFault;
                ccm.hasAmmeterParallelShort = lastValidation.hasAmmeterParallelShort;
            }
        }
    }
}
