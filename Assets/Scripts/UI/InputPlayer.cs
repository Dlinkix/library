using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerNameInput : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField nameInputField = null;
    [SerializeField] private Button continueButton = null;
    public static string DisplayName { get; set; }
    private const string PlayerPrefsNameKey = "PlayerName";

    private void Start()
    {
        nameInputField.onValueChanged.AddListener(SetPlayerName);
        SetUpInputField();
    }

    void SetUpInputField()
    {
        if (PlayerPrefs.HasKey(PlayerPrefsNameKey))
        {
            string defaultName = PlayerPrefs.GetString(PlayerPrefsNameKey);
            nameInputField.text = defaultName;
            SetPlayerName(defaultName);
        }
    }

    public void SetPlayerName(string name)
    {
        DisplayName = name;
        continueButton.interactable = !string.IsNullOrEmpty(name);
    }

    public void SavePlayerName()
    {
        DisplayName = nameInputField.text;
        PlayerPrefs.SetString(PlayerPrefsNameKey, DisplayName);
        PlayerPrefs.Save();
    }
}