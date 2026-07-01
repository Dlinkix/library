using Mirror;
using UnityEngine;
using System.Collections.Generic;

public class ChatManager : NetworkBehaviour
{
    public static ChatManager Instance { get; private set; }

    public readonly SyncList<ChatMessage> messages = new SyncList<ChatMessage>();

    [Header("Settings")]
    [SerializeField] private int maxMessages = 60;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        messages.Callback += OnMessagesChanged;
        Debug.Log($"[ChatManager] Start. isServer: {isServer}, isClient: {isClient}");
    }

    void OnDestroy()
    {
        messages.Callback -= OnMessagesChanged;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnMessagesChanged(SyncList<ChatMessage>.Operation op, int index,
         ChatMessage oldItem, ChatMessage newItem)
        {
            if (op == SyncList<ChatMessage>.Operation.OP_ADD)
            {
                Debug.Log($"[ChatManager] Новое сообщение: {newItem.sender}: {newItem.message}");
                AddMessageToAllChats(newItem);
            }
        }
    private void AddMessageToAllChats(ChatMessage message)
    {
#if UNITY_2022_1_OR_NEWER
        Chat[] chats = Object.FindObjectsByType<Chat>(FindObjectsSortMode.None);
#else
            Chat[] chats = FindObjectsOfType<Chat>();
#endif

        Debug.Log($"[ChatManager] Найдено чатов: {chats.Length}");

        foreach (var chat in chats)
        {
            if (chat != null)
            {
                chat.AddMessageToUI(message);
            }
        }
    }


[Command(requiresAuthority = false)]
    public void CmdSendMessage(string sender, string messageText)
    {
        if (string.IsNullOrEmpty(messageText)) return;

        Debug.Log($"[ChatManager Server] {sender}: {messageText}");

        ChatMessage newMessage = new ChatMessage
        {
            sender = sender,
            message = messageText,
            timestamp = (int)NetworkTime.time
        };

        messages.Add(newMessage);

        if (messages.Count > maxMessages)
            messages.RemoveAt(0);
    }

    [System.Serializable]
    public struct ChatMessage
    {
        public string sender;
        public string message;
        public int timestamp;
    }
}