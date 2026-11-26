using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Filtering;

namespace VRContent.Circuit
{
    [RequireComponent(typeof(XRSocketInteractor))]
    public class CircuitSocket : MonoBehaviour, IXRSelectFilter
    {
        [Tooltip("Unique identifier or label for this socket position")]
        public string socketID = "R1";
        public string socketLabel = "Resistor Slot 1";
        public SnapSlotHighlight slotHighlight;
        public bool lockOnCorrectPlacement = true;

        private XRSocketInteractor m_SocketInteractor;
        private ResistorComponent m_CurrentResistor;
        private SocketLockFilter m_LockFilter = new SocketLockFilter();
        private float _lastRejectFeedbackTime = -10f;

        private class SocketLockFilter : IXRSelectFilter
        {
            public bool canProcess => true;
            public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
            {
                // Only allow XRSocketInteractor to hold it; block hand/ray re-grabbing while locked
                return interactor is XRSocketInteractor;
            }
        }

        public ResistorComponent currentResistor => m_CurrentResistor;
        public bool hasResistor => m_CurrentResistor != null;
        public XRSocketInteractor socketInteractor
        {
            get
            {
                if (m_SocketInteractor == null) m_SocketInteractor = GetComponent<XRSocketInteractor>();
                return m_SocketInteractor;
            }
        }

        // IXRSelectFilter implementation to prevent incompatible items (like wires or meters) from snapping
        public bool canProcess => true;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactable == null || interactable.transform == null) return false;

            // 1. Only ResistorComponent interactables can be seated into circuit resistor sockets
            var resistor = interactable.transform.GetComponent<ResistorComponent>();
            if (resistor == null) return false;

            // 2. Reject if slot is already occupied by another resistor
            if (m_CurrentResistor != null && m_CurrentResistor != resistor)
            {
                return false;
            }

            // 3. Enforce active experiment mode (KVL sockets only in KVL, KCL sockets only in KCL)
            var sim = Object.FindFirstObjectByType<KirchhoffSimulator>();
            if (sim != null)
            {
                string idUpper = socketID != null ? socketID.ToUpperInvariant() : "";
                bool isKCLSocket = idUpper.Contains("RA") || idUpper.Contains("RB") || idUpper.Contains("KCL");
                bool isKVLSocket = !isKCLSocket && (idUpper.Contains("R1") || idUpper.Contains("R2") || idUpper.Contains("R3") || idUpper.Contains("KVL"));

                if (isKVLSocket && sim.currentMode != CircuitMode.KVL_Series)
                {
                    NotifyRejected(resistor, "Switch to KVL mode to use this series slot.");
                    return false;
                }
                if (isKCLSocket && sim.currentMode != CircuitMode.KCL_Parallel)
                {
                    NotifyRejected(resistor, "Switch to KCL mode to use this parallel slot.");
                    return false;
                }
            }

            // 4. Strict component-type / resistance validation: never snap a resistor into the wrong slot
            if (!IsExpectedResistanceForSocket(resistor.resistance))
            {
                NotifyRejected(resistor, null);
                return false;
            }

