using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LocalHandCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private RectTransform cardRect;

    private Vector2 defaultAnchoredPosition;
    private readonly Vector2 hoverOffset = new Vector2(0f, 70f);
    private bool initialized;

    public void Setup(RectTransform targetRect, LayoutElement targetLayout, TMP_Text targetDescription, Image targetBackground)
    {
        cardRect = targetRect;
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

    private void CacheDefaults()
    {
        if (initialized || cardRect == null)
        {
            return;
        }

        defaultAnchoredPosition = cardRect.anchoredPosition;
        initialized = true;
    }
}
