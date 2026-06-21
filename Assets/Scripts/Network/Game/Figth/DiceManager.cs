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

    void Update()
    {
        // Сброс выбора по правой кнопке мыши
        if (Input.GetMouseButtonDown(1))
        {
            ClearSelection();
        }
    }

    public void SelectPlayerDice(DiceRoll dice)
    {
        if (selectedPlayerDice != null)
        {
            selectedPlayerDice.SetSelected(false);
        }

        selectedPlayerDice = dice;
        selectedPlayerDice.SetSelected(true);
        OnPlayerDiceSelected?.Invoke(dice);

        // ===== НАХОДИМ ЛИНИЮ И УСТАНАВЛИВАЕМ selectedPlayerDice =====
        UIAimLine aimLine = dice.GetComponentInChildren<UIAimLine>();
        if (aimLine != null)
        {
            aimLine.SetPlayerDice(dice);
            // Если уже есть выбранная карта - показываем линию
            if (dice.selectedCardId != -1)
            {
                aimLine.SetCardSelected(true);
                aimLine.gameObject.SetActive(true);
            }
        }

        UpdateHandVisibilityForAllPlayers();
        Debug.Log($"Selected player dice: {dice.diceValue}");
    }

    public void SelectEnemyDice(DiceRoll dice)
    {
        if (selectedEnemyDice != null)
        {
            selectedEnemyDice.SetSelected(false);
        }

        selectedEnemyDice = dice;
        selectedEnemyDice.SetSelected(true);
        OnEnemyDiceSelected?.Invoke(dice);
    }




    public void ClearSelection()
    {
        if (selectedPlayerDice != null)
        {
            selectedPlayerDice.SetSelected(false);

            // Скрываем линию
            UIAimLine aimLine = selectedPlayerDice.GetComponentInChildren<UIAimLine>();
            if (aimLine != null)
            {
                aimLine.SetCardSelected(false);
                aimLine.gameObject.SetActive(false);
            }

            selectedPlayerDice = null;
        }

        if (selectedEnemyDice != null)
        {
            selectedEnemyDice.SetSelected(false);
            selectedEnemyDice = null;
        }

        OnEnemyDiceSelected?.Invoke(null);
        UpdateHandVisibilityForAllPlayers();
    }

    private void UpdateHandVisibilityForAllPlayers()
    {
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.isLocalPlayer)
            {
                player.UpdateHandVisibility();
            }
        }
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