using UnityEngine;
using System;

namespace VRContent.Circuit
{
    public enum CircuitMode
    {
        KVL_Series,     // Kirchhoff's Voltage Law: Series closed loop
        KCL_Parallel    // Kirchhoff's Current Law: Parallel junction
    }

    public class KirchhoffSimulator : MonoBehaviour
    {
        [Header("Circuit State")]
        public CircuitMode currentMode = CircuitMode.KVL_Series;
        public bool isPowerOn = true;
        public float sourceVoltage = 6f; // 6V for KVL, 12V for KCL

        [Header("KVL Series Sockets (R1 & R2)")]
        public CircuitSocket kvlSocket1; // R1
        public CircuitSocket kvlSocket2; // R2
        public CircuitSocket kvlSocket3; // Optional R3

        [Header("KCL Parallel Sockets (Branch A & Branch B)")]
        public CircuitSocket kclSocketA; // Branch A (R1)
        public CircuitSocket kclSocketB; // Branch B (R2)

        [Header("Mode-Specific Visual Objects")]
        public GameObject[] kvlObjects;
        public GameObject[] kclObjects;

        [Header("Calculated Results - KVL")]
        public float kvlR1;
        public float kvlR2;
        public float kvlTotalResistance;
        public float kvlLoopCurrent;
        public float kvlV1;
        public float kvlV2;
        public float kvlSumDrops;
        public float kvlResidual; // Vs - V1 - V2
        public bool isKVLClosed;
        public string kvlGuidanceMessage = "";
        public VoltmeterProbeTarget voltmeterProbeTarget = VoltmeterProbeTarget.TotalCircuit;
        public float measuredVoltmeterVoltage;

        [Header("Calculated Results - KCL")]
        public float kclResistanceA;
        public float kclResistanceB;
        public float kclEquivalentResistance;
        public float kclCurrentA;       // I1
        public float kclCurrentB;       // I2
        public float kclTotalCurrentIn; // Iin
        public float kclDeltaI;         // Residual: Iin - (I1 + I2)
        public bool isKCLActive;
        public string kclGuidanceMessage = "";
        public AmmeterProbeTarget ammeterProbeTarget = AmmeterProbeTarget.MainInflow;
        public float measuredAmmeterCurrent;

        public event Action OnCircuitUpdated;

        private void Start()
        {
            UpdateModeVisuals();
            RecalculateCircuit();
        }

