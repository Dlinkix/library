using System.Collections.Generic;
using Mirror;
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

    [Header("UI")]
    [SerializeField] private Vector2 uiOffset = Vector2.zero;
    [SerializeField] private DataGame dataGame;
    [SerializeField] private int startingHandSize = 4;
    [SerializeField] private int cardsToDrawAfterReadyCycle = 1;

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
    public static void RunEnemyReadyCycle()
    {
        for (int i = 0; i < AllEnemies.Count; i++)
        {
            NetworkGameEnemy enemy = AllEnemies[i];
            if (enemy == null)
            {
                continue;
            }

            int roll = enemy.GetRollValue();
            enemy.RpcShowRollResult(roll, enemy.EnemyName);
            enemy.AddRandomCardToDeck();
            enemy.DrawCardFromDeck(enemy.cardsToDrawAfterReadyCycle);
        }
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
        if (configuredCardIds.Count > deckLimit && deckLimit > 0)
        {
            Debug.LogWarning($"Enemy data for {name} contains more cards ({configuredCardIds.Count}) than dekaPlayer ({deckLimit}). Extra cards will be ignored.");
        }

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

        if (dataGame == null)
        {
            return;
        }

        int cardId = dataGame.GetRandomAllCardId();
        if (cardId <= 0)
        {
            Debug.LogWarning($"AllCards does not contain a valid random enemy card for {name}.");
            return;
        }

        enemyDeck.Add(cardId);
    }

    [Server]
    private void DrawCardFromDeck(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (enemyDeck.Count == 0)
            {
                AddRandomCardToDeck();
            }

            if (enemyDeck.Count == 0)
            {
                break;
            }

            int cardId = enemyDeck[0];
            enemyDeck.RemoveAt(0);
            enemyHand.Add(cardId);
        }
    }

    private void OnSpawnIndexChanged(int oldValue, int newValue)
    {
        ApplyUIPositionBySpawnIndex();
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
            Debug.LogWarning("No canvas found for enemy status UI.");
            return;
        }

        

        uiObject = Instantiate(uiPrefab, canvas.transform, false);
        Transform imageTransform = uiObject.transform.Find("Image");
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
        imagechar.transform.localRotation = Quaternion.Euler(0, 180, 0);
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

    private void ApplyUIPositionBySpawnIndex()
    {
        if (!uiCreated || uiObject == null || spawnIndex < 0)
        {
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
            return;
        }

        RectTransform anchorRect = targetPoint.GetComponent<RectTransform>();
        if (anchorRect == null)
        {
            return;
        }

        uiObject.transform.position = anchorRect.position + new Vector3(uiOffset.x, uiOffset.y, 0f);
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

    private void UpdateStatusText()
    {
        if (!uiCreated || rollText == null || isShowingRollResult)
        {
            return;
        }

        rollText.text = EnemyName;
    }

    [ClientRpc]
    private void RpcShowRollResult(int roll, string enemyName)
    {
        if (rollText == null)
        {
            return;
        }

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
            if (spawnCanvas != null)
            {
                return spawnCanvas;
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
    }

    private DataGame.EnemyData GetActiveEnemyData()
    {
        EnsureDataGameReference();

        if (dataGame == null)
        {
            return null;
        }

        return dataGame.GetEnemyData(enemyDataIndex);
    }

    private int GetRollValue()
    {
        DataGame.EnemyData enemyData = activeEnemyData ?? GetActiveEnemyData();
        if (enemyData == null)
        {
            return 0;
        }

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
        if (uiObject != null)
        {
            Destroy(uiObject);
        }

        uiCreated = false;
    }

    public override void OnStopServer()
    {
        AllEnemies.Remove(this);
    }
}
