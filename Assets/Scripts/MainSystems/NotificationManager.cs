using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance;

    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button continueButton;

    /// <summary>
    /// Whether this currently holds a TimeFreeze claim.
    ///
    /// Show and Hide are both public and Hide is also wired to the Continue button, so neither can
    /// assume it is called once or in order. This keeps the claim balanced regardless.
    /// </summary>
    private bool frozen;

    private void Awake()
    {
        Instance = this;

        panel.SetActive(false);

        continueButton.onClick.AddListener(Hide);
    }

    /// <summary>
    /// Whether a notification is up and waiting to be dismissed. Show freezes time, so anything wanting
    /// to wait for the player to acknowledge cannot use WaitForSeconds - that is scaled time and would
    /// never elapse. Poll this from a WaitUntil instead: coroutines still run a frame at a time at
    /// timeScale 0.
    /// </summary>
    public bool IsShowing => panel != null && panel.activeSelf;

    public void Show(string title, string message)
    {
        titleText.text = title;
        messageText.text = message;

        panel.SetActive(true);

        // Through TimeFreeze rather than writing Time.timeScale directly. The pause menu wants that
        // same global, and whichever of the two released it last would otherwise thaw the other - see
        // TimeFreeze's doc comment for why a count and not a bool.
        if (!frozen)
        {
            frozen = true;
            TimeFreeze.Acquire();
        }
    }

    public void Hide()
    {
        panel.SetActive(false);

        if (frozen)
        {
            frozen = false;
            TimeFreeze.Release();
        }
    }
}