            return true;
        }

        public void NotifyRejected(ResistorComponent resistor, string customReason = null)
        {
            if (Time.unscaledTime - _lastRejectFeedbackTime < 0.75f) return;
            _lastRejectFeedbackTime = Time.unscaledTime;

            if (slotHighlight != null) slotHighlight.FlashError();
            if (GuidedExperimentManager.Instance != null && resistor != null)
            {
                GuidedExperimentManager.Instance.OnWrongResistorRejected(socketID, resistor.resistance, customReason);
            }
        }

        private void Awake()
        {
            m_SocketInteractor = GetComponent<XRSocketInteractor>();
            if (slotHighlight == null)
                slotHighlight = GetComponent<SnapSlotHighlight>();
            if (m_SocketInteractor != null)
            {
                m_SocketInteractor.selectFilters.Add(this);
                m_SocketInteractor.selectEntered.AddListener(OnSelectEntered);
                m_SocketInteractor.selectExited.AddListener(OnSelectExited);
            }
        }

        private void OnDestroy()
        {
            if (m_SocketInteractor != null)
            {
                m_SocketInteractor.selectFilters.Remove(this);
                m_SocketInteractor.selectEntered.RemoveListener(OnSelectEntered);
                m_SocketInteractor.selectExited.RemoveListener(OnSelectExited);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            var resistor = args.interactableObject.transform.GetComponent<ResistorComponent>();
            if (resistor != null)
            {
                m_CurrentResistor = resistor;
                var rb = resistor.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    rb.useGravity = false;
                    rb.isKinematic = true;
                }

                bool matchesSlot = IsExpectedResistanceForSocket(resistor.resistance);
                if (m_SocketInteractor != null && m_SocketInteractor.attachTransform != null)
                {
                    resistor.transform.SetPositionAndRotation(
                        m_SocketInteractor.attachTransform.position,
                        m_SocketInteractor.attachTransform.rotation);
                }
                if (matchesSlot && lockOnCorrectPlacement)
                {
                    var xrg = resistor.GetComponent<XRGrabInteractable>();
                    if (xrg != null)
                    {
                        xrg.selectFilters.Remove(m_LockFilter);
                        xrg.selectFilters.Add(m_LockFilter);
                    }
                }

                if (slotHighlight != null)
                {
                    if (matchesSlot) slotHighlight.SetFilled(true);
                    else slotHighlight.FlashError();
                }
                if (GuidedExperimentManager.Instance != null)
                {
                    GuidedExperimentManager.Instance.OnResistorPlaced(socketID, resistor.resistance);
                }
                NotifySimulator();
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (m_CurrentResistor != null)
            {
                var xrg = m_CurrentResistor.GetComponent<XRGrabInteractable>();
                if (xrg != null) xrg.selectFilters.Remove(m_LockFilter);
            }
            m_CurrentResistor = null;
            if (slotHighlight != null)
                slotHighlight.SetFilled(false);
            NotifySimulator();
        }

        public bool IsExpectedResistanceForSocket(float res)
        {
            string id = socketID != null ? socketID.ToUpperInvariant() : "";
            if (id.Contains("R1")) return Mathf.Approximately(res, 100f);
            if (id.Contains("R2")) return Mathf.Approximately(res, 200f);
            if (id.Contains("RA")) return Mathf.Approximately(res, 200f);
            if (id.Contains("RB")) return Mathf.Approximately(res, 300f);
            return true;
        }

        public void SetResistor(ResistorComponent resistor)
        {
            m_CurrentResistor = resistor;
            NotifySimulator();
        }

        public void ClearResistor(bool returnToWorkbench = true)
        {
            var prevResistor = m_CurrentResistor;
            if (prevResistor != null)
            {
                var xrg = prevResistor.GetComponent<XRGrabInteractable>();
                if (xrg != null) xrg.selectFilters.Remove(m_LockFilter);
            }

            if (m_SocketInteractor != null && m_SocketInteractor.hasSelection && m_SocketInteractor.interactionManager != null)
            {
                var selected = new System.Collections.Generic.List<IXRSelectInteractable>(m_SocketInteractor.interactablesSelected);
                foreach (var sel in selected)
                {
                    if (sel != null)
                        m_SocketInteractor.interactionManager.SelectExit((IXRSelectInteractor)m_SocketInteractor, sel);
                }
            }

            if (returnToWorkbench && prevResistor != null)
            {
                var glc = prevResistor.GetComponent<GrabbableLabComponent>();
                if (glc != null) glc.ReturnToWorkbench();
            }

            m_CurrentResistor = null;
            if (slotHighlight != null) slotHighlight.SetFilled(false);
            NotifySimulator();
        }

        private void NotifySimulator()
        {
            var sim = Object.FindFirstObjectByType<KirchhoffSimulator>();
            if (sim != null)
            {
                sim.RecalculateCircuit();
            }
            if (CircuitManager.Instance != null)
            {
                CircuitManager.Instance.EvaluateCircuit();
            }
        }
    }
}
