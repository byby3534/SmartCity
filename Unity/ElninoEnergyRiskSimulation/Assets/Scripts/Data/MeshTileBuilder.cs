using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 구역 메시를 공간 타일로 분할하는 유틸리티.
/// </summary>
public static class MeshTileBuilder
{
    /// <summary>
    /// 구역 전체 메시를 tileN × tileN 공간 타일로 분할한다.
    ///
    /// 핵심 원리:
    ///   - 각 버텍스의 UV2.x 값이 글로벌 렌더링 버퍼 인덱스 (buildingId)
    ///   - bufferStart + 건물 내 순서 = 해당 건물의 bufferIndex
    ///   - 건물 위치(위경도) 기반으로 타일을 결정 → bufferIndex → tileId 매핑
    ///   - 삼각형은 v0의 타일로 배정 (건물이 작으므로 세 버텍스 모두 같은 타일)
    /// </summary>
    /// <param name="fullMesh">원본 구역 메시</param>
    /// <param name="buildingPositions">건물별 (lon, lat, 0) 배열 (구역 내 순서)</param>
    /// <param name="bufferStart">이 구역의 글로벌 렌더링 버퍼 시작 인덱스</param>
    /// <param name="tileN">한 변당 타일 수 (tileN × tileN 으로 분할)</param>
    /// <returns>타일별 (mesh, tileX, tileY) 리스트</returns>
    public static List<(Mesh mesh, int tx, int ty)> SplitIntoTiles(
        Mesh fullMesh,
        double3[] buildingPositions,
        int bufferStart,
        int tileN)
    {
        // 타일 분할이 의미 없는 경우 원본 그대로 반환
        var fallback = new List<(Mesh, int, int)> { (fullMesh, 0, 0) };
        if (buildingPositions == null || buildingPositions.Length == 0 || tileN <= 1)
            return fallback;

        // ── 1. 구역 위경도 경계 계산 ──────────────────────────────────
        double minLon = double.MaxValue, maxLon = double.MinValue;
        double minLat = double.MaxValue, maxLat = double.MinValue;

        for (int i = 0; i < buildingPositions.Length; i++)
        {
            double lon = buildingPositions[i].x;
            double lat = buildingPositions[i].y;
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
        }

        double lonSpan = maxLon - minLon;
        double latSpan = maxLat - minLat;

        // 구역이 너무 좁으면 타일 분할 의미 없음
        if (lonSpan < 1e-9 || latSpan < 1e-9) return fallback;

        // ── 2. 건물별 타일 ID 매핑 (bufferIndex → tileId) ─────────────
        int totalTiles = tileN * tileN;
        var bufferToTile = new Dictionary<int, int>(buildingPositions.Length);

        for (int i = 0; i < buildingPositions.Length; i++)
        {
            // 경도/위도 기준으로 격자 셀 계산
            int cx = Math.Min((int)((buildingPositions[i].x - minLon) / lonSpan * tileN), tileN - 1);
            int cy = Math.Min((int)((buildingPositions[i].y - minLat) / latSpan * tileN), tileN - 1);
            bufferToTile[bufferStart + i] = cy * tileN + cx;
        }

        // ── 3. 버텍스별 타일 배정 ────────────────────────────────────
        Vector3[] srcVerts = fullMesh.vertices;
        Vector2[] srcUV2   = fullMesh.uv2;
        int[]     srcTris  = fullMesh.triangles;

        int[] vertTile = new int[srcVerts.Length];
        for (int v = 0; v < srcVerts.Length; v++)
        {
            // UV2.x = 글로벌 버퍼 인덱스 (buildingId)
            int bufIdx = Mathf.RoundToInt(srcUV2[v].x);
            vertTile[v] = bufferToTile.TryGetValue(bufIdx, out int t) ? t : 0;
        }

        // ── 4. 타일별 버텍스/삼각형 리스트 준비 ───────────────────────
        var tileVerts = new List<Vector3>[totalTiles];
        var tileUV2   = new List<Vector2>[totalTiles];
        var tileTris  = new List<int>[totalTiles];
        // 글로벌 버텍스 인덱스 → 해당 타일 내 로컬 인덱스 매핑
        var remap     = new Dictionary<int, int>[totalTiles];

        for (int i = 0; i < totalTiles; i++)
        {
            tileVerts[i] = new List<Vector3>();
            tileUV2[i]   = new List<Vector2>();
            tileTris[i]  = new List<int>();
            remap[i]     = new Dictionary<int, int>();
        }

        // ── 5. 삼각형 분류 ───────────────────────────────────────────
        // 삼각형은 v0의 타일에 배정.
        // 건물이 작으므로 같은 건물의 세 버텍스는 항상 같은 타일에 속함.
        for (int i = 0; i < srcTris.Length; i += 3)
        {
            int v0 = srcTris[i], v1 = srcTris[i + 1], v2 = srcTris[i + 2];
            int tile = vertTile[v0];

            // GetOrAdd: 해당 타일에 버텍스가 없으면 추가하고 로컬 인덱스 반환
            tileTris[tile].Add(GetOrAdd(tile, v0, srcVerts, srcUV2, tileVerts, tileUV2, remap));
            tileTris[tile].Add(GetOrAdd(tile, v1, srcVerts, srcUV2, tileVerts, tileUV2, remap));
            tileTris[tile].Add(GetOrAdd(tile, v2, srcVerts, srcUV2, tileVerts, tileUV2, remap));
        }

        // ── 6. 타일별 Mesh 생성 ──────────────────────────────────────
        var result = new List<(Mesh, int, int)>();

        for (int ti = 0; ti < totalTiles; ti++)
        {
            if (tileVerts[ti].Count == 0) continue; // 건물 없는 타일 스킵

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.vertices  = tileVerts[ti].ToArray();
            mesh.uv2       = tileUV2[ti].ToArray();
            mesh.triangles = tileTris[ti].ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            result.Add((mesh, ti % tileN, ti / tileN));
        }

        return result;
    }

    // 타일에 버텍스가 없으면 추가하고, 있으면 기존 로컬 인덱스를 반환
    private static int GetOrAdd(
        int tile, int globalIdx,
        Vector3[] srcVerts, Vector2[] srcUV2,
        List<Vector3>[] tileVerts, List<Vector2>[] tileUV2,
        Dictionary<int, int>[] remap)
    {
        if (remap[tile].TryGetValue(globalIdx, out int localIdx))
            return localIdx;

        int newLocal = tileVerts[tile].Count;
        tileVerts[tile].Add(srcVerts[globalIdx]);
        tileUV2[tile].Add(srcUV2[globalIdx]);
        remap[tile][globalIdx] = newLocal;
        return newLocal;
    }

}
