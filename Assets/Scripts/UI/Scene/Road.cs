using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class Road25D : MonoBehaviour
{
    [Header("Road Layers")]
    [SerializeField] private RectTransform[] roadLayers;
    [SerializeField] private float[] layerSpeeds;

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

    [Header("Splash Screen")]
    [SerializeField] private RectTransform splashImage;
    [SerializeField] private float splashDuration = 2f;
    [SerializeField] private float flyOutDuration = 0.8f;
    [SerializeField] private Vector2 flyOutOffset = new Vector2(-500f, 800f);
    [SerializeField] private AnimationCurve flyOutCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private float currentOffset;
    private float targetOffset;
    private Vector2 mouseStartPos;
    private bool isDragging;
    private Vector2[] layerStartPositions;
    private Vector3[] layerStartScales;

    private enum SplashState { Showing, FlyingOut, Hidden, GameReady }
    private SplashState splashState = SplashState.Showing;
    private float splashTimer;
    private Vector2 splashStartPos;

    private float gameStartTime;
    private bool isGameStarted;

    void Start()
    {
        if (roadLayers == null || roadLayers.Length == 0) return;

        // Инициализация слоев
        layerStartPositions = new Vector2[roadLayers.Length];
        layerStartScales = new Vector3[roadLayers.Length];

        if (layerSpeeds == null || layerSpeeds.Length == 0)
        {
            layerSpeeds = new float[roadLayers.Length];
            for (int i = 0; i < roadLayers.Length; i++)
            {
                layerSpeeds[i] = 0.1f + i * 0.2f;
            }
        }

        for (int i = 0; i < roadLayers.Length; i++)
        {
            if (roadLayers[i] != null)
            {
                layerStartPositions[i] = roadLayers[i].anchoredPosition;
                layerStartScales[i] = roadLayers[i].localScale;
            }
        }

        // Настройка заставки
        if (splashImage != null)
        {
            splashStartPos = splashImage.anchoredPosition;
            splashImage.SetAsLastSibling();
        }

        splashState = SplashState.Showing;
        splashTimer = 0f;
        isGameStarted = false;
        gameStartTime = 0f;

        currentOffset = 0f;
        targetOffset = 0f;
    }

    void Update()
    {
        // Обработка заставки
        if (splashState != SplashState.GameReady && splashState != SplashState.Hidden)
        {
            UpdateSplash();
            return;
        }

        if (splashState == SplashState.Hidden)
        {
            return;
        }

        // Запоминаем время старта игры
        if (!isGameStarted)
        {
            isGameStarted = true;
            gameStartTime = Time.time;
        }

        // Плавный старт
        float timeSinceStart = Time.time - gameStartTime;
        float startFadeIn = Mathf.Clamp01(timeSinceStart / 1.5f);

        // --- УПРАВЛЕНИЕ ---
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
            float autoOffset = Mathf.Sin(timeSinceStart * moveSpeed) * maxOffset;
            targetOffset = autoOffset * startFadeIn;
        }

        currentOffset = Mathf.Lerp(currentOffset, targetOffset, Time.deltaTime * 3f);

        // --- ОБНОВЛЯЕМ СЛОИ ---
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

    void UpdateSplash()
    {
        splashTimer += Time.deltaTime;

        switch (splashState)
        {
            case SplashState.Showing:
                if (splashTimer >= splashDuration)
                {
                    splashState = SplashState.FlyingOut;
                    splashTimer = 0f;
                }
                break;

            case SplashState.FlyingOut:
                if (splashImage != null)
                {
                    float progress = Mathf.Clamp01(splashTimer / flyOutDuration);
                    float easedProgress = flyOutCurve.Evaluate(progress);

                    Vector2 currentPos = splashStartPos + flyOutOffset * easedProgress;
                    splashImage.anchoredPosition = currentPos;

                    if (progress >= 1f)
                    {
                        splashImage.gameObject.SetActive(false);
                        splashState = SplashState.Hidden;
                        splashState = SplashState.GameReady;
                    }
                }
                break;
        }
    }

    void SkipSplash()
    {
        if (splashState == SplashState.Showing || splashState == SplashState.FlyingOut)
        {
            if (splashImage != null)
            {
                splashImage.gameObject.SetActive(false);
            }
            splashState = SplashState.GameReady;
        }
    }
}