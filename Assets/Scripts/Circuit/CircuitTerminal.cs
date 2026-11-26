using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Filtering;

namespace VRContent.Circuit
{
    public enum TerminalType
    {
        Positive_Supply,    // DC Power Supply Red (+)
        Negative_Ground,    // DC Power Supply Black (-)
        Circuit_Input,      // Circuit Board Series Loop In
        Circuit_Ground,     // Circuit Board Ground Return
        Meter_Ammeter_In,   // Ammeter +10A
        Meter_Ammeter_Out,  // Ammeter -COM
        Meter_Voltmeter_In, // Voltmeter +V
        Meter_Voltmeter_Out,// Voltmeter -COM
        Junction_Inflow,    // Junction Node IN
        Junction_BranchA,   // Junction Node Branch A
        Junction_BranchB,   // Junction Node Branch B
        Junction_Return,    // Junction Node Return
        TestPoint_R1_Pos,   // R1 High Potential (+)
        TestPoint_R1_Neg,   // R1 Low Potential (-)
        TestPoint_R2_Pos,   // R2 High Potential (+)
        TestPoint_R2_Neg    // R2 Low Potential (-)
    }

    public enum ConnectionState
    {
        Disconnected,
        Valid,
        Invalid
    }

    [RequireComponent(typeof(XRSocketInteractor))]
    public class CircuitTerminal : MonoBehaviour, IXRSelectFilter
    {
        [Header("Terminal Configuration")]
        public string terminalId;
        public TerminalType terminalType;
        public string terminalLabel = "TERM";
        public bool enforcePolarity = true;

        [Header("Connection State")]
        public WirePlug connectedPlug = null;
        public ConnectionState connectionState = ConnectionState.Disconnected;

        [Header("Visual Feedback Indicators")]
        public Renderer feedbackIndicator;
        public Material disconnectedMat;
        public Material validGreenMat;
        public Material invalidRedMat;

        private XRSocketInteractor m_SocketInteractor;

        public XRSocketInteractor socketInteractor
        {
            get
            {
                if (m_SocketInteractor == null) m_SocketInteractor = GetComponent<XRSocketInteractor>();
                return m_SocketInteractor;
            }
        }
        public bool isConnected => connectedPlug != null;

        // IXRSelectFilter implementation to prevent incompatible connections
        public bool canProcess => true;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactable == null || interactable.transform == null) return false;

            // 1. Only WirePlug interactables can connect to terminals
            var plug = interactable.transform.GetComponent<WirePlug>();
            if (plug == null) return false;

            // 2. Reject if terminal is already occupied by a different plug
            if (connectedPlug != null && connectedPlug != plug)
            {
                return false;
            }

            // 3. Enforce polarity compatibility (Requirement 8)
            if (enforcePolarity)
            {
                bool isPositiveTerminal = IsPositivePolarityTerminal(terminalType);
                bool isNegativeTerminal = IsNegativePolarityTerminal(terminalType);

                if (isPositiveTerminal && plug.wirePolarity == WirePolarity.Black_Ground)
                {
                    // Incompatible: Black ground wire cannot plug into positive terminal
                    return false;
                }

                if (isNegativeTerminal && plug.wirePolarity == WirePolarity.Red_Positive)
                {
                    // Incompatible: Red positive wire cannot plug into negative / COM terminal
                    return false;
                }
            }

            // 4. Prevent self-looping on the exact same terminal
            var oppositePlug = plug.GetOppositePlug();
            if (oppositePlug != null && oppositePlug.currentTerminal == this)
            {
                return false;
            }