        public void SetVoltage(float v)
        {
            sourceVoltage = v;
            var ps = UnityEngine.Object.FindFirstObjectByType<DCPowerSupply>();
            if (ps != null)
            {
                ps.is6VoltMode = Mathf.Approximately(v, 6f);
                var ec = ps.GetComponent<ElectricalComponent>();
                if (ec != null) ec.voltage = v;
                ps.UpdateVisuals();
            }
            if (CircuitManager.Instance != null && CircuitManager.Instance.powerSupply != null)
            {
                CircuitManager.Instance.powerSupply.voltage = v;
            }
            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void ToggleVoltage()
        {
            if (Mathf.Approximately(sourceVoltage, 6f))
                SetVoltage(12f);
            else
                SetVoltage(6f);
        }

        public void SetPower(bool on)
        {
            isPowerOn = on;
            var ps = UnityEngine.Object.FindFirstObjectByType<DCPowerSupply>();
            if (ps != null)
            {
                ps.isPowerOn = on;
                var ec = ps.GetComponent<ElectricalComponent>();
                if (ec != null) ec.isEnergized = on;
                ps.UpdateVisuals();
            }
            if (CircuitManager.Instance != null && CircuitManager.Instance.powerSupply != null)
            {
                CircuitManager.Instance.powerSupply.isEnergized = on;
            }
            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void TogglePower()
        {
            SetPower(!isPowerOn);
        }

        public void SetMode(CircuitMode mode)
        {
            currentMode = mode;
            float targetVoltage = (mode == CircuitMode.KVL_Series) ? 6f : 12f;
            sourceVoltage = targetVoltage;

            var ps = UnityEngine.Object.FindFirstObjectByType<DCPowerSupply>();
            if (ps != null)
            {
                ps.is6VoltMode = Mathf.Approximately(targetVoltage, 6f);
                var ec = ps.GetComponent<ElectricalComponent>();
                if (ec != null) ec.voltage = targetVoltage;
                ps.UpdateVisuals();
            }
            if (CircuitManager.Instance != null)
            {
                CircuitManager.Instance.currentMode = mode;
                if (CircuitManager.Instance.powerSupply != null)
                    CircuitManager.Instance.powerSupply.voltage = targetVoltage;
            }

            // Set conventional components for the experiment only if no GuidedExperimentManager is active
            if (GuidedExperimentManager.Instance == null)
            {
                if (currentMode == CircuitMode.KVL_Series)
                {
                    if (kvlSocket1 != null && !kvlSocket1.hasResistor)
                        SeatStandardKVLResistors();
                }
                else
                {
                    if (kclSocketA != null && !kclSocketA.hasResistor)
                        SeatStandardKCLResistors();
                }
            }

            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                if (GuidedExperimentManager.Instance != null)
                {
                    ccm.ResetConnections();
                }
                else if (ccm.isAmmeterInSeries || ccm.isPowerSuppliedToBoard)
                {
                    ccm.SetStandardValidWiring(true, true);
                }
            }

            UpdateModeVisuals();
            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void UpdateModeVisuals()
        {
            bool isKVL = (currentMode == CircuitMode.KVL_Series);

            if (kvlObjects != null)
            {
                foreach (var go in kvlObjects)
                {
                    if (go != null) go.SetActive(isKVL);
                }
            }

            if (kclObjects != null)
            {
                foreach (var go in kclObjects)
                {
                    if (go != null) go.SetActive(!isKVL);
                }
            }

            UpdateCurrentArrows(!isKVL && isKCLActive && isPowerOn, kclTotalCurrentIn, kclCurrentA, kclCurrentB);
        }

        public void ToggleMode()
        {
            SetMode(currentMode == CircuitMode.KVL_Series ? CircuitMode.KCL_Parallel : CircuitMode.KVL_Series);
        }

        public void RecalculateCircuit()
        {
            if (currentMode == CircuitMode.KVL_Series)
            {
                CalculateKVL();
            }
            else
            {
                CalculateKCL();
            }

            OnCircuitUpdated?.Invoke();
        }

        public void CalculateKVL()
        {
            bool powerFeedConnected = false;
            bool groundConnected = false;
            bool hasShort = false;
            bool voltmeterFault = false;
            bool ammeterInSeries = false;

            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                hasShort = ccm.hasShortCircuit;
                powerFeedConnected = ccm.isPowerSuppliedToBoard;
                groundConnected = ccm.isGroundConnected;
                voltmeterFault = ccm.hasSeriesVoltmeterFault;
                ammeterInSeries = ccm.isAmmeterInSeries;
            }

            bool hasR1 = (kvlSocket1 != null && kvlSocket1.hasResistor && kvlSocket1.currentResistor != null);
            bool hasR2 = (kvlSocket2 != null && kvlSocket2.hasResistor && kvlSocket2.currentResistor != null);

            kvlR1 = hasR1 ? kvlSocket1.currentResistor.resistance : 0f;
            kvlR2 = hasR2 ? kvlSocket2.currentResistor.resistance : 0f;

            if (!isPowerOn)
            {
                isKVLClosed = false;
                kvlGuidanceMessage = "POWER IS OFF: Switch ON the DC Power Supply.";
            }
            else if (hasShort)
            {
                isKVLClosed = false;
                kvlGuidanceMessage = "SHORT CIRCUIT DETECTED: Direct (+) to (-) connection. Remove short circuit.";
            }
            else if (voltmeterFault)
            {
                isKVLClosed = false;
                kvlGuidanceMessage = "METER ERROR: Voltmeter connected in series! Voltmeter has infinite resistance and blocks current. Connect Voltmeter in parallel across load.";
            }
            else if (!powerFeedConnected)
            {
                isKVLClosed = false;
                kvlGuidanceMessage = "OPEN LOOP: Connect Power Feed from Supply (+) to Board IN (+). Note: Connect Ammeter in series for current measurement.";
            }
            else if (!hasR1)
            {
                isKVLClosed = false;
                kvlGuidanceMessage = "OPEN LOOP: Insert Resistor R1 (100 Ohm) into Socket 1.";
            }
            else if (!hasR2)
            {
                isKVLClosed = false;
                kvlGuidanceMessage = "OPEN LOOP: Insert Resistor R2 (200 Ohm) into Socket 2.";
            }
            else if (!groundConnected)
            {
                isKVLClosed = false;
                kvlGuidanceMessage = "OPEN LOOP: Connect Black Wire from Board GND (-) to Power Supply (-).";
            }
            else
            {
                isKVLClosed = true;
                if (ammeterInSeries)
                    kvlGuidanceMessage = "CLOSED LOOP COMPLETE: Circuit energized with Ammeter in series. KVL verified.";
                else
                    kvlGuidanceMessage = "CLOSED LOOP COMPLETE: Circuit energized (Ammeter bypassed). Place Ammeter in series to measure loop current.";
            }

            if (isKVLClosed)
            {
                kvlTotalResistance = kvlR1 + kvlR2;

                if (kvlTotalResistance > 0f)
                {
                    kvlLoopCurrent = sourceVoltage / kvlTotalResistance;
                    kvlV1 = kvlLoopCurrent * kvlR1;
                    kvlV2 = kvlLoopCurrent * kvlR2;
                    kvlSumDrops = kvlV1 + kvlV2;
                    kvlResidual = Mathf.Abs(sourceVoltage - (kvlV1 + kvlV2));
                    if (kvlResidual < 0.0001f) kvlResidual = 0f;

                    if (voltmeterProbeTarget == VoltmeterProbeTarget.Resistor1)
                        measuredVoltmeterVoltage = kvlV1;
                    else if (voltmeterProbeTarget == VoltmeterProbeTarget.Resistor2)
                        measuredVoltmeterVoltage = kvlV2;
                    else
                        measuredVoltmeterVoltage = sourceVoltage;
                }
                else
                {
                    kvlLoopCurrent = 0f;
                    kvlV1 = 0f;
                    kvlV2 = 0f;
                    kvlSumDrops = 0f;
                    kvlResidual = 0f;
                    measuredVoltmeterVoltage = 0f;
                }
            }
            else
            {
                kvlTotalResistance = (hasR1 && hasR2) ? (kvlR1 + kvlR2) : 0f;
                kvlLoopCurrent = 0f;
                kvlV1 = 0f;
                kvlV2 = 0f;
                kvlSumDrops = 0f;
                kvlResidual = 0f;
                measuredVoltmeterVoltage = 0f;
            }
        }

        public void SelectKVLExperiment()
        {
            SetMode(CircuitMode.KVL_Series);
            SetVoltage(6f);
        }

        public void MeasureVoltageAcrossR1()
        {
            voltmeterProbeTarget = VoltmeterProbeTarget.Resistor1;
            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                ccm.ConnectVoltmeterAcrossR1();
            }
            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void MeasureVoltageAcrossR2()
        {
            voltmeterProbeTarget = VoltmeterProbeTarget.Resistor2;
            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                ccm.ConnectVoltmeterAcrossR2();
            }
            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void MeasureVoltageAcrossTotal()
        {
            voltmeterProbeTarget = VoltmeterProbeTarget.TotalCircuit;
            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                ccm.ConnectVoltmeterAcrossTotal();
            }
            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void SeatStandardKVLResistors()
        {
            var allRes = UnityEngine.Object.FindObjectsByType<ResistorComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            ResistorComponent r100 = null;
            ResistorComponent r200 = null;
            foreach (var r in allRes)
            {
                if (Mathf.Approximately(r.resistance, 100f)) r100 = r;
                if (Mathf.Approximately(r.resistance, 200f) && !r.name.Contains("Branch") && !r.name.Contains("_B") && r200 == null) r200 = r;
            }
            if (r200 == null)
            {
                foreach (var r in allRes)
                {
                    if (Mathf.Approximately(r.resistance, 200f)) { r200 = r; break; }
                }
            }

            if (kvlSocket1 != null && r100 != null) kvlSocket1.SetResistor(r100);
            if (kvlSocket2 != null && r200 != null) kvlSocket2.SetResistor(r200);

            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void CalculateKCL()
        {
            bool powerFeedConnected = false;
            bool groundConnected = false;
            bool hasShort = false;
            bool voltmeterFault = false;
            bool ammeterInSeries = false;

            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                hasShort = ccm.hasShortCircuit;
                powerFeedConnected = ccm.isPowerSuppliedToBoard;
                groundConnected = ccm.isGroundConnected;
                voltmeterFault = ccm.hasSeriesVoltmeterFault;
                ammeterInSeries = ccm.isAmmeterInSeries;
            }

            bool hasRA = (kclSocketA != null && kclSocketA.hasResistor && kclSocketA.currentResistor != null);
            bool hasRB = (kclSocketB != null && kclSocketB.hasResistor && kclSocketB.currentResistor != null);

            kclResistanceA = hasRA ? kclSocketA.currentResistor.resistance : 0f;
            kclResistanceB = hasRB ? kclSocketB.currentResistor.resistance : 0f;

            if (!isPowerOn)
            {
                isKCLActive = false;
                kclGuidanceMessage = "POWER IS OFF: Switch ON the DC Power Supply.";
            }
            else if (hasShort)
            {
                isKCLActive = false;
                kclGuidanceMessage = "SHORT CIRCUIT DETECTED: Direct (+) to (-) connection. Remove short circuit.";
            }
            else if (voltmeterFault)
            {
                isKCLActive = false;
                kclGuidanceMessage = "METER ERROR: Voltmeter connected in series! Connect Voltmeter in parallel across load.";
            }
            else if (!powerFeedConnected)
            {
                isKCLActive = false;
                kclGuidanceMessage = "INCOMPLETE CIRCUIT: Connect Power Feed from Supply (+) to Circuit Board Input (+).";
            }
            else if (!hasRA)
            {
                isKCLActive = false;
                kclGuidanceMessage = "INCOMPLETE BRANCHES: Insert Resistor R1 (200 Ohm) into Parallel Branch A.";
            }
            else if (!hasRB)
            {
                isKCLActive = false;
                kclGuidanceMessage = "INCOMPLETE BRANCHES: Insert Resistor R2 (300 Ohm) into Parallel Branch B.";
            }
            else if (!groundConnected)
            {
                isKCLActive = false;
                kclGuidanceMessage = "INCOMPLETE CIRCUIT: Connect Black Wire from Board GND (-) to Power Supply (-).";
            }
            else
            {
                isKCLActive = true;
                if (ammeterInSeries)
                    kclGuidanceMessage = "PARALLEL JUNCTION COMPLETE: Circuit energized with Ammeter in series. KCL verified.";
                else
                    kclGuidanceMessage = "PARALLEL JUNCTION COMPLETE: Circuit energized. Place Ammeter in series to measure node inflow current.";
            }

            if (isKCLActive)
            {
                kclCurrentA = (kclResistanceA > 0f) ? sourceVoltage / kclResistanceA : 0f;
                kclCurrentB = (kclResistanceB > 0f) ? sourceVoltage / kclResistanceB : 0f;
                kclTotalCurrentIn = kclCurrentA + kclCurrentB;

                if (kclResistanceA > 0f && kclResistanceB > 0f)
                {
                    kclEquivalentResistance = (kclResistanceA * kclResistanceB) / (kclResistanceA + kclResistanceB);
                }
                else
                {
                    kclEquivalentResistance = kclResistanceA > 0f ? kclResistanceA : kclResistanceB;
                }

                kclDeltaI = Mathf.Abs(kclTotalCurrentIn - (kclCurrentA + kclCurrentB));
                if (kclDeltaI < 0.0001f) kclDeltaI = 0f;

                if (ammeterProbeTarget == AmmeterProbeTarget.Branch1)
                    measuredAmmeterCurrent = kclCurrentA;
                else if (ammeterProbeTarget == AmmeterProbeTarget.Branch2)
                    measuredAmmeterCurrent = kclCurrentB;
                else
                    measuredAmmeterCurrent = kclTotalCurrentIn;

                measuredVoltmeterVoltage = sourceVoltage;
            }
            else
            {
                kclCurrentA = 0f;
                kclCurrentB = 0f;
                kclTotalCurrentIn = 0f;
                kclEquivalentResistance = (hasRA && hasRB) ? ((kclResistanceA * kclResistanceB) / (kclResistanceA + kclResistanceB)) : 0f;
                kclDeltaI = 0f;
                measuredAmmeterCurrent = 0f;
                measuredVoltmeterVoltage = 0f;
            }

            UpdateCurrentArrows(isKCLActive && isPowerOn, kclTotalCurrentIn, kclCurrentA, kclCurrentB);
        }

        public void SelectKCLExperiment()
        {
            SetMode(CircuitMode.KCL_Parallel);
            SetVoltage(12f);
            SeatStandardKCLResistors();
        }

        public void MeasureCurrentMainInflow()
        {
            ammeterProbeTarget = AmmeterProbeTarget.MainInflow;
            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                ccm.ConnectAmmeterMainInflow();
            }
            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void MeasureCurrentBranch1()
        {
            ammeterProbeTarget = AmmeterProbeTarget.Branch1;
            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                ccm.ConnectAmmeterBranch1();
            }
            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void MeasureCurrentBranch2()
        {
            ammeterProbeTarget = AmmeterProbeTarget.Branch2;
            var ccm = CircuitConnectionManager.Instance;
            if (ccm != null)
            {
                ccm.ConnectAmmeterBranch2();
            }
            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void SeatStandardKCLResistors()
        {
            var allRes = UnityEngine.Object.FindObjectsByType<ResistorComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            ResistorComponent r200 = null;
            ResistorComponent r300 = null;
            foreach (var r in allRes)
            {
                if ((r.name.Contains("Branch") || r.name.Contains("_B") || (r != kvlSocket2?.currentResistor)) && Mathf.Approximately(r.resistance, 200f) && r200 == null)
                    r200 = r;
                if (Mathf.Approximately(r.resistance, 300f) && r300 == null)
                    r300 = r;
            }
            if (r200 == null)
            {
                foreach (var r in allRes)
                {
                    if (Mathf.Approximately(r.resistance, 200f)) { r200 = r; break; }
                }
            }

            if (kclSocketA != null && r200 != null) kclSocketA.SetResistor(r200);
            if (kclSocketB != null && r300 != null) kclSocketB.SetResistor(r300);

            RecalculateCircuit();
            if (CircuitManager.Instance != null) CircuitManager.Instance.EvaluateCircuit();
        }

        public void UpdateCurrentArrows(bool active, float iIn, float i1, float i2)
        {
            var arrowRoot = GameObject.Find("KCL_Current_Arrows");
            if (arrowRoot == null)
            {
                var all = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var go in all)
                {
                    if (go.name == "KCL_Current_Arrows") { arrowRoot = go; break; }
                }
            }
            if (arrowRoot == null) return;

            SetArrowLabel(arrowRoot.transform.Find("Arrow_Inflow"), active ? string.Format("Iin: {0:F3} A ({1:F1} mA)", iIn, iIn * 1000f) : "Iin: ---", active);
            SetArrowLabel(arrowRoot.transform.Find("Arrow_BranchA"), active ? string.Format("I1: {0:F3} A ({1:F1} mA)", i1, i1 * 1000f) : "I1: ---", active);
            SetArrowLabel(arrowRoot.transform.Find("Arrow_BranchB"), active ? string.Format("I2: {0:F3} A ({1:F1} mA)", i2, i2 * 1000f) : "I2: ---", active);
            SetArrowLabel(arrowRoot.transform.Find("Arrow_Return"), active ? string.Format("Iout: {0:F3} A ({1:F1} mA)", iIn, iIn * 1000f) : "Iout: ---", active);
        }

        private void SetArrowLabel(Transform arrowTr, string text, bool active)
        {
            if (arrowTr == null) return;
            var tm = arrowTr.GetComponentInChildren<TextMesh>(true);
            if (tm != null)
            {
                tm.text = text;
                tm.color = active ? new Color(0.2f, 1f, 0.6f) : new Color(0.5f, 0.5f, 0.5f);
            }
        }
    }
}