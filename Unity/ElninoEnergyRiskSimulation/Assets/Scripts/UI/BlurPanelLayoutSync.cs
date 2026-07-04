using System.Collections;
using Nova;
using NovaSamples.Effects;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nova BlurWindow 위치/크기를 uGUI Sidebar에 맞춘다.
/// Transform 회전/위치는 Nova Layout이 매 프레임 덮어쓴다 — Inspector Rotation은 적용되지 않는다.
/// </summary>
[DefaultExecutionOrder(200)]
public class BlurPanelLayoutSync : MonoBehaviour
{
    [SerializeField] private RectTransform leftSidebar;
    [SerializeField] private RectTransform rightSidebar;
    [SerializeField] private UIBlock2D leftBlurPanel;
    [SerializeField] private UIBlock2D rightBlurPanel;
    [SerializeField] private BlurEffect leftBlurEffect;
    [SerializeField] private BlurEffect rightBlurEffect;
    [SerializeField] private Canvas rootCanvas;

    private CanvasScaler _canvasScaler;
    private ScreenSpace _leftScreenSpace;
    private ScreenSpace _rightScreenSpace;
    private Vector2Int _lastScreenSize = Vector2Int.zero;
    private Vector2 _lastCanvasRectSize = Vector2.zero;
    private float _lastScaleFactor = -1f;
    private float _lastLeftCenterX = float.NaN;
    private float _lastRightCenterX = float.NaN;
    private float _lastLeftWidth = -1f;
    private float _lastRightWidth = -1f;

    private void Awake()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponent<Canvas>() ?? FindFirstObjectByType<Canvas>();

        if (rootCanvas != null)
            _canvasScaler = rootCanvas.GetComponent<CanvasScaler>();

        if (leftSidebar == null)
            leftSidebar = GameObject.Find("Sidebar_Left")?.GetComponent<RectTransform>();
        if (rightSidebar == null)
            rightSidebar = GameObject.Find("Sidebar_Right")?.GetComponent<RectTransform>();

        Transform leftSetup = GameObject.Find("GameBlurSetup_left")?.transform;
        Transform rightSetup = GameObject.Find("GameBlurSetup_right")?.transform;

        if (leftBlurPanel == null && leftSetup != null)
            leftBlurPanel = leftSetup.GetComponentInChildren<UIBlock2D>(true);
        if (rightBlurPanel == null && rightSetup != null)
            rightBlurPanel = rightSetup.GetComponentInChildren<UIBlock2D>(true);

        if (leftBlurEffect == null && leftBlurPanel != null)
            leftBlurEffect = leftBlurPanel.GetComponent<BlurEffect>();
        if (rightBlurEffect == null && rightBlurPanel != null)
            rightBlurEffect = rightBlurPanel.GetComponent<BlurEffect>();

