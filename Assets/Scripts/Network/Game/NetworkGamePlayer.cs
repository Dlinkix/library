using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using static DataGame;

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

    [SyncVar(hook = nameof(OnDiceRollAmountChanged))]
    private int DiceRollAmount;

    [SyncVar(hook = nameof(OnHpChanged))]
    public int currentLight;

    [SyncVar(hook = nameof(OnHpChanged))]
    public int maxLight;


    [Header("UI")]
    [SerializeField] private Vector2 uiOffset = Vector2.zero;
    [SerializeField] private DataGame dataGame;
    [SerializeField] private int startingHandSize = 4;
    [SerializeField] private int cardsToDrawAfterReadyCycle = 1;

    [Header("Setting")]
    [SerializeField] private float attackDelay = 0.5f;

    private readonly List<int> playerPool = new List<int>();
    private readonly List<int> playerDeck = new List<int>();
    private readonly List<int> playerHand = new List<int>();
    private readonly List<int> localHandCardIds = new List<int>();
    private DataGame.PlayerData activePlayerData;
    
    private GameObject uiObject;
    private RectTransform uiRect;
    private bool uiCreated;
    private Slider hpSlider;
    private Slider staggerSlider;
    private TMP_Text hpText;
    private TMP_Text staggerText;
    private TMP_Text rollText;
    private TMP_Text nametext;
    private Button readyButton;
    private bool isShowingRollResult;
    private int selectedCardId = -1;
    private uint selectedTargetEnemyNetId = 0;
    private int selectedTargetDiceIndex = -1;
    private RectTransform localHandRoot;
    private RectTransform localHandContentRoot;
    private TMP_Text localHandTitle;
    private Coroutine delayedHandRefreshCoroutine;
    private Queue<Action> pendingActions = new Queue<Action>();
    private float actionTimer = 0f;
    private bool isExecutingActions = false;
    public static List<NetworkGamePlayer> AllPlayers { get; } = new List<NetworkGamePlayer>();


    public int GetSelectedCardId() => selectedCardId;
    public uint GetSelectedTargetEnemyNetId() => selectedTargetEnemyNetId;
    public int GetSelectedTargetDiceIndex() => selectedTargetDiceIndex;



    #region other
    public override void OnStartServer()
    {
        if (!AllPlayers.Contains(this))
        {
            AllPlayers.Add(this);
        }

        ApplyPlayerStatsFromData();
        InitializeCardState();
    }


    public override void OnStartClient()
    {
        CreateUI();
        ApplyUIPositionBySlot();

        if (!isLocalPlayer)
        {
            UIAimLine[] aimLines = FindObjectsByType<UIAimLine>(FindObjectsSortMode.None);
            foreach (var line in aimLines)
            {
                if (line.gameObject.GetComponentInParent<NetworkGamePlayer>() == this)
                {
                    Destroy(line.gameObject);
                }
            }
        }
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

   


    private void OnSlotIndexChanged(int oldValue, int newValue)
    {
        ApplyUIPositionBySlot();
    }
    private void OnDiceRollAmountChanged(int oldValue, int newValue)
    {
        UpdateIconDiceRoll();
    }

    private void OnLightAmountChanged(int oldValue, int newValue)
    {
        UpdateIconLight();
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

        if (slotIndex < 0)
        {
            Debug.LogWarning($"Slot index not set for {PlayerName}, waiting...");
            return;
        }

        // Находим Anchor по slotIndex
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
            Debug.LogWarning($"PlayerUIAnchor with slot {slotIndex} not found for {PlayerName}");
            return;
        }
        uiObject = Instantiate(uiPrefab, targetAnchor.transform);
        uiRect = uiObject.GetComponent<RectTransform>();
        if (uiRect == null)
        {
            Destroy(uiObject);
            return;
        }
        

        uiRect.localPosition = new Vector3(uiOffset.x, uiOffset.y, 0f);
        uiRect.localRotation = Quaternion.identity;
        uiRect.localScale = Vector3.one;

        CreateDiceUI();
        if (isLocalPlayer)
        {
            UIAimLine aimLine = uiObject.GetComponent<UIAimLine>();
            if (aimLine == null)
            {
                aimLine = uiObject.AddComponent<UIAimLine>();
            }        
            aimLine.SetOwner(this, targetAnchor.GetComponent<RectTransform>());
        }
        else
        {
            UIAimLine aimLine = uiObject.GetComponent<UIAimLine>();
            if (aimLine != null)
            {
                // Для нелокального игрока тоже вызываем SetOwner до Start()
                aimLine.SetOwner(this);
            }
        }
        Transform gridTransform = uiObject.transform.Find("GridDice");
        Transform imageDice = gridTransform?.Find("DiceRoll");
        rollText = imageDice?.Find("Text (TMP)")?.GetComponent<TMP_Text>();

        hpText = uiObject.transform.Find("HpText")?.GetComponent<TMP_Text>();
        staggerText = uiObject.transform.Find("StaggerText")?.GetComponent<TMP_Text>();
        hpSlider = uiObject.transform.Find("HpSlider")?.GetComponent<Slider>();
        staggerSlider = uiObject.transform.Find("StaggerSlider")?.GetComponent<Slider>();
        readyButton = uiObject.transform.Find("ReadyButton")?.GetComponent<Button>();
        nametext = uiObject.transform.Find("NameText")?.GetComponent<TMP_Text>();


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

    private void CreateDiceUI()
    {
        Transform gridTransform = uiObject.transform.Find("GridDice");
        if (gridTransform == null)
        {
            Debug.LogWarning("GridDice not found in UI!");
            return;
        }

        // Удаляем старые кубики
        foreach (Transform child in gridTransform)
            Destroy(child.gameObject);

        GameObject dicePrefab = Resources.Load<GameObject>("UI/DiceRoll");
        if (dicePrefab == null)
        {
            Debug.LogWarning("DiceRoll prefab not found!");
            return;
        }

        for (int i = 0; i < DiceRollAmount; i++)
        {
            GameObject diceObj = Instantiate(dicePrefab, gridTransform);
            DiceRoll dice = diceObj.GetComponent<DiceRoll>();
            dice.SetOwner(this, i);
        }
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
        // Если UI создан как дочерний Anchor, позиция уже правильная
        if (uiCreated && uiObject != null && uiObject.transform.parent != null)
        {
            // Проверяем, что родитель - PlayerUIAnchor
            if (uiObject.transform.parent.GetComponent<PlayerUIAnchor>() != null)
            {
                // Убеждаемся что локальная позиция нулевая
                uiObject.transform.localPosition = Vector3.zero;
                uiObject.transform.localRotation = Quaternion.identity;
                uiObject.transform.localScale = Vector3.one;
                return;
            }
        }

        // Старая логика на случай, если UI не привязан к Anchor
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

        // Если UI не дочерний - устанавливаем позицию
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
            nametext.text = isReady ? "Waiting for other players..." : "Press SPACE to ready";
        }
        else
        {
            nametext.text = isReady ? $"{PlayerName}: ready" : $"{PlayerName}: not ready";
        }
    }
    

    public void SelectCard(int cardId)
    {
        selectedCardId = cardId;
    }
    public void SelectTarget(uint enemyNetId, int diceIndex)
    {
        selectedTargetEnemyNetId = enemyNetId;
        selectedTargetDiceIndex = diceIndex;
    }


    public void ClearSelection()
    {
        selectedCardId = -1;
        selectedTargetEnemyNetId = 0;
        selectedTargetDiceIndex = -1;
    }

    public bool HasSelection()
    {
        return selectedCardId != -1 && selectedTargetEnemyNetId != 0;
    }
    private void UpdateIconDiceRoll()
    {

    }
    private void UpdateIconLight()
    {

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

    private void OnReadyButtonClick()
    {
        if (isLocalPlayer && !isReady)
        {
            CmdSetPlayerReady();
        }
    }


    private void ClearRollText()
    {
        isShowingRollResult = false;
        UpdateReadyText();
    }

    private void Update()
    {
        // Клиентская часть
        if (isLocalPlayer)
        {
            if (Input.GetKeyDown(KeyCode.Space) && !isReady)
            {
                CmdSetPlayerReady();
            }
        }

        // Серверная часть
        if (isServer)
        {
            ProcessActionQueue();
        }
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
        hoverView.Setup(cardRect, layoutElement, descText, cardBackground, cardId, this);
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

    [Server]
    private void ApplyPlayerStatsFromData()
    {
        EnsureDataGameReference();
        activePlayerData = GetActivePlayerData();

        if (activePlayerData == null)
        {
            Debug.LogWarning($"Player data is missing in DataGame for {name}. Using current stat defaults.");
            hp = Maxhp;
            stagger = Maxstagger;
            return;
        }

        Maxhp = activePlayerData.maxHealth;
        Maxstagger = activePlayerData.maxStagger;
        hp = Maxhp;
        stagger = Maxstagger;
        DiceRollAmount = activePlayerData.diceRollPlayer;
        maxLight = activePlayerData.baseStartLight;
        currentLight = maxLight;
    }

    private DataGame.PlayerData GetActivePlayerData()
    {
        EnsureDataGameReference();

        if (dataGame == null || dataGame.playerData == null || dataGame.playerData.Count == 0)
        {
            return null;
        }

        return dataGame.GetPlayerData();
    }
    public DataGame.CardData GetCardData(int cardId)
    {
        if (dataGame == null) return null;
        dataGame.TryGetCardById(cardId, out CardData card);
        return card;
    }
    public int GetCardsToDrawAfterReadyCycle()
    {
        return cardsToDrawAfterReadyCycle;
    }
    private int GetMaxCardsInHand()
    {
        DataGame.PlayerData playerData = activePlayerData ?? GetActivePlayerData();
        if (playerData == null)
        {
            return int.MaxValue;
        }

        return Mathf.Max(0, playerData.maxCardOnHand);
    }

    public int GetRollValue()
    {
        DataGame.PlayerData playerData = activePlayerData ?? GetActivePlayerData();
        if (playerData == null)
        {
            return 0;
        }

        int minSpeed = playerData.baseSpeedMin;
        int maxSpeed = playerData.baseSpeedMax;
        if (minSpeed > maxSpeed)
        {
            int temp = minSpeed;
            minSpeed = maxSpeed;
            maxSpeed = temp;
        }

        return UnityEngine.Random.Range(minSpeed, maxSpeed + 1);
    }
    private NetworkGameEnemy FindEnemyByDice(DiceRoll dice)
    {
        if (dice == null) return null;

        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null && enemy.netId == dice.ownerNetId)
            {
                return enemy;
            }
        }
        return null;
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
    private DiceRoll FindDiceByNetId(uint netId)
    {
        // Ищем среди кубиков игроков
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player == null) continue;
            // Найти все DiceRoll на UI игрока
            DiceRoll[] dices = player.GetComponentsInChildren<DiceRoll>();
            foreach (var dice in dices)
            {
                if (dice != null && dice.netId == netId)
                    return dice;
            }
        }

        // Ищем среди кубиков врагов
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy == null) continue;
            DiceRoll[] dices = enemy.GetComponentsInChildren<DiceRoll>();
            foreach (var dice in dices)
            {
                if (dice != null && dice.netId == netId)
                    return dice;
            }
        }

        return null;
    }

    public override void OnStopServer()
    {
        AllPlayers.Remove(this);
    }

    #endregion

    #region Server

    [Server]
    public void QueueCardForTarget(int cardId, NetworkGameEnemy targetEnemy)
    {
        if (dataGame == null) return;
        if (!dataGame.TryGetCardById(cardId, out CardData card)) return;
        if (targetEnemy == null) return;

        // Тратим Light
        currentLight -= card.lightCost;

        // Удаляем карту из руки
        if (playerHand.Contains(cardId))
        {
            playerHand.Remove(cardId);
            SyncHandToOwner();
        }

        // Добавляем эффекты в очередь
        QueueCardEffects(card, targetEnemy);

        Debug.Log($"[QueueCardForTarget] Card {card.cardName} queued for {targetEnemy.EnemyName}");
    }


    [Server]
    private void ProcessActionQueue()
    {
        if (!isExecutingActions || pendingActions.Count == 0)
        {
            isExecutingActions = false;
            return;
        }

        actionTimer += Time.deltaTime;

        if (actionTimer >= attackDelay)
        {
            actionTimer = 0f;
            Action action = pendingActions.Dequeue();
            action?.Invoke();

            if (pendingActions.Count == 0)
            {
                isExecutingActions = false;
                Debug.Log("[ProcessActionQueue] All actions completed");
            }
        }
    }


    [Server]
    public void QueueCardEffects(DataGame.CardData card, NetworkGameEnemy targetEnemy)
    {
        Debug.Log($"[QueueCardEffects] Card: {card.cardName}, Attacks: {card.attacks?.Length ?? 0}");

        if (card.attacks != null)
        {
            foreach (var attack in card.attacks)
            {
                pendingActions.Enqueue(() => ApplyAttack(attack, targetEnemy));
                Debug.Log($"[QueueCardEffects] Added attack: {attack.attackName}");
            }
        }

        if (card.passiveActions != null)
        {
            foreach (var passive in card.passiveActions)
            {
                pendingActions.Enqueue(() => ApplyPassiveEffect(passive));
            }
        }

        if (!isExecutingActions)
        {
            isExecutingActions = true;
            actionTimer = 0f;
            Debug.Log("[QueueCardEffects] Started executing actions");
        }
    }

    

    //[Server]
    //public void ApplyCardToEnemy(int cardId, NetworkGameEnemy targetEnemy)
    //{
    //    if (dataGame == null) return;
    //    if (!dataGame.TryGetCardById(cardId, out CardData card)) return;

    //    // Тратим Light
    //    currentLight -= card.lightCost;

    //    // Применяем эффекты
    //    ApplyCardEffects(card, targetEnemy);

    //    // Удаляем карту из руки
    //    if (playerHand.Contains(cardId))
    //    {
    //        playerHand.Remove(cardId);
    //        SyncHandToOwner();
    //    }
    //}

    [Server]
    private void ApplyCardEffects(DataGame.CardData card, NetworkGameEnemy targetEnemy)
    {
        // Применяем атаки
        if (card.attacks != null)
        {
            foreach (var attack in card.attacks)
            {
                ApplyAttack(attack, targetEnemy);
            }
        }

        // Применяем пассивные эффекты
        if (card.passiveActions != null)
        {
            foreach (var passive in card.passiveActions)
            {
                ApplyPassiveEffect(passive);
            }
        }
    }

    [Server]
    private void ApplyAttack(DataGame.AttackData attack, NetworkGameEnemy target)
    {
        if (target == null) return;

        int roll = UnityEngine.Random.Range(attack.RollMin, attack.RollMax + 1);

        // ===== ИСПОЛЬЗУЕМ attack.Type (поле объекта), а не DataGame.AttackData.Type =====
        switch (attack.type)
        {
            case DataGame.AttackData.Type.Damage:
                target.hp -= roll;
                if (target.hp < 0) target.hp = 0;
                Debug.Log($"{PlayerName} нанес {roll} урона {target.EnemyName}");
                break;

            case DataGame.AttackData.Type.Block:
                Debug.Log($"{PlayerName} использовал Block");
                break;

            case DataGame.AttackData.Type.Escape:
                Debug.Log($"{PlayerName} использовал Escape");
                break;
        }

        if (attack.staggerDamage > 0)
        {
            target.stagger += attack.staggerDamage;
            Debug.Log($"{PlayerName} нанес {attack.staggerDamage} стагера {target.EnemyName}");
        }
    }

    [Server]
    private void ApplyPassiveEffect(DataGame.PassiveAction passive)
    {
        if (passive == null) return;

        switch (passive.effectType)
        {
            case DataGame.PassiveEffectType.DrawCard:
                DrawCardFromDeck(passive.value);
                Debug.Log($"{PlayerName} нарисовал {passive.value} карт(ы)");
                break;

            case DataGame.PassiveEffectType.GainLight:
                currentLight += passive.value;
                Debug.Log($"{PlayerName} получил {passive.value} света");
                break;

            case DataGame.PassiveEffectType.HealPlayer:
                hp += passive.value;
                if (hp > Maxhp) hp = Maxhp;
                Debug.Log($"{PlayerName} вылечил {passive.value} HP");
                break;

            default:
                Debug.Log($"Passive effect {passive.effectType} not implemented yet");
                break;
        }
    }

    [Server]
    public void PrepareStartingHand()
    {
        ApplyPlayerStatsFromData();
        InitializeCardState();
        DrawCardFromDeck(startingHandSize);
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
        activePlayerData = GetActivePlayerData();

        playerPool.Clear();
        playerDeck.Clear();
        playerHand.Clear();

        if (dataGame == null)
        {
            Debug.LogWarning($"DataGame is missing on {name}. Cards will not initialize.");
            SyncHandToOwner();
            return;
        }

        if (activePlayerData == null)
        {
            Debug.LogWarning($"Player data is missing in DataGame for {name}. Cards will not initialize.");
            SyncHandToOwner();
            return;
        }

        List<int> configuredCardIds = dataGame.GetPlayerCardIds();
        if (configuredCardIds.Count == 0)
        {
            Debug.LogWarning($"Player data for {name} does not contain any valid cards. Cards will not initialize.");
            SyncHandToOwner();
            return;
        }

        int deckLimit = Mathf.Max(0, activePlayerData.dekaPlayer);
        if (configuredCardIds.Count > deckLimit && deckLimit > 0)
        {
            Debug.LogWarning($"Player data for {name} contains more cards ({configuredCardIds.Count}) than dekaPlayer ({deckLimit}). Extra cards will be ignored.");
        }

        int cardsToUse = deckLimit > 0 ? Mathf.Min(configuredCardIds.Count, deckLimit) : 0;
        for (int i = 0; i < cardsToUse; i++)
        {
            int cardId = configuredCardIds[i];
            playerPool.Add(cardId);
            playerDeck.Add(cardId);
        }

        SyncHandToOwner();
    }


    [Server]
    public void AddRandomCardFromPoolToDeck()
    {
        EnsureDataGameReference();

        if (dataGame == null)
        {
            return;
        }

        int cardId = dataGame.GetRandomAllCardId();
        if (cardId <= 0)
        {
            Debug.LogWarning($"AllCards does not contain a valid random card for {name}.");
            return;
        }

        playerDeck.Add(cardId);
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

    [Server]
    public void DrawCardFromDeck(int count = 1)
    {
        int maxCardsInHand = GetMaxCardsInHand();

        for (int i = 0; i < count; i++)
        {
            if (playerHand.Count >= maxCardsInHand)
            {
                break;
            }

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
            int roll = player.GetRollValue();
            player.RpcShowRollResult(roll, player.PlayerName);
        }

        NetworkGameEnemy.RunEnemyReadyCycle();

        for (int i = 0; i < AllPlayers.Count; i++)
        {
            NetworkGamePlayer player = AllPlayers[i];
            player.AddRandomCardFromPoolToDeck();
            player.DrawCardFromDeck(player.cardsToDrawAfterReadyCycle);
            player.isReady = false;
        }
    }

    #endregion


    #region Client

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
    public void RpcShowRollResult(int roll, string playerName)
    {
        if (rollText == null)
        {
            return;
        }

        isShowingRollResult = true;
        rollText.text = $"{roll}";
        nametext.text = $"{playerName}";

        //CancelInvoke(nameof(ClearRollText));
        //Invoke(nameof(ClearRollText), 3f);
    }


    #endregion


    #region Command


    [Command]
    public void CmdPlayCard(int cardId, uint enemyNetId, int enemyDiceIndex)
    {
        Debug.Log($"[CmdPlayCard] Called! Player: {PlayerName}, Card: {cardId}, EnemyNetId: {enemyNetId}, DiceIndex: {enemyDiceIndex}");

        if (!isLocalPlayer) return;
        if (FightManager.Instance == null || !FightManager.Instance.IsFightActive) return;
        if (FightManager.Instance.CurrentState != FightState.Rolling) return;

        if (!playerHand.Contains(cardId)) return;
        if (dataGame == null) return;
        if (!dataGame.TryGetCardById(cardId, out CardData card)) return;
        if (currentLight < card.lightCost) return;

        // ===== НАХОДИМ ВРАГА ПО netId =====
        NetworkGameEnemy targetEnemy = null;
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null && enemy.netId == enemyNetId)
            {
                targetEnemy = enemy;
                break;
            }
        }

        if (targetEnemy == null)
        {
            Debug.Log($"[CmdPlayCard] Enemy with netId {enemyNetId} not found!");
            return;
        }

        // Проверяем, что кубик с таким индексом существует у врага
        // (можно добавить дополнительную проверку)

        currentLight -= card.lightCost;
        playerHand.Remove(cardId);

        // Добавляем в очередь вместо прямого вызова
        QueueCardEffects(card, targetEnemy);

        SyncHandToOwner();
    }

    [Command]
    private void CmdSetPlayerReady()
    {
        // Проверяем, не готов ли уже игрок
        if (isReady)
        {
            Debug.Log($"[CmdSetPlayerReady] Player {PlayerName} is already ready!");
            return;
        }

        // Проверяем, активен ли бой
        if (FightManager.Instance != null && FightManager.Instance.IsFightActive)
        {
            // Проверяем, можно ли сейчас готовиться (только Waiting и Rolling)
            if (!FightManager.Instance.CanPlayerReady())
            {
                Debug.LogWarning($"[CmdSetPlayerReady] Cannot ready in state: {FightManager.Instance.CurrentState}");
                return;
            }

            // Устанавливаем готовность
            isReady = true;

            // Уведомляем FightManager о готовности игрока
            FightManager.Instance.PlayerReady(this);

            Debug.Log($"[CmdSetPlayerReady] Player {PlayerName} is ready in fight! ({FightManager.Instance.GetReadyPlayersCount()}/{FightManager.Instance.GetTotalPlayersCount()})");
        }
        else
        {
            // Старая логика для лобби (до начала боя)
            SetReady(true);
            Debug.Log($"[CmdSetPlayerReady] Player {PlayerName} is ready in lobby!");
        }
    }

    [Command]
    private void CmdRequestInitialHandSync()
    {
        SyncHandToOwner();
    }


    #endregion

}
