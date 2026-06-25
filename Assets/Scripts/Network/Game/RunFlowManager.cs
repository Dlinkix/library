using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum RunPhase
{
    Map = 0,
    RoomPopup = 1,
    Battle = 2,
}

[DisallowMultipleComponent]
public class RunFlowManager : MonoBehaviour
{
    [Serializable]
    private class NodeDescriptor
    {
        public int nodeId;
        public int laneIndex;
        public int columnIndex;
        public int roomType;
        public bool isStart;
        public bool isBoss;
    }

    [Serializable]
    private class VoteDescriptor
    {
        public int nodeId;
        public int count;
    }

    [Serializable]
    private class RunFlowSnapshot
    {
        public int phase;
        public int currentNodeId;
        public int pendingNodeId;
        public string popupTitle;
        public string popupDescription;
        public string statusText;
        public float voteSecondsRemaining;
        public bool voteTimerRunning;
        public int[] selectableNodeIds;
        public int[] completedNodeIds;
        public int[] visitedNodeIds;
        public NodeDescriptor[] nodes;
        public VoteDescriptor[] votes;
    }

    private sealed class NodeRuntimeState
    {
        public int NodeId;
        public int LaneIndex;
        public int ColumnIndex;
        public MapRoomType RoomType;
        public bool IsStart;
        public bool IsBoss;
    }

    public static RunFlowManager Instance { get; private set; }

    private const float VoteDurationSeconds = 30f;
    private const string PopupRootName = "RunFlowPopup";
    private const string BattleBackButtonName = "BattleBackButton";
    private const string StatusLabelName = "RunFlowStatus";

    private readonly Dictionary<int, NodeRuntimeState> nodeStates = new Dictionary<int, NodeRuntimeState>();
    private readonly Dictionary<int, List<int>> adjacencyMap = new Dictionary<int, List<int>>();
    private readonly Dictionary<int, int> votesByConnection = new Dictionary<int, int>();
    private readonly HashSet<int> completedNodeIds = new HashSet<int>();
    private readonly HashSet<int> visitedNodeIds = new HashSet<int>();

    private FightMapGenerator mapGenerator;
    private Canvas uiCanvas;
    private RectTransform popupRoot;
    private TMP_Text popupTitleText;
    private TMP_Text popupBodyText;
    private Button popupBackButton;
    private Button battleBackButton;
    private TMP_Text statusLabel;
    private Road25D sceneRoad;
    private GameObject battlePresentationRoot;
    private string pendingSnapshotJson;
    private RunPhase currentClientPhase = RunPhase.Map;

