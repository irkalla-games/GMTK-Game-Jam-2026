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

    private void Awake()
    {
        Instance = this;

        panel.SetActive(false);

        continueButton.onClick.AddListener(Hide);
    }

    /// <summary>
    /// Whether a notification is up and waiting to be dismissed. Show zeroes Time.timeScale, so
    /// anything wanting to wait for the player to acknowledge cannot use WaitForSeconds - that is
    /// scaled time and would never elapse. Poll this from a WaitUntil instead: coroutines still run a
    /// frame at a time at timeScale 0.
    /// </summary>
    public bool IsShowing => panel != null && panel.activeSelf;

    public void Show(string title, string message)
    {
        titleText.text = title;
        messageText.text = message;

        panel.SetActive(true);

        Time.timeScale = 0f;
    }

    public void Hide()
    {
        panel.SetActive(false);

        Time.timeScale = 1f;
    }
}
