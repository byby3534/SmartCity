using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MinimapManager 역할
/// ① 미니맵 생성, ② 구 클릭 , ③ cmap 갱신, ④ 사용자 상호작용(클릭/툴팁)
/// </summary>

public class MinimapManager : MonoBehaviour
{
    // 구들의 부모 UI
    [Header("UI")]
    [SerializeField] private RectTransform districtRoot;

    // 구 경계 geojson 파일
    [Header("GeoJSON")]
    [SerializeField] private string fileName = "seoul_district.geojson";

    // minimap 스타일 설정
    [Header("Map Style")]
    [SerializeField] private Color districtColor = new Color(0.75f, 0.75f, 0.75f, 0.5f);
    [SerializeField] private Color selectedOutlineColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color nonSelectedOutlineColor = Color.white;
    [SerializeField] private float outlineWidth = 1.5f;

    // 구 이름 툴팁 UI
    [Header("Tooltip")]
    [SerializeField] private RectTransform tooltipRoot;
    [SerializeField] private TextMeshProUGUI tooltipText;


    // 클릭 구 이름
    [Header("SelectedGuName")]
    [SerializeField] private TMP_Text districtLabelText;
    [SerializeField] private TMP_Text SelectedGuName;

    [SerializeField] private BlackoutSimulationController simulationController;

    // 초기 선택 구
    [Header("initialDistrict")]
    [SerializeField] private DistrictType initialDistrict = DistrictType.JONGNO;

    private const string LabelClickedDistrict = "선택한 구역";
    private const string LabelSimDistrict = "순환 단전 구역";


    // 구 이름, 구 폴리곤 딕셔너리
    private Dictionary<DistrictType, List<MinimapPolygon>> districtPolygonMap =
        new Dictionary<DistrictType, List<MinimapPolygon>>();

    // 구 이름, 구 아웃라인 딕셔너리
    private Dictionary<DistrictType, List<MinimapOutline>> districtOutlineMap =
        new Dictionary<DistrictType, List<MinimapOutline>>();

    // 구 이름, 최대최소 좌표 딕셔너리
    private Dictionary<DistrictType, Vector4> districtPolygonLonLatMap = new Dictionary<DistrictType, Vector4>();

    // 현재 선택된 구 (클릭)
    private DistrictType selectedDistrictType;

    private DistrictType _simDistrict = DistrictType.None;
    private bool _isSimulationOn;
    private bool _userSelectedDuringSim;

    // 서울 전체 최소/최대 경위도
    private double minLon = double.MaxValue;
    private double maxLon = double.MinValue;
    private double minLat = double.MaxValue;
    private double maxLat = double.MinValue;

    // 미니맵 가장자리 여백
    private float padding = 15f;

    // Geojson의 모든 feature
    private JArray features;

    private MainCameraController mainCameraController;

    // 구 선택 이벤트
    public event Action<DistrictType> OnDistrictSelected;
    public event Action<DistrictType> OnDisplayedDistrictChanged;

    public DistrictType DisplayedDistrict { get; private set; }

    private void Awake()
    {
        SceneRefs.Resolve(ref mainCameraController);
        SceneRefs.Resolve(ref simulationController);
    }

    private void OnEnable()
    {
        if (simulationController == null) return;

        simulationController.OnBlackoutSimulationToggled += HandleSimulationToggled;
        simulationController.OnBlackoutDistrictChanged += HandleBlackoutDistrictChanged;
    }

    private void OnDisable()
    {
        if (simulationController == null) return;

        simulationController.OnBlackoutSimulationToggled -= HandleSimulationToggled;
        simulationController.OnBlackoutDistrictChanged -= HandleBlackoutDistrictChanged;
    }

    private void Start()
    {
        if (!SceneRefs.RequireAll(this,
        (districtRoot, nameof(districtRoot)),
        (mainCameraController, nameof(mainCameraController))
        ))
        {
            return;
        }

        StartCoroutine(InitializeMinimap());
    }

