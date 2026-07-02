using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MinimapColorController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MinimapManager minimapManager;
    [SerializeField] private DataManager dataManager;
    [SerializeField] private BlackoutSimulationController simulationController;

    // cmap 스타일 설정
    [Header("CMap Style")]
    [SerializeField] private Color lowPowerColor = new Color(1f, 0.9f, 0.75f, 0.9f);
    [SerializeField] private Color highPowerColor = new Color(1f, 0.25f, 0.05f, 0.9f);

    [Header("BlackOut Style")]
    [SerializeField] private Color blackoutColor = new Color(0.2f, 0.2f, 0.2f, 0.9f); // 정전 컬러


    // 구별 현재 cmap 색 저장
    private Dictionary<DistrictType, Color> districtCurrentColor =
        new Dictionary<DistrictType, Color>();

    private readonly List<OniRangeData> oniRangeEntries = new List<OniRangeData>();

    // 현재 선택된 ONI 값
    private float currentOni = 0f;

    // 현재 ONI 데이터 존재 여부
    private bool hasCurrentOni = false;

    private bool isReady = false;

    private bool _isSimulationOn; // 추가

    // 깜박이는 구 이름
    private DistrictType blinkingDistrictType;

    // 현재 깜빡이는 코루틴
    private Coroutine blackoutBlinkCoroutine;


    private void Awake()
    {
        if (minimapManager == null)
            minimapManager = GetComponent<MinimapManager>();

        if (dataManager == null)
            dataManager = FindFirstObjectByType<DataManager>();

        if (simulationController == null)
            simulationController = FindFirstObjectByType<BlackoutSimulationController>();
    }

    // dataManager 이벤트 구독
    private void OnEnable()
    {
        if (dataManager != null && simulationController != null)
        {
            // ONI 데이터 변경
            dataManager.OniRangeDataUpdated += HandleOniRangeDataUpdated;
            // 전력 데이터 변경
            dataManager.OnPowerDataUpdated += HandlePowerDataUpdated;
            // 블랙아웃 구 이벤트 구독
            simulationController.OnBlackoutDistrictChanged += HandleBlackoutDistrictChanged;
            simulationController.OnBlackoutSimulationToggled += HandleSimulationToggled;
        }
        else
        {
            Debug.LogWarning("[MinimapColorController] DataManager가 연결되지 않았습니다.");
        }
    }

    private void OnDisable()
    {
        if (dataManager != null)
        {
            dataManager.OniRangeDataUpdated -= HandleOniRangeDataUpdated;
            dataManager.OnPowerDataUpdated -= HandlePowerDataUpdated;
            simulationController.OnBlackoutDistrictChanged -= HandleBlackoutDistrictChanged;
            simulationController.OnBlackoutSimulationToggled -= HandleSimulationToggled;
        }
    }


    public void SetReady()
    {
        isReady = true;
        ApplyCurrentOniCMap();
    }

    private void HandleSimulationToggled(bool isOn)
    {
        _isSimulationOn = isOn;
        if (!isOn)
            StopBlinkAndRestore();
    }

        // 선택한 연/월 ONI 데이터 저장 -> cmap 갱신
    private void HandlePowerDataUpdated(PowerGridData data)
    {
        if (data == null) return;

        currentOni = data.oni;

        // 데이터 수신 여부
        hasCurrentOni = true;

        ApplyCurrentOniCMap();
    }

    // ONI 구간별 전력 사용량 데이터 저장 -> cmap 갱신
    private void HandleOniRangeDataUpdated(List<OniRangeData> data)
    {
        if (data == null || data.Count == 0)
        {
            Debug.LogWarning("[MinimapColorController] OniRangeData가 비어 있습니다.");
            return;
        }

        // 이전 데이터 삭제
        oniRangeEntries.Clear();

        // 새 데이터 저장
        oniRangeEntries.AddRange(data);

        // cmap 갱신
        ApplyCurrentOniCMap();
    }

    // 현재 oni와 가장 가까운 oni 데이터 찾아 해당 oni의 구별 전력 사용량 가져오기
    private void ApplyCurrentOniCMap()
    {
        if (!isReady) return;
        if (oniRangeEntries.Count == 0) return;

        float targetOni = hasCurrentOni ? currentOni : 0f;

        OniRangeData targetData = GetClosestOniData(targetOni);

        if (targetData == null || targetData.guConsumption == null)
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

    // cmap 그리기
    private void ApplyPowerUsageCMap(Dictionary<DistrictType, double> guConsumption)
    {
        if (guConsumption == null || guConsumption.Count == 0) return;

        // 전력 사용량 최대 최소 초기값 설정
        double minValue = double.MaxValue;
        double maxValue = double.MinValue;

        // 구 전력 사용량 돌면서 최대/최소 계산
        foreach (double value in guConsumption.Values)
        {
            minValue = Math.Min(minValue, value);
            maxValue = Math.Max(maxValue, value);
        }

        // 구 마다 전력 사용량에 따라 색상 결정됨
        foreach (var kvp in guConsumption)
        {
            DistrictType districtType = kvp.Key;
            double powerUsage = kvp.Value;

            // 전력 사용량 정규화 (0-1)
            float t = 0f;
            if (maxValue > minValue)
            {
                t = (float)((powerUsage - minValue) / (maxValue - minValue));
            }

            // 색상 계산
            Color cmapColor = Color.Lerp(lowPowerColor, highPowerColor, t);
            districtCurrentColor[districtType] = cmapColor;

            // 폴리곤 색 변경
            minimapManager.SetDistrictColor(districtType, cmapColor);

        }
    }

    // 블랙아웃 이벤트 함수
    private void HandleBlackoutDistrictChanged(DistrictType districtType)
    {
        // 원래 정전 중인 구의 코루틴 멈추고 검정으로 색상 고정
        if (_isSimulationOn)
        {
            StopBlinkAndSetBlack();
        }

        // 새로운 정전 구 이름으로 갱신
        blinkingDistrictType = districtType;

        // 시뮬레이션 토클 off 이면 멈추기
        if (!_isSimulationOn)
            return;

        // 갱신된 구로 블랙아웃 코루틴 시작
        blackoutBlinkCoroutine =
            StartCoroutine(BlinkBlackoutDistrict(districtType));
    }

    // 정전 구 깜박임 코루틴
    private IEnumerator BlinkBlackoutDistrict(DistrictType districtType)
    {
        // 구 cmap 색상 가져오기
        if (!districtCurrentColor.TryGetValue(districtType, out Color originalColor))
            yield break;

        // 처음엔 검정 아님
        bool dark = false;

        while (true)
        {
            // 검정인지 아닌지 확인 -> 검정이면 cmap / 아니면 검정
            Color target = dark ? blackoutColor : originalColor;

            // 구 색상 변경
            minimapManager.SetDistrictColor(districtType, target);

            // bool 검정 반대로 설정
            dark = !dark;

            // 0.3초씩 깜박임
            yield return new WaitForSeconds(0.3f);
        }
    }

    // 코루틴 stop -> 검정색으로 고정
    private void StopBlinkAndSetBlack()
    {
        if (blackoutBlinkCoroutine != null)
        {
            StopCoroutine(blackoutBlinkCoroutine);
            blackoutBlinkCoroutine = null;
        }

        minimapManager.SetDistrictColor(blinkingDistrictType, blackoutColor);
    }

    // 코루틴 stop & 토클 off -> 원래 cmap 색으로 복원
    private void StopBlinkAndRestore()
    {
        if (blackoutBlinkCoroutine != null)
        {
            // 깜박이는 코루틴 stop
            StopCoroutine(blackoutBlinkCoroutine);
            blackoutBlinkCoroutine = null;
        }

        foreach (var kvp in districtCurrentColor)
        {
            // 정전된 모든 구 색상을 검정 -> 원래 cmap색으로
            DistrictType districtName = kvp.Key;
            Color originalColor = kvp.Value;

            minimapManager.SetDistrictColor(districtName, originalColor);
        }

        blinkingDistrictType = DistrictType.None;
    }

}
