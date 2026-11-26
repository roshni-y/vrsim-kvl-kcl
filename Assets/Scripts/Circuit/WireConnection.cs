using UnityEngine;
using System;

namespace VRContent.Circuit
{
    [DisallowMultipleComponent]
    public class WireConnection : MonoBehaviour
    {
        [Header("Wire Identity")]
        public string wireId;
        public WirePolarity wirePolarity = WirePolarity.Red_Positive;
        public float wireResistance = 0.001f; // ~0 Ohms ideal conductor

        [Header("Connected Terminals")]
        public ElectricalTerminal terminalA;
        public ElectricalTerminal terminalB;

        [Header("Endpoints")]
        public WirePlug plugA;
        public WirePlug plugB;

        public bool isConfirmed => terminalA != null && terminalB != null;

        public event Action<WireConnection> OnWireConnectionStateChanged;

        private void Awake()
        {
            if (string.IsNullOrEmpty(wireId))
            {
                wireId = gameObject.name;
            }
            if (plugA == null || plugB == null)
            {
                var plugs = GetComponentsInChildren<WirePlug>(true);
                if (plugs.Length >= 2)
                {
                    plugA = plugs[0];
                    plugB = plugs[1];
                }
            }
        }

        public void Establish(ElectricalTerminal a, ElectricalTerminal b)
        {
            terminalA = a;
            terminalB = b;
            if (a != null) a.Connect(this);
            if (b != null) b.Connect(this);
            OnWireConnectionStateChanged?.Invoke(this);
        }

        public void SetTerminal(WirePlug plug, ElectricalTerminal terminal)
        {
            if (plug == plugA)
            {
                if (terminalA != null && terminalA != terminal) terminalA.Disconnect();
                terminalA = terminal;
                if (terminal != null) terminal.Connect(this);
            }
            else if (plug == plugB)
            {
                if (terminalB != null && terminalB != terminal) terminalB.Disconnect();
                terminalB = terminal;
                if (terminal != null) terminal.Connect(this);
            }

            OnWireConnectionStateChanged?.Invoke(this);
        }

        public void ClearTerminal(WirePlug plug)
        {
            if (plug == plugA)
            {
                if (terminalA != null) terminalA.Disconnect();
                terminalA = null;
            }
            else if (plug == plugB)
            {
                if (terminalB != null) terminalB.Disconnect();
                terminalB = null;
            }

            OnWireConnectionStateChanged?.Invoke(this);
        }

        public void Sever()
        {
            if (terminalA != null) terminalA.Disconnect();
            if (terminalB != null) terminalB.Disconnect();
            terminalA = null;
            terminalB = null;
            OnWireConnectionStateChanged?.Invoke(this);
        }

        public ElectricalTerminal GetOppositeTerminal(ElectricalTerminal from)
        {
            if (from == terminalA) return terminalB;
            if (from == terminalB) return terminalA;
            return null;
        }
    }
}
