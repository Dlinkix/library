using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class FightMapGenerator : MonoBehaviour
{
    private const string MapRootName = "FightMapRoot";
    private const string ConnectionsRootName = "Connections";
    private const string NodesRootName = "Nodes";

    [Header("Data")]
    [SerializeField] private FightMapConfig config;
    [SerializeField] private RectTransform mapRoot;

    [Header("Layout")]
    [SerializeField] private Vector2 startPosition = new Vector2(-620f, 0f);
    [SerializeField] private Vector2 bossPosition = new Vector2(620f, 0f);
    [SerializeField] private float columnSpacing = 112f;
    [SerializeField] private float laneSpacing = 180f;
    [SerializeField] private Vector2 roomNodeSize = new Vector2(70f, 70f);
    [SerializeField] private Vector2 specialNodeSize = new Vector2(92f, 92f);
    [SerializeField] private float connectionThickness = 10f;

    [Header("State Colors")]
    [SerializeField] private Color selectedColor = new Color(1f, 0.92f, 0.4f, 1f);
    [SerializeField] private Color disabledTint = new Color(0.28f, 0.28f, 0.28f, 1f);
    [SerializeField] private Color connectionColor = new Color(1f, 1f, 1f, 0.28f);

    private readonly List<FightMapNodeView> nodes = new List<FightMapNodeView>();
    private FightMapNodeView currentNode;
    private int nextSelectableColumn = 1;
    private bool pathStarted;

    public void BuildMap()
    {
        if (config == null)
        {
            Debug.LogError("[FightMapGenerator] FightMapConfig is not assigned.");
            return;
        }

        EnsureMapHierarchy();
        ClearGeneratedContent();

        FightMapNodeView startNode = CreateNode("StartNode", startPosition, specialNodeSize, -1, 0, MapRoomType.Start, true, false);

        List<MapRoomType>[] laneRoomPools = BuildLanePools();
        if (laneRoomPools == null)
        {
            ClearGeneratedContent();
            return;
        }

        List<FightMapNodeView> previousColumnNodes = new List<FightMapNodeView> { startNode };
        for (int column = 1; column <= config.RoomsPerLane; column++)
        {
            List<FightMapNodeView> currentColumnNodes = new List<FightMapNodeView>(3);

            for (int lane = 0; lane < 3; lane++)
            {
                MapRoomType roomType = laneRoomPools[lane][column - 1];
                Vector2 position = new Vector2(startPosition.x + columnSpacing * column, GetLaneY(lane));
                FightMapNodeView node = CreateNode(
                    $"Lane{lane + 1}_Room{column}_{roomType}",
                    position,
                    roomNodeSize,
                    lane,
                    column,
                    roomType,
                    false,
                    false);
                currentColumnNodes.Add(node);
            }

            CreateConnections(previousColumnNodes, currentColumnNodes, column == 1);
            previousColumnNodes = currentColumnNodes;
        }

        FightMapNodeView bossNode = CreateNode(
            "BossNode",
            bossPosition,
            specialNodeSize,
            -1,
            config.RoomsPerLane + 1,
            MapRoomType.Boss,
            false,
            true);
        CreateConnections(previousColumnNodes, new List<FightMapNodeView> { bossNode }, false);

        BindAllNodes();
        ResetProgress();
    }

    public void ResetProgress()
    {
        BindAllNodes();
        currentNode = null;
        pathStarted = false;
        nextSelectableColumn = 1;
        RefreshNodeStates();
    }

    public void HandleNodeSelected(FightMapNodeView node)
    {
        if (node == null || !IsSelectable(node))
        {
            return;
        }

        currentNode = node;
        pathStarted = true;

        if (node.IsBoss)
        {
            nextSelectableColumn = node.ColumnIndex;
        }
        else
        {
            nextSelectableColumn = node.ColumnIndex + 1;
        }

        RefreshNodeStates();
    }

    private void Awake()
    {
        BindAllNodes();
        ResetProgress();
    }

    private void EnsureMapHierarchy()
    {
        if (mapRoot == null)
        {
            Transform existingRoot = transform.Find(MapRootName);
            if (existingRoot != null)
            {
                mapRoot = existingRoot as RectTransform;
            }
        }

        if (mapRoot == null)
        {
            GameObject rootObject = new GameObject(MapRootName, typeof(RectTransform));
            mapRoot = rootObject.GetComponent<RectTransform>();
            mapRoot.SetParent(transform, false);
            StretchToParent(mapRoot);
        }

        EnsureChildRoot(ConnectionsRootName);
        EnsureChildRoot(NodesRootName);
    }

    private void ClearGeneratedContent()
    {
        nodes.Clear();

        RectTransform connectionsRoot = EnsureChildRoot(ConnectionsRootName);
        RectTransform nodesRoot = EnsureChildRoot(NodesRootName);
        DestroyChildrenImmediate(connectionsRoot);
        DestroyChildrenImmediate(nodesRoot);
    }

    private FightMapNodeView CreateNode(
        string objectName,
        Vector2 anchoredPosition,
        Vector2 size,
        int lane,
        int column,
        MapRoomType roomType,
        bool isStart,
        bool isBoss)
    {
        RectTransform nodesRoot = EnsureChildRoot(NodesRootName);
        GameObject nodeObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(FightMapNodeView));
        nodeObject.layer = gameObject.layer;
        RectTransform rectTransform = nodeObject.GetComponent<RectTransform>();
        rectTransform.SetParent(nodesRoot, false);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Image image = nodeObject.GetComponent<Image>();
        image.color = GetColorForType(roomType);

        Button button = nodeObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.targetGraphic = image;

        FightMapNodeView nodeView = nodeObject.GetComponent<FightMapNodeView>();
        nodeView.Configure(lane, column, roomType, isStart, isBoss, GetColorForType(roomType));
        nodes.Add(nodeView);
        return nodeView;
    }

    private void CreateConnections(List<FightMapNodeView> fromNodes, List<FightMapNodeView> toNodes, bool fromStart)
    {
        for (int fromIndex = 0; fromIndex < fromNodes.Count; fromIndex++)
        {
            FightMapNodeView fromNode = fromNodes[fromIndex];
            for (int toIndex = 0; toIndex < toNodes.Count; toIndex++)
            {
                FightMapNodeView toNode = toNodes[toIndex];

                if (!ShouldCreateConnection(fromNode, toNode, fromStart))
                {
                    continue;
                }

                CreateConnectionVisual(fromNode.GetComponent<RectTransform>(), toNode.GetComponent<RectTransform>());
            }
        }
    }

    private bool ShouldCreateConnection(FightMapNodeView fromNode, FightMapNodeView toNode, bool fromStart)
    {
        if (fromNode == null || toNode == null)
        {
            return false;
        }

        if (fromStart)
        {
            return !toNode.IsBoss;
        }

        if (toNode.IsBoss)
        {
            return fromNode.ColumnIndex == config.RoomsPerLane;
        }

        return Mathf.Abs(fromNode.LaneIndex - toNode.LaneIndex) <= 1;
    }

    private void CreateConnectionVisual(RectTransform from, RectTransform to)
    {
        RectTransform connectionsRoot = EnsureChildRoot(ConnectionsRootName);
        GameObject lineObject = new GameObject(
            $"{from.gameObject.name}_To_{to.gameObject.name}",
            typeof(RectTransform),
            typeof(Image));
        lineObject.layer = gameObject.layer;
        RectTransform rectTransform = lineObject.GetComponent<RectTransform>();
        rectTransform.SetParent(connectionsRoot, false);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        Vector2 start = from.anchoredPosition;
        Vector2 end = to.anchoredPosition;
        Vector2 direction = end - start;
        float distance = direction.magnitude;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        rectTransform.anchoredPosition = (start + end) * 0.5f;
        rectTransform.sizeDelta = new Vector2(distance, connectionThickness);
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);

        Image image = lineObject.GetComponent<Image>();
        image.color = connectionColor;
        image.raycastTarget = false;
    }

    private void BindAllNodes()
    {
        nodes.Clear();

        if (mapRoot == null)
        {
            return;
        }

        FightMapNodeView[] foundNodes = mapRoot.GetComponentsInChildren<FightMapNodeView>(true);
        for (int i = 0; i < foundNodes.Length; i++)
        {
            FightMapNodeView node = foundNodes[i];
            node.Bind(this);
            nodes.Add(node);
        }
    }

    private void RefreshNodeStates()
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            FightMapNodeView node = nodes[i];
            bool selected = node == currentNode;
            bool selectable = IsSelectable(node);
            node.SetState(selectable, selected, selectedColor, disabledTint);
        }
    }

    private bool IsSelectable(FightMapNodeView node)
    {
        if (node == null || node.IsStart)
        {
            return false;
        }

        if (!pathStarted)
        {
            return node.ColumnIndex == 1;
        }

        if (currentNode == null)
        {
            return false;
        }

        if (node.IsBoss)
        {
            return currentNode.ColumnIndex == config.RoomsPerLane && nextSelectableColumn == node.ColumnIndex;
        }

        if (node.ColumnIndex != nextSelectableColumn || currentNode.IsBoss)
        {
            return false;
        }

        return Mathf.Abs(currentNode.LaneIndex - node.LaneIndex) <= 1;
    }

    private RectTransform EnsureChildRoot(string childName)
    {
        Transform existing = mapRoot.Find(childName);
        if (existing != null)
        {
            return existing as RectTransform;
        }

        GameObject childObject = new GameObject(childName, typeof(RectTransform));
        childObject.layer = gameObject.layer;
        RectTransform childRoot = childObject.GetComponent<RectTransform>();
        childRoot.SetParent(mapRoot, false);
        StretchToParent(childRoot);
        return childRoot;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }

    private static void DestroyChildrenImmediate(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(child);
            }
            else
