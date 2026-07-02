using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DistrictManager : MonoBehaviour
{
    public Dictionary<DistrictType, DistrictObject> districtObjects = new Dictionary<DistrictType, DistrictObject>();

    [SerializeField] private DataManager dataManager;
    [SerializeField] private BuildingManager buildingManager;

    public event Action<Dictionary<DistrictType, DistrictData>> OnAllDistrictsDataUpdated;
    public event Action<Dictionary<DistrictType, DistrictObject>> OnAllDistrictsObjectsUpdated;

    // GPU 버퍼 준비 여부
    private bool _bufferReady;

    // 건물 인덱스별 districtType/buildingType 매핑 (한 번만 빌드)
    private int[] _buildingDistrictTypes;
    private int[] _buildingBuildingTypes;

    private void OnEnable()
    {
        if (dataManager != null)
        {
            dataManager.OnDistrictDataUpdated += HandleDistrictDataUpdated;
            dataManager.OnAllDistrictsParsed += HandleAllDistrictsParsed;
            buildingManager.OnDistrictObjectCreated += RegisterDistrictObject;
        }
        else
        {
            Debug.LogWarning("[DistrictManager] dataManager가 존재하지 않습니다.");
        }
    }

    private void OnDisable()
    {
        if (dataManager != null)
        {
            dataManager.OnDistrictDataUpdated -= HandleDistrictDataUpdated;
            dataManager.OnAllDistrictsParsed -= HandleAllDistrictsParsed;
            buildingManager.OnDistrictObjectCreated -= RegisterDistrictObject;
        }
    }

    private void Start()
    {
        StartCoroutine(WaitForBufferReady());
    }

    /// <summary>
    /// CityManager의 렌더링 버퍼가 준비될 때까지 대기.
    /// 준비 후 건물 매핑 테이블을 구축하고, 이미 도착한 데이터가 있으면 적용.
    /// </summary>
    private IEnumerator WaitForBufferReady()
    {
        if (buildingManager == null)
            buildingManager = FindFirstObjectByType<BuildingManager>();

        yield return new WaitUntil(() =>
            buildingManager != null &&
            buildingManager.CachedRenderData != null &&
            buildingManager.CachedRenderData.Length > 0);

        // 전체 건물 데이터에서 구/건물유형 매핑 테이블 생성
        BuildBuildingTypeMapping();
        _bufferReady = true;

        Debug.Log($"[DistrictManager] GPU 버퍼 준비 완료 ({buildingManager.CachedRenderData.Length}개 건물)");

        // 버퍼 준비 전에 이미 API 데이터가 도착했으면 즉시 반영
        if (districtObjects.Count > 0)
        {
            var dataDict = ExtractDataDictionary();
            if (dataDict.Count > 0)
                ApplyReductionScoresToBuffer(dataDict);
        }
    }

    /// <summary>
    /// CityManager에서 전체 NativeBuildingData를 한 번 읽어와
    /// 인덱스별 districtType/buildingType 배열을 만든다.
    /// 렌더링 버퍼(BuildingRenderData)에는 이 정보가 없으므로 여기서 매핑.
    /// </summary>
    private void BuildBuildingTypeMapping()
    {
        NativeBuildingData[] fullData = buildingManager.GetFullBuildingData();
        _buildingDistrictTypes = new int[fullData.Length];
        _buildingBuildingTypes = new int[fullData.Length];

        for (int i = 0; i < fullData.Length; i++)
        {
            _buildingDistrictTypes[i] = fullData[i].districtType;
            _buildingBuildingTypes[i] = fullData[i].buildingType;
        }

        Debug.Log($"[DistrictManager] 건물 매핑 테이블 구축 완료 ({fullData.Length}개)");
    }

    // ── DataManager 이벤트 핸들러 ──

    private void HandleDistrictDataUpdated(DistrictData newData)
    {
        if (districtObjects.TryGetValue(newData.districtType, out DistrictObject district))
        {
            district.data = newData;
        }
        else
        {
            districtObjects[newData.districtType] = new DistrictObject
            {
                data = newData,
                IsShutDown = false
            };
        }

        Debug.Log($"[DistrictManager] [{newData.districtType}] 전력 데이터 업데이트 완료.");
    }

    private void HandleAllDistrictsParsed()
    {
        Debug.Log("[DistrictManager] 모든 구 데이터 세팅 완료. 구독자들에게 전달!");

        Dictionary<DistrictType, DistrictData> pureDataDict = ExtractDataDictionary();

        // 기존 이벤트 발생 (UI 등 다른 구독자용)
        OnAllDistrictsDataUpdated?.Invoke(pureDataDict);
        OnAllDistrictsObjectsUpdated?.Invoke(districtObjects);

        // ── 수요감축 필요도 → GPU 버퍼 반영 ──
        if (_bufferReady)
            ApplyReductionScoresToBuffer(pureDataDict);
    }

    // ── DistrictObject 등록 ──

    public void RegisterDistrictObject(DistrictObject districtObject)
    {
        if (districtObject?.data == null) return;

        DistrictType districtType = (DistrictType)districtObject.districtId;

        if (districtObjects.TryGetValue(districtType, out DistrictObject tempObject))
        {
            districtObject.data = tempObject.data;
            districtObject.IsShutDown = tempObject.IsShutDown;
        }

        districtObjects[districtType] = districtObject;

        Debug.Log($"[DistrictManager] [{districtType}] DistrictObject 등록 완료.");
    }

    // ── 수요감축 필요도 → 렌더링 버퍼 반영 ──

    /// <summary>
    /// 모든 구의 건물 reductionValue를 CachedRenderData에 기록하고 GPU에 업로드한다.
    /// </summary>
    private void ApplyReductionScoresToBuffer(
        IReadOnlyDictionary<DistrictType, DistrictData> districts)
    {
        BuildingRenderData[] buffer = buildingManager.CachedRenderData;
        if (buffer == null || buffer.Length == 0) return;

        // 전체 구에서 min/max 계산 (정규화용)
        GetScoreRange(districts, out float minScore, out float maxScore);

        int updatedCount = 0;

        for (int i = 0; i < buffer.Length; i++)
        {
            // 매핑 테이블에서 이 건물의 구/용도 타입 조회
            DistrictType dt = (DistrictType)_buildingDistrictTypes[i];
            BuildingType bt = (BuildingType)_buildingBuildingTypes[i];

            if (!districts.TryGetValue(dt, out DistrictData districtData))
            {
                buffer[i].reductionValue = 0f;
                continue;
            }

            if (districtData.buildingReductionScores != null &&
                districtData.buildingReductionScores.TryGetValue(bt, out float score))
            {
                buffer[i].reductionValue = (maxScore > minScore)
                    ? Mathf.Clamp01((score - minScore) / (maxScore - minScore))
                    : 0f;
                updatedCount++;
            }
            else
            {
                buffer[i].reductionValue = 0f;
            }
        }

        // GPU에 반영
        buildingManager.MarkBufferDirty();
        buildingManager.FlushBufferToGPU();

        // reductionValue가 실제 값으로 갱신되었으므로, 정전 연출용 정렬 인덱스도 다시 계산해야
        // "필요도가 높은 건물부터" 정전이 발생한다 (Start() 시점엔 reductionValue가 전부 0이라
        // 그때 만든 정렬은 의미가 없음).
        buildingManager.RebuildSortedIndices();

        Debug.Log($"[DistrictManager] reductionValue 갱신 완료 ({updatedCount}개 건물, 범위 {minScore:F3}~{maxScore:F3})");
    }

    // ── 유틸리티 ──

    private Dictionary<DistrictType, DistrictData> ExtractDataDictionary()
    {
        var dict = new Dictionary<DistrictType, DistrictData>();
        foreach (var kvp in districtObjects)
        {
            dict[kvp.Key] = kvp.Value.data;
        }
        return dict;
    }

    private static void GetScoreRange(
        IReadOnlyDictionary<DistrictType, DistrictData> districts,
        out float minScore, out float maxScore)
    {
        minScore = float.MaxValue;
        maxScore = float.MinValue;

        foreach (DistrictData data in districts.Values)
        {
            if (data?.buildingReductionScores == null) continue;

            foreach (float score in data.buildingReductionScores.Values)
            {
                minScore = Mathf.Min(minScore, score);
                maxScore = Mathf.Max(maxScore, score);
            }
        }

        if (minScore > maxScore)
        {
            minScore = 0f;
            maxScore = 1f;
        }
    }
}
