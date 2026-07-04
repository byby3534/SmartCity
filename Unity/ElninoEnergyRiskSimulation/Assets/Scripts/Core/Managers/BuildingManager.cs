using CesiumForUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NativeBuildingData
{
    public double lon;
    public double lat;
    public float height;
    public float terrainAltitude;
    public float reductionValue;
    public int id;
    public int districtId;
    public int districtType;
    public int buildingType;
    public int isBlackout;
    public int polygonVertexCount;
    public int polygonStartIndex;
}

/// <summary>
/// 셰이더 전용 경량 구조체 (C++ BuildingRenderData와 동일 레이아웃).
/// GPU에는 이 8바이트만 전달된다.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BuildingRenderData
{
    public float reductionValue;   // 수요감축 필요도 (0~1)
    public int isBlackout;       // 정전 여부 (0 or 1)
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeVertex
{
    public float px, py, pz;
    public float nx, ny, nz;
    public float buildingId;
}

public class BuildingManager : MonoBehaviour
{
    public Cesium3DTileset terrainTileset;
    public CesiumGeoreference cesiumGeoreference;
    public Material buildingMaterial;
    // private ComputeBuffer renderBuffer;
    private Texture2D renderTexture;
    private Color[] _pixelUploadBuffer;
    private readonly List<int> _batchChangedIndices = new();

    private BuildingRenderData[] cachedRenderData;
    private bool bufferDirty;
    private bool _apiLoaded;

    private Dictionary<int, (int start, int count)> districtRanges = new();

    public event Action<DistrictObject> OnDistrictObjectCreated;

    public BuildingRenderData[] CachedRenderData => cachedRenderData;

    public void MarkBufferDirty() => bufferDirty = true;

    private Dictionary<int, int[]> sortedDistrictIndices = new();
    private Dictionary<int, GameObject> districtRoots = new();

    private Coroutine blackoutCoroutine;

    private bool _isSimulationActive;
    private DistrictType _selectedDistrict = DistrictType.None;
    private DistrictType _pendingBlackoutDistrict = DistrictType.None;

#if UNITY_WEBGL && !UNITY_EDITOR
    private const bool LazyDistrictMeshes = true;
#else
    private const bool LazyDistrictMeshes = false;
#endif

    private readonly Dictionary<int, Coroutine> _districtLoadCoroutines = new();

    [Header("매니저 연결")]
    [SerializeField] private DistrictManager districtManager;
    [SerializeField] private BlackoutSimulationController simulationController;
    [SerializeField] private MinimapManager minimapManager;
    [SerializeField] private MainCameraController mainCameraController;

    [Header("정전 연출 설정")]
    [SerializeField] private int buildingsPerBatch = 100;
    [SerializeField] private float secondsBetweenBatch = 0.05f;
    [SerializeField] private float blackoutHoldDuration = 3.0f;
    [SerializeField] private float secondsBetweenRestoreBatch = 0.03f;

#if UNITY_WEBGL && !UNITY_EDITOR
    private const string SeoulBuildingProcessor = "__Internal";
#else
    private const string SeoulBuildingProcessor = "SeoulBuildingProcessor";
#endif

    // --- C++ DLL 함수 연결 ---
    [DllImport(SeoulBuildingProcessor)]
    private static extern void LoadDistrictData(System.IntPtr dataPointer, int byteLength, int districtId);

    [DllImport(SeoulBuildingProcessor)]
    private static extern void LoadPolygonData(IntPtr dataPointer, int elementCount);

    [DllImport(SeoulBuildingProcessor)]
    private static extern System.IntPtr GetBuildingBufferPointer();

    [DllImport(SeoulBuildingProcessor)]
    private static extern int GetBuildingBufferCount();

    [DllImport(SeoulBuildingProcessor)]
    private static extern void BuildDistrictMesh(int districtId, System.IntPtr terrainHeights, int terrainArrayLength, double centerLon, double centerLat);

    [DllImport(SeoulBuildingProcessor)]
    private static extern System.IntPtr GetChunkVertices();

    [DllImport(SeoulBuildingProcessor)]
    private static extern int GetChunkVertexCount();

    [DllImport(SeoulBuildingProcessor)]
    private static extern System.IntPtr GetChunkIndices();

    [DllImport(SeoulBuildingProcessor)]
    private static extern int GetChunkIndexCount();

    [DllImport(SeoulBuildingProcessor)]
    private static extern int GetDistrictBuildingCount(int districtId);

    [DllImport(SeoulBuildingProcessor)]
    private static extern void GetBuildingPositions(int districtId, [In, Out] double[] lons, [In, Out] double[] lats);

    [DllImport(SeoulBuildingProcessor)]
    private static extern void ClearAllNativeData();

    // ── 렌더링 버퍼 전용 DLL 함수 ──
    [DllImport(SeoulBuildingProcessor)]
    private static extern void BuildRenderingBuffer();

    [DllImport(SeoulBuildingProcessor)]
    private static extern void SetReductionValues([In] float[] values, int count);

    [DllImport(SeoulBuildingProcessor)]
    private static extern IntPtr GetRenderingBufferPointer();

    [DllImport(SeoulBuildingProcessor)]
    private static extern int GetRenderingBufferCount();

    [DllImport(SeoulBuildingProcessor)]
    private static extern bool GetDistrictRange(int districtId, out int startIndex, out int count);

    private void Awake()
    {
        // 공유 .mat 에셋에 SetFloat/SetTexture 하면 Play 종료 후 파일이 더티해진다.
        if (buildingMaterial != null)
            buildingMaterial = new Material(buildingMaterial);

#if UNITY_WEBGL && !UNITY_EDITOR
        buildingsPerBatch = Mathf.Max(buildingsPerBatch, 400);
        secondsBetweenBatch = Mathf.Max(secondsBetweenBatch, 0.08f);
        secondsBetweenRestoreBatch = Mathf.Max(secondsBetweenRestoreBatch, 0.06f);
#endif

        SceneRefs.Resolve(ref districtManager);
        SceneRefs.Resolve(ref simulationController);
        SceneRefs.Resolve(ref minimapManager);
        SceneRefs.Resolve(ref mainCameraController);
    }

    private void Start()
    {
        ClearAllNativeData();

        Debug.Log("[CityManager] Start() - 구역별 건물 데이터 로드 및 메쉬 생성 시작...");
        // PloygonData 고속 로드
        LoadGlobalPolygonBinaryFast();

        // 서울시 25개 구별 건물 데이터(.bytes) 고속 로드
        foreach (DistrictType district in Enum.GetValues(typeof(DistrictType)))
        {
            if (district == DistrictType.None) continue;
            LoadDistrictBinaryFast((int)district);
        }

        // 모든 건물 데이터 로드 후 렌더링 버퍼 구축
        BuildRenderingBuffer();
        InitializeRenderBuffer();
        BuildingDistrictIndexMap();

        StartCoroutine(InitializeDistrict());
    }

    private void OnEnable()
    {
        if (!SceneRefs.RequireAll(this,
            (districtManager, nameof(districtManager)),
            (simulationController, nameof(simulationController)),
            (minimapManager, nameof(minimapManager)),
            (mainCameraController, nameof(mainCameraController))
            ))
            return;

        simulationController.OnBlackoutSimulationToggled += HandleBlackoutSimulationStart;
        simulationController.OnBlackoutDistrictChanged += HandleDistrictBlackedOut;
        simulationController.OnActiveDistrictsChanged += HandleActiveDistrictsChanged;
        minimapManager.OnDistrictSelected += HandleDistrictSelected;
        mainCameraController.OnEndFlyEnded += HandleFlyEndToStart;
    }

    private void OnDisable()
    {
        if (!SceneRefs.RequireAll(this,
            (districtManager, nameof(districtManager)),
            (simulationController, nameof(simulationController)),
            (minimapManager, nameof(minimapManager)),
            (mainCameraController, nameof(mainCameraController))
            ))
            return;

        simulationController.OnBlackoutSimulationToggled -= HandleBlackoutSimulationStart;
        simulationController.OnBlackoutDistrictChanged -= HandleDistrictBlackedOut;
        simulationController.OnActiveDistrictsChanged -= HandleActiveDistrictsChanged;
        minimapManager.OnDistrictSelected -= HandleDistrictSelected;
        mainCameraController.OnEndFlyEnded -= HandleFlyEndToStart;
    }

    IEnumerator InitializeDistrict()
    {
        while (terrainTileset == null || !terrainTileset.enabled)
            yield return null;

        yield return new WaitForSeconds(2.0f);
        Debug.Log("[BuildingManager] Tileset 준비 완료. 구역별 메시 생성 시작...");

        if (LazyDistrictMeshes)
        {
            Debug.Log("[BuildingManager] WebGL lazy mode — 선택/시뮬 구만 메시 생성 (데이터는 25구 전체 로드됨)");
            yield return SpawnDistrictAsync((int)DistrictType.JONGNO);
            WebGLMemoryDiagnostics.LogSnapshot("lazy-default-district", this);
            Debug.Log("[BuildingManager] lazy mode 초기 구 메시 준비 완료.");
            yield break;
        }

        foreach (DistrictType district in Enum.GetValues(typeof(DistrictType)))
        {
            if (district == DistrictType.None) continue;
            yield return StartCoroutine(SpawnDistrictAsync((int)district));
        }

        Debug.Log("[BuildingManager] 모든 구역 메시 생성 완료.");
        WebGLMemoryDiagnostics.LogSnapshot("all-district-meshes-ready", this);
    }

    public string GetMemoryReportLine()
    {
        int buildingCount = cachedRenderData?.Length ?? 0;
        long texBytes = renderTexture != null
            ? (long)renderTexture.width * renderTexture.height * 4
            : 0;
        return $"buildings={buildingCount} districtRoots={districtRoots.Count} " +
               $"dataTex={TexWidth}x{_texHeight} estTexMB={texBytes / (1024f * 1024f):F2} " +
               $"lazyMeshes={LazyDistrictMeshes}";
    }

    private void RequestDistrictMesh(int districtId)
    {
        if (districtRoots.ContainsKey(districtId))
            return;

        if (_districtLoadCoroutines.TryGetValue(districtId, out Coroutine running) && running != null)
            return;

        _districtLoadCoroutines[districtId] = StartCoroutine(LoadDistrictMeshTracked(districtId));
    }

    private IEnumerator LoadDistrictMeshTracked(int districtId)
    {
        yield return SpawnDistrictAsync(districtId);
        _districtLoadCoroutines.Remove(districtId);
    }

    private IEnumerator EnsureDistrictMeshesReady(params DistrictType[] districts)
    {
        foreach (DistrictType district in districts)
        {
            if (district == DistrictType.None) continue;

            int districtId = (int)district;
            RequestDistrictMesh(districtId);

            while (!districtRoots.ContainsKey(districtId))
                yield return null;
        }
    }

    #region DataLoad
    private void LoadDistrictBinaryFast(int districtId)
    {
        TextAsset binFile = Resources.Load<TextAsset>($"Districts/{districtId}/District");
        if (binFile == null)
        {
            Debug.Log($"[CityManager] 구역 {districtId} 바이너리 파일을 Resources에서 찾을 수 없음.");
            return;
        }

        byte[] rawData = binFile.bytes;
        GCHandle handle = GCHandle.Alloc(rawData, GCHandleType.Pinned);

        try
        {
            LoadDistrictData(handle.AddrOfPinnedObject(), rawData.Length, districtId);
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
    }

    private void LoadGlobalPolygonBinaryFast()
    {
        TextAsset polyFile = Resources.Load<TextAsset>("Districts/PolygonData");
        if (polyFile == null) return;

        byte[] rawData = polyFile.bytes;
        GCHandle handle = GCHandle.Alloc(rawData, GCHandleType.Pinned);
        try
        {
            LoadPolygonData(handle.AddrOfPinnedObject(), rawData.Length / 4);
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
    }

    /// <summary>
    /// 전체 건물의 NativeBuildingData를 읽어온다 (구/건물유형 매핑용).
    /// 렌더링과는 무관하며, DistrictManager가 reductionValue 계산 시 참조.
    /// </summary>
    public NativeBuildingData[] GetFullBuildingData()
    {
        int count = GetBuildingBufferCount();
        if (count == 0) return Array.Empty<NativeBuildingData>();

        NativeBuildingData[] data = new NativeBuildingData[count];
        IntPtr ptr = GetBuildingBufferPointer();
        int stride = Marshal.SizeOf(typeof(NativeBuildingData));

        for (int i = 0; i < count; i++)
        {
            IntPtr itemPtr = new IntPtr(ptr.ToInt64() + (i * stride));
            data[i] = Marshal.PtrToStructure<NativeBuildingData>(itemPtr);
        }

        return data;
    }
    #endregion

    #region MeshSpawn

    private IEnumerator SpawnDistrictAsync(int districtId)
    {
        // 1. TerrainHeights 로드
        TextAsset heightFile = Resources.Load<TextAsset>($"Districts/{districtId}/TerrainHeights");
        if (heightFile == null)
        {
            Debug.LogError($"[BuildingManager] {districtId}: TerrainHeights 없음. 'Tools > Bake Terrain Heights' 를 먼저 실행하세요.");
            yield break;
        }

        float[] heights = new float[heightFile.bytes.Length / sizeof(float)];
        Buffer.BlockCopy(heightFile.bytes, 0, heights, 0, heightFile.bytes.Length);
        yield return null;

        // 2. C++ DLL로 fullMesh 생성
        Mesh fullMesh = BuildAndGetDistrictMesh(districtId, heights);
        if (fullMesh == null)
        {
            Debug.LogError($"[BuildingManager] {districtId}: 메시 빌드 실패.");
            yield break;
        }
        yield return null;

        // 3. GameObject 생성
        SpawnDistrictObject(districtId, fullMesh);
    }

    private void SpawnDistrictObject(int districtId, Mesh mesh)
    {
        GameObject districtRoot = new GameObject($"District_Chunk_{districtId}");
        if (cesiumGeoreference != null)
            districtRoot.transform.SetParent(cesiumGeoreference.transform, false);

        Vector2 centerCoord = DistrictCoordinates.GetCenter(districtId);
        CesiumGlobeAnchor anchor = districtRoot.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = new double3(centerCoord.x, centerCoord.y, 0);

        districtRoots[districtId] = districtRoot;

        DistrictObject districtObject = districtRoot.AddComponent<DistrictObject>();
        districtObject.districtId = districtId;
        districtObject.data = new DistrictData();
        OnDistrictObjectCreated?.Invoke(districtObject);

        districtRoot.AddComponent<MeshFilter>().mesh = mesh;
        MeshRenderer r = districtRoot.AddComponent<MeshRenderer>();
        r.sharedMaterial = buildingMaterial;
        r.renderingLayerMask = RenderingLayerMask.GetMask("BUILDING");
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        Debug.Log($"[BuildingManager] 구 {districtId} 메시 생성 — verts={mesh.vertexCount:N0} tris={mesh.triangles.Length / 3:N0}");

        if (districtId != (int)DistrictType.JONGNO)
        {
            districtRoot.SetActive(false);
        }
    }

    /// <summary>
    /// BakeTerrainHeightsWindow 및 타일 분할에서 사용하는 건물 위치 반환.
    /// </summary>
    public double3[] GetBuildingPositionsForBaking(int districtId)
    {
        int count = GetDistrictBuildingCount(districtId);
        if (count == 0) return Array.Empty<double3>();

        double[] lons = new double[count];
        double[] lats = new double[count];
        GetBuildingPositions(districtId, lons, lats);

        double3[] positions = new double3[count];
        for (int i = 0; i < count; i++)
            positions[i] = new double3(lons[i], lats[i], 0);

        return positions;
    }

    /// <summary>
    /// BakeTerrainHeightsWindow에서도 사용. 지형 높이를 받아 C++ 메시를 빌드하고 Unity Mesh로 반환한다.
    /// </summary>
    public Mesh BuildAndGetDistrictMesh(int districtId, float[] heights)
    {
        Vector2 centerCoord = DistrictCoordinates.GetCenter(districtId);
        GCHandle handle = GCHandle.Alloc(heights, GCHandleType.Pinned);
        try
        {
            BuildDistrictMesh(districtId, handle.AddrOfPinnedObject(), heights.Length, centerCoord.x, centerCoord.y);
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
        return ExtractChunkMesh();
    }

    private Mesh ExtractChunkMesh()
    {
        int vCount = GetChunkVertexCount();
        int iCount = GetChunkIndexCount();
        if (vCount == 0 || iCount == 0) return null;

        float[] rawVertices = new float[vCount * 7];
        Marshal.Copy(GetChunkVertices(), rawVertices, 0, vCount * 7);

        int[] indices = new int[iCount];
        Marshal.Copy(GetChunkIndices(), indices, 0, iCount);

        Vector3[] verts = new Vector3[vCount];
        Vector2[] uv2 = new Vector2[vCount];
        for (int i = 0; i < vCount; i++)
        {
            int o = i * 7;
            verts[i] = new Vector3(rawVertices[o], rawVertices[o + 1], rawVertices[o + 2]);
            uv2[i] = new Vector2(rawVertices[o + 6], 0);
        }

        Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.uv2 = uv2;
        mesh.triangles = indices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    #endregion

    #region RenderBuffer
    /// <summary>
    /// 렌더링 전용 ComputeBuffer 초기화.
    /// C++의 renderingBuffer로부터 데이터를 읽어 GPU에 업로드한다.
    /// </summary>
    private const int TexWidth = 16384;
    private int _texHeight;

#if UNITY_WEBGL && !UNITY_EDITOR
    private const TextureFormat BuildingTexFormat = TextureFormat.RGHalf;
#else
    private const TextureFormat BuildingTexFormat = TextureFormat.RGFloat;
#endif

    private void InitializeRenderBuffer()
    {
        int count = GetRenderingBufferCount();
        if (count == 0) return;

        SyncRenderCacheFromNative();

        _texHeight = Mathf.CeilToInt((float)count / TexWidth);
        renderTexture = new Texture2D(TexWidth, _texHeight, BuildingTexFormat, false);
        renderTexture.filterMode = FilterMode.Point;
        buildingMaterial.SetTexture("_BuildingDataTex", renderTexture);
        buildingMaterial.SetFloat("_BuildingDataTexWidth", TexWidth);
        buildingMaterial.SetFloat("_BuildingDataTexHeight", _texHeight);
        ResetApiLoadedState();

        UploadToTexture();

        Debug.Log($"[CityManager] 렌더링 버퍼 초기화 완료 ({count}개 건물, {TexWidth}x{_texHeight} 텍스처)");
        WebGLMemoryDiagnostics.LogSnapshot("render-buffer-init", this);
    }

    private void EnsurePixelBuffer(int total)
    {
        if (_pixelUploadBuffer == null || _pixelUploadBuffer.Length != total)
            _pixelUploadBuffer = new Color[total];
    }

    private static Color PackBuildingPixel(BuildingRenderData data)
    {
        return new Color(data.reductionValue, data.isBlackout, 0, 0);
    }

    private void UploadToTexture()
    {
        if (renderTexture == null || cachedRenderData == null) return;

        int total = TexWidth * _texHeight;
        EnsurePixelBuffer(total);

        for (int i = 0; i < cachedRenderData.Length; i++)
            _pixelUploadBuffer[i] = PackBuildingPixel(cachedRenderData[i]);

        for (int i = cachedRenderData.Length; i < total; i++)
            _pixelUploadBuffer[i] = Color.clear;

        renderTexture.SetPixels(_pixelUploadBuffer);
        renderTexture.Apply(false);
    }

    private void UploadIndicesToTexture(IReadOnlyList<int> indices)
    {
        if (renderTexture == null || cachedRenderData == null || indices == null) return;

        for (int k = 0; k < indices.Count; k++)
        {
            int i = indices[k];
            if ((uint)i >= (uint)cachedRenderData.Length) continue;

            int x = i % TexWidth;
            int y = i / TexWidth;
            renderTexture.SetPixel(x, y, PackBuildingPixel(cachedRenderData[i]));
        }

        renderTexture.Apply(false);
    }

    /// <summary>
    /// C++ renderingBuffer → C# 캐시 배열로 복사.
    /// </summary>
    private void SyncRenderCacheFromNative()
    {
        int count = GetRenderingBufferCount();
        IntPtr ptr = GetRenderingBufferPointer();

        if (cachedRenderData == null || cachedRenderData.Length != count)
            cachedRenderData = new BuildingRenderData[count];

        int stride = Marshal.SizeOf(typeof(BuildingRenderData));
        for (int i = 0; i < count; i++)
        {
            IntPtr itemPtr = new IntPtr(ptr.ToInt64() + (i * stride));
            cachedRenderData[i] = Marshal.PtrToStructure<BuildingRenderData>(itemPtr);
        }
    }

    public void ResetApiLoadedState()
    {
        _apiLoaded = false;
        if (buildingMaterial != null)
            buildingMaterial.SetFloat("_ApiLoaded", 0f);
    }

    public void MarkPredictDataLoaded()
    {
        if (_apiLoaded || buildingMaterial == null)
            return;

        _apiLoaded = true;
        buildingMaterial.SetFloat("_ApiLoaded", 1f);
    }

    public void FlushBufferToGPU()
    {
        if (!bufferDirty || cachedRenderData == null || renderTexture == null)
            return;

        UploadToTexture();
        bufferDirty = false;
    }

    private void FlushChangedIndicesToGPU(IReadOnlyList<int> indices)
    {
        if (indices == null || indices.Count == 0 || cachedRenderData == null || renderTexture == null)
            return;

        UploadIndicesToTexture(indices);
        bufferDirty = false;
    }
    #endregion

    #region BlackoutHandling
    // 프레임마다 GetFullBuildingData()를 호출하지 않도록, 구별 건물 인덱스 매핑을 초기화 시점에 한 번만 수행
    private void BuildingDistrictIndexMap()
    {
        foreach (DistrictType district in Enum.GetValues(typeof(DistrictType)))
        {
            if (district == DistrictType.None) continue;
            int districtId = (int)district;

            if (GetDistrictRange(districtId, out int start, out int count))
            {
                districtRanges[districtId] = (start, count);
                BuildSortedIndices(districtId, start, count);
            }
        }
    }

    private void BuildSortedIndices(int districtId, int start, int count)
    {
        int[] indices = new int[count];
        for (int i = 0; i < count; i++)
            indices[i] = start + i;

        // reductionValue 내림차순 정렬 (물리적 배열은 그대로, 인덱스 순서만 정렬)
        Array.Sort(indices, (a, b) =>
            cachedRenderData[b].reductionValue.CompareTo(cachedRenderData[a].reductionValue));

        sortedDistrictIndices[districtId] = indices;
    }

    /// <summary>
    /// 모든 구역의 정렬된 건물 인덱스를 reductionValue 기준으로 다시 계산한다.
    /// BuildSortedIndices()는 Start() 시점(아직 API의 실제 reductionValue가 도착하기 전,
    /// 즉 전부 0인 상태)에 한 번 호출되므로 그대로 두면 정전이 항상 배열 순서대로 발생한다.
    /// DistrictManager가 실제 reductionValue를 buffer에 반영(ApplyReductionScoresToBuffer)한
    /// 직후 이 메서드를 호출해 정렬을 갱신해야 reductionValue가 높은 건물부터 정확히 꺼진다.
    /// </summary>
    public void RebuildSortedIndices()
    {
        if (districtRanges.Count == 0) return;

        foreach (var kvp in districtRanges)
            BuildSortedIndices(kvp.Key, kvp.Value.start, kvp.Value.count);
    }

    public void RebuildSortedIndicesForDistrict(int districtId)
    {
        if (districtRanges.TryGetValue(districtId, out var range))
            BuildSortedIndices(districtId, range.start, range.count);
    }

    private void HandleActiveDistrictsChanged(DistrictType current, DistrictType next)
    {
        _selectedDistrict = current;
        StartCoroutine(ActivateSimulationDistricts(current, next));
    }

    private IEnumerator ActivateSimulationDistricts(DistrictType current, DistrictType next)
    {
        if (next != DistrictType.None)
            yield return EnsureDistrictMeshesReady(current, next);
        else
            yield return EnsureDistrictMeshesReady(current);

        ApplyDistrictVisibility(current, next);
        WebGLMemoryDiagnostics.LogSnapshot($"sim-districts-{current}", this);
    }

    private void ApplyDistrictVisibility(DistrictType current, DistrictType next)
    {
        foreach (var (id, root) in districtRoots)
        {
            bool isActive = id == (int)current ||
                            (next != DistrictType.None && id == (int)next);
            root.SetActive(isActive);
        }
    }

    private void HandleDistrictSelected(DistrictType districtType)
    {
        if (_isSimulationActive) return;

        _selectedDistrict = districtType;
        StartCoroutine(ActivateSelectedDistrict(districtType));
    }

    private IEnumerator ActivateSelectedDistrict(DistrictType districtType)
    {
        yield return EnsureDistrictMeshesReady(districtType);

        int selectedId = (int)districtType;
        foreach (var (id, root) in districtRoots)
            root.SetActive(id == selectedId);

        WebGLMemoryDiagnostics.LogSnapshot($"selected-{districtType}", this);
    }

    private void HandleBlackoutSimulationStart(bool isOn)
    {
        _isSimulationActive = isOn;

        if (!isOn)
        {
            ResetAllBlackoutStates();
            HandleDistrictSelected(_selectedDistrict);
        }
    }

    private void HandleDistrictBlackedOut(DistrictType districtType)
    {
        _pendingBlackoutDistrict = districtType;
    }

    private void HandleFlyEndToStart()
    {
        Debug.Log("[BuildingManager] 구역 정전 연출 시작: " + DataConverter.GetDistrictName(_pendingBlackoutDistrict));
        if (_pendingBlackoutDistrict == DistrictType.None) return;

        DistrictType districtType = _pendingBlackoutDistrict;
        _pendingBlackoutDistrict = DistrictType.None;

        int districtId = (int)districtType;
        if (!sortedDistrictIndices.TryGetValue(districtId, out var sortedIndices)) return;

        if (blackoutCoroutine != null) StopCoroutine(blackoutCoroutine);
        RebuildSortedIndicesForDistrict(districtId);
        WebGLMemoryDiagnostics.LogSnapshot($"blackout-start-{districtType}", this);
        blackoutCoroutine = StartCoroutine(BlackoutSequence(districtType, sortedIndices));
    }

    IEnumerator BlackoutSequence(DistrictType districtType, int[] sortedIndices)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        const int applyEveryNBatches = 3;
        int batchCounter = 0;
#endif
        // ── 1단계: 정전 ──
        for (int i = 0; i < sortedIndices.Length; i += buildingsPerBatch)
        {
            int end = Mathf.Min(i + buildingsPerBatch, sortedIndices.Length);
            _batchChangedIndices.Clear();

            for (int j = i; j < end; j++)
            {
                int idx = sortedIndices[j];
                if (cachedRenderData[idx].reductionValue <= 0f) continue;
                cachedRenderData[idx].isBlackout = 1;
                _batchChangedIndices.Add(idx);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            batchCounter++;
            if (_batchChangedIndices.Count > 0 &&
                (batchCounter % applyEveryNBatches == 0 || end >= sortedIndices.Length))
                FlushChangedIndicesToGPU(_batchChangedIndices);
#else
            FlushChangedIndicesToGPU(_batchChangedIndices);
#endif

            yield return new WaitForSeconds(secondsBetweenBatch);
        }

        FlushChangedIndicesToGPU(CollectBlackoutIndices(sortedIndices, 1));
        simulationController.NotifyDistrictBlackoutComplete(districtType);
        Debug.Log($"[BuildingManager] 구역 정전 연출 완료 — {blackoutHoldDuration}초 유지 후 복전");
        WebGLMemoryDiagnostics.LogSnapshot($"blackout-complete-{districtType}", this);

        // ── 2단계: 정전 유지 ──
        yield return new WaitForSeconds(blackoutHoldDuration);

        simulationController.NotifyDistrictRestoreStarted(districtType);

        // ── 3단계: 복전 (배치 단위로 순차 복원) ──
#if UNITY_WEBGL && !UNITY_EDITOR
        batchCounter = 0;
#endif
        for (int i = 0; i < sortedIndices.Length; i += buildingsPerBatch)
        {
            int end = Mathf.Min(i + buildingsPerBatch, sortedIndices.Length);
            _batchChangedIndices.Clear();

            for (int j = i; j < end; j++)
            {
                int idx = sortedIndices[j];
                if (cachedRenderData[idx].reductionValue <= 0f) continue;
                cachedRenderData[idx].isBlackout = 0;
                _batchChangedIndices.Add(idx);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            batchCounter++;
            if (_batchChangedIndices.Count > 0 &&
                (batchCounter % applyEveryNBatches == 0 || end >= sortedIndices.Length))
                FlushChangedIndicesToGPU(_batchChangedIndices);
#else
            FlushChangedIndicesToGPU(_batchChangedIndices);
#endif

            yield return new WaitForSeconds(secondsBetweenRestoreBatch);
        }

        FlushChangedIndicesToGPU(CollectBlackoutIndices(sortedIndices, 0));
        simulationController.NotifyDistrictRestoreComplete(districtType);
        Debug.Log("[BuildingManager] 구역 복전 완료 → 다음 구로 이동");
        WebGLMemoryDiagnostics.LogSnapshot($"restore-complete-{districtType}", this);
        blackoutCoroutine = null;
    }

    private List<int> CollectBlackoutIndices(int[] sortedIndices, int blackoutValue)
    {
        _batchChangedIndices.Clear();
        for (int i = 0; i < sortedIndices.Length; i++)
        {
            int idx = sortedIndices[i];
            if (cachedRenderData[idx].reductionValue <= 0f) continue;
            if (cachedRenderData[idx].isBlackout == blackoutValue)
                _batchChangedIndices.Add(idx);
        }
        return _batchChangedIndices;
    }

    private void ResetAllBlackoutStates()
    {
        // 진행 중인 정전 연출 코루틴이 있다면 중단
        if (blackoutCoroutine != null)
        {
            StopCoroutine(blackoutCoroutine);
            blackoutCoroutine = null;
        }

        if (cachedRenderData == null || cachedRenderData.Length == 0) return;

        for (int i = 0; i < cachedRenderData.Length; i++)
        {
            cachedRenderData[i].isBlackout = 0;
        }

        MarkBufferDirty();
        FlushBufferToGPU();

        Debug.Log("[BuildingManager] 모든 건물 정전 상태 초기화 완료");
    }
    #endregion

    void OnDestroy()
    {
        if (renderTexture != null)
        {
            Destroy(renderTexture);
        }
    }
}
