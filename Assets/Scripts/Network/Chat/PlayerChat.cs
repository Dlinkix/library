using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class Chat : NetworkBehaviour
{
    [Header("UI Settings")]
    [SerializeField] private int MaxCharInput = 60;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private GameObject chatPanel;
    [SerializeField] private GameObject chatMessagePrefab;
    [SerializeField] private Transform messageContainer;
    [SerializeField] private Button SendMessageButton;
    [SerializeField] private ScrollRect scrollRect;

    private string currentMessage = "";
    private bool isChatOpen = false;
    private NetworkRoomPlayerLobby roomPlayer;

    void Start()
    {
        InitializeUI();

        if (chatPanel != null)
            chatPanel.SetActive(false);
        roomPlayer = GetComponentInParent<NetworkRoomPlayerLobby>();

        if (roomPlayer == null)
        {
            Debug.LogError("[Chat] NetworkRoomPlayerLobby не найден в родителе!");
            Debug.Log($"Родитель: {transform.parent?.name ?? "null"}");
        }
        else
        {
            Debug.Log($"[Chat] Найден roomPlayer: {roomPlayer.DisplayName}");
        }
        if (ChatManager.Instance != null && ChatManager.Instance.messages.Count > 0)
        {
            foreach (var msg in ChatManager.Instance.messages)
            {
                AddMessageToUI(msg);
            }
        }

        Debug.Log("[Chat] Инициализация завершена");
    }

    void OnDestroy()
    {
    }

    void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            ToggleChat();
        }
        if (Input.GetKeyDown(KeyCode.Return) && inputField != null && inputField.isFocused)
        {
            SendMessageFromInput();
        }
    }

    private void InitializeUI()
    {
        if (SendMessageButton != null)
            SendMessageButton.onClick.AddListener(SendMessageFromInput);

        if (inputField != null)
        {
            inputField.onValueChanged.AddListener(OnInputValueChanged);
        }
    }

    private void ToggleChat()
    {
        if (chatPanel == null) return;

        isChatOpen = !isChatOpen;
        chatPanel.SetActive(isChatOpen);

        if (inputField != null)
        {
            inputField.Select();
            inputField.ActivateInputField();
        }
    }

    private void OnInputValueChanged(string value)
    {
        if (value.Length > MaxCharInput)
        {
            inputField.text = currentMessage;
            inputField.caretPosition = currentMessage.Length;
        }
        else
        {
            currentMessage = value;
        }
    }

    private void SendMessageFromInput()
    {
        if (!isLocalPlayer) return;

        string message = inputField.text.Trim();
        if (string.IsNullOrEmpty(message)) return;
        if (roomPlayer == null)
        {
            roomPlayer = GetComponentInParent<NetworkRoomPlayerLobby>();
            if (roomPlayer == null)
            {
                Debug.LogError("[Chat] roomPlayer не найден!");
                return;
            }
        }

        string senderName = roomPlayer.DisplayName;

        Debug.Log($"[Chat] Отправка от {senderName}: {message}");

        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.CmdSendMessage(senderName, message);
        }
        else
        {
            Debug.LogError("[Chat] ChatManager.Instance is null!");
        }

        inputField.text = "";
        currentMessage = "";
        inputField.Select();
        inputField.ActivateInputField();
    }

    public void AddMessageToUI(ChatManager.ChatMessage message)
    {
        if (chatMessagePrefab == null || messageContainer == null)
        {
            Debug.LogError("[UI] chatMessagePrefab или messageContainer is null!");
            return;
        }

        Debug.Log($"[UI] Создаем сообщение: {message.sender}: {message.message}");

        GameObject msgObject = Instantiate(chatMessagePrefab, messageContainer);

        TMP_Text textComponent = msgObject.GetComponent<TMP_Text>();
        TMP_Text text = msgObject.transform.Find("Text").GetComponent<TMP_Text>();
        if (text == null) { return; }
        Transform texttrasform = msgObject.transform.Find("Text");
        if (texttrasform == null) { return; }
        TMP_Text texttime = texttrasform.Find("TimeText").GetComponent<TMP_Text>();
        if (texttime == null) { return; }

        string time = System.DateTime.Now.ToString("HH:mm");
        bool isOwnMessage = message.sender == roomPlayer?.DisplayName && isLocalPlayer;
        string color = isOwnMessage ? "#00FF00" : "#FFFFFF";

        textComponent.text = $"<color={color}><b>{message.sender}</b></color>";
        text.text = $"{message.message}";
        texttime.text = $"<color=#888888>[{time}]</color>";

        if (scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    public void ClearAllMessages()
    {
        if (messageContainer == null) return;

        foreach (Transform child in messageContainer)
        {
            Destroy(child.gameObject);
        }
    }
}