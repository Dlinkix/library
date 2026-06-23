using Mirror;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardView : MonoBehaviour
{
    [Header("Card Info")]
    [SerializeField] private TMP_Text cardNameText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private Image cardArt;

    [Header("Attack Dice")]
    [SerializeField] private Transform attackGrid; // Grid контейнер
    [SerializeField] private Transform dicePlaceholder; // Специальное место для активного кубика
    [SerializeField] private GameObject diceAttackPrefab;
    [SerializeField] private int maxDiceCount = 6;

    private int cardId;
    private DataGame.CardData cardData;
    private List<DiceAttackRoll> attackDices = new List<DiceAttackRoll>();
    private int activeDiceCount = 0;
    private int currentActiveDiceIndex = -1; // Индекс активного кубика
    private int currentCardIndex = -1;

    void Awake()
    {
        gameObject.SetActive(false);
        CreateDicePool();
    }

    private void CreateDicePool()
    {
        if (attackGrid == null || diceAttackPrefab == null) return;

        foreach (Transform child in attackGrid)
            Destroy(child.gameObject);
        attackDices.Clear();

        for (int i = 0; i < maxDiceCount; i++)
        {
            GameObject diceObj = Instantiate(diceAttackPrefab, attackGrid);
            DiceAttackRoll dice = diceObj.GetComponent<DiceAttackRoll>();
            if (dice != null)
            {
                dice.gameObject.SetActive(false);
                attackDices.Add(dice);
            }
        }
    }

    public void SetupCard(DataGame.CardData data)
    {
        if (data == null)
        {
            HideCardView();
            return;
        }

        cardData = data;
        cardId = data.cardId;
        currentActiveDiceIndex = -1;

        if (cardNameText != null) cardNameText.text = data.cardName;
        if (costText != null) costText.text = $"Light: {data.lightCost}";
        if (descText != null) descText.text = data.GetShortDescription();
        if (cardArt != null) cardArt.sprite = data.cardSprite;

        UpdateAttackDices(data);
    }

    private void UpdateAttackDices(DataGame.CardData data)
    {
        foreach (var dice in attackDices)
        {
            dice.gameObject.SetActive(false);
            // Возвращаем все кубики в Grid
            dice.transform.SetParent(attackGrid);
        }

        activeDiceCount = 0;

        if (data.attacks == null || data.attacks.Length == 0)
        {
            if (attackGrid != null) attackGrid.gameObject.SetActive(false);
            return;
        }

        if (attackGrid != null) attackGrid.gameObject.SetActive(true);

        int attackCount = data.attacks.Length;

        if (attackCount > attackDices.Count)
        {
            int needMore = attackCount - attackDices.Count;
            for (int i = 0; i < needMore; i++)
            {
                GameObject diceObj = Instantiate(diceAttackPrefab, attackGrid);
                DiceAttackRoll dice = diceObj.GetComponent<DiceAttackRoll>();
                if (dice != null)
                {
                    dice.gameObject.SetActive(false);
                    attackDices.Add(dice);
                }
            }
            Debug.Log($"[CardView] Added {needMore} more dice, total: {attackDices.Count}");
        }

        for (int i = 0; i < attackCount && i < attackDices.Count; i++)
        {
            DiceAttackRoll dice = attackDices[i];
            dice.gameObject.SetActive(true);
            dice.Setup(data.attacks[i]);
            activeDiceCount++;
        }
    }

    public void UpdateAttackDiceValues(int[] rollValues)
    {
        int count = Mathf.Min(rollValues.Length, activeDiceCount);
        for (int i = 0; i < count; i++)
        {
            if (i < attackDices.Count && attackDices[i].gameObject.activeSelf)
            {
                attackDices[i].SetRollResult(rollValues[i]);
            }
        }
    }

    // ===== ПЕРЕМЕЩАЕМ КУБИК В ПЛЕЙСХОЛДЕР =====
    public void MoveDiceToPlaceholder(int attackIndex)
    {
        if (attackIndex < 0 || attackIndex >= attackDices.Count) return;
        if (dicePlaceholder == null)
        {
            Debug.LogWarning("[CardView] DicePlaceholder is null!");
            return;
        }

        DiceAttackRoll dice = attackDices[attackIndex];
        if (dice == null || !dice.gameObject.activeSelf) return;

        // Перемещаем кубик в плейсхолдер
        dice.transform.SetParent(dicePlaceholder);
        dice.transform.localPosition = Vector3.zero;
        dice.transform.localRotation = Quaternion.identity;

        // ===== МЕНЯЕМ РАЗМЕР =====
        RectTransform rect = dice.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(30f, 30f); // Увеличиваем для наглядности
            rect.localScale = Vector3.one;
        }

        // ===== МЕНЯЕМ ЦВЕТ НА АКТИВНЫЙ =====
        dice.SetActiveState(true);

        currentActiveDiceIndex = attackIndex;
    }

    // ===== ВОЗВРАЩАЕМ КУБИК ИЗ ПЛЕЙСХОЛДЕРА В ГРИД =====
    public void ReturnDiceToGrid()
    {
        if (currentActiveDiceIndex < 0 || currentActiveDiceIndex >= attackDices.Count) return;

        DiceAttackRoll dice = attackDices[currentActiveDiceIndex];
        if (dice == null) return;

        // Возвращаем в Grid
        dice.transform.SetParent(attackGrid);

        // ===== ВОССТАНАВЛИВАЕМ РАЗМЕР =====
        RectTransform rect = dice.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(23f, 23f); // Исходный размер для Grid
            rect.localScale = Vector3.one;
        }

        // ===== ВОЗВРАЩАЕМ ОРИГИНАЛЬНЫЙ ЦВЕТ =====
        dice.SetActiveState(false);

        // Выключаем кубик (атака выполнена)
        dice.gameObject.SetActive(false);

        Debug.Log($"[CardView] Returned dice {currentActiveDiceIndex} to grid - size: 50x50, color: original, disabled");
        currentActiveDiceIndex = -1;
    }

    // ===== ВЫКЛЮЧАЕМ КОНКРЕТНЫЙ КУБИК =====
    public void DisableAttackDice(int attackIndex)
    {
        if (attackIndex < 0 || attackIndex >= attackDices.Count) return;

        DiceAttackRoll dice = attackDices[attackIndex];
        if (dice != null && dice.gameObject.activeSelf)
        {
            // Если кубик в плейсхолдере - сначала возвращаем
            if (dice.transform.parent == dicePlaceholder)
            {
                dice.transform.SetParent(attackGrid);
            }
            dice.gameObject.SetActive(false);
            Debug.Log($"[CardView] Disabled attack dice {attackIndex}");
        }
    }

    public void DisableAllAttackDices()
    {
        foreach (var dice in attackDices)
        {
            if (dice != null && dice.gameObject.activeSelf)
            {
                // Возвращаем все в Grid если они в плейсхолдере
                if (dice.transform.parent == dicePlaceholder)
                {
                    dice.transform.SetParent(attackGrid);
                }
                dice.gameObject.SetActive(false);
            }
        }
        activeDiceCount = 0;
        currentActiveDiceIndex = -1;
        Debug.Log("[CardView] Disabled all attack dices");
    }

    public void ResetAttackDices()
    {
        foreach (var dice in attackDices)
        {
            if (dice.gameObject.activeSelf)
            {
                dice.ResetDice();
            }
        }
    }

    public void ShowCardView()
    {
        gameObject.SetActive(true);
    }

    public void HideCardView()
    {
        gameObject.SetActive(false);
        DisableAllAttackDices();
    }
}