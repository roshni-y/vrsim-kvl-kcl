using UnityEngine;

namespace VRContent.Circuit
{
    public class KCLFlowArrows : MonoBehaviour
    {
        public KirchhoffSimulator simulator;

        public Renderer arrowInRend;
        public Renderer arrowBranchARend;
        public Renderer arrowBranchBRend;
        public Renderer arrowReturnRend;

        public TextMesh textIn;
        public TextMesh textBranchA;
        public TextMesh textBranchB;
        public TextMesh textReturn;

        public Material activeArrowMat;
        public Material inactiveArrowMat;

        private void Start()
        {
            if (simulator == null)
            {
                simulator = Object.FindFirstObjectByType<KirchhoffSimulator>();
            }
            if (simulator != null)
            {
                simulator.OnCircuitUpdated += UpdateVisuals;
            }
            UpdateVisuals();
        }

        private void OnDestroy()
        {
            if (simulator != null)
            {
                simulator.OnCircuitUpdated -= UpdateVisuals;
            }
        }

        public void UpdateVisuals()
        {
            if (simulator == null) return;

            bool isKCL = (simulator.currentMode == CircuitMode.KCL_Parallel);
            bool isActive = isKCL && simulator.isKCLActive;

            var mat = isActive ? activeArrowMat : inactiveArrowMat;

            if (arrowInRend != null && mat != null) arrowInRend.material = mat;
            if (arrowBranchARend != null && mat != null) arrowBranchARend.material = mat;
            if (arrowBranchBRend != null && mat != null) arrowBranchBRend.material = mat;
            if (arrowReturnRend != null && mat != null) arrowReturnRend.material = mat;

            if (isActive)
            {
                if (textIn != null) textIn.text = string.Format("Iin ➔ ({0:F2} A)", simulator.kclTotalCurrentIn);
                if (textBranchA != null) textBranchA.text = string.Format("➔ I1 ({0:F2} A)", simulator.kclCurrentA);
                if (textBranchB != null) textBranchB.text = string.Format("➔ I2 ({0:F2} A)", simulator.kclCurrentB);
                if (textReturn != null) textReturn.text = string.Format("➔ I_out ({0:F2} A)", simulator.kclTotalCurrentIn);
            }
            else
            {
                if (textIn != null) textIn.text = isKCL ? "Iin ➔ (-- A)" : "";
                if (textBranchA != null) textBranchA.text = isKCL ? "➔ I1 (-- A)" : "";
                if (textBranchB != null) textBranchB.text = isKCL ? "➔ I2 (-- A)" : "";
                if (textReturn != null) textReturn.text = isKCL ? "➔ I_out (-- A)" : "";
            }
        }
    }
}
