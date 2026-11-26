using UnityEngine;
using System.Collections.Generic;

namespace VRContent.Circuit
{
    [System.Serializable]
    public class CircuitCalculationResult
    {
        public bool isCalculated = false;
        public CircuitMode mode = CircuitMode.KVL_Series;

        [Header("Source Values")]
        public float sourceVoltage = 0f;

        [Header("Resistance & Main Current")]
        public float totalResistance = 0f;
        public float mainCurrent = 0f; // Amperes

        [Header("Component Breakdown")]
        public float[] componentResistances = new float[0];
        public float[] componentVoltages = new float[0]; // Volts
        public float[] branchCurrents = new float[0];    // Amperes

        [Header("KVL Verification")]
        public float kvlSumDrops = 0f;
        public float kvlResidual = 0f;
        public bool kvlVerified = false;

        [Header("KCL Verification")]
        public float kclTotalInflow = 0f;
        public float kclSumBranches = 0f;
        public float kclResidual = 0f;
        public bool kclVerified = false;

        [Header("Meters Readings")]
        public float ammeterReading = 0f; // 0 if not in series
        public float voltmeterReading = 0f; // Measured across parallel probe
        public bool isAmmeterValid = false;
        public bool isVoltmeterValid = false;
    }

    [DisallowMultipleComponent]
    public class ElectricalCalculator : MonoBehaviour
    {
        [Header("Numerical Tolerance")]
        [Tooltip("Acceptable residual tolerance for KVL (Volts) and KCL (Amperes) verification.")]
        public float tolerance = 0.005f; // 5 mV or 5 mA

        public CircuitCalculationResult Calculate(CircuitValidationResult validation)
        {
            var result = new CircuitCalculationResult();
            result.mode = validation.mode;

            // Only calculate valid, energized circuits. If invalid or open, readings remain 0
            if (!validation.isValid || !validation.isPowerOn || validation.hasShortCircuit)
            {
                result.isCalculated = false;
                result.sourceVoltage = (validation.powerSource != null && validation.isPowerOn) ? validation.powerSource.voltage : 0f;
                return result;
            }

            result.isCalculated = true;
            // Read actual voltage directly from the power supply component
            result.sourceVoltage = validation.powerSource != null ? validation.powerSource.voltage : 0f;

            if (validation.mode == CircuitMode.KVL_Series)
            {
                CalculateSeriesCircuit(validation, result);
            }
            else
            {
                CalculateParallelCircuit(validation, result);
            }

            // Meter Readings
            result.isAmmeterValid = validation.isAmmeterInSeries;
            if (validation.isAmmeterInSeries)
            {
                if (validation.mode == CircuitMode.KVL_Series)
                {
                    result.ammeterReading = result.mainCurrent;
                }
                else // KCL
                {
                    if (validation.ammeterProbeTarget == AmmeterProbeTarget.Branch1 && result.branchCurrents != null && result.branchCurrents.Length > 0)
                    {
                        result.ammeterReading = result.branchCurrents[0];
                    }
                    else if (validation.ammeterProbeTarget == AmmeterProbeTarget.Branch2 && result.branchCurrents != null && result.branchCurrents.Length > 1)
                    {
                        result.ammeterReading = result.branchCurrents[1];
                    }
                    else
                    {
                        result.ammeterReading = result.mainCurrent; // Total inflow (Iin)
                    }
                }
            }
            else
            {
                result.ammeterReading = 0f;
            }

            result.isVoltmeterValid = validation.isVoltmeterInParallel;
            if (validation.isVoltmeterInParallel)
            {
                if (validation.mode == CircuitMode.KVL_Series)
                {
                    if (validation.voltmeterProbeTarget == VoltmeterProbeTarget.Resistor1 && result.componentVoltages != null && result.componentVoltages.Length > 0)
                    {
                        result.voltmeterReading = result.componentVoltages[0];
                    }
                    else if (validation.voltmeterProbeTarget == VoltmeterProbeTarget.Resistor2 && result.componentVoltages != null && result.componentVoltages.Length > 1)
                    {
                        result.voltmeterReading = result.componentVoltages[1];
                    }
                    else
                    {
                        result.voltmeterReading = result.sourceVoltage;
                    }
                }
                else
                {
                    result.voltmeterReading = result.sourceVoltage;
                }
            }
            else
            {
                result.voltmeterReading = 0f;
            }

            return result;
        }

        private void CalculateSeriesCircuit(CircuitValidationResult validation, CircuitCalculationResult result)
        {
            int n = validation.activeResistors.Count;
            result.componentResistances = new float[n];
            result.componentVoltages = new float[n];
            result.branchCurrents = new float[0];

            float sumR = 0f;
            for (int i = 0; i < n; i++)
            {
                // Read actual resistance from the resistor component
                float r = validation.activeResistors[i].resistance;
                result.componentResistances[i] = r;
                sumR += r;
            }
            result.totalResistance = sumR;

            if (sumR > 0f && result.sourceVoltage > 0f)
            {
                // Ohm's law: I = V / R_total
                result.mainCurrent = result.sourceVoltage / sumR;

                float sumV = 0f;
                for (int i = 0; i < n; i++)
                {
                    // V_i = I * R_i
                    float vDrop = result.mainCurrent * result.componentResistances[i];
                    result.componentVoltages[i] = vDrop;
                    sumV += vDrop;
                }

                result.kvlSumDrops = sumV;
                // KVL Residual: |Vs - sum(V_drops)|
                result.kvlResidual = Mathf.Abs(result.sourceVoltage - sumV);
                result.kvlVerified = result.kvlResidual <= tolerance;
            }
            else
            {
                result.mainCurrent = 0f;
                result.kvlSumDrops = 0f;
                result.kvlResidual = 0f;
                result.kvlVerified = false;
            }
        }

        private void CalculateParallelCircuit(CircuitValidationResult validation, CircuitCalculationResult result)
        {
            int n = validation.activeResistors.Count;
            result.componentResistances = new float[n];
            result.componentVoltages = new float[n];
            result.branchCurrents = new float[n];

            float sumInverseR = 0f;
            for (int i = 0; i < n; i++)
            {
                float r = validation.activeResistors[i].resistance;
                result.componentResistances[i] = r;
                // In parallel, full voltage drop is across each branch
                result.componentVoltages[i] = result.sourceVoltage;

                if (r > 0f)
                {
                    sumInverseR += 1f / r;
                    // Branch current: I_i = V_s / R_i
                    result.branchCurrents[i] = result.sourceVoltage / r;
                }
            }

            if (sumInverseR > 0f)
            {
                result.totalResistance = 1f / sumInverseR;
            }

            float sumBranches = 0f;
            for (int i = 0; i < n; i++)
            {
                sumBranches += result.branchCurrents[i];
            }

            result.mainCurrent = sumBranches; // Total inflow from node
            result.kclTotalInflow = sumBranches;
            result.kclSumBranches = sumBranches;

            // KCL Residual: |I_in - sum(I_branches)|
            result.kclResidual = Mathf.Abs(result.kclTotalInflow - sumBranches);
            result.kclVerified = result.kclResidual <= tolerance;
        }
    }
}
