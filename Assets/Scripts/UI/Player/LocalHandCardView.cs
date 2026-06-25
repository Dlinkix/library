using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static DataGame;

public class LocalHandCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private RectTransform cardRect;
    [SerializeField] private Vector2 hoverOffset = new Vector2(0f, 40f);
    private Vector2 defaultAnchoredPosition;
    private bool initialized;

    private int cardId;
    private int cardIndex;
    private NetworkGamePlayer player;

    private Image cardBackground;
    private Color defaultColor = new Color(0.13f, 0.14f, 0.2f, 0.96f);
    private Color selectedColor = new Color(0.2f, 0.6f, 1f, 0.96f);
    private Color usedColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

    public void Setup(RectTransform targetRect, LayoutElement targetLayout, TMP_Text targetDescription, Image targetBackground, int cardId, int cardIndex, NetworkGamePlayer player)
    {
        cardRect = targetRect;
        this.cardId = cardId;
        this.cardIndex = cardIndex;
        this.player = player;
        cardBackground = targetBackground;
        if (cardBackground != null)
        {
            defaultColor = cardBackground.color;
        }
        CacheDefaults();
    }

    void Start()
    {
        if (player != null)
        {
            player.SetHandBackgroundVisible(false);
            player.SetHandCounterVisible(false);
        }
    }

    public void UpdateCardState()
    {
        if (player == null || cardBackground == null) return;

        if (player.UIObject == null) return;

        if (!player.IsCardInLocalHand(cardId, cardIndex))
        {
            cardBackground.color = usedColor;
            return;
        }

        DiceRoll selectedDice = DiceSelectionManager.Instance?.GetSelectedPlayerDice();
        if (selectedDice != null && selectedDice.selectedCardIndex == cardIndex)
        {
            cardBackground.color = selectedColor;
            return;
        }

        foreach (var p in NetworkGamePlayer.AllPlayers)
        {
            if (p == null) continue;
            DiceRoll[] dices = p.UIObject.GetComponentsInChildren<DiceRoll>();
            foreach (var dice in dices)
            {
                if (dice != null && dice.selectedCardIndex == cardIndex)
                {
                    cardBackground.color = usedColor;
                    return;
                }
            }
        }

        cardBackground.color = defaultColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        CacheDefaults();
        if (player != null && !player.IsCardInLocalHand(cardId, cardIndex)) return;

        if (cardRect != null)
        {
            cardRect.SetAsLastSibling();
            cardRect.anchoredPosition = defaultAnchoredPosition + hoverOffset;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (cardRect != null) cardRect.anchoredPosition = defaultAnchoredPosition;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (player == null || FightManager.Instance == null || !player.isLocalPlayer) return;
        if (FightManager.Instance.CurrentState != FightState.Rolling) return;

        if (!player.IsCardInLocalHand(cardId, cardIndex)) return;

        DiceRoll activeDice = DiceSelectionManager.Instance.GetSelectedPlayerDice();
        if (activeDice == null) return;

        if (activeDice.selectedCardIndex == cardIndex)
        {
            activeDice.ClearSelection();
            player.HideCardView();
            UpdateAllCards();
            return;
        }

        DataGame.CardData card = player.GetCardData(cardId);
        if (card == null) return;

        if (player.currentLight < card.lightCost) return;

        foreach (var p in NetworkGamePlayer.AllPlayers)
        {
            if (p != null && p.isLocalPlayer)
            {
                DiceRoll[] dices = p.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var dice in dices)
                {
                    if (dice != null && dice != activeDice && dice.selectedCardIndex == cardIndex)
                    {
                        dice.ClearSelection();
                        break;
                    }
                }
            }
        }

        activeDice.SelectCard(cardId, cardIndex);

        DiceRoll enemyDice = DiceSelectionManager.Instance.GetSelectedEnemyDice();
        if (enemyDice != null)
        {
            activeDice.SelectTarget(enemyDice.ownerNetId, enemyDice.ownerSlotIndex);
            player.CmdSyncDiceSelection(activeDice.ownerSlotIndex, cardId, cardIndex, enemyDice.ownerNetId, enemyDice.ownerSlotIndex);
        }

        UIAimLine aimLine = activeDice.GetComponentInChildren<UIAimLine>();
        if (aimLine != null)
        {
            aimLine.SetPlayerDice(activeDice);
            aimLine.SetCardSelected(true);
            if (enemyDice != null) aimLine.SetTarget(enemyDice);
        }

        UpdateAllCards();
    }

    private void CacheDefaults()
    {
        if (initialized || cardRect == null) return;
        defaultAnchoredPosition = cardRect.anchoredPosition;
        initialized = true;
    }

    private void UpdateAllCards()
    {
        if (player != null && player.isLocalPlayer)
        {
            LocalHandCardView[] cards = FindObjectsByType<LocalHandCardView>(FindObjectsSortMode.None);
            foreach (var card in cards)
            {
                if (card != null && card.player == player) card.UpdateCardState();
            }
        }
    }
}