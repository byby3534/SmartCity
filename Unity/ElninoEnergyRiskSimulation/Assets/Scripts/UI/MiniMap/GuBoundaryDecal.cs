using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Newtonsoft.Json.Linq;
using CesiumForUnity;
using Unity.Mathematics;

public class GuBoundaryDecal : MonoBehaviour
{
    [SerializeField] private MinimapManager minimapManager;
    [Header("GeoJSON")]
    public string fileName = "seoul_district.geojson";

    [Header("Cesium")]
    public CesiumGeoreference cesiumGeoreference;

    [Header("Decal")]
    public Material decalMaterial;
    public float projectionDepth = 5000f;
    public float altitude = 3000f;
    public float drawDistance = 50000f;

    [Header("Texture")]
    public int textureResolution = 4096;
    [Tooltip("경계선 반폭 (텍스처 픽셀)")]
    public float lineHalfWidth = 0.65f;
    public Color lineColor = new Color(1f, 0f, 0f, 1f);
    public Color selectedFillColor = new Color(0.72f, 0.72f, 0.72f, 0.62f);

    private const double LatScale = 111320.0;

    private Material _decalMatInstance;
    private Texture2D _texture;
    private string _selectedDistrict;

    private double _minLon, _maxLon, _minLat, _maxLat;
    private readonly List<DistrictData> _districts = new List<DistrictData>();
    private readonly List<JArray> _allRings = new List<JArray>();

    private struct DistrictData
    {
        public string name;
        public List<JArray> outerRings;
    }

    private void OnEnable()
    {
        minimapManager.OnDistrictSelected += HandleDistrictSelected;
    }

    private void OnDisable()
    {
        minimapManager.OnDistrictSelected -= HandleDistrictSelected;
    }

    private void Start()
    {
        LoadAndSpawn();
    }

    private void HandleDistrictSelected(DistrictType districtType)
    {
        SetSelectedDistrict(DataConverter.GetDistrictName(districtType));
    }

    public void SetSelectedDistrict(string districtName)
    {
        _selectedDistrict = districtName;
        RebuildTexture();
    }

    private void LoadAndSpawn()
    {
        if (decalMaterial == null)
        {
            Debug.LogError("[GuBoundaryDecal] decalMaterial이 할당되지 않았습니다.");
            return;
        }

        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogError("[GuBoundaryDecal] GeoJSON 없음: " + path);
            return;
        }

        JObject root = JObject.Parse(File.ReadAllText(path));
        JArray features = (JArray)root["features"];
        if (features == null || features.Count == 0) return;

        _districts.Clear();
        _allRings.Clear();
        _minLon = double.MaxValue; _maxLon = double.MinValue;
        _minLat = double.MaxValue; _maxLat = double.MinValue;

        foreach (JObject feature in features)
        {
            JToken props = feature["properties"];
            JToken geom  = feature["geometry"];
            if (geom == null) continue;

            string name = props?["SIGUNGU_NM"]?.ToString() ?? "Unknown";
            string type = geom["type"]?.ToString();
            var district = new DistrictData { name = name, outerRings = new List<JArray>() };

            if (type == "Polygon")
            {
                var coords = (JArray)geom["coordinates"];
                CollectRing((JArray)coords[0], district.outerRings);
            }
            else if (type == "MultiPolygon")
            {
                foreach (JArray polygon in (JArray)geom["coordinates"])
                    CollectRing((JArray)polygon[0], district.outerRings);
            }

            if (district.outerRings.Count > 0)
                _districts.Add(district);
        }

        if (_allRings.Count == 0) return;

        double lonRange = _maxLon - _minLon;
        double latRange = _maxLat - _minLat;
        _minLon -= lonRange * 0.005;
        _maxLon += lonRange * 0.005;
        _minLat -= latRange * 0.005;
        _maxLat += latRange * 0.005;

