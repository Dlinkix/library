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
            ClearAllSelections();
        }
    }

    public void SelectPlayerDice(DiceRoll dice)
    {
        // Сбрасываем старый кубик
        if (selectedPlayerDice != null && selectedPlayerDice != dice)
        {
            selectedPlayerDice.SetSelected(false);
        }

        selectedPlayerDice = dice;
        selectedPlayerDice.SetSelected(true);
        OnPlayerDiceSelected?.Invoke(dice);

        // Показываем линию у нового кубика
        UIAimLine aimLine = dice.GetComponentInChildren<UIAimLine>();
        if (aimLine != null)
        {
            aimLine.SetPlayerDice(dice);

            if (dice.selectedCardId != -1)
            {
                aimLine.SetCardSelected(true);

                if (dice.selectedTargetEnemyNetId != 0)
                {
                    foreach (var enemy in NetworkGameEnemy.AllEnemies)
                    {
                        if (enemy != null && enemy.netId == dice.selectedTargetEnemyNetId)
                        {
                            DiceRoll[] enemyDices = enemy.GetComponentsInChildren<DiceRoll>();
                            if (dice.selectedTargetDiceIndex >= 0 && dice.selectedTargetDiceIndex < enemyDices.Length)
                            {
                                aimLine.SetTarget(enemyDices[dice.selectedTargetDiceIndex]);
                                break;
                            }
                        }
                    }
                }
            }
        }

        // ===== ИСПРАВЛЕНО: Ищем локального игрока через connection =====
        NetworkGamePlayer localPlayer = null;

        // Способ 1: через NetworkClient
        if (NetworkClient.connection != null && NetworkClient.connection.identity != null)
        {
            localPlayer = NetworkClient.connection.identity.GetComponent<NetworkGamePlayer>();
        }

        // Способ 2: через AllPlayers (запасной)
        if (localPlayer == null)
        {
            foreach (var player in NetworkGamePlayer.AllPlayers)
            {
                if (player != null && player.isLocalPlayer)
                {
                    localPlayer = player;
                    break;
                }
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
            Debug.LogWarning("[SelectPlayerDice] Local player not found! Trying alternative method...");

            // Способ 3: найти любой NetworkGamePlayer и проверить isLocalPlayer
            NetworkGamePlayer[] allPlayers = FindObjectsByType<NetworkGamePlayer>(FindObjectsSortMode.None);
            foreach (var player in allPlayers)
            {
                if (player != null && player.isLocalPlayer)
                {
                    localPlayer = player;
                    player.EnsureLocalHandUI();
                    player.UpdateHandVisibility();
                    player.RefreshLocalHandUI();
                    Debug.Log("[SelectPlayerDice] Local player found via FindObjectsByType");
                    break;
                }
            }
        }

        UpdateHandVisibilityForAllPlayers();
        Debug.Log($"Selected player dice: {dice.ownerSlotIndex}, value: {dice.diceValue}");
    }

    public void SelectEnemyDice(DiceRoll dice)
    {
        if (selectedEnemyDice != null && selectedEnemyDice != dice)
        {
            selectedEnemyDice.SetSelected(false);
        }

        selectedEnemyDice = dice;

        if (selectedEnemyDice != null)
        {
            selectedEnemyDice.SetSelected(true);
        }

        OnEnemyDiceSelected?.Invoke(dice);
    }

    public void ClearAllSelections()
    {
        // Очищаем только выделение, но не данные кубиков
        if (selectedPlayerDice != null)
        {
            selectedPlayerDice.SetSelected(false);
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

    // ===== ОСТАВЛЯЕМ СТАРЫЙ МЕТОД ДЛЯ СОВМЕСТИМОСТИ =====
    public void ClearSelection()
    {
        ClearAllSelections();
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