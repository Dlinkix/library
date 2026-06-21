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
    // ===== Синглтон =====
    public static FightManager Instance { get; private set; }

    // ===== События =====
    public static UnityAction<FightState> OnFightStateChanged;
    public static UnityAction OnFightStarted;
    public static UnityAction OnFightEnded;
    public static UnityAction OnAllPlayersReady;

    // ===== Синхронизируемые переменные =====
    [SyncVar(hook = nameof(OnStateChanged))]
    private FightState currentState = FightState.Waiting;

    [SyncVar]
    private int turnNumber = 0;

    // ===== Настройки =====
    [Header("Settings")]
    [SerializeField] private float actionDuration = 2f;
    [SerializeField] private float endTurnDuration = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool debugMode = true;

    [Header("Audio")]
    [SerializeField] private AudioClip rollingSound;
    [SerializeField] private float soundVolume = 0.1f;

    private AudioSource audioSource;

    // ===== Состояние =====
    private bool isFightActive = false;
    private int readyPlayersCount = 0;
    private HashSet<NetworkGamePlayer> readyPlayers = new HashSet<NetworkGamePlayer>();
    private bool isWaitingForReady = false;

    // ===== Свойства =====
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

    void Start()
    {
        if (isServer)
        {
            Debug.Log("[FightManager] Auto-starting fight...");
            StartFight();
        }
    }
    public class TurnOrderEntry
    {
        public DiceRoll dice;
        public NetworkGamePlayer player;
        public int speedValue;
        public int diceIndex;
    }

    #endregion

    #region Server Methods


    [Server]
    private List<TurnOrderEntry> GetTurnOrder()
    {
        List<TurnOrderEntry> turnOrder = new List<TurnOrderEntry>();

        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player == null) continue;

            DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
            foreach (var dice in dices)
            {
                if (dice != null && dice.hasSelection)
                {
                    turnOrder.Add(new TurnOrderEntry
                    {
                        dice = dice,
                        player = player,
                        speedValue = dice.diceValue, // значение кубика = скорость
                        diceIndex = dice.ownerSlotIndex
                    });
                }
            }
        }

        // Сортируем по скорости (по убыванию - кто быстрее, тот ходит первым)
        turnOrder.Sort((a, b) => {
            // Сначала сравниваем скорость
            int speedCompare = b.speedValue.CompareTo(a.speedValue);
            if (speedCompare != 0) return speedCompare;

            // Если скорости равны - случайный порядок
            return Random.Range(-1, 2);
        });

        return turnOrder;
    }


    [Server]
    private void ClearAllDiceSelections()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null)
            {
                DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var dice in dices)
                {
                    if (dice != null)
                    {
                        dice.ClearSelection(); // Очищает ВСЕ данные выбора
                        Debug.Log($"[ClearAllDiceSelections] Cleared dice {dice.ownerSlotIndex}");
                    }
                }
            }
        }
    }

    [Server]
    private System.Collections.IEnumerator ExecuteActionPhase()
    {
        Debug.Log("[FightManager] Executing Action phase...");

        // ===== ПОЛУЧАЕМ ПОРЯДОК ХОДА =====
        List<TurnOrderEntry> turnOrder = GetTurnOrder();

        Debug.Log($"[ExecuteActionPhase] Turn order: {turnOrder.Count} entries");
        foreach (var entry in turnOrder)
        {
            Debug.Log($"[ExecuteActionPhase] Dice {entry.diceIndex} (Player {entry.player.PlayerName}) speed: {entry.speedValue}");
        }

        // ===== ВЫПОЛНЯЕМ ХОДЫ ПО ПОРЯДКУ =====
        foreach (var entry in turnOrder)
        {
            if (entry.dice != null && entry.dice.hasSelection)
            {
                ApplyCardFromDice(entry.player, entry.dice);
                entry.dice.ClearSelection();
            }
            yield return new WaitForSeconds(0.2f);
        }

        // ===== ЖДЕМ ЗАВЕРШЕНИЯ ВСЕХ АТАК =====
        bool allActionsCompleted = false;
        while (!allActionsCompleted)
        {
            allActionsCompleted = true;
            foreach (var player in NetworkGamePlayer.AllPlayers)
            {
                if (player != null && player.IsExecutingActions)
                {
                    allActionsCompleted = false;
                    break;
                }
            }
            yield return new WaitForSeconds(0.1f);
        }

        // ===== ОЧИЩАЕМ ТОЛЬКО ПОСЛЕ ВСЕХ АТАК =====
        ClearAllDiceSelections();
        RpcClearAllAimLines();

        yield return new WaitForSeconds(actionDuration);
        ChangeState(FightState.EndTurn);
        StartCoroutine(ExecuteEndTurnPhase());
    }


    [Server]
    private void ApplyCardFromDice(NetworkGamePlayer player, DiceRoll dice)
    {
        if (player == null || dice == null || !dice.hasSelection)
        {
            Debug.Log($"[ApplyCardFromDice] Skip: player={player != null}, dice={dice != null}, hasSelection={dice?.hasSelection}");
            return;
        }

        Debug.Log($"[ApplyCardFromDice] Processing dice {dice.ownerSlotIndex}: cardId={dice.selectedCardId}, cardIndex={dice.selectedCardIndex}, target={dice.selectedTargetEnemyNetId}");

        // ===== ПРОВЕРЯЕМ, ЧТО ИНДЕКС КАРТЫ ВАЛИДНЫЙ =====
        if (dice.selectedCardIndex < 0 || dice.selectedCardIndex >= player.PlayerHand.Count)
        {
            Debug.Log($"[ApplyCardFromDice] Invalid card index {dice.selectedCardIndex}! Hand size: {player.PlayerHand.Count}");
            dice.ClearSelection();
            return;
        }

        // ===== ПРОВЕРЯЕМ, ЧТО ПО ИНДЕКСУ ЛЕЖИТ ТА ЖЕ КАРТА =====
        if (player.PlayerHand[dice.selectedCardIndex] != dice.selectedCardId)
        {
            Debug.Log($"[ApplyCardFromDice] Card at index {dice.selectedCardIndex} is {player.PlayerHand[dice.selectedCardIndex]}, expected {dice.selectedCardId}!");
            dice.ClearSelection();
            return;
        }

        // Находим врага по цели
        NetworkGameEnemy targetEnemy = null;
        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null && enemy.netId == dice.selectedTargetEnemyNetId)
            {
                targetEnemy = enemy;
                break;
            }
        }

        if (targetEnemy == null)
        {
            Debug.LogWarning($"[ApplyCardFromDice] Target enemy not found for dice {dice.ownerSlotIndex}");
            dice.ClearSelection();
            return;
        }

        // Получаем карту
        if (!player.DataGame.TryGetCardById(dice.selectedCardId, out CardData card))
        {
            Debug.LogWarning($"[ApplyCardFromDice] Card {dice.selectedCardId} not found");
            dice.ClearSelection();
            return;
        }

        // Проверяем Light
        if (player.currentLight < card.lightCost)
        {
            Debug.Log($"[ApplyCardFromDice] Not enough Light! Need {card.lightCost}, have {player.currentLight}");
            dice.ClearSelection();
            return;
        }

        // ===== ЕЩЕ РАЗ ПРОВЕРЯЕМ ПЕРЕД УДАЛЕНИЕМ =====
        if (dice.selectedCardIndex < 0 || dice.selectedCardIndex >= player.PlayerHand.Count)
        {
            Debug.Log($"[ApplyCardFromDice] Card index {dice.selectedCardIndex} became invalid before removal!");
            dice.ClearSelection();
            return;
        }

        if (player.PlayerHand[dice.selectedCardIndex] != dice.selectedCardId)
        {
            Debug.Log($"[ApplyCardFromDice] Card at index {dice.selectedCardIndex} changed before removal!");
            dice.ClearSelection();
            return;
        }

        // Тратим Light
        player.currentLight -= card.lightCost;

        // ===== УДАЛЯЕМ КАРТУ ПО ИНДЕКСУ =====
        int indexToRemove = dice.selectedCardIndex;
        player.PlayerHand.RemoveAt(indexToRemove);
        player.SyncHandToOwner();

        // ===== ОБНОВЛЯЕМ ИНДЕКСЫ У ВСЕХ КУБИКОВ ЭТОГО ИГРОКА =====
        UpdateDiceCardIndices(player, indexToRemove);

        // Применяем эффекты
        player.QueueCardEffects(card, targetEnemy);

        // Сбрасываем выбор кубика после применения
        dice.ClearSelection();

        Debug.Log($"[ApplyCardFromDice] Applied card {card.cardName} from dice {dice.ownerSlotIndex} to {targetEnemy.EnemyName}");
    }
    [Server]
    private void UpdateDiceCardIndices(NetworkGamePlayer player, int removedIndex)
    {
        // Проходим по всем кубикам игрока
        DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
        foreach (var d in dices)
        {
            if (d != null && d.selectedCardIndex > removedIndex)
            {
                // Уменьшаем индекс на 1, так как карта была удалена
                d.selectedCardIndex--;
                Debug.Log($"[UpdateDiceCardIndices] Updated dice {d.ownerSlotIndex} index from {d.selectedCardIndex + 1} to {d.selectedCardIndex}");
            }
        }
    }
    [Server]
    public void StartFight()
    {
        if (isFightActive) return;

        Debug.Log("[FightManager] Fight started!");
        isFightActive = true;
        turnNumber = 0;

        OnFightStarted?.Invoke();

        ChangeState(FightState.Waiting);
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
        currentState = newState;
        Debug.Log($"[FightManager] State changed to: {newState}");
        OnFightStateChanged?.Invoke(newState);

        if (newState == FightState.Rolling)
        {
            RpcPlayRollingSound();
        }
        // ===== УБИРАЕМ ОЧИСТКУ ЗДЕСЬ =====
        // if (newState == FightState.Action) 
        // {
        //     RpcClearAllSelections();
        // }
        RpcUpdateDiceUI(newState);
    }

    // ===== Ролл кубиков =====

    [Server]
    private void RollAllDice()
    {
        Debug.Log("[FightManager] Rolling dice for all players and enemies...");

        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null)
            {
                // ===== ОБНОВЛЯЕМ КУБИКИ =====
                player.RollAllDice();

                // ===== ПОКАЗЫВАЕМ В UI =====
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

    // ===== Вытягивание карт =====

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

    // ===== Управление готовностью =====

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

        OnAllPlayersReady += HandleAllPlayersReady;

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
                ChangeState(FightState.Action);
                StartCoroutine(ExecuteActionPhase());
                break;

            default:
                Debug.LogWarning($"[FightManager] Unexpected state for HandleAllPlayersReady: {currentState}");
                break;
        }
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

    // ===== Фазы боя =====

    

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
            StopFight();
            yield break;
        }

        // ===== ВЫТЯГИВАЕМ КАРТЫ ПОСЛЕ ENDTURN =====
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
    private void RpcClearAllSelections()
    {
        // Очищаем выборы кубиков
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.isLocalPlayer)
            {
                DiceRoll[] dices = player.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var dice in dices)
                {
                    if (dice != null)
                    {
                        dice.ClearSelection();
                        UIAimLine aimLine = dice.GetComponentInChildren<UIAimLine>();
                        if (aimLine != null)
                        {
                            aimLine.SetCardSelected(false);
                            // НЕ ВЫКЛЮЧАЕМ aimLine.gameObject.SetActive(false)! <-- УБЕРИ ЭТО
                            // aimLine.gameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        // Обновляем все карты
        LocalHandCardView[] cards = FindObjectsByType<LocalHandCardView>(FindObjectsSortMode.None);
        foreach (var card in cards)
        {
            card.UpdateCardState();
        }
    }

    [ClientRpc]
    private void RpcClearAllAimLines()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.isLocalPlayer)
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
                            // НЕ ВЫКЛЮЧАЙТЕ aimLine.gameObject.SetActive(false)!
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
        switch (state)
        {
            case FightState.Waiting:
                // Обновляем всех игроков (только локального)
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    if (player != null && player.isLocalPlayer)
                    {
                        player.UpdateAllDiceRange();
                    }
                }

                //// Обновляем всех врагов
                //foreach (var enemy in NetworkGameEnemy.AllEnemies)
                //{
                //    if (enemy != null)
                //    {
                //        enemy.UpdateAllDiceRange();
                //    }
                //}
                break;

            case FightState.Rolling:
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    if (player != null && player.isLocalPlayer)
                    {
                        player.UpdateAllDiceResult();
                    }
                }

                //foreach (var enemy in NetworkGameEnemy.AllEnemies)
                //{
                //    if (enemy != null)
                //    {
                //        enemy.UpdateAllDiceResult();
                //    }
                //}
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

    #endregion

    #region Public Methods

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