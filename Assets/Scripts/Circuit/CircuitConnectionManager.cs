using UnityEngine;
using System.Collections.Generic;

namespace VRContent.Circuit
{
    public class CircuitConnectionManager : MonoBehaviour
    {
        private static CircuitConnectionManager _instance;
        public static CircuitConnectionManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Object.FindFirstObjectByType<CircuitConnectionManager>();
                return _instance;
            }
        }

        public KirchhoffSimulator simulator;

        [Header("Registered Terminals")]
        public List<CircuitTerminal> allTerminals = new List<CircuitTerminal>();

        [Header("Registered Wires")]
        public List<FlexibleWire> allWires = new List<FlexibleWire>();

        [Header("Global Electrical State")]
        public bool hasShortCircuit = false;
        public bool isPowerSuppliedToBoard = false;
        public bool isGroundConnected = false;
        public bool isAmmeterInSeries = false;
        public bool isVoltmeterInParallel = false;
        public bool hasSeriesVoltmeterFault = false;
        public bool hasAmmeterParallelShort = false;

        // Legacy compatibility property
        public bool isAmmeterInLoop => isAmmeterInSeries;
        public bool isVoltmeterConnected => isVoltmeterInParallel;

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            if (simulator == null)
            {
                simulator = Object.FindFirstObjectByType<KirchhoffSimulator>();
            }
            CollectTerminalsAndWires();
            OnTerminalConnectionChanged();
        }

        public void CollectTerminalsAndWires()
        {
            allTerminals.Clear();
            allTerminals.AddRange(Object.FindObjectsByType<CircuitTerminal>(FindObjectsSortMode.None));

            allWires.Clear();
            allWires.AddRange(Object.FindObjectsByType<FlexibleWire>(FindObjectsSortMode.None));
        }

        private static bool IsInflowTerminal(TerminalType t)
        {
            return t == TerminalType.Circuit_Input ||
                   t == TerminalType.Junction_Inflow ||
                   t == TerminalType.Junction_BranchA ||
                   t == TerminalType.Junction_BranchB;
        }

        private static bool IsReturnTerminal(TerminalType t)
        {
            return t == TerminalType.Circuit_Ground ||
                   t == TerminalType.Junction_Return;
        }

        public void OnTerminalConnectionChanged()
        {
            if (allWires == null || allWires.Count == 0 || allTerminals == null || allTerminals.Count == 0)
            {
                CollectTerminalsAndWires();
            }

            hasShortCircuit = false;
            isPowerSuppliedToBoard = false;
            isGroundConnected = false;
            isAmmeterInSeries = false;
            isVoltmeterInParallel = false;
            hasSeriesVoltmeterFault = false;
            hasAmmeterParallelShort = false;

            // Track terminal linkages
            bool supplyPosConnectedToBoardIn = false;
            bool supplyPosConnectedToAmmeterIn = false;
            bool ammeterOutConnectedToBoardIn = false;
            bool boardGndConnectedToSupplyNeg = false;

            bool voltInConnectedToHigh = false;
            bool voltOutConnectedToGnd = false;
            bool voltInConnectedToSupplyPos = false;
            bool voltOutConnectedToBoardIn = false;

            // Evaluate each wire
            foreach (var wire in allWires)
            {
                if (wire == null || wire.plugA == null || wire.plugB == null) continue;

                var termA = wire.plugA.currentTerminal;
                var termB = wire.plugB.currentTerminal;

                if (termA != null && termB != null)
                {
                    TerminalType tA = termA.terminalType;
                    TerminalType tB = termB.terminalType;

                    // 1. Direct dead short across supply: Supply (+) to Supply (-)
                    if ((tA == TerminalType.Positive_Supply && tB == TerminalType.Negative_Ground) ||
                        (tB == TerminalType.Positive_Supply && tA == TerminalType.Negative_Ground))
                    {
                        hasShortCircuit = true;
                        termA.SetConnectionState(ConnectionState.Invalid);
                        termB.SetConnectionState(ConnectionState.Invalid);
                        continue;
                    }

                    // 2. Ammeter incorrectly connected in parallel across supply rails (dead short)
                    if ((tA == TerminalType.Meter_Ammeter_In && tB == TerminalType.Negative_Ground) ||
                        (tB == TerminalType.Meter_Ammeter_In && tA == TerminalType.Negative_Ground) ||
                        (tA == TerminalType.Meter_Ammeter_Out && tB == TerminalType.Negative_Ground && supplyPosConnectedToAmmeterIn))
                    {
                        hasAmmeterParallelShort = true;
                        hasShortCircuit = true;
                        termA.SetConnectionState(ConnectionState.Invalid);
                        termB.SetConnectionState(ConnectionState.Invalid);
                        continue;
                    }

                    // 3. Voltmeter incorrectly wired in series (e.g. Supply (+) -> Voltmeter (+), and Voltmeter (-) -> Board Input)
                    if ((tA == TerminalType.Meter_Voltmeter_Out && IsInflowTerminal(tB)) ||
                        (tB == TerminalType.Meter_Voltmeter_Out && IsInflowTerminal(tA)))
                    {
                        voltOutConnectedToBoardIn = true;
                    }
                    if ((tA == TerminalType.Meter_Voltmeter_In && tB == TerminalType.Positive_Supply) ||
                        (tB == TerminalType.Meter_Voltmeter_In && tA == TerminalType.Positive_Supply))
                    {
                        voltInConnectedToSupplyPos = true;
                    }

                    // 4. Power feeds
                    if ((tA == TerminalType.Positive_Supply && IsInflowTerminal(tB)) ||
                        (tB == TerminalType.Positive_Supply && IsInflowTerminal(tA)))
                    {
                        supplyPosConnectedToBoardIn = true;
                    }

                    if ((tA == TerminalType.Positive_Supply && tB == TerminalType.Meter_Ammeter_In) ||
                        (tB == TerminalType.Positive_Supply && tA == TerminalType.Meter_Ammeter_In))
                    {
                        supplyPosConnectedToAmmeterIn = true;
                    }

                    if ((tA == TerminalType.Meter_Ammeter_Out && IsInflowTerminal(tB)) ||
                        (tB == TerminalType.Meter_Ammeter_Out && IsInflowTerminal(tA)))
                    {
                        ammeterOutConnectedToBoardIn = true;
                    }

                    // 5. Ground return
                    if ((tA == TerminalType.Negative_Ground && IsReturnTerminal(tB)) ||
                        (tB == TerminalType.Negative_Ground && IsReturnTerminal(tA)))
                    {
                        boardGndConnectedToSupplyNeg = true;
                    }

                    // 6. Voltmeter in parallel connections (across High and Ground)
                    if ((tA == TerminalType.Meter_Voltmeter_In && (IsInflowTerminal(tB) || tB == TerminalType.Positive_Supply)) ||
                        (tB == TerminalType.Meter_Voltmeter_In && (IsInflowTerminal(tA) || tA == TerminalType.Positive_Supply)))
                    {
                        voltInConnectedToHigh = true;
                    }

                    if ((tA == TerminalType.Meter_Voltmeter_Out && (IsReturnTerminal(tB) || tB == TerminalType.Negative_Ground)) ||
                        (tB == TerminalType.Meter_Voltmeter_Out && (IsReturnTerminal(tA) || tA == TerminalType.Negative_Ground)))
                    {
                        voltOutConnectedToGnd = true;
                    }

                    // 7. Voltmeter across specific test points (R1 & R2)
                    if ((tA == TerminalType.Meter_Voltmeter_In && tB == TerminalType.TestPoint_R1_Pos) ||
                        (tB == TerminalType.Meter_Voltmeter_In && tA == TerminalType.TestPoint_R1_Pos))
                    {
                        voltInConnectedToHigh = true;
                        if (simulator != null) simulator.voltmeterProbeTarget = VoltmeterProbeTarget.Resistor1;
                    }
                    if ((tA == TerminalType.Meter_Voltmeter_Out && tB == TerminalType.TestPoint_R1_Neg) ||
                        (tB == TerminalType.Meter_Voltmeter_Out && tA == TerminalType.TestPoint_R1_Neg))
                    {
                        voltOutConnectedToGnd = true;
                    }

                    if ((tA == TerminalType.Meter_Voltmeter_In && tB == TerminalType.TestPoint_R2_Pos) ||
                        (tB == TerminalType.Meter_Voltmeter_In && tA == TerminalType.TestPoint_R2_Pos))
                    {
                        voltInConnectedToHigh = true;
                        if (simulator != null) simulator.voltmeterProbeTarget = VoltmeterProbeTarget.Resistor2;
                    }
                    if ((tA == TerminalType.Meter_Voltmeter_Out && tB == TerminalType.TestPoint_R2_Neg) ||
                        (tB == TerminalType.Meter_Voltmeter_Out && tA == TerminalType.TestPoint_R2_Neg))
                    {
                        voltOutConnectedToGnd = true;
                    }

                    termA.SetConnectionState(ConnectionState.Valid);
                    termB.SetConnectionState(ConnectionState.Valid);
                }
                else if (termA != null)
                {
                    termA.SetConnectionState(ConnectionState.Disconnected);
                }
                else if (termB != null)
                {
                    termB.SetConnectionState(ConnectionState.Disconnected);
                }
            }

            // Check if Voltmeter was mistakenly placed in series
            if (voltInConnectedToSupplyPos && voltOutConnectedToBoardIn)
            {
                hasSeriesVoltmeterFault = true;
            }

            // Ammeter is in series when supply feeds through Ammeter into circuit input
            if (supplyPosConnectedToAmmeterIn && ammeterOutConnectedToBoardIn && !hasShortCircuit)
            {
                isAmmeterInSeries = true;
                isPowerSuppliedToBoard = true;
            }
            else if (supplyPosConnectedToBoardIn && !hasSeriesVoltmeterFault && !hasShortCircuit)
            {
                // Direct feed to board without ammeter in series
                isPowerSuppliedToBoard = true;
                isAmmeterInSeries = false;
            }
            else
            {
                isPowerSuppliedToBoard = false;
                isAmmeterInSeries = false;
            }

            isGroundConnected = boardGndConnectedToSupplyNeg && !hasShortCircuit;

            // Voltmeter is in parallel when connected across high potential and GND
            if (voltInConnectedToHigh && voltOutConnectedToGnd && !hasSeriesVoltmeterFault && !hasShortCircuit)
            {
                isVoltmeterInParallel = true;
            }
            else
            {
                isVoltmeterInParallel = false;
            }

            // Update terminal visual states
            foreach (var term in allTerminals)
            {
                if (term != null && term.connectedPlug == null)
                {
                    term.SetConnectionState(ConnectionState.Disconnected);
                }
            }

            // Update wire visual appearances (Requirement 7)
            foreach (var wire in allWires)
            {
                if (wire != null)
                {
                    bool wireConnected = (wire.plugA != null && wire.plugA.currentTerminal != null) &&
                                         (wire.plugB != null && wire.plugB.currentTerminal != null);
                    bool energized = isPowerSuppliedToBoard && wireConnected && !hasShortCircuit;
                    wire.UpdateWireAppearance(energized, hasShortCircuit);
                }
            }

            // Notify simulator
            if (simulator == null)
            {
                simulator = Object.FindFirstObjectByType<KirchhoffSimulator>();
            }

            if (simulator != null)
            {
                simulator.RecalculateCircuit();
            }
        }

        // Helper to programmatically configure standard valid wiring
        public void SetStandardValidWiring(bool withAmmeterInSeries, bool withVoltmeterInParallel)
        {
            CollectTerminalsAndWires();

            var supplyPos = allTerminals.Find(t => t.terminalType == TerminalType.Positive_Supply);
            var supplyNeg = allTerminals.Find(t => t.terminalType == TerminalType.Negative_Ground);
            var ammeterIn = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Ammeter_In);
            var ammeterOut = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Ammeter_Out);
            var voltIn = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Voltmeter_In);
            var voltOut = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Voltmeter_Out);

            bool isKCL = (simulator != null && simulator.currentMode == CircuitMode.KCL_Parallel);

            CircuitTerminal boardIn;
            CircuitTerminal boardGnd;

            if (isKCL)
            {
                boardIn = allTerminals.Find(t => t.terminalType == TerminalType.Junction_Inflow);
                if (boardIn == null) boardIn = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Input);
                boardGnd = allTerminals.Find(t => t.terminalType == TerminalType.Junction_Return);
                if (boardGnd == null) boardGnd = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Ground);
            }
            else
            {
                boardIn = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Input);
                boardGnd = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Ground);
            }

            // Disconnect all wires first
            foreach (var w in allWires)
            {
                if (w.plugA != null && w.plugA.currentTerminal != null)
                    w.plugA.currentTerminal.DisconnectPlug();
                if (w.plugB != null && w.plugB.currentTerminal != null)
                    w.plugB.currentTerminal.DisconnectPlug();
            }

            var wireSupplyPos = allWires.Find(w => w.name.Contains("Supply_VCC"));
            var wireSupplyNeg = allWires.Find(w => w.name.Contains("Supply_GND"));
            var wireAmmRed = allWires.Find(w => w.name.Contains("Ammeter") && w.name.Contains("Red"));
            var wireAmmBlack = allWires.Find(w => w.name.Contains("Ammeter") && w.name.Contains("Black"));
            var wireVoltRed = allWires.Find(w => w.name.Contains("Voltmeter") && w.name.Contains("Red"));
            var wireVoltBlack = allWires.Find(w => w.name.Contains("Voltmeter") && w.name.Contains("Black"));

            // 1. Ground wire: Board GND -> Supply (-)
            if (wireSupplyNeg != null && supplyNeg != null && boardGnd != null)
            {
                ConnectWireEnds(wireSupplyNeg, supplyNeg, boardGnd);
            }

            // 2. Power Feed
            if (withAmmeterInSeries)
            {
                // Series Ammeter: Supply (+) -> Ammeter (+) -> Ammeter (-) -> Board IN
                if (wireSupplyPos != null && supplyPos != null && ammeterIn != null)
                {
                    ConnectWireEnds(wireSupplyPos, supplyPos, ammeterIn);
                }
                if (wireAmmBlack != null && ammeterOut != null && boardIn != null)
                {
                    ConnectWireEnds(wireAmmBlack, ammeterOut, boardIn);
                }
            }
            else
            {
                // Direct feed (Ammeter bypassed)
                if (wireSupplyPos != null && supplyPos != null && boardIn != null)
                {
                    ConnectWireEnds(wireSupplyPos, supplyPos, boardIn);
                }
            }

            // 3. Voltmeter in Parallel
            if (withVoltmeterInParallel)
            {
                if (isKCL)
                {
                    // Across parallel junction network
                    if (wireVoltRed != null && voltIn != null && boardIn != null)
                    {
                        ConnectWireEnds(wireVoltRed, voltIn, boardIn);
                    }
                    if (wireVoltBlack != null && voltOut != null && boardGnd != null)
                    {
                        ConnectWireEnds(wireVoltBlack, voltOut, boardGnd);
                    }
                }
                else
                {
                    // Probed across R1 test points (matches 04_KVL_Circuit.png)
                    var tpR1Pos = allTerminals.Find(t => t.terminalType == TerminalType.TestPoint_R1_Pos);
                    var tpR1Neg = allTerminals.Find(t => t.terminalType == TerminalType.TestPoint_R1_Neg);
                    var targetPos = tpR1Pos != null ? tpR1Pos : boardIn;
                    var targetNeg = tpR1Neg != null ? tpR1Neg : boardGnd;

                    if (wireVoltRed != null && voltIn != null && targetPos != null)
                    {
                        ConnectWireEnds(wireVoltRed, voltIn, targetPos);
                    }
                    if (wireVoltBlack != null && voltOut != null && targetNeg != null)
                    {
                        ConnectWireEnds(wireVoltBlack, voltOut, targetNeg);
                    }
                }
            }

            OnTerminalConnectionChanged();
        }

        public void ConnectVoltmeterAcrossR1()
        {
            CollectTerminalsAndWires();
            if (simulator != null) simulator.voltmeterProbeTarget = VoltmeterProbeTarget.Resistor1;

            var wireVoltRed = allWires.Find(w => w.name.Contains("Voltmeter") && w.name.Contains("Red"));
            var wireVoltBlack = allWires.Find(w => w.name.Contains("Voltmeter") && w.name.Contains("Black"));
            var voltIn = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Voltmeter_In);
            var voltOut = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Voltmeter_Out);
            var tpR1Pos = allTerminals.Find(t => t.terminalType == TerminalType.TestPoint_R1_Pos);
            var tpR1Neg = allTerminals.Find(t => t.terminalType == TerminalType.TestPoint_R1_Neg);
            var boardIn = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Input);

            // Disconnect voltmeter wires first
            if (wireVoltRed != null && wireVoltRed.plugB != null && wireVoltRed.plugB.currentTerminal != null)
                wireVoltRed.plugB.currentTerminal.DisconnectPlug();
            if (wireVoltBlack != null && wireVoltBlack.plugB != null && wireVoltBlack.plugB.currentTerminal != null)
                wireVoltBlack.plugB.currentTerminal.DisconnectPlug();

            var targetPos = tpR1Pos != null ? tpR1Pos : boardIn;
            var targetNeg = tpR1Neg;

            if (wireVoltRed != null && voltIn != null && targetPos != null)
                ConnectWireEnds(wireVoltRed, voltIn, targetPos);
            if (wireVoltBlack != null && voltOut != null && targetNeg != null)
                ConnectWireEnds(wireVoltBlack, voltOut, targetNeg);

            OnTerminalConnectionChanged();
        }

        public void ConnectVoltmeterAcrossR2()
        {
            CollectTerminalsAndWires();
            if (simulator != null) simulator.voltmeterProbeTarget = VoltmeterProbeTarget.Resistor2;

            var wireVoltRed = allWires.Find(w => w.name.Contains("Voltmeter") && w.name.Contains("Red"));
            var wireVoltBlack = allWires.Find(w => w.name.Contains("Voltmeter") && w.name.Contains("Black"));
            var voltIn = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Voltmeter_In);
            var voltOut = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Voltmeter_Out);
            var tpR2Pos = allTerminals.Find(t => t.terminalType == TerminalType.TestPoint_R2_Pos);
            var tpR2Neg = allTerminals.Find(t => t.terminalType == TerminalType.TestPoint_R2_Neg);
            var boardGnd = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Ground);

            // Disconnect voltmeter wires first
            if (wireVoltRed != null && wireVoltRed.plugB != null && wireVoltRed.plugB.currentTerminal != null)
                wireVoltRed.plugB.currentTerminal.DisconnectPlug();
            if (wireVoltBlack != null && wireVoltBlack.plugB != null && wireVoltBlack.plugB.currentTerminal != null)
                wireVoltBlack.plugB.currentTerminal.DisconnectPlug();

            var targetPos = tpR2Pos;
            var targetNeg = tpR2Neg != null ? tpR2Neg : boardGnd;

            if (wireVoltRed != null && voltIn != null && targetPos != null)
                ConnectWireEnds(wireVoltRed, voltIn, targetPos);
            if (wireVoltBlack != null && voltOut != null && targetNeg != null)
                ConnectWireEnds(wireVoltBlack, voltOut, targetNeg);

            OnTerminalConnectionChanged();
        }

        public void ConnectVoltmeterAcrossTotal()
        {
            CollectTerminalsAndWires();
            if (simulator != null) simulator.voltmeterProbeTarget = VoltmeterProbeTarget.TotalCircuit;

            var wireVoltRed = allWires.Find(w => w.name.Contains("Voltmeter") && w.name.Contains("Red"));
            var wireVoltBlack = allWires.Find(w => w.name.Contains("Voltmeter") && w.name.Contains("Black"));
            var voltIn = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Voltmeter_In);
            var voltOut = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Voltmeter_Out);
            var boardIn = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Input);
            var boardGnd = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Ground);

            // Disconnect voltmeter wires first
            if (wireVoltRed != null && wireVoltRed.plugB != null && wireVoltRed.plugB.currentTerminal != null)
                wireVoltRed.plugB.currentTerminal.DisconnectPlug();
            if (wireVoltBlack != null && wireVoltBlack.plugB != null && wireVoltBlack.plugB.currentTerminal != null)
                wireVoltBlack.plugB.currentTerminal.DisconnectPlug();

            if (wireVoltRed != null && voltIn != null && boardIn != null)
                ConnectWireEnds(wireVoltRed, voltIn, boardIn);
            if (wireVoltBlack != null && voltOut != null && boardGnd != null)
                ConnectWireEnds(wireVoltBlack, voltOut, boardGnd);

            OnTerminalConnectionChanged();
        }

        public void ConnectAmmeterMainInflow()
        {
            CollectTerminalsAndWires();
            if (simulator != null) simulator.ammeterProbeTarget = AmmeterProbeTarget.MainInflow;

            var supplyPos = allTerminals.Find(t => t.terminalType == TerminalType.Positive_Supply);
            var ammeterIn = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Ammeter_In);
            var ammeterOut = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Ammeter_Out);
            var juncIn = allTerminals.Find(t => t.terminalType == TerminalType.Junction_Inflow);
            var boardIn = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Input);
            var targetIn = juncIn != null ? juncIn : boardIn;

            var wireSupplyPos = allWires.Find(w => w.name.Contains("Supply_VCC"));
            var wireAmmBlack = allWires.Find(w => w.name.Contains("Ammeter") && w.name.Contains("Black"));

            if (wireSupplyPos != null && wireSupplyPos.plugB != null && wireSupplyPos.plugB.currentTerminal != null)
                wireSupplyPos.plugB.currentTerminal.DisconnectPlug();
            if (wireAmmBlack != null && wireAmmBlack.plugB != null && wireAmmBlack.plugB.currentTerminal != null)
                wireAmmBlack.plugB.currentTerminal.DisconnectPlug();

            if (wireSupplyPos != null && supplyPos != null && ammeterIn != null)
                ConnectWireEnds(wireSupplyPos, supplyPos, ammeterIn);
            if (wireAmmBlack != null && ammeterOut != null && targetIn != null)
                ConnectWireEnds(wireAmmBlack, ammeterOut, targetIn);

            OnTerminalConnectionChanged();
        }

        public void ConnectAmmeterBranch1()
        {
            CollectTerminalsAndWires();
            if (simulator != null) simulator.ammeterProbeTarget = AmmeterProbeTarget.Branch1;

            var supplyPos = allTerminals.Find(t => t.terminalType == TerminalType.Positive_Supply);
            var ammeterIn = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Ammeter_In);
            var ammeterOut = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Ammeter_Out);
            var branchA = allTerminals.Find(t => t.terminalType == TerminalType.Junction_BranchA);
            var juncIn = allTerminals.Find(t => t.terminalType == TerminalType.Junction_Inflow);
            var boardIn = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Input);
            var targetA = branchA != null ? branchA : (juncIn != null ? juncIn : boardIn);

            var wireSupplyPos = allWires.Find(w => w.name.Contains("Supply_VCC"));
            var wireAmmBlack = allWires.Find(w => w.name.Contains("Ammeter") && w.name.Contains("Black"));

            if (wireSupplyPos != null && wireSupplyPos.plugB != null && wireSupplyPos.plugB.currentTerminal != null)
                wireSupplyPos.plugB.currentTerminal.DisconnectPlug();
            if (wireAmmBlack != null && wireAmmBlack.plugB != null && wireAmmBlack.plugB.currentTerminal != null)
                wireAmmBlack.plugB.currentTerminal.DisconnectPlug();

            if (wireSupplyPos != null && supplyPos != null && ammeterIn != null)
                ConnectWireEnds(wireSupplyPos, supplyPos, ammeterIn);
            if (wireAmmBlack != null && ammeterOut != null && targetA != null)
                ConnectWireEnds(wireAmmBlack, ammeterOut, targetA);

            OnTerminalConnectionChanged();
        }

        public void ConnectAmmeterBranch2()
        {
            CollectTerminalsAndWires();
            if (simulator != null) simulator.ammeterProbeTarget = AmmeterProbeTarget.Branch2;

            var supplyPos = allTerminals.Find(t => t.terminalType == TerminalType.Positive_Supply);
            var ammeterIn = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Ammeter_In);
            var ammeterOut = allTerminals.Find(t => t.terminalType == TerminalType.Meter_Ammeter_Out);
            var branchB = allTerminals.Find(t => t.terminalType == TerminalType.Junction_BranchB);
            var juncIn = allTerminals.Find(t => t.terminalType == TerminalType.Junction_Inflow);
            var boardIn = allTerminals.Find(t => t.terminalType == TerminalType.Circuit_Input);
            var targetB = branchB != null ? branchB : (juncIn != null ? juncIn : boardIn);

            var wireSupplyPos = allWires.Find(w => w.name.Contains("Supply_VCC"));
            var wireAmmBlack = allWires.Find(w => w.name.Contains("Ammeter") && w.name.Contains("Black"));

            if (wireSupplyPos != null && wireSupplyPos.plugB != null && wireSupplyPos.plugB.currentTerminal != null)
                wireSupplyPos.plugB.currentTerminal.DisconnectPlug();
            if (wireAmmBlack != null && wireAmmBlack.plugB != null && wireAmmBlack.plugB.currentTerminal != null)
                wireAmmBlack.plugB.currentTerminal.DisconnectPlug();

            if (wireSupplyPos != null && supplyPos != null && ammeterIn != null)
                ConnectWireEnds(wireSupplyPos, supplyPos, ammeterIn);
            if (wireAmmBlack != null && ammeterOut != null && targetB != null)
                ConnectWireEnds(wireAmmBlack, ammeterOut, targetB);

            OnTerminalConnectionChanged();
        }

        public void ResetConnections()
        {
            CollectTerminalsAndWires();

            foreach (var w in allWires)
            {
                if (w == null) continue;
                if (w.plugA != null && w.plugA.currentTerminal != null)
                {
                    w.plugA.currentTerminal.DisconnectPlug();
                }
                if (w.plugB != null && w.plugB.currentTerminal != null)
                {
                    w.plugB.currentTerminal.DisconnectPlug();
                }
            }

            foreach (var term in allTerminals)
            {
                if (term != null)
                {
                    term.SetConnectionState(ConnectionState.Disconnected);
                }
            }

            hasShortCircuit = false;
            isPowerSuppliedToBoard = false;
            isGroundConnected = false;
            isAmmeterInSeries = false;
            isVoltmeterInParallel = false;
            hasSeriesVoltmeterFault = false;
            hasAmmeterParallelShort = false;

            if (simulator != null)
            {
                simulator.RecalculateCircuit();
            }
        }

        private void ConnectWireEnds(FlexibleWire wire, CircuitTerminal termA, CircuitTerminal termB)
        {
            if (wire == null || termA == null || termB == null) return;
            if (wire.plugA != null)
            {
                wire.plugA.transform.position = termA.transform.position;
                termA.ConnectPlug(wire.plugA);
            }
            if (wire.plugB != null)
            {
                wire.plugB.transform.position = termB.transform.position;
                termB.ConnectPlug(wire.plugB);
            }
        }
    }
}