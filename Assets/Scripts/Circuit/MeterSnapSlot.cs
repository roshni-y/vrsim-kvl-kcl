using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRContent.Circuit
{
    /// <summary>
    /// Snap slot for placing Digital_Ammeter (series) or Digital_Voltmeter (parallel).
    /// Snaps the matching LabMeter cleanly into place, prevents falling, allows repositioning,
    /// and notifies SnapSlotHighlight + GuidedExperimentManager.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class MeterSnapSlot : MonoBehaviour, IXRSelectFilter
    {
        public MeterType expectedMeterType = MeterType.Ammeter;
        public SnapSlotHighlight slotHighlight;

        private XRSocketInteractor m_SocketInteractor;
        private LabMeter m_CurrentMeter;

        public LabMeter currentMeter => m_CurrentMeter;
        public bool hasMeter => m_CurrentMeter != null;

        public bool canProcess => true;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactable == null || interactable.transform == null) return false;

            var meter = interactable.transform.GetComponent<LabMeter>();
            if (meter == null) return false;

            if (m_CurrentMeter != null && m_CurrentMeter != meter)
                return false;

            // Reject mismatched meter type with red feedback
            if (meter.meterType != expectedMeterType)
            {
                if (slotHighlight != null) slotHighlight.FlashError();
                if (GuidedExperimentManager.Instance != null)
                {
                    GuidedExperimentManager.Instance.OnMeterPlaced(meter.meterType == MeterType.Ammeter, false);
                }
                return false;
            }

            return true;
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
            var meter = args.interactableObject.transform.GetComponent<LabMeter>();
            if (meter != null)
            {
                m_CurrentMeter = meter;
                bool correct = (meter.meterType == expectedMeterType);
                if (slotHighlight != null)
                {
                    if (correct) slotHighlight.SetFilled(true);
                    else slotHighlight.FlashError();
                }
                if (GuidedExperimentManager.Instance != null)
                {
                    GuidedExperimentManager.Instance.OnMeterPlaced(meter.meterType == MeterType.Ammeter, correct);
                }
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            m_CurrentMeter = null;
            if (slotHighlight != null)
                slotHighlight.SetFilled(false);
        }
    }
}
