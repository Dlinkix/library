using UnityEngine;
using UnityEngine.UI;

public class SceneMovement : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private RectTransform backgroundImage; // Само изображение
    [SerializeField] private float moveSpeed = 2f; // Скорость движения
    [SerializeField] private float maxOffset = 200f; // Максимальное смещение в пикселях
    [SerializeField] private bool autoMove = true; // Автоматическое движение
    [SerializeField] private bool useMouseInput = true; // Движение от мыши

    [Header("Parallax Layers (Optional)")]
    [SerializeField] private RectTransform[] parallaxLayers; // Дополнительные слои для параллакса
    [SerializeField] private float[] parallaxSpeeds; // Скорости для каждого слоя (0.1 - 0.9)

    private Vector2 startPosition;
    private float currentOffset;
    private float targetOffset;
    private Vector2 mouseStartPos;
    private bool isDragging;

    void Start()
    {
        if (backgroundImage != null)
        {
            startPosition = backgroundImage.anchoredPosition;
        }

        // Если есть параллакс-слои, сохраняем их стартовые позиции
        if (parallaxLayers != null && parallaxLayers.Length > 0)
        {
            // Если скорости не заданы, создаем автоматически
            if (parallaxSpeeds == null || parallaxSpeeds.Length == 0)
            {
                parallaxSpeeds = new float[parallaxLayers.Length];
                for (int i = 0; i < parallaxLayers.Length; i++)
                {
                    // Каждый следующий слой медленнее на 0.15
                    parallaxSpeeds[i] = 1f - (i + 1) * 0.15f;
                    parallaxSpeeds[i] = Mathf.Clamp01(parallaxSpeeds[i]);
                }
            }
        }
    }

    void Update()
    {
        if (backgroundImage == null) return;

        // --- УПРАВЛЕНИЕ МЫШЬЮ (Drag) ---
        if (useMouseInput)
        {
            if (Input.GetMouseButtonDown(0))
            {
                mouseStartPos = Input.mousePosition;
                isDragging = true;
            }

            if (Input.GetMouseButton(0) && isDragging)
            {
                Vector2 currentMousePos = Input.mousePosition;
                float deltaX = (currentMousePos.x - mouseStartPos.x) * 0.5f; // Чувствительность
                targetOffset = Mathf.Clamp(deltaX, -maxOffset, maxOffset);
            }

            if (Input.GetMouseButtonUp(0))
            {
                isDragging = false;
                // Плавно возвращаем в центр
                targetOffset = 0f;
            }
        }

        // --- АВТОМАТИЧЕСКОЕ ДВИЖЕНИЕ ---
        if (autoMove && !isDragging)
        {
            // Плавное качание влево-вправо
            float autoOffset = Mathf.Sin(Time.time * moveSpeed) * maxOffset;
            targetOffset = autoOffset;
        }

        // --- ПЛАВНОЕ ПЕРЕМЕЩЕНИЕ (Smooth) ---
        currentOffset = Mathf.Lerp(currentOffset, targetOffset, Time.deltaTime * 3f);

        // --- ПРИМЕНЯЕМ СМЕЩЕНИЕ К ФОНУ ---
        Vector2 newPos = startPosition;
        newPos.x += currentOffset;
        backgroundImage.anchoredPosition = newPos;

        // --- ПАРАЛЛАКС ДЛЯ ДОПОЛНИТЕЛЬНЫХ СЛОЕВ ---
        if (parallaxLayers != null)
        {
            for (int i = 0; i < parallaxLayers.Length && i < parallaxSpeeds.Length; i++)
            {
                if (parallaxLayers[i] != null)
                {
                    Vector2 layerPos = startPosition;
                    layerPos.x += currentOffset * parallaxSpeeds[i];
                    parallaxLayers[i].anchoredPosition = layerPos;
                }
            }
        }
    }

    // --- МЕТОДЫ ДЛЯ ВНЕШНЕГО УПРАВЛЕНИЯ ---

    // Сдвинуть сцену на определенное расстояние
    public void MoveScene(float offset)
    {
        targetOffset = Mathf.Clamp(offset, -maxOffset, maxOffset);
    }

    // Плавно вернуть сцену в центр
    public void ResetScene()
    {
        targetOffset = 0f;
    }

    // Установить скорость движения
    public void SetSpeed(float speed)
    {
        moveSpeed = speed;
    }

    // Установить максимальное смещение
    public void SetMaxOffset(float max)
    {
        maxOffset = max;
    }

    // Включить/выключить автодвижение
    public void SetAutoMove(bool enabled)
    {
        autoMove = enabled;
    }
}