using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class DataParser
{
    private const double LAT_SCALE = 111320.0;
    private const double MIN_POLYGON_SIZE_METERS = 2.0;
    private const float DEFAULT_HEIGHT = 3.0f;
    private const float METERS_PER_FLOOR = 3.0f;
    public async Task<List<BuildingData>> ParseGeoJson(string fileName)
    {
        List<BuildingData> result = new List<BuildingData>();
        string path = Path.Combine(Application.streamingAssetsPath, fileName);

        if (!File.Exists(path))
        {
            Debug.LogError("[DataParser] 파일 경로에 없음: " + path);
            return result;
        }

        result = await Task.Run(() =>
        {
            List<BuildingData> parsed = new List<BuildingData>();
            int count = 0; // 진행률 모니터링용 카운트

            // 1. 파일을 메모리에 다 올리지 않고 빨대(Stream)를 꽂아 한 줄씩 읽음
            using (StreamReader sr = new StreamReader(path))
            using (JsonTextReader reader = new JsonTextReader(sr))
            {
                while (reader.Read())
                {
                    // "features" 배열을 찾을 때까지 직진
                    if (reader.TokenType == JsonToken.PropertyName && reader.Value?.ToString() == "features")
                    {
                        reader.Read(); // 배열 시작 지점 '[' 으로 이동

                        // 배열이 끝날 때까지 반복
                        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                        {
                            if (reader.TokenType == JsonToken.StartObject)
                            {
                                JObject feat = JObject.Load(reader);

                                try
                                {
                                    JToken props = feat["properties"];
                                    JToken geom = feat["geometry"];

                                    if (props == null || geom == null) continue;
                                    if (geom["type"]?.ToString() != "Polygon") continue;

                                    JArray ring = (JArray)geom["coordinates"][0];
                                    if (ring == null || ring.Count < 3) continue;

                                    List<double[]> rawCoords = new List<double[]>();
                                    foreach (JArray point in ring)
                                    {
                                        rawCoords.Add(new double[] { (double)point[0], (double)point[1] });
                                    }

                                    if (!IsValidPolygon(rawCoords)) continue;

                                    BuildingData data = new BuildingData();

                                    if (props["A0"] != null) data.id = (int)props["A0"];

                                    string address = props["A4"]?.ToString();
                                    string addressDetail = props["A5"]?.ToString();
                                    data.name = (address + " " + addressDetail).Trim();

                                    string sigunguStr = props["A4"]?.ToString();
                                    if (!string.IsNullOrEmpty(sigunguStr))
                                    {
                                        string districtName = ExtractDistrictName(sigunguStr);
                                        data.districtType = DataConverter.GetDistrictType(districtName);
                                    }

                                    string buildingTypeStr = props["building_type"]?.ToString();
                                    data.buildingType = DataConverter.GetBuildingType(buildingTypeStr);

                                    string sigunguCodeStr = props["A23"]?.ToString();
                                    if (int.TryParse(sigunguCodeStr, out int sigunguCode))
                                        data.districtId = sigunguCode;
                                    else
                                        data.districtId = 0;

                                    double a26 = props["A26"] != null && props["A26"].Type != JTokenType.Null ? (double)props["A26"] : 0;
                                    double preHeight = props["height"] != null && props["height"].Type != JTokenType.Null ? (double)props["height"] : 0;

                                    if (preHeight > 0 && a26 > 0)
                                    {
                                        double meterPerFloor = preHeight / a26;
                                        if (meterPerFloor < 2.0 || meterPerFloor > 10.0) preHeight = 0;
                                    }

                                    if (preHeight > 0) data.height = (float)preHeight;
                                    else if (a26 > 0) data.height = (float)(a26 * METERS_PER_FLOOR);
                                    else data.height = DEFAULT_HEIGHT;

                                    data.floors = (int)a26;

                                    double lonSum = 0; double latSum = 0;
                                    foreach (double[] c in rawCoords)
                                    {
                                        lonSum += c[0];
                                        latSum += c[1];
                                    }

                                    data.lon = lonSum / rawCoords.Count;
                                    data.lat = latSum / rawCoords.Count;
                                    data.polygon = ConvertToLocalPolygon(rawCoords, data.lon, data.lat);

                                    if (data.polygon.Count > 1 && data.polygon[0] == data.polygon[data.polygon.Count - 1])
                                        data.polygon.RemoveAt(data.polygon.Count - 1);

                                    if (data.polygon.Count < 3) continue;

                                    parsed.Add(data);
                                    // ===========================================

                                    count++;
                                    // 1만 개 파싱할 때마다 콘솔에 생존 신고 (프리징 여부 확인용)
                                    if (count % 10000 == 0)
                                    {
                                        Debug.Log($"[DataParser] 열일 중... {count}개 파싱 완료");
                                    }
                                }
                                catch { continue; }
                            }
                        }
                        break; // features 배열 처리가 끝나면 루프 종료
                    }
                }
            }

            return parsed;
        });

        Debug.Log("[DataParser] GeoJSON 스트리밍 파싱 최종 완료: " + result.Count + "개");
        return result;
    }

    //!!!!!!추가된 부분!!!!!!!!!! 
    // 범용 JSON 파싱 — StreamingAssets 의 단순 배열 JSON 파일 처리
    // [ { ... }, { ... } ] 형태의 JSON 파일이면 무엇이든 파싱 가능
    //
    // 사용 예시:
    //   List<District>    = await parser.ParseJson<District>("seoul_districts.json");
    //   List<ShelterData> = await parser.ParseJson<ShelterData>("shelters.json");
    public async Task<List<T>> ParseJson<T>(string fileName)
    {
        List<T> result = new List<T>();

        string path = Path.Combine(Application.streamingAssetsPath, fileName);

        if (!File.Exists(path))
        {
            Debug.LogError($"[DataParser] 파일 없음: {path}");
            return result;
        }

        string json = File.ReadAllText(path);

        result = await Task.Run(() =>
            JsonConvert.DeserializeObject<List<T>>(json) ?? new List<T>()
        );

        Debug.Log($"[DataParser] {fileName} 로드 완료 — {result.Count}개");
        return result;
    }

    private List<Vector2> ConvertToLocalPolygon(List<double[]> rawCoords, double cLon, double cLat)
    {
        double lonScale = LAT_SCALE * Math.Cos(cLat * Math.PI / 180.0);

        List<Vector2> polygon = new List<Vector2>();
        foreach (double[] c in rawCoords)
        {
            // 중심점에서 얼마나 떨어져 있는지 미터로 계산한다.
            float x = (float)((c[0] - cLon) * lonScale);
            float z = (float)((c[1] - cLat) * LAT_SCALE);
            polygon.Add(new Vector2(x, z));
        }
        return polygon;
    }
    private bool IsValidPolygon(List<double[]> coords)
    {
        if (coords.Count < 3) return false;

        double cLat = 0;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        double minLat = double.MaxValue, maxLat = double.MinValue;

        foreach (double[] c in coords)
        {
            cLat += c[1];
            if (c[0] < minLon) minLon = c[0];
            if (c[0] > maxLon) maxLon = c[0];
            if (c[1] < minLat) minLat = c[1];
            if (c[1] > maxLat) maxLat = c[1];
        }

        // 위도 평균값 계산 (경도 1도당 미터 거리 보정에 사용)
        cLat /= coords.Count;

        // 도 단위 차이를 미터로 변환하여 건물의 가로/세로 크기를 구한다.
        double lonScale = LAT_SCALE * Math.Cos(cLat * Math.PI / 180.0);
        double xRange = (maxLon - minLon) * lonScale;
        double zRange = (maxLat - minLat) * LAT_SCALE;

        // 가로 세로 중 긴 쪽이 2m 미만이면 실제로 존재할 수 없는 건물이므로 탈락
        return Math.Max(xRange, zRange) >= MIN_POLYGON_SIZE_METERS;
    }

    private string ExtractDistrictName(string fullAddress)
    {
        // 공백으로 나눈 뒤 '구'로 끝나는 단어를 찾음
        string[] parts = fullAddress.Split(' ');
        foreach (string part in parts)
        {
            if (part.EndsWith("구"))
            {
                return part; // 예: "강남구"
            }
        }
        return "Unknown";
    }
}
