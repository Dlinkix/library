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
    [SerializeField] private Color originalColor;

    private int minValue;
    private int maxValue;
    private int rolledValue;
    private DataGame.AttackData.Type attackType;
    private bool isRolled = false;


    void Awake()
    {
        // Сохраняем оригинальный цвет
        if (diceImage != null)
        {
            originalColor = diceImage.color;
        }
    }

    public void Setup(DataGame.AttackData attackData)
    {
        // Сохраняем данные
        minValue = attackData.RollMin;
        maxValue = attackData.RollMax;
        attackType = attackData.type;
        typeIcon.gameObject.SetActive(true);
        valueTextRoll.gameObject.SetActive(false);


        // Отображаем диапазон
        if (valueText != null)
        {
            valueText.text = $"{minValue}-{maxValue}";
            valueText.color = Color.gray;
        }

        // Устанавливаем цвет в зависимости от типа атаки
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

        // Показываем иконку типа атаки
        if (typeIcon != null)
        {
            // Можно загрузить иконку в зависимости от типа
        }

        isRolled = false;
    }

    public void SetRollResult(int value)
    {
        typeIcon.gameObject.SetActive(false );
        valueTextRoll.gameObject.SetActive(true);
        rolledValue = value;
        isRolled = true;

        if (valueTextRoll != null)
        {
            valueTextRoll.text = value.ToString();
            valueTextRoll.color = Color.white;
        }

        // Можно добавить анимацию или подсветку
        if (diceImage != null)
        {
            // Подсвечиваем результат
        }
    }
    public void SetActiveState(bool isActive)
    {
        if (diceImage != null)
        {
            if (isActive)
            {
                diceImage.color = activeColor; // Желтый для активного
            }
            else
            {
                diceImage.color = originalColor; // Возвращаем оригинальный цвет
            }
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

    public int GetRolledValue()
    {
        return isRolled ? rolledValue : 0;
    }

    public bool IsRolled()
    {
        return isRolled;
    }
}