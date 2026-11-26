using UnityEngine;

namespace VRContent.Circuit
{
    [RequireComponent(typeof(LineRenderer))]
    public class FlexibleWire : MonoBehaviour
    {
        [Header("Endpoints")]
        public WirePlug plugA;
        public WirePlug plugB;

        [Header("Appearance")]
        public Color wireColor = Color.red;
        public Color energizedColor = new Color(1f, 0.3f, 0.2f);
        public Color shortCircuitColor = Color.red;
        public int curveSegments = 20;
        public float sagAmount = 0.06f;
        public float defaultWidth = 0.009f;

        private LineRenderer lineRenderer;
        private Material wireMat;

        public bool isEnergized = false;
        public bool isShortCircuit = false;

        private void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();
            ConfigureLineRenderer();
        }

        private void Start()
        {
            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }
            ConfigureLineRenderer();
            UpdateWireAppearance(false, false);
        }

        public void ConfigureLineRenderer()
        {
            if (lineRenderer == null) return;
            lineRenderer.positionCount = curveSegments;
            lineRenderer.startWidth = defaultWidth;
            lineRenderer.endWidth = defaultWidth;
            lineRenderer.useWorldSpace = true;
        }

        private void LateUpdate()
        {
            if (plugA == null || plugB == null || lineRenderer == null) return;

            Vector3 start = plugA.transform.position;
            Vector3 end = plugB.transform.position;
            float dist = Vector3.Distance(start, end);

            // Dynamic sag amount based on distance between plugs
            float effectiveSag = Mathf.Clamp(dist * 0.12f, 0.02f, 0.15f);
            Vector3 mid = (start + end) * 0.5f + Vector3.down * effectiveSag;

            for (int i = 0; i < curveSegments; i++)
            {
                float t = i / (float)(curveSegments - 1);
                // Quadratic Bezier curve
                Vector3 pt = (1f - t) * (1f - t) * start + 2f * (1f - t) * t * mid + t * t * end;
                lineRenderer.SetPosition(i, pt);
            }
        }

        public void UpdateWireAppearance(bool energized, bool shortCircuit)
        {
            isEnergized = energized;
            isShortCircuit = shortCircuit;

            if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null) return;

            if (shortCircuit)
            {
                lineRenderer.startColor = shortCircuitColor;
                lineRenderer.endColor = shortCircuitColor;
                lineRenderer.startWidth = defaultWidth * 1.3f;
                lineRenderer.endWidth = defaultWidth * 1.3f;
            }
            else if (energized)
            {
                Color c = wireColor == Color.red ? energizedColor : wireColor;
                lineRenderer.startColor = c;
                lineRenderer.endColor = c;
                lineRenderer.startWidth = defaultWidth;
                lineRenderer.endWidth = defaultWidth;
            }
            else
            {
                lineRenderer.startColor = wireColor;
                lineRenderer.endColor = wireColor;
                lineRenderer.startWidth = defaultWidth;
                lineRenderer.endWidth = defaultWidth;
            }
        }
    }
}