#endif
            {
                Object.Destroy(child);
            }
        }
    }

    private float GetLaneY(int laneIndex)
    {
        return (1 - laneIndex) * laneSpacing;
    }

    private List<MapRoomType>[] BuildLanePools()
    {
        List<MapRoomType>[] lanePools = new List<MapRoomType>[3];
        for (int lane = 0; lane < lanePools.Length; lane++)
        {
            if (!config.TryBuildLanePool(lane, out List<MapRoomType> roomPool, out string error))
            {
                Debug.LogError($"[FightMapGenerator] {error}");
                return null;
            }

            ShuffleLanePool(roomPool, lane);
            lanePools[lane] = roomPool;
        }

        return lanePools;
    }

    private static void ShuffleLanePool(List<MapRoomType> roomPool, int laneIndex)
    {
        System.Random random = new System.Random(1000 + laneIndex * 97);
        for (int i = roomPool.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(0, i + 1);
            MapRoomType temporary = roomPool[i];
            roomPool[i] = roomPool[swapIndex];
            roomPool[swapIndex] = temporary;
        }
    }

    private Color GetColorForType(MapRoomType roomType)
    {
        switch (roomType)
        {
            case MapRoomType.Start:
                return new Color(0.42f, 0.9f, 0.56f, 1f);
            case MapRoomType.Mob:
                return new Color(0.88f, 0.32f, 0.32f, 1f);
            case MapRoomType.Shop:
                return new Color(0.95f, 0.73f, 0.27f, 1f);
            case MapRoomType.RandomEvent:
                return new Color(0.38f, 0.72f, 1f, 1f);
            case MapRoomType.Anomaly:
                return new Color(0.65f, 0.38f, 0.95f, 1f);
            case MapRoomType.EliteMob:
                return new Color(1f, 0.46f, 0.22f, 1f);
            case MapRoomType.Chest:
                return new Color(0.98f, 0.86f, 0.38f, 1f);
            case MapRoomType.Boss:
                return new Color(0.75f, 0.16f, 0.2f, 1f);
            default:
                return Color.white;
        }
    }
}