        double centerLon = (_minLon + _maxLon) * 0.5;
        double centerLat = (_minLat + _maxLat) * 0.5;
        double lonScale = LatScale * System.Math.Cos(centerLat * System.Math.PI / 180.0);
        lonRange = _maxLon - _minLon;
        latRange = _maxLat - _minLat;

        float widthM  = (float)(lonRange * lonScale);
        float heightM = (float)(latRange * LatScale);

        int res = textureResolution;
        _texture = new Texture2D(res, res, TextureFormat.RGBA32, false);
        _texture.wrapMode = TextureWrapMode.Clamp;
        _texture.filterMode = FilterMode.Bilinear;

        _decalMatInstance = new Material(decalMaterial);
        _decalMatInstance.SetTexture("Base_Map", _texture);
        _decalMatInstance.SetColor("_BaseColor", Color.white);

        RebuildTexture();

        Transform parent = cesiumGeoreference != null
            ? cesiumGeoreference.transform
            : transform;

        GameObject anchorObj = new GameObject("Decal_Gu_Seoul");
        anchorObj.transform.SetParent(parent, false);

        CesiumGlobeAnchor globeAnchor = anchorObj.AddComponent<CesiumGlobeAnchor>();
        globeAnchor.longitudeLatitudeHeight = new double3(centerLon, centerLat, altitude);

        GameObject projObj = new GameObject("Projector");
        projObj.transform.SetParent(anchorObj.transform, false);
        projObj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        DecalProjector projector = projObj.AddComponent<DecalProjector>();
        projector.size                = new Vector3(widthM, heightM, projectionDepth);
        projector.pivot               = new Vector3(0f, 0f, -projectionDepth * 0.5f);
        projector.material            = _decalMatInstance;
        projector.drawDistance        = drawDistance;
        // Use Rendering Layers 모드 — BUILDING Rendering Layer는 제외
        projector.renderingLayerMask  = ~RenderingLayerMask.GetMask("BUILDING");

