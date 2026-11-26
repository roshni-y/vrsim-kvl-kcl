using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using TMPro;

namespace VRContent.Circuit
{
    /// <summary>
    /// Extends any grabbable object with component-type metadata and
    /// visual outline feedback (glow) when it's the currently-needed component.
    /// Attach to any resistor / meter GameObject that already has XRGrabInteractable.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class GrabbableLabComponent : MonoBehaviour
    {
        public enum LabComponentType { Resistor100, Resistor200, Resistor300, Resistor200Branch, Ammeter, Voltmeter, Wire }

        [Header("Identity")]
        public LabComponentType componentType = LabComponentType.Resistor100;
        public float resistance = 100f;         // Only relevant for resistors
        public string componentLabel = "100 Ω";

        [Header("Grab Hint")]
        public TextMeshPro grabHintText;        // Optional floating label showing grab key
        public bool showHintOnHighlight = true;

        [Header("Outline/Glow")]
        public Renderer[] outlineRenderers;     // Renderers to tint for highlight
        public Color normalTint   = Color.white;
        public Color highlightTint = new Color(0f, 1f, 0.9f, 1f);  // Cyan
        public Color grabbedTint   = new Color(1f, 0.9f, 0.1f, 1f); // Yellow

        private XRGrabInteractable _grab;
        private Rigidbody _rb;
        private bool _isHighlighted;
        private bool _pendingThrow;
        private bool _isThrownInFlight;
        private Vector3 _pendingLaunchVelocity;
        private float _flightStartTime;
        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColorPropID = Shader.PropertyToID("_Color");
        private static readonly int EmissionPropID = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            _initialPosition = transform.position;
            _initialRotation = transform.rotation;
            _grab = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
            _mpb = new MaterialPropertyBlock();

            // Collect renderers automatically if not assigned
            if (outlineRenderers == null || outlineRenderers.Length == 0)
                outlineRenderers = GetComponentsInChildren<Renderer>();

            if (_grab != null)
            {
                _grab.throwOnDetach = false;
                _grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                _grab.useDynamicAttach = true;
                _grab.selectEntered.AddListener(OnGrabbed);
                _grab.selectExited.AddListener(OnReleased);
            }

            if (_rb != null)
            {
                _rb.useGravity = false;
                _rb.isKinematic = true;
                _rb.linearDamping = 4f;
                _rb.angularDamping = 6f;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            SetVisualTint(normalTint, 0f);
        }

        public void ReturnToWorkbench()
        {
            _isThrownInFlight = false;
            _pendingThrow = false;
            StopAllCoroutines();
            if (_rb != null)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.useGravity = false;
                _rb.isKinematic = true;
            }
            transform.SetPositionAndRotation(_initialPosition, _initialRotation);
        }

        private void OnDestroy()
        {
            if (_grab != null)
            {
                _grab.selectEntered.RemoveListener(OnGrabbed);
                _grab.selectExited.RemoveListener(OnReleased);
            }
        }

        public void SetNeeded(bool needed)
        {
            _isHighlighted = needed;
            SetVisualTint(needed ? highlightTint : normalTint, needed ? 0.6f : 0f);

            if (grabHintText != null && showHintOnHighlight)
            {
                grabHintText.gameObject.SetActive(needed);
                grabHintText.text = needed ? "← GRAB" : "";
            }
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            _isThrownInFlight = false;
            _pendingThrow = false;
            SetVisualTint(grabbedTint, 0.8f);
            if (grabHintText != null) grabHintText.gameObject.SetActive(false);

            if (!(args.interactorObject is XRSocketInteractor))
            {
                if (_rb != null)
                {
                    _rb.linearDamping = 4f;
                    _rb.angularDamping = 6f;
                    _rb.isKinematic = false;
                }
                if (GuidedExperimentManager.Instance != null)
                    GuidedExperimentManager.Instance.OnComponentGrabbed(this);
            }
        }

