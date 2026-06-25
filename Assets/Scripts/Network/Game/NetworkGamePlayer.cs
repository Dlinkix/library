using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using System.Linq;
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
    private Slider hpSlider;
    private Slider staggerSlider;
    private TMP_Text hpText;
    private TMP_Text staggerText;
    private TMP_Text rollText;
    private TMP_Text nametext;
    private Button readyButton;
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

    #region Unity Callbacks
    public override void OnStartServer()
    {
        if (!AllPlayers.Contains(this))
            AllPlayers.Add(this);

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
                    Destroy(line.gameObject);
            }
        }
    }

    public override void OnStartLocalPlayer()
    {
        SetupUIForLocalOrRemotePlayer();
        UpdateReadyText();
        StartCoroutine(WaitForReadyAndInitialize());
        Invoke(nameof(UpdateAllDiceRange), 1f);

        if (DiceSelectionManager.Instance != null && DiceSelectionManager.Instance.GetSelectedPlayerDice() != null)
            UpdateHandVisibility();
    }

    public override void OnStopClient()
    {
        if (uiObject != null) Destroy(uiObject);
        if (localHandRoot != null) Destroy(localHandRoot.gameObject);
        uiCreated = false;
    }

    public override void OnStopServer()
    {
        AllPlayers.Remove(this);
    }
    #endregion

    #region Initialization
    private IEnumerator WaitForReadyAndInitialize()
    {
        float timeout = 5f;
        float timer = 0f;

        while (slotIndex < 0 && timer < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            timer += 0.1f;
        }

        if (slotIndex < 0) yield break;

        yield return null;
        EnsureLocalHandUI();
        yield return null;
        QueueLocalHandRefresh();
        CmdRequestInitialHandSync();
        Invoke(nameof(UpdateAllDiceRange), 0.5f);

        if (DiceSelectionManager.Instance != null && DiceSelectionManager.Instance.GetSelectedPlayerDice() != null)
            UpdateHandVisibility();
    }

    private IEnumerator RefreshHandAfterUIReady()
    {
        for (int i = 0; i < 30; i++)
        {
            EnsureLocalHandUI();

            if (localHandRoot != null && localHandContentRoot != null)
            {
                RefreshLocalHandUI();
                if (DiceSelectionManager.Instance != null && DiceSelectionManager.Instance.GetSelectedPlayerDice() != null)
                    UpdateHandVisibility();
                yield break;
            }
            yield return null;
        }
    }
    #endregion

    #region UI Creation
    private void CreateUI()
    {
        if (uiCreated) return;

        GameObject uiPrefab = Resources.Load<GameObject>("UI/PlayerUI");
        if (uiPrefab == null || slotIndex < 0) return;

        PlayerUIAnchor[] anchors = GetScenePlayerAnchors();
        PlayerUIAnchor targetAnchor = null;
        for (int i = 0; i < anchors.Length; i++)
        {
            if (anchors[i].SlotIndex == slotIndex) { targetAnchor = anchors[i]; break; }
        }
        if (targetAnchor == null) return;

        uiObject = Instantiate(uiPrefab, targetAnchor.transform);
        uiRect = uiObject.GetComponent<RectTransform>();
        if (uiRect == null) { Destroy(uiObject); return; }

        uiRect.localPosition = new Vector3(uiOffset.x, uiOffset.y, 0f);
        uiRect.localRotation = Quaternion.identity;
        uiRect.localScale = Vector3.one;

        if (DiceRollAmount > 0) CreateDiceUI();

        Transform gridTransform = uiObject.transform.Find("GridDice");
        Transform imageDice = gridTransform?.Find("DiceRoll");
        rollText = imageDice?.Find("Text (TMP)")?.GetComponent<TMP_Text>();

        hpText = uiObject.transform.Find("HpText")?.GetComponent<TMP_Text>();
        staggerText = uiObject.transform.Find("StaggerText")?.GetComponent<TMP_Text>();
        hpSlider = uiObject.transform.Find("HpSlider")?.GetComponent<Slider>();
        staggerSlider = uiObject.transform.Find("StaggerSlider")?.GetComponent<Slider>();
        readyButton = uiObject.transform.Find("ReadyButton")?.GetComponent<Button>();
        nametext = uiObject.transform.Find("NameText")?.GetComponent<TMP_Text>();

        if (hpSlider != null) { hpSlider.minValue = 0f; hpSlider.maxValue = 100f; hpSlider.wholeNumbers = true; }
        if (staggerSlider != null) { staggerSlider.minValue = 0f; staggerSlider.maxValue = 100f; staggerSlider.wholeNumbers = true; }

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnReadyButtonClick);
        }

        if (isLocalPlayer) CreateCardView();

        uiCreated = true;

        if (RunFlowManager.Instance != null) RunFlowManager.Instance.RefreshBattleRoot();

        SetupUIForLocalOrRemotePlayer();
        UpdateHpView();
        UpdateReadyText();
        ApplyUIPositionBySlot();
        originalUIPosition = uiRect.position;

        if (RunFlowManager.Instance != null) RunFlowManager.Instance.RefreshClientVisuals();
    }

    private void CreateDiceUI()
    {
        Transform gridTransform = uiObject.transform.Find("GridDice");
        if (gridTransform == null) return;

        foreach (Transform child in gridTransform)
            Destroy(child.gameObject);

        GameObject dicePrefab = Resources.Load<GameObject>("UI/DiceRoll");
        if (dicePrefab == null) return;

        for (int i = 0; i < DiceRollAmount; i++)
        {
            GameObject diceObj = Instantiate(dicePrefab, gridTransform);
            DiceRoll dice = diceObj.GetComponent<DiceRoll>();
            dice.SetOwner(this, i);

            UIAimLine aimLine = diceObj.GetComponent<UIAimLine>();
            if (aimLine != null)
            {
                if (isLocalPlayer) { aimLine.SetOwnerDice(dice); dice.SetAimLine(aimLine); }
                else Destroy(aimLine);
            }
        }
    }

    private void SetupUIForLocalOrRemotePlayer()
    {
        if (readyButton == null) return;
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

        if (!uiCreated || uiObject == null || slotIndex < 0) return;

        PlayerUIAnchor[] anchors = GetScenePlayerAnchors();
        PlayerUIAnchor targetAnchor = null;
        for (int i = 0; i < anchors.Length; i++)
        {
            if (anchors[i].SlotIndex == slotIndex) { targetAnchor = anchors[i]; break; }
        }
        if (targetAnchor == null) return;

        RectTransform anchorRect = targetAnchor.GetComponent<RectTransform>();
        if (anchorRect == null) return;

        uiObject.transform.position = anchorRect.position + new Vector3(uiOffset.x, uiOffset.y, 0f);
    }

    private static PlayerUIAnchor[] GetScenePlayerAnchors()
    {
        return Resources.FindObjectsOfTypeAll<PlayerUIAnchor>()
            .Where(anchor => anchor != null && anchor.gameObject.scene.IsValid())
            .ToArray();
    }
    #endregion

    #region Hand UI
    public void EnsureLocalHandUI()
    {
        if (!isLocalPlayer || localHandRoot != null) return;

        Canvas canvas = FindStatusCanvas();
        if (canvas == null) return;

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

        localHandRoot.gameObject.SetActive(false);
        RefreshLocalHandUI();
    }

    public void RefreshLocalHandUI()
    {
        if (!isLocalPlayer) return;
        EnsureLocalHandUI();
        EnsureDataGameReference();
        if (localHandContentRoot == null) return;

        for (int i = localHandContentRoot.childCount - 1; i >= 0; i--)
            Destroy(localHandContentRoot.GetChild(i).gameObject);

        for (int i = 0; i < localHandCardIds.Count; i++)
        {
            DataGame.CardData cardData = dataGame != null ? dataGame.GetCardById(localHandCardIds[i]) : null;
            CreateHandCardView(cardData, localHandCardIds[i], i, localHandCardIds.Count);
        }

        if (localHandTitle != null) localHandTitle.text = $"Hand ({localHandCardIds.Count})";
    }

    private void CreateHandCardView(DataGame.CardData cardData, int cardId, int cardIndex, int totalCards)
    {
        GameObject cardPrefab = Resources.Load<GameObject>("UI/Card");
        if (cardPrefab == null) return;

        GameObject cardObj = Instantiate(cardPrefab, localHandContentRoot);
        RectTransform cardRect = cardObj.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0f, 0f);
        cardRect.anchorMax = new Vector2(0f, 0f);
        cardRect.pivot = new Vector2(0f, 0f);
        cardRect.sizeDelta = new Vector2(150f, 180f);
        cardRect.anchoredPosition = GetHandCardPosition(cardIndex, totalCards);

        LocalHandCardView cardView = cardObj.GetComponent<LocalHandCardView>();
        if (cardView != null)
        {
            Image cardBackground = cardObj.GetComponent<Image>();
            TMP_Text descText = cardObj.transform.Find("Desc")?.GetComponent<TMP_Text>();
            cardView.Setup(cardRect, null, descText, cardBackground, cardId, cardIndex, this);
        }

        Transform topCard = cardObj.transform.Find("TopCard");
        Transform imageName = topCard.Find("ImageName");
        TMP_Text nameText = imageName.Find("Name")?.GetComponent<TMP_Text>();
        if (nameText != null) nameText.text = cardData != null ? cardData.cardName : $"Card {cardId}";

        Transform imageCost = topCard.Find("imageCost");
        TMP_Text costText = imageCost.Find("Cost")?.GetComponent<TMP_Text>();
        if (costText != null) costText.text = cardData != null ? $"Light {cardData.lightCost}" : "Light ?";

        Transform descTransform = cardObj.transform.Find("GameObject");
        TMP_Text descText2 = descTransform.Find("Desc")?.GetComponent<TMP_Text>();
        if (descText2 != null) descText2.text = cardData != null ? cardData.GetShortDescription() : "No data";

        Image artImage = cardObj.transform.Find("ImageCard")?.GetComponent<Image>();
        if (artImage != null && cardData != null) artImage.sprite = cardData.cardSprite;

        Transform simpleGrid = cardObj.transform.Find("SimpeGrid");
        Transform fullGrid = descTransform.Find("Grid");

        if (cardData != null && cardData.attacks != null && cardData.attacks.Length > 0)
        {
            GameObject simplePrefab = Resources.Load<GameObject>("UI/ImageDiceAttackSimple");
            GameObject fullPrefab = Resources.Load<GameObject>("UI/ImageDiceAttackFull");

            if (fullGrid != null && fullPrefab != null)
            {
                foreach (var attack in cardData.attacks)
                {
                    GameObject fullObj = Instantiate(fullPrefab, fullGrid);
                    TMP_Text damageText = fullObj.transform.Find("DamageText")?.GetComponent<TMP_Text>();
                    if (damageText != null) damageText.text = $"{attack.RollMin}-{attack.RollMax}";
                    TMP_Text effectsText = fullObj.transform.Find("TextCardEffects")?.GetComponent<TMP_Text>();
                    if (effectsText != null) effectsText.text = GetAttackEffectDescription(attack);
                }
            }

            if (simpleGrid != null && simplePrefab != null)
            {
                for (int i = 0; i < cardData.attacks.Length; i++)
                    Instantiate(simplePrefab, simpleGrid);
            }
        }
    }

    private string GetAttackEffectDescription(DataGame.AttackData attack)
    {
        switch (attack.type)
        {
            case DataGame.AttackData.Type.Damage: return "Damage";
            case DataGame.AttackData.Type.Block: return $"Block {attack.staggerDamage}";
            case DataGame.AttackData.Type.Escape: return "Escape";
            default: return "";
        }
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

    public void UpdateHandVisibility()
    {
        if (!isLocalPlayer) return;
        EnsureLocalHandUI();
        if (localHandRoot == null) return;

        bool hasSelectedDice = DiceSelectionManager.Instance != null && DiceSelectionManager.Instance.GetSelectedPlayerDice() != null;
        localHandRoot.gameObject.SetActive(hasSelectedDice);
        if (hasSelectedDice && localHandCardIds.Count > 0) RefreshLocalHandUI();
    }

    public void SetHandBackgroundVisible(bool visible)
    {
        if (localHandRoot != null)
        {
            Image bg = localHandRoot.GetComponent<Image>();
            if (bg != null) bg.enabled = visible;
        }
    }

    public void SetHandCounterVisible(bool visible)
    {
        if (localHandTitle != null) localHandTitle.gameObject.SetActive(visible);
    }

    public bool IsHandVisible() => localHandRoot != null && localHandRoot.gameObject.activeSelf;

    public bool IsCardInLocalHand(int cardId, int cardIndex)
    {
        if (cardIndex < 0 || cardIndex >= localHandCardIds.Count) return false;
        return localHandCardIds[cardIndex] == cardId;
    }

    private void QueueLocalHandRefresh()
    {
        if (!isLocalPlayer) return;
        if (delayedHandRefreshCoroutine != null) StopCoroutine(delayedHandRefreshCoroutine);
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
            if (anchorCanvas != null) return anchorCanvas;
        }
        return FindFirstObjectByType<Canvas>();
    }

    private void EnsureDataGameReference()
    {
        if (dataGame != null) return;
        DataGame[] loadedData = Resources.FindObjectsOfTypeAll<DataGame>();
        if (loadedData != null && loadedData.Length > 0) dataGame = loadedData[0];
    }
    #endregion

    #region UI Updates
    private void UpdateHpView()
    {
        if (hpText == null || staggerText == null) return;
        hpText.text = hp.ToString();
        staggerText.text = stagger.ToString();
        if (hpSlider != null && Maxhp > 0) hpSlider.value = (hp / (float)Maxhp) * hpSlider.maxValue;
        if (staggerSlider != null && Maxstagger > 0) staggerSlider.value = (stagger / (float)Maxstagger) * staggerSlider.maxValue;
    }

    private void UpdateReadyText()
    {
        if (!uiCreated || rollText == null) return;
        if (isLocalPlayer)
            nametext.text = isReady ? "Waiting for other players..." : "Press SPACE to ready";
        else
            nametext.text = isReady ? $"{PlayerName}: ready" : $"{PlayerName}: not ready";
    }

    private void UpdateIconDiceRoll() { }
    private void UpdateIconLight() { }

    private void OnReadyButtonClick()
    {
        if (isLocalPlayer && !isReady) CmdSetPlayerReady();
    }

    private void ClearRollText()
    {
        isShowingRollResult = false;
        UpdateReadyText();
    }

    private void Update()
    {
        if (isLocalPlayer && Input.GetKeyDown(KeyCode.Space) && !isReady) CmdSetPlayerReady();
        if (isServer) ProcessActionQueue();
    }
    #endregion

    #region SyncVar Hooks
    private void OnSlotIndexChanged(int oldValue, int newValue)
    {
        if (!uiCreated) CreateUI();
        ApplyUIPositionBySlot();
        if (uiCreated) UpdateAllDiceRange();

        if (isLocalPlayer)
        {
            EnsureLocalHandUI();
            if (DiceSelectionManager.Instance != null && DiceSelectionManager.Instance.GetSelectedPlayerDice() != null)
                UpdateHandVisibility();
            else if (localHandRoot != null)
                localHandRoot.gameObject.SetActive(false);
        }
    }

    private void OnDiceRollAmountChanged(int oldValue, int newValue) { if (uiObject != null) CreateDiceUI(); }
    private void OnLightAmountChanged(int oldValue, int newValue) { UpdateIconLight(); }
    private void OnPlayerNameChanged(string oldName, string newName) { UpdateReadyText(); }
    private void OnReadyChanged(bool oldValue, bool newValue) { if (!isShowingRollResult) UpdateReadyText(); }
    private void OnHpChanged(int oldValue, int newValue) { UpdateHpView(); }
    #endregion

    #region Combat
    public void ResetUIPosition() { if (uiRect != null) uiRect.localPosition = Vector3.zero; }
    private NetworkGameEnemy FindAttackerEnemy(uint netId)
    {
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
            if (enemy != null && enemy.netId == netId) return enemy;
        return null;
    }

    public void SetCombatPresentationActive(bool isVisible)
    {
        if (uiObject != null) uiObject.SetActive(isVisible);
        if (localHandRoot != null)
        {
            bool shouldShowHand = isVisible && DiceSelectionManager.Instance != null && DiceSelectionManager.Instance.GetSelectedPlayerDice() != null;
            localHandRoot.gameObject.SetActive(shouldShowHand);
        }
        if (!isVisible) HideCardView();
    }

    private IEnumerator AnimatePlayerPush(NetworkGameEnemy attacker)
    {
        if (isUIMoving) yield break;
        isUIMoving = true;

        RectTransform attackerRect = attacker.UIObject.GetComponent<RectTransform>();
        if (attackerRect == null || uiRect == null) { isUIMoving = false; yield break; }

        Vector3 attackerStartPos = attackerRect.position;
        Vector3 playerStartPos = uiRect.position;
        float dirX = playerStartPos.x > attackerStartPos.x ? 1f : -1f;
        Vector3 direction = new Vector3(dirX, 0f, 0f);
        if (direction.magnitude < 0.1f) direction = Vector3.right;

        Vector3 approachTarget = attackerStartPos + direction * (Vector3.Distance(attackerStartPos, playerStartPos) * 0.95f);
        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            float t = elapsed / 0.3f;
            attackerRect.position = Vector3.Lerp(attackerStartPos, approachTarget, t * t * (3f - 2f * t));
            elapsed += Time.deltaTime;
            yield return null;
        }
        attackerRect.position = approachTarget;

        Vector3 pushTarget = playerStartPos + direction * pushDistance;

        GameObject[] passObjects = GameObject.FindGameObjectsWithTag("Pass");
        foreach (GameObject pass in passObjects)
        {
            RectTransform passRect = pass.GetComponent<RectTransform>();
            if (passRect != null)
            {
                Vector3[] corners = new Vector3[4];
                passRect.GetWorldCorners(corners);
                pushTarget.x = Mathf.Clamp(pushTarget.x, corners[0].x + 50f, corners[2].x - 50f);
                pushTarget.y = Mathf.Clamp(pushTarget.y, corners[0].y + 50f, corners[2].y - 50f);
            }
        }

        elapsed = 0f;
        while (elapsed < 0.2f)
        {
            float t = elapsed / 0.2f;
            uiRect.position = Vector3.Lerp(playerStartPos, pushTarget, t * t * (3f - 2f * t));
            elapsed += Time.deltaTime;
            yield return null;
        }
        uiRect.position = pushTarget;

        isUIMoving = false;
        _attackerOriginalPos = approachTarget;
        _playerOriginalPos = pushTarget;
    }
    #endregion

    #region Card View
    private void CreateCardView()
    {
        if (cardViewPrefab == null)
            cardViewPrefab = Resources.Load<CardView>("UI/CardView");
        if (cardViewPrefab == null || uiObject == null) return;

        GameObject cardViewObj = Instantiate(cardViewPrefab.gameObject, uiObject.transform);
        currentCardView = cardViewObj.GetComponent<CardView>();
        if (currentCardView != null)
        {
            RectTransform rect = cardViewObj.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(50f, 90f);
                rect.sizeDelta = new Vector2(400f, 300f);
            }
            currentCardView.gameObject.SetActive(false);
            isCardViewCreated = true;
        }
    }

    public void ShowCardView(DataGame.CardData cardData, int cardIndex)
    {
        if (!isCardViewCreated) CreateCardView();
        if (currentCardView == null) return;
        currentCardView.SetupCard(cardData, cardIndex);
        currentCardView.ShowCardView();
    }

    public void HideCardView()
    {
        if (currentCardView != null) currentCardView.HideCardView();
    }

    public DataGame.CardData GetCardData(int cardId)
    {
        if (dataGame == null) return null;
        dataGame.TryGetCardById(cardId, out CardData card);
        return card;
    }

    public void UpdateAllDiceRange()
    {
        if (uiObject == null) return;
        DataGame.PlayerData playerData = GetActivePlayerData();
        if (playerData == null) return;

        int minSpeed = playerData.baseSpeedMin;
        int maxSpeed = playerData.baseSpeedMax;
        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();
        foreach (var dice in dices) dice?.ShowDiceRange(minSpeed, maxSpeed);
    }

    public void UpdateAllDiceResult()
    {
        if (uiObject == null) return;
        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();
        foreach (var dice in dices) if (dice != null && dice.isReady) dice.ShowDiceResult(dice.diceValue);
    }
    #endregion

    #region Server
    [Server] private IEnumerator ServerCreateDiceDelayed() { yield return null; yield return null; }

    [Server] public void PushPlayerUI(NetworkGameEnemy attacker) => RpcPushPlayerUI(attacker.netId);

    [Server]
    public void RollAllDice()
    {
        if (uiObject == null) return;
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
        if (dataGame == null || targetEnemy == null) return;
        if (!dataGame.TryGetCardById(cardId, out CardData card)) return;
        if (cardIndex < 0 || cardIndex >= playerHand.Count || playerHand[cardIndex] != cardId) return;

        currentLight -= card.lightCost;
        playerHand.RemoveAt(cardIndex);
        SyncHandToOwner();
        QueueCardEffects(card, cardIndex, targetEnemy);
    }

    [Server]
    private void ProcessActionQueue()
    {
        if (!isExecutingActions || pendingActions.Count == 0) { isExecutingActions = false; return; }
        actionTimer += Time.deltaTime;
        float totalDelay = attackDelay + 0.5f;
        if (actionTimer >= totalDelay)
        {
            actionTimer = 0f;
            Action action = pendingActions.Dequeue();
            action?.Invoke();
            if (pendingActions.Count == 0) isExecutingActions = false;
        }
    }

    [Server]
    public void QueueCardEffects(DataGame.CardData card, int cardIndex, NetworkGameEnemy targetEnemy)
    {
        RpcShowCardView(card.cardId, cardIndex);
        List<int> rollValues = new List<int>();

        if (card.attacks != null)
        {
            foreach (var attack in card.attacks)
                rollValues.Add(UnityEngine.Random.Range(attack.RollMin, attack.RollMax + 1));

            RpcUpdateAttackRolls(cardIndex, rollValues.ToArray());

            int attackIndex = 0;
            foreach (var attack in card.attacks)
            {
                int roll = rollValues[attackIndex];
                int currentIndex = attackIndex;

                pendingActions.Enqueue(() => RpcMoveDiceToPlaceholder(cardIndex, currentIndex));
                pendingActions.Enqueue(() =>
                {
                    targetEnemy.PushEnemyUI(this);
                    ApplyAttack(attack, targetEnemy, roll);
                });
                pendingActions.Enqueue(() => RpcReturnDiceToGrid(cardIndex));
                attackIndex++;
            }
        }

        if (card.passiveActions != null)
            foreach (var passive in card.passiveActions)
                pendingActions.Enqueue(() => ApplyPassiveEffect(passive));

        if (!isExecutingActions) { isExecutingActions = true; actionTimer = 0f; }
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
        }
        if (attack.staggerDamage > 0) target.stagger += attack.staggerDamage;
    }

    [Server]
    private void ApplyPassiveEffect(DataGame.PassiveAction passive)
    {
        if (passive == null) return;
        switch (passive.effectType)
        {
            case DataGame.PassiveEffectType.DrawCard: DrawCardFromDeck(passive.value); break;
            case DataGame.PassiveEffectType.GainLight: currentLight += passive.value; break;
            case DataGame.PassiveEffectType.HealPlayer: hp += passive.value; if (hp > Maxhp) hp = Maxhp; break;
        }
    }

    [Server] public void PrepareStartingHand() { ApplyPlayerStatsFromData(); InitializeCardState(); DrawCardFromDeck(startingHandSize); isReady = false; }

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

    [Server] public void ServerSetSlotIndex(int index) => slotIndex = index;

    [Server]
    public void InitializeCardState()
    {
        EnsureDataGameReference();
        activePlayerData = GetActivePlayerData();
        playerPool.Clear(); playerDeck.Clear(); playerHand.Clear();
        if (dataGame == null || activePlayerData == null) { SyncHandToOwner(); return; }

        List<int> configuredCardIds = dataGame.GetPlayerCardIds();
        if (configuredCardIds.Count == 0) { SyncHandToOwner(); return; }

        int deckLimit = Mathf.Max(0, activePlayerData.dekaPlayer);
        int cardsToUse = deckLimit > 0 ? Mathf.Min(configuredCardIds.Count, deckLimit) : 0;
        for (int i = 0; i < cardsToUse; i++) { playerPool.Add(configuredCardIds[i]); playerDeck.Add(configuredCardIds[i]); }
        SyncHandToOwner();
    }

    [Server]
    public void AddRandomCardFromPoolToDeck()
    {
        EnsureDataGameReference();
        if (dataGame == null) return;
        int cardId = dataGame.GetRandomAllCardId();
        if (cardId > 0) playerDeck.Add(cardId);
    }

    [Server] public void SyncHandToOwner() { if (connectionToClient != null) TargetSyncHand(connectionToClient, playerHand.ToArray()); }

    [Server]
    public void DrawCardFromDeck(int count = 1)
    {
        int maxCardsInHand = GetMaxCardsInHand();
        for (int i = 0; i < count; i++)
        {
            if (playerHand.Count >= maxCardsInHand) break;
            if (playerDeck.Count == 0) AddRandomCardFromPoolToDeck();
            if (playerDeck.Count == 0) break;
            playerHand.Add(playerDeck[0]);
            playerDeck.RemoveAt(0);
        }
        SyncHandToOwner();
    }

    [Server] public void SetReady(bool ready) { isReady = ready; CheckAllPlayersReady(); }

    [Server]
    public static void CheckAllPlayersReady()
    {
        if (AllPlayers.Count == 0) return;
        foreach (var p in AllPlayers) if (!p.isReady) return;

        foreach (var p in AllPlayers) p.RpcShowRollResult(p.GetRollValue(), p.PlayerName);
        NetworkGameEnemy.RunEnemyReadyCycle();
        foreach (var p in AllPlayers) { p.AddRandomCardFromPoolToDeck(); p.DrawCardFromDeck(p.cardsToDrawAfterReadyCycle); p.isReady = false; }
    }
    #endregion

    #region Client
    [ClientRpc] private void RpcPushPlayerUI(uint attackerNetId) { if (FindAttackerEnemy(attackerNetId) is NetworkGameEnemy attacker && uiRect != null) StartCoroutine(AnimatePlayerPush(attacker)); }

    [ClientRpc] public void RpcShowCardView(int cardId, int cardIndex) { if (GetCardData(cardId) is DataGame.CardData cardData) ShowCardView(cardData, cardIndex); }

    [ClientRpc] public void RpcDisableAttackDice(int cardId, int attackIndex) { if (currentCardView != null && currentCardView.gameObject.activeSelf) currentCardView.DisableAttackDice(attackIndex); }

    [ClientRpc] public void RpcUpdateAttackRolls(int cardIndex, int[] rollValues) { if (currentCardView != null && currentCardView.gameObject.activeSelf) currentCardView.UpdateAttackDiceValues(cardIndex, rollValues); }

    [ClientRpc] public void RpcMoveDiceToPlaceholder(int cardIndex, int attackIndex) { if (currentCardView != null && currentCardView.gameObject.activeSelf) currentCardView.MoveDiceToPlaceholder(cardIndex, attackIndex); }

    [ClientRpc] public void RpcReturnDiceToGrid(int cardIndex) { if (currentCardView != null && currentCardView.gameObject.activeSelf) currentCardView.ReturnDiceToGrid(cardIndex); }

    [ClientRpc]
    public void RpcUpdateDiceValues(int[] values)
    {
        if (values == null || values.Length == 0 || uiObject == null) return;
        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();
        for (int i = 0; i < dices.Length && i < values.Length; i++) dices[i]?.SetDiceValue(values[i]);
    }

    [TargetRpc] public void TargetUpdateDiceValues(NetworkConnection target, int[] values) { foreach (var d in GetComponentsInChildren<DiceRoll>()) if (d != null) d.SetDiceValue(values[0]); }

    [TargetRpc]
    private void TargetSyncHand(NetworkConnection target, int[] handCardIds)
    {
        localHandCardIds.Clear();
        if (handCardIds != null) localHandCardIds.AddRange(handCardIds);
        StartCoroutine(RefreshHandAfterUIReady());
    }

    [ClientRpc]
    public void RpcShowRollResult(int roll, string playerName)
    {
        if (rollText == null) return;
        isShowingRollResult = true;
        rollText.text = $"{roll}";
        nametext.text = playerName;
    }
    #endregion

    #region Commands
    [Command]
    public void CmdSyncDiceSelection(int diceSlotIndex, int cardId, int cardIndex, uint enemyNetId, int enemyDiceIndex)
    {
        if (FightManager.Instance == null || FightManager.Instance.CurrentState != FightState.Rolling) return;
        if (cardIndex < 0 || cardIndex >= playerHand.Count || playerHand[cardIndex] != cardId) return;

        DiceRoll[] serverDices = uiObject.GetComponentsInChildren<DiceRoll>();
        foreach (var dice in serverDices)
        {
            if (dice.ownerSlotIndex == diceSlotIndex)
            {
                dice.SelectCard(cardId, cardIndex);
                dice.SelectTarget(enemyNetId, enemyDiceIndex);
                return;
            }
        }
    }

    [Command] public void CmdPlayCard(int cardId, int cardIndex, uint enemyNetId) { /* Логика если нужна */ }

    [Command]
    private void CmdSetPlayerReady()
    {
        if (isReady) return;
        if (FightManager.Instance != null && FightManager.Instance.IsFightActive)
        {
            if (!FightManager.Instance.CanPlayerReady()) return;
            isReady = true;
            FightManager.Instance.PlayerReady(this);
        }
        else SetReady(true);
    }

    [Command] public void CmdSubmitMapVote(int nodeId) => RunFlowManager.Instance?.SubmitVote(this, nodeId);
    [Command] public void CmdCloseCurrentRunPopup() => RunFlowManager.Instance?.CloseRoomAndReturnToMap();
    [Command] public void CmdLeaveBattleToMap() => FightManager.Instance?.EndEncounterAndReturnToMap();
    [Command] private void CmdRequestInitialHandSync() => SyncHandToOwner();
    #endregion

    #region Helpers
    private DataGame.PlayerData GetActivePlayerData() { EnsureDataGameReference(); return dataGame?.GetPlayerData(); }
    public int GetCardsToDrawAfterReadyCycle() => cardsToDrawAfterReadyCycle;
    public int GetRollValue()
    {
        DataGame.PlayerData playerData = activePlayerData ?? GetActivePlayerData();
        if (playerData == null) return 0;
        return UnityEngine.Random.Range(playerData.baseSpeedMin, playerData.baseSpeedMax + 1);
    }

    private int GetMaxCardsInHand() => Mathf.Max(0, (activePlayerData ?? GetActivePlayerData())?.maxCardOnHand ?? int.MaxValue);

    [Server]
    private void ApplyPlayerStatsFromData()
    {
        EnsureDataGameReference();
        activePlayerData = GetActivePlayerData();
        if (activePlayerData == null) { hp = Maxhp; stagger = Maxstagger; return; }
        Maxhp = activePlayerData.maxHealth; Maxstagger = activePlayerData.maxStagger;
        hp = Maxhp; stagger = Maxstagger;
        DiceRollAmount = activePlayerData.diceRollPlayer;
        maxLight = activePlayerData.baseStartLight;
        currentLight = maxLight;
    }
    #endregion
}