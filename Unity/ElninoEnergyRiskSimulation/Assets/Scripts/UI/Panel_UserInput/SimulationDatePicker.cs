using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 시뮬레이션 시점 — YEAR / MONTH 카드 + 팝업 피커.
/// 팝업 UI 생성·배치만 담당하며, 연/월 확정(commit) 판단은 UIController가 한다.
/// 버튼 클릭 시 OnYearPicked/OnMonthPicked만 발행한다.
/// </summary>
[AddComponentMenu("UI/Simulation Date Picker")]
public class SimulationDatePicker : MonoBehaviour
{
    private const string PopupOverlayName = "DatePickerOverlay";
    private const string BackdropName = "Backdrop";

    [Header("Cards")]
    [SerializeField] private Button yearCardButton;
    [SerializeField] private Button monthCardButton;
    [SerializeField] private TMP_Text yearValueText;
    [SerializeField] private TMP_Text monthValueText;

    [Header("Status (optional)")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject statusCheckIcon;

    [Header("Popups (optional — 비어 있으면 런타임 생성)")]
    [SerializeField] private RectTransform popupRoot;
    [SerializeField] private GameObject yearPopup;
    [SerializeField] private GameObject monthPopup;

    [Header("Range")]
    [SerializeField] private int minYear = 2005;
    [SerializeField] private int maxYear = 2040;

    [Header("Placeholder")]
    [SerializeField] private string yearPlaceholder = "-";
    [SerializeField] private string monthPlaceholder = "-";

    [Header("Messages")]
    [SerializeField] private string promptMessage = "연/월을 선택하세요";
    [SerializeField] private string loadedMessage = "기준 ONI 데이터 로드됨";

    [Header("Dropdown Style")]
    [SerializeField] private float dropdownGap = 4f;
    [SerializeField] private float yearDropdownHeight = 168f;
    [SerializeField] private int yearGridColumns = 3;
    [SerializeField] private float yearCellHeight = 28f;
    [SerializeField] private float yearMinCellWidth = 56f;
    [SerializeField] private float yearGridSpacing = 2f;
    [SerializeField] private float yearGridPadding = 4f;
    [SerializeField] private int monthGridColumns = 3;
    [SerializeField] private float monthCellHeight = 32f;
    [SerializeField] private float monthGridSpacing = 4f;
    [SerializeField] private float monthGridPadding = 6f;
    [SerializeField] private float scrollSensitivity = 45f;

    // CreateScrollContent 내부 offsetMin/offsetMax 와 동일하게 유지
    private const float ScrollInsetLeft = 4f;
    private const float ScrollInsetRight = 4f;
    private const float ScrollInsetRightWithScrollbar = 10f;

    // 카드/팝업에서 값을 고르는 순간 발행 — 확정(연+월 모두 채워졌는지) 판단은 UIController가 한다.
    public event Action<int> OnYearPicked;
    public event Action<int> OnMonthPicked;

    private Transform _popupOverlay;
    private GameObject _backdrop;
    private RectTransform _yearCardRect;
    private RectTransform _monthCardRect;
    private RectTransform _dateRowRect;
    private Canvas _rootCanvas;

    private void Awake()
    {
        // Sidebar 안에서 Simulation_List/ONI 패널과 영역이 겹칠 때 연·월 카드 클릭이 막히지 않도록 맨 앞으로 올린다.
        transform.SetAsLastSibling();

        ResolveReferences();
        DisablePanelBackgroundRaycast();
        EnsureCardClickable(yearCardButton);
        EnsureCardClickable(monthCardButton);
        ResetToPlaceholder();

        yearCardButton?.onClick.AddListener(OpenYearPopup);
        monthCardButton?.onClick.AddListener(OpenMonthPopup);

        if (yearPopup != null || monthPopup != null)
            WireEditorPopups();

        SetStatusLoaded(false);
    }

    private void OnDestroy()
    {
        if (yearCardButton != null) yearCardButton.onClick.RemoveListener(OpenYearPopup);
        if (monthCardButton != null) monthCardButton.onClick.RemoveListener(OpenMonthPopup);
    }

    public void SetStatusLoaded(bool loaded)
    {
        if (statusText != null)
            statusText.text = loaded ? loadedMessage : promptMessage;
        if (statusCheckIcon != null)
            statusCheckIcon.SetActive(loaded);
    }

    private void ResolveReferences()
    {
        if (yearCardButton == null)
            yearCardButton = transform.Find("Row_DateCards/Btn_YearCard")?.GetComponent<Button>();
        if (monthCardButton == null)
            monthCardButton = transform.Find("Row_DateCards/Btn_MonthCard")?.GetComponent<Button>();

        if (yearValueText == null && yearCardButton != null)
            yearValueText = FindValueText(yearCardButton.transform, "Text_YearValue");
        if (monthValueText == null && monthCardButton != null)
            monthValueText = FindValueText(monthCardButton.transform, "Text_MonthValue", "Text_YearValue");

        if (statusText == null)
            statusText = transform.Find("Text_Status")?.GetComponent<TMP_Text>();
        if (statusCheckIcon == null)
        {
            var check = transform.Find("Icon_StatusCheck");
            if (check != null) statusCheckIcon = check.gameObject;
        }

        _rootCanvas = GetComponentInParent<Canvas>();
        _dateRowRect = transform.Find("Row_DateCards")?.GetComponent<RectTransform>();
        if (yearCardButton != null)
            _yearCardRect = yearCardButton.GetComponent<RectTransform>();
        if (monthCardButton != null)
            _monthCardRect = monthCardButton.GetComponent<RectTransform>();
    }

    private float GetDropdownWidth(RectTransform preferred, RectTransform fallback = null)
    {
        if (preferred != null && preferred.rect.width > 0f)
            return preferred.rect.width;
        if (fallback != null && fallback.rect.width > 0f)
            return fallback.rect.width;
        return 280f;
    }

    private static float GetYearGridViewportWidth(float dropdownWidth, bool withScrollbar)
    {
        float rightInset = withScrollbar ? ScrollInsetRightWithScrollbar : ScrollInsetRight;
        return dropdownWidth - ScrollInsetLeft - rightInset;
    }

    private RectTransform GetYearPlacementRect() => _dateRowRect != null ? _dateRowRect : _yearCardRect;

    private void ResetToPlaceholder()
    {
        if (yearValueText != null)
            yearValueText.text = yearPlaceholder;
        if (monthValueText != null)
            monthValueText.text = monthPlaceholder;
    }

    /// <summary>패널 배경이 자식 버튼 클릭을 삼키지 않도록 raycast 비활성화.</summary>
    private void DisablePanelBackgroundRaycast()
    {
        if (TryGetComponent<Graphic>(out var panelBg))
            panelBg.raycastTarget = false;
    }

    private static void EnsureCardClickable(Button button)
    {
        if (button == null) return;

        DisableChildRaycasts(button);

        var decorative = button.GetComponent<Graphic>();
        if (decorative != null && decorative.material != null &&
            decorative.material.name.IndexOf("Blur", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            decorative.raycastTarget = false;
        }

        var hitArea = EnsureHitArea(button);
        button.targetGraphic = hitArea;
    }

    /// <summary>자식 TMP/Image가 클릭을 가로채지 않도록 raycast 비활성화.</summary>
    private static void DisableChildRaycasts(Button button)
    {
        if (button == null) return;

        foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.gameObject == button.gameObject)
                continue;
            graphic.raycastTarget = false;
        }
    }

