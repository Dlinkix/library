using System.Collections;
using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NetworkGamePlayer : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string PlayerName = "Unknown";

    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReady;

    [SyncVar(hook = nameof(OnHpChanged))]
    public int hp;

    [SyncVar(hook = nameof(OnHpChanged))]
    public int Maxhp = 40;

    [SyncVar(hook = nameof(OnHpChanged))]
    public int stagger;

    [SyncVar(hook = nameof(OnHpChanged))]
    public int Maxstagger = 16;

    [SyncVar(hook = nameof(OnSlotIndexChanged))]
    private int slotIndex = -1;

    [Header("UI")]
    [SerializeField] private Vector2 uiOffset = Vector2.zero;
    [SerializeField] private DataGame dataGame;
    [SerializeField] private int startingHandSize = 4;
    [SerializeField] private int cardsToDrawAfterReadyCycle = 1;

    private readonly List<int> playerPool = new List<int>();
    private readonly List<int> playerDeck = new List<int>();
    private readonly List<int> playerHand = new List<int>();
    private readonly List<int> localHandCardIds = new List<int>();

    private GameObject uiObject;
    private RectTransform uiRect;
    private bool uiCreated;
    private Slider hpSlider;
    private Slider staggerSlider;
    private TMP_Text hpText;
    private TMP_Text staggerText;
    private TMP_Text rollText;
    private Button readyButton;
    private bool isShowingRollResult;

    private RectTransform localHandRoot;
    private RectTransform localHandContentRoot;
    private TMP_Text localHandTitle;
    private Coroutine delayedHandRefreshCoroutine;

    public static List<NetworkGamePlayer> AllPlayers { get; } = new List<NetworkGamePlayer>();

    public override void OnStartServer()
    {
        if (!AllPlayers.Contains(this))
        {
            AllPlayers.Add(this);
        }

        hp = Maxhp;
        stagger = Maxstagger;
        InitializeCardState();
    }

    public override void OnStartClient()
    {
        CreateUI();
        ApplyUIPositionBySlot();
    }

    public override void OnStartLocalPlayer()
    {
        SetupUIForLocalOrRemotePlayer();
        EnsureLocalHandUI();
        UpdateReadyText();
        RefreshLocalHandUI();
        QueueLocalHandRefresh();
        CmdRequestInitialHandSync();
    }

    [Server]
    public void ServerSetSlotIndex(int index)
    {
        slotIndex = index;
    }

    [Server]
    public void InitializeCardState()
    {
        EnsureDataGameReference();

        playerPool.Clear();
        playerDeck.Clear();
        playerHand.Clear();

        if (dataGame == null)
        {
            Debug.LogWarning($"DataGame is missing on {name}. Cards will not initialize.");
            SyncHandToOwner();
            return;
        }

        IReadOnlyList<int> startPoolIds = dataGame.GetStartPlayerPoolCardIds();
        for (int i = 0; i < startPoolIds.Count; i++)
        {
            playerPool.Add(startPoolIds[i]);
        }

        SyncHandToOwner();
    }

    [Server]
    public void PrepareStartingHand()
    {
        InitializeCardState();
        DrawCardFromDeck(startingHandSize);
    }

    [Server]
    public void AddRandomCardFromPoolToDeck()
    {
        if (playerPool.Count == 0)
        {
            return;
        }

        int randomIndex = Random.Range(0, playerPool.Count);
        int cardId = playerPool[randomIndex];
        playerDeck.Add(cardId);
    }

    [Server]
    public void DrawCardFromDeck(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            if (playerDeck.Count == 0)
            {
                AddRandomCardFromPoolToDeck();
            }

            if (playerDeck.Count == 0)
            {
                break;
            }

            int cardId = playerDeck[0];
            playerDeck.RemoveAt(0);
            playerHand.Add(cardId);
        }

        SyncHandToOwner();
    }

    [Server]
    public void SyncHandToOwner()
    {
        if (connectionToClient == null)
        {
            return;
        }

        TargetSyncHand(connectionToClient, playerHand.ToArray());
    }

    private void OnSlotIndexChanged(int oldValue, int newValue)
    {
        ApplyUIPositionBySlot();
    }

    private void OnPlayerNameChanged(string oldName, string newName)
    {
        UpdateReadyText();
    }

    private void OnReadyChanged(bool oldValue, bool newValue)
    {
        if (isShowingRollResult)
        {
            return;
        }

        UpdateReadyText();
    }

    private void OnHpChanged(int oldValue, int newValue)
    {
        UpdateHpView();
    }

    private void CreateUI()
    {
        if (uiCreated)
        {
            return;
        }

        GameObject uiPrefab = Resources.Load<GameObject>("UI/PlayerUI");
        if (uiPrefab == null)
        {
            Debug.LogWarning("PlayerUI prefab was not found in Resources/UI.");
            return;
        }

        Canvas canvas = FindStatusCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("No canvas found for player status UI.");
            return;
        }

        uiObject = Instantiate(uiPrefab, canvas.transform, false);
        uiRect = uiObject.GetComponent<RectTransform>();
        if (uiRect == null)
        {
            Destroy(uiObject);
            return;
        }

        rollText = uiObject.transform.Find("Text (TMP)")?.GetComponent<TMP_Text>();
        hpText = uiObject.transform.Find("HpText")?.GetComponent<TMP_Text>();
        staggerText = uiObject.transform.Find("StaggerText")?.GetComponent<TMP_Text>();
        hpSlider = uiObject.transform.Find("HpSlider")?.GetComponent<Slider>();
        staggerSlider = uiObject.transform.Find("StaggerSlider")?.GetComponent<Slider>();
        readyButton = uiObject.transform.Find("ReadyButton")?.GetComponent<Button>();

        if (hpSlider != null)
        {
            hpSlider.minValue = 0f;
            hpSlider.maxValue = 100f;
            hpSlider.wholeNumbers = true;
        }

        if (staggerSlider != null)
        {
            staggerSlider.minValue = 0f;
            staggerSlider.maxValue = 100f;
            staggerSlider.wholeNumbers = true;
        }

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnReadyButtonClick);
        }

        uiCreated = true;

        SetupUIForLocalOrRemotePlayer();
        UpdateHpView();
        UpdateReadyText();
        ApplyUIPositionBySlot();
    }

    private void SetupUIForLocalOrRemotePlayer()
    {
        if (readyButton == null)
        {
            return;
        }

        readyButton.gameObject.SetActive(isLocalPlayer);
        readyButton.interactable = isLocalPlayer;
    }

    private void ApplyUIPositionBySlot()
    {
        if (!uiCreated || uiObject == null || slotIndex < 0)
        {
            return;
        }

        PlayerUIAnchor[] anchors = FindObjectsByType<PlayerUIAnchor>(FindObjectsSortMode.None);
        PlayerUIAnchor targetAnchor = null;

        for (int i = 0; i < anchors.Length; i++)
        {
            if (anchors[i].SlotIndex == slotIndex)
            {
                targetAnchor = anchors[i];
                break;
            }
        }

        if (targetAnchor == null)
        {
            return;
        }

        RectTransform anchorRect = targetAnchor.GetComponent<RectTransform>();
        if (anchorRect == null)
        {
            return;
        }

        uiObject.transform.position = anchorRect.position + new Vector3(uiOffset.x, uiOffset.y, 0f);
    }

    private void UpdateReadyText()
    {
        if (!uiCreated || rollText == null)
        {
            return;
        }

        if (isLocalPlayer)
        {
            rollText.text = isReady ? "Waiting for other players..." : "Press SPACE to ready";
        }
        else
        {
            rollText.text = isReady ? $"{PlayerName}: ready" : $"{PlayerName}: not ready";
        }
    }

    private void UpdateHpView()
    {
        if (hpText == null || staggerText == null)
        {
            return;
        }

        hpText.text = hp.ToString();
        staggerText.text = stagger.ToString();

        if (hpSlider != null && Maxhp > 0)
        {
            hpSlider.value = (hp / (float)Maxhp) * hpSlider.maxValue;
        }

        if (staggerSlider != null && Maxstagger > 0)
        {
            staggerSlider.value = (stagger / (float)Maxstagger) * staggerSlider.maxValue;
        }
    }

    private void Update()
    {
        if (!isLocalPlayer)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space) && !isReady)
        {
            CmdSetPlayerReady();
        }
    }

    private void OnReadyButtonClick()
    {
        if (isLocalPlayer && !isReady)
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

    [Command]
    private void CmdRequestInitialHandSync()
    {
        SyncHandToOwner();
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
        if (AllPlayers.Count == 0)
        {
            return;
        }

        for (int i = 0; i < AllPlayers.Count; i++)
        {
            if (!AllPlayers[i].isReady)
            {
                return;
            }
        }

        for (int i = 0; i < AllPlayers.Count; i++)
        {
            NetworkGamePlayer player = AllPlayers[i];
            int roll = Random.Range(1, 7);
            player.RpcShowRollResult(roll, player.PlayerName);
        }

        for (int i = 0; i < AllPlayers.Count; i++)
        {
            NetworkGamePlayer player = AllPlayers[i];
            player.AddRandomCardFromPoolToDeck();
            player.DrawCardFromDeck(player.cardsToDrawAfterReadyCycle);
            player.isReady = false;
        }
    }

    [TargetRpc]
    private void TargetSyncHand(NetworkConnection target, int[] handCardIds)
    {
        localHandCardIds.Clear();
        if (handCardIds != null)
        {
            localHandCardIds.AddRange(handCardIds);
        }

        RefreshLocalHandUI();
        QueueLocalHandRefresh();
    }

    [ClientRpc]
    private void RpcShowRollResult(int roll, string playerName)
    {
        if (rollText == null)
        {
            return;
        }

        isShowingRollResult = true;
        rollText.text = $"{playerName}: {roll}";

        CancelInvoke(nameof(ClearRollText));
        Invoke(nameof(ClearRollText), 3f);
    }

    private void ClearRollText()
    {
        isShowingRollResult = false;
        UpdateReadyText();
    }

    private void EnsureLocalHandUI()
    {
        if (!isLocalPlayer || localHandRoot != null)
        {
            return;
        }

        Canvas canvas = FindStatusCanvas();
        if (canvas == null)
        {
            return;
        }

        GameObject rootObject = new GameObject("LocalHandUI", typeof(RectTransform), typeof(Image));
        localHandRoot = rootObject.GetComponent<RectTransform>();
        localHandRoot.SetParent(canvas.transform, false);
        localHandRoot.anchorMin = new Vector2(0.5f, 0f);
        localHandRoot.anchorMax = new Vector2(0.5f, 0f);
        localHandRoot.pivot = new Vector2(0.5f, 0f);
        localHandRoot.anchoredPosition = new Vector2(0f, 24f);
        localHandRoot.sizeDelta = new Vector2(980f, 250f);

        Image background = rootObject.GetComponent<Image>();
        background.color = new Color(0.06f, 0.07f, 0.11f, 0.88f);
        background.raycastTarget = false;

        GameObject titleObject = new GameObject("HandTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.SetParent(localHandRoot, false);
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -8f);
        titleRect.sizeDelta = new Vector2(-24f, 36f);

        localHandTitle = titleObject.GetComponent<TextMeshProUGUI>();
        localHandTitle.fontSize = 28f;
        localHandTitle.alignment = TextAlignmentOptions.Left;
        localHandTitle.text = "Hand (0)";
        localHandTitle.color = new Color(0.95f, 0.95f, 0.95f);
        localHandTitle.raycastTarget = false;

        GameObject contentObject = new GameObject("HandContent", typeof(RectTransform));
        localHandContentRoot = contentObject.GetComponent<RectTransform>();
        localHandContentRoot.SetParent(localHandRoot, false);
        localHandContentRoot.anchorMin = new Vector2(0f, 0f);
        localHandContentRoot.anchorMax = new Vector2(1f, 1f);
        localHandContentRoot.pivot = new Vector2(0f, 0f);
        localHandContentRoot.offsetMin = new Vector2(16f, 16f);
        localHandContentRoot.offsetMax = new Vector2(-16f, -48f);

        RefreshLocalHandUI();
    }

    private void RefreshLocalHandUI()
    {
        if (!isLocalPlayer)
        {
            return;
        }

        EnsureLocalHandUI();
        EnsureDataGameReference();

        if (localHandContentRoot == null)
        {
            return;
        }

        for (int i = localHandContentRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(localHandContentRoot.GetChild(i).gameObject);
        }

        for (int i = 0; i < localHandCardIds.Count; i++)
        {
            DataGame.CardData cardData = dataGame != null ? dataGame.GetCardById(localHandCardIds[i]) : null;
            CreateHandCardView(cardData, localHandCardIds[i], i, localHandCardIds.Count);
        }

        if (localHandTitle != null)
        {
            localHandTitle.text = $"Hand ({localHandCardIds.Count})";
        }
    }

    private void CreateHandCardView(DataGame.CardData cardData, int cardId, int cardIndex, int totalCards)
    {
        GameObject cardRootObject = new GameObject($"Card_{cardId}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        RectTransform cardRect = cardRootObject.GetComponent<RectTransform>();
        cardRect.SetParent(localHandContentRoot, false);
        cardRect.anchorMin = new Vector2(0f, 0f);
        cardRect.anchorMax = new Vector2(0f, 0f);
        cardRect.pivot = new Vector2(0f, 0f);
        cardRect.sizeDelta = new Vector2(150f, 180f);
        cardRect.anchoredPosition = GetHandCardPosition(cardIndex, totalCards);

        LayoutElement layoutElement = cardRootObject.GetComponent<LayoutElement>();
        layoutElement.preferredWidth = 150f;
        layoutElement.preferredHeight = 180f;

        Image cardBackground = cardRootObject.GetComponent<Image>();
        cardBackground.color = new Color(0.13f, 0.14f, 0.2f, 0.96f);
        cardBackground.raycastTarget = true;

        GameObject artObject = new GameObject("Art", typeof(RectTransform), typeof(Image));
        RectTransform artRect = artObject.GetComponent<RectTransform>();
        artRect.SetParent(cardRect, false);
        artRect.anchorMin = new Vector2(0.5f, 1f);
        artRect.anchorMax = new Vector2(0.5f, 1f);
        artRect.pivot = new Vector2(0.5f, 1f);
        artRect.anchoredPosition = new Vector2(0f, -10f);
        artRect.sizeDelta = new Vector2(126f, 82f);

        Image artImage = artObject.GetComponent<Image>();
        artImage.sprite = cardData != null ? cardData.cardSprite : null;
        artImage.preserveAspect = true;
        artImage.color = artImage.sprite == null ? new Color(0.18f, 0.19f, 0.26f) : Color.white;
        artImage.raycastTarget = false;

        CreateCardText(cardRect, "Name", cardData != null ? cardData.cardName : $"Card {cardId}", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(-18f, 28f), 20f, TextAlignmentOptions.Center, out _);
        CreateCardText(cardRect, "Cost", cardData != null ? $"Light {cardData.lightCost}" : "Light ?", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(-18f, 22f), 16f, TextAlignmentOptions.Center, out _);
        CreateCardText(cardRect, "Desc", cardData != null ? cardData.GetShortDescription() : "No data", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(-18f, 52f), 14f, TextAlignmentOptions.TopLeft, out TMP_Text descText);
        descText.textWrappingMode = TextWrappingModes.Normal;
        descText.overflowMode = TextOverflowModes.Ellipsis;

        LocalHandCardView hoverView = cardRootObject.AddComponent<LocalHandCardView>();
        hoverView.Setup(cardRect, layoutElement, descText, cardBackground);
    }

    private Vector2 GetHandCardPosition(int cardIndex, int totalCards)
    {
        const float cardWidth = 150f;
        const float spacing = 18f;
        float totalWidth = totalCards * cardWidth + Mathf.Max(0, totalCards - 1) * spacing;
        float startX = Mathf.Max(0f, (localHandContentRoot.rect.width - totalWidth) * 0.5f);
        float x = startX + cardIndex * (cardWidth + spacing);
        return new Vector2(x, 0f);
    }

    private void CreateCardText(RectTransform parent, string objectName, string textValue, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, float fontSize, TextAlignmentOptions alignment, out TMP_Text textComponent)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, false);
        textRect.anchorMin = anchorMin;
        textRect.anchorMax = anchorMax;
        textRect.pivot = pivot;
        textRect.anchoredPosition = anchoredPosition;
        textRect.sizeDelta = sizeDelta;

        textComponent = textObject.GetComponent<TextMeshProUGUI>();
        textComponent.text = textValue;
        textComponent.fontSize = fontSize;
        textComponent.enableAutoSizing = false;
        textComponent.alignment = alignment;
        textComponent.color = Color.white;
        textComponent.raycastTarget = false;
    }

    private void QueueLocalHandRefresh()
    {
        if (!isLocalPlayer)
        {
            return;
        }

        if (delayedHandRefreshCoroutine != null)
        {
            StopCoroutine(delayedHandRefreshCoroutine);
        }

        delayedHandRefreshCoroutine = StartCoroutine(RefreshLocalHandWhenSceneReady());
    }

    private IEnumerator RefreshLocalHandWhenSceneReady()
    {
        for (int i = 0; i < 20; i++)
        {
            EnsureLocalHandUI();

            if (localHandRoot != null && localHandContentRoot != null)
            {
                RefreshLocalHandUI();
                delayedHandRefreshCoroutine = null;
                yield break;
            }

            yield return null;
        }

        delayedHandRefreshCoroutine = null;
    }

    private Canvas FindStatusCanvas()
    {
        PlayerUIAnchor anchor = FindFirstObjectByType<PlayerUIAnchor>();
        if (anchor != null)
        {
            Canvas anchorCanvas = anchor.GetComponentInParent<Canvas>();
            if (anchorCanvas != null)
            {
                return anchorCanvas;
            }
        }

        return FindFirstObjectByType<Canvas>();
    }

    private void EnsureDataGameReference()
    {
        if (dataGame != null)
        {
            return;
        }

        DataGame[] loadedData = Resources.FindObjectsOfTypeAll<DataGame>();
        if (loadedData != null && loadedData.Length > 0)
        {
            dataGame = loadedData[0];
        }
    }

    public override void OnStopClient()
    {
        if (uiObject != null)
        {
            Destroy(uiObject);
        }

        if (localHandRoot != null)
        {
            Destroy(localHandRoot.gameObject);
        }

        uiCreated = false;
    }

    public override void OnStopServer()
    {
        AllPlayers.Remove(this);
    }
}
