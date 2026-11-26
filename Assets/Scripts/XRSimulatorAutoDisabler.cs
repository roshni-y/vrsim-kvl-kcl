using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation
{
    /// <summary>
    /// Automatically detects if a physical VR headset is connected and active.
    /// If an active VR headset is detected, it disables the XR Device Simulator so that
    /// the physical headset and controllers have direct, conflict-free tracking.
    /// If no physical headset is present, the simulator remains active for PC Play Mode testing.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class XRSimulatorAutoDisabler : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("If true, automatically disables this GameObject when an active physical VR display is detected.")]
        private bool m_AutoDisableOnHeadsetPresent = true;

        public bool autoDisableOnHeadsetPresent
        {
            get => m_AutoDisableOnHeadsetPresent;
            set => m_AutoDisableOnHeadsetPresent = value;
        }

        private void Awake()
        {
            // Ensure this object is a root GameObject so DontDestroyOnLoad succeeds without warning
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            if (!m_AutoDisableOnHeadsetPresent)
                return;

            if (IsPhysicalHeadsetPresent())
            {
                Debug.Log("[XRSimulatorAutoDisabler] Physical VR headset detected and active. Disabling XR Device Simulator to avoid conflict.");
                gameObject.SetActive(false);
            }
            else
            {
                Debug.Log("[XRSimulatorAutoDisabler] No physical VR headset detected. XR Device Simulator is active for PC Play Mode.");
            }
        }

        /// <summary>
        /// Checks whether any physical XR display subsystem is actively running.
        /// </summary>
        public static bool IsPhysicalHeadsetPresent()
        {
            if (XRSettings.isDeviceActive && !string.IsNullOrEmpty(XRSettings.loadedDeviceName))
            {
                // In OpenXR/XR SDK, if loaded device name is active and not simulation
                if (!XRSettings.loadedDeviceName.ToLower().Contains("mock") &&
                    !XRSettings.loadedDeviceName.ToLower().Contains("simulat"))
                {
                    return true;
                }
            }

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (display.running)
                {
                    // Check if not a mock/simulated display
                    string id = display.subsystemDescriptor != null ? display.subsystemDescriptor.id : "";
                    if (!id.ToLower().Contains("mock") && !id.ToLower().Contains("simulat"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
