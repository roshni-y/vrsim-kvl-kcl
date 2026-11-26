using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections.Generic;

namespace VRContent.Circuit
{
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class ElectricalComponentInteractable : MonoBehaviour
    {
        [Header("Component Identity")]
        public string componentName = "Lab Instrument";
        public bool isMovable = true;
        public bool lockRotationOnRelease = true;

        [Header("Attached Terminals")]
        public List<CircuitTerminal> componentTerminals = new List<CircuitTerminal>();

        private XRGrabInteractable m_GrabInteractable;
        private Rigidbody m_Rigidbody;
        private Vector3 m_InitialPosition;
        private Quaternion m_InitialRotation;

        public XRGrabInteractable grabInteractable => m_GrabInteractable;
        public Rigidbody componentRigidbody => m_Rigidbody;

        private void Awake()
        {
            m_GrabInteractable = GetComponent<XRGrabInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();

            m_InitialPosition = transform.position;
            m_InitialRotation = transform.rotation;

            ConfigurePhysics();
            CollectChildTerminals();

            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.selectEntered.AddListener(OnSelectEntered);
                m_GrabInteractable.selectExited.AddListener(OnSelectExited);
            }
        }

        private void OnDestroy()
        {
            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                m_GrabInteractable.selectExited.RemoveListener(OnSelectExited);
            }
        }

        public void ConfigurePhysics()
        {
            if (m_Rigidbody != null)
            {
                m_Rigidbody.mass = 0.5f;
                m_Rigidbody.linearDamping = 3f;
                m_Rigidbody.angularDamping = 5f;
                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                m_Rigidbody.isKinematic = true; // Kinematic when at rest to avoid sliding on table
            }

            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                m_GrabInteractable.throwOnDetach = false;
            }
        }

        public void CollectChildTerminals()
        {
            componentTerminals.Clear();
            componentTerminals.AddRange(GetComponentsInChildren<CircuitTerminal>(true));
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = false;
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                if (lockRotationOnRelease)
                {
                    // Snap upright to prevent instruments flipping on workbench
                    Vector3 euler = transform.eulerAngles;
                    transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
                }
            }

            // Update any connected wires immediately upon drop
            UpdateAttachedWires();
        }

        private void LateUpdate()
        {
            // If component is moving or being grabbed, ensure all connected plugs remain locked to terminals
            if (m_GrabInteractable != null && m_GrabInteractable.isSelected)
            {
                UpdateAttachedWires();
            }
        }

        public void UpdateAttachedWires()
        {
            for (int i = 0; i < componentTerminals.Count; i++)
            {
                var term = componentTerminals[i];
                if (term != null && term.connectedPlug != null)
                {
                    term.connectedPlug.transform.position = term.transform.position;
                    term.connectedPlug.transform.rotation = term.transform.rotation;
                }
            }
        }

        public void ResetToInitialTransform()
        {
            transform.position = m_InitialPosition;
            transform.rotation = m_InitialRotation;
            if (m_Rigidbody != null)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
                m_Rigidbody.isKinematic = true;
            }
            UpdateAttachedWires();
        }
    }
}
