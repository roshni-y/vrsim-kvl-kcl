using UnityEngine;
using System.Collections.Generic;
using System;

namespace VRContent.Circuit
{
    public enum ElectricalComponentType
    {
        DCPowerSource,
        Resistor,
        Ammeter,
        Voltmeter,
        JunctionNode,
        CircuitBoard
    }

    [DisallowMultipleComponent]
    public class ElectricalComponent : MonoBehaviour
    {
        [Header("Component Identity")]
        public string componentId;
        public ElectricalComponentType componentType = ElectricalComponentType.Resistor;
        public string displayName = "Component";

        [Header("Physical / Electrical Parameters")]
        [Tooltip("Actual resistance in Ohms. Resistors: 100, 200, 300. Ammeter: ~0.001. Voltmeter: ~10M")]
        public float resistance = 100f;

        [Tooltip("Actual source voltage in Volts (e.g. 6V or 12V for DC Power Supply)")]
        public float voltage = 0f;

        [Tooltip("For active sources: whether source is powered ON")]
        public bool isEnergized = true;

        [Header("Terminals")]
        public List<ElectricalTerminal> terminals = new List<ElectricalTerminal>();

        public event Action<ElectricalComponent> OnComponentStateChanged;

        private void Awake()
        {
            if (string.IsNullOrEmpty(componentId))
            {
                componentId = gameObject.name;
            }
            CollectTerminals();
        }

        public void CollectTerminals()
        {
            terminals.Clear();
            terminals.AddRange(GetComponentsInChildren<ElectricalTerminal>(true));
            foreach (var t in terminals)
            {
                t.parentComponent = this;
            }
        }

        public void SetResistance(float r)
        {
            resistance = r;
            OnComponentStateChanged?.Invoke(this);
        }

        public void SetVoltage(float v)
        {
            voltage = v;
            OnComponentStateChanged?.Invoke(this);
        }

        public void SetEnergized(bool energized)
        {
            isEnergized = energized;
            OnComponentStateChanged?.Invoke(this);
        }

        public ElectricalTerminal GetTerminalByRole(TerminalRole role)
        {
            if (terminals == null || terminals.Count == 0) CollectTerminals();
            for (int i = 0; i < terminals.Count; i++)
            {
                if (terminals[i] != null && terminals[i].role == role) return terminals[i];
            }
            return null;
        }

        public ElectricalTerminal GetTerminalByPolarity(TerminalPolarity polarity)
        {
            if (terminals == null || terminals.Count == 0) CollectTerminals();
            for (int i = 0; i < terminals.Count; i++)
            {
                if (terminals[i] != null && terminals[i].polarity == polarity) return terminals[i];
            }
            return null;
        }

        public ElectricalTerminal GetTerminal(string id)
        {
            if (terminals == null || terminals.Count == 0) CollectTerminals();
            for (int i = 0; i < terminals.Count; i++)
            {
                if (terminals[i] != null && terminals[i].terminalId == id) return terminals[i];
            }
            return null;
        }
    }
}
