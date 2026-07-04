using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// WebGL/에디터에서 메모리 스냅샷을 남긴다. OOM 원인 추적용.
/// </summary>
public static class WebGLMemoryDiagnostics
{
    public static void LogSnapshot(string tag, BuildingManager buildingManager = null)
    {
        var sb = new StringBuilder(512);
        sb.Append("[MemDiag:").Append(tag).Append("] ");

        long meshBytes = 0;
        int meshCount = 0;
        long totalVerts = 0;
        long totalTris = 0;
        int activeDistrictMeshes = 0;

        foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;

            meshCount++;
            totalVerts += mesh.vertexCount;
            totalTris += mesh.triangles.Length / 3;
            meshBytes += EstimateMeshBytes(mesh);

            if (filter.gameObject.activeInHierarchy)
                activeDistrictMeshes++;
        }

        sb.Append("meshes=").Append(meshCount);
        sb.Append(" active=").Append(activeDistrictMeshes);
        sb.Append(" verts=").Append(totalVerts);
        sb.Append(" tris=").Append(totalTris);
        sb.Append(" estMeshMB=").Append(BytesToMb(meshBytes).ToString("F1"));

        if (buildingManager != null)
            sb.Append(' ').Append(buildingManager.GetMemoryReportLine());

        sb.Append(" unityAllocMB=").Append(BytesToMb(Profiler.GetTotalAllocatedMemoryLong()).ToString("F1"));
        sb.Append(" monoUsedMB=").Append(BytesToMb(Profiler.GetMonoUsedSizeLong()).ToString("F1"));

        Debug.Log(sb.ToString());
    }

    private static long EstimateMeshBytes(Mesh mesh)
    {
        long bytes = 0;
        bytes += (long)mesh.vertexCount * 32;
        bytes += (long)mesh.triangles.Length * 4;
        return bytes;
    }

    private static float BytesToMb(long bytes) => bytes / (1024f * 1024f);
}
