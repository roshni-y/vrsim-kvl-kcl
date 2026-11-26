using UnityEngine;
using TMPro;

namespace VRContent.Circuit
{
    public enum MeterType
    {
        Ammeter,
        Voltmeter
    }

    public class LabMeter : MonoBehaviour
    {
        public MeterType meterType = MeterType.Ammeter;
        public KirchhoffSimulator simulator;
        
        [Header("Readout Text Components")]
        public TextMesh readoutText;
        public TextMesh labelText;
        public TextMesh statusSubText;
        public TMP_Text readoutTMP;
        public TMP_Text statusTMP;

        [Header("Visual Feedback")]
        public Renderer screenBackground;
        public Color activeScreenColor = new Color(0.02f, 0.08f, 0.05f, 1f); // Dark digital LCD green
        public Color inactiveScreenColor = new Color(0.04f, 0.04f, 0.04f, 1f); // Dark standby
        public Color errorScreenColor = new Color(0.12f, 0.02f, 0.02f, 1f); // Subtle red warning

        private void Start()
        {
            if (simulator == null)
            {
                simulator = Object.FindFirstObjectByType<KirchhoffSimulator>();
            }
            if (labelText != null)
            {
                labelText.text = meterType == MeterType.Ammeter ? "AMMETER (A) - SERIES" : "VOLTMETER (V) - PARALLEL";
            }
            UpdateReading();
        }

        private void Update()
        {
            UpdateReading();
        }

        public void UpdateReading()
        {
            if (simulator == null)
            {
                simulator = Object.FindFirstObjectByType<KirchhoffSimulator>();
            }

            var ccm = CircuitConnectionManager.Instance;
            bool isPowerOn = simulator != null && simulator.isPowerOn;
            bool hasShort = ccm != null && ccm.hasShortCircuit;
            bool isCircuitValid = false;

            if (simulator != null)
            {
                isCircuitValid = (simulator.currentMode == CircuitMode.KVL_Series)
                    ? simulator.isKVLClosed
                    : simulator.isKCLActive;
            }

            string displayStr = "";
            string statusStr = "";
            Color textColor = Color.yellow;

            if (meterType == MeterType.Ammeter)
            {
                bool inSeries = ccm != null && ccm.isAmmeterInSeries;

                if (!isPowerOn)
                {
                    displayStr = "0.000 A";
                    statusStr = "STANDBY (PWR OFF)";
                    textColor = new Color(0.6f, 0.6f, 0.6f);
                }
                else if (hasShort)
                {
                    displayStr = "ERR A";
                    statusStr = "SHORT CIRCUIT";
                    textColor = Color.red;
                }
                else if (!inSeries)
                {
                    // Ammeter placement MUST be in series; unavailable otherwise
                    displayStr = "--- A";
                    statusStr = "NOT IN SERIES";
                    textColor = new Color(1f, 0.4f, 0.2f);
                }
                else if (!isCircuitValid)
                {
                    displayStr = "--- A";
                    statusStr = "OPEN LOOP";
                    textColor = new Color(1f, 0.7f, 0.2f);
                }
                else
                {
                    // Valid circuit with Ammeter properly in series!
                    float current = (simulator.currentMode == CircuitMode.KVL_Series)
                        ? simulator.kvlLoopCurrent
                        : simulator.measuredAmmeterCurrent;

                    if (current < 0.15f && current > 0f)
                    {
                        displayStr = string.Format("{0:F1} mA", current * 1000f);
                    }
                    else
                    {
                        displayStr = string.Format("{0:F3} A", current);
                    }

                    if (simulator.currentMode == CircuitMode.KVL_Series)
                    {
                        statusStr = "SERIES OK (I)";
                    }
                    else
                    {
                        if (simulator.ammeterProbeTarget == AmmeterProbeTarget.Branch1)
                            statusStr = "BRANCH 1 (I1)";
                        else if (simulator.ammeterProbeTarget == AmmeterProbeTarget.Branch2)
                            statusStr = "BRANCH 2 (I2)";
                        else
                            statusStr = "MAIN PATH (Iin)";
                    }
                    textColor = new Color(0.2f, 1f, 0.5f); // Bright LED Green
                }
            }
            else // Voltmeter
            {
                bool inParallel = ccm != null && ccm.isVoltmeterInParallel;

                if (!isPowerOn)
                {
                    displayStr = "0.00 V";
                    statusStr = "STANDBY (PWR OFF)";
                    textColor = new Color(0.6f, 0.6f, 0.6f);
                }
                else if (hasShort)
                {
                    displayStr = "ERR V";
                    statusStr = "SHORT CIRCUIT";
                    textColor = Color.red;
                }
                else if (!inParallel)
                {
                    // Voltmeter placement MUST be in parallel; unavailable otherwise
                    displayStr = "--- V";
                    statusStr = "NOT IN PARALLEL";
                    textColor = new Color(1f, 0.4f, 0.2f);
                }
                else if (!isCircuitValid)
                {
                    displayStr = "--- V";
                    statusStr = "INCOMPLETE";
                    textColor = new Color(1f, 0.7f, 0.2f);
                }
                else
                {
                    // Valid circuit with Voltmeter in parallel across energized load
                    float voltage = simulator != null ? simulator.measuredVoltmeterVoltage : 0f;
                    displayStr = string.Format("{0:F2} V", voltage);
                    if (simulator != null && simulator.voltmeterProbeTarget == VoltmeterProbeTarget.Resistor1)
                        statusStr = "PARALLEL R1 (V1)";
                    else if (simulator != null && simulator.voltmeterProbeTarget == VoltmeterProbeTarget.Resistor2)
                        statusStr = "PARALLEL R2 (V2)";
                    else
                        statusStr = "PARALLEL OK (Vs)";
                    textColor = new Color(0.2f, 1f, 0.5f); // Bright LED Green
                }
            }

            // Apply to TextMesh
            if (readoutText != null)
            {
                readoutText.text = displayStr;
                readoutText.color = textColor;
            }

            if (statusSubText != null)
            {
                statusSubText.text = statusStr;
                statusSubText.color = textColor;
            }

            // Apply to TMP if available
            if (readoutTMP != null)
            {
                readoutTMP.text = displayStr;
                readoutTMP.color = textColor;
            }

            if (statusTMP != null)
            {
                statusTMP.text = statusStr;
                statusTMP.color = textColor;
            }
        }
    }
}