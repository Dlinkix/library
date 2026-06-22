using Mirror;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("UI")]
    [SerializeField] private Vector2 uiOffset = Vector2.zero;
    [SerializeField] private DataGame dataGame;
    [SerializeField] private int startingHandSize = 4;
    [SerializeField] private int cardsToDrawAfterReadyCycle = 1;

    [Header("UI Animation")]
    [SerializeField] private float pushDistance = 300f;
    [SerializeField] private float pushDuration = 0.3f;
    [SerializeField] private float returnDuration = 0.5f;
    [SerializeField] private AnimationCurve pushCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private readonly List<int> enemyDeck = new List<int>();
    private readonly List<int> enemyHand = new List<int>();
    private DataGame.EnemyData activeEnemyData;
    private int enemyDataIndex = -1;
    private bool isAimLineRemoved = false;
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

    public static List<NetworkGameEnemy> AllEnemies { get; } = new List<NetworkGameEnemy>();

    public override void OnStartServer()
    {
        if (!AllEnemies.Contains(this))
        {
            AllEnemies.Add(this);
        }
    }

    public override void OnStartClient()
    {
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
        if (isUIMoving) return;

        RectTransform attackerRect = attacker.UIObject.GetComponent<RectTransform>();
        if (attackerRect == null) return;

        RpcPushEnemyUI(attacker.netId);
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
        // Ищем атакующего игрока
        NetworkGamePlayer attacker = null;

        // Способ 1: через AllPlayers
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.netId == attackerNetId)
            {
                attacker = player;
                break;
            }
        }

        // Способ 2: через FindObjectsByType (запасной)
        if (attacker == null)
        {
            NetworkGamePlayer[] allPlayers = FindObjectsByType<NetworkGamePlayer>(FindObjectsSortMode.None);
            foreach (var player in allPlayers)
            {
                if (player != null && player.netId == attackerNetId)
                {
                    attacker = player;
                    break;
                }
            }
        }

        if (attacker == null)
        {
            Debug.LogWarning($"[RpcPushEnemyUI] Attacker with NetId {attackerNetId} not found!");
            return;
        }

        if (uiRect == null)
        {
            Debug.LogWarning($"[RpcPushEnemyUI] uiRect is null for {EnemyName}!");
            return;
        }

        Debug.Log($"[RpcPushEnemyUI] Starting push animation. Attacker: {attacker.PlayerName}, Enemy: {EnemyName}");
        StartCoroutine(AnimateUIPush(attacker));
    }

    private IEnumerator AnimateUIPush(NetworkGamePlayer attacker)
    {
        if (isUIMoving)
        {
            Debug.Log($"[AnimateUIPush] Already moving, skipping");
            yield break;
        }

        isUIMoving = true;

        RectTransform attackerRect = attacker.UIObject?.GetComponent<RectTransform>();
        if (attackerRect == null)
        {
            Debug.LogWarning($"[AnimateUIPush] attackerRect is null!");
            isUIMoving = false;
            yield break;
        }

        if (uiRect == null)
        {
            Debug.LogWarning($"[AnimateUIPush] uiRect is null!");
            isUIMoving = false;
            yield break;
        }

        Vector3 attackerStartPos = attackerRect.position;
        Vector3 enemyStartPos = uiRect.position;

        Debug.Log($"[AnimateUIPush] Attacker: {attackerStartPos}, Enemy: {enemyStartPos}");

        Vector3 direction = (enemyStartPos - attackerStartPos).normalized;
        if (direction.magnitude < 0.1f)
        {
            direction = Vector3.right;
        }

        // ===== 1. ПОДХОД =====
        Vector3 approachTarget = Vector3.Lerp(attackerStartPos, enemyStartPos, 0.95f);

        float elapsed = 0f;
        float approachDuration = 0.5f;
        while (elapsed < approachDuration)
        {
            float t = elapsed / approachDuration;
            float smoothT = t * t * (3f - 2f * t);
            Vector3 newPos = Vector3.Lerp(attackerStartPos, approachTarget, smoothT);
            attackerRect.position = newPos;
            elapsed += Time.deltaTime;
            yield return null;
        }
        attackerRect.position = approachTarget;

        // ===== 2. УДАР =====
        Vector3 enemyPushPos = enemyStartPos + direction * pushDistance;

        elapsed = 0f;
        float hitDuration = 0.2f;
        while (elapsed < hitDuration)
        {
            float t = elapsed / hitDuration;
            float smoothT = t * t * (3f - 2f * t);
            uiRect.position = Vector3.Lerp(enemyStartPos, enemyPushPos, smoothT);
            elapsed += Time.deltaTime;
            yield return null;
        }
        uiRect.position = enemyPushPos;

        // ===== 3. ВОЗВРАТ =====
        elapsed = 0f;
        float returnDuration = 0.5f;
        while (elapsed < returnDuration)
        {
            float t = elapsed / returnDuration;
            float smoothT = t * t * (3f - 2f * t);
            attackerRect.position = Vector3.Lerp(approachTarget, attackerStartPos, smoothT);
            elapsed += Time.deltaTime;
            yield return null;
        }
        attackerRect.position = attackerStartPos;

        elapsed = 0f;
        while (elapsed < returnDuration)
        {
            float t = elapsed / returnDuration;
            float smoothT = t * t * (3f - 2f * t);
            uiRect.position = Vector3.Lerp(enemyPushPos, enemyStartPos, smoothT);
            elapsed += Time.deltaTime;
            yield return null;
        }
        uiRect.position = enemyStartPos;

        isUIMoving = false;
        Debug.Log($"[AnimateUIPush] Animation complete for {EnemyName}");
    }

    [Server]
    private void InitializeCardState()
    {
        EnsureDataGameReference();
        activeEnemyData = GetActiveEnemyData();

        enemyDeck.Clear();
        enemyHand.Clear();

        if (dataGame == null)
        {
            Debug.LogWarning($"DataGame is missing on {name}. Enemy cards will not initialize.");
            return;
        }

        if (activeEnemyData == null)
        {
            Debug.LogWarning($"Enemy data is missing in DataGame for {name}. Enemy cards will not initialize.");
            return;
        }

        List<int> configuredCardIds = dataGame.GetEnemyCardIds(enemyDataIndex);
        if (configuredCardIds.Count == 0)
        {
            Debug.LogWarning($"Enemy data for {name} does not contain any valid cards. Enemy deck will stay empty.");
            return;
        }

        int deckLimit = Mathf.Max(0, activeEnemyData.dekaPlayer);
        int cardsToUse = deckLimit > 0 ? Mathf.Min(configuredCardIds.Count, deckLimit) : 0;
        for (int i = 0; i < cardsToUse; i++)
        {
            enemyDeck.Add(configuredCardIds[i]);
        }
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

    private void OnSpawnIndexChanged(int oldValue, int newValue)
    {
        ApplyUIPositionBySpawnIndex();
    }

    private void OnDiceRollAmountChanged(int oldValue, int newValue)
    {
        UpdateIconDiceRoll();
    }

    private void OnEnemyNameChanged(string oldValue, string newValue)
    {
        UpdateStatusText();
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
            Debug.LogWarning("PlayerUI prefab was not found in Resources/UI.");
            return;
        }

        if (spawnIndex < 0)
        {
            Debug.LogWarning($"Spawn index not set for {EnemyName}, waiting...");
            return;
        }

        EnemySpawnPoint[] spawnPoints = FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None);
        EnemySpawnPoint targetPoint = null;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i].SpawnIndex == spawnIndex)
            {
                targetPoint = spawnPoints[i];
                break;
            }
        }

        if (targetPoint == null)
        {
            Debug.LogWarning($"EnemySpawnPoint with index {spawnIndex} not found for {EnemyName}");
            return;
        }

        uiObject = Instantiate(uiPrefab, targetPoint.transform);
        uiRect = uiObject.GetComponent<RectTransform>();
        uiObject.transform.localPosition = Vector3.zero;
        uiObject.transform.localRotation = Quaternion.identity;
        uiObject.transform.localScale = Vector3.one;

        CreateDiceUI();

        Transform imageTransform = uiObject.transform.Find("DiceRoll");
        if (imageTransform != null)
        {
            rollText = imageTransform.Find("Text (TMP)")?.GetComponent<TMP_Text>();
        }

        UIAimLine aimLine = uiObject.GetComponent<UIAimLine>();
        if (aimLine != null)
        {
            Destroy(aimLine);
            Debug.Log("UIAimLine removed from enemy UI");
        }

        imagechar = uiObject.transform.Find("ImageChar")?.GetComponent<Image>();
        if (imagechar != null) imagechar.transform.localRotation = Quaternion.Euler(0, 180, 0);

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
            readyButton.gameObject.SetActive(false);
            readyButton.interactable = false;
        }

        uiCreated = true;
        UpdateHpView();
        UpdateStatusText();
        ApplyUIPositionBySpawnIndex();
    }

    private void CreateDiceUI()
    {
        Transform gridTransform = uiObject.transform.Find("GridDice");
        if (gridTransform == null)
        {
            Debug.LogWarning("GridDice not found in enemy UI!");
            return;
        }

        foreach (Transform child in gridTransform)
            Destroy(child.gameObject);

        GameObject dicePrefab = Resources.Load<GameObject>("UI/DiceRoll");
        if (dicePrefab == null)
        {
            Debug.LogError("DiceRoll prefab not found in Resources/UI/!");
            return;
        }

        int enemyDiceCount = DiceRollAmount;

        for (int i = 0; i < enemyDiceCount; i++)
        {
            GameObject diceObj = Instantiate(dicePrefab, gridTransform);
            DiceRoll dice = diceObj.GetComponent<DiceRoll>();
            if (dice != null)
            {
                dice.SetOwner(this, i);
            }
            else
            {
                Debug.LogError($"DiceRoll component not found on enemy prefab! Index: {i}");
            }
        }
    }

    private void ApplyUIPositionBySpawnIndex()
    {
        if (!uiCreated || uiObject == null || spawnIndex < 0) return;

        if (uiObject.transform.parent != null &&
            uiObject.transform.parent.GetComponent<EnemySpawnPoint>() != null)
        {
            uiObject.transform.localPosition = new Vector3(uiOffset.x, uiOffset.y, 0f);
            return;
        }

        EnemySpawnPoint[] spawnPoints = FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None);
        EnemySpawnPoint targetPoint = null;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i].SpawnIndex == spawnIndex)
            {
                targetPoint = spawnPoints[i];
                break;
            }
        }

        if (targetPoint == null) return;

        RectTransform anchorRect = targetPoint.GetComponent<RectTransform>();
        if (anchorRect == null) return;

        uiObject.transform.position = anchorRect.position + new Vector3(uiOffset.x, uiOffset.y, 0f);
    }

    private void UpdateIconDiceRoll() { }

    private void UpdateHpView()
    {
        if (hpText == null || staggerText == null) return;

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
        if (spawnPoint != null)
        {
            Canvas spawnCanvas = spawnPoint.GetComponentInParent<Canvas>();
            if (spawnCanvas != null) return spawnCanvas;
        }

        return FindFirstObjectByType<Canvas>();
    }

    private void EnsureDataGameReference()
    {
        if (dataGame != null) return;

        DataGame[] loadedData = Resources.FindObjectsOfTypeAll<DataGame>();
        if (loadedData != null && loadedData.Length > 0)
        {
            dataGame = loadedData[0];
        }
    }

    [Server]
    public void RollAllDice()
    {
        DiceRoll[] dices = GetComponentsInChildren<DiceRoll>();
        foreach (var dice in dices)
        {
            if (dice != null)
            {
                int roll = GetRollValue();
                dice.RollDice(roll, roll);
                Debug.Log($"[NetworkGameEnemy] {EnemyName} dice rolled: {roll}");
            }
        }
    }

    [Server]
    private void ApplyEnemyStatsFromData()
    {
        EnsureDataGameReference();
        activeEnemyData = GetActiveEnemyData();

        if (activeEnemyData == null)
        {
            Debug.LogWarning($"Enemy data is missing in DataGame for {name}. Using current stat defaults.");
            hp = Maxhp;
            stagger = Maxstagger;
            return;
        }

        EnemyName = string.IsNullOrWhiteSpace(activeEnemyData.enemyName) ? $"Enemy {enemyDataIndex + 1}" : activeEnemyData.enemyName;
        Maxhp = activeEnemyData.maxHealth;
        Maxstagger = activeEnemyData.maxStagger;
        hp = Maxhp;
        stagger = Maxstagger;
        DiceRollAmount = activeEnemyData.diceRollEnemy;
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
        if (minSpeed > maxSpeed)
        {
            int temp = minSpeed;
            minSpeed = maxSpeed;
            maxSpeed = temp;
        }

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