using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.LowLevel;
using static DataGame;

public enum FightState
{
    Waiting,
    Rolling,
    Action,
    EndTurn
}

public class FightManager : NetworkBehaviour
{
    // ===== Ñèíãëòîí =====
    public static FightManager Instance { get; private set; }

    // ===== Ñîáûòèÿ =====
    public static UnityAction<FightState> OnFightStateChanged;
    public static UnityAction OnFightStarted;
    public static UnityAction OnFightEnded;
    public static UnityAction OnAllPlayersReady;

    // ===== Ñèíõðîíèçèðóåìûå ïåðåìåííûå =====
    [SyncVar(hook = nameof(OnStateChanged))]
    private FightState currentState = FightState.Waiting;

    [SyncVar]
    private int turnNumber = 0;


    [SyncVar] // <-- ÄÎÁÀÂÜ ÝÒÎ
    private bool isFightActive = false;


    // ===== Íàñòðîéêè =====
    [Header("Settings")]
    [SerializeField] private float actionDuration = 2f;
    [SerializeField] private float endTurnDuration = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool debugMode = true;

    [Header("Audio")]
    [SerializeField] private AudioClip rollingSound;
    [SerializeField] private float soundVolume = 0.1f;

    private AudioSource audioSource;

    private int readyPlayersCount = 0;
    private HashSet<NetworkGamePlayer> readyPlayers = new HashSet<NetworkGamePlayer>();
    private bool isWaitingForReady = false;

    // ===== Ñâîéñòâà =====
    public FightState CurrentState => currentState;
    public int TurnNumber => turnNumber;
    public bool IsFightActive => isFightActive;

    #region Unity Lifecycle

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (GetComponent<RunFlowManager>() == null)
        {
            gameObject.AddComponent<RunFlowManager>();
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = soundVolume;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public class TurnOrderEntry
    {
        public DiceRoll dice;
        public NetworkGamePlayer player;  
        public NetworkGameEnemy enemy;   
        public int speedValue;
        public int diceIndex;
    }

    #endregion

    #region Server Methods


    [Server]
    private List<TurnOrderEntry> GetTurnOrder()
    {
        List<TurnOrderEntry> turnOrder = new List<TurnOrderEntry>();

        // Добавляем игроков
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player == null || player.UIObject == null) continue;

            DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
            foreach (var dice in dices)
            {
                if (dice != null && dice.hasSelection)
                {
                    turnOrder.Add(new TurnOrderEntry
                    {
                        dice = dice,
                        player = player,
                        enemy = null,
                        speedValue = dice.diceValue,
                        diceIndex = dice.ownerSlotIndex
                    });
                }
            }
        }

