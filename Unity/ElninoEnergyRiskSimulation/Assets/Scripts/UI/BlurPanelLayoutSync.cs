using System.Collections;
using Nova;
using NovaSamples.Effects;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nova BlurWindow 위치를 uGUI Sidebar_Left/Right에 맞춘다.
/// WebGL 등 해상도·캔버스 스케일이 달라질 때 블러만 어긋나는 문제를 방지한다.
/// </summary>
[DefaultExecutionOrder(100)]
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
    private Vector2Int _lastScreenSize = Vector2Int.zero;
    private Vector2 _lastCanvasRectSize = Vector2.zero;
    private float _lastScaleFactor = -1f;

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
    }

    private void Start()
    {
        WebGLRuntimeTuning.TuneBlurGroups();
        SyncAll();
        StartCoroutine(SyncUntilStable());
    }

    private IEnumerator SyncUntilStable()
    {
        // WebGL은 첫 프레임에 캔버스 스케일이 안정되지 않을 수 있다.
        for (int i = 0; i < 90; i++)
        {
            SyncAll();
            yield return null;
        }
    }

    private void LateUpdate()
    {
        if (!IsLayoutDirty())
            return;

        CacheLayoutState();
        SyncAll();
    }

    private bool IsLayoutDirty()
    {
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (screen != _lastScreenSize)
            return true;

        if (rootCanvas == null)
            return false;

        var canvasRect = (RectTransform)rootCanvas.transform;
        if (canvasRect.rect.size != _lastCanvasRectSize)
            return true;

        if (_canvasScaler != null && !Mathf.Approximately(_canvasScaler.scaleFactor, _lastScaleFactor))
            return true;

        return false;
    }

    private void CacheLayoutState()
    {
        _lastScreenSize = new Vector2Int(Screen.width, Screen.height);

        if (rootCanvas == null)
            return;

        var canvasRect = (RectTransform)rootCanvas.transform;
        _lastCanvasRectSize = canvasRect.rect.size;
        _lastScaleFactor = _canvasScaler != null ? _canvasScaler.scaleFactor : 1f;
    }

    private void SyncAll()
    {
        if (rootCanvas == null)
            return;

        SyncSidebarToBlur(leftSidebar, leftBlurPanel, leftBlurEffect);
        SyncSidebarToBlur(rightSidebar, rightBlurPanel, rightBlurEffect);

#if UNITY_WEBGL && !UNITY_EDITOR
        foreach (var group in FindObjectsByType<BackgroundBlurGroup>(FindObjectsSortMode.None))
            group.UpdateEffects(immediately: true);
#endif
    }

    private void SyncSidebarToBlur(RectTransform sidebar, UIBlock2D blurPanel, BlurEffect blurEffect)
    {
        if (sidebar == null || blurPanel == null || rootCanvas == null)
            return;

        var canvasRect = (RectTransform)rootCanvas.transform;

        Vector3[] corners = new Vector3[4];
        sidebar.GetWorldCorners(corners);

        Vector2 bottomLeft = canvasRect.InverseTransformPoint(corners[0]);
        Vector2 topRight = canvasRect.InverseTransformPoint(corners[2]);

        Vector2 center = (bottomLeft + topRight) * 0.5f;
        Vector2 size = new Vector2(
            Mathf.Abs(topRight.x - bottomLeft.x),
            Mathf.Abs(topRight.y - bottomLeft.y));
        Vector2 canvasSize = canvasRect.rect.size;

        ref var layout = ref blurPanel.Layout;
        layout.Position.X.Raw = center.x - canvasSize.x * 0.5f;
        layout.Position.Y.Raw = center.y - canvasSize.y * 0.5f;
        layout.Size.X.Raw = size.x;
        layout.Size.Y.Raw = size.y;

        blurPanel.CalculateLayout();
        blurEffect?.Reblur();
    }
}
