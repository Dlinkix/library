using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class UIAimLine : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private RectTransform playerRect;
    [SerializeField] private float maxDistance = 3000f;
    [SerializeField] private Color lineColor = Color.cyan;
    [SerializeField] private float dotWidth = 6f;
    [SerializeField] private float dotHeight = 3f;
    [SerializeField] private float spacing = 15f;
    [SerializeField] private float arcHeight = 80f;

    [Header("End Icon")]
    [SerializeField] private Sprite endIconSprite;
    [SerializeField] private float endIconSize = 30f;

    [Header("Animation")]
    [SerializeField] private float animationSpeed = 200f;
    [SerializeField] private int blocksForMaxHeight = 5;

    private Canvas canvas;
    private RectTransform canvasRect;
    private bool isLocalPlayer = false;
    private bool isInitialized = false;
    private bool isDestroyed = false;
    private Sprite squareSprite;
    private float animationOffset;
    private NetworkGamePlayer ownerPlayer;
    private NetworkGameEnemy ownerEnemy;
    private List<RectTransform> dotRects = new List<RectTransform>();
    private List<Image> dotImages = new List<Image>();
    private const int POOL_SIZE = 50;

    private Vector2[] basePositions;
    private float[] dotAngles;
    private int currentDotCount;

    private GameObject lineContainer;
    private GameObject endIconObject;
    private RectTransform endIconRect;
    private Image endIconImage;

    private List<RaycastResult> raycastResults = new List<RaycastResult>();
    private PointerEventData pointerData;

    private bool isCardSelected = false;

    public void SetOwner(NetworkGamePlayer player)
    {
        ownerPlayer = player;
        if (ownerPlayer != null)
        {
            isLocalPlayer = ownerPlayer.isLocalPlayer;
            Debug.Log($"UIAimLine: Owner set to {ownerPlayer.PlayerName}, isLocal: {isLocalPlayer}");

            if (!isLocalPlayer)
            {
                
                Destroy(this); 
                Debug.Log("UIAimLine: Removed component from non-local player UI");
            }
            else
            {
                InitializeComponents();
            }
        }
    }
    public void SetOwner(NetworkGameEnemy enemy)
    {
        ownerEnemy = enemy;
        Debug.Log("UIAimLine: Destroyed on enemy UI");
        Destroy(this); 
    }

    void Awake()
    {
        // Ничего не делаем в Awake - ждем SetOwner
    }

    void Start()
    {
        // Если владелец не установлен, пробуем найти через GetComponentInParent
        if (ownerPlayer == null && ownerEnemy == null)
        {
            // Пробуем найти NetworkGamePlayer на родительских объектах
            NetworkGamePlayer player = GetComponentInParent<NetworkGamePlayer>();
            if (player != null)
            {
                ownerPlayer = player;
                isLocalPlayer = player.isLocalPlayer;
                Debug.Log($"UIAimLine: Found owner via GetComponentInParent: {player.PlayerName}, isLocal: {isLocalPlayer}");

                if (!isLocalPlayer)
                {     
                    Destroy(this);
                    return;
                }
                else
                {
                    InitializeComponents();
                    return;
                }
            }

            // Пробуем найти NetworkGameEnemy на родительских объектах
            NetworkGameEnemy enemy = GetComponentInParent<NetworkGameEnemy>();
            if (enemy != null)
            {
                ownerEnemy = enemy;
                Debug.Log("UIAimLine: Found enemy via GetComponentInParent, destroying");
                Destroy(gameObject);
                return;
            }

            // Если ничего не нашли - удаляем
            Debug.Log("UIAimLine: No owner set and not found in parent, destroying");
            Destroy(gameObject);
            return;
        }

        // Если владелец установлен, но это не локальный игрок - удаляем
        if (!isLocalPlayer)
        {
            Debug.Log("UIAimLine: Not local player, destroying");
            Destroy(gameObject);
            return;
        }

        // Если все ок и еще не инициализированы - инициализируем
        if (!isInitialized)
        {
            InitializeComponents();
        }
    }

    private void InitializeComponents()
    {
        if (isInitialized || isDestroyed) return;

        canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("UIAimLine: No canvas found!");
            Destroy(gameObject);
            return;
        }

        canvasRect = canvas.transform as RectTransform;
        if (playerRect == null) playerRect = GetComponent<RectTransform>();

        lineColor = new Color(0.2f, 0.6f, 1f, 1f);

        basePositions = new Vector2[POOL_SIZE];
        dotAngles = new float[POOL_SIZE];

        pointerData = new PointerEventData(EventSystem.current);

        CreateSquareSprite();
        CreateLineContainer();
        CreateEndIcon();
        CreateDotPool(POOL_SIZE);

        isInitialized = true;
        Debug.Log("UIAimLine: Initialized successfully");
    }

    void CreateSquareSprite()
    {
        Texture2D texture = new Texture2D(4, 4);
        texture.filterMode = FilterMode.Point;

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                texture.SetPixel(x, y, Color.white);
            }
        }
        texture.Apply();

        squareSprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        squareSprite.name = "SquareSprite";
    }

    void CreateLineContainer()
    {
        lineContainer = new GameObject("AimLineDots", typeof(RectTransform));
        lineContainer.transform.SetParent(transform.parent, false);

        RectTransform containerRect = lineContainer.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.sizeDelta = new Vector2(0, 0);

        lineContainer.SetActive(false);
    }

    void CreateDotPool(int poolSize)
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject dot = new GameObject("Dot", typeof(RectTransform));
            dot.transform.SetParent(lineContainer.transform, false);

            Image dotImage = dot.AddComponent<Image>();
            dotImage.sprite = squareSprite;
            dotImage.raycastTarget = false;
            dotImage.color = lineColor;

            RectTransform dotRect = dot.GetComponent<RectTransform>();
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(dotWidth, dotHeight);

            dot.SetActive(false);

            dotRects.Add(dotRect);
            dotImages.Add(dotImage);
        }
    }

    void CreateEndIcon()
    {
        endIconObject = new GameObject("EndIcon", typeof(RectTransform));
        endIconObject.transform.SetParent(transform.parent, false);

        endIconImage = endIconObject.AddComponent<Image>();
        endIconImage.sprite = endIconSprite;
        endIconImage.raycastTarget = false;
        endIconImage.color = lineColor;

        endIconRect = endIconObject.GetComponent<RectTransform>();
        endIconRect.anchorMin = new Vector2(0.5f, 0.5f);
        endIconRect.anchorMax = new Vector2(0.5f, 0.5f);
        endIconRect.pivot = new Vector2(0.5f, 0.5f);
        endIconRect.sizeDelta = new Vector2(endIconSize, endIconSize);

        endIconObject.SetActive(false);
    }

    void Update()
    {
        if (isDestroyed || !isInitialized) return;

        if (!isLocalPlayer)
        {
            if (lineContainer != null) lineContainer.SetActive(false);
            if (endIconObject != null) endIconObject.SetActive(false);
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                pointerData.position = Input.mousePosition;
                raycastResults.Clear();
                EventSystem.current.RaycastAll(pointerData, raycastResults);

                for (int i = 0; i < raycastResults.Count; i++)
                {
                    if (raycastResults[i].gameObject.GetComponent<LocalHandCardView>() != null ||
                        raycastResults[i].gameObject.GetComponentInParent<LocalHandCardView>() != null)
                    {
                        isCardSelected = true;
                        break;
                    }
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            isCardSelected = false;
            if (lineContainer != null) lineContainer.SetActive(false);
            if (endIconObject != null) endIconObject.SetActive(false);
            animationOffset = 0f;
        }

        if (isCardSelected && Input.GetMouseButton(0))
        {
            animationOffset += Time.deltaTime * animationSpeed;
            UpdateLine();
            if (lineContainer != null) lineContainer.SetActive(true);
            if (endIconObject != null) endIconObject.SetActive(true);
        }
        else
        {
            if (lineContainer != null && lineContainer.activeSelf) lineContainer.SetActive(false);
            if (endIconObject != null && endIconObject.activeSelf) endIconObject.SetActive(false);
        }
    }

    void UpdateLine()
    {
        if (lineContainer == null || playerRect == null) return;

        Vector2 startPos = playerRect.anchoredPosition;

        Vector2 mousePos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            Input.mousePosition,
            null,
            out mousePos
        );

        Vector2 direction = mousePos - startPos;
        float distance = direction.magnitude;

        if (distance > maxDistance)
        {
            direction = direction.normalized * maxDistance;
            distance = maxDistance;
        }

        Vector2 endPos = startPos + direction;

        float minDistance = dotWidth * 2;

        HideAllDots();

        if (distance < minDistance)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            ShowDot(0, endPos, angle);
            UpdateEndIcon(endPos, direction.normalized);
            return;
        }

        int dotCount = Mathf.FloorToInt(distance / spacing);
        if (dotCount < 2) dotCount = 2;

        int arraySize = dotCount + 1;
        if (arraySize > basePositions.Length)
        {
            int newSize = Mathf.Max(arraySize, POOL_SIZE);
            basePositions = new Vector2[newSize];
            dotAngles = new float[newSize];
        }

        float heightProgress = Mathf.Clamp01(dotCount / (float)blocksForMaxHeight);
        float heightMultiplier = heightProgress * heightProgress;
        float currentArcHeight = arcHeight * heightMultiplier;

        float verticalComponent = Mathf.Abs(direction.y);
        float horizontalComponent = Mathf.Abs(direction.x);
        float totalComponent = verticalComponent + horizontalComponent;

        float directionMultiplier;
        if (totalComponent > 0.001f)
        {
            float horizontalRatio = horizontalComponent / totalComponent;
            directionMultiplier = 0.15f + 0.85f * Mathf.Pow(horizontalRatio, 1.5f);
        }
        else
        {
            directionMultiplier = 0.15f;
        }

        directionMultiplier = Mathf.Clamp(directionMultiplier, 0.15f, 1f);
        currentArcHeight *= directionMultiplier;

        Vector2 previousDotPos = startPos;

        for (int i = 0; i <= dotCount; i++)
        {
            float t = i / (float)dotCount;

            Vector2 pointOnLine = Vector2.Lerp(startPos, endPos, t);
            float arcOffset = currentArcHeight * 4 * t * (1 - t);
            Vector2 dotPos = pointOnLine + new Vector2(0, arcOffset);

            basePositions[i] = dotPos;

            Vector2 directionToNext;
            if (i < dotCount)
            {
                float nextT = (i + 1) / (float)dotCount;
                Vector2 nextPointOnLine = Vector2.Lerp(startPos, endPos, nextT);
                float nextArcOffset = currentArcHeight * 4 * nextT * (1 - nextT);
                Vector2 nextDotPos = nextPointOnLine + new Vector2(0, nextArcOffset);
                directionToNext = (nextDotPos - dotPos).normalized;
            }
            else
            {
                directionToNext = (dotPos - previousDotPos).normalized;
            }

            if (directionToNext == Vector2.zero)
                directionToNext = Vector2.right;

            dotAngles[i] = Mathf.Atan2(directionToNext.y, directionToNext.x) * Mathf.Rad2Deg;
            previousDotPos = dotPos;
        }

        float lastAngle = dotAngles[dotCount];
        Vector2 lastDirection = new Vector2(
            Mathf.Cos(lastAngle * Mathf.Deg2Rad),
            Mathf.Sin(lastAngle * Mathf.Deg2Rad)
        );
        UpdateEndIcon(endPos, lastDirection);

        int dotsToShow = dotCount + 1;
        float normalizedOffset = animationOffset / distance;
        normalizedOffset = (normalizedOffset % 1f + 1f) % 1f;

        for (int i = 0; i < dotsToShow && i < dotRects.Count; i++)
        {
            float baseT = i / (float)dotCount;
            float animatedT = baseT + normalizedOffset;
            animatedT = (animatedT % 1f + 1f) % 1f;

            float floatIndex = animatedT * dotCount;
            int index1 = Mathf.FloorToInt(floatIndex);
            int index2 = index1 + 1;
            float frac = floatIndex - index1;

            index1 = Mathf.Clamp(index1, 0, dotCount);
            index2 = Mathf.Clamp(index2, 0, dotCount);

            Vector2 dotPos = Vector2.Lerp(basePositions[index1], basePositions[index2], frac);
            float dotAngle = Mathf.LerpAngle(dotAngles[index1], dotAngles[index2], frac);

            ShowDot(i, dotPos, dotAngle);
        }

        currentDotCount = dotsToShow;
    }

    void ShowDot(int index, Vector2 position, float angle)
    {
        if (index >= dotRects.Count) return;

        GameObject dot = dotRects[index].gameObject;
        if (!dot.activeSelf) dot.SetActive(true);

        dotRects[index].anchoredPosition = position;
        dotRects[index].rotation = Quaternion.Euler(0, 0, angle);
    }

    void HideAllDots()
    {
        for (int i = 0; i < dotRects.Count; i++)
        {
            if (dotRects[i].gameObject.activeSelf)
                dotRects[i].gameObject.SetActive(false);
        }
        currentDotCount = 0;
    }

    void UpdateEndIcon(Vector2 position, Vector2 direction)
    {
        if (endIconObject == null) return;

        endIconRect.anchoredPosition = position;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        endIconRect.rotation = Quaternion.Euler(0, 0, angle);
    }

    void OnDestroy()
    {
        isDestroyed = true;
        if (lineContainer != null) Destroy(lineContainer);
        if (endIconObject != null) Destroy(endIconObject);
    }
}