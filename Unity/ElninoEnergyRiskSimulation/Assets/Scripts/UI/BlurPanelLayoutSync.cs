using Nova;
using NovaSamples.Effects;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nova BlurWindow 위치를 uGUI Sidebar_Left/Right에 맞춘다.
/// WebGL 등 해상도가 달라질 때 블러만 어긋나는 문제를 방지한다.
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

    private Vector2Int _lastScreenSize = Vector2Int.zero;

    private void Awake()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponent<Canvas>() ?? FindFirstObjectByType<Canvas>();

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
        SyncAll();
    }

    private void LateUpdate()
    {
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (screen == _lastScreenSize)
            return;

        _lastScreenSize = screen;
        SyncAll();
    }

    private void SyncAll()
    {
        SyncSidebarToBlur(leftSidebar, leftBlurPanel, leftBlurEffect);
        SyncSidebarToBlur(rightSidebar, rightBlurPanel, rightBlurEffect);
    }

    private static void SyncSidebarToBlur(RectTransform sidebar, UIBlock2D blurPanel, BlurEffect blurEffect)
    {
        if (sidebar == null || blurPanel == null)
            return;

        Canvas canvas = sidebar.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        RectTransform canvasRect = canvas.transform as RectTransform;
        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        Vector3[] corners = new Vector3[4];
        sidebar.GetWorldCorners(corners);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, corners[0], eventCamera, out Vector2 bottomLeft))
            return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, corners[2], eventCamera, out Vector2 topRight))
            return;

        Vector2 center = (bottomLeft + topRight) * 0.5f;
        Vector2 size = new Vector2(Mathf.Abs(topRight.x - bottomLeft.x), Mathf.Abs(topRight.y - bottomLeft.y));
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
