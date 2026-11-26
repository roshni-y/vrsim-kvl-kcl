using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRContent.Circuit
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    [RequireComponent(typeof(Collider))]
    public class CircuitInteractiveSwitch : MonoBehaviour
    {
        [Header("Events")]
        public UnityEvent onActivated;

        [Header("Visual Feedback")]
        public Transform handleTransform;
        public Vector3 stateA_EulerRotation = new Vector3(-20f, 0f, 0f);
        public Vector3 stateB_EulerRotation = new Vector3(20f, 0f, 0f);
        public bool isStateB = false;

        private XRSimpleInteractable m_Interactable;

        private void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.AddListener(OnSelectEntered);
            }
            if (handleTransform == null)
            {
                handleTransform = transform;
            }
        }

        private void OnDestroy()
        {
            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            Toggle();
        }

        private void OnMouseDown()
        {
            Toggle();
        }

        public void Toggle()
        {
            isStateB = !isStateB;
            UpdateVisualRotation();
            onActivated?.Invoke();
        }

        public void SetState(bool stateB)
        {
            isStateB = stateB;
            UpdateVisualRotation();
        }

        private void UpdateVisualRotation()
        {
            if (handleTransform != null)
            {
                handleTransform.localRotation = Quaternion.Euler(isStateB ? stateB_EulerRotation : stateA_EulerRotation);
            }
        }
    }
}
