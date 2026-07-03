using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InfoPanelUI : MonoBehaviour
{
    private const DistrictType DefaultDistrict = DistrictType.JONGNO;

    [Header("HUD")]
    public GameObject Panel_HUD_Info;
    public GameObject Panel_HUD_Status;
    public TMP_Text Text_Date_Info;
    public TMP_Text Text_Temperature_Info;
    public TMP_Text Text_Emergency_Value;
    public Image Img_Emergency_Dot;

    [Header("데이터")]
    [SerializeField] private DataManager dataManager;
    [SerializeField] private UIController uiController;
    [SerializeField] private MinimapManager minimapManager;
    [SerializeField] private ReserveRateStateController reserveRateState;
    private bool _hasPredictContext;
    private readonly List<OniRangeData> _oniRangeEntries = new();
    private readonly Dictionary<DistrictType, float> _districtTemperatures = new();
    private DistrictType _selectedDistrict = DefaultDistrict;
    private string cachedTemperatureText;
    private string cachedEmergencyStage;
    private int _currentStageLevel = -1;

    private void Awake()
    {
        SceneRefs.Resolve(ref dataManager);
        SceneRefs.Resolve(ref uiController);
        SceneRefs.Resolve(ref minimapManager);
        SceneRefs.Resolve(ref reserveRateState);

        WarnMissingReferences();
        RefreshRealtimeDateDisplay();
    }

    private void WarnMissingReferences()
    {
        SceneRefs.RequireAll(this,
            (Panel_HUD_Info, nameof(Panel_HUD_Info)),
            (Panel_HUD_Status, nameof(Panel_HUD_Status)),
            (Text_Date_Info, nameof(Text_Date_Info)),
            (Text_Temperature_Info, nameof(Text_Temperature_Info)),
            (Text_Emergency_Value, nameof(Text_Emergency_Value)),
            (Img_Emergency_Dot, nameof(Img_Emergency_Dot)));
    }

    private void OnEnable()
    {
        SceneRefs.Resolve(ref reserveRateState);

        if (!SceneRefs.RequireAll(this,
                (dataManager, nameof(dataManager)),
                (uiController, nameof(uiController)),
                (minimapManager, nameof(minimapManager)),
                (reserveRateState, nameof(reserveRateState))))
            return;

        dataManager.OnCurrentTempDataUpdated += HandleCurrentTempUpdated;
        dataManager.OnPowerDataUpdated += HandlePowerDataUpdated;
        dataManager.OnDistrictDataUpdated += HandleDistrictDataUpdated;
        dataManager.OniRangeDataUpdated += HandleOniRangeDataUpdated;

        uiController.OnOniValueChanged += HandleOniValueChanged;

        // 변경
        minimapManager.OnDisplayedDistrictChanged += HandleDisplayedDistrictChanged;

        reserveRateState.OnStateChanged += HandleReserveRateStateChanged;
        HandleReserveRateStateChanged(reserveRateState.Current);
    }

    private void OnDisable()
    {
        if (dataManager != null)
        {
            dataManager.OnCurrentTempDataUpdated -= HandleCurrentTempUpdated;
            dataManager.OnPowerDataUpdated -= HandlePowerDataUpdated;
            dataManager.OnDistrictDataUpdated -= HandleDistrictDataUpdated;
            dataManager.OniRangeDataUpdated -= HandleOniRangeDataUpdated;
        }

        if (uiController != null)
            uiController.OnOniValueChanged -= HandleOniValueChanged;

        if (minimapManager != null)
            minimapManager.OnDisplayedDistrictChanged -= HandleDisplayedDistrictChanged;

        if (reserveRateState != null)
            reserveRateState.OnStateChanged -= HandleReserveRateStateChanged;
    }

    private void HandleReserveRateStateChanged(ReserveRateSnapshot snapshot)
    {
        ApplyReserveStage(snapshot.ReserveRate, force: true);
    }

    private void HandleCurrentTempUpdated(JObject weather)
    {
        if (_hasPredictContext || weather == null)
            return;

        RefreshRealtimeDateDisplay();

        if (weather["temperature"] == null)
            return;

        if (float.TryParse(weather["temperature"].ToString(), out float temperature))
            SetRealtimeTemperature(temperature);
    }

    private void HandlePowerDataUpdated(PowerGridData data)
    {
        if (data == null)
            return;

        _hasPredictContext = true;
        _selectedDistrict = DefaultDistrict;
        _districtTemperatures.Clear();

        if (Text_Date_Info != null)
            Text_Date_Info.text = $"{data.year}년 {data.month}월";

        RefreshSimulationTemperatureDisplay();
    }

    private void HandleOniRangeDataUpdated(List<OniRangeData> data)
    {
        _oniRangeEntries.Clear();

        if (data == null || data.Count == 0)
            return;

        _oniRangeEntries.AddRange(data);

        if (_hasPredictContext)
            RefreshSimulationTemperatureFromOniRange(uiController.GetCurrentOni());
    }

    private void HandleOniValueChanged(float oniValue)
    {
        if (_oniRangeEntries.Count == 0)
            return;

        if (_hasPredictContext)
            RefreshSimulationTemperatureFromOniRange(oniValue);
    }

    private void HandleDistrictSelected(DistrictType districtType)
    {
        if (!_hasPredictContext)
            return;

        _selectedDistrict = districtType;
        RefreshSimulationTemperatureDisplay();
    }

    private void HandleDistrictDataUpdated(DistrictData data)
    {
        if (!_hasPredictContext || data == null)
            return;

        _districtTemperatures[data.districtType] = data.temperature;

        if (data.districtType == _selectedDistrict)
            SetSimulationTemperature(data.districtType, data.temperature);
    }

    private void ApplyReserveStage(float reserveRate, bool force = false)
    {
        int level = ReserveRateStagePalette.ToLevel(reserveRate);
        if (!force && level == _currentStageLevel)
            return;

        _currentStageLevel = level;
        string stageTitle = ReserveRateStagePalette.GetStageTitle(level);
        Color stageColor = ReserveRateStagePalette.GetSegmentColor(level);

        if (cachedEmergencyStage == stageTitle)
            return;

        cachedEmergencyStage = stageTitle;

        if (Text_Emergency_Value != null)
        {
            Text_Emergency_Value.text = stageTitle;
            Text_Emergency_Value.color = stageColor;
        }

        if (Img_Emergency_Dot != null)
            Img_Emergency_Dot.color = stageColor;
    }

    private void SetRealtimeTemperature(float temperature)
    {
        if (_hasPredictContext)
            return;

        ApplyTemperatureText($"현재 {temperature:0.0}°C");
    }

    private void SetSimulationTemperature(DistrictType districtType, float temperature)
    {
        if (!_hasPredictContext || districtType != _selectedDistrict)
            return;

        string guName = DataConverter.GetDistrictName(districtType);
        ApplyTemperatureText($"{guName} {temperature:0.0}°C");
    }

    private void RefreshSimulationTemperatureDisplay()
    {
        if (!_hasPredictContext)
            return;

        if (_districtTemperatures.TryGetValue(_selectedDistrict, out float temperature))
        {
            SetSimulationTemperature(_selectedDistrict, temperature);
            return;
        }

        float oni = uiController.GetCurrentOni();
        RefreshSimulationTemperatureFromOniRange(oni);
    }

    private void RefreshSimulationTemperatureFromOniRange(float oniValue)
    {
        OniRangeData closest = GetClosestOniEntry(oniValue);
        if (closest?.guTemperature == null)
            return;

        if (!closest.guTemperature.TryGetValue(_selectedDistrict, out float temperature))
            return;

        _districtTemperatures[_selectedDistrict] = temperature;
        SetSimulationTemperature(_selectedDistrict, temperature);
    }

    private void ApplyTemperatureText(string temperatureText)
    {
        if (cachedTemperatureText == temperatureText)
            return;

        cachedTemperatureText = temperatureText;

        if (Text_Temperature_Info != null)
            Text_Temperature_Info.text = temperatureText;
    }

    private void RefreshRealtimeDateDisplay()
    {
        if (_hasPredictContext || HasSimulationDateSelected())
            return;

        if (Text_Date_Info == null)
            return;

        DateTime now = DateTime.Now;
        Text_Date_Info.text = $"{now.Year} - {now.Month:D2}";
    }

    private bool HasSimulationDateSelected()
    {
        return !string.IsNullOrEmpty(uiController.GetSelectedYear())
            && !string.IsNullOrEmpty(uiController.GetSelectedMonth());
    }

    private OniRangeData GetClosestOniEntry(float oniValue)
    {
        OniRangeData closest = null;
        float minDistance = float.MaxValue;

        foreach (OniRangeData data in _oniRangeEntries)
        {
            float distance = Mathf.Abs(data.oni - oniValue);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = data;
            }
        }

        return closest;
    }
    private void HandleDisplayedDistrictChanged(DistrictType districtType)
    {
        if (!_hasPredictContext)
            return;

        _selectedDistrict = districtType;
        RefreshSimulationTemperatureDisplay();
    }
}
