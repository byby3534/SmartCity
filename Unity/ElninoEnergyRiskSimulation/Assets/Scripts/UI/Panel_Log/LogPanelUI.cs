using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌측 로그 패널 — ScrollRect + 줄 단위 TMP. SimulationLog 구독.
/// </summary>
[AddComponentMenu("UI/Log Panel UI")]
public class LogPanelUI : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private float lineHeight = 22f;
    [SerializeField] private float fontSize = 12.5f;
    [SerializeField] private float indentPixels = 14f;
    [SerializeField] private int maxLines = 120;

    [Header("Colors")]
    [SerializeField] private Color normalColor = new(0.39f, 0.45f, 0.55f, 1f);
    [SerializeField] private Color mutedColor = new(0.55f, 0.58f, 0.65f, 1f);
    [SerializeField] private Color emphasisColor = new(0.06f, 0.09f, 0.16f, 1f);
    [SerializeField] private Color districtCompleteColor = new(0.85f, 0.19f, 0.19f, 1f);

    private readonly Queue<GameObject> _lines = new();

    private void Awake()
    {
        normalColor = DashboardColors.Hex(DashboardColors.HexTextSub);
        emphasisColor = DashboardColors.Hex(DashboardColors.HexText);
        mutedColor = new Color(normalColor.r, normalColor.g, normalColor.b, 0.75f);
        districtCompleteColor = DashboardColors.Hex(DashboardColors.HexAccent);

        EnsureScrollHierarchy();
        HidePlaceholderContent();
    }

    private void OnEnable()
    {
        SimulationLog.OnEntry += HandleLogEntry;
    }

    private void OnDisable()
    {
        SimulationLog.OnEntry -= HandleLogEntry;
    }

    private void HidePlaceholderContent()
    {
        Transform textGroup = transform.Find("Text_Group");
        if (textGroup == null)
            return;

        foreach (TMP_Text tmp in textGroup.GetComponentsInChildren<TMP_Text>(true))
            tmp.gameObject.SetActive(false);

        var groupImage = textGroup.GetComponent<Image>();
        if (groupImage != null)
            groupImage.raycastTarget = false;
    }

    private void EnsureScrollHierarchy()
    {
        if (contentRoot != null && scrollRect != null)
        {
            ConfigureScrollRect(scrollRect);
            return;
        }

        Transform existingScroll = transform.Find("Scroll");
        if (existingScroll != null)
        {
            scrollRect = existingScroll.GetComponent<ScrollRect>();
            contentRoot = existingScroll.Find("Viewport/Content") as RectTransform;
            if (scrollRect != null && contentRoot != null)
            {
                ConfigureScrollRect(scrollRect);
                return;
            }
        }

        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollGo.transform.SetParent(transform, false);
        var scrollRectTransform = scrollGo.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = Vector2.zero;
        scrollRectTransform.anchorMax = Vector2.one;
        scrollRectTransform.offsetMin = new Vector2(8f, 8f);
        scrollRectTransform.offsetMax = new Vector2(-8f, -4f);
        scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewport.transform, false);
        contentRoot = contentGo.GetComponent<RectTransform>();
        contentRoot.anchorMin = new Vector2(0f, 1f);
        contentRoot.anchorMax = new Vector2(1f, 1f);
        contentRoot.pivot = new Vector2(0.5f, 1f);
        contentRoot.anchoredPosition = Vector2.zero;
        contentRoot.sizeDelta = new Vector2(0f, 0f);

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 2f;
        layout.padding = new RectOffset(2, 2, 4, 4);

        contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect = scrollGo.GetComponent<ScrollRect>();
        scrollRect.viewport = viewport.GetComponent<RectTransform>();
        scrollRect.content = contentRoot;
        ConfigureScrollRect(scrollRect);
    }

    private static void ConfigureScrollRect(ScrollRect scroll)
    {
        if (scroll == null) return;

        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.horizontalScrollbar = null;
        scroll.scrollSensitivity = 24f;
    }

    private void HandleLogEntry(SimulationLogEntry entry)
    {
        if (entry.IsClear)
        {
            ClearLines();
            return;
        }

        AppendLine(entry.Message, entry.Style, entry.Indent);
    }

    public void AppendLine(string message, LogLineStyle style = LogLineStyle.Normal, int indent = 0)
    {
        if (contentRoot == null)
            EnsureScrollHierarchy();

        GameObject lineGo = CreateLineObject();
        lineGo.transform.SetParent(contentRoot, false);

        var tmp = lineGo.GetComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.color = ResolveColor(style);
        tmp.margin = new Vector4(indent * indentPixels, 0f, 0f, 0f);

        _lines.Enqueue(lineGo);
        TrimOldLines();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
        ScrollToBottom();
    }

    private GameObject CreateLineObject()
    {
        var go = new GameObject("LogLine", typeof(RectTransform));
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = lineHeight;
        le.preferredHeight = lineHeight;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        if (font != null)
            tmp.font = font;

        return go;
    }

    private Color ResolveColor(LogLineStyle style)
    {
        return style switch
        {
            LogLineStyle.Muted => mutedColor,
            LogLineStyle.Emphasis => emphasisColor,
            LogLineStyle.DistrictComplete => districtCompleteColor,
            _ => normalColor,
        };
    }

    private void TrimOldLines()
    {
        while (_lines.Count > maxLines)
        {
            GameObject oldest = _lines.Dequeue();
            if (oldest != null)
                Destroy(oldest);
        }
    }

    private void ClearLines()
    {
        while (_lines.Count > 0)
        {
            GameObject line = _lines.Dequeue();
            if (line != null)
                Destroy(line);
        }
    }

    private void ScrollToBottom()
    {
        if (scrollRect == null)
            return;

        scrollRect.velocity = Vector2.zero;
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
