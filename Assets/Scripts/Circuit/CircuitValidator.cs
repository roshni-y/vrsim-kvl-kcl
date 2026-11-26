using UnityEngine;
using System.Collections.Generic;

namespace VRContent.Circuit
{
    public enum VoltmeterProbeTarget
    {
        TotalCircuit,   // Across whole board / supply (Vs)
        Resistor1,      // Across R1 (V1)
        Resistor2       // Across R2 (V2)
    }

    public enum AmmeterProbeTarget
    {
        MainInflow,     // Main incoming current into junction (Iin)
        Branch1,        // In series with Branch 1 / R1 (I1)
        Branch2         // In series with Branch 2 / R2 (I2)
    }

    public enum CircuitFaultType
    {
        None,
        PowerOff,
        OpenCircuit,
        ShortCircuit,
        VoltmeterInSeries,
        AmmeterInParallel,
        MissingResistor,
        MissingComponent,
        InvalidWiring
    }

    [System.Serializable]
    public class CircuitValidationResult
    {
        public bool isValid = false;
        public CircuitMode mode = CircuitMode.KVL_Series;
        public CircuitFaultType faultType = CircuitFaultType.None;
        public string diagnosticMessage = "";
        public string missingConnectionDetails = "";

        public bool isPowerOn = false;
        public bool hasShortCircuit = false;
        public bool isAmmeterInSeries = false;
        public bool isVoltmeterInParallel = false;
        public bool hasSeriesVoltmeterFault = false;
        public bool hasAmmeterParallelShort = false;
        public VoltmeterProbeTarget voltmeterProbeTarget = VoltmeterProbeTarget.TotalCircuit;
        public AmmeterProbeTarget ammeterProbeTarget = AmmeterProbeTarget.MainInflow;

        public ElectricalComponent powerSource;
        public List<ElectricalComponent> activeResistors = new List<ElectricalComponent>();
        public ElectricalComponent ammeterComponent;
        public ElectricalComponent voltmeterComponent;
    }

    [DisallowMultipleComponent]
    public class CircuitValidator : MonoBehaviour
    {
        [Header("Configuration")]
        public bool requireAmmeterForValidity = false; // If true, ammeter MUST be in series for circuit to be valid

