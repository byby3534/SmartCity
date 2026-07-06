using CesiumForUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[StructLayout(LayoutKind.Sequential)]
struct BuildingVertex
{
    public Vector3 position;
    public Vector3 normal;
    public Vector2 uv2;
}

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
    public CesiumGeoreference cesiumGeoreference;
    public Material buildingMaterial;
    // private ComputeBuffer renderBuffer;
    private Texture2D renderTexture;
    private ushort[] _rawPixelBuffer;
    private WaitForSeconds _waitBatch;
    private WaitForSeconds _waitRestore;
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

    private readonly Mesh[] _meshPool = new Mesh[2];
    private int _meshPoolNext = 0;

    private Coroutine blackoutCoroutine;

    private bool _isSimulationActive;
    private DistrictType _selectedDistrict = DistrictType.None;
    private DistrictType _pendingBlackoutDistrict = DistrictType.None;

    private Coroutine _activationCoroutine;

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

        buildingsPerBatch = Mathf.Max(buildingsPerBatch, 400);
        secondsBetweenBatch = Mathf.Max(secondsBetweenBatch, 0.08f);
        secondsBetweenRestoreBatch = Mathf.Max(secondsBetweenRestoreBatch, 0.06f);
        _waitBatch = new WaitForSeconds(secondsBetweenBatch);
        _waitRestore = new WaitForSeconds(secondsBetweenRestoreBatch);

        SceneRefs.Resolve(ref districtManager);
        SceneRefs.Resolve(ref simulationController);
        SceneRefs.Resolve(ref minimapManager);
        SceneRefs.Resolve(ref mainCameraController);
    }

    private void Start()
    {
        ClearAllNativeData();
        Debug.Log("[CityManager] Start() - 구역별 건물 데이터 로드 및 메쉬 생성 시작...");
        StartCoroutine(InitializeAsync());
    }

    private IEnumerator InitializeAsync()
    {
        LoadGlobalPolygonBinaryFast();
        yield return null;

        foreach (DistrictType district in Enum.GetValues(typeof(DistrictType)))
        {
            if (district == DistrictType.None) continue;
            LoadDistrictBinaryFast((int)district);
            yield return null; // GC 실행 기회 확보
        }

        BuildRenderingBuffer();
        InitializeRenderBuffer();
        BuildingDistrictIndexMap();

        StartCoroutine(SpawnDefaultDistrict());
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

    IEnumerator SpawnDefaultDistrict()
    {
        yield return new WaitForSeconds(2.0f);
        yield return SpawnDistrictAsync((int)DistrictType.JONGNO);

        WebGLMemoryDiagnostics.LogSnapshot("lazy-default-district", this);
        Debug.Log("[BuildingManager] 기본 구(종로) 메시 준비 완료.");
    }

    public string GetMemoryReportLine()
    {
        int buildingCount = cachedRenderData?.Length ?? 0;
        long texBytes = renderTexture != null
            ? (long)renderTexture.width * renderTexture.height * 4
            : 0;
        return $"buildings={buildingCount} districtRoots={districtRoots.Count} " +
               $"dataTex={TexWidth}x{_texHeight} estTexMB={texBytes / (1024f * 1024f):F2} " +
               $"lazyMeshes=true";
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

        Resources.UnloadAsset(binFile);
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

        Resources.UnloadAsset(polyFile);
    }

    public int GetBuildingCount() => GetBuildingBufferCount();

    /// <summary>
    /// native 버퍼에서 districtType/buildingType만 직접 읽어 채운다.
    /// NativeBuildingData[] 전체 복사(~20MB) 없이 필요한 2 필드만 추출.
    /// </summary>
    public unsafe void FillBuildingTypeMappings(int[] districtTypes, int[] buildingTypes)
    {
        int count = GetBuildingBufferCount();
        if (count == 0) return;

        IntPtr ptr = GetBuildingBufferPointer();
        int stride = Marshal.SizeOf<NativeBuildingData>();
        int dtOffset = (int)Marshal.OffsetOf<NativeBuildingData>(nameof(NativeBuildingData.districtType));
        int btOffset = (int)Marshal.OffsetOf<NativeBuildingData>(nameof(NativeBuildingData.buildingType));

        byte* basePtr = (byte*)ptr.ToPointer();
        for (int i = 0; i < count; i++)
        {
            byte* item = basePtr + i * stride;
            districtTypes[i] = *(int*)(item + dtOffset);
            buildingTypes[i] = *(int*)(item + btOffset);
        }
    }

    /// <summary>
    /// 전체 건물의 NativeBuildingData를 읽어온다.
    /// 메모리 부담이 크므로 빌드 툴 등 에디터 전용으로만 사용할 것.
    /// 런타임 매핑은 FillBuildingTypeMappings()를 사용.
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
        WebGLMemoryDiagnostics.LogSnapshot($"spawn[{districtId}]-A:start", this);

        NativeArray<float> heights = LoadHeightsToNative(districtId);
        if (!heights.IsCreated)
        {
            Debug.LogError($"[BuildingManager] {districtId}: TerrainHeights 없음. 'Tools > Bake Terrain Heights' 를 먼저 실행하세요.");
            yield break;
        }

        WebGLMemoryDiagnostics.LogSnapshot($"spawn[{districtId}]-C:after-heights", this);

        Vector2 centerCoord = DistrictCoordinates.GetCenter(districtId);
        CallBuildDistrictMesh(districtId, heights, centerCoord.x, centerCoord.y);
        heights.Dispose();

        WebGLMemoryDiagnostics.LogSnapshot($"spawn[{districtId}]-D:after-builddist", this);

        yield return null;

        Mesh fullMesh = ExtractChunkMesh();
        if (fullMesh == null)
        {
            Debug.LogError($"[BuildingManager] {districtId}: 메시 빌드 실패.");
            yield break;
        }

        WebGLMemoryDiagnostics.LogSnapshot($"spawn[{districtId}]-E:after-extract", this);

        yield return null;

        SpawnDistrictObject(districtId, fullMesh);

        WebGLMemoryDiagnostics.LogSnapshot($"spawn[{districtId}]-F:after-spawn", this);
    }

    private NativeArray<float> LoadHeightsToNative(int id)
    {
        TextAsset file = Resources.Load<TextAsset>($"Districts/{id}/TerrainHeights");
        if (file == null) return default;

        byte[] raw = file.bytes;
        Resources.UnloadAsset(file);

        int floatCount = raw.Length / sizeof(float);
        // Temp: 같은 프레임 내(yield 전)에 Dispose하므로 단편화 없는 스택 할당 사용
        NativeArray<float> native = new NativeArray<float>(floatCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        CopyBytesToNativeArray(raw, native);
        return native;
    }

    private static unsafe void CopyBytesToNativeArray(byte[] src, NativeArray<float> dst)
    {
        fixed (byte* p = src)
            UnsafeUtility.MemCpy(dst.GetUnsafePtr(), p, src.Length);
    }

    private static unsafe void CallBuildDistrictMesh(int districtId, NativeArray<float> heights, double centerLon, double centerLat)
    {
        void* ptr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(heights);
        BuildDistrictMesh(districtId, (IntPtr)ptr, heights.Length, centerLon, centerLat);
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

        districtRoot.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = districtRoot.AddComponent<MeshRenderer>();
        r.sharedMaterial = buildingMaterial;
        r.renderingLayerMask = RenderingLayerMask.GetMask("BUILDING");
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        Debug.Log($"[BuildingManager] 구 {districtId} 메시 생성 — verts={mesh.vertexCount:N0} tris={mesh.GetIndexCount(0) / 3:N0}");
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

        int slot = _meshPoolNext;
        _meshPoolNext = 1 - _meshPoolNext;
        if (_meshPool[slot] == null)
            _meshPool[slot] = new Mesh { indexFormat = IndexFormat.UInt32 };
        Mesh mesh = _meshPool[slot];

        Mesh.MeshDataArray dataArray = Mesh.AllocateWritableMeshData(1);
        Mesh.MeshData data = dataArray[0];

        data.SetVertexBufferParams(vCount,
            new VertexAttributeDescriptor(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal,    VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2));
        data.SetIndexBufferParams(iCount, IndexFormat.UInt32);

        NativeArray<BuildingVertex> vertexBuffer = data.GetVertexData<BuildingVertex>();
        unsafe
        {
            float* src = (float*)GetChunkVertices().ToPointer();
            for (int i = 0; i < vCount; i++)
            {
                int o = i * 7;
                vertexBuffer[i] = new BuildingVertex
                {
                    position = new Vector3(src[o],     src[o + 1], src[o + 2]),
                    normal   = new Vector3(src[o + 3], src[o + 4], src[o + 5]),
                    uv2      = new Vector2(src[o + 6], 0f)
                };
            }
        }

        NativeArray<int> indexBuffer = data.GetIndexData<int>();
        unsafe
        {
            void* dst = indexBuffer.GetUnsafePtr();
            void* src = GetChunkIndices().ToPointer();
            UnsafeUtility.MemCpy(dst, src, (long)iCount * sizeof(int));
        }

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor(0, iCount));

        Mesh.ApplyAndDisposeWritableMeshData(dataArray, mesh,
            MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontValidateIndices   |
            MeshUpdateFlags.DontNotifyMeshUsers);
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

    private const TextureFormat BuildingTexFormat = TextureFormat.RGHalf;

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

    private void UploadToTexture()
    {
        if (renderTexture == null || cachedRenderData == null) return;

        int total = TexWidth * _texHeight;
        int bufSize = total * 2;
        if (_rawPixelBuffer == null || _rawPixelBuffer.Length != bufSize)
            _rawPixelBuffer = new ushort[bufSize];

        for (int i = 0; i < cachedRenderData.Length; i++)
        {
            _rawPixelBuffer[i * 2]     = new half(cachedRenderData[i].reductionValue).value;
            _rawPixelBuffer[i * 2 + 1] = new half(cachedRenderData[i].isBlackout).value;
        }
        for (int i = cachedRenderData.Length * 2; i < bufSize; i++)
            _rawPixelBuffer[i] = 0;

        renderTexture.SetPixelData(_rawPixelBuffer, 0);
        renderTexture.Apply(false);
    }

    private void UploadIndicesToTexture(IReadOnlyList<int> indices)
    {
        if (renderTexture == null || cachedRenderData == null || indices == null) return;

        var raw = renderTexture.GetRawTextureData<ushort>();
        for (int k = 0; k < indices.Count; k++)
        {
            int i = indices[k];
            if ((uint)i >= (uint)cachedRenderData.Length) continue;
            raw[i * 2]     = new half(cachedRenderData[i].reductionValue).value;
            raw[i * 2 + 1] = new half(cachedRenderData[i].isBlackout).value;
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

    // 프레임마다 GetFullBuildingData()를 호출하지 않도록, 구별 건물 인덱스 매핑을 초기화 시점에 한 번만 수행
    private void BuildingDistrictIndexMap()
    {
        foreach (DistrictType district in Enum.GetValues(typeof(DistrictType)))
        {
            if (district == DistrictType.None) continue;
            int districtId = (int)district;
            if (GetDistrictRange(districtId, out int start, out int count))
                districtRanges[districtId] = (start, count);
        }
#if !(UNITY_WEBGL && !UNITY_EDITOR)
        // WebGL: reductionValue 데이터 도착 전 정렬은 무의미. HandleFlyEndToStart()에서 lazy sort.
        RebuildSortedIndices();
#endif
    }

    private void SortDistrictByReduction(int districtId)
    {
        if (!districtRanges.TryGetValue(districtId, out var range)) return;
        int[] indices = new int[range.count];
        for (int i = 0; i < range.count; i++)
            indices[i] = range.start + i;
        Array.Sort(indices, (a, b) =>
            cachedRenderData[b].reductionValue.CompareTo(cachedRenderData[a].reductionValue));
        sortedDistrictIndices[districtId] = indices;
    }

    public void RebuildSortedIndices()
    {
        foreach (var id in districtRanges.Keys)
            SortDistrictByReduction(id);
    }

    public void SortDistrictByReduction(DistrictType district) =>
        SortDistrictByReduction((int)district);
    #endregion

    private void DestroyDistrictRoot(GameObject root)
    {
        if (root == null) return;
        var mf = root.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null
            && mf.sharedMesh != _meshPool[0]
            && mf.sharedMesh != _meshPool[1])
            Destroy(mf.sharedMesh);
        Destroy(root);
    }

    #region DistrictActivation

    private void HandleDistrictSelected(DistrictType districtType)
    {
        if (_isSimulationActive) return;
        _selectedDistrict = districtType;
        if (_activationCoroutine != null)
        {
            StopCoroutine(_activationCoroutine);
            DestroyAllDistrictsExcept(-1);
            // 코루틴이 중단되면 ActivateAndMoveToDistrict의 후반부 GC/Unload가 실행 안 됨 → 여기서 보완
            System.GC.Collect();
            StartCoroutine(UnloadUnusedAsync());
        }
        _activationCoroutine = StartCoroutine(ActivateAndMoveToDistrict(districtType));
    }

    private void DestroyAllDistrictsExcept(int keepId)
    {
        var toDestroy = new List<int>();
        foreach (var (id, _) in districtRoots)
            if (id != keepId) toDestroy.Add(id);
        foreach (int id in toDestroy)
        {
            DestroyDistrictRoot(districtRoots[id]);
            districtRoots.Remove(id);
        }
    }

    private IEnumerator ActivateAndMoveToDistrict(DistrictType districtType)
    {
        // 1. 새 구 메시 생성
        if (!districtRoots.ContainsKey((int)districtType))
            yield return SpawnDistrictAsync((int)districtType);

        // 2. 카메라 이동
        mainCameraController.MoveToDistrict(districtType);

        // 3. 이동 완료 대기
        yield return new WaitUntil(() => !mainCameraController.IsFlying);
        yield return new WaitForSeconds(0.3f);

        // 4. 이전 구 파괴
        DestroyAllDistrictsExcept((int)districtType);

        yield return Resources.UnloadUnusedAssets();
        System.GC.Collect();
        yield return null;

        WebGLMemoryDiagnostics.LogSnapshot($"activate-{districtType}", this);
        _activationCoroutine = null;
    }

    private void HandleActiveDistrictsChanged(DistrictType current, DistrictType next)
    {
        _selectedDistrict = current;
        if (_activationCoroutine != null)
        {
            StopCoroutine(_activationCoroutine);
            DestroyAllDistrictsExcept(-1);
            System.GC.Collect();
            StartCoroutine(UnloadUnusedAsync());
        }
        _activationCoroutine = StartCoroutine(ActivateDistricts(current, next));
    }

    private IEnumerator UnloadUnusedAsync()
    {
        yield return Resources.UnloadUnusedAssets();
    }

    private IEnumerator ActivateDistricts(DistrictType current, DistrictType next = DistrictType.None)
    {
        if (!districtRoots.ContainsKey((int)current))
            yield return SpawnDistrictAsync((int)current);
        if (next != DistrictType.None && !districtRoots.ContainsKey((int)next))
            yield return SpawnDistrictAsync((int)next);

        var toDestroy = new List<int>();
        foreach (var (id, _) in districtRoots)
        {
            bool needed = id == (int)current || (next != DistrictType.None && id == (int)next);
            if (!needed) toDestroy.Add(id);
        }

        foreach (int id in toDestroy)
        {
            DestroyDistrictRoot(districtRoots[id]);
            districtRoots.Remove(id);
        }

        yield return Resources.UnloadUnusedAssets();
        System.GC.Collect();
        yield return null;

        WebGLMemoryDiagnostics.LogSnapshot($"activate-{current}", this);
    }

    #endregion

    #region BlackoutHandling
    

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
        if (_pendingBlackoutDistrict == DistrictType.None) return;

        DistrictType districtType = _pendingBlackoutDistrict;
        _pendingBlackoutDistrict = DistrictType.None;

        int districtId = (int)districtType;

        if (blackoutCoroutine != null) StopCoroutine(blackoutCoroutine);
        SortDistrictByReduction(districtId);

        if (!sortedDistrictIndices.TryGetValue(districtId, out var sortedIndices)) return;

        WebGLMemoryDiagnostics.LogSnapshot($"blackout-start-{districtType}", this);
        blackoutCoroutine = StartCoroutine(BlackoutSequence(districtType, sortedIndices));
    }

    IEnumerator BlackoutSequence(DistrictType districtType, int[] sortedIndices)
    {
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

            if (_batchChangedIndices.Count > 0)
                FlushChangedIndicesToGPU(_batchChangedIndices);

            yield return _waitBatch;
        }

        simulationController.NotifyDistrictBlackoutComplete(districtType);
        Debug.Log($"[BuildingManager] 구역 정전 연출 완료 — {blackoutHoldDuration}초 유지 후 복전");
        WebGLMemoryDiagnostics.LogSnapshot($"blackout-complete-{districtType}", this);

        // ── 2단계: 정전 유지 ──
        yield return new WaitForSeconds(blackoutHoldDuration);

        simulationController.NotifyDistrictRestoreStarted(districtType);

        // ── 3단계: 복전 ──
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

            if (_batchChangedIndices.Count > 0)
                FlushChangedIndicesToGPU(_batchChangedIndices);

            yield return _waitRestore;
        }

        simulationController.NotifyDistrictRestoreComplete(districtType);
        Debug.Log("[BuildingManager] 구역 복전 완료 → 다음 구로 이동");
        WebGLMemoryDiagnostics.LogSnapshot($"restore-complete-{districtType}", this);
        blackoutCoroutine = null;
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
            Destroy(renderTexture);
        foreach (var m in _meshPool)
            if (m != null) Destroy(m);
    }
}
