using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XCharts.Runtime;

public class XchartUIManager : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    // Inspector 필드
    // ═══════════════════════════════════════════════════════════════════════

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI titleText;

    [Header("Chart")]
    [SerializeField] private GameObject chartPanel; // 차트 패널 전체 (제목 포함)
    [SerializeField] private LineChart oniChart;

    [Header("DataManager 연동")]
    [SerializeField] private DataManager dataManager;

    [Header("UIController 연동")]
    [SerializeField] private UIController uiController;

    [SerializeField] private MinimapManager minimapManager;
    // ═══════════════════════════════════════════════════════════════════════
    // 색상 상수
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Color ColorReserve     = HexColor("13CC35");  // 예비율  (강조)
    private static readonly Color ColorTemperature = HexColor("FF0202");  // 기온
    private static readonly Color ColorConsumption = HexColor("FF9D00");  // 소비량
    private static readonly Color ColorSupply      = HexColor("6A98D7");  // 공급량

    private static readonly Color32 ColorLanina  = new Color32( 80, 130, 200, 70);
    private static readonly Color32 ColorNeutral = new Color32(160, 160, 160, 35);
    private static readonly Color32 ColorElnino  = new Color32(200,  80,  80, 70);

    private static readonly Color32 ColorCritical = new Color32(220,  50,  50, 210);
    private static readonly Color32 ColorWarn     = new Color32(230, 120,  30, 210);
    private static readonly Color32 ColorCaution  = new Color32(200, 180,   0, 210);
    private static readonly Color32 ColorNormal   = new Color32( 60, 170,  80, 210);

    // ═══════════════════════════════════════════════════════════════════════
    // 상수
    // ═══════════════════════════════════════════════════════════════════════

    private const DistrictType DEFAULT_DISTRICT = DistrictType.JONGNO;
    private const double Y_MIN = 0.0;
    private const double Y_MAX = 25.0;

    // ═══════════════════════════════════════════════════════════════════════
    // 상태
    // ═══════════════════════════════════════════════════════════════════════

    private readonly List<OniRangeData> _entries = new List<OniRangeData>();
    private DistrictType _currentDistrict = DEFAULT_DISTRICT;
    private float _currentOni = 0f;

    // ═══════════════════════════════════════════════════════════════════════
    // Unity 생명주기
    // ═══════════════════════════════════════════════════════════════════════

    private void Start()
    {
        SetChartVisible(false);

        if (dataManager != null)
            dataManager.OniRangeDataUpdated += OnOniRangeDataUpdated;
        else
            Debug.LogWarning("[XchartUIManager] dataManager가 연결되지 않았습니다.");

        if (uiController != null)
            uiController.OnOniValueChanged += OnOniSliderChanged;
    }

    private void OnEnable()
    {
        minimapManager.OnDistrictSelected += SetDistrict;
    }

    private void OnDisable()
    {
        minimapManager  .OnDistrictSelected -= SetDistrict;
    }

    private void OnDestroy()
    {
        if (dataManager != null)
            dataManager.OniRangeDataUpdated -= OnOniRangeDataUpdated;
        if (uiController != null)
            uiController.OnOniValueChanged -= OnOniSliderChanged;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 외부 인터페이스
    // ═══════════════════════════════════════════════════════════════════════

    // 슬라이더 실시간 → 수직선 위치만 업데이트 (API 재호출 없음)
    private void OnOniSliderChanged(float oniValue)
    {
        _currentOni = oniValue;
        UpdateCurrentOniLine();
    }

    // x축 인덱스로 변환 후 수직 MarkLine 위치 갱신
    private void UpdateCurrentOniLine()
    {
        if (oniChart == null || _entries.Count == 0) return;

        // ONI 값 → x축 인덱스 (가장 가까운 포인트)
        int idx = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < _entries.Count; i++)
        {
            float dist = Mathf.Abs(_entries[i].oni - _currentOni);
            if (dist < minDist) { minDist = dist; idx = i; }
        }

        oniChart.RemoveChartComponents<MarkLine>();
        var ml = oniChart.AddChartComponent<MarkLine>();
        if (ml == null) return;

        ml.show       = true;
        ml.serieIndex = 0;
        ml.data.Clear();

        // 현재 ONI 수직선 (XValue 타입으로 x축 인덱스 지정)
        var vLine = new MarkLineData();
        vLine.type                     = MarkLineType.Custom;
        vLine.name                     = $"ONI {_currentOni:F1}";
        vLine.xValue                   = idx;
        vLine.lineStyle.color          = new Color32(50, 50, 50, 200);
        vLine.lineStyle.width          = 2.0f;
        vLine.lineStyle.type           = LineStyle.Type.Solid;
        vLine.label.show               = true;
        vLine.label.formatter          = $"ONI {_currentOni:F1}";
        vLine.label.textStyle.color    = new Color32(40, 40, 40, 220);
        vLine.label.textStyle.fontSize = 11;
        vLine.startSymbol.type         = SymbolType.None;
        vLine.endSymbol.type           = SymbolType.None;
        ml.data.Add(vLine);

        // HLine 임계선 재추가 (MarkLine 제거했으므로)
        ml.data.Add(HLine( 5.0, "심각 5%",  ColorCritical));
        ml.data.Add(HLine( 7.0, "경계 7%",  ColorWarn));
        ml.data.Add(HLine(10.0, "주의 10%", ColorCaution));
        ml.data.Add(HLine(15.0, "정상 15%", ColorNormal));
    }

    // DataManager 이벤트 핸들러 — /predict/oni_range 응답 시 호출
    public void OnOniRangeDataUpdated(List<OniRangeData> data)
    {
        if (data == null || data.Count == 0)
        {
            SetChartVisible(false);
            return;
        }
        _entries.Clear();
        _entries.AddRange(data);
        SetChartVisible(true);
        BuildChart();
    }

    private void SetChartVisible(bool visible)
    {
        if (chartPanel != null)
            chartPanel.SetActive(visible);
        else if (oniChart != null)
            oniChart.gameObject.SetActive(visible);
    }

    // 구 클릭 시 외부에서 호출 — 해당 구 데이터로 차트 갱신
    public void SetDistrict(DistrictType districtName)
    {
        _currentDistrict = districtName;
        if (_entries.Count > 0)
            BuildChart();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 차트 빌드
    // ═══════════════════════════════════════════════════════════════════════

    private void BuildChart()
    {
        if (oniChart == null || _entries.Count == 0) return;

        SetTitle($"{DataConverter.GetDistrictName(_currentDistrict)} ONI별 에너지 현황");

        oniChart.RemoveChartComponents<MarkArea>();
        oniChart.RemoveChartComponents<MarkLine>();
        oniChart.RemoveData();

        ConfigureBackground();
        ConfigureAxes();
        ConfigureTooltip();

        AddXAxisLabels();

        bool hasGuTemp = _entries[0].guTemperature != null &&
                         _entries[0].guTemperature.ContainsKey(_currentDistrict);
        bool hasGuCons = _entries[0].guConsumption != null &&
                         _entries[0].guConsumption.ContainsKey(_currentDistrict);

        // 정규화 범위 계산
        (float supplyMin, float supplyMax) = MinMax(e => e.supplyPower);
        (float consMin,   float consMax)   = MinMax(e =>
            hasGuCons ? (float)e.guConsumption.GetValueOrDefault(_currentDistrict, 0.0)
                      : e.seoulTotalConsumption);
        (float tempMin,   float tempMax)   = MinMax(e =>
            hasGuTemp ? e.guTemperature.GetValueOrDefault(_currentDistrict, 0f)
                      : e.seoulTemperature);

        // Serie 0 : 공급예비율 — 강조 (두껍고 solid)
        AddLineSerie("예비율 (%)", ColorReserve, 2.5f, false,
            e => e.reserveRate);

        // Serie 1 : 기온 — 흐리게, 실선
        string tempLabel = hasGuTemp ? $"{_currentDistrict} 기온" : "서울 기온";
        AddLineSerie(tempLabel, ColorTemperature, 0.8f, false,
            e => (float)Normalize(
                hasGuTemp ? e.guTemperature.GetValueOrDefault(_currentDistrict, 0f) : e.seoulTemperature,
                tempMin, tempMax));

        // Serie 2 : 공급량 — 흐리게, 실선
        AddLineSerie("공급량", ColorSupply, 0.8f, false,
            e => (float)Normalize(e.supplyPower, supplyMin, supplyMax));

        // Serie 3 : 소비량 — 빨강, 흐리게, 실선
        string consLabel = hasGuCons ? $"{_currentDistrict} 소비량" : "서울 소비량";
        AddLineSerie(consLabel, ColorConsumption, 2.5f, false,
            e => (float)Normalize(
                hasGuCons ? (float)e.guConsumption.GetValueOrDefault(_currentDistrict, 0.0) : e.seoulTotalConsumption,
                consMin, consMax));

        AddBackgroundZones();
        AddThresholdLines();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 차트 설정
    // ═══════════════════════════════════════════════════════════════════════

    private void ConfigureBackground()
    {
        oniChart.theme.transparentBackground = true;

        // XCharts Background 컴포넌트: 반투명 흰색 60%
        var bg = oniChart.GetChartComponent<Background>();
        if (bg != null)
        {
            bg.show       = true;
            bg.autoColor  = false;
            bg.imageColor = new Color(1f, 1f, 1f, 0.6f);
        }

        var grid = oniChart.GetChartComponent<GridCoord>();
        if (grid != null)
        {
            grid.backgroundColor = new Color32(0, 0, 0, 0);
            grid.showBorder      = false;
            grid.borderWidth     = 0;
        }

        // XCharts 자동 생성 child "background" Image → 흰색 60%
        var bgChild = oniChart.transform.Find("background");
        if (bgChild != null)
        {
            var img = bgChild.GetComponent<Image>();
            if (img != null)
            {
                img.color         = new Color(1f, 1f, 1f, 0.6f);
                img.raycastTarget = false;
            }
        }
    }

    private void ConfigureAxes()
    {
        var xAxis = oniChart.GetChartComponent<XAxis>();
        if (xAxis != null)
        {
            xAxis.axisLabel.show               = true;
            xAxis.axisLabel.textStyle.color    = Color.black;
            xAxis.axisLabel.textStyle.fontSize = 17;
            xAxis.axisLabel.interval           = 0; // 각 데이터 레이블 개별 제어 (AddXAxisLabels에서 빈값 처리)
            xAxis.axisLine.lineStyle.color     = new Color32(0, 0, 0, 150);
            xAxis.splitLine.show               = false;
            // x축 "ONI" 제목: 축 끝 아래쪽
            xAxis.axisName.show                          = true;
            xAxis.axisName.name                          = "ONI";
            xAxis.axisName.onZero                        = false;
            xAxis.axisName.labelStyle.position           = LabelStyle.Position.Bottom;
            xAxis.axisName.labelStyle.offset             = new Vector3(0, -20, 0);
            xAxis.axisName.labelStyle.textStyle.color    = Color.black;
            xAxis.axisName.labelStyle.textStyle.fontSize = 15;
        }

        var yAxis = oniChart.GetChartComponent<YAxis>();
        if (yAxis != null)
        {
            yAxis.axisLabel.show               = true;
            yAxis.axisLabel.textStyle.color    = Color.black;
            yAxis.axisLabel.textStyle.fontSize = 11;
            yAxis.axisLabel.formatter          = "{value}%";
            yAxis.axisLine.show                = false;
            yAxis.splitLine.show               = false;
            yAxis.minMaxType                   = Axis.AxisMinMaxType.Custom;
            yAxis.min                          = Y_MIN;
            yAxis.max                          = Y_MAX;
        }
    }

    private void ConfigureTooltip()
    {
        var tooltip = oniChart.GetChartComponent<Tooltip>();
        if (tooltip == null) return;

        tooltip.type           = Tooltip.Type.Cross;
        tooltip.titleFormatter = "ONI: {b}";
        tooltip.itemFormatter  = "{a}: {c}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 데이터 추가
    // ═══════════════════════════════════════════════════════════════════════

    private void AddXAxisLabels()
    {
        // -2.5(idx0) / 0.0(idx25) / +2.5(idx50) 만 표시, 나머지 빈 문자열
        for (int i = 0; i < _entries.Count; i++)
        {
            bool show = (i == 0 || i == 25 || i == _entries.Count - 1);
            oniChart.AddXAxisData(show ? _entries[i].oni.ToString("F1") : "");
        }
    }

    private void AddLineSerie(string name, Color color, float width, bool dashed,
                              Func<OniRangeData, float> selector)
    {
        var serie = oniChart.AddSerie<Line>(name);
        serie.lineStyle.color = color;
        serie.lineStyle.width = width;
        serie.lineStyle.type  = dashed ? LineStyle.Type.Dashed : LineStyle.Type.Solid;
        serie.symbol.type     = SymbolType.None;
        serie.show            = true;

        foreach (var e in _entries)
            oniChart.AddData(name, (double)selector(e));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MarkArea / MarkLine
    // ═══════════════════════════════════════════════════════════════════════

    private void AddBackgroundZones()
    {
        AddMarkArea("라니냐",  0, 19, ColorLanina);
        AddMarkArea("중립",   20, 30, ColorNeutral);
        AddMarkArea("엘니뇨", 31, 50, ColorElnino);
    }

    private void AddMarkArea(string label, int startIdx, int endIdx, Color32 color)
    {
        var ma = oniChart.AddChartComponent<MarkArea>();
        if (ma == null) return;

        ma.show                     = true;
        ma.serieIndex               = 0;
        ma.text                     = label;
        ma.start.type               = MarkAreaType.None;
        ma.start.xValue             = startIdx;
        ma.end.type                 = MarkAreaType.None;
        ma.end.xValue               = endIdx;
        ma.itemStyle.color          = color;
        ma.itemStyle.borderWidth    = 0;
        ma.label.show               = true;
        ma.label.position           = LabelStyle.Position.Top;
        ma.label.offset             = new Vector3(0, 25, 0);
        ma.label.textStyle.color    = new Color32(30, 30, 30, 230);
        ma.label.textStyle.fontSize = 17;
    }

    private void AddThresholdLines()
    {
        var ml = oniChart.AddChartComponent<MarkLine>();
        if (ml == null) return;

        ml.show       = true;
        ml.serieIndex = 0;
        ml.data.Clear();
        ml.data.Add(HLine( 5.0, "심각 5%",  ColorCritical));
        ml.data.Add(HLine( 7.0, "경계 7%",  ColorWarn));
        ml.data.Add(HLine(10.0, "주의 10%", ColorCaution));
        ml.data.Add(HLine(15.0, "정상 15%", ColorNormal));
    }

    private static MarkLineData HLine(double yVal, string label, Color32 color)
    {
        var d = new MarkLineData();
        d.type                     = MarkLineType.Custom;
        d.name                     = label;
        d.yValue                   = yVal;
        d.lineStyle.color          = color;
        d.lineStyle.width          = 0.6f;
        d.lineStyle.type           = LineStyle.Type.Dashed;
        d.lineStyle.dashLength     = 0.8f;
        d.lineStyle.gapLength      = 0.3f;
        d.label.show               = true;
        d.label.formatter          = label;
        d.label.textStyle.color    = new Color32(40, 40, 40, 220);
        d.label.textStyle.fontSize = 15;
        d.startSymbol.type         = SymbolType.None;
        d.endSymbol.type           = SymbolType.None;
        return d;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 유틸리티
    // ═══════════════════════════════════════════════════════════════════════

    private (float min, float max) MinMax(Func<OniRangeData, float> selector)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var e in _entries)
        {
            float v = selector(e);
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return (min, max);
    }

    private static double Normalize(float val, float min, float max)
    {
        if (Math.Abs(max - min) < 1e-6f) return Y_MIN;
        return Y_MIN + (val - min) / (max - min) * (Y_MAX - Y_MIN);
    }

    private static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    private void SetTitle(string text)
    {
        if (titleText != null) titleText.text = text;
    }
}