        public CircuitValidationResult Validate(
            CircuitMode mode,
            List<ElectricalComponent> components,
            List<WireConnection> connections,
            List<CircuitSocket> kvlSockets,
            List<CircuitSocket> kclSockets,
            ElectricalComponent psu)
        {
            var result = new CircuitValidationResult();
            result.mode = mode;
            result.powerSource = psu;

            // 1. Power Supply Check
            if (psu == null)
            {
                result.faultType = CircuitFaultType.MissingComponent;
                result.diagnosticMessage = "ERROR: DC Power Supply not found in scene.";
                result.missingConnectionDetails = "DC Power Supply missing.";
                return result;
            }

            result.isPowerOn = psu.isEnergized;
            if (!psu.isEnergized)
            {
                result.faultType = CircuitFaultType.PowerOff;
                result.diagnosticMessage = "POWER IS OFF: Turn ON the DC Power Supply switch to energize the circuit.";
                result.missingConnectionDetails = "Power switch is OFF.";
                return result;
            }

            // Find terminals
            var supplyPos = psu.GetTerminalByPolarity(TerminalPolarity.Positive);
            var supplyNeg = psu.GetTerminalByPolarity(TerminalPolarity.Negative_Ground);

            // Find meters
            foreach (var comp in components)
            {
                if (comp.componentType == ElectricalComponentType.Ammeter) result.ammeterComponent = comp;
                if (comp.componentType == ElectricalComponentType.Voltmeter) result.voltmeterComponent = comp;
            }

            // 2. Direct Short Circuit Check across Supply Rails
            foreach (var wire in connections)
            {
                if (wire == null || !wire.isConfirmed) continue;

                var tA = wire.terminalA;
                var tB = wire.terminalB;

                if ((tA == supplyPos && tB == supplyNeg) || (tB == supplyPos && tA == supplyNeg))
                {
                    result.faultType = CircuitFaultType.ShortCircuit;
                    result.hasShortCircuit = true;
                    result.diagnosticMessage = "CRITICAL SHORT CIRCUIT DETECTED: Direct wire connecting Power Supply (+) to (-)! Disconnect shorting wire immediately.";
                    result.missingConnectionDetails = "Remove shorting bridge across power supply.";
                    return result;
                }
            }

            // 3. Voltmeter & Ammeter Fault Inspection
            ElectricalTerminal ammIn = result.ammeterComponent?.GetTerminalByRole(TerminalRole.MeterInputPositive);
            ElectricalTerminal ammOut = result.ammeterComponent?.GetTerminalByRole(TerminalRole.MeterInputCommon);
            ElectricalTerminal voltIn = result.voltmeterComponent?.GetTerminalByRole(TerminalRole.MeterInputPositive);
            ElectricalTerminal voltOut = result.voltmeterComponent?.GetTerminalByRole(TerminalRole.MeterInputCommon);

            // Ammeter parallel short check: Ammeter (+10A) and (-COM) across supply rails
            bool ammInAtPos = IsTerminalConnectedTo(ammIn, supplyPos, connections);
            bool ammOutAtNeg = IsTerminalConnectedTo(ammOut, supplyNeg, connections);
            bool ammInAtNeg = IsTerminalConnectedTo(ammIn, supplyNeg, connections);
            bool ammOutAtPos = IsTerminalConnectedTo(ammOut, supplyPos, connections);

            if ((ammInAtPos && ammOutAtNeg) || (ammInAtNeg && ammOutAtPos))
            {
                result.faultType = CircuitFaultType.AmmeterInParallel;
                result.hasShortCircuit = true;
                result.hasAmmeterParallelShort = true;
                result.diagnosticMessage = "CRITICAL METER ERROR: Ammeter connected in parallel directly across power supply rails! Ammeter has zero resistance and creates a dead short. Ammeter must be connected in series.";
                result.missingConnectionDetails = "Disconnect Ammeter from parallel supply rails and place in series.";
                return result;
            }

            // Voltmeter in series fault check
            // Voltmeter has ~10 MΩ internal resistance. If placed in series in loop, it chokes the circuit.
            if (voltIn != null && voltOut != null)
            {
                bool voltInAtSupplyPos = IsTerminalConnectedTo(voltIn, supplyPos, connections);
                bool voltOutAtBoardIn = false;
                var boardIn = FindTerminalByRole(components, TerminalRole.CircuitInlet);
                if (boardIn != null && IsTerminalConnectedTo(voltOut, boardIn, connections))
                {
                    voltOutAtBoardIn = true;
                }

                if (voltInAtSupplyPos && voltOutAtBoardIn)
                {
                    result.faultType = CircuitFaultType.VoltmeterInSeries;
                    result.hasSeriesVoltmeterFault = true;
                    result.diagnosticMessage = "METER FAULT: Voltmeter connected in series! Voltmeter has near-infinite internal resistance and blocks current flow. Connect Voltmeter in parallel across components.";
                    result.missingConnectionDetails = "Remove Voltmeter from series path; wire in parallel.";
                    return result;
                }
            }

            // 4. Mode-Specific Topological Validation
            if (mode == CircuitMode.KVL_Series)
            {
                ValidateKVLSeries(result, components, connections, kvlSockets, supplyPos, supplyNeg, ammIn, ammOut, voltIn, voltOut);
            }
            else
            {
                ValidateKCLParallel(result, components, connections, kclSockets, supplyPos, supplyNeg, ammIn, ammOut, voltIn, voltOut);
            }

            return result;
        }