        public void PrepareThrow(Vector3 launchVelocity)
        {
            _pendingThrow = true;
            _pendingLaunchVelocity = launchVelocity;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            SetVisualTint(_isHighlighted ? highlightTint : normalTint, _isHighlighted ? 0.6f : 0f);
            if (!(args.interactorObject is XRSocketInteractor))
            {
                if (_pendingThrow)
                {
                    _pendingThrow = false;
                    StartCoroutine(LaunchNextFrame(_pendingLaunchVelocity));
                    return;
                }

                // Never launch/throw on G release
                if (_rb != null)
                {
                    if (!_rb.isKinematic)
                    {
                        _rb.linearVelocity = Vector3.zero;
                        _rb.angularVelocity = Vector3.zero;
                    }

                    // Check if released inside a valid snap slot; if not, drop naturally onto workbench
                    bool nearValidSocket = false;
                    var hits = Physics.OverlapSphere(transform.position, 0.14f);
                    foreach (var h in hits)
                    {
                        var cs = h.GetComponent<CircuitSocket>();
                        if (cs != null && !cs.hasResistor && cs.socketInteractor != null && _grab != null)
                        {
                            if (cs.socketInteractor.CanSelect((IXRSelectInteractable)_grab))
                            {
                                nearValidSocket = true;
                                break;
                            }
                        }
                    }

                    if (!nearValidSocket)
                    {
                        StartCoroutine(EnableGravityNextFrame());
                    }
                    else
                    {
                        _rb.useGravity = false;
                        _rb.isKinematic = true;
                    }
                }
            }
        }

        private System.Collections.IEnumerator LaunchNextFrame(Vector3 launchVelocity)
        {
            yield return null;
            if (_rb != null && (_grab == null || !_grab.isSelected))
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
                _rb.linearDamping = 0.05f;
                _rb.angularDamping = 0.5f;
                _rb.linearVelocity = launchVelocity;
                _rb.angularVelocity = Vector3.zero;
                _isThrownInFlight = true;
                _flightStartTime = Time.time;
            }
        }

        private void FixedUpdate()
        {
            if (!_isThrownInFlight || _grab == null || _grab.isSelected) return;

            var hits = Physics.OverlapSphere(transform.position, 0.16f);
            var res = GetComponent<ResistorComponent>();
            foreach (var h in hits)
            {
                var cs = h.GetComponent<CircuitSocket>();
                if (cs == null || cs.hasResistor || cs.socketInteractor == null) continue;

                if (cs.socketInteractor.CanSelect((IXRSelectInteractable)_grab))
                {
                    var xrMgr = cs.socketInteractor.interactionManager ?? FindFirstObjectByType<XRInteractionManager>();
                    if (xrMgr != null)
                    {
                        _isThrownInFlight = false;
                        xrMgr.SelectEnter((IXRSelectInteractor)cs.socketInteractor, (IXRSelectInteractable)_grab);
                        return;
                    }
                }
                else if (res != null)
                {
                    cs.NotifyRejected(res);
                    SettleAfterMiss();
                    return;
                }
            }

            if (Time.time - _flightStartTime > 2.0f)
            {
                SettleAfterMiss();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_isThrownInFlight && Time.time - _flightStartTime > 0.08f)
            {
                SettleAfterMiss();
            }
        }

        private void SettleAfterMiss()
        {
            _isThrownInFlight = false;
            if (_rb != null)
            {
                _rb.linearDamping = 4f;
                _rb.angularDamping = 6f;
            }
        }

        private System.Collections.IEnumerator EnableGravityNextFrame()
        {
            yield return null;
            if (_rb != null && (_grab == null || !_grab.isSelected))
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        private void SetVisualTint(Color col, float emissionStrength)
        {
            if (outlineRenderers == null) return;
            foreach (var r in outlineRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorPropID, col);
                _mpb.SetColor(EmissionPropID, col * emissionStrength);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
