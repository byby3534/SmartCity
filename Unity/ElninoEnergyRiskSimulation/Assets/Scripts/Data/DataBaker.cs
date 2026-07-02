#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class DataBaker : MonoBehaviour
{
    [MenuItem("Tools/Bake Seoul Building Data")]
    public static async void BakeData()
    {
        Debug.Log("[DataBaker] GeoJSON 파싱 및 바이너리 굽기 시작...");

        // 1. 원본 데이터 파싱 (기존 DataParser 활용)
        DataParser parser = new DataParser();
        // 비동기를 동기화하여 에디터에서 강제 실행 (또는 async void로 처리)
        List<BuildingData> allBuildings = await parser.ParseGeoJson("seoul_buildings.geojson");

        if (allBuildings == null || allBuildings.Count == 0)
        {
            Debug.LogError("[DataBaker] 파싱 실패 또는 데이터가 없습니다.");
            return;
        }

        Debug.Log($"[DataBaker] 파싱 완료 ({allBuildings.Count}개). 이제 디스크에 굽기 시작합니다...");

        await Task.Run(() =>
        {
            var groupedByDistrict = allBuildings.GroupBy(b => b.districtId);
            string baseDirectory = "Assets/Resources/Districts";

            if (!Directory.Exists(baseDirectory))
                Directory.CreateDirectory(baseDirectory);

            using (BinaryWriter polyWriter = new BinaryWriter(File.Open($"{baseDirectory}/PolygonData.bytes", FileMode.Create)))
            {
                int globalPolygonIndex = 0;

                foreach (var group in groupedByDistrict)
                {
                    string districtDir = $"{baseDirectory}/{group.Key}";
                    Directory.CreateDirectory(districtDir);
                    string filePath = $"{districtDir}/District.bytes";
                    using (BinaryWriter writer = new BinaryWriter(File.Open(filePath, FileMode.Create)))
                    {
                        foreach (var b in group)
                        {
                            int vertexCount = b.polygon.Count;

                            // 구조체 순서와 동일하게 기록
                            writer.Write(b.lon);
                            writer.Write(b.lat);
                            writer.Write(b.height);
                            writer.Write(0.0f); // terrainAltitude
                            writer.Write(0.0f); // reducationValue
                            writer.Write(b.id);
                            writer.Write(b.districtId);
                            writer.Write((int)b.districtType);
                            writer.Write((int)b.buildingType);
                            writer.Write(0); // isBlackout

                            writer.Write(vertexCount);
                            writer.Write(globalPolygonIndex);

                            foreach (var v in b.polygon)
                            {
                                polyWriter.Write(v.x);
                                polyWriter.Write(v.y);
                            }

                            globalPolygonIndex += vertexCount;
                        }
                    }
                }
            }
        });

        Debug.Log("[DataBaker] 모든 구역 바이너리 빌드 완료!");
        AssetDatabase.Refresh();
    }
}
#endif