        private void ValidateKVLSeries(
            CircuitValidationResult result,
            List<ElectricalComponent> components,
            List<WireConnection> connections,
            List<CircuitSocket> kvlSockets,
            ElectricalTerminal supplyPos,
            ElectricalTerminal supplyNeg,
            ElectricalTerminal ammIn,
            ElectricalTerminal ammOut,
            ElectricalTerminal voltIn,
            ElectricalTerminal voltOut)
        {
            var boardIn = FindTerminalByRole(components, TerminalRole.CircuitInlet);
            var boardGnd = FindTerminalByRole(components, TerminalRole.CircuitReturn);

            // A. Check Power Feed to Board Input
            bool supplyToAmm = ammIn != null && IsTerminalConnectedTo(supplyPos, ammIn, connections);
            bool ammToBoard = ammOut != null && boardIn != null && IsTerminalConnectedTo(ammOut, boardIn, connections);
            bool directSupplyToBoard = boardIn != null && IsTerminalConnectedTo(supplyPos, boardIn, connections);

            if (supplyToAmm && ammToBoard)
            {
                result.isAmmeterInSeries = true;
            }
            else if (directSupplyToBoard)
            {
                result.isAmmeterInSeries = false;
            }
            else
            {
                result.faultType = CircuitFaultType.OpenCircuit;
                if (supplyToAmm && !ammToBoard)
                {
                    result.diagnosticMessage = "OPEN CIRCUIT: Ammeter (+) is connected to supply, but Ammeter (-) is not connected to Circuit Board Input (+).";
                    result.missingConnectionDetails = "Wire Ammeter (-COM) to Circuit Board Input (+).";
                }
                else
                {
                    result.diagnosticMessage = "OPEN CIRCUIT: No power feed wire from Power Supply (+) to Circuit Board Input (+) or Ammeter (+).";
                    result.missingConnectionDetails = "Wire Power Supply (+) to Ammeter (+) (or Board Input (+)).";
                }
                return;
            }

            // B. Check Resistors in Series Sockets
            if (kvlSockets == null || kvlSockets.Count < 2)
            {
                result.faultType = CircuitFaultType.MissingComponent;
                result.diagnosticMessage = "ERROR: Circuit board series resistor sockets not found.";
                result.missingConnectionDetails = "Resistor sockets missing.";
                return;
            }

            var s1 = kvlSockets[0];
            var s2 = kvlSockets[1];

            if (s1 == null || !s1.hasResistor || s1.currentResistor == null)
            {
                result.faultType = CircuitFaultType.MissingResistor;
                result.diagnosticMessage = "OPEN CIRCUIT: Resistor R1 slot is empty! Insert Resistor R1 into Socket 1 to close the series path.";
                result.missingConnectionDetails = "Seat Resistor R1 into Socket 1.";
                return;
            }

            if (s2 == null || !s2.hasResistor || s2.currentResistor == null)
            {
                result.faultType = CircuitFaultType.MissingResistor;
                result.diagnosticMessage = "OPEN CIRCUIT: Resistor R2 slot is empty! Insert Resistor R2 into Socket 2 to complete the series loop.";
                result.missingConnectionDetails = "Seat Resistor R2 into Socket 2.";
                return;
            }

            // Resistors are present - read actual components
            var r1Comp = s1.currentResistor.GetComponent<ElectricalComponent>();
            var r2Comp = s2.currentResistor.GetComponent<ElectricalComponent>();
            if (r1Comp != null) result.activeResistors.Add(r1Comp);
            if (r2Comp != null) result.activeResistors.Add(r2Comp);

            // Optional R3 socket
            if (kvlSockets.Count > 2 && kvlSockets[2] != null && kvlSockets[2].hasResistor && kvlSockets[2].currentResistor != null)
            {
                var r3Comp = kvlSockets[2].currentResistor.GetComponent<ElectricalComponent>();
                if (r3Comp != null) result.activeResistors.Add(r3Comp);
            }

            // C. Check Ground Return to Power Supply (-)
            bool boardGndToSupply = boardGnd != null && IsTerminalConnectedTo(boardGnd, supplyNeg, connections);
            if (!boardGndToSupply)
            {
                result.faultType = CircuitFaultType.OpenCircuit;
                result.diagnosticMessage = "OPEN CIRCUIT: Missing ground return! Connect black wire from Board Ground (-) back to Power Supply (-).";
                result.missingConnectionDetails = "Wire Circuit Board Ground (-) to Power Supply (-).";
                return;
            }

            // D. Check Voltmeter Parallel Connection
            if (voltIn != null && voltOut != null)
            {
                var tpR1Pos = FindTerminalByRole(components, TerminalRole.TestPointR1Positive);
                var tpR1Neg = FindTerminalByRole(components, TerminalRole.TestPointR1Negative);
                bool voltAcrossR1 = tpR1Pos != null && tpR1Neg != null &&
                                    IsTerminalConnectedTo(voltIn, tpR1Pos, connections) &&
                                    IsTerminalConnectedTo(voltOut, tpR1Neg, connections);

                var tpR2Pos = FindTerminalByRole(components, TerminalRole.TestPointR2Positive);
                var tpR2Neg = FindTerminalByRole(components, TerminalRole.TestPointR2Negative);
                bool voltAcrossR2 = tpR2Pos != null && tpR2Neg != null &&
                                    IsTerminalConnectedTo(voltIn, tpR2Pos, connections) &&
                                    IsTerminalConnectedTo(voltOut, tpR2Neg, connections);

                bool voltInHigh = IsTerminalConnectedTo(voltIn, supplyPos, connections) ||
                                  (boardIn != null && IsTerminalConnectedTo(voltIn, boardIn, connections));
                bool voltOutGnd = IsTerminalConnectedTo(voltOut, supplyNeg, connections) ||
                                  (boardGnd != null && IsTerminalConnectedTo(voltOut, boardGnd, connections));
                bool voltAcrossTotal = voltInHigh && voltOutGnd;

                var sim = Object.FindFirstObjectByType<KirchhoffSimulator>();
                if (voltAcrossR1 || (sim != null && sim.voltmeterProbeTarget == VoltmeterProbeTarget.Resistor1 && (voltAcrossTotal || voltAcrossR1)))
                {
                    result.isVoltmeterInParallel = true;
                    result.voltmeterProbeTarget = VoltmeterProbeTarget.Resistor1;
                }
                else if (voltAcrossR2 || (sim != null && sim.voltmeterProbeTarget == VoltmeterProbeTarget.Resistor2 && (voltAcrossTotal || voltAcrossR2)))
                {
                    result.isVoltmeterInParallel = true;
                    result.voltmeterProbeTarget = VoltmeterProbeTarget.Resistor2;
                }
                else if (voltAcrossTotal)
                {
                    result.isVoltmeterInParallel = true;
                    result.voltmeterProbeTarget = VoltmeterProbeTarget.TotalCircuit;
                }
                else
                {
                    result.isVoltmeterInParallel = false;
                }
            }

            // Circuit is topologically complete and closed!
            result.isValid = true;
            result.faultType = CircuitFaultType.None;
            if (result.isAmmeterInSeries)
            {
                result.diagnosticMessage = "CLOSED LOOP COMPLETE: Circuit energized with Ammeter in series. KVL verified.";
            }
            else
            {
                result.diagnosticMessage = "CLOSED LOOP COMPLETE: Circuit energized (Ammeter bypassed). Place Ammeter in series to measure loop current.";
            }
        }