    private IEnumerator InitializeMinimap()
    {
        yield return LoadGeoJson();
        CreateDistricts();

        // cmap/blackout 담당 Controller에게 미니맵 생성 완료 알림
        GetComponent<MinimapColorController>()?.SetReady();

        SetupTooltip();

        selectedDistrictType = initialDistrict;
        UpdateSelectedDistrictDisplay();
    }

    // 블랙아웃 시뮬레이션 토글 이벤트 처리
    private void HandleSimulationToggled(bool isOn)
    {
        _isSimulationOn = isOn;
        _userSelectedDuringSim = false; // 사용자 선택 초기화

        if (isOn)
        {
            // 시뮬레이션 시작 시 선택 구 초기화 -> 아웃라인 흰색으로
            ResetSelectedDistrictOutline();
        }
        else
        {
            // 시뮬레이션 종료 시, 시뮬레이션 중인 구 그대로 표시
            if (_simDistrict != DistrictType.None)
            {
                selectedDistrictType = _simDistrict;
            }

            // UI 갱신
            UpdateSelectedDistrictDisplay();
        }
    }

    // 시뮬레이션이 다음 구로 이동할때마다 호출
    private void HandleBlackoutDistrictChanged(DistrictType districtType)
    {
        _simDistrict = districtType;

        if (_isSimulationOn && !_userSelectedDuringSim)
            UpdateSelectedDistrictDisplay();
    }

    // 현재 화면에 어떤 구 표시할지 결정
    private void UpdateSelectedDistrictDisplay()
    {
        // 시뮬 중이고 사용자 선택 없음
        bool showSimDistrict = _isSimulationOn && !_userSelectedDuringSim;
        DistrictType displayDistrict = showSimDistrict ? _simDistrict : selectedDistrictType;

        if (districtLabelText != null)
            districtLabelText.text = showSimDistrict ? LabelSimDistrict : LabelClickedDistrict;

        if (SelectedGuName != null)
            SelectedGuName.text = DataConverter.GetDistrictName(displayDistrict);

        if (displayDistrict == DisplayedDistrict)
            return;

        DisplayedDistrict = displayDistrict;
        OnDisplayedDistrictChanged?.Invoke(DisplayedDistrict);
    }

    private void ResetSelectedDistrictOutline()
    {
        if (selectedDistrictType == DistrictType.None) return;

        if (!districtOutlineMap.TryGetValue(selectedDistrictType, out List<MinimapOutline> previousOutlines))
            return;

        foreach (MinimapOutline outline in previousOutlines)
        {
            if (outline == null) continue;

            outline.lineWidth = outlineWidth;
            outline.SetOutlineColor(nonSelectedOutlineColor);
        }
    }

    // Tooltip 설정
    private void SetupTooltip()
    {
        if (tooltipRoot == null) return;

        tooltipRoot.gameObject.SetActive(false);

        CanvasGroup cg = tooltipRoot.GetComponent<CanvasGroup>();
        if (cg == null)
        {
            cg = tooltipRoot.gameObject.AddComponent<CanvasGroup>();
        }
        // 마우스 클릭 이벤트 방지
        cg.blocksRaycasts = false;
        cg.interactable = false;
    }

    // geojson 로드 (WebGL 포함 전 플랫폼: UnityWebRequest 기반)
    private IEnumerator LoadGeoJson()
    {
        string json = null;
        yield return StreamingAssetsLoader.LoadText(fileName, text => json = text);

        if (string.IsNullOrEmpty(json))
        {
            Debug.LogError("[MinimapManager] GeoJSON 로드 실패: " + fileName);
            yield break;
        }

        // Json 파싱
        JObject geoJson = JObject.Parse(json);
        features = (JArray)geoJson["features"];

        // 모든 Polygon 검사
        foreach (JObject feature in features)
        {
            JToken geometry = feature["geometry"];
            string type = geometry["type"]?.ToString();

            // 폴리곤의 좌표 최대/최소 계산
            if (type == "Polygon")
            {
                ScanPolygonBounds((JArray)geometry["coordinates"]);
            }
             else
            {
                Debug.LogWarning("[MinimapManager] 알 수 없는 지오메트리 타입: " + type);
                yield break;
            }
        }
    }

