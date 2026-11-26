using UnityEngine;
using TMPro;

namespace VRContent.Circuit
{
    /// <summary>
    /// Attached to a CircuitSocket's GameObject (or any snap-slot object).
    /// Pulses a highlight color when active, shows a label, and reports snapped state.
    /// </summary>
    [DisallowMultipleComponent]
    public class SnapSlotHighlight : MonoBehaviour
    {
        [Header("Visuals")]
        public Renderer slotRenderer;           // The visual ring/disc for this slot
        public TextMeshPro slotLabel;           // Floating label above slot
        public string labelText = "R1 Slot";

        [Header("Colors")]
        public Color idleColor      = new Color(0.1f, 0.3f, 0.5f, 0.6f);
        public Color highlightColor = new Color(0f, 1f, 0.9f, 0.9f);    // Cyan pulse when active
        public Color successColor   = new Color(0f, 1f, 0.3f, 0.9f);    // Green when filled
        public Color errorColor     = new Color(1f, 0.2f, 0.1f, 0.9f);  // Red on wrong drop

        [Header("State")]
        public bool isHighlighted = false;
        public bool isFilled = false;

        private float _pulseTime = 0f;
        private Color _displayColor;
        private static readonly int ColorPropID = Shader.PropertyToID("_Color");
        private static readonly int EmissionPropID = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (slotLabel != null)
            {
                slotLabel.text = labelText;
            }
            ApplyColor(idleColor);
        }

        private void Update()
        {
            if (isFilled)
            {
                ApplyColor(successColor);
                return;
            }
            if (isHighlighted)
            {
                _pulseTime += Time.deltaTime * 3f;
                float pulse = (Mathf.Sin(_pulseTime) + 1f) * 0.5f; // 0..1
                _displayColor = Color.Lerp(idleColor, highlightColor, pulse);
                ApplyColor(_displayColor);
            }
            else
            {
                _pulseTime = 0f;
                ApplyColor(idleColor);
            }
        }

        public void SetHighlight(bool active)
        {
            isHighlighted = active;
            if (slotLabel != null)
            {
                slotLabel.gameObject.SetActive(true);
                slotLabel.color = active ? new Color(0f, 1f, 0.9f) : new Color(0.6f, 0.6f, 0.6f);
            }
        }

        public void SetFilled(bool filled)
        {
            isFilled = filled;
            isHighlighted = false;
            ApplyColor(filled ? successColor : idleColor);
            if (slotLabel != null)
            {
                slotLabel.text = filled ? ("✓ " + labelText) : labelText;
                slotLabel.color = filled ? new Color(0f, 1f, 0.3f) : new Color(0.6f, 0.6f, 0.6f);
            }
        }

        public void FlashError()
        {
            StopAllCoroutines();
            StartCoroutine(FlashErrorRoutine());
        }

        private System.Collections.IEnumerator FlashErrorRoutine()
        {
            for (int i = 0; i < 3; i++)
            {
                ApplyColor(errorColor);
                yield return new WaitForSeconds(0.12f);
                ApplyColor(idleColor);
                yield return new WaitForSeconds(0.12f);
            }
        }

        private void ApplyColor(Color c)
        {
            if (slotRenderer == null) return;
            slotRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorPropID, c);
            _mpb.SetColor(EmissionPropID, c * 1.5f);
            slotRenderer.SetPropertyBlock(_mpb);
        }
    }
}
