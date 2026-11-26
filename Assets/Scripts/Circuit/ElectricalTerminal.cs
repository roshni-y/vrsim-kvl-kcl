using UnityEngine;
using System;

namespace VRContent.Circuit
{
    public enum TerminalPolarity
    {
        Positive,
        Negative_Ground,
        Signal,
        Neutral
    }

    public enum TerminalRole
    {
        SupplyOutputPositive,
        SupplyOutputGround,
        CircuitInlet,
        CircuitReturn,
        MeterInputPositive,
        MeterInputCommon,
        BranchTerminalA,
        BranchTerminalB,
        JunctionInlet,
        JunctionReturn,
        ResistorLead1,
        ResistorLead2,
        TestPointR1Positive,
        TestPointR1Negative,
        TestPointR2Positive,
        TestPointR2Negative
    }

    [DisallowMultipleComponent]
    public class ElectricalTerminal : MonoBehaviour
    {
        [Header("Terminal Identity")]
        public string terminalId;
        public TerminalPolarity polarity = TerminalPolarity.Positive;
        public TerminalRole role = TerminalRole.CircuitInlet;
        public string displayName = "Terminal";

        [Header("Parent Component")]
        public ElectricalComponent parentComponent;

        [Header("Connection State")]
        public WireConnection connectedWire;

        public bool isConnected => connectedWire != null && connectedWire.isConfirmed;

        public event Action<ElectricalTerminal, WireConnection> OnConnectionChanged;

        private void Awake()
        {
            if (string.IsNullOrEmpty(terminalId))
            {
                terminalId = gameObject.name;
            }
            if (parentComponent == null)
            {
                parentComponent = GetComponentInParent<ElectricalComponent>();
            }
        }

        public bool CanConnect(WireConnection wire, out string reason)
        {
            reason = string.Empty;
            if (wire == null)
            {
                reason = "Wire is null.";
                return false;
            }

            if (connectedWire != null && connectedWire != wire)
            {
                reason = $"Terminal '{displayName}' is already connected to wire '{connectedWire.wireId}'.";
                return false;
            }

            // Polarity compatibility
            if (polarity == TerminalPolarity.Positive && wire.wirePolarity == WirePolarity.Black_Ground)
            {
                reason = $"Incompatible polarity: Cannot connect Ground (Black) wire to Positive terminal '{displayName}'.";
                return false;
            }

            if (polarity == TerminalPolarity.Negative_Ground && wire.wirePolarity == WirePolarity.Red_Positive)
            {
                reason = $"Incompatible polarity: Cannot connect Positive (Red) wire to Ground/COM terminal '{displayName}'.";
                return false;
            }

            return true;
        }

        public void Connect(WireConnection wire)
        {
            if (wire == null) return;
            connectedWire = wire;
            OnConnectionChanged?.Invoke(this, wire);
        }

        public void Disconnect()
        {
            var prevWire = connectedWire;
            connectedWire = null;
            OnConnectionChanged?.Invoke(this, prevWire);
        }
    }
}
