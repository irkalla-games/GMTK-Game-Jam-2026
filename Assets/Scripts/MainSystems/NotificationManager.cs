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
