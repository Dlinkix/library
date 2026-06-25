using Mirror;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static DataGame;

public class NetworkGameEnemy : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnEnemyNameChanged))]
    public string EnemyName = "Enemy";

    [SyncVar(hook = nameof(OnHpChanged))]
    public int hp;

    [SyncVar(hook = nameof(OnHpChanged))]
    public int Maxhp = 100;

    [SyncVar(hook = nameof(OnHpChanged))]
    public int stagger;

    [SyncVar(hook = nameof(OnHpChanged))]
    public int Maxstagger = 16;

    [SyncVar(hook = nameof(OnSpawnIndexChanged))]
    private int spawnIndex = -1;

    [SyncVar(hook = nameof(OnDiceRollAmountChanged))]
    private int DiceRollAmount;

    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReady;

    [SyncVar]
    public int currentLight = 0;

    [SyncVar]
    public int maxLight = 0;

    [Header("UI")]
    [SerializeField] private Vector2 uiOffset = Vector2.zero;
    [SerializeField] private DataGame dataGame;
    [SerializeField] private int startingHandSize = 4;
    [SerializeField] private int cardsToDrawAfterReadyCycle = 1;

    [Header("UI Animation")]
    [SerializeField] private float pushDistance = 300f;


    [Header("Card View")]
    [SerializeField] private CardView cardViewPrefab;
    private CardView currentCardView;
    private bool isCardViewCreated = false;


    private Vector3 originalPosition;
    private readonly List<int> enemyDeck = new List<int>();
    public readonly List<int> enemyHand = new List<int>();
    private DataGame.EnemyData activeEnemyData;
    private int enemyDataIndex = -1;
    private GameObject uiObject;
    private bool uiCreated;
    private Slider hpSlider;
    private Slider staggerSlider;
    private Image imagechar;
    private TMP_Text hpText;
    private TMP_Text nametext;
    private TMP_Text staggerText;
    private TMP_Text rollText;
    private Button readyButton;
    private bool isShowingRollResult;
    private RectTransform uiRect;
    private bool isUIMoving = false;
    private bool isInCombat = false;
    private Vector3 attackerOriginalPos;
    private Vector3 enemyOriginalPos;
    public DataGame DataGame => dataGame;
    public static List<NetworkGameEnemy> AllEnemies { get; } = new List<NetworkGameEnemy>();
    public GameObject UIObject => uiObject;

    private Queue<System.Action> pendingActions = new Queue<System.Action>();
    private float actionTimer = 0f;
    private bool isExecutingActions = false;
    public bool IsExecutingActions => isExecutingActions;

    public override void OnStartServer()
    {
        if (!AllEnemies.Contains(this)) AllEnemies.Add(this);
    }

    [System.Serializable]
    public class AISelectionData
    {
        public int diceSlot;
        public int cardId;
        public int cardIndex;
        public uint targetNetId;
        public int targetDiceIndex;
    }

    public override void OnStartClient()
    {
        if (!AllEnemies.Contains(this))
            AllEnemies.Add(this);

        CreateUI();
        ApplyUIPositionBySpawnIndex();
    }

    [Server]
    public void InitializeEnemy(int dataIndex, int targetSpawnIndex)
    {
        enemyDataIndex = dataIndex;
        spawnIndex = targetSpawnIndex;
        ApplyEnemyStatsFromData();
        InitializeCardState();
        DrawCardFromDeck(startingHandSize);
    }

    [Server]
    public void PushEnemyUI(NetworkGamePlayer attacker)
    {
        RpcPushEnemyUI(attacker.netId);
    }

    [Server]
    public void ReturnToPosition()
    {
        RpcReturnToPosition();
    }

    [Server]
    public static void RunEnemyReadyCycle()
    {
        for (int i = 0; i < AllEnemies.Count; i++)
        {
            NetworkGameEnemy enemy = AllEnemies[i];
            if (enemy == null) continue;
            int roll = enemy.GetRollValue();
            enemy.RpcShowRollResult(roll, enemy.EnemyName);
            enemy.AddRandomCardToDeck();
            enemy.DrawCardFromDeck(enemy.cardsToDrawAfterReadyCycle);
        }
    }

    [ClientRpc]
    private void RpcPushEnemyUI(uint attackerNetId)
    {
        NetworkGamePlayer attacker = FindAttacker(attackerNetId);
        if (attacker == null || uiRect == null) return;

        attackerOriginalPos = attacker.UIObject.GetComponent<RectTransform>().position;
        enemyOriginalPos = uiRect.position;

        isInCombat = true;

        StartCoroutine(AnimatePush(attacker));
    }

    [ClientRpc]
    private void RpcReturnToPosition()
    {
        if (!isInCombat) return;
        StartCoroutine(AnimateReturn());
    }

    private NetworkGamePlayer FindAttacker(uint netId)
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
            if (player != null && player.netId == netId) return player;

        NetworkGamePlayer[] allPlayers = FindObjectsByType<NetworkGamePlayer>(FindObjectsSortMode.None);
        foreach (var player in allPlayers)
            if (player != null && player.netId == netId) return player;

        return null;
    }

    public void ResetUIPosition()
    {
        StopAllCoroutines();

        if (uiRect != null)
            uiRect.position = originalPosition;

        isInCombat = false;
        isUIMoving = false;
    }

    public void SetCombatPresentationActive(bool isVisible)
    {
        if (uiObject != null)
        {
            uiObject.SetActive(isVisible);
        }

        SetAllDiceUIVisible(isVisible);
    }

    public void SetAllDiceUIVisible(bool visible)
    {
        if (uiObject == null) return;

        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();
        foreach (var dice in dices)
        {
            if (dice != null)
            {
                dice.SetUIVisible(visible);
            }
        }
    }

    public void UpdateAllDiceRange()
    {
        if (uiObject == null) return;

        DataGame.EnemyData enemyData = GetActiveEnemyData();
        if (enemyData == null) return;

        int minSpeed = enemyData.baseSpeedMin;
        int maxSpeed = dataGame != null ? dataGame.GetEnemyBaseSpeedMax(enemyDataIndex) : 0;

        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();
        foreach (var dice in dices)
        {
            if (dice != null)
            {
                dice.ShowDiceRange(minSpeed, maxSpeed);
            }
        }
    }

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

    public void UpdateDiceValues(int[] values)
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

    private IEnumerator AnimatePush(NetworkGamePlayer attacker)
    {
        if (isUIMoving) yield break;
        isUIMoving = true;

        RectTransform attackerRect = attacker.UIObject.GetComponent<RectTransform>();
        if (attackerRect == null || uiRect == null) { isUIMoving = false; yield break; }

        Vector3 attackerPos = attackerRect.position;
        Vector3 enemyPos = uiRect.position;
        float dirX = enemyPos.x > attackerPos.x ? 1f : -1f;
        Vector3 direction = new Vector3(dirX, 0f, 0f);

        if (direction.magnitude < 0.1f) direction = Vector3.right;

        Vector3 approachTarget = attackerPos + direction * (Vector3.Distance(attackerPos, enemyPos) * 0.95f);
        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            float t = elapsed / 0.3f;
            attackerRect.position = Vector3.Lerp(attackerPos, approachTarget, t * t * (3f - 2f * t));
            elapsed += Time.deltaTime;
            yield return null;
        }
        attackerRect.position = approachTarget;

        Vector3 enemyPushPos = enemyPos + direction * pushDistance;

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

                enemyPushPos.x = Mathf.Clamp(enemyPushPos.x, minX + 50f, maxX - 50f);
                enemyPushPos.y = Mathf.Clamp(enemyPushPos.y, minY + 50f, maxY - 50f);
            }
        }

        elapsed = 0f;
        while (elapsed < 0.2f)
        {
            float t = elapsed / 0.2f;
            uiRect.position = Vector3.Lerp(enemyPos, enemyPushPos, t * t * (3f - 2f * t));
            elapsed += Time.deltaTime;
            yield return null;
        }
        uiRect.position = enemyPushPos;

        isUIMoving = false;
    }

    private IEnumerator AnimateReturn()
    {
        NetworkGamePlayer attacker = null;
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.isLocalPlayer) { attacker = player; break; }
        }
        if (attacker == null) yield break;

        RectTransform attackerRect = attacker.UIObject.GetComponent<RectTransform>();
        if (attackerRect == null || uiRect == null) yield break;

        Vector3 currentAttackerPos = attackerRect.position;
        Vector3 currentEnemyPos = uiRect.position;

        float elapsed = 0f;
        while (elapsed < 0.6f)
        {
            float t = elapsed / 0.6f;
            float smoothT = t * t * (3f - 2f * t);
            attackerRect.position = Vector3.Lerp(currentAttackerPos, attackerOriginalPos, smoothT);
            uiRect.position = Vector3.Lerp(currentEnemyPos, enemyOriginalPos, smoothT);
            elapsed += Time.deltaTime;
            yield return null;
        }

        attackerRect.position = attackerOriginalPos;
        uiRect.position = enemyOriginalPos;
        isInCombat = false;
    }

    [Server]
    private void InitializeCardState()
    {
        EnsureDataGameReference();
        activeEnemyData = GetActiveEnemyData();
        enemyDeck.Clear(); enemyHand.Clear();
        if (dataGame == null || activeEnemyData == null) return;
        List<int> configuredCardIds = dataGame.GetEnemyCardIds(enemyDataIndex);
        if (configuredCardIds.Count == 0) return;
        int deckLimit = Mathf.Max(0, activeEnemyData.dekaPlayer);
        int cardsToUse = deckLimit > 0 ? Mathf.Min(configuredCardIds.Count, deckLimit) : 0;
        for (int i = 0; i < cardsToUse; i++) enemyDeck.Add(configuredCardIds[i]);
    }

    [Server]
    private void AddRandomCardToDeck()
    {
        EnsureDataGameReference();
        if (dataGame == null) return;
        int cardId = dataGame.GetRandomAllCardId();
        if (cardId <= 0) return;
        enemyDeck.Add(cardId);
    }

    [Server]
    private void DrawCardFromDeck(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (enemyDeck.Count == 0) AddRandomCardToDeck();
            if (enemyDeck.Count == 0) break;
            int cardId = enemyDeck[0];
            enemyDeck.RemoveAt(0);
            enemyHand.Add(cardId);
        }
    }

    private void OnSpawnIndexChanged(int oldValue, int newValue) { ApplyUIPositionBySpawnIndex(); }
    private void OnDiceRollAmountChanged(int oldValue, int newValue) { }
    private void OnEnemyNameChanged(string oldValue, string newValue) {     UpdateStatusText(); }
    private void OnHpChanged(int oldValue, int newValue) {
        if (isServer && newValue <= 0)
        {
            DestroyEnemy();
        }
         UpdateHpView(); }

    private void CreateUI()
    {
        if (uiCreated) return;
        GameObject uiPrefab = Resources.Load<GameObject>("UI/PlayerUI");
        if (uiPrefab == null || spawnIndex < 0) return;

        EnemySpawnPoint[] spawnPoints = GetSceneSpawnPoints();
        EnemySpawnPoint targetPoint = null;
        for (int i = 0; i < spawnPoints.Length; i++)
            if (spawnPoints[i].SpawnIndex == spawnIndex) { targetPoint = spawnPoints[i]; break; }
        if (targetPoint == null) return;

        uiObject = Instantiate(uiPrefab, targetPoint.transform);
        uiRect = uiObject.GetComponent<RectTransform>();
        uiObject.transform.localPosition = Vector3.zero;
        uiObject.transform.localRotation = Quaternion.identity;
        uiObject.transform.localScale = Vector3.one;

        CreateDiceUI();

        Transform imageTransform = uiObject.transform.Find("DiceRoll");
        if (imageTransform != null) rollText = imageTransform.Find("Text (TMP)")?.GetComponent<TMP_Text>();

        UIAimLine aimLine = uiObject.GetComponent<UIAimLine>();
        if (aimLine != null) Destroy(aimLine);

        imagechar = uiObject.transform.Find("ImageChar")?.GetComponent<Image>();
        if (imagechar != null)
        {
            imagechar.transform.localRotation = Quaternion.Euler(0, 180, 0);
            DataGame.EnemyData enemyData = GetActiveEnemyData();
            if (enemyData != null && enemyData.EnemyImage != null)
            {
                imagechar.sprite = enemyData.EnemyImage;
            }
        }

        hpText = uiObject.transform.Find("HpText")?.GetComponent<TMP_Text>();
        staggerText = uiObject.transform.Find("StaggerText")?.GetComponent<TMP_Text>();
        hpSlider = uiObject.transform.Find("HpSlider")?.GetComponent<Slider>();
        staggerSlider = uiObject.transform.Find("StaggerSlider")?.GetComponent<Slider>();
        readyButton = uiObject.transform.Find("ReadyButton")?.GetComponent<Button>();
        nametext = uiObject.transform.Find("NameText")?.GetComponent<TMP_Text>();

        if (hpSlider != null) { hpSlider.minValue = 0f; hpSlider.maxValue = 100f; hpSlider.wholeNumbers = true; }
        if (staggerSlider != null) { staggerSlider.minValue = 0f; staggerSlider.maxValue = 100f; staggerSlider.wholeNumbers = true; }
        if (readyButton != null) { readyButton.gameObject.SetActive(false); readyButton.interactable = false; }
        CreateCardView();
        uiCreated = true;
        UpdateHpView();
        UpdateStatusText();
        ApplyUIPositionBySpawnIndex();
        originalPosition = uiRect.position;

        if (RunFlowManager.Instance != null)
        {
            RunFlowManager.Instance.RefreshClientVisuals();
        }
    }

    private void CreateDiceUI()
    {
        Transform gridTransform = uiObject.transform.Find("GridDice");
        if (gridTransform == null) return;
        foreach (Transform child in gridTransform) Destroy(child.gameObject);

        GameObject dicePrefab = Resources.Load<GameObject>("UI/DiceRoll");
        if (dicePrefab == null) return;

        for (int i = 0; i < DiceRollAmount; i++)
        {
            GameObject diceObj = Instantiate(dicePrefab, gridTransform);
            DiceRoll dice = diceObj.GetComponent<DiceRoll>();
            if (dice != null)
            {
                dice.SetOwner(this, i);
                UIAimLine aimLine = diceObj.GetComponent<UIAimLine>();
                if (aimLine != null) Destroy(aimLine);
            }
        }
    }

    private void OnReadyChanged(bool oldValue, bool newValue)
    {
    }

    private void ApplyUIPositionBySpawnIndex()
    {
        if (!uiCreated || uiObject == null || spawnIndex < 0) return;
        if (uiObject.transform.parent != null && uiObject.transform.parent.GetComponent<EnemySpawnPoint>() != null)
        { uiObject.transform.localPosition = new Vector3(uiOffset.x, uiOffset.y, 0f); return; }

        EnemySpawnPoint[] spawnPoints = GetSceneSpawnPoints();
        EnemySpawnPoint targetPoint = null;
        for (int i = 0; i < spawnPoints.Length; i++)
            if (spawnPoints[i].SpawnIndex == spawnIndex) { targetPoint = spawnPoints[i]; break; }
        if (targetPoint == null) return;

        uiObject.transform.position = targetPoint.transform.position + new Vector3(uiOffset.x, uiOffset.y, 0f);
    }

    private void UpdateHpView()
    {
        if (hpText == null || staggerText == null) return;
        hpText.text = hp.ToString(); staggerText.text = stagger.ToString();
        if (hpSlider != null && Maxhp > 0) hpSlider.value = (hp / (float)Maxhp) * hpSlider.maxValue;
        if (staggerSlider != null && Maxstagger > 0) staggerSlider.value = (stagger / (float)Maxstagger) * staggerSlider.maxValue;
    }

    private static EnemySpawnPoint[] GetSceneSpawnPoints()
    {
        return Resources.FindObjectsOfTypeAll<EnemySpawnPoint>()
            .Where(point => point != null && point.gameObject.scene.IsValid())
            .ToArray();
    }

    private void UpdateStatusText()
    {
        if (!uiCreated || rollText == null || isShowingRollResult) return;
        rollText.text = EnemyName;
    }

    [ClientRpc]
    public void RpcShowRollResult(int roll, string enemyName)
    {
        if (rollText == null) return;
        isShowingRollResult = true;
        rollText.text = $"{roll}";
        nametext.text = $"{enemyName}";
    }

    private Canvas FindStatusCanvas()
    {
        EnemySpawnPoint spawnPoint = FindFirstObjectByType<EnemySpawnPoint>();
        if (spawnPoint != null) return spawnPoint.GetComponentInParent<Canvas>();
        return FindFirstObjectByType<Canvas>();
    }

    private void EnsureDataGameReference()
    {
        if (dataGame != null) return;
        DataGame[] loadedData = Resources.FindObjectsOfTypeAll<DataGame>();
        if (loadedData != null && loadedData.Length > 0) dataGame = loadedData[0];
    }

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

    [Command(requiresAuthority = false)]
    public void CmdAISyncDiceSelection(int diceSlotIndex, int cardId, int cardIndex, uint targetNetId, int targetDiceIndex)
    {
        if (FightManager.Instance == null || uiObject == null) return;

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

        if (serverDice == null) return;

        int actualIndex = -1;
        for (int i = 0; i < enemyHand.Count; i++)
        {
            if (enemyHand[i] == cardId)
            {
                actualIndex = i;
                break;
            }
        }

        if (actualIndex == -1) return;

        serverDice.SelectTarget(targetNetId, targetDiceIndex);
        serverDice.SelectCard(cardId, actualIndex);
    }

    [Command(requiresAuthority = false)]
    public void CmdSetEnemyReady()
    {
        if (FightManager.Instance == null || !FightManager.Instance.IsFightActive)
            return;

        isReady = true;

        bool allPlayersReady = true;
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && !player.isReady)
            {
                allPlayersReady = false;
                break;
            }
        }

        bool allEnemiesReady = true;
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null && !enemy.isReady)
            {
                allEnemiesReady = false;
                break;
            }
        }

        if (allPlayersReady && allEnemiesReady)
        {
            FightManager.OnAllPlayersReady?.Invoke();
        }
    }

    [Server]
    public void ProcessAITurn()
    {
        if (uiObject == null) return;
        if (FightManager.Instance == null || FightManager.Instance.CurrentState != FightState.Rolling) return;

        DiceRoll[] dices = uiObject.GetComponentsInChildren<DiceRoll>();
        int currentLight = GetActiveEnemyData()?.baseStartLight ?? 3;

        NetworkGamePlayer targetPlayer = GetRandomPlayerTarget();
        if (targetPlayer == null) return;

        DiceRoll[] playerDices = targetPlayer.UIObject.GetComponentsInChildren<DiceRoll>();
        if (playerDices.Length == 0) return;

        List<int> availableHand = new List<int>(enemyHand);
        int syncedCount = 0;

        foreach (var dice in dices)
        {
            if (dice == null) continue;
            dice.ClearSelection();

            int selectedCardId = -1;
            int originalIndex = -1;

            for (int i = 0; i < availableHand.Count; i++)
            {
                int cardId = availableHand[i];
                DataGame.CardData card = GetCardData(cardId);

                if (card != null && currentLight >= card.lightCost)
                {
                    selectedCardId = cardId;
                    originalIndex = enemyHand.IndexOf(cardId);
                    break;
                }
            }

            if (selectedCardId != -1)
            {
                DataGame.CardData selectedCard = GetCardData(selectedCardId);
                if (selectedCard != null)
                {
                    currentLight -= selectedCard.lightCost;
                    int randomDiceIndex = Random.Range(0, playerDices.Length);

                    dice.SelectTarget(targetPlayer.netId, randomDiceIndex);
                    dice.SelectCard(selectedCardId, originalIndex);

                    availableHand.RemoveAt(availableHand.IndexOf(selectedCardId));
                }
            }
        }

        foreach (var dice in dices)
        {
            if (dice != null && dice.hasSelection)
            {
                syncedCount++;
                CmdAISyncDiceSelection(
                    dice.ownerSlotIndex,
                    dice.selectedCardId,
                    -1,
                    dice.selectedTargetEnemyNetId,
                    dice.selectedTargetDiceIndex
                );
            }
        }

        CmdSetEnemyReady();
    }

    [Server]
    private DataGame.CardData GetCardData(int cardId)
    {
        EnsureDataGameReference();
        if (dataGame == null) return null;
        dataGame.TryGetCardById(cardId, out DataGame.CardData card);
        return card;
    }

    [Server]
    public void QueueCardEffects(CardData card, int cardIndex, NetworkGamePlayer targetPlayer)
    {
        RpcShowCardView(card.cardId, cardIndex);

        List<int> rollValues = new List<int>();

        if (card.attacks != null && card.attacks.Length > 0)
        {
            foreach (var attack in card.attacks)
            {
                int roll = UnityEngine.Random.Range(attack.RollMin, attack.RollMax + 1);
                rollValues.Add(roll);
            }

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
                    targetPlayer.PushPlayerUI(this);
                    ApplyAttackToPlayer(attack, targetPlayer, roll);
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

        if (!isExecutingActions)
        {
            isExecutingActions = true;
            StartCoroutine(ProcessActionQueue());
        }
    }

    [Server]
    private IEnumerator ProcessActionQueue()
    {
        float totalDelay = 0.5f;

        while (pendingActions.Count > 0)
        {
            yield return new WaitForSeconds(totalDelay);

            var action = pendingActions.Dequeue();
            action?.Invoke();
        }

        isExecutingActions = false;
    }

    [Server]
    private void ApplyAttackToPlayer(AttackData attack, NetworkGamePlayer target, int roll)
    {
        if (target == null) return;

        switch (attack.type)
        {
            case AttackData.Type.Damage:
                int damage = roll;
                target.hp -= damage;
                if (target.hp < 0) target.hp = 0;
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

    [Command(requiresAuthority = false)]
    public void CmdSyncAllAIDiceSelections()
    {
        if (FightManager.Instance == null || uiObject == null) return;

        DiceRoll[] serverDices = uiObject.GetComponentsInChildren<DiceRoll>();
        int syncedCount = 0;
        foreach (var dice in serverDices)
        {
            if (dice != null && dice.hasSelection)
            {
                CmdAISyncDiceSelection(dice.ownerSlotIndex, dice.selectedCardId, -1, dice.selectedTargetEnemyNetId, dice.selectedTargetDiceIndex);
                syncedCount++;
            }
        }
    }

    [Server]
    private NetworkGamePlayer GetRandomPlayerTarget()
    {
        List<NetworkGamePlayer> alivePlayers = new List<NetworkGamePlayer>();
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.hp > 0)
            {
                alivePlayers.Add(player);
            }
        }

        if (alivePlayers.Count == 0) return null;
        return alivePlayers[Random.Range(0, alivePlayers.Count)];
    }

    [Client]
    private void CreateCardView()
    {
        if (cardViewPrefab == null)
        {
            cardViewPrefab = Resources.Load<CardView>("UI/CardView");
        }

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
                rect.anchoredPosition = new Vector2(0f, 90f);
                rect.sizeDelta = new Vector2(400f, 300f);
            }

            currentCardView.gameObject.SetActive(false);
            isCardViewCreated = true;
        }
    }

    [Client]
    public void ShowCardView(DataGame.CardData cardData, int cardIndex)
    {
        if (!isCardViewCreated) CreateCardView();
        if (currentCardView == null) return;
        currentCardView.SetupCard(cardData, cardIndex);
        currentCardView.ShowCardView();
    }

    [Client]
    public void HideCardView()
    {
        if (currentCardView != null) currentCardView.HideCardView();
    }

    [ClientRpc] public void RpcShowCardView(int cardId, int cardIndex) { if (GetCardData(cardId) is DataGame.CardData cardData) ShowCardView(cardData, cardIndex); }
    [ClientRpc] public void RpcHideCardView() => HideCardView();
    [ClientRpc] public void RpcMoveDiceToPlaceholder(int cardIndex, int attackIndex) { if (currentCardView != null && currentCardView.gameObject.activeSelf) currentCardView.MoveDiceToPlaceholder(cardIndex, attackIndex); }
    [ClientRpc] public void RpcReturnDiceToGrid(int cardIndex) { if (currentCardView != null && currentCardView.gameObject.activeSelf) currentCardView.ReturnDiceToGrid(cardIndex); }
    [ClientRpc] public void RpcUpdateAttackRolls(int cardIndex, int[] rollValues) { if (currentCardView != null && currentCardView.gameObject.activeSelf) currentCardView.UpdateAttackDiceValues(cardIndex, rollValues); }
    [ClientRpc] public void RpcDisableAttackDice(int cardId, int attackIndex) { if (currentCardView != null && currentCardView.gameObject.activeSelf) currentCardView.DisableAttackDice(attackIndex); }

    [Server]
    private void ApplyEnemyStatsFromData()
    {
        EnsureDataGameReference();
        activeEnemyData = GetActiveEnemyData();
        if (activeEnemyData == null) { hp = Maxhp; stagger = Maxstagger; return; }
        EnemyName = string.IsNullOrWhiteSpace(activeEnemyData.enemyName) ? $"Enemy {enemyDataIndex + 1}" : activeEnemyData.enemyName;
        Maxhp = activeEnemyData.maxHealth; Maxstagger = activeEnemyData.maxStagger;
        hp = Maxhp; stagger = Maxstagger;
        DiceRollAmount = activeEnemyData.diceRollEnemy;
        maxLight = activeEnemyData.baseStartLight;
        currentLight = maxLight;
    }
    [Server]
    public void DestroyEnemy()
    {
        if (hp <= 0)
        {
            RpcOnEnemyDestroyed();
            pendingActions.Clear();
            isExecutingActions = false;
            if (AllEnemies.Contains(this))
                AllEnemies.Remove(this);
            if (uiObject != null)
            {
                Destroy(uiObject);
                uiObject = null;
                uiCreated = false;
            }
        }
    }

    [ClientRpc]
    private void RpcOnEnemyDestroyed()
    {
        if (uiObject != null)
        {
            Destroy(uiObject);
            uiObject = null;
            uiCreated = false;
        }
    }

    private DataGame.EnemyData GetActiveEnemyData()
    {
        EnsureDataGameReference();
        if (dataGame == null) return null;
        return dataGame.GetEnemyData(enemyDataIndex);
    }

    public int GetRollValue()
    {
        DataGame.EnemyData enemyData = activeEnemyData ?? GetActiveEnemyData();
        if (enemyData == null) return 0;
        int minSpeed = enemyData.baseSpeedMin;
        int maxSpeed = dataGame != null ? dataGame.GetEnemyBaseSpeedMax(enemyDataIndex) : 0;
        if (minSpeed > maxSpeed) { int temp = minSpeed; minSpeed = maxSpeed; maxSpeed = temp; }
        return Random.Range(minSpeed, maxSpeed + 1);
    }

    public override void OnStopClient()
    {
        if (uiObject != null) Destroy(uiObject);
        uiCreated = false;
    }

    public override void OnStopServer()
    {
        AllEnemies.Remove(this);
    }
}