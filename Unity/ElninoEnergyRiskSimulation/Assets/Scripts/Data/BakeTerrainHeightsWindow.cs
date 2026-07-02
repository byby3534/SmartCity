using System;
using System.Collections;
using System.IO;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Cesium 지형 높이를 모든 구의 건물에 대해 사전 계산하고
/// Resources/Districts/{districtId}/TerrainHeights.bytes 로 저장하는 에디터 도구.
///
/// 사용법:
///   1. 씬을 플레이 모드로 실행 (Cesium 지형 타일이 로드된 상태)
///   2. Tools > Bake Terrain Heights 메뉴 클릭
///   3. [씬에서 자동 탐색] 버튼 클릭
///   4. [지형 높이 굽기 시작] 클릭 → 구별로 순차 처리
///   5. 완료 후 AssetDatabase 자동 새로고침
///
/// 저장된 파일은 BuildingManager가 런타임에서 자동으로 로드해
/// SampleHeightMostDetailed 호출 없이 즉시 메시를 생성합니다.
/// </summary>
public class BakeTerrainHeightsWindow : EditorWindow
{
    private BuildingManager _buildingManager;
    private Cesium3DTileset _terrainTileset;
    private bool _isBaking;
    private string _status = "대기 중";
    private int _progress;
    private int _total;

    [MenuItem("Tools/Bake Terrain Heights")]
    static void ShowWindow()
    {
        GetWindow<BakeTerrainHeightsWindow>("지형 높이 사전 계산");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("지형 높이 사전 계산 도구", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUILayout.HelpBox(
            "플레이 모드에서 Cesium 지형의 건물별 높이를 미리 계산해 파일로 저장합니다.\n" +
            "한 번만 실행하면 이후부터 BuildingManager가 Cesium 샘플링 없이 바로 메시를 생성합니다.\n\n" +
            "출력: Resources/Districts/{districtId}/TerrainHeights.bytes",
            MessageType.Info);

        EditorGUILayout.Space(4);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("⚠ 플레이 모드를 먼저 시작하세요 (Cesium 지형 타일이 로드되어야 합니다).", MessageType.Warning);
        }

        EditorGUILayout.Space(4);

        _buildingManager = (BuildingManager)EditorGUILayout.ObjectField(
            "BuildingManager", _buildingManager, typeof(BuildingManager), true);
        _terrainTileset = (Cesium3DTileset)EditorGUILayout.ObjectField(
            "Terrain Tileset", _terrainTileset, typeof(Cesium3DTileset), true);

        EditorGUILayout.Space(4);

        if (GUILayout.Button("씬에서 자동 탐색"))
        {
            _buildingManager = FindFirstObjectByType<BuildingManager>();
            _terrainTileset = FindFirstObjectByType<Cesium3DTileset>();

            if (_buildingManager == null)
                Debug.LogWarning("[TerrainBaker] BuildingManager를 씬에서 찾을 수 없습니다.");
            if (_terrainTileset == null)
                Debug.LogWarning("[TerrainBaker] Cesium3DTileset을 씬에서 찾을 수 없습니다.");
        }

        EditorGUILayout.Space(8);

        bool canBake = Application.isPlaying && !_isBaking
                       && _buildingManager != null && _terrainTileset != null;

        GUI.enabled = canBake;
        if (GUILayout.Button("지형 높이 굽기 시작", GUILayout.Height(40)))
        {
            _isBaking = true;
            _progress = 0;
            _buildingManager.StartCoroutine(BakeRoutine());
        }
        GUI.enabled = true;

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("상태", _status);

        if (_total > 0)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(r, (float)_progress / _total, $"{_progress} / {_total} 구 완료");
        }

        if (_isBaking) Repaint();
    }

    private IEnumerator BakeRoutine()
    {
        DistrictType[] allDistricts = (DistrictType[])Enum.GetValues(typeof(DistrictType));
        _total = allDistricts.Length - 1; // DistrictType.None 제외

        string districtsBaseDir = Path.Combine(Application.dataPath, "Resources", "Districts");

        foreach (DistrictType district in allDistricts)
        {
            if (district == DistrictType.None) continue;

            int districtId = (int)district;
            _status = $"[{district}] 건물 좌표 로드 중...";
            Repaint();
            yield return null;

            // BuildingManager의 public 래퍼를 통해 C++ DLL에서 건물 위치 취득
            double3[] positions = _buildingManager.GetBuildingPositionsForBaking(districtId);

            if (positions.Length == 0)
            {
                Debug.LogWarning($"[TerrainBaker] {district} ({districtId}): 건물 없음 — 건너뜀");
                _progress++;
                yield return null;
                continue;
            }

            _status = $"[{district}] Cesium 지형 높이 샘플링 중... ({positions.Length:N0}개 건물)";
            Repaint();

            // Task → WaitUntil 패턴 (코루틴에서 async 결과 기다리기)
            var task = _terrainTileset.SampleHeightMostDetailed(positions);
            yield return new WaitUntil(() => task.IsCompleted);

            float[] heights = new float[positions.Length];

            if (task.IsFaulted)
            {
                Debug.LogError($"[TerrainBaker] {district} 샘플링 실패: {task.Exception?.GetBaseException().Message} — 해당 구는 높이 0으로 저장됩니다.");
                // heights 배열이 이미 0으로 초기화되어 있으므로 그대로 저장
            }
            else
            {
                var result = task.Result;
                for (int i = 0; i < positions.Length; i++)
                {
                    heights[i] = result.sampleSuccess[i] ? (float)result.longitudeLatitudeHeightPositions[i].z : 0f;
                }
            }

            // float[] → byte[] 변환 후 파일 저장
            byte[] raw = new byte[heights.Length * sizeof(float)];
            Buffer.BlockCopy(heights, 0, raw, 0, raw.Length);

            string districtDir = Path.Combine(districtsBaseDir, $"{districtId}");
            Directory.CreateDirectory(districtDir);
            string filePath = Path.Combine(districtDir, "TerrainHeights.bytes");
            File.WriteAllBytes(filePath, raw);

            Debug.Log($"[TerrainBaker] {district} ({districtId}) 완료 — {heights.Length:N0}개 건물 저장 ({raw.Length / 1024} KB)");

            _status = $"[{district}] 저장 완료 ({heights.Length:N0}개)";
            _progress++;
            Repaint();

            yield return null; // 다음 구로 넘어가기 전에 한 프레임 대기 (UI 갱신)
        }

        // Resources 폴더 새로고침 (Unity가 새 .bytes 파일을 인식하도록)
        AssetDatabase.Refresh();

        _status = $"전체 완료! {_total}개 구 처리됨 — AssetDatabase 새로고침 완료";
        _isBaking = false;
        Repaint();

        Debug.Log("[TerrainBaker] 지형 높이 사전 계산 전체 완료. Resources/Districts/{districtId}/ 폴더를 확인하세요.");
    }
}