    // 폴리곤의 최대/최소 좌표 계산
    // : 실제 좌표 -> UI 좌표 변환시 사용
    private void ScanPolygonBounds(JArray polygon)
    {
        JArray outerRing = (JArray)polygon[0];

        foreach (JArray coord in outerRing)
        {
            double lon = coord[0].Value<double>();
            double lat = coord[1].Value<double>();

            minLon = Math.Min(minLon, lon);
            maxLon = Math.Max(maxLon, lon);
            minLat = Math.Min(minLat, lat);
            maxLat = Math.Max(maxLat, lat);
        }
    }

    // 구 생성
    private void CreateDistricts()
    {
        if (features == null) return;

        foreach (JObject feature in features)
        {
            string districtName =
                feature["properties"]?["SIGUNGU_NM"]?.ToString() ?? "Unknown";
            DistrictType districtType = DataConverter.GetDistrictType(districtName); // 구 이름 -> DistrictType 변환

            JToken props = feature["properties"];

            // centroid 좌표 존재 여부 확인
            bool hasRepPoint =
                props?["rep_lon"] != null &&
                props?["rep_lat"] != null;

            // centroid 좌표 가져오기
            if (hasRepPoint && mainCameraController != null)
            {
                double repLon = props["rep_lon"].Value<double>();
                double repLat = props["rep_lat"].Value<double>();

                // 카메라 이동 좌표 저장: mainCamera에 구별 좌표 등록
                mainCameraController.RegisterDistrictPosition(
                    districtType,
                    repLon,
                    repLat
                );
            }

            // 폴리곤 생성
            JToken geometry = feature["geometry"];
            string type = geometry["type"]?.ToString();

            double minLon = double.MaxValue;
            double maxLon = double.MinValue;
            double minLat = double.MaxValue;
            double maxLat = double.MinValue;

            if (type == "Polygon")
            {
                ScanDistrictBounds(
                    (JArray)geometry["coordinates"],
                    ref minLon,
                    ref maxLon,
                    ref minLat,
                    ref maxLat
                );
                CreatePolygonUI(districtType, (JArray)geometry["coordinates"]);
            }
            else
            {
                Debug.LogWarning("[MinimapManager] 알 수 없는 지오메트리 타입: " + type);
                return;
            }

            districtPolygonLonLatMap[districtType] =
            new Vector4(
                (float)minLon,
                (float)maxLon,
                (float)minLat,
                (float)maxLat
                );
        }

        // 초기 카메라 좌표 설정
        if (mainCameraController != null)
        {
            mainCameraController.MoveToDistrict(initialDistrict);
        }
    }
    
    // 구 폴리곤의 최대/최소 좌표 계산
    private void ScanDistrictBounds(
    JArray polygon,
    ref double minLon,
    ref double maxLon,
    ref double minLat,
    ref double maxLat)
    {
        JArray outerRing = (JArray)polygon[0];

        foreach (JArray coord in outerRing)
        {
            double lon = coord[0].Value<double>();
            double lat = coord[1].Value<double>();

            minLon = Math.Min(minLon, lon);
            maxLon = Math.Max(maxLon, lon);
            minLat = Math.Min(minLat, lat);
            maxLat = Math.Max(maxLat, lat);
        }
    }

    // MainCameraController에서 가져오기 위한 함수
    public bool TryGetDistrictBounds(
        DistrictType districtType,
        out Vector4 bounds)
    {
        return districtPolygonLonLatMap.TryGetValue(districtType, out bounds);
    }

