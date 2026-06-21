using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static DataGame;


public class LocalHandCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private RectTransform cardRect;

    private Vector2 defaultAnchoredPosition;
    private readonly Vector2 hoverOffset = new Vector2(0f, 70f);
    private bool initialized;

    private int cardId;
    private int cardIndex;
    private NetworkGamePlayer player;

    // ===== НОВЫЕ ПОЛЯ =====
    private Image cardBackground;
    private Color defaultColor = new Color(0.13f, 0.14f, 0.2f, 0.96f);
    private Color selectedColor = new Color(0.2f, 0.6f, 1f, 0.96f); // Синий когда выбрана
    private Color usedColor = new Color(0.5f, 0.5f, 0.5f, 0.5f); // Серый когда использована

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

    public void UpdateCardState()
    {
        if (player == null || cardBackground == null) return;

        // Проверяем, есть ли карта в руке по индексу
        if (cardIndex >= player.PlayerHand.Count || player.PlayerHand[cardIndex] != cardId)
        {
            cardBackground.color = usedColor;
            return;
        }

        bool isSelected = false;
        DiceRoll selectedDice = DiceSelectionManager.Instance.GetSelectedPlayerDice();

        // Проверяем, не выбрана ли ЭТА КОНКРЕТНАЯ карта (по индексу)
        if (selectedDice != null && selectedDice.selectedCardIndex == cardIndex)
        {
            isSelected = true;
            cardBackground.color = selectedColor;
            return;
        }

        // Проверяем, не выбрана ли эта карта другим кубиком
        foreach (var p in NetworkGamePlayer.AllPlayers)
        {
            if (p != null && p.isLocalPlayer)
            {
                DiceRoll[] dices = p.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var dice in dices)
                {
                    if (dice != null && dice.selectedCardIndex == cardIndex)
                    {
                        isSelected = true;
                        cardBackground.color = usedColor;
                        return;
                    }
                }
            }
        }

        // Если не выбрана - возвращаем стандартный цвет
        if (!isSelected)
        {
            cardBackground.color = defaultColor;
        }
    }


    public void OnPointerEnter(PointerEventData eventData)
    {
        CacheDefaults();

        // Не поднимаем карту если она использована
        if (player != null && !player.PlayerHand.Contains(cardId)) return;

        if (cardRect != null)
        {
            cardRect.SetAsLastSibling();
            cardRect.anchoredPosition = defaultAnchoredPosition + hoverOffset;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (cardRect != null)
        {
            cardRect.anchoredPosition = defaultAnchoredPosition;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (player == null) return;
        if (FightManager.Instance == null) return;
        if (!player.isLocalPlayer) return;
        if (FightManager.Instance.CurrentState != FightState.Rolling) return;

        // Проверяем, что карта в руке по индексу
        if (cardIndex >= player.PlayerHand.Count || player.PlayerHand[cardIndex] != cardId)
        {
            Debug.Log($"[LocalHandCardView] Card at index {cardIndex} is no longer in hand!");
            return;
        }

        DiceRoll activeDice = DiceSelectionManager.Instance.GetSelectedPlayerDice();
        if (activeDice == null)
        {
            Debug.Log("[LocalHandCardView] Select your dice first!");
            return;
        }

        // Если карта уже выбрана этим кубиком - снимаем выбор
        if (activeDice.selectedCardIndex == cardIndex)
        {
            Debug.Log($"[LocalHandCardView] Deselecting card at index {cardIndex}");
            activeDice.ClearSelection();
            UIAimLine AimLine = activeDice.GetComponentInChildren<UIAimLine>();
            if (AimLine != null)
            {
                AimLine.SetCardSelected(false);
            }
            UpdateAllCards();
            return;
        }

        DataGame.CardData card = player.GetCardData(cardId);
        if (card == null) return;

        if (player.currentLight < card.lightCost)
        {
            Debug.Log($"[LocalHandCardView] Not enough Light! Need {card.lightCost}, have {player.currentLight}");
            return;
        }

        // ===== ПРОВЕРЯЕМ, НЕ ЗАНЯТА ЛИ КАРТА ДРУГИМ КУБИКОМ =====
        foreach (var p in NetworkGamePlayer.AllPlayers)
        {
            if (p != null && p.isLocalPlayer)
            {
                DiceRoll[] dices = p.UIObject.GetComponentsInChildren<DiceRoll>();
                foreach (var dice in dices)
                {
                    if (dice != null && dice != activeDice && dice.selectedCardIndex == cardIndex)
                    {
                        // Сбрасываем выбор у старого кубика
                        Debug.Log($"[LocalHandCardView] Card at index {cardIndex} was selected by another dice, reassigning to current dice");
                        dice.ClearSelection();
                        // Сбрасываем линию у старого кубика
                        UIAimLine oldLine = dice.GetComponentInChildren<UIAimLine>();
                        if (oldLine != null)
                        {
                            oldLine.SetCardSelected(false);
                        }
                        break;
                    }
                }
            }
        }

        // Выбираем карту для активного кубика
        activeDice.SelectCard(cardId, cardIndex);

        // Показываем линию у активного кубика
        UIAimLine aimLine = activeDice.GetComponentInChildren<UIAimLine>();
        if (aimLine != null)
        {
            aimLine.SetPlayerDice(activeDice);
            aimLine.SetCardSelected(true);
        }

        // Обновляем состояние всех карт
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
        // Обновляем все карты в руке
        if (player != null && player.isLocalPlayer)
        {
            // Находим все карты в UI
            LocalHandCardView[] cards = FindObjectsByType<LocalHandCardView>(FindObjectsSortMode.None);
            foreach (var card in cards)
            {
                if (card != null && card.player == player)
                {
                    card.UpdateCardState();
                }
            }
        }
    }
}