    private RunPhase currentPhase = RunPhase.Map;
    private int currentNodeId = FightMapNodeView.InvalidNodeId;
    private int pendingNodeId = FightMapNodeView.InvalidNodeId;
    private float voteDeadline = -1f;
    private string popupTitle = string.Empty;
    private string popupDescription = string.Empty;
    private string statusText = string.Empty;
    private bool serverInitialized;
    private bool clientInitialized;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        InitializeClientView();
    }

    private void Update()
    {
        if (!clientInitialized)
        {
            InitializeClientView();
        }

        if (NetworkServer.active && currentPhase == RunPhase.Map && voteDeadline > 0f && Time.time >= voteDeadline)
        {
            ResolveVote();
        }

        if (clientInitialized)
        {
            UpdateStatusLabel();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void InitializeClientView()
    {
        if (clientInitialized)
        {
            return;
        }

        mapGenerator = FindFirstObjectByType<FightMapGenerator>(FindObjectsInactive.Include);
        if (mapGenerator == null)
        {
            return;
        }

        mapGenerator.InitializeRuntimeNodes();
        mapGenerator.SetNodeSelectionHandler(HandleLocalNodeSelected);
        sceneRoad = FindFirstObjectByType<Road25D>(FindObjectsInactive.Include);

        battlePresentationRoot = FindBattlePresentationRoot();

        uiCanvas = mapGenerator.GetComponentInParent<Canvas>();
        if (uiCanvas == null)
        {
            uiCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        }

        if (uiCanvas == null)
        {
            return;
        }

        EnsureOverlayUI();
        clientInitialized = true;

        if (!string.IsNullOrEmpty(pendingSnapshotJson))
        {
            ApplySnapshot(pendingSnapshotJson);
            pendingSnapshotJson = null;
        }
        else
        {
            ApplyVisualPhase(currentClientPhase);
        }
    }

    public void RefreshBattleRoot()
    {
        battlePresentationRoot = GameObject.Find("UI");

        if (battlePresentationRoot != null && clientInitialized)
        {
            SetBattlePresentationRootVisible(currentClientPhase == RunPhase.Battle);
        }
    }

    [Server]
    public void BeginRun()
    {
        InitializeServerState();
        currentPhase = RunPhase.Map;
        pendingNodeId = FightMapNodeView.InvalidNodeId;
        votesByConnection.Clear();
        voteDeadline = -1f;
        popupTitle = string.Empty;
        popupDescription = string.Empty;
        statusText = "Choose the next room.";
        completedNodeIds.Clear();
        visitedNodeIds.Clear();
        if (currentNodeId != FightMapNodeView.InvalidNodeId)
        {
            completedNodeIds.Add(currentNodeId);
            visitedNodeIds.Add(currentNodeId);
        }
        BroadcastSnapshot();
    }

    [Server]
    public void SubmitVote(NetworkGamePlayer player, int nodeId)
    {
        if (player == null || player.connectionToClient == null)
        {
            return;
        }

        InitializeServerState();

        if (currentPhase != RunPhase.Map || !adjacencyMap.ContainsKey(currentNodeId))
        {
            return;
        }

        List<int> selectableNodes = adjacencyMap[currentNodeId];
        if (!selectableNodes.Contains(nodeId))
        {
            return;
        }

        if (votesByConnection.Count == 0)
        {
            voteDeadline = Time.time + VoteDurationSeconds;
        }

        votesByConnection[player.connectionToClient.connectionId] = nodeId;
        statusText = "Voting is in progress.";
        BroadcastSnapshot();

        if (AllConnectedPlayersVoted())
        {
            ResolveVote();
        }
    }

    [Server]
    public void CloseRoomAndReturnToMap()
    {
        if (currentPhase != RunPhase.RoomPopup || pendingNodeId == FightMapNodeView.InvalidNodeId)
        {
            return;
        }

        CompletePendingNodeAndReturnToMap("Room completed. Choose the next room.");
    }

    [Server]
    public void ReturnToMapFromBattle()
    {
        if (pendingNodeId == FightMapNodeView.InvalidNodeId)
        {
            currentPhase = RunPhase.Map;
            statusText = "Choose the next room.";
            BroadcastSnapshot();
            return;
        }

        CompletePendingNodeAndReturnToMap("Battle finished. Choose the next room.");
    }

    [Server]
    public void HandleConnectionDisconnected(int connectionId)
    {
        if (votesByConnection.Remove(connectionId))
        {
            if (currentPhase == RunPhase.Map)
            {
                if (votesByConnection.Count == 0)
                {
                    voteDeadline = -1f;
                    statusText = "Choose the next room.";
                }
                else if (AllConnectedPlayersVoted())
                {
                    ResolveVote();
                    return;
                }

                BroadcastSnapshot();
            }
        }
    }

    public void ApplySnapshot(string snapshotJson)
    {
        if (!clientInitialized)
        {
            pendingSnapshotJson = snapshotJson;
            return;
        }

        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return;
        }

        RunFlowSnapshot snapshot = JsonUtility.FromJson<RunFlowSnapshot>(snapshotJson);
        if (snapshot == null)
        {
            return;
        }

        currentClientPhase = (RunPhase)snapshot.phase;

        if (snapshot.nodes != null && snapshot.nodes.Length > 0)
        {
            mapGenerator.ApplyNodeMetadata(snapshot.nodes);
        }

        Dictionary<int, int> voteCounts = new Dictionary<int, int>();
        if (snapshot.votes != null)
        {
            for (int i = 0; i < snapshot.votes.Length; i++)
            {
                VoteDescriptor vote = snapshot.votes[i];
                voteCounts[vote.nodeId] = vote.count;
            }
        }

        mapGenerator.ApplyRunState(
            snapshot.currentNodeId,
            snapshot.pendingNodeId,
            snapshot.selectableNodeIds ?? Array.Empty<int>(),
            snapshot.completedNodeIds ?? Array.Empty<int>(),
            snapshot.visitedNodeIds ?? Array.Empty<int>(),
            voteCounts,
            currentClientPhase == RunPhase.Map);

        popupTitleText.text = snapshot.popupTitle ?? string.Empty;
        popupBodyText.text = snapshot.popupDescription ?? string.Empty;
        statusText = snapshot.statusText ?? string.Empty;
        voteDeadline = snapshot.voteTimerRunning ? Time.time + snapshot.voteSecondsRemaining : -1f;

        ApplyVisualPhase(currentClientPhase);
        UpdateStatusLabel();
    }

    public void RefreshClientVisuals()
    {
        if (!clientInitialized)
        {
            return;
        }

        ApplyVisualPhase(currentClientPhase);
        UpdateStatusLabel();
    }

    [Server]
    public string BuildSnapshotJson()
    {
        InitializeServerState();

        VoteDescriptor[] voteDescriptors = votesByConnection
            .GroupBy(pair => pair.Value)
            .Select(group => new VoteDescriptor
            {
                nodeId = group.Key,
                count = group.Count(),
            })
            .ToArray();

        RunFlowSnapshot snapshot = new RunFlowSnapshot
        {
            phase = (int)currentPhase,
            currentNodeId = currentNodeId,
            pendingNodeId = pendingNodeId,
            popupTitle = popupTitle,
            popupDescription = popupDescription,
            statusText = statusText,
            voteSecondsRemaining = voteDeadline > 0f ? Mathf.Max(0f, voteDeadline - Time.time) : 0f,
            voteTimerRunning = voteDeadline > 0f,
            selectableNodeIds = GetSelectableNodeIds().ToArray(),
            completedNodeIds = completedNodeIds.OrderBy(id => id).ToArray(),
            visitedNodeIds = visitedNodeIds.OrderBy(id => id).ToArray(),
            nodes = nodeStates.Values
                .OrderBy(node => node.ColumnIndex)
                .ThenBy(node => node.LaneIndex)
                .Select(node => new NodeDescriptor
                {
                    nodeId = node.NodeId,
                    laneIndex = node.LaneIndex,
                    columnIndex = node.ColumnIndex,
                    roomType = (int)node.RoomType,
                    isStart = node.IsStart,
                    isBoss = node.IsBoss,
                })
                .ToArray(),
            votes = voteDescriptors,
        };

        return JsonUtility.ToJson(snapshot);
    }

    private void InitializeServerState()
    {
        if (serverInitialized)
        {
            return;
        }

        if (mapGenerator == null)
        {
            mapGenerator = FindFirstObjectByType<FightMapGenerator>(FindObjectsInactive.Include);
        }

        if (mapGenerator == null)
        {
            return;
        }

        mapGenerator.InitializeRuntimeNodes();
        FightMapNodeView[] nodes = mapGenerator.GetRuntimeNodes();
        nodeStates.Clear();
        adjacencyMap.Clear();

        for (int i = 0; i < nodes.Length; i++)
        {
            FightMapNodeView node = nodes[i];
            if (node == null)
            {
                continue;
            }

            NodeRuntimeState state = new NodeRuntimeState
            {
                NodeId = node.NodeId,
                LaneIndex = node.LaneIndex,
                ColumnIndex = node.ColumnIndex,
                RoomType = node.RoomType,
                IsStart = node.IsStart,
                IsBoss = node.IsBoss,
            };

            nodeStates[state.NodeId] = state;
        }

        foreach (NodeRuntimeState state in nodeStates.Values)
        {
            adjacencyMap[state.NodeId] = BuildAdjacency(state);
        }

        NodeRuntimeState startNode = nodeStates.Values.FirstOrDefault(node => node.IsStart);
        currentNodeId = startNode != null ? startNode.NodeId : FightMapNodeView.InvalidNodeId;
        pendingNodeId = FightMapNodeView.InvalidNodeId;
        completedNodeIds.Clear();
        visitedNodeIds.Clear();
        serverInitialized = true;
    }

    private List<int> BuildAdjacency(NodeRuntimeState state)
    {
        List<int> result = new List<int>();
        foreach (NodeRuntimeState candidate in nodeStates.Values)
        {
            if (candidate.NodeId == state.NodeId)
            {
                continue;
            }

            if (state.IsStart)
            {
                if (candidate.ColumnIndex == 1 && !candidate.IsBoss)
                {
                    result.Add(candidate.NodeId);
                }
                continue;
            }

            if (candidate.IsBoss)
            {
                int maxColumn = nodeStates.Values.Where(node => !node.IsBoss).Max(node => node.ColumnIndex);
                if (state.ColumnIndex == maxColumn)
                {
                    result.Add(candidate.NodeId);
                }
                continue;
            }

            if (candidate.ColumnIndex == state.ColumnIndex + 1 && Mathf.Abs(candidate.LaneIndex - state.LaneIndex) <= 1)
            {
                result.Add(candidate.NodeId);
            }
        }

        return result;
    }

    private void EnsureOverlayUI()
    {
        popupRoot = FindOrCreateRectTransform(PopupRootName, uiCanvas.transform as RectTransform);
        popupRoot.anchorMin = new Vector2(0.5f, 0.5f);
        popupRoot.anchorMax = new Vector2(0.5f, 0.5f);
        popupRoot.pivot = new Vector2(0.5f, 0.5f);
        popupRoot.sizeDelta = new Vector2(520f, 260f);

        Image popupBackground = popupRoot.GetComponent<Image>();
        if (popupBackground == null)
        {
            popupBackground = popupRoot.gameObject.AddComponent<Image>();
        }

        popupBackground.color = new Color(0.08f, 0.1f, 0.15f, 0.94f);

        popupTitleText = FindOrCreateText("Title", popupRoot, 30f, TextAlignmentOptions.Center);
        popupTitleText.rectTransform.anchorMin = new Vector2(0f, 1f);
        popupTitleText.rectTransform.anchorMax = new Vector2(1f, 1f);
        popupTitleText.rectTransform.pivot = new Vector2(0.5f, 1f);
        popupTitleText.rectTransform.anchoredPosition = new Vector2(0f, -24f);
        popupTitleText.rectTransform.sizeDelta = new Vector2(-48f, 48f);

        popupBodyText = FindOrCreateText("Body", popupRoot, 22f, TextAlignmentOptions.Center);
        popupBodyText.rectTransform.anchorMin = new Vector2(0f, 0f);
        popupBodyText.rectTransform.anchorMax = new Vector2(1f, 1f);
        popupBodyText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        popupBodyText.rectTransform.anchoredPosition = new Vector2(0f, -10f);
        popupBodyText.rectTransform.sizeDelta = new Vector2(-64f, -120f);
        popupBodyText.textWrappingMode = TextWrappingModes.Normal;

        popupBackButton = FindOrCreateButton("BackButton", popupRoot, "Back");
        RectTransform popupBackRect = popupBackButton.GetComponent<RectTransform>();
        popupBackRect.anchorMin = new Vector2(0.5f, 0f);
        popupBackRect.anchorMax = new Vector2(0.5f, 0f);
        popupBackRect.pivot = new Vector2(0.5f, 0f);
        popupBackRect.anchoredPosition = new Vector2(0f, 20f);
        popupBackRect.sizeDelta = new Vector2(180f, 56f);
        popupBackButton.onClick.RemoveAllListeners();
        popupBackButton.onClick.AddListener(HandlePopupBackClicked);

        battleBackButton = FindOrCreateButton(BattleBackButtonName, uiCanvas.transform as RectTransform, "Back");
        RectTransform battleBackRect = battleBackButton.GetComponent<RectTransform>();
        battleBackRect.anchorMin = new Vector2(1f, 1f);
        battleBackRect.anchorMax = new Vector2(1f, 1f);
        battleBackRect.pivot = new Vector2(1f, 1f);
        battleBackRect.anchoredPosition = new Vector2(-24f, -24f);
        battleBackRect.sizeDelta = new Vector2(180f, 52f);
        battleBackButton.onClick.RemoveAllListeners();
        battleBackButton.onClick.AddListener(HandleBattleBackClicked);

        statusLabel = FindOrCreateText(StatusLabelName, uiCanvas.transform as RectTransform, 24f, TextAlignmentOptions.TopLeft);
        statusLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        statusLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        statusLabel.rectTransform.pivot = new Vector2(0f, 1f);
        statusLabel.rectTransform.anchoredPosition = new Vector2(24f, -24f);
        statusLabel.rectTransform.sizeDelta = new Vector2(640f, 120f);
    }

    private RectTransform FindOrCreateRectTransform(string objectName, RectTransform parent)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null)
        {
            return existing as RectTransform;
        }

        GameObject gameObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        return rectTransform;
    }

    private TMP_Text FindOrCreateText(string objectName, RectTransform parent, float fontSize, TextAlignmentOptions alignment)
    {
        Transform existing = parent.Find(objectName);
        TextMeshProUGUI text = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (text == null)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            text = textObject.GetComponent<TextMeshProUGUI>();
        }

        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private Button FindOrCreateButton(string objectName, RectTransform parent, string caption)
    {
        Transform existing = parent.Find(objectName);
        Button button = existing != null ? existing.GetComponent<Button>() : null;
        if (button == null)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.24f, 0.39f, 0.72f, 0.98f);

            button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            button.colors = colors;

            TMP_Text captionText = FindOrCreateText("Label", rectTransform, 24f, TextAlignmentOptions.Center);
            captionText.text = caption;
            captionText.rectTransform.anchorMin = Vector2.zero;
            captionText.rectTransform.anchorMax = Vector2.one;
            captionText.rectTransform.offsetMin = Vector2.zero;
            captionText.rectTransform.offsetMax = Vector2.zero;
        }

        TMP_Text label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.text = caption;
        }

        return button;
    }

    private void HandleLocalNodeSelected(int nodeId)
    {
        NetworkGamePlayer localPlayer = FindLocalPlayer();
        if (localPlayer == null)
        {
            return;
        }

        localPlayer.CmdSubmitMapVote(nodeId);
    }

    private void HandlePopupBackClicked()
    {
        NetworkGamePlayer localPlayer = FindLocalPlayer();
        if (localPlayer == null)
        {
            return;
        }

        localPlayer.CmdCloseCurrentRunPopup();
    }

    private void HandleBattleBackClicked()
    {
        NetworkGamePlayer localPlayer = FindLocalPlayer();
        if (localPlayer == null)
        {
            return;
        }

        localPlayer.CmdLeaveBattleToMap();
    }

    private NetworkGamePlayer FindLocalPlayer()
    {
        if (NetworkClient.localPlayer != null)
        {
            return NetworkClient.localPlayer.GetComponent<NetworkGamePlayer>();
        }

        NetworkGamePlayer[] players = FindObjectsByType<NetworkGamePlayer>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].isLocalPlayer)
            {
                return players[i];
            }
        }

        return null;
    }

    private void ApplyVisualPhase(RunPhase phase)
    {
        bool battle = phase == RunPhase.Battle;

        if (sceneRoad != null)
        {
            sceneRoad.gameObject.SetActive(battle);
            if (battle)
            {
                sceneRoad.RestartSplashSequence();
            }
        }

        if (popupRoot != null)
            popupRoot.gameObject.SetActive(phase == RunPhase.RoomPopup);

        if (battleBackButton != null)
            battleBackButton.gameObject.SetActive(battle);
    }

    private GameObject FindBattlePresentationRoot()
    {
        return GameObject.Find("UI");
    }

    private void SetBattlePresentationRootVisible(bool visible)
    {
        if (battlePresentationRoot == null)
        {
            battlePresentationRoot = FindBattlePresentationRoot();
        }

        if (battlePresentationRoot != null)
        {
            battlePresentationRoot.SetActive(visible);
        }
    }

    private bool ShouldShowSceneRoad(RunPhase phase)
    {
        if (phase != RunPhase.Battle || pendingNodeId == FightMapNodeView.InvalidNodeId)
        {
            return false;
        }

        if (!nodeStates.TryGetValue(pendingNodeId, out NodeRuntimeState nodeState))
        {
            if (mapGenerator != null)
            {
                FightMapNodeView[] nodes = mapGenerator.GetRuntimeNodes();
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i] != null && nodes[i].NodeId == pendingNodeId)
                    {
                        return nodes[i].RoomType == MapRoomType.Mob ||
                               nodes[i].RoomType == MapRoomType.EliteMob ||
                               nodes[i].RoomType == MapRoomType.Boss;
                    }
                }
            }
            return false;
        }

        return nodeState.RoomType == MapRoomType.Mob ||
               nodeState.RoomType == MapRoomType.EliteMob ||
               nodeState.RoomType == MapRoomType.Boss;
    }

    private void SetCombatUIVisible(bool visible)
    {
        for (int i = 0; i < NetworkGamePlayer.AllPlayers.Count; i++)
        {
            NetworkGamePlayer player = NetworkGamePlayer.AllPlayers[i];
            if (player != null)
            {
                player.SetCombatPresentationActive(visible);
            }
        }

        for (int i = 0; i < NetworkGameEnemy.AllEnemies.Count; i++)
        {
            NetworkGameEnemy enemy = NetworkGameEnemy.AllEnemies[i];
            if (enemy != null)
            {
                enemy.SetCombatPresentationActive(visible);
            }
        }
    }

    private void UpdateStatusLabel()
    {
        if (statusLabel == null)
        {
            return;
        }

        string label = statusText;
        if (currentClientPhase == RunPhase.Map && voteDeadline > 0f)
        {
            float seconds = Mathf.Max(0f, voteDeadline - Time.time);
            label = string.IsNullOrEmpty(label)
                ? $"Voting ends in {Mathf.CeilToInt(seconds)}s"
                : $"{label}\nVoting ends in {Mathf.CeilToInt(seconds)}s";
        }

        statusLabel.text = label;
    }

    [Server]
    private void ResolveVote()
    {
        if (votesByConnection.Count == 0)
        {
            voteDeadline = -1f;
            statusText = "Choose the next room.";
            BroadcastSnapshot();
            return;
        }

        Dictionary<int, int> counts = new Dictionary<int, int>();
        foreach (KeyValuePair<int, int> vote in votesByConnection)
        {
            if (!counts.TryAdd(vote.Value, 1))
            {
                counts[vote.Value]++;
            }
        }

        int bestScore = counts.Values.Max();
        List<int> finalists = counts
            .Where(pair => pair.Value == bestScore)
            .Select(pair => pair.Key)
            .ToList();

        int chosenNodeId = finalists[UnityEngine.Random.Range(0, finalists.Count)];
        votesByConnection.Clear();
        voteDeadline = -1f;
        pendingNodeId = chosenNodeId;
        visitedNodeIds.Add(chosenNodeId);

        NodeRuntimeState chosenNode = nodeStates[chosenNodeId];
        switch (chosenNode.RoomType)
        {
            case MapRoomType.Shop:
                OpenPopupForPendingNode("Shop", "The shop is open. This is a temporary UI. Press Back to return to the map.");
                break;
            case MapRoomType.Chest:
                OpenPopupForPendingNode("Chest", "You found a chest. This is a temporary UI. Press Back to return to the map.");
                break;
            case MapRoomType.Anomaly:
            case MapRoomType.RandomEvent:
                OpenPopupForPendingNode("Anomaly", "An event is active. This is a temporary UI. Press Back to return to the map.");
                break;
            case MapRoomType.Mob:
            case MapRoomType.EliteMob:
            case MapRoomType.Boss:
                currentPhase = RunPhase.Battle;
                statusText = $"Battle started: {chosenNode.RoomType}.";
                BroadcastSnapshot();
                FightManager.Instance?.BeginEncounter(chosenNode.RoomType);
                break;
            default:
                CompletePendingNodeAndReturnToMap("Node completed. Choose the next room.");
                break;
        }
    }

    [Server]
    private void OpenPopupForPendingNode(string title, string description)
    {
        currentPhase = RunPhase.RoomPopup;
        popupTitle = title;
        popupDescription = description;
        statusText = $"{title} opened.";
        BroadcastSnapshot();
    }

    [Server]
    private void CompletePendingNodeAndReturnToMap(string nextStatusText)
    {
        currentNodeId = pendingNodeId;
        completedNodeIds.Add(currentNodeId);
        visitedNodeIds.Add(currentNodeId);
        pendingNodeId = FightMapNodeView.InvalidNodeId;
        currentPhase = RunPhase.Map;
        popupTitle = string.Empty;
        popupDescription = string.Empty;
        statusText = nextStatusText;
        voteDeadline = -1f;
        votesByConnection.Clear();
        BroadcastSnapshot();
    }

    [Server]
    private bool AllConnectedPlayersVoted()
    {
        int activePlayerCount = 0;
        for (int i = 0; i < NetworkGamePlayer.AllPlayers.Count; i++)
        {
            NetworkGamePlayer player = NetworkGamePlayer.AllPlayers[i];
            if (player != null && player.connectionToClient != null)
            {
                activePlayerCount++;
            }
        }

        return activePlayerCount > 0 && votesByConnection.Count >= activePlayerCount;
    }

    [Server]
    private IEnumerable<int> GetSelectableNodeIds()
    {
        if (currentPhase != RunPhase.Map || !adjacencyMap.TryGetValue(currentNodeId, out List<int> nextNodes))
        {
            return Array.Empty<int>();
        }

        return nextNodes;
    }

    [Server]
    private void BroadcastSnapshot()
    {
        string snapshotJson = BuildSnapshotJson();
        FightManager.Instance?.BroadcastRunFlowSnapshot(snapshotJson);
    }
}