using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRContent.Circuit
{
    /// <summary>
    /// Enables physics-based throw-to-slot for held resistors in VR_Content_Sim:
    /// - Hold G to grab with the right controller.
    /// - Shows a curved parabolic trajectory preview and landing-point indicator while held.
    /// - Press W while holding a resistor to launch it toward the aimed target (context-sensitive:
    ///   W retains normal movement when not holding an object).
    /// - Releasing G drops naturally without launching.
    /// </summary>
    public class ProjectileThrowController : MonoBehaviour
    {
        [Header("Trajectory Preview")]
        public int arcPoints = 24;
        public float maxAimDistance = 4.5f;
        public float slotAssistRadius = 0.18f;
        public Color validSlotColor = new Color(0.1f, 1f, 0.35f, 0.95f);
        public Color wrongSlotColor = new Color(1f, 0.25f, 0.2f, 0.95f);
        public Color defaultArcColor = new Color(0f, 0.9f, 1f, 0.85f);

        private XRInteractionManager _xrManager;
        private XRDeviceSimulator _simulator;
        private XRRayInteractor _rightRay;
        private XRDirectInteractor _rightDirect;

        private LineRenderer _arcLine;
        private GameObject _landingIndicator;
        private Renderer _landingRenderer;
        private MaterialPropertyBlock _mpb;

        private bool _suppressedMoveAction;
        private GrabbableLabComponent _heldResistor;
        private IXRSelectInteractor _holdingInteractor;
        private Vector3 _computedLaunchVelocity;
        private CircuitSocket _aimedSocket;
        private bool _aimedAtWrongSlot;

        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private static readonly int EmissionPropId = Shader.PropertyToID("_EmissionColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoAttach()
        {
            if (FindFirstObjectByType<ProjectileThrowController>() != null) return;
            var mgr = FindFirstObjectByType<GuidedExperimentManager>();
            if (mgr != null)
            {
                mgr.gameObject.AddComponent<ProjectileThrowController>();
            }
        }

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            CreateVisuals();
        }

        private void Start()
        {
            FindInteractors();
        }

        private void OnDisable()
        {
            RestoreMovementAction();
            SetPreviewVisible(false);
        }

        private void FindInteractors()
        {
            if (_xrManager == null)
                _xrManager = FindFirstObjectByType<XRInteractionManager>();
            if (_simulator == null)
                _simulator = FindFirstObjectByType<XRDeviceSimulator>();

            foreach (var ray in FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None))
            {
                if (ray.name.Contains("Teleport")) continue;
                if (ray.transform.parent != null && ray.transform.parent.name.Contains("Right"))
                {
                    _rightRay = ray;
                    break;
                }
            }

            foreach (var dir in FindObjectsByType<XRDirectInteractor>(FindObjectsSortMode.None))
            {
                if (dir.transform.parent != null && dir.transform.parent.name.Contains("Right"))
                {
                    _rightDirect = dir;
                    break;
                }
            }
        }

        private void CreateVisuals()
        {
            var lineGo = new GameObject("ThrowTrajectoryLine");
            lineGo.transform.SetParent(transform, false);
            _arcLine = lineGo.AddComponent<LineRenderer>();
            _arcLine.useWorldSpace = true;
            _arcLine.positionCount = arcPoints;
            _arcLine.startWidth = 0.012f;
            _arcLine.endWidth = 0.008f;
            _arcLine.numCapVertices = 4;
            _arcLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _arcLine.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) _arcLine.material = new Material(shader);

            _landingIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _landingIndicator.name = "ThrowLandingIndicator";
            _landingIndicator.transform.SetParent(transform, false);
            _landingIndicator.transform.localScale = new Vector3(0.14f, 0.004f, 0.14f);
            var col = _landingIndicator.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _landingRenderer = _landingIndicator.GetComponent<Renderer>();
            if (_landingRenderer != null && shader != null)
            {
                _landingRenderer.material = new Material(shader);
            }

            SetPreviewVisible(false);
        }

        private void Update()
        {
            if (_rightRay == null && _rightDirect == null)
                FindInteractors();

            DetectHeldResistor();
            UpdateContextSensitiveMove();

            if (_heldResistor == null)
            {
                SetPreviewVisible(false);
                return;
            }

            UpdateTrajectoryPreview();

            if (Keyboard.current != null && Keyboard.current.wKey.wasPressedThisFrame)
            {
                LaunchHeldResistor();
            }
        }

        private void DetectHeldResistor()
        {
            _heldResistor = null;
            _holdingInteractor = null;

            CheckInteractor(_rightDirect);
            if (_heldResistor == null)
                CheckInteractor(_rightRay);
        }

        private void CheckInteractor(IXRSelectInteractor interactor)
        {
            if (interactor == null || !interactor.hasSelection) return;
            foreach (var selected in interactor.interactablesSelected)
            {
                if (selected == null || selected.transform == null) continue;
                var labComp = selected.transform.GetComponent<GrabbableLabComponent>();
                var res = selected.transform.GetComponent<ResistorComponent>();
                if (labComp != null && res != null)
                {
                    _heldResistor = labComp;
                    _holdingInteractor = interactor;
                    return;
                }
            }
        }

        private void UpdateContextSensitiveMove()
        {
            if (_simulator == null || _simulator.keyboardZTranslateAction.action == null) return;

            bool holding = _heldResistor != null;
            var zAction = _simulator.keyboardZTranslateAction.action;

            if (holding && zAction.enabled)
            {
                zAction.Disable();
                _suppressedMoveAction = true;
            }
            else if (!holding && _suppressedMoveAction)
            {
                RestoreMovementAction();
            }
        }

        private void RestoreMovementAction()
        {
            if (_suppressedMoveAction && _simulator != null && _simulator.keyboardZTranslateAction.action != null)
            {
                _simulator.keyboardZTranslateAction.action.Enable();
            }
            _suppressedMoveAction = false;
        }

        private void UpdateTrajectoryPreview()
        {
            Transform aimTransform = _rightRay != null ? _rightRay.transform : (_rightDirect != null ? _rightDirect.transform : _heldResistor.transform);
            Vector3 startPos = _heldResistor.transform.position;
            Vector3 aimOrigin = aimTransform.position;
            Vector3 aimDir = aimTransform.forward;

            Vector3 targetPoint = FindAimTarget(aimOrigin, aimDir, startPos, out _aimedSocket);

            float dist = Vector3.Distance(
                new Vector3(startPos.x, 0f, startPos.z),
                new Vector3(targetPoint.x, 0f, targetPoint.z));
            float flightTime = Mathf.Clamp(dist * 0.45f, 0.28f, 0.65f);

            _computedLaunchVelocity = (targetPoint - startPos - 0.5f * Physics.gravity * flightTime * flightTime) / flightTime;

            SetPreviewVisible(true);
            _arcLine.positionCount = arcPoints;

            Vector3 prev = startPos;
            Vector3 finalLanding = targetPoint;
            for (int i = 0; i < arcPoints; i++)
            {
                float t = (i / (float)(arcPoints - 1)) * flightTime;
                Vector3 pt = startPos + _computedLaunchVelocity * t + 0.5f * Physics.gravity * t * t;

                if (i > 0 && _aimedSocket == null)
                {
                    Vector3 seg = pt - prev;
                    float segLen = seg.magnitude;
                    if (segLen > 0.001f && Physics.Raycast(prev, seg / segLen, out RaycastHit hit, segLen, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.collider.gameObject != _heldResistor.gameObject)
                        {
                            finalLanding = hit.point + Vector3.up * 0.005f;
                            for (int j = i; j < arcPoints; j++)
                                _arcLine.SetPosition(j, finalLanding);
                            break;
                        }
                    }
                }

                _arcLine.SetPosition(i, pt);
                prev = pt;
                finalLanding = pt;
            }

            if (_aimedSocket != null)
            {
                finalLanding = _aimedSocket.transform.position + Vector3.up * 0.01f;
            }

            _landingIndicator.transform.position = finalLanding;

            Color c = _aimedSocket != null
                ? validSlotColor
                : (_aimedAtWrongSlot ? wrongSlotColor : defaultArcColor);
            _arcLine.startColor = c;
            _arcLine.endColor = c;
            if (_landingRenderer != null)
            {
                _landingRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorPropId, c);
                _mpb.SetColor(EmissionPropId, c * 1.4f);
                _landingRenderer.SetPropertyBlock(_mpb);
            }
        }

        private Vector3 FindAimTarget(Vector3 aimOrigin, Vector3 aimDir, Vector3 startPos, out CircuitSocket bestSocket)
        {
            bestSocket = null;
            _aimedAtWrongSlot = false;
            float bestDistToRay = slotAssistRadius;
            var heldGrab = _heldResistor != null ? _heldResistor.GetComponent<XRGrabInteractable>() : null;

            foreach (var cs in FindObjectsByType<CircuitSocket>(FindObjectsSortMode.None))
            {
                if (cs.hasResistor) continue;
                Vector3 toSlot = cs.transform.position - aimOrigin;
                float proj = Vector3.Dot(toSlot, aimDir);
                if (proj <= 0.1f || proj > maxAimDistance) continue;

                Vector3 closestOnRay = aimOrigin + aimDir * proj;
                float perpDist = Vector3.Distance(closestOnRay, cs.transform.position);
                if (perpDist < bestDistToRay)
                {
                    bool canAccept = cs.socketInteractor != null && heldGrab != null &&
                                     cs.socketInteractor.CanSelect((IXRSelectInteractable)heldGrab);
                    if (canAccept)
                    {
                        bestDistToRay = perpDist;
                        bestSocket = cs;
                        _aimedAtWrongSlot = false;
                    }
                    else if (bestSocket == null)
                    {
                        _aimedAtWrongSlot = true;
                    }
                }
            }

            if (bestSocket != null)
            {
                return bestSocket.socketInteractor != null && bestSocket.socketInteractor.attachTransform != null
                    ? bestSocket.socketInteractor.attachTransform.position
                    : bestSocket.transform.position;
            }

            var hits = Physics.RaycastAll(aimOrigin, aimDir, maxAimDistance, ~0, QueryTriggerInteraction.Ignore);
            float closestHitDist = float.MaxValue;
            Vector3 hitPoint = startPos + aimDir * 1.2f;
            hitPoint.y = 0.86f;

            foreach (var h in hits)
            {
                if (h.collider.gameObject == _heldResistor.gameObject) continue;
                if (h.distance < closestHitDist)
                {
                    closestHitDist = h.distance;
                    hitPoint = h.point + Vector3.up * 0.005f;
                }
            }

            return hitPoint;
        }

        public void LaunchHeldResistor()
        {
            if (_heldResistor == null || _holdingInteractor == null || _xrManager == null) return;

            var grab = _heldResistor.GetComponent<XRGrabInteractable>();
            if (grab == null) return;

            var resistorToThrow = _heldResistor;
            Vector3 launchVel = _computedLaunchVelocity;

            resistorToThrow.PrepareThrow(launchVel);
            _xrManager.SelectExit(_holdingInteractor, (IXRSelectInteractable)grab);

            RestoreMovementAction();
            SetPreviewVisible(false);
        }

        private void SetPreviewVisible(bool visible)
        {
            if (_arcLine != null && _arcLine.enabled != visible)
                _arcLine.enabled = visible;
            if (_landingIndicator != null && _landingIndicator.activeSelf != visible)
                _landingIndicator.SetActive(visible);
        }
    }
}
