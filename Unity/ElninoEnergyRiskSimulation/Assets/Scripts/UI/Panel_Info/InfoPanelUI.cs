using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InfoPanelUI : MonoBehaviour
{
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

    private bool _hasPredictContext;
    private PowerGridData _latestPowerData;
    private readonly List<OniRangeData> _oniRangeEntries = new();
    private readonly Dictionary<DistrictType, float> _districtTemperatures = new();
    private DistrictType _selectedDistrict = DistrictType.JONGNO;
    private string cachedTemperatureText;
    private string cachedEmergencyStage;
    private int _currentStageLevel = -1;

    private void Awake()
    {
        if (dataManager == null)
            dataManager = FindFirstObjectByType<DataManager>();
        if (uiController == null)
            uiController = FindFirstObjectByType<UIController>();
        if (minimapManager == null)
            minimapManager = FindFirstObjectByType<MinimapManager>();

        ResolveReferences();
        ApplyReserveStage(ReserveRateStagePalette.DefaultReserveRate, force: true);
        RefreshRealtimeDateDisplay();
    }

    private void OnEnable()
    {
        if (dataManager != null)
        {
            dataManager.OnCurrentDataUpdated += HandleCurrentDataUpdated;
            dataManager.OnPowerDataUpdated += HandlePowerDataUpdated;
            dataManager.OnDistrictDataUpdated += HandleDistrictDataUpdated;
            dataManager.OniRangeDataUpdated += HandleOniRangeDataUpdated;
        }

        if (uiController != null)
            uiController.OnOniValueChanged += HandleOniValueChanged;

        if (minimapManager != null)
            minimapManager.OnDistrictSelected += HandleDistrictSelected;
    }

    private void OnDisable()
    {
        if (dataManager != null)
        {
            dataManager.OnCurrentDataUpdated -= HandleCurrentDataUpdated;
            dataManager.OnPowerDataUpdated -= HandlePowerDataUpdated;
            dataManager.OnDistrictDataUpdated -= HandleDistrictDataUpdated;
            dataManager.OniRangeDataUpdated -= HandleOniRangeDataUpdated;
        }

        if (uiController != null)
            uiController.OnOniValueChanged -= HandleOniValueChanged;

        if (minimapManager != null)
            minimapManager.OnDistrictSelected -= HandleDistrictSelected;
    }

    private void HandleCurrentDataUpdated(JObject weather)
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
        _latestPowerData = data;
        _selectedDistrict = DistrictType.JONGNO;
        _districtTemperatures.Clear();

        if (Text_Date_Info != null)
            Text_Date_Info.text = $"{data.year}년 {data.month}월";

        ApplyReserveStage(data.reserveRate, force: true);
        RefreshSimulationTemperatureDisplay();
    }

    private void HandleOniRangeDataUpdated(List<OniRangeData> data)
    {
        _oniRangeEntries.Clear();

        if (data == null || data.Count == 0)
        {
            if (!_hasPredictContext)
                ApplyReserveStage(ReserveRateStagePalette.DefaultReserveRate);
            return;
        }

        _oniRangeEntries.AddRange(data);

        float oni = uiController != null ? uiController.GetCurrentOni() : 0f;
        ApplyReserveStage(GetClosestOniEntry(oni)?.reserveRate ?? ReserveRateStagePalette.DefaultReserveRate);

        if (_hasPredictContext)
            RefreshSimulationTemperatureFromOniRange(oni);
    }

    private void HandleOniValueChanged(float oniValue)
    {
        if (_oniRangeEntries.Count == 0)
            return;

        ApplyReserveStage(GetClosestOniEntry(oniValue)?.reserveRate ?? ReserveRateStagePalette.DefaultReserveRate);

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

        float oni = uiController != null ? uiController.GetCurrentOni() : 0f;
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
        if (uiController == null)
            return false;

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

    private void ResolveReferences()
    {
        if (Panel_HUD_Info == null)
            Panel_HUD_Info = FindChildPanel("Panel_HUD_Info");

        if (Panel_HUD_Status == null)
            Panel_HUD_Status = FindChildPanel("Panel_HUD_Status");

        GameObject searchRoot = GetSearchRoot();

        if (Text_Date_Info == null) Text_Date_Info = FindText(searchRoot, "Text_Date_Info");
        if (Text_Temperature_Info == null) Text_Temperature_Info = FindText(searchRoot, "Text_Temperature_Info");
        if (Text_Emergency_Value == null) Text_Emergency_Value = FindText(searchRoot, "Text_Emergency_Value");

        if (Img_Emergency_Dot == null)
        {
            Img_Emergency_Dot = FindImage(searchRoot, "Img_Emergency_Dot");
            if (Img_Emergency_Dot == null)
                Img_Emergency_Dot = FindImage(searchRoot, "Dot");
        }
    }

    private GameObject GetSearchRoot()
    {
        if (Panel_HUD_Info != null)
            return Panel_HUD_Info;

        Transform current = transform;
        while (current != null)
        {
            if (current.name == "HUD_Header")
                return current.gameObject;

            current = current.parent;
        }

        return gameObject;
    }

    private GameObject FindChildPanel(string panelName)
    {
        Transform current = transform;
        while (current != null)
        {
            Transform found = current.Find(panelName);
            if (found != null)
                return found.gameObject;

            if (current.name == panelName)
                return current.gameObject;

            current = current.parent;
        }

        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        foreach (Transform target in transforms)
        {
            if (target.name == panelName)
                return target.gameObject;
        }

        return null;
    }

    private static TMP_Text FindText(GameObject searchRoot, string objectName)
    {
        if (searchRoot == null)
            return null;

        TMP_Text[] texts = searchRoot.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text.gameObject.name == objectName)
                return text;
        }

        return null;
    }

    private static Image FindImage(GameObject searchRoot, string objectName)
    {
        if (searchRoot == null)
            return null;

        Image[] images = searchRoot.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image.gameObject.name == objectName)
                return image;
        }

        return null;
    }
}
