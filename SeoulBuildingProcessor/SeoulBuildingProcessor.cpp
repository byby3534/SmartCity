#include <vector>
#include <algorithm>
#include <unordered_map>
#include <cmath>


#ifdef _WIN32
#define DllExport __declspec(dllexport)  // Windows
#else
#define DllExport __attribute__((visibility("default")))  // Mac, WebGL 둘 다 여기
#endif


extern "C" {
	// 1. 유니티와 통신할 버텍스 구조체 (UV 좌표 대신 buildingId를 심음)
	struct NativeVertex {
		float px, py, pz; // Position
		float nx, ny, nz; // Normal (빛 반사용)
		float buildingId; // 정전 쉐이더 판별용 ID
	};

	// 2. 건물의 기본 정보 및 상태
#pragma pack(push, 1)
	struct BuildingInstanceData {
		double lon;
		double lat;
		float height;
		float terrainAltitude;
		float reductionValue;
		int id;
		int districtId;
		int districtType;
		int buildingType;
		int isBlackout;
		int polygonVertexCount;
		int polygonStartIndex;
	};
#pragma pack(pop)

	// 3. 렌더링 전용 경량 구조체 (GPU 셰이더에 전달할 최소 데이터)
	struct BuildingRenderData {
		float reductionValue;  // 수요감축 필요도 (0~1)
		int   isBlackout;      // 정전 여부 (0 or 1)
	};

	// --- 마스터 데이터 보관소 ---
	std::vector<BuildingInstanceData> buildingBuffer;
	std::vector<float> polygonPointsBuffer; // 모든 건물의 2D 폴리곤 좌표를 일렬로 모아둔 배열 [x1, y1, x2, y2...]

	// --- 렌더링 전용 버퍼 (buildingBuffer와 1:1 대응) ---
	std::vector<BuildingRenderData> renderingBuffer;

	// --- 생성된 메쉬 결과물 보관소 ---
	std::vector<NativeVertex> chunkVertices;
	std::vector<int> chunkIndices;

	// districtId → (시작 인덱스, 개수)
	std::unordered_map<int, std::pair<int, int>> districtRanges;

	// (기본 데이터 로드 함수 생략 - 이전 답변과 동일하게 memcpy로 데이터 적재)
	DllExport void LoadDistrictData(unsigned char* dataPointer, int byteLength, int districtId) {
		if (dataPointer == nullptr || byteLength <= 0)
		{
			return;
		}

		size_t structSize = sizeof(BuildingInstanceData);
		int buildingCount = byteLength / structSize;

		int startIndex = static_cast<int>(buildingBuffer.size());

		// 미리 버퍼 크기 확보(최적화)
		buildingBuffer.reserve(buildingBuffer.size() + buildingCount);

		// 받아온 포인터를 BuildingInstanceData에 복사
		const BuildingInstanceData* rawArray = reinterpret_cast<const BuildingInstanceData*>(dataPointer);
		buildingBuffer.insert(buildingBuffer.end(), rawArray, rawArray + buildingCount);

		districtRanges[districtId] = { startIndex, buildingCount };
	}

	DllExport void LoadPolygonData(float* dataPointer, int elementCount) {
		// C#에서 넘어온 더블 배열을 C++ 벡터에 복사
		polygonPointsBuffer.assign(dataPointer, dataPointer + elementCount);
	}

	// ---------------------------------------------------------
	// 렌더링 버퍼 관리
	// ---------------------------------------------------------

	/// buildingBuffer로부터 렌더링 버퍼를 (재)구축한다.
	/// 모든 구역 데이터 로드가 끝난 후 C#에서 한 번 호출한다.
	DllExport void BuildRenderingBuffer() {
		size_t count = buildingBuffer.size();
		renderingBuffer.resize(count);
		for (size_t i = 0; i < count; i++) {
			renderingBuffer[i].reductionValue = buildingBuffer[i].reductionValue;
			renderingBuffer[i].isBlackout = buildingBuffer[i].isBlackout;
		}
	}

	/// C#에서 계산한 reductionValue 배열을 일괄 적용한다.
	/// values 배열의 길이는 renderingBuffer와 동일해야 한다.
	DllExport void SetReductionValues(float* values, int count) {
		int limit = (count < (int)renderingBuffer.size()) ? count : (int)renderingBuffer.size();
		for (int i = 0; i < limit; i++) {
			renderingBuffer[i].reductionValue = values[i];
			buildingBuffer[i].reductionValue = values[i]; // 원본도 동기화
		}
	}

	DllExport bool GetDistrictRange(int districtId, int* outStartIndex, int* outCount) {
		auto it = districtRanges.find(districtId);
		if (it == districtRanges.end()) {
			*outStartIndex = 0;
			*outCount = 0;
			return false;
		}
		*outStartIndex = it->second.first;
		*outCount = it->second.second;
		return true;
	}

	/// 렌더링 버퍼 포인터 반환 (C# → ComputeBuffer.SetData용)
	DllExport BuildingRenderData* GetRenderingBufferPointer() {
		if (renderingBuffer.empty()) return nullptr;
		return renderingBuffer.data();
	}

	/// 렌더링 버퍼 요소 수 반환
	DllExport int GetRenderingBufferCount() {
		return static_cast<int>(renderingBuffer.size());
	}

	// ---------------------------------------------------------
	// 유니티 쉐이더로 넘겨줄 배열 포인터 반환
	// ---------------------------------------------------------
	DllExport BuildingInstanceData* GetBuildingBufferPointer() {
		if (buildingBuffer.empty()) return nullptr;
		return buildingBuffer.data();
	}

	DllExport int GetBuildingBufferCount() {
		return static_cast<int>(buildingBuffer.size());
	}


	// --- [핵심] EarClipping 알고리즘용 헬퍼 함수 (C# MeshBuilder 포팅) ---
	double CrossProduct(double ox, double oy, double ax, double ay, double bx, double by) {
		return (ax - ox) * (by - oy) - (ay - oy) * (bx - ox);
	}

	bool PointInTriangle(double px, double py, double ax, double ay, double bx, double by, double cx, double cy) {
		double d1 = CrossProduct(px, py, ax, ay, bx, by);
		double d2 = CrossProduct(px, py, bx, by, cx, cy);
		double d3 = CrossProduct(px, py, cx, cy, ax, ay);
		bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
		bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
		return !(hasNeg && hasPos);
	}


	// --- [핵심] 구역 메쉬 병합 및 지형 높이 적용 ---
	// terrainHeights: C#에서 Cesium으로 측정한 해당 구역 건물들의 지형 높이 배열
	DllExport void BuildDistrictMesh(int districtId, float* terrainHeights, int terrainArrayLength, double centerLon, double centerLat) {
		chunkVertices.clear();
		chunkIndices.clear();

		int districtBuildingIndex = 0; // 지형 높이 배열과 매칭하기 위한 인덱스

		const double LAT_TO_METER = 111320.0;
		const double LON_TO_METER = 111319.5 * cos(centerLat * 3.14159265359 / 180.0);

		for (size_t bufferPos = 0; bufferPos < buildingBuffer.size(); bufferPos++) {
			auto& building = buildingBuffer[bufferPos];

			// 해당 구역이 아닌 건물은 스킵
			if (building.districtId != districtId)
			{
				continue;
			}

			// ★ GPU 버퍼(_BuildingRenderBuffer)는 renderingBuffer 위치(=buildingBuffer 로드 순서) 기준으로 정렬되어 있으므로,
			//   정점에는 building.id(원본 고유번호)가 아니라 실제 배열 위치(bufferPos)를 심어야 한다.
			float renderBufferIndex = (float)bufferPos;

			// 1. 배열에서 고도를 읽어와서 건물의 지형 높이로 설정하고 건물의 baseZ와 topZ 계산
			float sampleHeight = (districtBuildingIndex < terrainArrayLength) ? terrainHeights[districtBuildingIndex] : 0.0f;
			building.terrainAltitude = sampleHeight; // Ceisum에서 측정한 지형 높이 저장
			float skirtDepth = 10.0f;
			float baseZ = sampleHeight - skirtDepth;

			float adjustedHeight = (building.height < 5.0f) ? 5.0f : building.height;
			float topZ = sampleHeight + adjustedHeight;
			districtBuildingIndex++;

			// 2. 해당 건물의 폴리곤 좌표 추출
			std::vector<std::pair<double, double>> polygon;
			int floatStartIdx = building.polygonStartIndex * 2;

			if (floatStartIdx + (building.polygonVertexCount * 2) > polygonPointsBuffer.size()) {
				continue;
			}

			double bCenterLocalX = (building.lon - centerLon) * LON_TO_METER;
			double bCenterLocalY = (building.lat - centerLat) * LAT_TO_METER;

			for (int p = 0; p < building.polygonVertexCount; p++) {
				float offsetX = polygonPointsBuffer[floatStartIdx + (p * 2)];
				float offsetY = polygonPointsBuffer[floatStartIdx + (p * 2) + 1];

				double localX = bCenterLocalX + offsetX;
				double localY = bCenterLocalY + offsetY;
				polygon.push_back({ localX, localY });
			}

			int n = polygon.size();
			if (n < 3) continue;

			// 벽면을 세우기 '전'에 다각형의 방향(CCW/CW)을 먼저 판별합니다.
			double area = 0.0;
			for (int i = 0; i < n; i++) {
				int next = (i + 1) % n;
				area += (polygon[i].first * polygon[next].second) - (polygon[next].first * polygon[i].second);
			}
			bool isCCW = (area > 0);

			int vertexOffset = chunkVertices.size();

			// 3. 벽면 생성
			for (int i = 0; i < n; i++) {
				int next = (i + 1) % n;

				NativeVertex b0 = { (float)polygon[i].first, baseZ, (float)polygon[i].second, 0,0,0, renderBufferIndex };
				NativeVertex b1 = { (float)polygon[next].first, baseZ, (float)polygon[next].second, 0,0,0, renderBufferIndex };
				NativeVertex t0 = { (float)polygon[i].first, topZ, (float)polygon[i].second, 0,0,0, renderBufferIndex };
				NativeVertex t1 = { (float)polygon[next].first, topZ, (float)polygon[next].second, 0,0,0, renderBufferIndex };

				int idx = (int)chunkVertices.size();
				chunkVertices.push_back(b0);
				chunkVertices.push_back(b1);
				chunkVertices.push_back(t0);
				chunkVertices.push_back(t1);

				if (isCCW) {
					chunkIndices.insert(chunkIndices.end(), { idx, idx + 2, idx + 1, idx + 1, idx + 2, idx + 3 });
				}
				else {
					chunkIndices.insert(chunkIndices.end(), { idx + 1, idx + 3, idx, idx, idx + 3, idx + 2 });
				}
			}

			// 4. 지붕 생성 (EarClipping) - 벽면 루프 밖에서 건물당 한 번만
			std::vector<int> roofTris;
			std::vector<int> activeIndices(n);
			for (int i = 0; i < n; i++) activeIndices[i] = i;

			int vCount = n;
			int safeguard = 2 * n;

			while (vCount > 2 && safeguard-- > 0) {
				bool earFound = false;
				for (int i = 0; i < vCount; i++) {
					int prevIdx = (i - 1 + vCount) % vCount;
					int nextIdx = (i + 1) % vCount;

					int u = activeIndices[prevIdx];
					int v = activeIndices[i];
					int w = activeIndices[nextIdx];

					double cp = CrossProduct(polygon[u].first, polygon[u].second,
						polygon[v].first, polygon[v].second,
						polygon[w].first, polygon[w].second);

					bool isConvex = isCCW ? (cp > 0) : (cp < 0);

					if (isConvex) {
						bool isEar = true;
						for (int j = 0; j < vCount; j++) {
							if (j == prevIdx || j == i || j == nextIdx) continue;
							int p = activeIndices[j];
							if (PointInTriangle(polygon[p].first, polygon[p].second,
								polygon[u].first, polygon[u].second,
								polygon[v].first, polygon[v].second,
								polygon[w].first, polygon[w].second)) {
								isEar = false;
								break;
							}
						}

						if (isEar) {
							roofTris.push_back(u);
							if (isCCW) {
								roofTris.push_back(w);
								roofTris.push_back(v);
							}
							else {
								roofTris.push_back(v);
								roofTris.push_back(w);
							}
							activeIndices.erase(activeIndices.begin() + i);
							vCount--;
							earFound = true;
							break;
						}
					}
				}
				if (!earFound) break;
			}

			int roofBase = (int)chunkVertices.size();
			for (int i = 0; i < n; i++) {
				chunkVertices.push_back({ (float)polygon[i].first, topZ, (float)polygon[i].second, 0,1,0, renderBufferIndex });
			}
			for (int t : roofTris) chunkIndices.push_back(roofBase + t);
			// 바닥면 생략 - baseZ가 지형 아래에 묻혀 보이지 않음
		}
	}

	DllExport int GetDistrictBuildingCount(int districtId) {
		int count = 0;
		for (auto& b : buildingBuffer) {
			if (b.districtId == districtId) count++;
		}
		return count;
	}

	// 위경도 데이터를 배열로 복사해서 넘겨주기
	DllExport void GetBuildingPositions(int districtId, double* outLons, double* outLats) {
		int i = 0;
		for (auto& b : buildingBuffer) {
			if (b.districtId == districtId) {
				outLons[i] = b.lon;
				outLats[i] = b.lat;
				i++;
			}
		}
	}

	DllExport void ClearAllNativeData() {
		buildingBuffer.clear();
		polygonPointsBuffer.clear();
		renderingBuffer.clear();
		chunkVertices.clear();
		chunkIndices.clear();
		districtRanges.clear();
	}

	// 유니티로 메쉬 데이터를 넘겨주기 위한 포인터 반환 함수
	DllExport NativeVertex* GetChunkVertices() { return chunkVertices.data(); }
	DllExport int GetChunkVertexCount() { return static_cast<int>(chunkVertices.size()); }
	DllExport int* GetChunkIndices() { return chunkIndices.data(); }
	DllExport int GetChunkIndexCount() { return static_cast<int>(chunkIndices.size()); }
}