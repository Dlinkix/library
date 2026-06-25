using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
[RequireComponent(typeof(Button))]
public class FightMapNodeView : MonoBehaviour
{
    public const int InvalidNodeId = -1;

    [SerializeField] private int laneIndex = -1;
    [SerializeField] private int columnIndex = -1;
    [SerializeField] private int nodeId = InvalidNodeId;
    [SerializeField] private MapRoomType roomType = MapRoomType.Mob;
    [SerializeField] private bool isStart;
    [SerializeField] private bool isBoss;
    [SerializeField] private Color baseColor = Color.white;
    [SerializeField] private Image iconImage;
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text voteText;

    private FightMapGenerator owner;

    public int NodeId => nodeId;
    public int LaneIndex => laneIndex;
    public int ColumnIndex => columnIndex;
    public MapRoomType RoomType => roomType;
    public bool IsStart => isStart;
    public bool IsBoss => isBoss;

    public void Configure(int lane, int column, MapRoomType type, bool start, bool boss, Color color)
    {
        laneIndex = lane;
        columnIndex = column;
        roomType = type;
        isStart = start;
        isBoss = boss;
        baseColor = color;
        nodeId = FightMapGenerator.BuildNodeId(column, lane, start, boss);
        CacheReferences();
        ApplyBaseVisual();
    }

    public void Bind(FightMapGenerator generator, int runtimeNodeId)
    {
        owner = generator;
        nodeId = runtimeNodeId;
        CacheReferences();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }
    }

    public void SetState(bool selectable, bool selected, bool completed, bool visited, int votes, Color selectedColor, Color completedColor, Color visitedColor, Color disabledTint)
    {
        CacheReferences();
        if (iconImage != null)
        {
            if (selected) iconImage.color = selectedColor;
            else if (completed) iconImage.color = completedColor;
            else if (visited) iconImage.color = visitedColor;
            else if (selectable) iconImage.color = Color.Lerp(baseColor, Color.white, 0.25f);
            else iconImage.color = Color.Lerp(baseColor, disabledTint, 0.65f);
        }
        if (button != null) button.interactable = selectable;
        if (voteText != null) voteText.text = votes > 0 ? votes.ToString() : string.Empty;
    }

    public void ApplyMetadata(int runtimeNodeId, int runtimeLane, int runtimeColumn, MapRoomType runtimeRoomType, bool runtimeIsStart, bool runtimeIsBoss)
    {
        nodeId = runtimeNodeId;
        laneIndex = runtimeLane;
        columnIndex = runtimeColumn;
        roomType = runtimeRoomType;
        isStart = runtimeIsStart;
        isBoss = runtimeIsBoss;
        CacheReferences();
        ApplyBaseVisual();
    }

    private void Reset() { CacheReferences(); ApplyBaseVisual(); }
    private void OnValidate() { CacheReferences(); ApplyBaseVisual(); }
    private void HandleClick() => owner?.HandleNodeSelected(this);

    private void CacheReferences()
    {
        if (iconImage == null) iconImage = GetComponent<Image>();
        if (button == null) button = GetComponent<Button>();
        if (voteText == null) voteText = GetComponentInChildren<TextMeshProUGUI>(true);

        if (voteText == null)
        {
            GameObject textObject = new GameObject("VoteText", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            voteText = textObject.GetComponent<TextMeshProUGUI>();
            voteText.fontSize = 24f;
            voteText.alignment = TextAlignmentOptions.Center;
            voteText.color = Color.black;
            voteText.raycastTarget = false;
        }
    }

    private void ApplyBaseVisual()
    {
        if (iconImage != null) { iconImage.color = baseColor; iconImage.raycastTarget = true; }
        if (voteText != null) voteText.text = string.Empty;
    }
}