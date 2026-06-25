using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DiceAttackRoll : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private TMP_Text valueTextRoll;
    [SerializeField] private Image diceImage;
    [SerializeField] private Image typeIcon;

    [Header("Colors")]
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private Color blockColor = Color.blue;
    [SerializeField] private Color escapeColor = Color.green;
    [SerializeField] private Color defaultColor = Color.gray;
    [SerializeField] private Color activeColor = Color.yellow;

    private int minValue;
    private int maxValue;
    private int rolledValue;
    private DataGame.AttackData.Type attackType;
    private bool isRolled = false;
    private Color originalColor;

    void Awake()
    {
        if (diceImage != null)
        {
            originalColor = diceImage.color;
        }
    }

    public void Setup(DataGame.AttackData attackData)
    {
        minValue = attackData.RollMin;
        maxValue = attackData.RollMax;
        attackType = attackData.type;
        typeIcon.gameObject.SetActive(true);
        valueTextRoll.gameObject.SetActive(false);

        if (valueText != null)
        {
            valueText.text = $"{minValue}-{maxValue}";
            valueText.color = Color.gray;
        }

        if (diceImage != null)
        {
            switch (attackType)
            {
                case DataGame.AttackData.Type.Damage:
                    diceImage.color = damageColor;
                    break;
                case DataGame.AttackData.Type.Block:
                    diceImage.color = blockColor;
                    break;
                case DataGame.AttackData.Type.Escape:
                    diceImage.color = escapeColor;
                    break;
                default:
                    diceImage.color = defaultColor;
                    break;
            }
        }

        isRolled = false;
    }

    public void SetRollResult(int value)
    {
        typeIcon.gameObject.SetActive(false);
        valueTextRoll.gameObject.SetActive(true);
        rolledValue = value;
        isRolled = true;

        if (valueTextRoll != null)
        {
            valueTextRoll.text = value.ToString();
            valueTextRoll.color = Color.white;
        }
    }

    public void SetActiveState(bool isActive)
    {
        if (diceImage != null)
        {
            diceImage.color = isActive ? activeColor : originalColor;
        }
    }

    public void ResetDice()
    {
        isRolled = false;
        typeIcon.gameObject.SetActive(true);
        valueTextRoll.gameObject.SetActive(false);
        if (valueText != null)
        {
            valueText.text = $"{minValue}-{maxValue}";
            valueText.color = Color.gray;
        }
    }

    public int GetRolledValue() => isRolled ? rolledValue : 0;
    public bool IsRolled() => isRolled;
}