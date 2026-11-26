using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRContent.Circuit
{
    public enum WirePolarity
    {
        Red_Positive,
        Black_Ground,
        Signal
    }

    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class WirePlug : MonoBehaviour
    {
        [Header("Plug Properties")]
        public WirePolarity wirePolarity = WirePolarity.Red_Positive;
        public FlexibleWire parentWire;
        public CircuitTerminal currentTerminal;

        [Header("Feedback Visuals")]
        public Renderer plugHeadRenderer;
        public Material defaultMat;
        public Material validGreenMat;
        public Material invalidRedMat;

        private XRGrabInteractable m_GrabInteractable;
        private Rigidbody m_Rigidbody;

        public XRGrabInteractable grabInteractable
        {
            get
            {
                if (m_GrabInteractable == null) m_GrabInteractable = GetComponent<XRGrabInteractable>();
                return m_GrabInteractable;
            }
        }
        public bool isConnected => currentTerminal != null;

        private void Awake()
        {
            m_GrabInteractable = GetComponent<XRGrabInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.mass = 0.05f;
                m_Rigidbody.linearDamping = 2f;
                m_Rigidbody.angularDamping = 3f;
                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                m_GrabInteractable.throwOnDetach = false;
                m_GrabInteractable.selectEntered.AddListener(OnGrabbed);
                m_GrabInteractable.selectExited.AddListener(OnReleased);
            }

            if (plugHeadRenderer != null && defaultMat == null)
            {
                defaultMat = plugHeadRenderer.sharedMaterial;
            }
        }

        private void OnDestroy()
        {
            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.selectEntered.RemoveListener(OnGrabbed);
                m_GrabInteractable.selectExited.RemoveListener(OnReleased);
            }
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            // If grabbed by an XR controller or interactor (not an XRSocketInteractor), disconnect from terminal
            if (!(args.interactorObject is XRSocketInteractor))
            {
                if (currentTerminal != null)
                {
                    currentTerminal.DisconnectPlug();
                }

                if (m_Rigidbody != null)
                {
                    m_Rigidbody.isKinematic = false;
                }
            }
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            // If not placed in a socket, make kinematic to avoid rolling away on workbench
            if (!(args.interactorObject is XRSocketInteractor) && currentTerminal == null)
            {
                if (m_Rigidbody != null)
                {
                    m_Rigidbody.isKinematic = true;
                }
            }
        }

        private void LateUpdate()
        {
            // Keep connected wire endpoint locked to terminal position and rotation when components move
            if (currentTerminal != null)
            {
                transform.position = currentTerminal.transform.position;
                transform.rotation = currentTerminal.transform.rotation;
                if (m_Rigidbody != null && !m_Rigidbody.isKinematic)
                {
                    m_Rigidbody.isKinematic = true;
                }
            }
        }

        public WirePlug GetOppositePlug()
        {
            if (parentWire == null) return null;
            return (parentWire.plugA == this) ? parentWire.plugB : parentWire.plugA;
        }

        public void SetFeedback(ConnectionState state)
        {
            if (plugHeadRenderer == null) return;

            switch (state)
            {
                case ConnectionState.Valid:
                    if (validGreenMat != null) plugHeadRenderer.material = validGreenMat;
                    break;
                case ConnectionState.Invalid:
                    if (invalidRedMat != null) plugHeadRenderer.material = invalidRedMat;
                    break;
                default:
                    if (defaultMat != null) plugHeadRenderer.material = defaultMat;
                    break;
            }
        }
    }
}
