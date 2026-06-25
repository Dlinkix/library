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

    [Header("Audio")]
    [SerializeField] private AudioClip[] battleThemes;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float maxVolume = 0.04f;
    [SerializeField] private float fadeInDuration = 2f;
    private int currentThemeIndex = 0;
    private Coroutine fadeInCoroutine;

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
        if (splashImage != null) splashStartPos = splashImage.anchoredPosition;
        CacheLayerState();
        RestartSplashSequence();
    }

    private void CacheLayerState()
    {
        if (roadLayers == null || roadLayers.Length == 0) return;

        layerStartPositions = new Vector2[roadLayers.Length];
        layerStartScales = new Vector3[roadLayers.Length];

        if (layerSpeeds == null || layerSpeeds.Length == 0)
        {
            layerSpeeds = new float[roadLayers.Length];
            for (int i = 0; i < roadLayers.Length; i++) layerSpeeds[i] = 0.1f + i * 0.2f;
        }

        for (int i = 0; i < roadLayers.Length; i++)
        {
            if (roadLayers[i] != null)
            {
                layerStartPositions[i] = roadLayers[i].anchoredPosition;
                layerStartScales[i] = roadLayers[i].localScale;
            }
        }
    }

    public void RestartSplashSequence()
    {
        if (splashImage != null)
        {
            splashImage.gameObject.SetActive(true);
            splashImage.anchoredPosition = Vector2.zero;
            splashImage.SetAsLastSibling();
        }

        splashState = SplashState.Showing;
        splashTimer = 0f;
        isGameStarted = false;
        gameStartTime = 0f;
        currentOffset = 0f;
        targetOffset = 0f;
        isDragging = false;

        if (roadLayers == null) return;

        for (int i = 0; i < roadLayers.Length; i++)
        {
            if (roadLayers[i] == null) continue;
            if (layerStartPositions != null && i < layerStartPositions.Length) roadLayers[i].anchoredPosition = layerStartPositions[i];
            if (layerStartScales != null && i < layerStartScales.Length) roadLayers[i].localScale = layerStartScales[i];
        }
    }

    void Update()
    {
        if (splashState != SplashState.GameReady && splashState != SplashState.Hidden) { UpdateSplash(); return; }
        if (splashState == SplashState.Hidden) return;

        if (!isGameStarted)
        {
            isGameStarted = true;
            gameStartTime = Time.time;
            PlayNextTheme();
        }

        float timeSinceStart = Time.time - gameStartTime;
        float startFadeIn = Mathf.Clamp01(timeSinceStart / 1.5f);

        if (useMouseInput)
        {
            if (Input.GetMouseButtonDown(0)) { mouseStartPos = Input.mousePosition; isDragging = true; }
            if (Input.GetMouseButton(0) && isDragging) targetOffset = Mathf.Clamp((Input.mousePosition.x - mouseStartPos.x) * 0.5f, -maxOffset, maxOffset);
            if (Input.GetMouseButtonUp(0)) { isDragging = false; targetOffset = 0f; }
        }

        if (battleThemes != null && battleThemes.Length > 0 && audioSource != null && !audioSource.isPlaying && isGameStarted) PlayNextTheme();

        if (autoMove && !isDragging) targetOffset = Mathf.Sin(timeSinceStart * moveSpeed) * maxOffset * startFadeIn;

        currentOffset = Mathf.Lerp(currentOffset, targetOffset, Time.deltaTime * 3f);

        for (int i = 0; i < roadLayers.Length; i++)
        {
            if (roadLayers[i] == null) continue;

            float speed = layerSpeeds[i];
            RectTransform layer = roadLayers[i];
            Vector2 pos = layer.anchoredPosition;
            pos.x = layerStartPositions[i].x + currentOffset * speed;
            pos.y = layerStartPositions[i].y;

            if (enableVerticalOffset) pos.y += -currentOffset * speed * verticalOffsetIntensity * 0.3f;
            layer.anchoredPosition = pos;

            if (enableScaleEffect)
            {
                float progress = Mathf.Abs(currentOffset) / maxOffset;
                layer.localScale = layerStartScales[i] * (1f + progress * scaleIntensity * speed);
            }
        }
    }

    private void PlayNextTheme()
    {
        if (battleThemes == null || battleThemes.Length == 0 || audioSource == null) return;

        currentThemeIndex = (currentThemeIndex + 1) % battleThemes.Length;
        audioSource.clip = battleThemes[currentThemeIndex];
        audioSource.volume = 0f;
        audioSource.Play();

        if (fadeInCoroutine != null) StopCoroutine(fadeInCoroutine);
        fadeInCoroutine = StartCoroutine(FadeInVolume());
    }

    private IEnumerator FadeInVolume()
    {
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(0f, maxVolume, elapsed / fadeInDuration);
            yield return null;
        }
        audioSource.volume = maxVolume;
    }

    public void StopAllThemes() => audioSource?.Stop();

    void UpdateSplash()
    {
        splashTimer += Time.deltaTime;

        switch (splashState)
        {
            case SplashState.Showing:
                if (splashTimer >= splashDuration) { splashState = SplashState.FlyingOut; splashTimer = 0f; }
                break;
            case SplashState.FlyingOut:
                if (splashImage != null)
                {
                    float progress = Mathf.Clamp01(splashTimer / flyOutDuration);
                    float easedProgress = flyOutCurve.Evaluate(progress);
                    splashImage.anchoredPosition = splashStartPos + flyOutOffset * easedProgress;
                    if (progress >= 1f) { splashImage.gameObject.SetActive(false); splashState = SplashState.GameReady; }
                }
                break;
        }
    }
}