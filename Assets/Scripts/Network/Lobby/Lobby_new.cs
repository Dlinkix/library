using Mirror;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManagerLobby : NetworkManager
{

    [SerializeField] private int minPlayers = 1;
    [Scene][SerializeField] private string menuScene = string.Empty;
    [Scene][SerializeField] private string gameScene = string.Empty;
    [Header("Room")]
    [SerializeField] private NetworkRoomPlayerLobby roomPlayerLobby = null;
    private List<NetworkConnectionToClient> playerConnections = new List<NetworkConnectionToClient>();
    public static event System.Action OnClientConnected;
    public static event System.Action OnClientDisconnected;

    public List<NetworkRoomPlayerLobby> RoomPlayers { get; } = new List<NetworkRoomPlayerLobby>();

    #region zalupa

    public override void OnStartServer()
    {
        spawnPrefabs = Resources.LoadAll<GameObject>("SpawnablePrefabs").ToList();

        if (playerPrefab != null && !spawnPrefabs.Contains(playerPrefab))
        {
            spawnPrefabs.Add(playerPrefab);
        }
    }

    public override void OnStartClient()
    {
        var spawnablePrefabs = Resources.LoadAll<GameObject>("SpawnablePrefabs");

        foreach (var prefab in spawnablePrefabs)
        {
            NetworkClient.RegisterPrefab(prefab);
        }

        if (playerPrefab != null)
        {
            NetworkClient.RegisterPrefab(playerPrefab);
        }
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        OnClientConnected?.Invoke();
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        OnClientDisconnected?.Invoke();
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        if (numPlayers >= maxConnections)
        {
            conn.Disconnect();
            return;
        }
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        string expectedSceneName = string.IsNullOrEmpty(menuScene)
            ? ""
            : System.IO.Path.GetFileNameWithoutExtension(menuScene);

        string currentSceneName = SceneManager.GetActiveScene().name;

        if (string.IsNullOrEmpty(expectedSceneName) || currentSceneName == expectedSceneName)
        {
            if (roomPlayerLobby == null) return;

            bool isLeader = RoomPlayers.Count == 0;
            NetworkRoomPlayerLobby roomPlayerInstance = Instantiate(roomPlayerLobby);
            roomPlayerInstance.IsLeader = isLeader;
            NetworkServer.AddPlayerForConnection(conn, roomPlayerInstance.gameObject);
        }
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (conn.identity != null)
        {
            var player = conn.identity.GetComponent<NetworkRoomPlayerLobby>();
            RoomPlayers.Remove(player);
            NotifyPlayersOfReadyState();
        }
        base.OnServerDisconnect(conn);
    }

    public override void OnStopServer()
    {
        RoomPlayers.Clear();
    }

    public void NotifyPlayersOfReadyState()
    {
        foreach (var player in RoomPlayers)
        {
            player.HandleReadyToStart(IsReadyToStart());
        }
    }

    private bool IsReadyToStart()
    {
        if (numPlayers < minPlayers) return false;

        foreach (var player in RoomPlayers)
        {
            if (!player.IsReady) return false;
        }
        return true;
    }

    public void StartGame()
    {
        if (NetworkServer.active && !string.IsNullOrEmpty(gameScene))
        {
            ServerChangeScene(gameScene);
        }
    }

    public override void ServerChangeScene(string newSceneName)
    {
        string currentSceneName = System.IO.Path.GetFileNameWithoutExtension(SceneManager.GetActiveScene().path);
        string newSceneNameOnly = System.IO.Path.GetFileNameWithoutExtension(newSceneName);
        string menuSceneNameOnly = System.IO.Path.GetFileNameWithoutExtension(menuScene);
        string gameSceneNameOnly = System.IO.Path.GetFileNameWithoutExtension(gameScene);

        if (currentSceneName == menuSceneNameOnly && newSceneNameOnly == gameSceneNameOnly)
        {
            playerConnections.Clear();
            foreach (var player in RoomPlayers)
            {
                playerConnections.Add(player.connectionToClient);
            }
        }

        base.ServerChangeScene(newSceneName);
    }
    //
    public override void OnServerSceneChanged(string sceneName)
    {
        string sceneNameOnly = System.IO.Path.GetFileNameWithoutExtension(sceneName);
        string gameSceneNameOnly = System.IO.Path.GetFileNameWithoutExtension(gameScene);

        if (sceneNameOnly == gameSceneNameOnly)
        {
            StartCoroutine(SpawnPlayersWhenReady());
        }
    }

    private System.Collections.IEnumerator SpawnPlayersWhenReady()
    {
        bool allReady = false;
        int maxAttempts = 50;
        int attempts = 0;

        while (!allReady && attempts < maxAttempts)
        {
            allReady = true;
            foreach (var conn in playerConnections)
            {
                if (conn != null && !conn.isReady)
                {
                    allReady = false;
                    break;
                }
            }

            if (!allReady)
            {
                yield return new WaitForSeconds(0.1f);
                attempts++;
            }
        }

        var startPositions = FindObjectsByType<NetworkStartPosition>(FindObjectsSortMode.None);

        for (int i = 0; i < playerConnections.Count; i++)
        {
            var conn = playerConnections[i];

            if (conn == null) continue;
            if (!conn.isReady) continue;

            Transform startPos = startPositions.Length > 0
                ? startPositions[i % startPositions.Length].transform
                : null;

            GameObject gamePlayer;

            Vector3 uiPosition = Vector3.zero;

            if (startPos != null)
            {
                uiPosition = startPos.position;

                Vector3 spawnPosition = startPos.position + new Vector3(0, 25f, 0);

                gamePlayer = Instantiate(playerPrefab, spawnPosition, startPos.rotation);
                gamePlayer.transform.localScale = Vector3.one;

                NetworkGamePlayer netPlayer = gamePlayer.GetComponent<NetworkGamePlayer>();

                if (netPlayer != null)
                {
                    foreach (var player in RoomPlayers)
                    {
                        if (player.connectionToClient == conn)
                        {
                            netPlayer.PlayerName = player.DisplayName;
                            break;
                        }
                    }
                }
            }
            else
            {
                gamePlayer = Instantiate(playerPrefab);
                gamePlayer.transform.localScale = Vector3.one;

                uiPosition = gamePlayer.transform.position;
            }

            NetworkServer.ReplacePlayerForConnection(
                conn,
                gamePlayer,
                ReplacePlayerOptions.KeepAuthority
            );
            NetworkGamePlayer spawnedPlayer = gamePlayer.GetComponent<NetworkGamePlayer>();

            if (spawnedPlayer != null)
            {
                spawnedPlayer.ServerSetSlotIndex(i);
                spawnedPlayer.PrepareStartingHand();
            }
        }

        SpawnEnemiesForFightScene();
       
        playerConnections.Clear();
    }

    [Server]
    private void SpawnEnemiesForFightScene()
    {
        GameObject enemyPrefab = Resources.Load<GameObject>("SpawnablePrefabs/Enemy");
        if (enemyPrefab == null)
        {
            Debug.LogWarning("Enemy prefab was not found in Resources/SpawnablePrefabs.");
            return;
        }

        DataGame[] loadedData = Resources.FindObjectsOfTypeAll<DataGame>();
        if (loadedData == null || loadedData.Length == 0 || loadedData[0] == null)
        {
            Debug.LogWarning("DataGame was not found. Enemies will not spawn.");
            return;
        }

        DataGame dataGame = loadedData[0];
        if (dataGame.enemyData == null || dataGame.enemyData.Count == 0)
        {
            return;
        }

        EnemySpawnPoint[] spawnPoints = FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None)
            .OrderBy(point => point.SpawnIndex)
            .ToArray();

        if (spawnPoints.Length == 0)
        {
            Debug.LogWarning("Enemy spawn points were not found in Fight scene.");
            return;
        }

        int configuredEnemyCount = dataGame.GetEnemyCount();
        int enemyCount = Mathf.Min(configuredEnemyCount, spawnPoints.Length);
        if (configuredEnemyCount > spawnPoints.Length)
        {
            Debug.LogWarning($"Not enough enemy spawn points for all enemies. Spawning {enemyCount} of {configuredEnemyCount}.");
        }

        for (int i = 0; i < enemyCount; i++)
        {
            EnemySpawnPoint spawnPoint = spawnPoints[i];
            int enemyDataIndex = i % dataGame.enemyData.Count;
            Quaternion enemyFacingRightRotation = Quaternion.Euler(0f, 180f, 0f);
            GameObject enemyObject = Instantiate(enemyPrefab, spawnPoint.transform.position, enemyFacingRightRotation);
            enemyObject.transform.localScale = Vector3.one;

            NetworkGameEnemy networkEnemy = enemyObject.GetComponent<NetworkGameEnemy>();
            if (networkEnemy == null)
            {
                Destroy(enemyObject);
                Debug.LogWarning("Enemy prefab does not contain NetworkGameEnemy.");
                continue;
            }

            networkEnemy.InitializeEnemy(enemyDataIndex, spawnPoint.SpawnIndex);
            NetworkServer.Spawn(enemyObject);
        }
    }

    #endregion
}