        private void ValidateKCLParallel(
            CircuitValidationResult result,
            List<ElectricalComponent> components,
            List<WireConnection> connections,
            List<CircuitSocket> kclSockets,
            ElectricalTerminal supplyPos,
            ElectricalTerminal supplyNeg,
            ElectricalTerminal ammIn,
            ElectricalTerminal ammOut,
            ElectricalTerminal voltIn,
            ElectricalTerminal voltOut)
        {
            var juncIn = FindTerminalByRole(components, TerminalRole.JunctionInlet);
            var boardIn = FindTerminalByRole(components, TerminalRole.CircuitInlet);
            var juncGnd = FindTerminalByRole(components, TerminalRole.JunctionReturn);
            var boardGnd = FindTerminalByRole(components, TerminalRole.CircuitReturn);

            // A. Power feed to Junction Node Inflow or Circuit Board Input
            bool supplyToAmm = ammIn != null && IsTerminalConnectedTo(supplyPos, ammIn, connections);
            bool ammToJunc = ammOut != null && (IsTerminalConnectedTo(ammOut, juncIn, connections) || IsTerminalConnectedTo(ammOut, boardIn, connections));
            bool directSupplyToJunc = IsTerminalConnectedTo(supplyPos, juncIn, connections) || IsTerminalConnectedTo(supplyPos, boardIn, connections);

            if (supplyToAmm && ammToJunc)
            {
                result.isAmmeterInSeries = true;
            }
            else if (directSupplyToJunc)
            {
                result.isAmmeterInSeries = false;
            }
            else
            {
                result.faultType = CircuitFaultType.OpenCircuit;
                result.diagnosticMessage = "OPEN CIRCUIT: No power feed wire from Supply (+) to Junction Node IN (or Ammeter (+)).";
                result.missingConnectionDetails = "Wire Power Supply (+) to Junction Node IN (via Ammeter).";
                return;
            }

            // B. Parallel Branches Sockets
            if (kclSockets == null || kclSockets.Count < 2)
            {
                result.faultType = CircuitFaultType.MissingComponent;
                result.diagnosticMessage = "ERROR: Parallel branch sockets not found.";
                result.missingConnectionDetails = "Parallel branch sockets missing.";
                return;
            }

            var sA = kclSockets[0];
            var sB = kclSockets[1];

            if (sA == null || !sA.hasResistor || sA.currentResistor == null)
            {
                result.faultType = CircuitFaultType.MissingResistor;
                result.diagnosticMessage = "OPEN CIRCUIT: Parallel Branch A socket is empty! Insert resistor RA into Branch A.";
                result.missingConnectionDetails = "Seat Resistor RA into Parallel Branch A.";
                return;
            }

            if (sB == null || !sB.hasResistor || sB.currentResistor == null)
            {
                result.faultType = CircuitFaultType.MissingResistor;
                result.diagnosticMessage = "OPEN CIRCUIT: Parallel Branch B socket is empty! Insert resistor RB into Branch B.";
                result.missingConnectionDetails = "Seat Resistor RB into Parallel Branch B.";
                return;
            }

            var raComp = sA.currentResistor.GetComponent<ElectricalComponent>();
            var rbComp = sB.currentResistor.GetComponent<ElectricalComponent>();
            if (raComp != null) result.activeResistors.Add(raComp);
            if (rbComp != null) result.activeResistors.Add(rbComp);

            // C. Ground return to Supply (-)
            bool juncGndToSupply = IsTerminalConnectedTo(juncGnd, supplyNeg, connections) || IsTerminalConnectedTo(boardGnd, supplyNeg, connections);
            if (!juncGndToSupply)
            {
                result.faultType = CircuitFaultType.OpenCircuit;
                result.diagnosticMessage = "OPEN CIRCUIT: Missing ground return from Junction Node Ground (-) to Power Supply (-).";
                result.missingConnectionDetails = "Wire Junction Node Ground (-) to Power Supply (-).";
                return;
            }

            // D. Voltmeter parallel check
            if (voltIn != null && voltOut != null)
            {
                bool voltInHigh = IsTerminalConnectedTo(voltIn, supplyPos, connections) ||
                                  IsTerminalConnectedTo(voltIn, juncIn, connections) ||
                                  IsTerminalConnectedTo(voltIn, boardIn, connections);
                bool voltOutGnd = IsTerminalConnectedTo(voltOut, supplyNeg, connections) ||
                                  IsTerminalConnectedTo(voltOut, juncGnd, connections) ||
                                  IsTerminalConnectedTo(voltOut, boardGnd, connections);
                result.isVoltmeterInParallel = voltInHigh && voltOutGnd;
            }

            var sim = Object.FindFirstObjectByType<KirchhoffSimulator>();
            if (sim != null)
            {
                result.ammeterProbeTarget = sim.ammeterProbeTarget;
            }

            result.isValid = true;
            result.faultType = CircuitFaultType.None;
            result.diagnosticMessage = "PARALLEL JUNCTION COMPLETE: Circuit energized with parallel branches verified. KCL verified.";
        }

        private static bool IsTerminalConnectedTo(ElectricalTerminal a, ElectricalTerminal b, List<WireConnection> connections)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;

            foreach (var wire in connections)
            {
                if (wire == null || !wire.isConfirmed) continue;
                if ((wire.terminalA == a && wire.terminalB == b) || (wire.terminalA == b && wire.terminalB == a))
                {
                    return true;
                }
            }
            return false;
        }

        private static ElectricalTerminal FindTerminalByRole(List<ElectricalComponent> components, TerminalRole role)
        {
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var t = comp.GetTerminalByRole(role);
                if (t != null) return t;
            }
            return null;
        }
    }
}
