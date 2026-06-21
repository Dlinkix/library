using UnityEngine;

public class DiceSelectionManager : MonoBehaviour
{
    public static DiceSelectionManager Instance { get; private set; }
    public static System.Action<DiceRoll> OnPlayerDiceSelected;
    public static System.Action<DiceRoll> OnEnemyDiceSelected;

    private DiceRoll selectedPlayerDice;
    private DiceRoll selectedEnemyDice;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void SelectPlayerDice(DiceRoll dice)
    {
        if (selectedPlayerDice != null)
            selectedPlayerDice.SetSelected(false);

        selectedPlayerDice = dice;
        selectedPlayerDice.SetSelected(true);
        OnPlayerDiceSelected?.Invoke(dice);

        Debug.Log($"Selected player dice: {dice.diceValue}");
    }

    public void SelectEnemyDice(DiceRoll dice)
    {
        if (selectedPlayerDice == null)
        {
            Debug.Log("Select player dice first!");
            return;
        }

        if (!dice.isEnemyDice)
        {
            Debug.Log("This is not an enemy dice!");
            return;
        }

        selectedEnemyDice = dice;
        OnEnemyDiceSelected?.Invoke(dice);

        Debug.Log($"Selected enemy dice: {dice.diceValue}");
    }

    public void ClearSelection()
    {
        if (selectedPlayerDice != null)
        {
            selectedPlayerDice.SetSelected(false);
            selectedPlayerDice = null;
        }
        selectedEnemyDice = null;
        OnEnemyDiceSelected?.Invoke(null);
    }

    public DiceRoll GetSelectedPlayerDice()
    {
        return selectedPlayerDice;
    }

    public DiceRoll GetSelectedEnemyDice()
    {
        return selectedEnemyDice;
    }
}