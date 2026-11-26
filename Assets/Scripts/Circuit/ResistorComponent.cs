using UnityEngine;
using TMPro;

namespace VRContent.Circuit
{
    public class ResistorComponent : MonoBehaviour
    {
        [Header("Resistor Properties")]
        [Tooltip("Resistance in Ohms")]
        public float resistance = 100f;
        public string resistorLabel = "100 Ω";

        [Header("Visuals")]
        public TextMeshPro labelText;
        public Renderer bodyRenderer;

        private void Start()
        {
            UpdateVisuals();
        }

        public void SetResistance(float newResistance, string label)
        {
            resistance = newResistance;
            resistorLabel = label;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (labelText != null)
            {
                labelText.text = resistorLabel;
            }
        }
    }
}
