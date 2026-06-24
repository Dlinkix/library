using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
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

    public static List<NetworkGamePlayer> AllPlayers { get; } = new List<NetworkGamePlayer>();

    [Header("UI")]
    [SerializeField] private Vector2 uiOffset = Vector2.zero;
    [SerializeField] private DataGame dataGame;
    [SerializeField] private int startingHandSize = 4;
    [SerializeField] private int cardsToDrawAfterReadyCycle = 1;

    [Header("Setting")]
    [SerializeField] private float attackDelay = 0.5f;

    [Header("Card View")]
    [SerializeField] private CardView cardViewPrefab;
    private CardView currentCardView;
    private bool isCardViewCreated = false;


    [Header("UI Animation")]
    [SerializeField] private float pushDistance = 300f;
    private Vector3 _attackerOriginalPos;
    private Vector3 _playerOriginalPos;
    private bool isUIMoving = false;

    private readonly List<int> playerPool = new List<int>();
    private readonly List<int> playerDeck = new List<int>();
    private readonly List<int> playerHand = new List<int>();
    private readonly List<int> localHandCardIds = new List<int>();
    private DataGame.PlayerData activePlayerData;

    private GameObject uiObject;
    private RectTransform uiRect;
    private bool uiCreated;
    private UnityEngine.UI.Slider hpSlider;
    private UnityEngine.UI.Slider staggerSlider;
    private TMP_Text hpText;
    private TMP_Text staggerText;
    private TMP_Text rollText;
    private TMP_Text nametext;
    private UnityEngine.UI.Button readyButton;
    private bool isShowingRollResult;
    private RectTransform localHandRoot;
    private RectTransform localHandContentRoot;
    private TMP_Text localHandTitle;
    private Coroutine delayedHandRefreshCoroutine;
    private Queue<Action> pendingActions = new Queue<Action>();
    private float actionTimer = 0f;
    private bool isExecutingActions = false;
    private Vector3 originalUIPosition;
    public GameObject UIObject => uiObject;
    public List<int> PlayerHand => playerHand;
    public DataGame DataGame => dataGame;
    public bool IsExecutingActions => isExecutingActions;



    #region other
    public override void OnStartServer()
    {
        if (!AllPlayers.Contains(this))
        {
            AllPlayers.Add(this);
        }

        ApplyPlayerStatsFromData();
        InitializeCardState();

        StartCoroutine(ServerCreateDiceDelayed());
    }



    public override void OnStartClient()
    {
        if (!AllPlayers.Contains(this))
            AllPlayers.Add(this);

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
        UpdateReadyText();
        StartCoroutine(WaitForReadyAndInitialize());
        Invoke(nameof(UpdateAllDiceRange), 1f);

        if (DiceSelectionManager.Instance != null &&
            DiceSelectionManager.Instance.GetSelectedPlayerDice() != null)
        {
            UpdateHandVisibility();
        }
        Invoke(nameof(DebugHand), 2f);
    }

    private IEnumerator WaitForReadyAndInitialize()
    {
        float timeout = 5f;
        float timer = 0f;

        while (slotIndex < 0 && timer < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            timer += 0.1f;
        }

        if (slotIndex < 0)
        {
            yield break;
        }

        yield return null;

        EnsureLocalHandUI();

        yield return null;

        QueueLocalHandRefresh();
        CmdRequestInitialHandSync();

        Invoke(nameof(UpdateAllDiceRange), 0.5f);

        if (DiceSelectionManager.Instance != null &&
            DiceSelectionManager.Instance.GetSelectedPlayerDice() != null)
        {
            UpdateHandVisibility();
        }
    }
    private IEnumerator RefreshHandAfterUIReady()
    {
        for (int i = 0; i < 30; i++)
        {
            EnsureLocalHandUI();

            if (localHandRoot != null && localHandContentRoot != null)
            {
                RefreshLocalHandUI();

                if (DiceSelectionManager.Instance != null &&
                    DiceSelectionManager.Instance.GetSelectedPlayerDice() != null)
                {
                    UpdateHandVisibility();
                }

                yield break;
            }

            yield return null;
        }
    }
    public void ResetUIPosition()
    {
        if (uiRect != null)
        {
            uiRect.localPosition = Vector3.zero;
        }
    }

    private NetworkGameEnemy FindAttackerEnemy(uint netId)
    {
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
            if (enemy != null && enemy.netId == netId) return enemy;

        return null;
    }
    public void SetCombatPresentationActive(bool isVisible)
    {
        if (uiObject != null)
        {
            uiObject.SetActive(isVisible);
        }

        if (localHandRoot != null)
        {
            bool shouldShowHand = isVisible &&
                                  DiceSelectionManager.Instance != null &&
                                  DiceSelectionManager.Instance.GetSelectedPlayerDice() != null;
            localHandRoot.gameObject.SetActive(shouldShowHand);
        }

        if (!isVisible)
        {
            HideCardView();
        }
    }
    private void DebugHand()
    {
    }
    public bool IsCardInLocalHand(int cardId, int cardIndex)
    {
        if (cardIndex < 0 || cardIndex >= localHandCardIds.Count)
            return false;
        return localHandCardIds[cardIndex] == cardId;
    }

    private void OnSlotIndexChanged(int oldValue, int newValue)
    {
        if (!uiCreated)
        {
            CreateUI();
        }

        ApplyUIPositionBySlot();

        if (uiCreated)
        {
            UpdateAllDiceRange();
        }

        if (isLocalPlayer)
        {
            EnsureLocalHandUI();

            if (DiceSelectionManager.Instance != null &&
                DiceSelectionManager.Instance.GetSelectedPlayerDice() != null)
            {
                UpdateHandVisibility();
            }
            else
            {
                if (localHandRoot != null)
                {
                    localHandRoot.gameObject.SetActive(false);
                }
            }
        }
    }
    private void OnDiceRollAmountChanged(int oldValue, int newValue)
    {
        if (uiObject != null)
        {
            CreateDiceUI();
        }
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
        if (uiCreated) return;

        GameObject uiPrefab = Resources.Load<GameObject>("UI/PlayerUI");
        if (uiPrefab == null)
        {
            return;
        }

        if (slotIndex < 0)
        {
            return;
        }

        PlayerUIAnchor[] anchors = GetScenePlayerAnchors();
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

        if (DiceRollAmount > 0)
        {
            CreateDiceUI();
        }

        Transform gridTransform = uiObject.transform.Find("GridDice");
        Transform imageDice = gridTransform?.Find("DiceRoll");
        rollText = imageDice?.Find("Text (TMP)")?.GetComponent<TMP_Text>();

        hpText = uiObject.transform.Find("HpText")?.GetComponent<TMP_Text>();
        staggerText = uiObject.transform.Find("StaggerText")?.GetComponent<TMP_Text>();
        hpSlider = uiObject.transform.Find("HpSlider")?.GetComponent<UnityEngine.UI.Slider>();
        staggerSlider = uiObject.transform.Find("StaggerSlider")?.GetComponent<UnityEngine.UI.Slider>();
        readyButton = uiObject.transform.Find("ReadyButton")?.GetComponent<UnityEngine.UI.Button>();
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
        // ===== ÑÎÇÄÀÅÌ CARDVIEW ÄËß ËÎÊÀËÜÍÎÃÎ ÈÃÐÎÊÀ =====
        if (isLocalPlayer)
        {
            CreateCardView();
        }

        uiCreated = true;

        if (RunFlowManager.Instance != null)
        {
            RunFlowManager.Instance.RefreshBattleRoot();
        }

        SetupUIForLocalOrRemotePlayer();
        UpdateHpView();
        UpdateReadyText();
        ApplyUIPositionBySlot();
        originalUIPosition = uiRect.position;

        if (RunFlowManager.Instance != null)
        {
            RunFlowManager.Instance.RefreshClientVisuals();
        }

        
    }
    public void UpdateHandVisibility()
    {
        if (!isLocalPlayer)
        {
            return;
        }

        EnsureLocalHandUI();
        if (localHandRoot == null)
        {
            return;
        }

        bool hasSelectedDice = DiceSelectionManager.Instance != null &&
                               DiceSelectionManager.Instance.GetSelectedPlayerDice() != null;

        localHandRoot.gameObject.SetActive(hasSelectedDice);

        if (hasSelectedDice && localHandCardIds.Count > 0)
        {
            RefreshLocalHandUI();
        }
    }
    private void CreateDiceUI()
    {
        Transform gridTransform = uiObject.transform.Find("GridDice");
        if (gridTransform == null)
        {
            return;
        }

        foreach (Transform child in gridTransform)
            Destroy(child.gameObject);

        GameObject dicePrefab = Resources.Load<GameObject>("UI/DiceRoll");
        if (dicePrefab == null)
        {
            return;
        }

        for (int i = 0; i < DiceRollAmount; i++)
        {
            GameObject diceObj = Instantiate(dicePrefab, gridTransform);
            DiceRoll dice = diceObj.GetComponent<DiceRoll>();
            dice.SetOwner(this, i);

            UIAimLine aimLine = diceObj.GetComponent<UIAimLine>();
            if (aimLine != null)
            {
                if (isLocalPlayer)
                {
                    aimLine.SetOwnerDice(dice);
                    dice.SetAimLine(aimLine);
                }
                else
                {
                    Destroy(aimLine);
                }
            }
        }
    }

    public bool IsHandVisible()
    {
        return localHandRoot != null && localHandRoot.gameObject.activeSelf;
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
        if (uiCreated && uiObject != null && uiObject.transform.parent != null)
        {
            if (uiObject.transform.parent.GetComponent<PlayerUIAnchor>() != null)
            {
                uiObject.transform.localPosition = Vector3.zero;
                uiObject.transform.localRotation = Quaternion.identity;
                uiObject.transform.localScale = Vector3.one;
                return;
            }
        }

        if (!uiCreated || uiObject == null || slotIndex < 0)
        {
            return;
        }

        PlayerUIAnchor[] anchors = GetScenePlayerAnchors();
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
            nametext.text = isReady ? "Waiting for other players..." : "Press SPACE to ready";
        }
        else
        {
            nametext.text = isReady ? $"{PlayerName}: ready" : $"{PlayerName}: not ready";
        }
    }

    private static PlayerUIAnchor[] GetScenePlayerAnchors()
    {
        return Resources.FindObjectsOfTypeAll<PlayerUIAnchor>()
            .Where(anchor => anchor != null && anchor.gameObject.scene.IsValid())
            .ToArray();
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
        if (isLocalPlayer)
        {
            if (Input.GetKeyDown(KeyCode.Space) && !isReady)
            {
                CmdSetPlayerReady();
            }
        }

        if (isServer)
        {
            ProcessActionQueue();
        }
    }

    public void EnsureLocalHandUI()
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

        GameObject rootObject = new GameObject("LocalHandUI", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        localHandRoot = rootObject.GetComponent<RectTransform>();
        localHandRoot.SetParent(canvas.transform, false);
        localHandRoot.anchorMin = new Vector2(0.5f, 0f);
        localHandRoot.anchorMax = new Vector2(0.5f, 0f);
        localHandRoot.pivot = new Vector2(0.5f, 0f);
        localHandRoot.anchoredPosition = new Vector2(0f, 24f);
        localHandRoot.sizeDelta = new Vector2(980f, 250f);

        UnityEngine.UI.Image background = rootObject.GetComponent<UnityEngine.UI.Image>();
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

        localHandRoot.gameObject.SetActive(false);


        RefreshLocalHandUI();
    }

    public void RefreshLocalHandUI()
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
        GameObject cardRootObject = new GameObject($"Card_{cardId}_{cardIndex}", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(LayoutElement));
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

        UnityEngine.UI.Image cardBackground = cardRootObject.GetComponent<UnityEngine.UI.Image>();
        cardBackground.color = new Color(0.13f, 0.14f, 0.2f, 0.96f);
        cardBackground.raycastTarget = true;

        GameObject artObject = new GameObject("Art", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        RectTransform artRect = artObject.GetComponent<RectTransform>();
        artRect.SetParent(cardRect, false);
        artRect.anchorMin = new Vector2(0.5f, 1f);
        artRect.anchorMax = new Vector2(0.5f, 1f);
        artRect.pivot = new Vector2(0.5f, 1f);
        artRect.anchoredPosition = new Vector2(0f, -10f);
        artRect.sizeDelta = new Vector2(126f, 82f);

        UnityEngine.UI.Image artImage = artObject.GetComponent<UnityEngine.UI.Image>();
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
        hoverView.Setup(cardRect, layoutElement, descText, cardBackground, cardId, cardIndex, this);
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

    private IEnumerator AnimatePlayerPush(NetworkGameEnemy attacker)
    {
        if (isUIMoving) yield break;
        isUIMoving = true;

        RectTransform attackerRect = attacker.UIObject.GetComponent<RectTransform>();
        if (attackerRect == null || uiRect == null)
        {
            isUIMoving = false;
            yield break;
        }

        Vector3 attackerStartPos = attackerRect.position;
        Vector3 playerStartPos = uiRect.position;

        Vector3 direction = (playerStartPos - attackerStartPos).normalized;
        if (direction.magnitude < 0.1f) direction = Vector3.right;

        // Фаза 1: враг приближается
        Vector3 approachTarget = attackerStartPos + direction * (Vector3.Distance(attackerStartPos, playerStartPos) * 0.95f);

        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            float t = elapsed / 0.3f;
            float smoothT = t * t * (3f - 2f * t);
            attackerRect.position = Vector3.Lerp(attackerStartPos, approachTarget, smoothT);
            elapsed += Time.deltaTime;
            yield return null;
        }
        attackerRect.position = approachTarget;

        // Фаза 2: игрок отодвигается
        Vector3 pushTarget = playerStartPos + direction * pushDistance;

        // Ограничиваем позицию границами Pass
        GameObject[] passObjects = GameObject.FindGameObjectsWithTag("Pass");
        foreach (GameObject pass in passObjects)
        {
            RectTransform passRect = pass.GetComponent<RectTransform>();
            if (passRect != null)
            {
                Vector3[] corners = new Vector3[4];
                passRect.GetWorldCorners(corners);

                float minX = corners[0].x;
                float maxX = corners[2].x;
                float minY = corners[0].y;
                float maxY = corners[2].y;

                pushTarget.x = Mathf.Clamp(pushTarget.x, minX + 50f, maxX - 50f);
                pushTarget.y = Mathf.Clamp(pushTarget.y, minY + 50f, maxY - 50f);
            }
        }

        elapsed = 0f;
        while (elapsed < 0.2f)
        {
            float t = elapsed / 0.2f;
            float smoothT = t * t * (3f - 2f * t);
            uiRect.position = Vector3.Lerp(playerStartPos, pushTarget, smoothT);
            elapsed += Time.deltaTime;
            yield return null;
        }
        uiRect.position = pushTarget;

        isUIMoving = false;
        _attackerOriginalPos = approachTarget;
        _playerOriginalPos = pushTarget;
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

    #endregion

    #region Server

    [Server]
    public void PushPlayerUI(NetworkGameEnemy attacker)
    {
        RpcPushPlayerUI(attacker.netId);
    }

    [Server]
    private IEnumerator ServerCreateDiceDelayed()
    {
        yield return null;
        yield return null;
    }

    [Server]
    public void RollAllDice()
    {
        if (uiObject == null)
        {
            return;
        }

        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();
        List<int> values = new List<int>();

        foreach (var dice in dices)
        {
            if (dice != null)
            {
                int roll = GetRollValue();
                dice.RollDice(roll, roll);
                values.Add(roll);
            }
        }

        RpcUpdateDiceValues(values.ToArray());
    }

    [Server]
    public void QueueCardForTarget(int cardId, int cardIndex, NetworkGameEnemy targetEnemy) 
    {
        if (dataGame == null) return;
        if (!dataGame.TryGetCardById(cardId, out CardData card)) return;
        if (targetEnemy == null) return;

        currentLight -= card.lightCost;

        // Ïðîâåðÿåì, ÷òî êàðòà âñå åùå â ðóêå ïî èíäåêñó
        if (cardIndex < 0 || cardIndex >= playerHand.Count) return;
        if (playerHand[cardIndex] != cardId) return;

        // Óäàëÿåì ïî èíäåêñó
        playerHand.RemoveAt(cardIndex);
        SyncHandToOwner();

        QueueCardEffects(card, cardIndex, targetEnemy); 
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

        // Óâåëè÷èâàåì çàäåðæêó ìåæäó äåéñòâèÿìè
        float totalDelay = attackDelay + 0.5f; // 0.5 ñåêóíäû ìåæäó äåéñòâèÿìè

        if (actionTimer >= totalDelay)
        {
            actionTimer = 0f;
            Action action = pendingActions.Dequeue();
            action?.Invoke();

            if (pendingActions.Count == 0)
            {
                isExecutingActions = false;
            }
        }
    }


    [Server]
    public void QueueCardEffects(DataGame.CardData card, int cardIndex, NetworkGameEnemy targetEnemy)
    {
        // Ïîêàçûâàåì CardView ñ ID è èíäåêñîì
        RpcShowCardView(card.cardId, cardIndex);

        List<int> rollValues = new List<int>();

        if (card.attacks != null)
        {
            foreach (var attack in card.attacks)
            {
                int roll = UnityEngine.Random.Range(attack.RollMin, attack.RollMax + 1);
                rollValues.Add(roll);
            }

            // Îòïðàâëÿåì ñ cardIndex
            RpcUpdateAttackRolls(cardIndex, rollValues.ToArray());

            int attackIndex = 0;
            foreach (var attack in card.attacks)
            {
                int roll = rollValues[attackIndex];
                int currentIndex = attackIndex;

                pendingActions.Enqueue(() => {
                    RpcMoveDiceToPlaceholder(cardIndex, currentIndex);
                });

                pendingActions.Enqueue(() => {
                    targetEnemy.PushEnemyUI(this);
                    ApplyAttack(attack, targetEnemy, roll);
                });

                pendingActions.Enqueue(() => {
                    RpcReturnDiceToGrid(cardIndex);
                });

                attackIndex++;
            }
        }

        if (card.passiveActions != null)
        {
            foreach (var passive in card.passiveActions)
                pendingActions.Enqueue(() => ApplyPassiveEffect(passive));
        }

        if (!isExecutingActions) { isExecutingActions = true; actionTimer = 0f; }
    }


    [ClientRpc]
    public void RpcMoveDiceToPlaceholder(int cardIndex, int attackIndex)
    {
        if (currentCardView != null && currentCardView.gameObject.activeSelf)
        {
            currentCardView.MoveDiceToPlaceholder(cardIndex, attackIndex);
        }
    }

    [ClientRpc]
    public void RpcReturnDiceToGrid(int cardIndex)
    {
        if (currentCardView != null && currentCardView.gameObject.activeSelf)
        {
            currentCardView.ReturnDiceToGrid(cardIndex);
        }
    }
    [Server]
    private void ApplyAttack(DataGame.AttackData attack, NetworkGameEnemy target, int roll)
    {
        if (target == null) return;

        switch (attack.type)
        {
            case DataGame.AttackData.Type.Damage:
                target.hp -= roll;
                if (target.hp < 0) target.hp = 0;
                break;

            case DataGame.AttackData.Type.Block:
                break;

            case DataGame.AttackData.Type.Escape:
                break;
        }

        if (attack.staggerDamage > 0)
        {
            target.stagger += attack.staggerDamage;
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
                break;

            case DataGame.PassiveEffectType.GainLight:
                currentLight += passive.value;
                break;

            case DataGame.PassiveEffectType.HealPlayer:
                hp += passive.value;
                if (hp > Maxhp) hp = Maxhp;
                break;
        }
    }

    [Server]
    public void PrepareStartingHand()
    {
        ApplyPlayerStatsFromData();
        InitializeCardState();
        DrawCardFromDeck(startingHandSize);
        isReady = false;
    }

    [Server]
    public void PrepareForNewEncounter()
    {
        ApplyPlayerStatsFromData();
        InitializeCardState();
        DrawCardFromDeck(startingHandSize);
        isReady = false;
        pendingActions.Clear();
        actionTimer = 0f;
        isExecutingActions = false;
        SyncHandToOwner();
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
            SyncHandToOwner();
            return;
        }

        if (activePlayerData == null)
        {
            SyncHandToOwner();
            return;
        }

        List<int> configuredCardIds = dataGame.GetPlayerCardIds();
        if (configuredCardIds.Count == 0)
        {
            SyncHandToOwner();
            return;
        }

        int deckLimit = Mathf.Max(0, activePlayerData.dekaPlayer);
        if (configuredCardIds.Count > deckLimit && deckLimit > 0)
        {
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

    [ClientRpc]
    private void RpcPushPlayerUI(uint attackerNetId)
    {
        NetworkGameEnemy attacker = FindAttackerEnemy(attackerNetId);
        if (attacker == null || uiRect == null) return;

        // Аналогичная анимация отталкивания для игрока
        StartCoroutine(AnimatePlayerPush(attacker));
    }



    [ClientRpc]
    public void RpcShowCardView(int cardId, int cardIndex)
    {
        DataGame.CardData cardData = GetCardData(cardId);
        if (cardData != null)
        {
            ShowCardView(cardData, cardIndex);
        }
    }


    [ClientRpc]
    public void RpcDisableAttackDice(int cardId, int attackIndex)
    {
        if (currentCardView != null && currentCardView.gameObject.activeSelf)
        {
            currentCardView.DisableAttackDice(attackIndex);
        }
    }

    [Client]
    private void CreateCardView()
    {
        if (cardViewPrefab == null)
        {
            cardViewPrefab = Resources.Load<CardView>("UI/CardView");
            if (cardViewPrefab == null)
            {
                Debug.LogError("[NetworkGamePlayer] CardView prefab not found in Resources/UI/CardView!");
                return;
            }
        }

        // Èñïîëüçóåì uiObject êàê ðîäèòåëÿ
        if (uiObject == null)
        {
            Debug.LogError("[NetworkGamePlayer] uiObject is null, cannot create CardView!");
            return;
        }

        // Ñîçäàåì CardView êàê äî÷åðíèé îáúåêò UI èãðîêà
        GameObject cardViewObj = Instantiate(cardViewPrefab.gameObject, uiObject.transform);
        currentCardView = cardViewObj.GetComponent<CardView>();

        if (currentCardView != null)
        {
            // Íàñòðàèâàåì ïîçèöèþ
            RectTransform rect = cardViewObj.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(50f, 90f); // Ñìåùåíèå ââåðõ
                rect.sizeDelta = new Vector2(400f, 300f);
            }

            currentCardView.gameObject.SetActive(false);
            isCardViewCreated = true;
            Debug.Log("[NetworkGamePlayer] CardView created as child of UI");
        }
    }


    [Client]
    public void ShowCardView(DataGame.CardData cardData, int cardIndex)
    {
        if (!isCardViewCreated)
            CreateCardView();

        if (currentCardView == null) return;

        currentCardView.SetupCard(cardData, cardIndex); 
        currentCardView.ShowCardView();
        Debug.Log($"[NetworkGamePlayer] Showing CardView for card: {cardData.cardName} (index: {cardIndex})");
    }


    [Client]
    public void HideCardView()
    {
        if (currentCardView != null)
        {
            currentCardView.HideCardView();
            Debug.Log("[NetworkGamePlayer] CardView hidden");
        }
    }

    // RPC äëÿ îáíîâëåíèÿ çíà÷åíèé àòàê (ñåðâåð -> âñå êëèåíòû)
    // Îáíîâëÿåì çíà÷åíèÿ àòàê
    [ClientRpc]
    public void RpcUpdateAttackRolls(int cardIndex, int[] rollValues)
    {
        if (currentCardView != null && currentCardView.gameObject.activeSelf)
        {
            currentCardView.UpdateAttackDiceValues(cardIndex, rollValues);
        }
    }


    [Client]
    public void UpdateAllDiceRange()
    {
        if (uiObject == null) return;

        DataGame.PlayerData playerData = GetActivePlayerData();
        if (playerData == null) return;

        int minSpeed = playerData.baseSpeedMin;
        int maxSpeed = playerData.baseSpeedMax;

        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();
        foreach (var dice in dices)
        {
            if (dice != null)
            {
                dice.ShowDiceRange(minSpeed, maxSpeed);
            }
        }
    }
    [Client]
    public void UpdateAllDiceResult()
    {
        if (uiObject == null) return;

        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();
        foreach (var dice in dices)
        {
            if (dice != null && dice.isReady)
            {
                dice.ShowDiceResult(dice.diceValue);
            }
        }
    }

    [ClientRpc]
    public void RpcUpdateDiceValues(int[] values)
    {
        if (values == null || values.Length == 0) return;
        if (uiObject == null) return;

        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();

        for (int i = 0; i < dices.Length && i < values.Length; i++)
        {
            if (dices[i] != null)
            {
                dices[i].SetDiceValue(values[i]);
            }
        }
    }
    [TargetRpc]
    public void TargetUpdateDiceValues(NetworkConnection target, int[] values)
    {
        DiceRoll[] dices = GetComponentsInChildren<DiceRoll>();
        for (int i = 0; i < dices.Length && i < values.Length; i++)
        {
            if (dices[i] != null)
            {
                dices[i].SetDiceValue(values[i]);
            }
        }
    }

    [TargetRpc]
    private void TargetSyncHand(NetworkConnection target, int[] handCardIds)
    {
        localHandCardIds.Clear();

        if (handCardIds != null)
            localHandCardIds.AddRange(handCardIds);


        StartCoroutine(RefreshHandAfterUIReady());
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
    }


    #endregion


    #region Command
    [Command]
    public void CmdSyncDiceSelection(int diceSlotIndex, int cardId, int cardIndex, uint enemyNetId, int enemyDiceIndex)
    {
        if (FightManager.Instance == null || FightManager.Instance.CurrentState != FightState.Rolling)
            return;

        DiceRoll[] serverDices = uiObject.GetComponentsInChildren<DiceRoll>();
        DiceRoll serverDice = null;

        foreach (var dice in serverDices)
        {
            if (dice.ownerSlotIndex == diceSlotIndex)
            {
                serverDice = dice;
                break;
            }
        }

        if (serverDice == null)
        {
            return;
        }

        if (cardIndex < 0 || cardIndex >= playerHand.Count) return;
        if (playerHand[cardIndex] != cardId) return;

        serverDice.SelectCard(cardId, cardIndex);
        serverDice.SelectTarget(enemyNetId, enemyDiceIndex);
    }

    [Command]
    public void CmdPlayCard(int cardId, int cardIndex, uint enemyNetId)
    {
        if (FightManager.Instance == null || !FightManager.Instance.IsFightActive)
        {
            return;
        }

        if (FightManager.Instance.CurrentState != FightState.Rolling)
        {
            return;
        }

        if (cardIndex < 0 || cardIndex >= playerHand.Count)
        {
            return;
        }

        if (playerHand[cardIndex] != cardId)
        {
            return;
        }

        if (dataGame == null) return;
        if (!dataGame.TryGetCardById(cardId, out CardData card)) return;
        if (currentLight < card.lightCost) return;

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
            return;
        }

        currentLight -= card.lightCost;
        playerHand.RemoveAt(cardIndex);
        SyncHandToOwner();

        QueueCardEffects(card, cardIndex, targetEnemy);
    }

    [Command]
    private void CmdSetPlayerReady()
    {
        if (isReady)
        {
            return;
        }

        if (FightManager.Instance != null && FightManager.Instance.IsFightActive)
        {
            if (!FightManager.Instance.CanPlayerReady())
            {
                return;
            }

            isReady = true;

            FightManager.Instance.PlayerReady(this);
        }
        else
        {
            SetReady(true);
        }
    }

    [Command]
    public void CmdSubmitMapVote(int nodeId)
    {
        if (RunFlowManager.Instance == null)
        {
            return;
        }

        RunFlowManager.Instance.SubmitVote(this, nodeId);
    }

    [Command]
    public void CmdCloseCurrentRunPopup()
    {
        if (RunFlowManager.Instance == null)
        {
            return;
        }

        RunFlowManager.Instance.CloseRoomAndReturnToMap();
    }

    [Command]
    public void CmdLeaveBattleToMap()
    {
        if (FightManager.Instance == null)
        {
            return;
        }

        FightManager.Instance.EndEncounterAndReturnToMap();
    }

    [Command]
    private void CmdRequestInitialHandSync()
    {
        SyncHandToOwner();
    }


    #endregion

}
