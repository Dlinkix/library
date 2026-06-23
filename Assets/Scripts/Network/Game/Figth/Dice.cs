using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class DiceRoll : NetworkBehaviour, IPointerClickHandler
{
    [Header("Dice Data")]
    [SyncVar] public int diceValue;
    [SyncVar] public bool isReady;
    [SyncVar] public uint ownerNetId;
    [SyncVar] public int ownerSlotIndex;
    [SyncVar] public bool isEnemyDice;

    // ===== ВЫБОР КАЖДОГО КУБИКА =====
    public int selectedCardId = -1;
    public int selectedCardIndex = -1;
    public uint selectedTargetEnemyNetId = 0;
    public int selectedTargetDiceIndex = -1;
    public bool hasSelection => selectedCardId != -1 && selectedTargetEnemyNetId != 0;

    [Header("UI")]
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private Image diceImage;
    [SerializeField] private Color readyColor = Color.green;
    [SerializeField] private Color waitingColor = Color.gray;
    [SerializeField] private Color selectedColor = Color.yellow;
    [SerializeField] private Color hasCardColor = Color.cyan; // Цвет когда выбрана карта

    private NetworkGamePlayer ownerPlayer;
    private NetworkGameEnemy ownerEnemy;
    private bool isSelected = false;
    private UIAimLine aimLine;
    void Start()
    {
        UpdateUI();
        Debug.Log($"[DiceRoll.Start] Dice {ownerSlotIndex} started. isEnemyDice: {isEnemyDice}, has AimLine: {aimLine != null}");
    }

    public void SetOwner(NetworkGamePlayer player, int slotIndex)
    {
        ownerPlayer = player;
        ownerNetId = player.netId;
        ownerSlotIndex = slotIndex;
        isEnemyDice = false;
    }

    public void SetOwner(NetworkGameEnemy enemy, int slotIndex)
    {
        ownerEnemy = enemy;
        ownerNetId = enemy.netId;
        ownerSlotIndex = slotIndex;
        isEnemyDice = true;
    }

    public void SetImageVisible(bool visible)
    {
        if (diceImage != null)
        {
            diceImage.enabled = visible;
        }
    }

    public void ShowDiceRange(int minValue, int maxValue)
    {
        if (valueText != null)
        {
            valueText.text = $"{minValue}-{maxValue}";
            valueText.color = Color.gray;
        }

        if (diceImage != null && !isSelected)
        {
            diceImage.color = waitingColor;
        }
    }
    public void SetAimLine(UIAimLine line)
    {
        aimLine = line;
    }

    public void UpdateAimLine(bool visible)
    {
        if (aimLine != null)
        {
            aimLine.gameObject.SetActive(visible);
        }
    }
    public void ShowDiceResult(int value)
    {
        if (valueText != null)
        {
            valueText.text = value.ToString();
            valueText.color = Color.green;
        }

        if (diceImage != null && !isSelected)
        {
            diceImage.color = readyColor;
        }
    }

    public void RollDice(int minValue, int maxValue)
    {
        diceValue = Random.Range(minValue, maxValue + 1);
        isReady = true;
        UpdateUI();
    }

    public void SetUIVisible(bool visible)
    {
        if (diceImage != null) diceImage.enabled = visible;
        if (valueText != null) valueText.enabled = visible;
    }
    public void ResetDice()
    {
        isReady = false;
        diceValue = 0;
        isSelected = false;
        selectedCardId = -1;
        selectedTargetEnemyNetId = 0;
        selectedTargetDiceIndex = -1;
        UpdateUI();
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateUI();
    }

    public void SetDiceValue(int value)
    {
        diceValue = value;
        isReady = true;
        UpdateUI();
    }

    // ===== МЕТОДЫ ДЛЯ ВЫБОРА =====

    private void UpdateAllHandCards()
    {
        if (ownerPlayer != null && ownerPlayer.isLocalPlayer)
        {
            LocalHandCardView[] cards = FindObjectsByType<LocalHandCardView>(FindObjectsSortMode.None);
            foreach (var card in cards)
            {
                if (card != null)
                {
                    card.UpdateCardState();
                }
            }
        }
    }
    public void SelectCard(int cardId, int cardIndex)
    {
        // ===== ИСПРАВЛЕНО: проверяем через localHandCardIds для клиента =====
        if (ownerPlayer != null)
        {
            bool cardValid = false;

            // Если это локальный игрок - проверяем localHandCardIds
            if (ownerPlayer.isLocalPlayer)
            {
                cardValid = ownerPlayer.IsCardInLocalHand(cardId, cardIndex);
            }
            else
            {
                // Для сервера - проверяем playerHand
                cardValid = (cardIndex >= 0 && cardIndex < ownerPlayer.PlayerHand.Count &&
                            ownerPlayer.PlayerHand[cardIndex] == cardId);
            }

            if (!cardValid)
            {
                Debug.Log($"[DiceRoll] Card at index {cardIndex} is no longer in hand! (localHand: {ownerPlayer.isLocalPlayer})");
                return;
            }
        }

        // ===== ЕСЛИ УЖЕ ВЫБРАНА ДРУГАЯ КАРТА - СБРАСЫВАЕМ СТАРУЮ =====
        if (selectedCardIndex != -1 && selectedCardIndex != cardIndex)
        {
            Debug.Log($"[DiceRoll] Changing card from index {selectedCardIndex} to {cardIndex}");
            selectedCardId = -1;
            selectedCardIndex = -1;
            if (aimLine != null)
            {
                aimLine.SetCardSelected(false);
            }
        }

        // Проверяем, не занята ли карта другим кубиком
        if (ownerPlayer != null)
        {
            DiceRoll[] dices = ownerPlayer.UIObject.GetComponentsInChildren<DiceRoll>(true);
            foreach (var dice in dices)
            {
                if (dice != null && dice != this && dice.selectedCardIndex == cardIndex)
                {
                    Debug.Log($"[DiceRoll] Card at index {cardIndex} is already selected by another dice! Reassigning.");
                    dice.ClearSelection();
                    break;
                }
            }
        }

        selectedCardId = cardId;
        selectedCardIndex = cardIndex;
        UpdateUI();

        UpdateAllHandCards();

        // Обновляем UIAimLine
        if (aimLine != null)
        {
            aimLine.SetPlayerDice(this);
            aimLine.SetCardSelected(true);

            if (selectedTargetEnemyNetId != 0)
            {
                foreach (var enemy in NetworkGameEnemy.AllEnemies)
                {
                    if (enemy != null && enemy.netId == selectedTargetEnemyNetId)
                    {
                        DiceRoll[] enemyDices = enemy.GetComponentsInChildren<DiceRoll>();
                        if (selectedTargetDiceIndex >= 0 && selectedTargetDiceIndex < enemyDices.Length)
                        {
                            aimLine.SetTarget(enemyDices[selectedTargetDiceIndex]);
                            break;
                        }
                    }
                }
            }
        }

        Debug.Log($"[DiceRoll] Dice {ownerSlotIndex} selected card: {cardId} at index {cardIndex}, target saved: {selectedTargetEnemyNetId != 0}");
    }


    public void SelectTarget(uint enemyNetId, int diceIndex)
    {
        selectedTargetEnemyNetId = enemyNetId;
        selectedTargetDiceIndex = diceIndex;
        UpdateUI();

        Debug.Log($"[DiceRoll.SelectTarget] Dice {ownerSlotIndex}: saved EnemyNetId={enemyNetId}, DiceIndex={diceIndex}");
        Debug.Log($"[DiceRoll.SelectTarget] OwnerEnemy NetId check: ownerEnemy?.netId={ownerEnemy?.netId}");

        // ===== ОБНОВЛЯЕМ UIAimLine для ЭТОГО кубика =====
        if (aimLine != null)
        {
            aimLine.SetPlayerDice(this);

            bool foundTarget = false;
            foreach (var enemy in NetworkGameEnemy.AllEnemies)
            {
                if (enemy != null && enemy.netId == enemyNetId)
                {
                    DiceRoll[] enemyDices = enemy.GetComponentsInChildren<DiceRoll>();
                    Debug.Log($"[DiceRoll.SelectTarget] Found enemy: {enemy.EnemyName}, dices: {enemyDices.Length}");

                    if (enemyDices != null && diceIndex < enemyDices.Length)
                    {
                        aimLine.SetTarget(enemyDices[diceIndex]);
                        aimLine.SetCardSelected(true);
                        foundTarget = true;
                        Debug.Log($"[DiceRoll.SelectTarget] AimLine target set to enemy dice {diceIndex}");
                        break;
                    }
                }
            }

            if (!foundTarget)
            {
                Debug.LogWarning($"[DiceRoll.SelectTarget] Could not find enemy dice for NetId={enemyNetId}, Index={diceIndex}");
            }
        }

        Debug.Log($"[DiceRoll.SelectTarget] hasSelection={hasSelection}");
    }

    public void ClearSelection()
    {
        Debug.Log($"[DiceRoll] ClearSelection called for dice {ownerSlotIndex}");

        selectedCardId = -1;
        selectedCardIndex = -1;
        selectedTargetEnemyNetId = 0;
        selectedTargetDiceIndex = -1;
        UpdateUI();

        UpdateAllHandCards();

        // ===== ОЧИЩАЕМ UIAimLine =====
        if (aimLine != null)
        {
            aimLine.ClearAimData();
        }

        Debug.Log($"[DiceRoll] ClearSelection complete");
    }
    void OnEnable()
    {
        Debug.Log($"[DiceRoll] Dice {ownerSlotIndex} enabled");
    }

    void OnDisable()
    {
        Debug.Log($"[DiceRoll] Dice {ownerSlotIndex} disabled");
    }

    private void UpdateUI()
    {
        if (valueText != null)
        {
            valueText.text = isReady ? diceValue.ToString() : "?";
        }

        if (diceImage != null)
        {
            if (isSelected)
            {
                diceImage.color = selectedColor;
            }
            else if (hasSelection)
            {
                // Если есть выбор карты и цели - показываем специальный цвет
                diceImage.color = hasCardColor;
            }
            else
            {
                diceImage.color = isReady ? readyColor : waitingColor;
            }
        }
    }


    public void OnPointerClick(PointerEventData eventData)
    {
        if (ownerPlayer != null && !ownerPlayer.isLocalPlayer) return;
        if (FightManager.Instance == null || FightManager.Instance.CurrentState != FightState.Rolling) return;

        if (ownerPlayer != null && ownerPlayer.isLocalPlayer)
        {
            // Выбираем этот кубик как активный
            DiceSelectionManager.Instance.SelectPlayerDice(this);
            ownerPlayer.UpdateHandVisibility();

            // ===== Активируем UIAimLine при выборе кубика =====
            if (aimLine != null)
            {
                aimLine.SetPlayerDice(this);

                // Если уже есть выбранная карта - показываем линию
                if (selectedCardId != -1)
                {
                    aimLine.SetCardSelected(true);
                }

                // Если уже есть выбранная цель - показываем линию до цели
                if (selectedTargetEnemyNetId != 0)
                {
                    foreach (var enemy in NetworkGameEnemy.AllEnemies)
                    {
                        if (enemy != null && enemy.netId == selectedTargetEnemyNetId)
                        {
                            DiceRoll[] enemyDices = enemy.GetComponentsInChildren<DiceRoll>();
                            if (selectedTargetDiceIndex >= 0 && selectedTargetDiceIndex < enemyDices.Length)
                            {
                                aimLine.SetTarget(enemyDices[selectedTargetDiceIndex]);
                                break;
                            }
                        }
                    }
                }
            }

            Debug.Log($"[DiceRoll] Selected player dice {ownerSlotIndex}");
        }
        else if (isEnemyDice)
        {
            // ===== ИСПРАВЛЕНО: Получаем локального игрока =====
            NetworkGamePlayer localPlayer = null;

            // Способ 1: через NetworkClient
            if (NetworkClient.connection != null && NetworkClient.connection.identity != null)
            {
                localPlayer = NetworkClient.connection.identity.GetComponent<NetworkGamePlayer>();
            }

            // Способ 2: через AllPlayers
            if (localPlayer == null)
            {
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    if (player != null && player.isLocalPlayer)
                    {
                        localPlayer = player;
                        break;
                    }
                }
            }

            if (localPlayer == null)
            {
                Debug.LogError("[DiceRoll] Local player not found!");
                return;
            }

            DiceRoll activeDice = DiceSelectionManager.Instance.GetSelectedPlayerDice();
            if (activeDice == null)
            {
                Debug.Log("[DiceRoll] Select your dice first!");
                return;
            }

            if (activeDice.selectedCardId == -1)
            {
                Debug.Log("[DiceRoll] Select a card first!");
                return;
            }

            // Сохраняем цель локально
            activeDice.SelectTarget(ownerNetId, ownerSlotIndex);

            // ===== ОТПРАВЛЯЕМ КОМАНДУ ЧЕРЕЗ ИГРОКА =====
            localPlayer.CmdSyncDiceSelection(
                activeDice.ownerSlotIndex,
                activeDice.selectedCardId,
                activeDice.selectedCardIndex,
                ownerNetId,
                ownerSlotIndex
            );
            Debug.Log($"[DiceRoll] Sent CmdSyncDiceSelection via player. Dice: {activeDice.ownerSlotIndex}, Card: {activeDice.selectedCardId}, Target: {ownerNetId}");

            // Линия для UI
            UIAimLine aimLine = activeDice.GetComponentInChildren<UIAimLine>();
            if (aimLine != null)
            {
                aimLine.SetPlayerDice(activeDice);
                aimLine.SetTarget(this);
                aimLine.SetCardSelected(true);
            }

            Debug.Log($"[DiceRoll] Enemy dice {ownerSlotIndex} (NetId: {ownerNetId}) selected as target for player dice {activeDice.ownerSlotIndex}");
        }
    }
}