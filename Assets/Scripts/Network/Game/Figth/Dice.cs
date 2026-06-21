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
    public void SelectCard(int cardId)
    {
        selectedCardId = cardId;
        UpdateUI();

        // ===== ПРИВЯЗЫВАЕМ ЛИНИЮ =====
        UIAimLine aimLine = GetComponentInChildren<UIAimLine>();
        if (aimLine != null)
        {
            aimLine.SetPlayerDice(this);
            aimLine.SetCardSelected(true);
            aimLine.gameObject.SetActive(true);
        }

        Debug.Log($"[DiceRoll] Dice {ownerSlotIndex} selected card: {cardId}");
    }

    public void SelectTarget(uint enemyNetId, int diceIndex)
    {
        selectedTargetEnemyNetId = enemyNetId;
        selectedTargetDiceIndex = diceIndex;
        UpdateUI();

        // ===== ПРИВЯЗЫВАЕМ ЛИНИЮ И УСТАНАВЛИВАЕМ ЦЕЛЬ =====
        UIAimLine aimLine = GetComponentInChildren<UIAimLine>();
        if (aimLine != null)
        {
            aimLine.SetPlayerDice(this);

            // Находим вражеский кубик
            foreach (var enemy in NetworkGameEnemy.AllEnemies)
            {
                if (enemy != null && enemy.netId == enemyNetId)
                {
                    DiceRoll[] enemyDices = enemy.GetComponentsInChildren<DiceRoll>();
                    if (enemyDices != null && diceIndex < enemyDices.Length)
                    {
                        aimLine.SetTarget(enemyDices[diceIndex]);
                        aimLine.SetCardSelected(true);
                        aimLine.gameObject.SetActive(true);
                        break;
                    }
                }
            }
        }

        Debug.Log($"[DiceRoll] Dice {ownerSlotIndex} selected target: {enemyNetId}, dice: {diceIndex}");
    }

    public void ClearSelection()
    {
        selectedCardId = -1;
        selectedTargetEnemyNetId = 0;
        selectedTargetDiceIndex = -1;
        UpdateUI();
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
        }
        else if (isEnemyDice)
        {
            NetworkGamePlayer localPlayer = NetworkClient.connection.identity.GetComponent<NetworkGamePlayer>();
            if (localPlayer == null) return;

            DiceRoll activeDice = DiceSelectionManager.Instance.GetSelectedPlayerDice();
            if (activeDice == null)
            {
                Debug.Log("[DiceRoll] Select your dice first!");
                return;
            }

            int selectedCardId = activeDice.selectedCardId;
            if (selectedCardId == -1)
            {
                Debug.Log("[DiceRoll] Select a card first!");
                return;
            }

            DataGame.CardData card = localPlayer.GetCardData(selectedCardId);
            if (card == null) return;

            if (localPlayer.currentLight < card.lightCost)
            {
                Debug.Log($"[DiceRoll] Not enough Light! Need {card.lightCost}, have {localPlayer.currentLight}");
                return;
            }

            // ===== ВАЖНО: Устанавливаем вражеский кубик в DiceSelectionManager =====
            DiceSelectionManager.Instance.SelectEnemyDice(this);

            // Сохраняем цель в активном кубике
            activeDice.SelectTarget(ownerNetId, ownerSlotIndex);

            // Находим линию у активного кубика
            UIAimLine aimLine = activeDice.GetComponentInChildren<UIAimLine>();
            if (aimLine != null)
            {
                aimLine.SetPlayerDice(activeDice);
                aimLine.SetTarget(this);
                aimLine.SetCardSelected(true);
                aimLine.gameObject.SetActive(true);
            }
        }
    }
}