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
        if (Input.GetMouseButtonDown(1))
        {
            ClearAllSelections();
        }
    }

    public void SelectPlayerDice(DiceRoll dice)
    {
        if (selectedPlayerDice != null && selectedPlayerDice != dice)
        {
            selectedPlayerDice.SetSelected(false);
        }

        selectedPlayerDice = dice;
        selectedPlayerDice.SetSelected(true);
        OnPlayerDiceSelected?.Invoke(dice);

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

        NetworkGamePlayer localPlayer = null;

        if (NetworkClient.connection != null && NetworkClient.connection.identity != null)
        {
            localPlayer = NetworkClient.connection.identity.GetComponent<NetworkGamePlayer>();
        }

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
            NetworkGamePlayer[] allPlayers = FindObjectsByType<NetworkGamePlayer>(FindObjectsSortMode.None);
            foreach (var player in allPlayers)
            {
                if (player != null && player.isLocalPlayer)
                {
                    localPlayer = player;
                    player.EnsureLocalHandUI();
                    player.UpdateHandVisibility();
                    player.RefreshLocalHandUI();
                    break;
                }
            }
        }

        UpdateHandVisibilityForAllPlayers();
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

    public DiceRoll GetSelectedPlayerDice() => selectedPlayerDice;
    public DiceRoll GetSelectedEnemyDice() => selectedEnemyDice;
}