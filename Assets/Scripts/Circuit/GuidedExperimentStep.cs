using UnityEngine;

namespace VRContent.Circuit
{
    public enum StepType
    {
        PlaceResistor,      // Grab resistor and drop in socket
        PlaceMeter,         // Grab meter and drop in meter zone
        ConnectWire,        // Connect a wire to terminals
        TogglePower,        // Turn on the power switch
        CheckCircuit,       // Press Check Circuit
        RecordReading,      // Press Record Reading
        VerifyResults,      // Press Verify Results
        Info                // Informational/narration step (auto-advance optional)
    }

    [System.Serializable]
    public class GuidedExperimentStep
    {
        [Header("Display")]
        public string stepTitle = "Step";
        [TextArea(3, 6)]
        public string instructions = "";
        public string targetObjectName = "";   // e.g. "Resistor_100Ohm"
        public string targetSlotName = "";     // e.g. "Socket_KVL_R1"
        public string controlHint = "";        // e.g. "Grip button to grab, release to drop"

        [Header("Validation")]
        public StepType stepType = StepType.Info;
        public float requiredResistance = 0f;  // For PlaceResistor: must match
        public bool autoAdvance = false;       // Auto-proceed when condition met
    }
}