        Debug.Log($"[GuBoundaryDecal] 완료 — 구 {_districts.Count}개, 텍스처 {res}px");
    }

    private void CollectRing(JArray outerRing, List<JArray> districtRings)
    {
        districtRings.Add(outerRing);
        _allRings.Add(outerRing);

        foreach (JArray coord in outerRing)
        {
            double lon = coord[0].Value<double>();
            double lat = coord[1].Value<double>();
            if (lon < _minLon) _minLon = lon;
            if (lon > _maxLon) _maxLon = lon;
            if (lat < _minLat) _minLat = lat;
            if (lat > _maxLat) _maxLat = lat;
        }
    }

    private void RebuildTexture()
    {
        if (_texture == null) return;

        int res = _texture.width;
        var pixels = new Color[res * res];

        if (!string.IsNullOrEmpty(_selectedDistrict))
        {
            foreach (DistrictData d in _districts)
            {
                if (d.name != _selectedDistrict) continue;
                foreach (JArray ring in d.outerRings)
                {
                    List<Vector2> pts = RingToPoints(ring, res);
                    FillPolygon(pixels, res, pts, selectedFillColor);
                }
            }
        }

        foreach (JArray ring in _allRings)
            DrawRing(pixels, res, ring);

        _texture.SetPixels(pixels);
        _texture.Apply(false);
    }

    private List<Vector2> RingToPoints(JArray ring, int res)
    {
        double lonRange = _maxLon - _minLon;
        double latRange = _maxLat - _minLat;
        var pts = new List<Vector2>();

        foreach (JArray coord in ring)
        {
            double lon = coord[0].Value<double>();
            double lat = coord[1].Value<double>();
            float u = (float)((lon - _minLon) / lonRange);
            float v = 1f - (float)((lat - _minLat) / latRange);
            pts.Add(new Vector2(u * (res - 1), v * (res - 1)));
        }

        if (pts.Count > 1 && Vector2.Distance(pts[0], pts[pts.Count - 1]) < 0.5f)
            pts.RemoveAt(pts.Count - 1);

        return pts;
    }

    private void DrawRing(Color[] pixels, int res, JArray ring)
    {
        List<Vector2> pts = RingToPoints(ring, res);
        for (int i = 0; i < pts.Count; i++)
            DrawSegment(pixels, res, pts[i], pts[(i + 1) % pts.Count], lineColor, lineHalfWidth);
    }

    // 선 — 불투명하게 덮어써서 지형과 섞여 흐려지지 않게
    private static void DrawSegment(Color[] pixels, int res, Vector2 a, Vector2 b, Color col, float halfWidth)
    {
        float minX = Mathf.Min(a.x, b.x) - halfWidth - 1f;
        float maxX = Mathf.Max(a.x, b.x) + halfWidth + 1f;
        float minY = Mathf.Min(a.y, b.y) - halfWidth - 1f;
        float maxY = Mathf.Max(a.y, b.y) + halfWidth + 1f;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(minX));
        int x1 = Mathf.Min(res - 1, Mathf.CeilToInt(maxX));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(minY));
        int y1 = Mathf.Min(res - 1, Mathf.CeilToInt(maxY));

        Vector2 ab = b - a;
        float abLenSq = ab.sqrMagnitude;
        if (abLenSq < 1e-6f) return;

        float aa = 0.2f;

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSq);
                float dist = Vector2.Distance(p, a + t * ab);

                if (dist > halfWidth + aa) continue;

                float alpha = dist <= halfWidth
                    ? 1f
                    : Mathf.Clamp01((halfWidth + aa - dist) / aa);

                int idx = y * res + x;
                pixels[idx] = new Color(col.r, col.g, col.b, Mathf.Max(alpha, pixels[idx].a));
            }
        }
    }

    // 스캔라인 폴리곤 채우기
    private static void FillPolygon(Color[] pixels, int res, List<Vector2> pts, Color fill)
    {
        if (pts.Count < 3) return;

        int minY = Mathf.Max(0, Mathf.FloorToInt(MinComponent(pts, p => p.y)));
        int maxY = Mathf.Min(res - 1, Mathf.CeilToInt(MaxComponent(pts, p => p.y)));

        for (int y = minY; y <= maxY; y++)
        {
            float scanY = y + 0.5f;
            var xs = new List<float>();

            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % pts.Count];
                if ((a.y <= scanY && b.y > scanY) || (b.y <= scanY && a.y > scanY))
                {
                    float t = (scanY - a.y) / (b.y - a.y);
                    xs.Add(a.x + t * (b.x - a.x));
                }
            }

            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                int xStart = Mathf.Max(0, Mathf.CeilToInt(xs[i]));
                int xEnd   = Mathf.Min(res - 1, Mathf.FloorToInt(xs[i + 1]));
                for (int x = xStart; x <= xEnd; x++)
                {
                    int idx = y * res + x;
                    Color dst = pixels[idx];
                    float srcA = fill.a;
                    float outA = srcA + dst.a * (1f - srcA);
                    pixels[idx] = new Color(
                        (fill.r * srcA + dst.r * dst.a * (1f - srcA)) / outA,
                        (fill.g * srcA + dst.g * dst.a * (1f - srcA)) / outA,
                        (fill.b * srcA + dst.b * dst.a * (1f - srcA)) / outA,
                        outA
                    );
                }
            }
        }
    }

    private static float MinComponent(List<Vector2> pts, System.Func<Vector2, float> sel)
    {
        float m = float.MaxValue;
        foreach (Vector2 p in pts) m = Mathf.Min(m, sel(p));
        return m;
    }

    private static float MaxComponent(List<Vector2> pts, System.Func<Vector2, float> sel)
    {
        float m = float.MinValue;
        foreach (Vector2 p in pts) m = Mathf.Max(m, sel(p));
        return m;
    }
}
