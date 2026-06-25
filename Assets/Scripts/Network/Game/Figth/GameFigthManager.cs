using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
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
    public static FightManager Instance { get; private set; }

    public static UnityAction<FightState> OnFightStateChanged;
    public static UnityAction OnFightStarted;
    public static UnityAction OnFightEnded;
    public static UnityAction OnAllPlayersReady;

    [SyncVar(hook = nameof(OnStateChanged))]
    private FightState currentState = FightState.Waiting;

    [SyncVar]
    private int turnNumber = 0;

    [SyncVar]
    private bool isFightActive = false;

    [Header("Settings")]
    [SerializeField] private float actionDuration = 2f;
    [SerializeField] private float endTurnDuration = 1.5f;

    [Header("Audio")]
    [SerializeField] private AudioClip rollingSound;
    [SerializeField] private float soundVolume = 0.1f;

    private AudioSource audioSource;
    private int readyPlayersCount = 0;
    private HashSet<NetworkGamePlayer> readyPlayers = new HashSet<NetworkGamePlayer>();
    private bool isWaitingForReady = false;

    public FightState CurrentState => currentState;
    public int TurnNumber => turnNumber;
    public bool IsFightActive => isFightActive;

    public class TurnOrderEntry
    {
        public DiceRoll dice;
        public NetworkGamePlayer player;
        public NetworkGameEnemy enemy;
        public int speedValue;
        public int diceIndex;
    }

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

    [Server]
    private List<TurnOrderEntry> GetTurnOrder()
    {
        List<TurnOrderEntry> turnOrder = new List<TurnOrderEntry>();

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
                    if (dice != null) dice.ClearSelection();
                }
            }
        }
        RpcHideCardView();
    }

    [Server]
    private System.Collections.IEnumerator ExecuteActionPhase()
    {
        List<TurnOrderEntry> turnOrder = GetTurnOrder();

        foreach (var entry in turnOrder)
        {
            if (entry.dice != null && entry.dice.hasSelection)
            {
                object source = entry.player != null ? (object)entry.player : entry.enemy;
                ApplyCardFromDice(source, entry.dice);
                entry.dice.ClearSelection();

                if (entry.player != null)
                {
                    while (entry.player.IsExecutingActions)
                    {
                        yield return new WaitForSeconds(0.1f);
                    }
                }
                else if (entry.enemy != null)
                {
                    float enemyTimeout = 5f;
                    float enemyTimer = 0f;
                    while (entry.enemy.IsExecutingActions && enemyTimer < enemyTimeout)
                    {
                        yield return new WaitForSeconds(0.1f);
                        enemyTimer += 0.1f;
                    }
                    yield return new WaitForSeconds(0.3f);
                }
            }
        }

        float timeout = 10f;
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
                    break;
                }
            }

            if (!allActionsCompleted)
            {
                yield return new WaitForSeconds(0.1f);
                timer += 0.1f;
            }
        }

        ClearAllDiceSelections();
        RpcClearAllAimLines();
        yield return new WaitForSeconds(actionDuration);
        ChangeState(FightState.EndTurn);
        StartCoroutine(ExecuteEndTurnPhase());
    }

    [Server]
    private void ApplyCardFromDice(object source, DiceRoll dice)
    {
        if (dice == null || !dice.hasSelection) return;

        NetworkGamePlayer playerOwner = null;
        NetworkGameEnemy enemyOwner = null;

        if (dice.isEnemyDice)
        {
            foreach (var enemy in NetworkGameEnemy.AllEnemies)
            {
                if (enemy == null || enemy.UIObject == null) continue;
                DiceRoll[] enemyDices = enemy.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var d in enemyDices)
                {
                    if (d == dice) { enemyOwner = enemy; break; }
                }
                if (enemyOwner != null) break;
            }
        }
        else
        {
            foreach (var player in NetworkGamePlayer.AllPlayers)
            {
                if (player == null || player.UIObject == null) continue;
                DiceRoll[] playerDices = player.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var d in playerDices)
                {
                    if (d == dice) { playerOwner = player; break; }
                }
                if (playerOwner != null) break;
            }
        }

        if (playerOwner != null) ApplyCardFromPlayer(playerOwner, dice);
        else if (enemyOwner != null) ApplyCardFromEnemy(enemyOwner, dice);
        else dice.ClearSelection();
    }

    [Server]
    private void ApplyCardFromPlayer(NetworkGamePlayer player, DiceRoll dice)
    {
        if (dice.selectedCardIndex < 0 || dice.selectedCardIndex >= player.PlayerHand.Count) { dice.ClearSelection(); return; }
        if (player.PlayerHand[dice.selectedCardIndex] != dice.selectedCardId) { dice.ClearSelection(); return; }

        NetworkGameEnemy targetEnemy = GetTargetEnemy(dice.selectedTargetEnemyNetId);
        if (targetEnemy == null) { dice.ClearSelection(); return; }

        if (!player.DataGame.TryGetCardById(dice.selectedCardId, out CardData card)) { dice.ClearSelection(); return; }
        if (player.currentLight < card.lightCost) { dice.ClearSelection(); return; }

        int indexToRemove = dice.selectedCardIndex;
        if (indexToRemove < 0 || indexToRemove >= player.PlayerHand.Count || player.PlayerHand[indexToRemove] != dice.selectedCardId)
        { dice.ClearSelection(); return; }

        player.currentLight -= card.lightCost;
        player.PlayerHand.RemoveAt(indexToRemove);
        player.SyncHandToOwner();

        UpdateDiceCardIndices(player, indexToRemove);
        foreach (var enemy in NetworkGameEnemy.AllEnemies) UpdateDiceCardIndices(enemy, indexToRemove);

        player.QueueCardEffects(card, indexToRemove, targetEnemy);
        dice.ClearSelection();
    }

    [Server]
    private void ApplyCardFromEnemy(NetworkGameEnemy enemy, DiceRoll dice)
    {
        if (dice.selectedCardIndex < 0 || dice.selectedCardIndex >= enemy.enemyHand.Count) { dice.ClearSelection(); return; }
        if (enemy.enemyHand[dice.selectedCardIndex] != dice.selectedCardId) { dice.ClearSelection(); return; }

        NetworkGamePlayer targetPlayer = GetTargetPlayer(dice.selectedTargetEnemyNetId);
        if (targetPlayer == null) { dice.ClearSelection(); return; }

        if (!enemy.DataGame.TryGetCardById(dice.selectedCardId, out CardData card)) { dice.ClearSelection(); return; }
        if (enemy.currentLight < card.lightCost) { dice.ClearSelection(); return; }

        int indexToRemove = dice.selectedCardIndex;
        if (indexToRemove < 0 || indexToRemove >= enemy.enemyHand.Count || enemy.enemyHand[indexToRemove] != dice.selectedCardId)
        { dice.ClearSelection(); return; }

        enemy.currentLight -= card.lightCost;
        enemy.enemyHand.RemoveAt(indexToRemove);

        foreach (var e in NetworkGameEnemy.AllEnemies) UpdateDiceCardIndices(e, indexToRemove);

        enemy.QueueCardEffects(card, indexToRemove, targetPlayer);
        dice.ClearSelection();
    }

    [Server] private NetworkGameEnemy GetTargetEnemy(uint targetNetId) { foreach (var e in NetworkGameEnemy.AllEnemies) if (e != null && e.netId == targetNetId) return e; return null; }
    [Server] private NetworkGamePlayer GetTargetPlayer(uint targetNetId) { foreach (var p in NetworkGamePlayer.AllPlayers) if (p != null && p.netId == targetNetId) return p; return null; }

    [Server]
    private void UpdateDiceCardIndices(object owner, int removedIndex)
    {
        if (owner is NetworkGamePlayer player && player != null && player.UIObject != null)
        {
            foreach (var d in player.UIObject.GetComponentsInChildren<DiceRoll>())
                if (d != null && d.selectedCardIndex > removedIndex) d.selectedCardIndex--;
        }
        else if (owner is NetworkGameEnemy enemy && enemy != null && enemy.UIObject != null)
        {
            foreach (var d in enemy.UIObject.GetComponentsInChildren<DiceRoll>())
                if (d != null && d.selectedCardIndex > removedIndex) d.selectedCardIndex--;
        }
    }

    [Server] public void BeginEncounter(MapRoomType roomType) { ResetEncounterState(); (NetworkManager.singleton as NetworkManagerLobby)?.StartBattleEncounter(roomType); StartFight(); }

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
        (NetworkManager.singleton as NetworkManagerLobby)?.ResetBattleEncounter();
        ResetAllPlayersReady();
        RpcClearAllAimLines();
        RpcClearAllSelections();
        RpcResetAllUIPositions();
        RpcUpdateDiceUI(FightState.Waiting);
    }

    [Server] public void EndEncounterAndReturnToMap() { StopFight(); ResetEncounterState(); RunFlowManager.Instance?.ReturnToMapFromBattle(); }

    [Server]
    public void StartFight()
    {
        if (isFightActive) return;
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
        currentState = newState;
        if (newState == FightState.Rolling) RpcPlayRollingSound();
        if (newState == FightState.Waiting) { ClearAllDiceSelections(); RpcClearAllAimLines(); RpcClearAllSelections(); RpcResetAllUIPositions(); }
        RpcUpdateDiceUI(newState);
    }

    [Server]
    private void RollAllDice()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers) if (player != null) { player.RollAllDice(); player.RpcShowRollResult(player.GetRollValue(), player.PlayerName); }
        foreach (var enemy in NetworkGameEnemy.AllEnemies) if (enemy != null) { enemy.RollAllDice(); enemy.RpcShowRollResult(enemy.GetRollValue(), enemy.EnemyName); }
    }

    [Server]
    private void DrawCardsForAllPlayers()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers) if (player != null) player.DrawCardFromDeck(player.GetCardsToDrawAfterReadyCycle());
    }

    [Server]
    public void PlayerReady(NetworkGamePlayer player)
    {
        if (!isFightActive || player == null || !isWaitingForReady || readyPlayers.Contains(player)) return;
        if (currentState != FightState.Waiting && currentState != FightState.Rolling) return;
        readyPlayers.Add(player);
        readyPlayersCount++;
        player.isReady = true;
        if (readyPlayersCount >= NetworkGamePlayer.AllPlayers.Count) OnAllPlayersReady?.Invoke();
    }

    [Server]
    private void StartWaitingForPlayers()
    {
        ResetAllPlayersReady();
        isWaitingForReady = true;
        readyPlayers.Clear();
        readyPlayersCount = 0;
        foreach (var enemy in NetworkGameEnemy.AllEnemies) if (enemy != null) enemy.isReady = false;
        OnAllPlayersReady += HandleAllPlayersReady;
        RpcClearAllAimLines();
        RpcClearAllSelections();
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
                foreach (var enemy in NetworkGameEnemy.AllEnemies) if (enemy != null) enemy.ProcessAITurn();
                StartCoroutine(WaitForEnemySyncAndStartAction());
                break;
        }
    }

    [Server] private System.Collections.IEnumerator WaitForEnemySyncAndStartAction() { yield return new WaitForSeconds(0.5f); ChangeState(FightState.Action); StartCoroutine(ExecuteActionPhase()); }

    [Server]
    private void ResetAllPlayersReady()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers) if (player != null) player.isReady = false;
        readyPlayers.Clear();
        readyPlayersCount = 0;
    }

    [Server]
    private System.Collections.IEnumerator ExecuteEndTurnPhase()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers) { yield return new WaitForSeconds(0.1f); }
        foreach (var enemy in NetworkGameEnemy.AllEnemies) { yield return new WaitForSeconds(0.1f); }
        yield return new WaitForSeconds(endTurnDuration);
        if (CheckFightEndConditions()) { EndEncounterAndReturnToMap(); yield break; }
        DrawCardsForAllPlayers();
        ChangeState(FightState.Waiting);
        StartWaitingForPlayers();
    }

    [Server]
    private bool CheckFightEndConditions()
    {
        bool allPlayersDead = true;
        bool allEnemiesDead = true;
        foreach (var player in NetworkGamePlayer.AllPlayers) if (player.hp > 0) { allPlayersDead = false; break; }
        foreach (var enemy in NetworkGameEnemy.AllEnemies) if (enemy.hp > 0) { allEnemiesDead = false; break; }
        return allPlayersDead || allEnemiesDead;
    }

    private void OnStateChanged(FightState oldState, FightState newState) => OnFightStateChanged?.Invoke(newState);

    [ClientRpc]
    public void RpcHideCardView()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers) player?.HideCardView();
        foreach (var enemy in NetworkGameEnemy.AllEnemies) enemy?.HideCardView();
    }

    [ClientRpc] private void RpcResetAllUIPositions() { foreach (var p in NetworkGamePlayer.AllPlayers) p?.ResetUIPosition(); foreach (var e in NetworkGameEnemy.AllEnemies) e?.ResetUIPosition(); }

    [ClientRpc]
    private void RpcClearAllSelections()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.isLocalPlayer && player.UIObject != null)
            {
                foreach (var dice in player.UIObject.GetComponentsInChildren<DiceRoll>())
                {
                    if (dice != null) { dice.ClearSelection(); dice.GetComponentInChildren<UIAimLine>()?.ClearAimData(); }
                }
            }
        }
        DiceSelectionManager.Instance?.ClearAllSelections();
        foreach (var card in FindObjectsByType<LocalHandCardView>(FindObjectsSortMode.None)) card?.UpdateCardState();
    }

    [ClientRpc]
    private void RpcClearAllAimLines()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.isLocalPlayer && player.UIObject != null)
            {
                foreach (var dice in player.UIObject.GetComponentsInChildren<DiceRoll>())
                    dice?.GetComponentInChildren<UIAimLine>()?.ClearAimData();
            }
        }
    }

    [ClientRpc] private void RpcPlayRollingSound() { if (rollingSound != null && audioSource != null) audioSource.PlayOneShot(rollingSound, soundVolume); }

    [ClientRpc]
    private void RpcUpdateDiceUI(FightState state)
    {
        bool showDiceUI = (state == FightState.Waiting || state == FightState.Rolling);
        foreach (var player in NetworkGamePlayer.AllPlayers) if (player != null && player.UIObject != null) foreach (var dice in player.UIObject.GetComponentsInChildren<DiceRoll>()) dice?.SetUIVisible(showDiceUI);
        foreach (var enemy in NetworkGameEnemy.AllEnemies) if (enemy != null && enemy.UIObject != null) foreach (var dice in enemy.UIObject.GetComponentsInChildren<DiceRoll>()) dice?.SetUIVisible(showDiceUI);

        switch (state)
        {
            case FightState.Waiting:
                foreach (var player in NetworkGamePlayer.AllPlayers) player?.HideCardView();
                foreach (var enemy in NetworkGameEnemy.AllEnemies) enemy?.HideCardView();
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    if (player != null && player.isLocalPlayer && player.UIObject != null)
                    {
                        foreach (var dice in player.UIObject.GetComponentsInChildren<DiceRoll>()) dice?.ClearSelection();
                        player.UpdateAllDiceRange();
                    }
                }
                DiceSelectionManager.Instance?.ClearAllSelections();
                foreach (var card in FindObjectsByType<LocalHandCardView>(FindObjectsSortMode.None)) card?.UpdateCardState();
                break;
            case FightState.Rolling:
                foreach (var player in NetworkGamePlayer.AllPlayers) if (player != null && player.isLocalPlayer) player.UpdateAllDiceResult();
                break;
        }
    }

    [Client] public FightState GetCurrentState() => currentState;
    [Client] public bool CanPlayerReady() => currentState == FightState.Waiting || currentState == FightState.Rolling;
    [ClientRpc] private void RpcApplyRunFlowSnapshot(string snapshotJson) => RunFlowManager.Instance?.ApplySnapshot(snapshotJson);
    [Server] public void BroadcastRunFlowSnapshot(string snapshotJson) => RpcApplyRunFlowSnapshot(snapshotJson);

    [Server]
    public void ForceNextState()
    {
        switch (currentState)
        {
            case FightState.Waiting: foreach (var p in NetworkGamePlayer.AllPlayers) PlayerReady(p); break;
            case FightState.Rolling: foreach (var p in NetworkGamePlayer.AllPlayers) PlayerReady(p); break;
            case FightState.Action: ChangeState(FightState.EndTurn); StartCoroutine(ExecuteEndTurnPhase()); break;
            case FightState.EndTurn: ChangeState(FightState.Waiting); StartWaitingForPlayers(); break;
        }
    }

    [Server] public int GetReadyPlayersCount() => readyPlayersCount;
    [Server] public int GetTotalPlayersCount() => NetworkGamePlayer.AllPlayers.Count;
    [Server] public bool IsPlayerReady(NetworkGamePlayer player) => player != null && readyPlayers.Contains(player);
}