        // Добавляем врагов
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy == null || enemy.UIObject == null) continue;

            DiceRoll[] dices = enemy.UIObject.GetComponentsInChildren<DiceRoll>();
            foreach (var dice in dices)
            {
                if (dice != null && dice.hasSelection)
                {
                    turnOrder.Add(new TurnOrderEntry
                    {
                        dice = dice,
                        player = null,
                        enemy = enemy,
                        speedValue = dice.diceValue,
                        diceIndex = dice.ownerSlotIndex
                    });
                }
            }
        }

        // Сортируем по скорости
        turnOrder.Sort((a, b) => {
            int speedCompare = b.speedValue.CompareTo(a.speedValue);
            if (speedCompare != 0) return speedCompare;
            return Random.Range(-1, 2);
        });

        return turnOrder;
    }


    [Server]
    private void ClearAllDiceSelections()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.UIObject != null)
            {
                DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var dice in dices)
                {
                    if (dice != null)
                    {
                        dice.ClearSelection();
                        Debug.Log($"[ClearAllDiceSelections] Cleared dice {dice.ownerSlotIndex}");
                    }
                }
            }
        }

        // ===== ÂÛÇÛÂÀÅÌ ÎÄÈÍ ÐÀÇ ÏÎÑËÅ ÖÈÊËÀ! =====
        RpcHideCardView();
    }

    [Server]
    private System.Collections.IEnumerator ExecuteActionPhase()
    {
        Debug.Log("[FightManager] Executing Action phase...");

        List<TurnOrderEntry> turnOrder = GetTurnOrder();

        Debug.Log($"[ExecuteActionPhase] Turn order: {turnOrder.Count} entries");
        foreach (var entry in turnOrder)
        {
            string ownerName = entry.player != null ? entry.player.PlayerName : (entry.enemy != null ? entry.enemy.EnemyName : "Unknown");
            Debug.Log($"[ExecuteActionPhase] Dice {entry.diceIndex} ({ownerName}) speed: {entry.speedValue}, cardId: {entry.dice.selectedCardId}, target: {entry.dice.selectedTargetEnemyNetId}");
        }

        foreach (var entry in turnOrder)
        {
            if (entry.dice != null && entry.dice.hasSelection)
            {
                // ===== ПЕРЕДАЕМ source =====
                object source = entry.player != null ? (object)entry.player : entry.enemy;
                ApplyCardFromDice(source, entry.dice);
                entry.dice.ClearSelection();

                // ===== ЖДЕМ ЗАВЕРШЕНИЯ ДЕЙСТВИЙ =====
                if (entry.player != null)
                {
                    while (entry.player.IsExecutingActions)
                    {
                        yield return new WaitForSeconds(0.1f);
                    }
                }
                else if (entry.enemy != null)
                {
                    // Ждем пока враг завершит все действия (как у игрока)
                    float enemyTimeout = 5f;
                    float enemyTimer = 0f;
                    while (entry.enemy.IsExecutingActions && enemyTimer < enemyTimeout)
                    {
                        yield return new WaitForSeconds(0.1f);
                        enemyTimer += 0.1f;
                    }

                    if (enemyTimer >= enemyTimeout)
                    {
                        Debug.LogWarning($"[ExecuteActionPhase] Timeout waiting for enemy {entry.enemy.EnemyName} actions!");
                    }

                    // Дополнительная пауза между врагами
                    yield return new WaitForSeconds(0.3f);
                }
            }
        }

        // ===== ÆÄÅÌ ÇÀÂÅÐØÅÍÈß ÂÑÅÕ ÀÒÀÊ =====
        float timeout = 10f; // Ìàêñèìàëüíîå âðåìÿ îæèäàíèÿ
        float timer = 0f;
        bool allActionsCompleted = false;

        while (!allActionsCompleted && timer < timeout)
        {
            allActionsCompleted = true;
            foreach (var player in NetworkGamePlayer.AllPlayers)
            {
                if (player != null && player.IsExecutingActions)
                {
                    allActionsCompleted = false;
                    Debug.Log($"[ExecuteActionPhase] Waiting for {player.PlayerName} to finish actions...");
                    break;
                }
            }

            if (!allActionsCompleted)
            {
                yield return new WaitForSeconds(0.1f);
                timer += 0.1f;
            }
        }

        if (timer >= timeout)
        {
            Debug.LogWarning("[ExecuteActionPhase] Timeout waiting for actions to complete!");
        }

        // ===== Î×ÈÙÀÅÌ ÏÎÑËÅ ÂÑÅÕ ÀÒÀÊ =====
        Debug.Log("[ExecuteActionPhase] All actions completed, cleaning up...");
        ClearAllDiceSelections();
        RpcClearAllAimLines();

        // Íåáîëüøàÿ ïàóçà ïåðåä EndTurn
        yield return new WaitForSeconds(actionDuration);

        ChangeState(FightState.EndTurn);
        StartCoroutine(ExecuteEndTurnPhase());
    }


    [Server]
    private void ApplyCardFromDice(object source, DiceRoll dice)
    {
        if (dice == null || !dice.hasSelection)
        {
            Debug.Log($"[ApplyCardFromDice] Skip: dice={dice != null}, hasSelection={dice?.hasSelection}");
            return;
        }

        Debug.Log($"[ApplyCardFromDice] Processing dice {dice.ownerSlotIndex}: cardId={dice.selectedCardId}, cardIndex={dice.selectedCardIndex}, target={dice.selectedTargetEnemyNetId}, isEnemyDice={dice.isEnemyDice}");

        // ===== ОПРЕДЕЛЯЕМ ВЛАДЕЛЬЦА КУБИКА =====
        NetworkGamePlayer playerOwner = null;
        NetworkGameEnemy enemyOwner = null;

        if (dice.isEnemyDice)
        {
            // Кубик принадлежит врагу
            foreach (var enemy in NetworkGameEnemy.AllEnemies)
            {
                if (enemy == null || enemy.UIObject == null) continue;

                DiceRoll[] enemyDices = enemy.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var d in enemyDices)
                {
                    if (d == dice)
                    {
                        enemyOwner = enemy;
                        break;
                    }
                }
                if (enemyOwner != null) break;
            }

            if (enemyOwner == null)
            {
                Debug.LogWarning($"[ApplyCardFromDice] Enemy owner not found for dice {dice.ownerSlotIndex}");
                dice.ClearSelection();
                return;
            }
        }
        else
        {
            // Кубик принадлежит игроку
            foreach (var player in NetworkGamePlayer.AllPlayers)
            {
                if (player == null || player.UIObject == null) continue;

                DiceRoll[] playerDices = player.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var d in playerDices)
                {
                    if (d == dice)
                    {
                        playerOwner = player;
                        break;
                    }
                }
                if (playerOwner != null) break;
            }

            if (playerOwner == null)
            {
                Debug.LogWarning($"[ApplyCardFromDice] Player owner not found for dice {dice.ownerSlotIndex}");
                dice.ClearSelection();
                return;
            }
        }

        // ===== ОБРАБОТКА ДЛЯ ИГРОКА =====
        if (playerOwner != null)
        {
            ApplyCardFromPlayer(playerOwner, dice);
        }
        // ===== ОБРАБОТКА ДЛЯ ВРАГА =====
        else if (enemyOwner != null)
        {
            ApplyCardFromEnemy(enemyOwner, dice);
        }
        else
        {
            Debug.LogWarning($"[ApplyCardFromDice] Unknown owner for dice {dice.ownerSlotIndex}");
            dice.ClearSelection();
        }
    }

    [Server]
    private void ApplyCardFromPlayer(NetworkGamePlayer player, DiceRoll dice)
    {
        Debug.Log($"[ApplyCardFromPlayer] START: dice={dice.ownerSlotIndex}, cardId={dice.selectedCardId}, cardIndex={dice.selectedCardIndex}, handSize={player.PlayerHand.Count}");
        Debug.Log($"[ApplyCardFromPlayer] Hand: {string.Join(", ", player.PlayerHand)}");
        if (dice.selectedCardIndex < 0 || dice.selectedCardIndex >= player.PlayerHand.Count)
        {
            Debug.Log($"[ApplyCardFromPlayer] Invalid card index {dice.selectedCardIndex}! Hand size: {player.PlayerHand.Count}");
            dice.ClearSelection();
            return;
        }

        // Проверяем, что по индексу лежит та же карта
        if (player.PlayerHand[dice.selectedCardIndex] != dice.selectedCardId)
        {
            Debug.Log($"[ApplyCardFromPlayer] Card at index {dice.selectedCardIndex} is {player.PlayerHand[dice.selectedCardIndex]}, expected {dice.selectedCardId}!");
            dice.ClearSelection();
            return;
        }

        // Находим врага по цели
        NetworkGameEnemy targetEnemy = GetTargetEnemy(dice.selectedTargetEnemyNetId);
        if (targetEnemy == null)
        {
            Debug.LogWarning($"[ApplyCardFromPlayer] Target enemy not found for dice {dice.ownerSlotIndex}");
            dice.ClearSelection();
            return;
        }

        // Получаем карту
        if (!player.DataGame.TryGetCardById(dice.selectedCardId, out CardData card))
        {
            Debug.LogWarning($"[ApplyCardFromPlayer] Card {dice.selectedCardId} not found");
            dice.ClearSelection();
            return;
        }

        // Проверяем Light
        if (player.currentLight < card.lightCost)
        {
            Debug.Log($"[ApplyCardFromPlayer] Not enough Light! Need {card.lightCost}, have {player.currentLight}");
            dice.ClearSelection();
            return;
        }

        // Повторная проверка перед удалением
        if (dice.selectedCardIndex < 0 || dice.selectedCardIndex >= player.PlayerHand.Count)
        {
            Debug.Log($"[ApplyCardFromPlayer] Card index {dice.selectedCardIndex} became invalid before removal!");
            dice.ClearSelection();
            return;
        }

        if (player.PlayerHand[dice.selectedCardIndex] != dice.selectedCardId)
        {
            Debug.Log($"[ApplyCardFromPlayer] Card at index {dice.selectedCardIndex} changed before removal!");
            dice.ClearSelection();
            return;
        }

        // Тратим Light
        player.currentLight -= card.lightCost;

        // Удаляем карту по индексу
        int indexToRemove = dice.selectedCardIndex;
        player.PlayerHand.RemoveAt(indexToRemove);
        player.SyncHandToOwner();

        // ===== ОБНОВЛЯЕМ ИНДЕКСЫ У ВСЕХ =====
        UpdateDiceCardIndices(player, indexToRemove);

        // ===== ТАКЖЕ ОБНОВЛЯЕМ ИНДЕКСЫ У ВРАГОВ =====
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null)
            {
                UpdateDiceCardIndices(enemy, indexToRemove);
            }
        }

        // Применяем эффекты
        player.QueueCardEffects(card, indexToRemove, targetEnemy);
        dice.ClearSelection();
    }

        [Server]
    private void ApplyCardFromEnemy(NetworkGameEnemy enemy, DiceRoll dice)
    {
        // Проверяем индекс карты в руке врага
        if (dice.selectedCardIndex < 0 || dice.selectedCardIndex >= enemy.enemyHand.Count)
        {
            Debug.Log($"[ApplyCardFromEnemy] Invalid card index {dice.selectedCardIndex}! Hand size: {enemy.enemyHand.Count}");
            dice.ClearSelection();
            return;
        }

        // Проверяем, что по индексу лежит та же карта
        if (enemy.enemyHand[dice.selectedCardIndex] != dice.selectedCardId)
        {
            Debug.Log($"[ApplyCardFromEnemy] Card at index {dice.selectedCardIndex} is {enemy.enemyHand[dice.selectedCardIndex]}, expected {dice.selectedCardId}!");
            dice.ClearSelection();
            return;
        }

        // Для врага цель - игрок
        NetworkGamePlayer targetPlayer = GetTargetPlayer(dice.selectedTargetEnemyNetId);
        if (targetPlayer == null)
        {
            Debug.LogWarning($"[ApplyCardFromEnemy] Target player not found for dice {dice.ownerSlotIndex}");
            dice.ClearSelection();
            return;
        }

        // Получаем карту
        if (!enemy.DataGame.TryGetCardById(dice.selectedCardId, out CardData card))
        {
            Debug.LogWarning($"[ApplyCardFromEnemy] Card {dice.selectedCardId} not found");
            dice.ClearSelection();
            return;
        }

        // Проверяем Light у врага (если у врага есть свет)
        int enemyLight = enemy.currentLight;
        if (enemyLight < card.lightCost)
        {
            Debug.Log($"[ApplyCardFromEnemy] Not enough Light! Need {card.lightCost}, have {enemyLight}");
            dice.ClearSelection();
            return;
        }

        // Повторная проверка перед удалением
        if (dice.selectedCardIndex < 0 || dice.selectedCardIndex >= enemy.enemyHand.Count)
        {
            Debug.Log($"[ApplyCardFromEnemy] Card index {dice.selectedCardIndex} became invalid before removal!");
            dice.ClearSelection();
            return;
        }

        if (enemy.enemyHand[dice.selectedCardIndex] != dice.selectedCardId)
        {
            Debug.Log($"[ApplyCardFromEnemy] Card at index {dice.selectedCardIndex} changed before removal!");
            dice.ClearSelection();
            return;
        }

        // Тратим Light врага
        enemy.currentLight -= card.lightCost;

        // Удаляем карту из руки врага
        int indexToRemove = dice.selectedCardIndex;
        enemy.enemyHand.RemoveAt(indexToRemove);

        // ===== ОБНОВЛЯЕМ ИНДЕКСЫ У ВСЕХ ВРАГОВ =====
        foreach (var e in NetworkGameEnemy.AllEnemies)
        {
            if (e != null)
            {
                UpdateDiceCardIndices(e, indexToRemove);
            }
        }

        // Применяем эффекты
        enemy.QueueCardEffects(card, indexToRemove, targetPlayer);
        dice.ClearSelection();
    }

    [Server]
    private NetworkGameEnemy GetTargetEnemy(uint targetNetId)
    {
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null && enemy.netId == targetNetId)
            {
                return enemy;
            }
        }
        return null;
    }

    [Server]
    private NetworkGamePlayer GetTargetPlayer(uint targetNetId)
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.netId == targetNetId)
            {
                return player;
            }
        }
        return null;
    }
    [Server]
    private void UpdateDiceCardIndices(object owner, int removedIndex)
    {
        if (owner is NetworkGamePlayer player)
        {
            if (player == null || player.UIObject == null) return;
            DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
            foreach (var d in dices)
            {
                if (d != null && d.selectedCardIndex > removedIndex)
                {
                    d.selectedCardIndex--;
                    Debug.Log($"[UpdateDiceCardIndices] Updated player dice {d.ownerSlotIndex} index to {d.selectedCardIndex}");
                }
            }
        }
        else if (owner is NetworkGameEnemy enemy)
        {
            if (enemy == null || enemy.UIObject == null) return;
            DiceRoll[] dices = enemy.UIObject.GetComponentsInChildren<DiceRoll>();
            foreach (var d in dices)
            {
                if (d != null && d.selectedCardIndex > removedIndex)
                {
                    d.selectedCardIndex--;
                    Debug.Log($"[UpdateDiceCardIndices] Updated enemy dice {d.ownerSlotIndex} index to {d.selectedCardIndex}");
                }
            }
        }
    }
    [Server]
    public void BeginEncounter(MapRoomType roomType)
    {
        ResetEncounterState();

        NetworkManagerLobby lobby = NetworkManager.singleton as NetworkManagerLobby;
        lobby?.StartBattleEncounter(roomType);

        StartFight();
    }

    [Server]
    public void ResetEncounterState()
    {
        StopAllCoroutines();
        OnAllPlayersReady -= HandleAllPlayersReady;
        readyPlayers.Clear();
        readyPlayersCount = 0;
        isWaitingForReady = false;
        isFightActive = false;
        turnNumber = 0;
        currentState = FightState.Waiting;

        NetworkManagerLobby lobby = NetworkManager.singleton as NetworkManagerLobby;
        lobby?.ResetBattleEncounter();

        ResetAllPlayersReady();
        RpcClearAllAimLines();
        RpcClearAllSelections();
        RpcResetAllUIPositions();
        RpcUpdateDiceUI(FightState.Waiting);
    }

    [Server]
    public void EndEncounterAndReturnToMap()
    {
        StopFight();
        ResetEncounterState();
        RunFlowManager.Instance?.ReturnToMapFromBattle();
    }

    [Server]
    public void StartFight()
    {
        if (isFightActive) return;

        Debug.Log("[FightManager] Fight started!");
        isFightActive = true;
        turnNumber = 0;
        currentState = FightState.Waiting;

        OnFightStarted?.Invoke();

        ClearAllDiceSelections();
        RpcClearAllAimLines();
        RpcClearAllSelections();
        RpcResetAllUIPositions();
        RpcUpdateDiceUI(FightState.Waiting);
        StartWaitingForPlayers();
    }

    [Server]
    public void StopFight()
    {
        if (!isFightActive) return;

        Debug.Log("[FightManager] Fight stopped!");
        isFightActive = false;
        isWaitingForReady = false;
        readyPlayers.Clear();
        readyPlayersCount = 0;

        OnFightEnded?.Invoke();
    }

    [Server]
    private void ChangeState(FightState newState)
    {
        if (currentState == newState) return;

        FightState oldState = currentState;
        currentState = newState;

        if (newState == FightState.Rolling)
        {
            RpcPlayRollingSound();
        }

        if (newState == FightState.Waiting)
        {
            ClearAllDiceSelections();
            RpcClearAllAimLines();
            RpcClearAllSelections();
            RpcResetAllUIPositions();
        }

        // ===== ÓÁÐÀÒÜ ÝÒÈ ÂÛÇÎÂÛ =====
        // if (newState == FightState.Waiting) RpcSetAllDiceImagesVisible(true);
        // if (newState == FightState.Action) RpcSetAllDiceImagesVisible(false);

        // ===== ÂÑ¨ ÓÏÐÀÂËÅÍÈÅ Â RpcUpdateDiceUI =====
        RpcUpdateDiceUI(newState);
    }

    // ===== Ðîëë êóáèêîâ =====

    //[Server]
    //public void CheckEnemyReady(NetworkGameEnemy enemy)
    //{
    //    if (!isFightActive) return;
    //    if (enemy == null) return;
    //    if (!isWaitingForReady) return;

    //    // Проверяем всех врагов
    //    bool allEnemiesReady = true;
    //    foreach (var e in NetworkGameEnemy.AllEnemies)
    //    {
    //        if (e != null && !e.isReady)
    //        {
    //            allEnemiesReady = false;
    //            break;
    //        }
    //    }

    //    if (allEnemiesReady && readyPlayersCount >= NetworkGamePlayer.AllPlayers.Count)
    //    {
    //        Debug.Log("[FightManager] All enemies are ready!");
    //        OnAllPlayersReady?.Invoke();
    //    }
    //}


    [Server]
    private void RollAllDice()
    {
        Debug.Log("[FightManager] Rolling dice for all players and enemies...");

        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null)
            {
                // ===== ÎÁÍÎÂËßÅÌ ÊÓÁÈÊÈ =====
                player.RollAllDice();

                // ===== ÏÎÊÀÇÛÂÀÅÌ Â UI =====
                int roll = player.GetRollValue();
                player.RpcShowRollResult(roll, player.PlayerName);

                Debug.Log($"[FightManager] Player {player.PlayerName} rolled: {roll}");
            }
        }

        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null)
            {
                enemy.RollAllDice();
                int roll = enemy.GetRollValue();
                enemy.RpcShowRollResult(roll, enemy.EnemyName);
                Debug.Log($"[FightManager] Enemy {enemy.EnemyName} rolled: {roll}");
            }
        }
    }

    // ===== Âûòÿãèâàíèå êàðò =====

    [Server]
    private void DrawCardsForAllPlayers()
    {
        Debug.Log("[FightManager] Drawing cards for all players after EndTurn...");

        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null)
            {
                player.DrawCardFromDeck(player.GetCardsToDrawAfterReadyCycle());
                Debug.Log($"[FightManager] Player {player.PlayerName} drew cards");
            }
        }
    }

    // ===== Óïðàâëåíèå ãîòîâíîñòüþ =====

    [Server]
    public void PlayerReady(NetworkGamePlayer player)
    {
        if (!isFightActive) return;
        if (player == null) return;
        if (!isWaitingForReady) return;
        if (readyPlayers.Contains(player)) return;

        if (currentState != FightState.Waiting && currentState != FightState.Rolling)
        {
            Debug.Log($"[FightManager] Player {player.PlayerName} tried to ready but state is {currentState}");
            return;
        }

        readyPlayers.Add(player);
        readyPlayersCount++;
        player.isReady = true;

        Debug.Log($"[FightManager] Player {player.PlayerName} is ready! ({readyPlayersCount}/{NetworkGamePlayer.AllPlayers.Count})");

        int totalPlayers = NetworkGamePlayer.AllPlayers.Count;
        if (readyPlayersCount >= totalPlayers && totalPlayers > 0)
        {
            Debug.Log("[FightManager] All players are ready!");
            OnAllPlayersReady?.Invoke();
        }
    }

    [Server]
    private void StartWaitingForPlayers()
    {
        ResetAllPlayersReady();
        isWaitingForReady = true;
        readyPlayers.Clear();
        readyPlayersCount = 0;

        // ===== СБРАСЫВАЕМ ГОТОВНОСТЬ ВРАГОВ =====
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null)
            {
                enemy.isReady = false;
            }
        }

        OnAllPlayersReady += HandleAllPlayersReady;

        RpcClearAllAimLines();
        RpcClearAllSelections();

        Debug.Log($"[FightManager] Waiting for {NetworkGamePlayer.AllPlayers.Count} players...");
    }


    [Server]
    private void HandleAllPlayersReady()
    {
        OnAllPlayersReady -= HandleAllPlayersReady;
        isWaitingForReady = false;

        switch (currentState)
        {
            case FightState.Waiting:
                ChangeState(FightState.Rolling);
                RollAllDice();
                StartWaitingForPlayers();
                break;

            case FightState.Rolling:
                // ===== СНАЧАЛА ЗАПУСКАЕМ AI И СИНХРОНИЗИРУЕМ =====
                foreach (var enemy in NetworkGameEnemy.AllEnemies)
                {
                    if (enemy != null)
                    {
                        enemy.ProcessAITurn(); 
                    }
                }

                // ===== ЖДЕМ ЗАВЕРШЕНИЯ СИНХРОНИЗАЦИИ =====
                StartCoroutine(WaitForEnemySyncAndStartAction());
                break;

            default:
                Debug.LogWarning($"[FightManager] Unexpected state for HandleAllPlayersReady: {currentState}");
                break;
        }
    }
    [Server]
    private System.Collections.IEnumerator WaitForEnemySyncAndStartAction()
    {
        // Даем время на синхронизацию (1 секунда)
        yield return new WaitForSeconds(0.5f);

        ChangeState(FightState.Action);
        StartCoroutine(ExecuteActionPhase());
    }
    [Server]
    private void ResetAllPlayersReady()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null)
            {
                player.isReady = false;
            }
        }
        readyPlayers.Clear();
        readyPlayersCount = 0;
        Debug.Log("[FightManager] All players ready reset");
    }

    // ===== Ôàçû áîÿ =====

    

    [Server]
    private System.Collections.IEnumerator ExecuteEndTurnPhase()
    {
        Debug.Log("[FightManager] Executing End Turn phase...");

        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            // player.ApplyEndTurnEffects();
            yield return new WaitForSeconds(0.1f);
        }

        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            // enemy.ApplyEndTurnEffects();
            yield return new WaitForSeconds(0.1f);
        }

        yield return new WaitForSeconds(endTurnDuration);

        if (CheckFightEndConditions())
        {
            EndEncounterAndReturnToMap();
            yield break;
        }

        // ===== ÂÛÒßÃÈÂÀÅÌ ÊÀÐÒÛ ÏÎÑËÅ ENDTURN =====
        DrawCardsForAllPlayers();

        ChangeState(FightState.Waiting);
        StartWaitingForPlayers();
    }

    [Server]
    private bool CheckFightEndConditions()
    {
        bool allPlayersDead = true;
        bool allEnemiesDead = true;

        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player.hp > 0)
            {
                allPlayersDead = false;
                break;
            }
        }

        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy.hp > 0)
            {
                allEnemiesDead = false;
                break;
            }
        }

        if (allPlayersDead)
        {
            Debug.Log("[FightManager] All players are dead! Fight lost!");
            return true;
        }

        if (allEnemiesDead)
        {
            Debug.Log("[FightManager] All enemies are dead! Fight won!");
            return true;
        }

        return false;
    }

    #endregion

    #region Client Hooks

    private void OnStateChanged(FightState oldState, FightState newState)
    {
        Debug.Log($"[FightManager] Client: State changed from {oldState} to {newState}");
        OnFightStateChanged?.Invoke(newState);
    }

    #endregion

    #region Client Methods

    [ClientRpc]
    public void RpcHideCardView()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null)
            {
                player.HideCardView();
            }
        }

        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null)
            {
                enemy.HideCardView();
            }
        }
    }


    [ClientRpc]
    private void RpcResetAllUIPositions()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
            if (player != null) player.ResetUIPosition();

        foreach (var enemy in NetworkGameEnemy.AllEnemies)
            if (enemy != null) enemy.ResetUIPosition();
    }

    [ClientRpc]
    private void RpcClearAllSelections()
    {
        Debug.Log("[RpcClearAllSelections] Clearing all selections...");

        // Î÷èùàåì âûáîðû êóáèêîâ
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.isLocalPlayer && player.UIObject != null)
            {
                DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var dice in dices)
                {
                    if (dice != null)
                    {
                        dice.ClearSelection();

                        // Äîïîëíèòåëüíî î÷èùàåì UIAimLine
                        UIAimLine aimLine = dice.GetComponentInChildren<UIAimLine>();
                        if (aimLine != null)
                        {
                            aimLine.ClearAimData();
                        }
                    }
                }
            }
        }

        // Î÷èùàåì ãëîáàëüíûé âûáîð
        if (DiceSelectionManager.Instance != null)
        {
            DiceSelectionManager.Instance.ClearAllSelections();
        }

        // Îáíîâëÿåì âñå êàðòû
        LocalHandCardView[] cards = FindObjectsByType<LocalHandCardView>(FindObjectsSortMode.None);
        foreach (var card in cards)
        {
            card.UpdateCardState();
        }

        Debug.Log("[RpcClearAllSelections] Complete");
    }

    [ClientRpc]
    private void RpcClearAllAimLines()
    {
        Debug.Log("[RpcClearAllAimLines] Clearing all aim lines...");

        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.isLocalPlayer && player.UIObject != null)
            {
                DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var dice in dices)
                {
                    if (dice != null)
                    {
                        UIAimLine aimLine = dice.GetComponentInChildren<UIAimLine>();
                        if (aimLine != null)
                        {
                            aimLine.ClearAimData();
                            Debug.Log($"[RpcClearAllAimLines] Cleared aim line for dice {dice.ownerSlotIndex}");
                        }
                    }
                }
            }
        }
    }


    [ClientRpc]
    private void RpcPlayRollingSound()
    {
        if (rollingSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(rollingSound, soundVolume);
        }
    }
    [ClientRpc]
    private void RpcUpdateDiceUI(FightState state)
    {
        bool showDiceUI = (state == FightState.Waiting || state == FightState.Rolling);

        // Скрываем/показываем UI кубиков у всех игроков и врагов
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.UIObject != null)
            {
                foreach (var dice in player.UIObject.GetComponentsInChildren<DiceRoll>())
                {
                    dice?.SetUIVisible(showDiceUI);
                }
            }
        }

        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null && enemy.UIObject != null)
            {
                foreach (var dice in enemy.UIObject.GetComponentsInChildren<DiceRoll>())
                {
                    dice?.SetUIVisible(showDiceUI);
                }
            }
        }

        switch (state)
        {
            case FightState.Waiting:
                // Скрываем все CardView у всех
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    if (player != null)
                    {
                        player.HideCardView();
                    }
                }

                foreach (var enemy in NetworkGameEnemy.AllEnemies)
                {
                    if (enemy != null)
                    {
                        enemy.HideCardView();
                    }
                }

                // Очищаем все выборы кубиков локального игрока
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    if (player != null && player.isLocalPlayer && player.UIObject != null)
                    {
                        // Очищаем выборы кубиков
                        DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
                        foreach (var dice in dices)
                        {
                            if (dice != null)
                            {
                                dice.ClearSelection();
                            }
                        }

                        // Обновляем диапазоны кубиков
                        player.UpdateAllDiceRange();
                    }
                }

                // Очищаем глобальный выбор
                if (DiceSelectionManager.Instance != null)
                {
                    DiceSelectionManager.Instance.ClearAllSelections();
                }

                // Обновляем все карты в руке
                LocalHandCardView[] cards = FindObjectsByType<LocalHandCardView>(FindObjectsSortMode.None);
                foreach (var card in cards)
                {
                    card.UpdateCardState();
                }
                break;

            case FightState.Rolling:
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    if (player != null && player.isLocalPlayer)
                    {
                        player.UpdateAllDiceResult();
                    }
                }
                break;
        }
    }

    [Client]
    public FightState GetCurrentState()
    {
        return currentState;
    }

    [Client]
    public bool CanPlayerReady()
    {
        return currentState == FightState.Waiting || currentState == FightState.Rolling;
    }

    [ClientRpc]
    private void RpcApplyRunFlowSnapshot(string snapshotJson)
    {
        RunFlowManager.Instance?.ApplySnapshot(snapshotJson);
    }

    #endregion

    #region Public Methods

    [Server]
    public void BroadcastRunFlowSnapshot(string snapshotJson)
    {
        RpcApplyRunFlowSnapshot(snapshotJson);
    }

    [Server]
    public void ForceNextState()
    {
        switch (currentState)
        {
            case FightState.Waiting:
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    PlayerReady(player);
                }
                break;
            case FightState.Rolling:
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    PlayerReady(player);
                }
                break;
            case FightState.Action:
                ChangeState(FightState.EndTurn);
                StartCoroutine(ExecuteEndTurnPhase());
                break;
            case FightState.EndTurn:
                ChangeState(FightState.Waiting);
                StartWaitingForPlayers();
                break;
        }
    }

    [Server]
    public int GetReadyPlayersCount()
    {
        return readyPlayersCount;
    }

    [Server]
    public int GetTotalPlayersCount()
    {
        return NetworkGamePlayer.AllPlayers.Count;
    }

    [Server]
    public bool IsPlayerReady(NetworkGamePlayer player)
    {
        if (player == null) return false;
        return readyPlayers.Contains(player);
    }

    #endregion
}