            return true;
        }

        private static bool IsPositivePolarityTerminal(TerminalType type)
        {
            return type == TerminalType.Positive_Supply ||
                   type == TerminalType.Circuit_Input ||
                   type == TerminalType.Meter_Ammeter_In ||
                   type == TerminalType.Meter_Voltmeter_In ||
                   type == TerminalType.Junction_Inflow ||
                   type == TerminalType.Junction_BranchA ||
                   type == TerminalType.Junction_BranchB ||
                   type == TerminalType.TestPoint_R1_Pos ||
                   type == TerminalType.TestPoint_R2_Pos;
        }

        private static bool IsNegativePolarityTerminal(TerminalType type)
        {
            return type == TerminalType.Negative_Ground ||
                   type == TerminalType.Circuit_Ground ||
                   type == TerminalType.Meter_Ammeter_Out ||
                   type == TerminalType.Meter_Voltmeter_Out ||
                   type == TerminalType.Junction_Return ||
                   type == TerminalType.TestPoint_R1_Neg ||
                   type == TerminalType.TestPoint_R2_Neg;
        }

        private void Awake()
        {
            m_SocketInteractor = GetComponent<XRSocketInteractor>();
            if (m_SocketInteractor != null)
            {
                m_SocketInteractor.selectFilters.Add(this);
                m_SocketInteractor.selectEntered.AddListener(OnSocketSelectEntered);
                m_SocketInteractor.selectExited.AddListener(OnSocketSelectExited);
            }
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(terminalId))
            {
                terminalId = gameObject.name;
            }
            UpdateVisuals();
        }

        private void OnDestroy()
        {
            if (m_SocketInteractor != null)
            {
                m_SocketInteractor.selectFilters.Remove(this);
                m_SocketInteractor.selectEntered.RemoveListener(OnSocketSelectEntered);
                m_SocketInteractor.selectExited.RemoveListener(OnSocketSelectExited);
            }
        }

        private void OnSocketSelectEntered(SelectEnterEventArgs args)
        {
            var plug = args.interactableObject.transform.GetComponent<WirePlug>();
            if (plug != null)
            {
                ConnectPlug(plug);
            }
        }

        private void OnSocketSelectExited(SelectExitEventArgs args)
        {
            var plug = args.interactableObject.transform.GetComponent<WirePlug>();
            if (plug != null && connectedPlug == plug)
            {
                DisconnectPlug();
            }
        }

        public void ConnectPlug(WirePlug plug)
        {
            if (plug == null) return;

            connectedPlug = plug;
            plug.currentTerminal = this;

            // Snap plug transform to terminal
            plug.transform.position = transform.position;
            plug.transform.rotation = transform.rotation;

            // Link to ElectricalTerminal and WireConnection
            var elecTerm = GetComponent<ElectricalTerminal>();
            var wireConn = plug.GetComponentInParent<WireConnection>();
            if (wireConn != null && elecTerm != null)
            {
                wireConn.SetTerminal(plug, elecTerm);
            }

            // Notify CircuitConnectionManager of confirmed electrical connection
            if (CircuitConnectionManager.Instance != null)
            {
                CircuitConnectionManager.Instance.OnTerminalConnectionChanged();
            }
            else
            {
                SetConnectionState(ConnectionState.Valid);
            }

            if (CircuitManager.Instance != null)
            {
                CircuitManager.Instance.EvaluateCircuit();
            }
        }

        public void DisconnectPlug()
        {
            if (connectedPlug != null)
            {
                var plug = connectedPlug;
                var wireConn = plug.GetComponentInParent<WireConnection>();
                if (wireConn != null)
                {
                    wireConn.ClearTerminal(plug);
                }

                connectedPlug = null;
                plug.currentTerminal = null;
                plug.SetFeedback(ConnectionState.Disconnected);
            }

            // Ensure any plug referencing this terminal is completely cleared
            var allPlugs = Object.FindObjectsByType<WirePlug>(FindObjectsSortMode.None);
            foreach (var p in allPlugs)
            {
                if (p.currentTerminal == this)
                {
                    p.currentTerminal = null;
                    p.SetFeedback(ConnectionState.Disconnected);
                }
            }

            SetConnectionState(ConnectionState.Disconnected);

            // Notify CircuitConnectionManager of disconnection
            if (CircuitConnectionManager.Instance != null)
            {
                CircuitConnectionManager.Instance.OnTerminalConnectionChanged();
            }

            if (CircuitManager.Instance != null)
            {
                CircuitManager.Instance.EvaluateCircuit();
            }
        }

        public void SetConnectionState(ConnectionState state)
        {
            connectionState = state;
            UpdateVisuals();
            if (connectedPlug != null)
            {
                connectedPlug.SetFeedback(state);
            }
        }

        public void UpdateVisuals()
        {
            if (feedbackIndicator == null) return;

            switch (connectionState)
            {
                case ConnectionState.Valid:
                    if (validGreenMat != null) feedbackIndicator.material = validGreenMat;
                    break;
                case ConnectionState.Invalid:
                    if (invalidRedMat != null) feedbackIndicator.material = invalidRedMat;
                    break;
                default:
                    if (disconnectedMat != null) feedbackIndicator.material = disconnectedMat;
                    break;
            }
        }

        private void LateUpdate()
        {
            // Keep connected wire endpoints firmly attached when parent component moves (Requirement 9)
            if (connectedPlug != null)
            {
                connectedPlug.transform.position = transform.position;
                connectedPlug.transform.rotation = transform.rotation;
            }
        }
    }
}
