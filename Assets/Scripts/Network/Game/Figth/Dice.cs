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

    [Header("UI")]
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private Image diceImage;
    [SerializeField] private Color readyColor = Color.green;
    [SerializeField] private Color waitingColor = Color.gray;
    [SerializeField] private Color selectedColor = Color.yellow;

    private NetworkGamePlayer ownerPlayer;
    private NetworkGameEnemy ownerEnemy;
    private bool isSelected = false;

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
        UpdateUI();
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
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
            DiceSelectionManager.Instance.SelectPlayerDice(this);
        }
        else if (isEnemyDice)
        {
            NetworkGamePlayer localPlayer = NetworkClient.connection.identity.GetComponent<NetworkGamePlayer>();
            if (localPlayer == null) return;

            int selectedCardId = localPlayer.GetSelectedCardId();
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

            // ===== —Œ’–¿Õﬂ≈Ã ÷≈À‹ =====
            localPlayer.SelectTarget(ownerNetId, ownerSlotIndex);

            // ===== Œ¡ÕŒ¬Àﬂ≈Ã UIAimLine Õ¿œ–ﬂÃ”ﬁ =====
            UIAimLine aimLine = Object.FindFirstObjectByType<UIAimLine>();
            if (aimLine != null)
            {
                // ¬€«€¬¿≈Ã SetTarget — ›“»Ã  ”¡» ŒÃ!
                aimLine.SetTarget(this);
                aimLine.SetCardSelected(true);
            }

            Debug.Log($"[DiceRoll] Target selected: Enemy {ownerNetId}, Dice {ownerSlotIndex}");
        }
    }
}