using Mirror;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class NetworkGamePlayer : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string PlayerName = "Unknown";

    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReady = false;

    [SyncVar(hook = nameof(OnSlotIndexChanged))]
    private int slotIndex = -1;

    [Header("UI")]
    [SerializeField] private Vector2 uiOffset = Vector2.zero;

    private GameObject uiObject;
    private RectTransform uiRect;
    private bool uiCreated = false;

    private TMP_Text rollText;
    private Button readyButton;
    private bool isShowingRollResult = false;


    public static List<NetworkGamePlayer> AllPlayers = new List<NetworkGamePlayer>();

    public override void OnStartServer(){
        if (!AllPlayers.Contains(this))
            AllPlayers.Add(this);

     
    }

    public override void OnStartClient()
    {
        CreateUI();
        ApplyUIPositionBySlot();
    }

    public override void OnStartLocalPlayer()
    {
        SetupUIForLocalOrRemotePlayer();
        UpdateReadyText();

    }

    [Server]
    public void ServerSetSlotIndex(int index)
    {
        slotIndex = index;
    }

    private void OnSlotIndexChanged(int oldValue, int newValue)
    {
        Debug.Log($"Slot changed for {PlayerName}: {oldValue} -> {newValue}");
        ApplyUIPositionBySlot();
    }

    private void OnPlayerNameChanged(string oldName, string newName)
    {
        UpdateReadyText();
    }

    private void OnReadyChanged(bool oldValue, bool newValue)
    {
        if (isShowingRollResult)
            return;

        UpdateReadyText();
    }

    private void CreateUI()
    {
        if (uiCreated) return;

        GameObject uiPrefab = Resources.Load<GameObject>("UI/PlayerUI");

        Canvas canvas = FindFirstObjectByType<Canvas>();

        uiObject = Instantiate(uiPrefab);
        uiObject.transform.SetParent(canvas.transform, false);

        uiRect = uiObject.GetComponent<RectTransform>();

        if (uiRect == null)
        {
            Destroy(uiObject);
            return;
        }

        rollText = uiObject.transform.Find("Text (TMP)")?.GetComponent<TMP_Text>();

        if (rollText == null)
            rollText = uiObject.GetComponentInChildren<TMP_Text>();

        readyButton = uiObject.transform.Find("ReadyButton")?.GetComponent<Button>();

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnReadyButtonClick);
        }

        uiCreated = true;

        SetupUIForLocalOrRemotePlayer();
        UpdateReadyText();
        ApplyUIPositionBySlot();

        Debug.Log($"UI создан для {PlayerName}. IsLocalPlayer = {isLocalPlayer}, Slot = {slotIndex}");
    }

    private void SetupUIForLocalOrRemotePlayer()
    {
        if (readyButton == null) return;

        if (isLocalPlayer)
        {
            readyButton.gameObject.SetActive(true);
            readyButton.interactable = true;
        }
        else
        {
            readyButton.gameObject.SetActive(false);
        }
    }

    private void ApplyUIPositionBySlot()
    {
        if (!uiCreated) return;
        if (uiObject == null) return;
        if (slotIndex < 0) return;

        PlayerUIAnchor[] anchors = FindObjectsByType<PlayerUIAnchor>(FindObjectsSortMode.None);

        PlayerUIAnchor targetAnchor = null;

        foreach (PlayerUIAnchor anchor in anchors)
        {
            if (anchor.SlotIndex == slotIndex)
            {
                targetAnchor = anchor;
                break;
            }
        }

        RectTransform anchorRect = targetAnchor.GetComponent<RectTransform>();

        if (anchorRect == null)
        {
            Debug.LogWarning($"UI anchor {targetAnchor.name} не имеет RectTransform.");
            return;
        }

        Vector3 finalPosition = anchorRect.position + new Vector3(uiOffset.x, uiOffset.y, 0f);

        uiObject.transform.position = finalPosition;
    }

    private void UpdateReadyText()
    {
        if (!uiCreated) return;
        if (rollText == null) return;

        if (isLocalPlayer)
        {
            rollText.text = isReady
                ? "Ожидаем других игроков..."
                : "Нажми ПРОБЕЛ чтобы начать ход";
        }
        else
        {
            rollText.text = isReady
                ? $"{PlayerName}: готов"
                : $"{PlayerName}: не готов";
        }
    }

    private void OnReadyButtonClick()
    {
        if (isLocalPlayer && !isReady)
            CmdSetPlayerReady();
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetKeyDown(KeyCode.Space) && !isReady)
        {
            CmdSetPlayerReady();
        }
    }

    [Command]
    private void CmdSetPlayerReady()
    {
        if (!isReady)
        {
            SetReady(true);
        }
    }

    [Server]
    public void SetReady(bool ready)
    {
        isReady = ready;
        CheckAllPlayersReady();
    }

    [Server]
    public static void CheckAllPlayersReady()
    {
        if (AllPlayers.Count == 0) return;

        foreach (var player in AllPlayers)
        {
            if (!player.isReady)
                return;
        }

        foreach (var player in AllPlayers)
        {
            int roll = Random.Range(1, 7);
            player.RpcShowRollResult(roll, player.PlayerName);
        }

        foreach (var player in AllPlayers)
        {
            player.isReady = false;
        }
    }

    [ClientRpc]
    private void RpcShowRollResult(int roll, string playerName)
    {
        if (rollText == null)
            return;

        isShowingRollResult = true;

        rollText.text = $"{playerName} выбросил: {roll}";

        CancelInvoke(nameof(ClearRollText));
        Invoke(nameof(ClearRollText), 3f);

        Debug.Log($"Roll result. Player: {playerName}, Roll: {roll}, Object: {name}");
    }

    private void ClearRollText()
    {
        isShowingRollResult = false;
        UpdateReadyText();
    }

    public override void OnStopClient()
    {
        if (uiObject != null)
            Destroy(uiObject);

        uiCreated = false;
    }

    public override void OnStopServer()
    {
        AllPlayers.Remove(this);
    }
}