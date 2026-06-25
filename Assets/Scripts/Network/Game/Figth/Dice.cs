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
    [SerializeField] private Color hasCardColor = Color.cyan;

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

    public void SetImageVisible(bool visible)
    {
        if (diceImage != null) diceImage.enabled = visible;
    }

    public void ShowDiceRange(int minValue, int maxValue)
    {
        if (valueText != null)
        {
            valueText.text = $"{minValue}-{maxValue}";
            valueText.color = Color.gray;
        }
        if (diceImage != null && !isSelected && !isEnemyDice) diceImage.color = waitingColor;
    }

    public void SetAimLine(UIAimLine line) => aimLine = line;

    public void UpdateAimLine(bool visible)
    {
        if (aimLine != null) aimLine.gameObject.SetActive(visible);
    }

    public void ShowDiceResult(int value)
    {
        if (valueText != null)
        {
            valueText.text = value.ToString();
            valueText.color = Color.green;
        }
        if (diceImage != null && !isSelected && !isEnemyDice) diceImage.color = readyColor;
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

    private void UpdateAllHandCards()
    {
        if (ownerPlayer != null && ownerPlayer.isLocalPlayer)
        {
            LocalHandCardView[] cards = FindObjectsByType<LocalHandCardView>(FindObjectsSortMode.None);
            foreach (var card in cards)
            {
                if (card != null) card.UpdateCardState();
            }
        }
    }

    public void SelectCard(int cardId, int cardIndex)
    {
        if (ownerEnemy != null)
        {
            if (cardIndex < 0 || cardIndex >= ownerEnemy.enemyHand.Count) return;
            if (ownerEnemy.enemyHand[cardIndex] != cardId) return;
        }
        else if (ownerPlayer != null)
        {
            bool cardValid = ownerPlayer.isLocalPlayer
                ? ownerPlayer.IsCardInLocalHand(cardId, cardIndex)
                : (cardIndex >= 0 && cardIndex < ownerPlayer.PlayerHand.Count && ownerPlayer.PlayerHand[cardIndex] == cardId);
            if (!cardValid) return;
        }

        if (selectedCardIndex != -1 && selectedCardIndex != cardIndex)
        {
            selectedCardId = -1;
            selectedCardIndex = -1;
            if (aimLine != null) aimLine.SetCardSelected(false);
        }

        if (ownerPlayer != null)
        {
            DiceRoll[] dices = ownerPlayer.UIObject.GetComponentsInChildren<DiceRoll>(true);
            foreach (var dice in dices)
            {
                if (dice != null && dice != this && dice.selectedCardIndex == cardIndex)
                {
                    dice.ClearSelection();
                    break;
                }
            }
        }

        selectedCardId = cardId;
        selectedCardIndex = cardIndex;
        UpdateUI();
        UpdateAllHandCards();

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
    }

    public void SelectTarget(uint targetNetId, int diceIndex)
    {
        selectedTargetEnemyNetId = targetNetId;
        selectedTargetDiceIndex = diceIndex;
        UpdateUI();

        DiceRoll targetDice = null;

        if (isEnemyDice)
        {
            NetworkGamePlayer targetPlayer = null;
            foreach (var player in NetworkGamePlayer.AllPlayers)
            {
                if (player != null && player.netId == targetNetId) { targetPlayer = player; break; }
            }
            if (targetPlayer == null || targetPlayer.UIObject == null) return;

            DiceRoll[] playerDices = targetPlayer.UIObject.GetComponentsInChildren<DiceRoll>();
            foreach (var d in playerDices)
            {
                if (d != null && d.ownerSlotIndex == diceIndex) { targetDice = d; break; }
            }
        }
        else
        {
            NetworkGameEnemy targetEnemy = null;
            foreach (var enemy in NetworkGameEnemy.AllEnemies)
            {
                if (enemy != null && enemy.netId == targetNetId) { targetEnemy = enemy; break; }
            }
            if (targetEnemy == null || targetEnemy.UIObject == null) return;

            DiceRoll[] enemyDices = targetEnemy.UIObject.GetComponentsInChildren<DiceRoll>();
            foreach (var d in enemyDices)
            {
                if (d != null && d.ownerSlotIndex == diceIndex) { targetDice = d; break; }
            }
        }

        if (targetDice == null) return;

        if (aimLine != null)
        {
            aimLine.SetTarget(targetDice);
            aimLine.SetCardSelected(true);
        }
    }

    public void ClearSelection()
    {
        selectedCardId = -1;
        selectedCardIndex = -1;
        selectedTargetEnemyNetId = 0;
        selectedTargetDiceIndex = -1;
        UpdateUI();
        UpdateAllHandCards();
        if (aimLine != null) aimLine.ClearAimData();
    }

    private void UpdateUI()
    {
        if (valueText != null) valueText.text = isReady ? diceValue.ToString() : "?";
        if (diceImage != null)
        {
            if (isSelected) diceImage.color = selectedColor;
            else if (hasSelection) diceImage.color = hasCardColor;
            else if (!isEnemyDice) diceImage.color = isReady ? readyColor : waitingColor;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (ownerPlayer != null && !ownerPlayer.isLocalPlayer) return;
        if (FightManager.Instance == null || FightManager.Instance.CurrentState != FightState.Rolling) return;

        if (ownerPlayer != null && ownerPlayer.isLocalPlayer)
        {
            DiceSelectionManager.Instance.SelectPlayerDice(this);
            ownerPlayer.UpdateHandVisibility();

            if (aimLine != null)
            {
                aimLine.SetPlayerDice(this);
                if (selectedCardId != -1) aimLine.SetCardSelected(true);
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
        }
        else if (isEnemyDice)
        {
            NetworkGamePlayer localPlayer = null;
            if (NetworkClient.connection != null && NetworkClient.connection.identity != null)
                localPlayer = NetworkClient.connection.identity.GetComponent<NetworkGamePlayer>();

            if (localPlayer == null)
            {
                foreach (var player in NetworkGamePlayer.AllPlayers)
                {
                    if (player != null && player.isLocalPlayer) { localPlayer = player; break; }
                }
            }

            if (localPlayer == null) return;

            DiceRoll activeDice = DiceSelectionManager.Instance.GetSelectedPlayerDice();
            if (activeDice == null || activeDice.selectedCardId == -1) return;

            activeDice.SelectTarget(ownerNetId, ownerSlotIndex);

            localPlayer.CmdSyncDiceSelection(
                activeDice.ownerSlotIndex,
                activeDice.selectedCardId,
                activeDice.selectedCardIndex,
                ownerNetId,
                ownerSlotIndex
            );

            UIAimLine aimLine = activeDice.GetComponentInChildren<UIAimLine>();
            if (aimLine != null)
            {
                aimLine.SetPlayerDice(activeDice);
                aimLine.SetTarget(this);
                aimLine.SetCardSelected(true);
            }
        }
    }
}