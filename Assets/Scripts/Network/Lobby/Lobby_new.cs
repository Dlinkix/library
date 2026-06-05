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

            if (startPos != null)
            {
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
            }

            NetworkServer.ReplacePlayerForConnection(conn, gamePlayer, ReplacePlayerOptions.KeepAuthority);
        }

        playerConnections.Clear();
    }
}