using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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

    #endregion

    #region Server Methods

    [Server]
    private System.Collections.IEnumerator ExecuteActionPhase()
    {
        Debug.Log("[FightManager] Executing Action phase...");

        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.HasSelection())
            {
                int cardId = player.GetSelectedCardId();
                uint enemyNetId = player.GetSelectedTargetEnemyNetId();

                // Находим врага
                NetworkGameEnemy targetEnemy = null;
                foreach (var enemy in NetworkGameEnemy.AllEnemies)
                {
                    if (enemy != null && enemy.netId == enemyNetId)
                    {
                        targetEnemy = enemy;
                        break;
                    }
                }

                if (targetEnemy != null)
                {
                    // ===== ВМЕСТО ApplyCadToEnemy ИСПОЛЬЗУЕМ QueueCardForTarget =====
                    player.QueueCardForTarget(cardId, targetEnemy);
                    Debug.Log($"[FightManager] Player {player.PlayerName} queued card for {targetEnemy.EnemyName}");
                }
                else
                {
                    Debug.LogWarning($"[FightManager] Target enemy not found for player {player.PlayerName}");
                }

                // Очищаем выбор
                player.ClearSelection();
            }
            else
            {
                Debug.Log($"[FightManager] Player {player?.PlayerName} has no selection");
            }
            yield return new WaitForSeconds(0.2f);
        }

        //// ===== ПРИМЕНЯЕМ ДЕЙСТВИЯ ВРАГОВ (AI) =====
        //foreach (var enemy in NetworkGameEnemy.AllEnemies)
        //{
        //    if (enemy != null)
        //    {
        //        // ВЫЗЫВАЕМ AI ВРАГА
        //        enemy.ExecuteAIAction();
        //        Debug.Log($"[FightManager] Enemy {enemy.EnemyName} AI action executed");
        //    }
        //    yield return new WaitForSeconds(0.2f);
        //}

        yield return new WaitForSeconds(actionDuration);

        ChangeState(FightState.EndTurn);
        StartCoroutine(ExecuteEndTurnPhase());
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
                int roll = player.GetRollValue();
                player.RpcShowRollResult(roll, player.PlayerName);
                Debug.Log($"[FightManager] Player {player.PlayerName} rolled: {roll}");
            }
        }

        foreach (var enemy in NetworkGameEnemy.AllEnemies)
        {
            if (enemy != null)
            {
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
    private void RpcPlayRollingSound()
    {
        if (rollingSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(rollingSound, soundVolume);
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