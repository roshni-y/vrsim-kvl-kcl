using UnityEngine;
using TMPro;

public class EscapePortalTrigger : MonoBehaviour
{
    [Header("UI / Banner")]
    public GameObject completionBanner;
    public TextMeshPro completionText;

    private bool m_HasTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (m_HasTriggered) return;

        var isPlayer = other.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null ||
                       other.GetComponent<CharacterController>() != null ||
                       other.CompareTag("MainCamera") ||
                       other.CompareTag("Player");

        if (isPlayer)
        {
            m_HasTriggered = true;

            if (completionBanner != null)
            {
                completionBanner.SetActive(true);
            }

            if (completionText != null)
            {
                completionText.text = "MISSION COMPLETE!\nYOU ESCAPED THE VR TRAINING FACILITY!";
                completionText.gameObject.SetActive(true);
            }

            Debug.Log("[EscapePortalTrigger] MISSION COMPLETE! YOU ESCAPED THE VR TRAINING FACILITY!");
        }
    }
}