    // 구 UI 범위에 맞게 구 그리기
    private void CreatePolygonUI(DistrictType districtType, JArray polygon)
    {
        JArray outerRing = (JArray)polygon[0];

        // 각 구를 나타낼 게임 오브젝트 생성
        GameObject obj = new GameObject("UI_District_" + DataConverter.GetDistrictName(districtType));

        // 각 구를 districtRoot에 자식으로 변환
        obj.transform.SetParent(districtRoot, false);

        // districtRoot의 RectTransform 컴포넌트 가져오기
        RectTransform parentRect = districtRoot.GetComponent<RectTransform>();

        // 각 구에 RectTransform 컴포넌트 추가
        RectTransform rt = obj.AddComponent<RectTransform>();

        // UI 위치 설정
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = parentRect.rect.size;

        // obj 화면에 표시
        obj.AddComponent<CanvasRenderer>();

        // Graphic 상속받기
        MinimapPolygon polygonGraphic = obj.AddComponent<MinimapPolygon>();
        polygonGraphic.color = districtColor;
        polygonGraphic.districtType = districtType;
        polygonGraphic.minimapManager = this;

        // ui 좌표 리스트
        List<Vector2> uiPoints = new List<Vector2>();

        // 실제 위경도 -> UI 좌표 변환
        foreach (JArray coord in outerRing)
        {
            double lon = coord[0].Value<double>();
            double lat = coord[1].Value<double>();

            uiPoints.Add(LonLatToUI(lon, lat));
        }

        // 마지막 좌표 제거
        if (
            // 첫번째 점 & 마지막 점이 같은지 확인
            uiPoints.Count > 1 &&
            Vector2.Distance(uiPoints[0], uiPoints[uiPoints.Count - 1]) < 0.01f
        )
        {
            // 마지막 중복 점 제거
            uiPoints.RemoveAt(uiPoints.Count - 1);
        }

        // polygonGraphic에 좌표 전달
        polygonGraphic.points = uiPoints;

        // 바뀐 포인트로 다시 그리기 요청
        // : 내부적으로 OnPopulateMesh() 호출 -> 새로운 mesh 생성 및 화면 갱신
        polygonGraphic.SetVerticesDirty();

        // districtPolygonMap에 폴리곤 list 생성
        // : 왜 list? -> 구 하나에 여러 폴리곤 있을 수 있음(ex: 섬)
        if (!districtPolygonMap.ContainsKey(districtType))
        {
            districtPolygonMap[districtType] = new List<MinimapPolygon>();
        }

        // 폴리곤 추가
        districtPolygonMap[districtType].Add(polygonGraphic);

        CreateOutline(districtType, obj.transform, uiPoints, polygonGraphic);
    }

    private void CreateOutline(
        DistrictType districtType,
        Transform parent,
        List<Vector2> uiPoints,
        MinimapPolygon polygonGraphic
    )
    {
       // 구(폴리곤)의 아웃라인 생성
        // 새 outline object 생성
        GameObject outlineObj = new GameObject("Outline_" + DataConverter.GetDistrictName(districtType));

        // obj(폴리곤)의 자식으로 추가
        // outlineObj.transform.SetParent(obj.transform, false);
        outlineObj.transform.SetParent(parent, false);

        // outline에 RectTransform 컴포넌트 추가 및 크기 설정
        RectTransform outlineRt = outlineObj.AddComponent<RectTransform>();

        // 부모 폴리곤과 동일한 크기 갖도록 설정
        outlineRt.anchorMin = Vector2.zero;
        outlineRt.anchorMax = Vector2.one;
        outlineRt.offsetMin = Vector2.zero;
        outlineRt.offsetMax = Vector2.zero;

        // CanvasRenderer 추가
        // : outline을 canvas에 그릴 수 있도록 
        outlineObj.AddComponent<CanvasRenderer>();

        // MinimapOutline 추가
        // : 외곽선을 실제로 그리는 컴포넌트
        MinimapOutline outline = outlineObj.AddComponent<MinimapOutline>();
        outline.points = uiPoints; // 폴리곤과 동일한 좌표
        outline.color = nonSelectedOutlineColor;
        outline.lineWidth = outlineWidth;
        outline.raycastTarget = false; // 외곽선이 마우스 이벤트 받지 않도록
        outline.SetVerticesDirty();

        // 폴리곤이 자신의 outline 참조하도록 연결
        polygonGraphic.outline = outline;

        // outline 딕셔너리 추가
        // : 나중에 선택된 구의 모든 외곽선을 불러와 색상 변경 가능하도록
        if (!districtOutlineMap.ContainsKey(districtType))
        {
            districtOutlineMap[districtType] = new List<MinimapOutline>();
        }

        districtOutlineMap[districtType].Add(outline); 
    }

