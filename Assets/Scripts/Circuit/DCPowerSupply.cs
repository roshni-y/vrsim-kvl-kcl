using UnityEngine;

namespace VRContent.Circuit
{
    public class DCPowerSupply : MonoBehaviour
    {
        [Header("References")]
        public KirchhoffSimulator simulator;
        public TextMesh voltageReadoutText;
        public TextMesh modeVoltageText;
        public Renderer powerIndicatorLed;
        public Material ledOnMat;
        public Material ledOffMat;

        [Header("Switch Handles")]
        public Transform powerSwitchHandle;
        public Transform voltageSwitchHandle;

        [Header("State")]
        public bool is6VoltMode = true;
        public bool isPowerOn = true;

        private void Start()
        {
            if (simulator == null)
            {
                simulator = Object.FindFirstObjectByType<KirchhoffSimulator>();
            }
            if (simulator != null)
            {
                isPowerOn = simulator.isPowerOn;
                is6VoltMode = Mathf.Approximately(simulator.sourceVoltage, 6f);
            }
            UpdateVisuals();
        }

        private void Update()
        {
            // Sync with simulator if externally changed (e.g. from UI buttons)
            if (simulator != null)
            {
                bool sim6V = Mathf.Approximately(simulator.sourceVoltage, 6f);
                if (simulator.isPowerOn != isPowerOn || sim6V != is6VoltMode)
                {
                    isPowerOn = simulator.isPowerOn;
                    is6VoltMode = sim6V;
                    var ec = GetComponent<ElectricalComponent>();
                    if (ec != null)
                    {
                        ec.voltage = simulator.sourceVoltage;
                        ec.isEnergized = simulator.isPowerOn;
                    }
                    UpdateVisuals();
                }
            }
        }

        public void ToggleVoltage()
        {
            is6VoltMode = !is6VoltMode;
            float newVoltage = is6VoltMode ? 6f : 12f;
            if (simulator != null) simulator.SetVoltage(newVoltage);
            SyncElectricalComponent();
            UpdateVisuals();
        }

        public void SetVoltage6V()
        {
            is6VoltMode = true;
            if (simulator != null) simulator.SetVoltage(6f);
            SyncElectricalComponent();
            UpdateVisuals();
        }

        public void SetVoltage12V()
        {
            is6VoltMode = false;
            if (simulator != null) simulator.SetVoltage(12f);
            SyncElectricalComponent();
            UpdateVisuals();
        }

        public void TogglePower()
        {
            isPowerOn = !isPowerOn;
            if (simulator != null) simulator.SetPower(isPowerOn);
            SyncElectricalComponent();
            UpdateVisuals();
        }

        public void SyncElectricalComponent()
        {
            var ec = GetComponent<ElectricalComponent>();
            if (ec != null)
            {
                ec.voltage = is6VoltMode ? 6f : 12f;
                ec.isEnergized = isPowerOn;
            }
            if (CircuitManager.Instance != null)
            {
                CircuitManager.Instance.EvaluateCircuit();
            }
        }

        public void UpdateVisuals()
        {
            float v = is6VoltMode ? 6.0f : 12.0f;
            if (voltageReadoutText != null)
            {
                voltageReadoutText.text = isPowerOn ? $"{v:F1} V" : "0.0 V";
                voltageReadoutText.color = isPowerOn ? new Color(0.2f, 1f, 0.9f) : new Color(0.4f, 0.4f, 0.4f);
            }
            if (modeVoltageText != null)
            {
                modeVoltageText.text = is6VoltMode ? "[6V] 12V" : "6V [12V]";
            }
            if (powerIndicatorLed != null && ledOnMat != null && ledOffMat != null)
            {
                powerIndicatorLed.material = isPowerOn ? ledOnMat : ledOffMat;
            }
            if (powerSwitchHandle != null)
            {
                powerSwitchHandle.localRotation = Quaternion.Euler(isPowerOn ? 18f : -18f, 0f, 0f);
            }
            if (voltageSwitchHandle != null)
            {
                voltageSwitchHandle.localRotation = Quaternion.Euler(0f, 0f, is6VoltMode ? -22f : 22f);
            }
        }
    }
}
