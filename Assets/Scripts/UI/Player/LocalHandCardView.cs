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
    private NetworkGamePlayer player;

    public void Setup(RectTransform targetRect, LayoutElement targetLayout, TMP_Text targetDescription, Image targetBackground, int cardId, NetworkGamePlayer player)
    {
        cardRect = targetRect;
        this.cardId = cardId;
        this.player = player;
        CacheDefaults();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        CacheDefaults();

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

        DiceRoll activeDice = DiceSelectionManager.Instance.GetSelectedPlayerDice();
        if (activeDice == null)
        {
            Debug.Log("[LocalHandCardView] Select your dice first!");
            return;
        }

        DataGame.CardData card = player.GetCardData(cardId);
        if (card == null) return;

        if (player.currentLight < card.lightCost)
        {
            Debug.Log($"[LocalHandCardView] Not enough Light! Need {card.lightCost}, have {player.currentLight}");
            return;
        }

        activeDice.SelectCard(cardId);

        // ===== ÏÎÊÀÇÛÂÀÅÌ ËÈÍÈÞ Ó ÀÊÒÈÂÍÎÃÎ ÊÓÁÈÊÀ =====
        UIAimLine aimLine = activeDice.GetComponentInChildren<UIAimLine>();
        if (aimLine != null)
        {
            aimLine.SetPlayerDice(activeDice);
            aimLine.SetCardSelected(true);
            aimLine.gameObject.SetActive(true);
        }
    }

    private void CacheDefaults()
    {
        if (initialized || cardRect == null) return;
        defaultAnchoredPosition = cardRect.anchoredPosition;
        initialized = true;
    }
}