    // 좌표 UI 범위에 맞게 재설정
    private Vector2 LonLatToUI(double lon, double lat)
    {
        float width = districtRoot.rect.width - padding * 2f;
        float height = districtRoot.rect.height - padding * 2f;

        float lonRange = (float)(maxLon - minLon);
        float latRange = (float)(maxLat - minLat);

        float scaleX = width / lonRange;
        float scaleY = height / latRange;
        float scale = Mathf.Min(scaleX, scaleY);

        float mapWidth = lonRange * scale;
        float mapHeight = latRange * scale;

        float x = (float)((lon - minLon) * scale);
        float y = (float)((lat - minLat) * scale);

        x -= mapWidth / 2f;
        y -= mapHeight / 2f;

        return new Vector2(x, y);
    }

    // 구 클릭 시 선택 상태 처리
    public void SelectDistrict(MinimapPolygon polygon)
    {
        if (polygon == null) return;

        // 시뮬레이션 중일 때 구 클릭 무시
        if (_isSimulationOn)
        {
            Debug.Log("[MinimapManager] 시뮬레이션 중이므로 구 클릭이 비활성화되어 있습니다.");
            return;
        }

        // 구 전환 카메라 이동 중 클릭 무시
        if (mainCameraController != null && mainCameraController.IsFlying)
            return;

        // 현재 구 상태 변경 전 이전 선택 구 상태 변경
        // : 이전 선택된 구 아웃라인 -> 원래 색으로
        ResetSelectedDistrictOutline();

        // 현재 클릭 구로 선택 구 이름 변경
        selectedDistrictType = polygon.districtType;

        // 새로 선택된 구 outline 가져오기
        if (districtOutlineMap.TryGetValue(selectedDistrictType, out List<MinimapOutline> selectedOutlines))
        {
            foreach (MinimapOutline outline in selectedOutlines)
            {
                if (outline != null)
                {
                    outline.SetOutlineColor(selectedOutlineColor);

                    // outline의 부모 폴리곤 맨 뒤로 -> 가장 위에 그려짐
                    outline.transform.parent.SetAsLastSibling();

                    // outline을 폴리곤 맨 뒤로
                    outline.transform.SetAsLastSibling();
                }
            }
        }

        Debug.Log("[MinimapManager] 클릭한 구: " + DataConverter.GetDistrictName(polygon.districtType));

        // if (_isSimulationOn)
        //     _userSelectedDuringSim = true;

        OnDistrictSelected?.Invoke(polygon.districtType);
        UpdateSelectedDistrictDisplay();
    }

    // 구 색상 설정
    public void SetDistrictColor(DistrictType districtType, Color targetColor)
    {
        // 색 변경할 구의 폴리곤 가져오기
        if (!districtPolygonMap.TryGetValue(districtType, out List<MinimapPolygon> polygons))
            return;

        foreach (MinimapPolygon polygon in polygons)
        {
            if (polygon == null) continue;

            // 폴리곤 색상 변경
            polygon.color = targetColor;
            polygon.SetVerticesDirty();
        }
    }

    // 구 이름 Tooltip 함수
    public void ShowDistrictTooltip(DistrictType districtType, Vector2 screenPosition)
    {
        if (tooltipRoot == null || tooltipText == null) return;

        tooltipText.text = DataConverter.GetDistrictName(districtType);
        tooltipRoot.gameObject.SetActive(true);

        // 마우스 위치로 설정
        tooltipRoot.position = screenPosition;
    }

    // 툴팁 숨기기
    public void HideDistrictTooltip()
    {
        if (tooltipRoot == null) return;

        tooltipRoot.gameObject.SetActive(false);
    }

    // 마우스에 따라 툴팁 이동
    public void MoveDistrictTooltip(Vector2 screenPosition)
    {
        if (tooltipRoot == null) return;

        // 툴팁 보이는 상태인지 확인
        if (!tooltipRoot.gameObject.activeSelf) return;

        // 현재 마우스 위치로 변경
        tooltipRoot.position = screenPosition;
    }
}