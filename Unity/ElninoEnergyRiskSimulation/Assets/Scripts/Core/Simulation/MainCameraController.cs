using System.Collections.Generic;
using CesiumForUnity;
using TMPro;
using Unity.Mathematics;
using UnityEngine;

public class MainCameraController : MonoBehaviour
{
    [Header("Main Camera")]
    [SerializeField] private Camera mainCamera;

    private bool _isSimulationOn;

    [Header("SimulationController")]
    [SerializeField] private BlackoutSimulationController simulationController;

    [SerializeField] private MinimapManager minimapManager;

    // 카메라가 화면을 비출때 방향 조정
    private Vector3 lookDownRotation = new Vector3(90f, 0f, 0f);

    private CesiumGlobeAnchor cameraAnchor;
    private CesiumCameraController cesiumController;
    private CesiumFlyToController flyToController;
    private float initialHeight = 4000f; // 초기 카메라 높이 (미터 단위)

    // 카메라 이동 중인지 여부
    private bool isFlying = false;

    // 카메라 이동 제한 범위 있는지
    private bool hasCurrentBounds = false;

    // 카메라 이동 속도
    private const float FlyTime = 1f;

    // 카메라 이동 제한 범위 (xMin, xMax, yMin, yMax)
    private Vector4 currentBounds;

    // 구 이름/구 센터 좌표 저장
    // 좌표는 MinimapManager에서 가져옴
    private Dictionary<DistrictType, double2> districtLonLatMap =
        new Dictionary<DistrictType, double2>();

    private void Awake()
    {
        // 메인 카메라 설정 안되어 있으면 자동으로 메인카메라 찾아서 넣음
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        // 메인 카메라에 CesiumGlobeAnchor가 없으면 경고
        if (mainCamera != null)
        {
            // CesiumGlobeAnchor 컴포넌트 가져오기
            cameraAnchor = mainCamera.GetComponent<CesiumGlobeAnchor>(); // 카메라 위치 고정
            cesiumController = mainCamera.GetComponent<CesiumCameraController>(); // 카메라 이동 제어
            flyToController = mainCamera.GetComponent<CesiumFlyToController>(); // 부드럽게 이동

            if (cameraAnchor != null && cesiumController != null && flyToController != null)
            {
                // FlyTo 이동 시간 설정
                flyToController.flyToDuration = 1f;

                // 초기 위치를 종로구로 설정
                MoveToDistrict(DistrictType.JONGNO);

            }
            else
            {
                Debug.LogError("[MainCameraController] MainCamera에 CesiumGlobeAnchor와 CesiumCameraController가 없습니다.");
            }
        }
    }

    // 구 클릭 이벤트 구독
    // 클릭시 해당 구로 카메라 이동
    private void OnEnable()
    {
        minimapManager.OnDistrictSelected += MoveToClickedDistrict;
        simulationController.OnBlackoutDistrictChanged += MoveToBlackoutDistrict;
        simulationController.OnBlackoutSimulationToggled += HandleSimulationToggled;
    }

    private void OnDisable()
    {
        minimapManager.OnDistrictSelected -= MoveToClickedDistrict;
        simulationController.OnBlackoutDistrictChanged -= MoveToBlackoutDistrict;
        simulationController.OnBlackoutSimulationToggled -= HandleSimulationToggled;
    }

    private void LateUpdate()
    {
        if (cameraAnchor == null) return;

        mainCamera.transform.rotation = Quaternion.Euler(lookDownRotation);

        if (!hasCurrentBounds) return;
        if (isFlying) return;

        double3 pos = cameraAnchor.longitudeLatitudeHeight;

        pos.x = math.clamp(
            pos.x,
            currentBounds.x,
            currentBounds.y);

        pos.y = math.clamp(
            pos.y,
            currentBounds.z,
            currentBounds.w);

        cameraAnchor.longitudeLatitudeHeight = pos;
    }

    // 회전 비활성화
    private void FixedUpdate()
    {
        if (cesiumController == null) return;

        cesiumController.enableRotation = false;
    }

    private void HandleSimulationToggled(bool isOn)
    {
        _isSimulationOn = isOn;
    }
    public void RegisterDistrictPosition(DistrictType districtType, double lon, double lat)
    {
        if (string.IsNullOrEmpty(DataConverter.GetDistrictName(districtType))) return;

        if (!districtLonLatMap.ContainsKey(districtType))
        {
            districtLonLatMap.Add(districtType, new double2(lon, lat));
        }
    }

    // 모드 1. 시뮬레이션 ❌: 구 클릭 가능 -> 클릭된 구로 카메라 이동
    public void MoveToClickedDistrict(DistrictType districtType)
    {
        // 토글 ON이면 구 클릭으로 카메라 이동 금지
        if (_isSimulationOn)
        {
            Debug.Log("[MainCameraController] 시뮬레이션 ON 상태이므로 구 클릭 카메라 이동 비활성화");
            return;
        }

        MoveToDistrict(districtType);
    }

    // 모드 2. 시뮬레이션 ⭕️: 구 클릭 이동 안됨 / 정전 순회 중인 구로 카메라 이동
    public void MoveToBlackoutDistrict(DistrictType districtType)
    {
        MoveToDistrict(districtType);
    }

    public void MoveToDistrict(DistrictType districtType)
    {
        if (cameraAnchor == null)
        {
            Debug.LogWarning("[MainCameraController] 카메라 Anchor가 없습니다.");
            return;
        }

        if (!districtLonLatMap.TryGetValue(districtType, out double2 lonLat))
        {
            Debug.LogWarning("[MainCameraController] 이동 좌표를 찾을 수 없습니다: " + DataConverter.GetDistrictName(districtType));
            return;
        }

        if (minimapManager.TryGetDistrictBounds(districtType, out currentBounds))
        {
            hasCurrentBounds = true;
        }
        else
        {
            hasCurrentBounds = false;
        }

        CancelInvoke(nameof(EndFly));

        if (flyToController != null)
        {
            isFlying = true;

            flyToController.FlyToLocationLongitudeLatitudeHeight(
                new double3(lonLat.x, lonLat.y, initialHeight),
                0f,
                90f,
                false
            );

            Invoke(nameof(EndFly), FlyTime);
        }
        else
        {
            cameraAnchor.longitudeLatitudeHeight =
                new double3(lonLat.x, lonLat.y, initialHeight);

            mainCamera.transform.rotation = Quaternion.Euler(lookDownRotation);
        }
    }

    private void EndFly()
    {
        isFlying = false;
    }
}