    /// <summary>Blur 등 커스텀 Material이 있어도 클릭이 되도록 투명 hit area 추가.</summary>
    private static Image EnsureHitArea(Button button)
    {
        const string hitAreaName = "HitArea";
        var existing = button.transform.Find(hitAreaName);
        if (existing != null && existing.TryGetComponent(out Image cached))
            return cached;

        var go = new GameObject(hitAreaName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(button.transform, false);
        go.transform.SetAsFirstSibling();
        Stretch(go.GetComponent<RectTransform>());

        var hit = go.GetComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0.01f);
        hit.raycastTarget = true;
        return hit;
    }

    private static TMP_Text FindValueText(Transform card, params string[] preferredNames)
    {
        foreach (string name in preferredNames)
        {
            var t = card.Find(name);
            if (t != null && t.TryGetComponent(out TMP_Text tmp))
                return tmp;
        }

        foreach (var tmp in card.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!tmp.name.Contains("Label"))
                return tmp;
        }

        return null;
    }

    private void WireEditorPopups()
    {
        if (yearPopup != null)
        {
            WirePopupButtons(yearPopup, SelectYear);
            ConfigurePopupScroll(yearPopup);
        }

        if (monthPopup != null)
            WirePopupButtons(monthPopup, SelectMonth);

        if (yearPopup != null) yearPopup.SetActive(false);
        if (monthPopup != null) monthPopup.SetActive(false);
    }

    private void WirePopupButtons(GameObject popup, Action<int> onSelect)
    {
        var options = popup.GetComponentsInChildren<DatePickerOptionButton>(true);
        if (options.Length == 0)
        {
            Debug.LogWarning(
                $"[SimulationDatePicker] {popup.name}에 DatePickerOptionButton이 없습니다. " +
                "버튼에 컴포넌트를 추가하거나 Year/Month Popup 필드를 비워 두세요.");
            return;
        }

        foreach (var option in options)
        {
            if (!option.TryGetComponent(out Button button))
                continue;

            int captured = option.Value;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onSelect(captured));
        }
    }

    private Transform EnsurePopupOverlay()
    {
        if (_popupOverlay != null)
            return _popupOverlay;

        if (popupRoot != null)
        {
            _popupOverlay = popupRoot;
            return _popupOverlay;
        }

        if (_rootCanvas == null)
            _rootCanvas = GetComponentInParent<Canvas>();

        if (_rootCanvas == null)
        {
            Debug.LogError("[SimulationDatePicker] Canvas를 찾지 못해 팝업 오버레이를 만들 수 없습니다.");
            return transform;
        }

        var existing = _rootCanvas.transform.Find(PopupOverlayName);
        if (existing != null)
        {
            _popupOverlay = existing;
            return _popupOverlay;
        }

        var overlayGo = new GameObject(PopupOverlayName, typeof(RectTransform));
        overlayGo.transform.SetParent(_rootCanvas.transform, false);
        overlayGo.transform.SetAsLastSibling();

        var overlayRect = overlayGo.GetComponent<RectTransform>();
        Stretch(overlayRect);

        _popupOverlay = overlayGo.transform;
        return _popupOverlay;
    }

    private void EnsureBackdrop()
    {
        var overlay = EnsurePopupOverlay();
        if (overlay == null)
            return;

        if (_backdrop != null)
            return;

        _backdrop = new GameObject(BackdropName, typeof(RectTransform), typeof(Image));
        _backdrop.transform.SetParent(overlay, false);
        _backdrop.transform.SetAsFirstSibling();
        Stretch(_backdrop.GetComponent<RectTransform>());

        var image = _backdrop.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.01f);
        image.raycastTarget = true;
        _backdrop.SetActive(false);
    }

    private void SetBackdropActive(bool active)
    {
        EnsureBackdrop();
        if (_backdrop != null)
            _backdrop.SetActive(active);
    }

    private void EnsurePopups()
    {
        var overlay = EnsurePopupOverlay();
        if (overlay == null)
            return;

        if (yearPopup != null && IsStaleYearPopup(yearPopup))
        {
            Destroy(yearPopup);
            yearPopup = null;
        }

        if (yearPopup == null)
        {
            yearPopup = BuildYearGridPopup("Popup_Year", GetYearPlacementRect(), minYear, maxYear, SelectYear);
            yearPopup.SetActive(false);
        }

        if (monthPopup == null)
        {
            monthPopup = BuildGridPopup("Popup_Month", _monthCardRect, 1, 12, SelectMonth, v => v.ToString("D2"));
            monthPopup.SetActive(false);
        }
    }

    private void OpenYearPopup()
    {
        EnsurePopups();
        bool wasOpen = yearPopup != null && yearPopup.activeSelf;
        ClosePopups();
        var placementRect = GetYearPlacementRect();
        if (wasOpen || yearPopup == null || placementRect == null) return;

        var yearRect = yearPopup.GetComponent<RectTransform>();
        PlaceDropdown(yearRect, placementRect, yearDropdownHeight);
        BringPopupToFront(yearPopup);
        yearPopup.SetActive(true);
        SetBackdropActive(true);
        ConfigurePopupScroll(yearPopup);
        ResetScrollToTop(yearPopup);
    }

    private void OpenMonthPopup()
    {
        EnsurePopups();
        bool wasOpen = monthPopup != null && monthPopup.activeSelf;
        ClosePopups();
        if (wasOpen || monthPopup == null || _monthCardRect == null) return;

        var monthRect = monthPopup.GetComponent<RectTransform>();
        PlaceDropdown(monthRect, _monthCardRect, monthRect.sizeDelta.y);
        BringPopupToFront(monthPopup);
        monthPopup.SetActive(true);
        SetBackdropActive(true);
    }

    private void BringPopupToFront(GameObject popup)
    {
        var overlay = EnsurePopupOverlay();
        if (overlay == null || popup == null)
            return;

        popup.transform.SetParent(overlay, false);
        if (_backdrop != null)
            _backdrop.transform.SetAsFirstSibling();
        popup.transform.SetAsLastSibling();
        overlay.SetAsLastSibling();
    }

    private static void ConfigureScrollRect(ScrollRect scroll)
    {
        if (scroll == null)
            return;

        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.horizontalScrollbar = null;
    }

    private static void ConfigurePopupScroll(GameObject popup)
    {
        if (popup == null)
            return;

        foreach (ScrollRect scroll in popup.GetComponentsInChildren<ScrollRect>(true))
            ConfigureScrollRect(scroll);
    }

    private static void ResetScrollToTop(GameObject popup)
    {
        var scroll = popup.GetComponentInChildren<ScrollRect>(true);
        if (scroll == null) return;

        ConfigureScrollRect(scroll);

        if (scroll.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);

        Canvas.ForceUpdateCanvases();
        scroll.velocity = Vector2.zero;
        scroll.verticalNormalizedPosition = 1f;
    }

    private void Update()
    {
        if (yearPopup == null && monthPopup == null) return;
        if (!IsAnyPopupOpen()) return;
        if (!Input.GetMouseButtonDown(0)) return;
        if (IsPointerOverPicker()) return;

        ClosePopups();
    }

    private bool IsAnyPopupOpen()
    {
        return (yearPopup != null && yearPopup.activeSelf)
            || (monthPopup != null && monthPopup.activeSelf);
    }

    private bool IsPointerOverPicker()
    {
        if (EventSystem.current == null)
            return false;

        var eventData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        foreach (var hit in results)
        {
            if (_backdrop != null && hit.gameObject == _backdrop)
                return true;
            if (yearPopup != null && hit.gameObject.transform.IsChildOf(yearPopup.transform))
                return true;
            if (monthPopup != null && hit.gameObject.transform.IsChildOf(monthPopup.transform))
                return true;
            if (yearCardButton != null && hit.gameObject.transform.IsChildOf(yearCardButton.transform))
                return true;
            if (monthCardButton != null && hit.gameObject.transform.IsChildOf(monthCardButton.transform))
                return true;
        }

        return false;
    }

    private void ClosePopups()
    {
        if (yearPopup != null) yearPopup.SetActive(false);
        if (monthPopup != null) monthPopup.SetActive(false);
        SetBackdropActive(false);
    }

    private void SelectYear(int year)
    {
        if (yearValueText != null) yearValueText.text = year.ToString();
        ClosePopups();
        SetStatusLoaded(false);
        OnYearPicked?.Invoke(year);
    }

    private void SelectMonth(int month)
    {
        if (monthValueText != null) monthValueText.text = month.ToString("D2");
        ClosePopups();
        SetStatusLoaded(false);
        OnMonthPicked?.Invoke(month);
    }

    private bool IsStaleYearPopup(GameObject popup)
    {
        if (popup == null || GetYearPlacementRect() == null)
            return false;

        float expectedWidth = GetDropdownWidth(GetYearPlacementRect(), _yearCardRect);
        float actualWidth = popup.GetComponent<RectTransform>().sizeDelta.x;
        return Mathf.Abs(actualWidth - expectedWidth) > 1f;
    }

    private GameObject BuildYearGridPopup(string name, RectTransform anchorRect, int from, int to, Action<int> onPick)
    {
        const bool showScrollbar = true;
        float width = GetDropdownWidth(anchorRect, _yearCardRect);
        float viewportWidth = GetYearGridViewportWidth(width, showScrollbar);
        int columns = ResolveYearGridColumns(viewportWidth);
        float cellWidth = (viewportWidth - yearGridPadding * 2f - yearGridSpacing * (columns - 1)) / columns;
        cellWidth = Mathf.Max(yearMinCellWidth, cellWidth);

        var root = CreateDropdownShell(name, anchorRect, new Vector2(width, yearDropdownHeight));
        var content = CreateScrollContent(
            root.transform,
            isGrid: true,
            columns: columns,
            showScrollbar: showScrollbar,
            cellSize: new Vector2(cellWidth, yearCellHeight),
            gridSpacing: yearGridSpacing,
            gridPadding: yearGridPadding,
            contentWidth: viewportWidth);

        for (int y = from; y <= to; y++)
        {
            int captured = y;
            CreatePickerButton(content, y.ToString(), () => onPick(captured), compact: true);
        }

        return root;
    }

    private int ResolveYearGridColumns(float dropdownWidth)
    {
        int columns = Mathf.Max(1, yearGridColumns);
        while (columns > 1)
        {
            float cellWidth = (dropdownWidth - yearGridPadding * 2f - yearGridSpacing * (columns - 1)) / columns;
            if (cellWidth >= yearMinCellWidth)
                return columns;
            columns--;
        }

        return 1;
    }

    private GameObject BuildGridPopup(string name, RectTransform anchorCard, int from, int to, Action<int> onPick, Func<int, string> label)
    {
        float width = anchorCard != null ? anchorCard.rect.width : 120f;
        int itemCount = to - from + 1;
        int rows = Mathf.CeilToInt(itemCount / (float)monthGridColumns);
        float cellWidth = (width - monthGridPadding * 2f - monthGridSpacing * (monthGridColumns - 1)) / monthGridColumns;
        float height = monthGridPadding * 2f + rows * monthCellHeight + (rows - 1) * monthGridSpacing;

        var root = CreateDropdownShell(name, anchorCard, new Vector2(width, height));
        var content = CreateStaticGrid(
            root.transform,
            monthGridColumns,
            new Vector2(cellWidth, monthCellHeight),
            monthGridSpacing,
            monthGridPadding);

        for (int m = from; m <= to; m++)
        {
            int captured = m;
            CreatePickerButton(content, label(captured), () => onPick(captured), compact: true);
        }

        return root;
    }

    private GameObject CreateDropdownShell(string name, RectTransform anchorCard, Vector2 size)
    {
        var overlay = EnsurePopupOverlay();
        var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        root.transform.SetParent(overlay, false);

        var rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = size;
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 1f);

        if (anchorCard != null)
            PlaceDropdown(rootRect, anchorCard, size.y);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Shadow));
        panel.transform.SetParent(root.transform, false);
        Stretch(panel.GetComponent<RectTransform>());

        var panelImage = panel.GetComponent<Image>();
        panelImage.color = Color.white;
        panelImage.raycastTarget = true;

        var shadow = panel.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.18f);
        shadow.effectDistance = new Vector2(0f, -2f);

        return root;
    }

    /// <summary>카드 기준으로 아래/위 중 공간이 넓은 쪽에 드롭다운을 배치한다.</summary>
    private void PlaceDropdown(RectTransform dropdown, RectTransform card, float height)
    {
        dropdown.SetParent(EnsurePopupOverlay(), false);
        dropdown.pivot = new Vector2(0.5f, 1f);
        dropdown.anchorMin = new Vector2(0.5f, 0.5f);
        dropdown.anchorMax = new Vector2(0.5f, 0.5f);
        dropdown.sizeDelta = new Vector2(card.rect.width, height);

        Camera eventCamera = GetEventCamera();
        Vector3[] corners = new Vector3[4];
        card.GetWorldCorners(corners);

        float cardCenterX = (corners[0].x + corners[3].x) * 0.5f;
        float cardBottomY = corners[0].y;
        float cardTopY = corners[1].y;

        RectTransform overlayRect = _popupOverlay as RectTransform;
        float spaceBelow = GetSpaceBelowCard(overlayRect, cardBottomY, eventCamera);
        float spaceAbove = GetSpaceAboveCard(overlayRect, cardTopY, eventCamera);
        bool openUpward = spaceBelow < height + dropdownGap && spaceAbove > spaceBelow;

        if (openUpward)
        {
            dropdown.pivot = new Vector2(0.5f, 0f);
            dropdown.position = new Vector3(cardCenterX, cardTopY + dropdownGap, card.position.z);
        }
        else
        {
            dropdown.pivot = new Vector2(0.5f, 1f);
            dropdown.position = new Vector3(cardCenterX, cardBottomY - dropdownGap, card.position.z);
        }
    }

    private Camera GetEventCamera()
    {
        if (_rootCanvas == null)
            _rootCanvas = GetComponentInParent<Canvas>();
        if (_rootCanvas == null)
            return null;

        return _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _rootCanvas.worldCamera;
    }

    private static float GetSpaceBelowCard(RectTransform overlayRect, float cardBottomY, Camera eventCamera)
    {
        if (overlayRect == null)
            return float.MaxValue;

        Vector3[] overlayCorners = new Vector3[4];
        overlayRect.GetWorldCorners(overlayCorners);
        return cardBottomY - overlayCorners[0].y;
    }

    private static float GetSpaceAboveCard(RectTransform overlayRect, float cardTopY, Camera eventCamera)
    {
        if (overlayRect == null)
            return float.MaxValue;

        Vector3[] overlayCorners = new Vector3[4];
        overlayRect.GetWorldCorners(overlayCorners);
        return overlayCorners[1].y - cardTopY;
    }

    private Transform CreateScrollContent(
        Transform popupRoot,
        bool isGrid,
        int columns = 1,
        bool showScrollbar = false,
        Vector2? cellSize = null,
        float gridSpacing = 2f,
        float gridPadding = 4f,
        float listSpacing = 0f,
        float? contentWidth = null)
    {
        var panel = popupRoot.Find("Panel");
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollGo.transform.SetParent(panel, false);
        var scrollRect = scrollGo.GetComponent<RectTransform>();
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(ScrollInsetLeft, 4f);
        scrollRect.offsetMax = showScrollbar
            ? new Vector2(-ScrollInsetRightWithScrollbar, -4f)
            : new Vector2(-ScrollInsetRight, -4f);
        scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        scrollGo.GetComponent<Image>().raycastTarget = true;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewport.transform, false);
        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = contentWidth.HasValue ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        if (contentWidth.HasValue)
            contentRect.sizeDelta = new Vector2(contentWidth.Value, 0f);

        if (isGrid)
        {
            Vector2 resolvedCellSize = cellSize ?? new Vector2(34f, yearCellHeight);
            var grid = contentGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = resolvedCellSize;
            grid.spacing = new Vector2(gridSpacing, gridSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.padding = new RectOffset(
                Mathf.RoundToInt(gridPadding),
                Mathf.RoundToInt(gridPadding),
                Mathf.RoundToInt(gridPadding),
                Mathf.RoundToInt(gridPadding));
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
        else
        {
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = listSpacing;
            layout.padding = new RectOffset(4, 4, 2, 2);
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRect;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = scrollSensitivity;
        ConfigureScrollRect(scroll);

        if (showScrollbar)
        {
            var scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarGo.transform.SetParent(scrollGo.transform, false);
            var sbRect = scrollbarGo.GetComponent<RectTransform>();
            sbRect.anchorMin = new Vector2(1f, 0f);
            sbRect.anchorMax = new Vector2(1f, 1f);
            sbRect.pivot = new Vector2(1f, 0.5f);
            sbRect.sizeDelta = new Vector2(5f, 0f);
            sbRect.anchoredPosition = Vector2.zero;
            scrollbarGo.GetComponent<Image>().color = new Color(0.9f, 0.92f, 0.95f, 1f);

            var handleSlide = new GameObject("Sliding Area", typeof(RectTransform));
            handleSlide.transform.SetParent(scrollbarGo.transform, false);
            Stretch(handleSlide.GetComponent<RectTransform>());

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleSlide.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(5f, 20f);
            handle.GetComponent<Image>().color = new Color(0.65f, 0.7f, 0.78f, 1f);

            var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        return contentGo.transform;
    }

    /// <summary>월 선택 — 스크롤 없이 01~12 전부 표시.</summary>
    private static Transform CreateStaticGrid(
        Transform popupRoot,
        int columns,
        Vector2 cellSize,
        float spacing,
        float padding)
    {
        var panel = popupRoot.Find("Panel");
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(panel, false);

        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(padding, padding);
        contentRect.offsetMax = new Vector2(-padding, -padding);

        var grid = contentGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = cellSize;
        grid.spacing = new Vector2(spacing, spacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;

        return contentGo.transform;
    }

    private void CreatePickerButton(Transform parent, string label, Action onClick, bool compact = false, float? rowHeight = null)
    {
        var go = new GameObject($"Btn_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        float height = rowHeight ?? (compact ? 0f : 32f);
        if (height > 0f)
        {
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
        }

        var image = go.GetComponent<Image>();
        image.color = new Color(0.97f, 0.98f, 0.99f, 1f);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = compact ? 13f : (height > 0f && height < 28f ? 13f : 15f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = new Color(0.12f, 0.16f, 0.22f, 1f);
        tmp.raycastTarget = false;
        if (yearValueText != null)
            tmp.font = yearValueText.font;

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.highlightedColor = new Color(0.9f, 0.94f, 0.98f, 1f);
        colors.pressedColor = new Color(0.82f, 0.88f, 0.95f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => onClick());
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