        if (leftBlurPanel != null)
            _leftScreenSpace = leftBlurPanel.GetComponentInParent<ScreenSpace>();
        if (rightBlurPanel != null)
            _rightScreenSpace = rightBlurPanel.GetComponentInParent<ScreenSpace>();
    }

    private void OnEnable()
    {
        if (_leftScreenSpace != null)
            _leftScreenSpace.OnPostCameraSync += HandleNovaCameraSync;
        if (_rightScreenSpace != null)
            _rightScreenSpace.OnPostCameraSync += HandleNovaCameraSync;
    }

    private void OnDisable()
    {
        if (_leftScreenSpace != null)
            _leftScreenSpace.OnPostCameraSync -= HandleNovaCameraSync;
        if (_rightScreenSpace != null)
            _rightScreenSpace.OnPostCameraSync -= HandleNovaCameraSync;
    }

    private void Start()
    {
        WebGLRuntimeTuning.TuneAll();
        SyncAll();
        StartCoroutine(SyncUntilStable());
    }

    private void HandleNovaCameraSync()
    {
        if (!IsLayoutDirty())
            return;

        SyncAll();
        CacheLayoutState();
    }

    private IEnumerator SyncUntilStable()
    {
        for (int i = 0; i < 5; i++)
        {
            SyncAll();
            yield return null;
        }

        CacheLayoutState();
    }

    private void LateUpdate()
    {
        if (!IsLayoutDirty())
            return;

        SyncAll();
        CacheLayoutState();
    }

    private bool IsLayoutDirty()
    {
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (screen != _lastScreenSize)
            return true;

        if (rootCanvas == null)
            return false;

        float canvasWidth = GetCanvasWidth((RectTransform)rootCanvas.transform);
        if (canvasWidth <= 0f)
            return false;

        if (_canvasScaler != null && !Mathf.Approximately(_canvasScaler.scaleFactor, _lastScaleFactor))
            return true;

        if (leftSidebar != null &&
            TryGetSidebarHorizontalBounds(leftSidebar, canvasWidth, out float leftCenterX, out float leftWidth))
        {
            if (!Mathf.Approximately(leftCenterX, _lastLeftCenterX) ||
                !Mathf.Approximately(leftWidth, _lastLeftWidth))
                return true;
        }

        if (rightSidebar != null &&
            TryGetSidebarHorizontalBounds(rightSidebar, canvasWidth, out float rightCenterX, out float rightWidth))
        {
            if (!Mathf.Approximately(rightCenterX, _lastRightCenterX) ||
                !Mathf.Approximately(rightWidth, _lastRightWidth))
                return true;
        }

        return false;
    }

    private void CacheLayoutState()
    {
        _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        _lastScaleFactor = _canvasScaler != null ? _canvasScaler.scaleFactor : 1f;

        if (rootCanvas == null)
            return;

        var canvasRect = (RectTransform)rootCanvas.transform;
        _lastCanvasRectSize = canvasRect.rect.size;

        float canvasWidth = GetCanvasWidth(canvasRect);
        if (canvasWidth <= 0f)
            return;

        if (leftSidebar != null &&
            TryGetSidebarHorizontalBounds(leftSidebar, canvasWidth, out float leftCenterX, out float leftWidth))
        {
            _lastLeftCenterX = leftCenterX;
            _lastLeftWidth = leftWidth;
        }

        if (rightSidebar != null &&
            TryGetSidebarHorizontalBounds(rightSidebar, canvasWidth, out float rightCenterX, out float rightWidth))
        {
            _lastRightCenterX = rightCenterX;
            _lastRightWidth = rightWidth;
        }
    }

    private float GetCanvasWidth(RectTransform canvasRect)
    {
        float width = canvasRect.rect.width;
        if (width > 0f)
            return width;

        if (_canvasScaler != null)
            return _canvasScaler.referenceResolution.x;

        return 1920f;
    }

    private void SyncAll()
    {
        if (rootCanvas == null)
            return;

        Canvas.ForceUpdateCanvases();

        var canvasRect = (RectTransform)rootCanvas.transform;
        float canvasWidth = GetCanvasWidth(canvasRect);
        if (canvasWidth <= 0f)
            return;

        bool leftChanged = SyncSidebarToBlur(leftSidebar, leftBlurPanel, leftBlurEffect, canvasWidth);
        bool rightChanged = SyncSidebarToBlur(rightSidebar, rightBlurPanel, rightBlurEffect, canvasWidth);

        if (!leftChanged && !rightChanged)
            return;

        foreach (var group in FindObjectsByType<BackgroundBlurGroup>(FindObjectsSortMode.None))
            group.UpdateEffects(immediately: true);
    }

    /// <summary>
    /// Canvas 좌하단 원점 기준 가로 bounds.
    /// CalculateRelativeRectTransformBounds / GetWorldCorners는 RootCanvas scale 0일 때 비정상 값을 반환한다.
    /// </summary>
    private static bool TryGetSidebarHorizontalBounds(
        RectTransform sidebar,
        float canvasWidth,
        out float centerX,
        out float width)
    {
        centerX = 0f;
        width = sidebar.sizeDelta.x > 0f ? sidebar.sizeDelta.x : sidebar.rect.width;

        if (width <= 0f || canvasWidth <= 0f)
            return false;

        if (Mathf.Approximately(sidebar.anchorMin.x, sidebar.anchorMax.x))
        {
            float anchorX = sidebar.anchorMin.x * canvasWidth;
            centerX = anchorX + sidebar.anchoredPosition.x + width * (0.5f - sidebar.pivot.x);
        }
        else
        {
            float anchorCenterX = (sidebar.anchorMin.x + sidebar.anchorMax.x) * 0.5f * canvasWidth;
            centerX = anchorCenterX + sidebar.anchoredPosition.x;
        }

        if (centerX < 0f || centerX > canvasWidth || width > canvasWidth)
            return false;

        return true;
    }

    private static bool SyncSidebarToBlur(
        RectTransform sidebar,
        UIBlock2D blurPanel,
        BlurEffect blurEffect,
        float canvasWidth)
    {
        if (sidebar == null || blurPanel == null)
            return false;

        if (!TryGetSidebarHorizontalBounds(sidebar, canvasWidth, out float centerX, out float width))
            return false;

        float positionX = centerX - canvasWidth * 0.5f;

        ref var layout = ref blurPanel.Layout;
        if (Mathf.Approximately(layout.Size.X.Raw, width) &&
            layout.Size.X.Type == LengthType.Value &&
            Mathf.Approximately(layout.Size.Y.Percent, 1f) &&
            Mathf.Approximately(layout.Position.X.Raw, positionX) &&
            layout.Position.X.Type == LengthType.Value &&
            Mathf.Approximately(layout.Position.Y.Raw, 0f))
        {
            return false;
        }

        layout.Alignment = Alignment.Center;

        layout.Size.X.Raw = width;
        layout.Size.X.Type = LengthType.Value;
        layout.Size.Y.Percent = 1f;

        layout.Position.X.Raw = positionX;
        layout.Position.X.Type = LengthType.Value;
        layout.Position.Y.Raw = 0f;
        layout.Position.Y.Type = LengthType.Value;

        blurPanel.CalculateLayout();
        blurEffect?.Reblur();
        return true;
    }
}
