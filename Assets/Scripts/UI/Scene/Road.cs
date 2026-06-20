using UnityEngine;
using UnityEngine.UI;

public class Road25D : MonoBehaviour
{
    [Header("Road Layers")]
    [SerializeField] private RectTransform[] roadLayers; // Слои дороги от дальнего к ближнему
    [SerializeField] private float[] layerSpeeds; // Скорости для каждого слоя

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float maxOffset = 300f;
    [SerializeField] private bool autoMove = true;
    [SerializeField] private bool useMouseInput = true;

    [Header("2.5D Effects")]
    [SerializeField] private bool enableVerticalOffset = true;
    [SerializeField] private float verticalOffsetIntensity = 0.5f;
    [SerializeField] private bool enableScaleEffect = true;
    [SerializeField] private float scaleIntensity = 0.15f;
    [SerializeField] private bool enableAlphaEffect = true;

    private float currentOffset;
    private float targetOffset;
    private Vector2 mouseStartPos;
    private bool isDragging;
    private Vector2[] layerStartPositions;
    private Vector3[] layerStartScales;
    private CanvasGroup[] layerCanvasGroups;

    void Start()
    {
        if (roadLayers == null || roadLayers.Length == 0) return;

        layerStartPositions = new Vector2[roadLayers.Length];
        layerStartScales = new Vector3[roadLayers.Length];
        layerCanvasGroups = new CanvasGroup[roadLayers.Length];

        // Автоматические скорости если не заданы
        if (layerSpeeds == null || layerSpeeds.Length == 0)
        {
            layerSpeeds = new float[roadLayers.Length];
            for (int i = 0; i < roadLayers.Length; i++)
            {
                // От 0.1 до 0.9 с шагом ~0.2
                layerSpeeds[i] = 0.1f + i * 0.2f;
            }
        }

        for (int i = 0; i < roadLayers.Length; i++)
        {
            if (roadLayers[i] != null)
            {
                layerStartPositions[i] = roadLayers[i].anchoredPosition;
                layerStartScales[i] = roadLayers[i].localScale;
                layerCanvasGroups[i] = roadLayers[i].GetComponent<CanvasGroup>();
                if (layerCanvasGroups[i] == null)
                {
                    layerCanvasGroups[i] = roadLayers[i].gameObject.AddComponent<CanvasGroup>();
                }
            }
        }
    }

    void Update()
    {
        // --- УПРАВЛЕНИЕ (как в SceneMovement) ---
        if (useMouseInput)
        {
            if (Input.GetMouseButtonDown(0))
            {
                mouseStartPos = Input.mousePosition;
                isDragging = true;
            }

            if (Input.GetMouseButton(0) && isDragging)
            {
                float deltaX = (Input.mousePosition.x - mouseStartPos.x) * 0.5f;
                targetOffset = Mathf.Clamp(deltaX, -maxOffset, maxOffset);
            }

            if (Input.GetMouseButtonUp(0))
            {
                isDragging = false;
                targetOffset = 0f;
            }
        }

        if (autoMove && !isDragging)
        {
            // ТОЧНО КАК В SceneMovement
            float autoOffset = Mathf.Sin(Time.time * moveSpeed) * maxOffset;
            targetOffset = autoOffset;
        }

        // --- ПЛАВНОЕ ПЕРЕМЕЩЕНИЕ (как в SceneMovement) ---
        currentOffset = Mathf.Lerp(currentOffset, targetOffset, Time.deltaTime * 3f);

        // --- ОБНОВЛЯЕМ СЛОИ (уникальная часть Road25D) ---
        for (int i = 0; i < roadLayers.Length; i++)
        {
            if (roadLayers[i] == null) continue;

            float speed = layerSpeeds[i];
            RectTransform layer = roadLayers[i];

            Vector2 pos = layer.anchoredPosition;
            pos.x = layerStartPositions[i].x + currentOffset * speed;
            pos.y = layerStartPositions[i].y;

            if (enableVerticalOffset)
            {
                float yOffset = -currentOffset * speed * verticalOffsetIntensity * 0.3f;
                pos.y += yOffset;
            }

            layer.anchoredPosition = pos;

            if (enableScaleEffect)
            {
                float progress = Mathf.Abs(currentOffset) / maxOffset;
                float scaleFactor = 1f + progress * scaleIntensity * speed;
                layer.localScale = layerStartScales[i] * scaleFactor;
            }
        }
    }
}