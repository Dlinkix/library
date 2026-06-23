using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
[RequireComponent(typeof(Button))]
public class FightMapNodeView : MonoBehaviour
{
    [SerializeField] private int laneIndex = -1;
    [SerializeField] private int columnIndex = -1;
    [SerializeField] private MapRoomType roomType = MapRoomType.Mob;
    [SerializeField] private bool isStart;
    [SerializeField] private bool isBoss;
    [SerializeField] private Color baseColor = Color.white;
    [SerializeField] private Image iconImage;
    [SerializeField] private Button button;

    private FightMapGenerator owner;

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
        CacheReferences();
        ApplyBaseVisual();
    }

    public void Bind(FightMapGenerator generator)
    {
        owner = generator;
        CacheReferences();

        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(HandleClick);
    }

    public void SetState(bool selectable, bool selected, Color selectedColor, Color disabledTint)
    {
        CacheReferences();

        if (iconImage != null)
        {
            if (selected)
            {
                iconImage.color = selectedColor;
            }
            else if (selectable)
            {
                iconImage.color = Color.Lerp(baseColor, Color.white, 0.25f);
            }
            else
            {
                iconImage.color = Color.Lerp(baseColor, disabledTint, 0.65f);
            }
        }

        if (button != null)
        {
            button.interactable = selectable;
        }
    }

    private void Reset()
    {
        CacheReferences();
        ApplyBaseVisual();
    }

    private void OnValidate()
    {
        CacheReferences();
        ApplyBaseVisual();
    }

    private void HandleClick()
    {
        owner?.HandleNodeSelected(this);
    }

    private void CacheReferences()
    {
        if (iconImage == null)
        {
            iconImage = GetComponent<Image>();
        }

        if (button == null)
        {
            button = GetComponent<Button>();
        }
    }

    private void ApplyBaseVisual()
    {
        if (iconImage != null)
        {
            iconImage.color = baseColor;
            iconImage.raycastTarget = true;
        }
    }
}
