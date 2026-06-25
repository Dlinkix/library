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
    [SerializeField] private Transform attackGrid;
    [SerializeField] private Transform dicePlaceholder;
    [SerializeField] private GameObject diceAttackPrefab;
    [SerializeField] private int maxDiceCount = 6;

    private int cardId;
    private DataGame.CardData cardData;
    private List<DiceAttackRoll> attackDices = new List<DiceAttackRoll>();
    private int activeDiceCount = 0;
    private int currentActiveDiceIndex = -1;
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

    public void SetupCard(DataGame.CardData data, int cardIndex)
    {
        if (data == null)
        {
            HideCardView();
            return;
        }

        cardData = data;
        cardId = data.cardId;
        currentCardIndex = cardIndex;
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
        }

        for (int i = 0; i < attackCount && i < attackDices.Count; i++)
        {
            DiceAttackRoll dice = attackDices[i];
            dice.gameObject.SetActive(true);
            dice.Setup(data.attacks[i]);
            activeDiceCount++;
        }
    }

    public void UpdateAttackDiceValues(int cardIndex, int[] rollValues)
    {
        if (currentCardIndex != cardIndex) return;

        int count = Mathf.Min(rollValues.Length, activeDiceCount);
        for (int i = 0; i < count; i++)
        {
            if (i < attackDices.Count && attackDices[i].gameObject.activeSelf)
            {
                attackDices[i].SetRollResult(rollValues[i]);
            }
        }
    }

    public void MoveDiceToPlaceholder(int cardIndex, int attackIndex)
    {
        if (currentCardIndex != cardIndex) return;
        if (attackIndex < 0 || attackIndex >= attackDices.Count) return;
        if (dicePlaceholder == null) return;

        DiceAttackRoll dice = attackDices[attackIndex];
        if (dice == null || !dice.gameObject.activeSelf) return;

        dice.transform.SetParent(dicePlaceholder);
        dice.transform.localPosition = Vector3.zero;
        dice.transform.localRotation = Quaternion.identity;

        RectTransform rect = dice.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(30f, 30f);
            rect.localScale = Vector3.one;
        }

        dice.SetActiveState(true);
        currentActiveDiceIndex = attackIndex;
    }

    public void ReturnDiceToGrid(int cardIndex)
    {
        if (currentCardIndex != cardIndex) return;
        if (currentActiveDiceIndex < 0 || currentActiveDiceIndex >= attackDices.Count) return;

        DiceAttackRoll dice = attackDices[currentActiveDiceIndex];
        if (dice == null) return;

        dice.transform.SetParent(attackGrid);

        RectTransform rect = dice.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(23f, 23f);
            rect.localScale = Vector3.one;
        }

        dice.SetActiveState(false);
        dice.gameObject.SetActive(false);

        currentActiveDiceIndex = -1;
    }

    public void DisableAttackDice(int attackIndex)
    {
        if (attackIndex < 0 || attackIndex >= attackDices.Count) return;

        DiceAttackRoll dice = attackDices[attackIndex];
        if (dice != null && dice.gameObject.activeSelf)
        {
            if (dice.transform.parent == dicePlaceholder)
            {
                dice.transform.SetParent(attackGrid);
            }
            dice.gameObject.SetActive(false);
        }
    }

    public void DisableAllAttackDices()
    {
        foreach (var dice in attackDices)
        {
            if (dice != null && dice.gameObject.activeSelf)
            {
                if (dice.transform.parent == dicePlaceholder)
                {
                    dice.transform.SetParent(attackGrid);
                }
                dice.gameObject.SetActive(false);
            }
        }
        activeDiceCount = 0;
        currentActiveDiceIndex = -1;
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