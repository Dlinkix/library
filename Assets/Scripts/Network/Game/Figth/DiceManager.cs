using Mirror;
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

        // Показываем линию у выбранного кубика
        UIAimLine aimLine = dice.GetComponentInChildren<UIAimLine>();
        if (aimLine != null && dice.selectedCardId != -1)
        {
            aimLine.SetCardSelected(true);
        }

        // ===== ИЩЕМ ЛОКАЛЬНОГО ИГРОКА БЕЗОПАСНО =====
        NetworkGamePlayer localPlayer = null;
        foreach (var player in NetworkGamePlayer.AllPlayers)
        {
            if (player != null && player.isLocalPlayer)
            {
                localPlayer = player;
                break;
            }
        }

        if (localPlayer != null)
        {
            localPlayer.EnsureLocalHandUI();
            localPlayer.UpdateHandVisibility();
            localPlayer.RefreshLocalHandUI();
        }
        else
        {
            Debug.LogWarning("[SelectPlayerDice] Local player not found in AllPlayers!");
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

            UIAimLine aimLine = selectedPlayerDice.GetComponentInChildren<UIAimLine>();
            if (aimLine != null)
            {
                aimLine.SetCardSelected(false);
                aimLine.ClearAimData();
                // НЕ ВЫКЛЮЧАЙ aimLine.gameObject!
                // aimLine.gameObject.SetActive(false);
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