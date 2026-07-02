using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 미니맵 구별 전력 cmap.
/// /predict(OnPowerDataUpdated)로 즉시 반영, oni_range + 슬라이더는 보간용 보충.
/// </summary>
public class MinimapColorController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MinimapManager minimapManager;
    [SerializeField] private DataManager dataManager;
    [SerializeField] private UIController uiController;
    [SerializeField] private BlackoutSimulationController simulationController;

    [Header("CMap Style")]
    [SerializeField] private Color lowPowerColor = new Color(1f, 0.9f, 0.75f, 0.9f);
    [SerializeField] private Color highPowerColor = new Color(1f, 0.25f, 0.05f, 0.9f);

    [Header("BlackOut Style")]
    [SerializeField] private Color blackoutColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);

    private readonly Dictionary<DistrictType, Color> districtCurrentColor =
        new Dictionary<DistrictType, Color>();

    private readonly List<OniRangeData> oniRangeEntries = new List<OniRangeData>();
    private readonly Dictionary<DistrictType, double> predictGuConsumption =
        new Dictionary<DistrictType, double>();

    private float currentOni;
    private bool hasCurrentOni;
    private bool hasReceivedPredict;
    private bool isReady;
    private bool _isSimulationOn;

    private DistrictType blinkingDistrictType;
    private Coroutine blackoutBlinkCoroutine;

    private void Awake()
    {
        if (minimapManager == null)
            minimapManager = GetComponent<MinimapManager>();

        SceneRefs.Resolve(ref dataManager);
        SceneRefs.Resolve(ref uiController);
        SceneRefs.Resolve(ref simulationController);
    }

    private void OnEnable()
    {
        if (!SceneRefs.RequireAll(this,
            (minimapManager, nameof(minimapManager)),
            (dataManager, nameof(dataManager)),
            (uiController, nameof(uiController)),
            (simulationController, nameof(simulationController))
        ))
        {
            return;
        }

        dataManager.OniRangeDataUpdated += HandleOniRangeDataUpdated;
        dataManager.OnPowerDataUpdated += HandlePowerDataUpdated;

        uiController.OnOniValueChanged += HandleOniSliderChanged;

        simulationController.OnBlackoutDistrictChanged += HandleBlackoutDistrictChanged;
        simulationController.OnBlackoutSimulationToggled += HandleSimulationToggled;
    }

    private void OnDisable()
    {
        if (dataManager != null)
        {
            dataManager.OniRangeDataUpdated -= HandleOniRangeDataUpdated;
            dataManager.OnPowerDataUpdated -= HandlePowerDataUpdated;
        }

        if (uiController != null)
            uiController.OnOniValueChanged -= HandleOniSliderChanged;

        if (simulationController != null)
        {
            simulationController.OnBlackoutDistrictChanged -= HandleBlackoutDistrictChanged;
            simulationController.OnBlackoutSimulationToggled -= HandleSimulationToggled;
        }
    }

    public void SetReady()
    {
        isReady = true;

        if (hasReceivedPredict)
            ApplyFromPredict();
        else
            ApplyFromOniRange(hasCurrentOni ? currentOni : 0f);
    }

    private void HandleSimulationToggled(bool isOn)
    {
        _isSimulationOn = isOn;
        if (!isOn)
            StopBlinkAndRestore();
    }

    private void HandlePowerDataUpdated(PowerGridData data)
    {
        if (data == null || _isSimulationOn)
            return;

        hasReceivedPredict = true;
        currentOni = data.oni;
        hasCurrentOni = true;

        predictGuConsumption.Clear();
        foreach (var kvp in data.guConsumption)
            predictGuConsumption[DataConverter.GetDistrictType(kvp.Key)] = kvp.Value;

        ApplyFromPredict();
    }

    private void HandleOniRangeDataUpdated(List<OniRangeData> data)
    {
        oniRangeEntries.Clear();

        if (data == null || data.Count == 0)
        {
            if (!hasReceivedPredict)
                Debug.LogWarning("[MinimapColorController] OniRangeData가 비어 있습니다.");
            return;
        }

        oniRangeEntries.AddRange(data);

        if (!hasReceivedPredict)
            ApplyFromOniRange(hasCurrentOni ? currentOni : 0f);
    }

    private void HandleOniSliderChanged(float oniValue)
    {
        if (_isSimulationOn)
            return;

        currentOni = oniValue;
        hasCurrentOni = true;
        ApplyFromOniRange(oniValue);
    }

    private void ApplyFromPredict()
    {
        if (!isReady || predictGuConsumption.Count == 0)
            return;

        ApplyPowerUsageCMap(predictGuConsumption);
    }

    private void ApplyFromOniRange(float oniValue)
    {
        if (!isReady || oniRangeEntries.Count == 0)
            return;

        OniRangeData targetData = GetClosestOniData(oniValue);
        if (targetData?.guConsumption == null)
        {
            Debug.LogWarning("[MinimapColorController] guConsumption 데이터가 없습니다.");
            return;
        }

        ApplyPowerUsageCMap(targetData.guConsumption);
    }

    private OniRangeData GetClosestOniData(float oniValue)
    {
        OniRangeData closest = null;
        float minDistance = float.MaxValue;

        foreach (OniRangeData data in oniRangeEntries)
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

    private void ApplyPowerUsageCMap(Dictionary<DistrictType, double> guConsumption)
    {
        if (guConsumption == null || guConsumption.Count == 0)
            return;

        double minValue = double.MaxValue;
        double maxValue = double.MinValue;

        foreach (double value in guConsumption.Values)
        {
            minValue = Math.Min(minValue, value);
            maxValue = Math.Max(maxValue, value);
        }

        foreach (var kvp in guConsumption)
        {
            DistrictType districtType = kvp.Key;
            double powerUsage = kvp.Value;

            float t = 0f;
            if (maxValue > minValue)
                t = (float)((powerUsage - minValue) / (maxValue - minValue));

            Color cmapColor = Color.Lerp(lowPowerColor, highPowerColor, t);
            districtCurrentColor[districtType] = cmapColor;
            minimapManager.SetDistrictColor(districtType, cmapColor);
        }
    }

    private void HandleBlackoutDistrictChanged(DistrictType districtType)
    {
        if (_isSimulationOn)
            StopBlinkAndSetBlack();

        blinkingDistrictType = districtType;

        if (!_isSimulationOn)
            return;

        blackoutBlinkCoroutine = StartCoroutine(BlinkBlackoutDistrict(districtType));
    }

    private IEnumerator BlinkBlackoutDistrict(DistrictType districtType)
    {
        if (!districtCurrentColor.TryGetValue(districtType, out Color originalColor))
            yield break;

        bool dark = false;

        while (true)
        {
            Color target = dark ? blackoutColor : originalColor;
            minimapManager.SetDistrictColor(districtType, target);
            dark = !dark;
            yield return new WaitForSeconds(0.3f);
        }
    }

    private void StopBlinkAndSetBlack()
    {
        if (blackoutBlinkCoroutine != null)
        {
            StopCoroutine(blackoutBlinkCoroutine);
            blackoutBlinkCoroutine = null;
        }

        minimapManager.SetDistrictColor(blinkingDistrictType, blackoutColor);
    }

    private void StopBlinkAndRestore()
    {
        if (blackoutBlinkCoroutine != null)
        {
            StopCoroutine(blackoutBlinkCoroutine);
            blackoutBlinkCoroutine = null;
        }

        foreach (var kvp in districtCurrentColor)
            minimapManager.SetDistrictColor(kvp.Key, kvp.Value);

        blinkingDistrictType = DistrictType.None;
    }
}
