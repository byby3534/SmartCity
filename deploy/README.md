# SmartCity 실행 가이드

사전 세팅(`.env`, 모델, nginx 등)은 완료된 상태를 가정합니다.

---

## WebGL 빌드 전 (Unity 캐시 정리)

**macOS / Linux / Git Bash**

```bash
./deploy/scripts/clean_unity_webgl_cache.sh
cd SeoulBuildingProcessor && ./build_webgl.sh && cd ..
```

Unity → **WebGL Build** → 출력을 `www/`에 복사 (`Build/`, `TemplateData/`, `index.html`).

> Windows에서 `build_webgl.sh`는 Unity Emscripten 경로가 Mac 기준입니다. 네이티브 플러그인(`.a`)은 Mac에서 빌드해 커밋하거나, Windows Unity WebGL Build Support 경로에 맞게 스크립트를 수정하세요.

---

## WebGL 빌드 후 (www 후처리)

**macOS / Linux**

```bash
./deploy/scripts/decompress_webgl_build.sh www/Build
./deploy/scripts/patch_unity_webgl_index.sh www/index.html
```

**Windows (PowerShell)**

```powershell
.\deploy\scripts\decompress_webgl_build.ps1 www\Build
.\deploy\scripts\patch_unity_webgl_index.ps1 www\index.html
```

`www1`에 빌드했다면 먼저 `www1\` 내용을 `www\`로 복사.

---

## 로컬 실행 (WebGL + API)

**macOS**

```bash
./deploy/scripts/local_stack.sh
```

**Windows**

1. [nginx for Windows](https://nginx.org/en/download.html) → `C:\nginx` 등에 압축 해제
2. PowerShell (레포 루트):

```powershell
.\deploy\scripts\local_stack.ps1
# nginx 경로가 다르면:
.\deploy\scripts\local_stack.ps1 -NginxRoot "D:\nginx"
```

| URL                              | 용도  |
| -------------------------------- | ----- |
| http://localhost:8080/           | WebGL |
| http://localhost:8080/api/health | API   |

종료: `Ctrl+C`

**멀티스레딩 확인** — 브라우저 콘솔: `crossOriginIsolated` → `true` 여야 함.  
`false`이면 COOP/COEP 헤더 없이 서빙 중인 것 (Live Server, `python -m http.server`, `file://` 등).

---

## API만 실행

**macOS**

```bash
./deploy/scripts/start_api.sh
```

**Windows**

```powershell
.\deploy\scripts\start_api.ps1
```

확인: `http://127.0.0.1:5001/health`

---

## 프로덕션 (Linux 서버)

```bash
./deploy/scripts/start_api.sh          # 또는 systemd gunicorn
# nginx: deploy/nginx/smartcity.conf 적용 후 reload
```

WebGL 정적 파일 → 서버 `www/`  
Cesium Ion **Allowed URLs**에 배포 URL 등록.

**서버도 동일 조건** — Unity WebGL Multithreading ON이면 `smartcity.conf`처럼 **모든 관련 location**에 COOP/COEP가 있어야 합니다.  
Windows 로컬에서 pthread 오류가 났다면, 서버 nginx 설정이 불완전할 때 **같은 종류의 오류**가 날 수 있습니다. 반대로 `smartcity.conf`를 그대로 적용했다면 Linux 서버는 정상일 수 있습니다.

---

## 스크립트 대응표

| 작업 | macOS | Windows |
|------|-------|---------|
| 로컬 스택 | `local_stack.sh` | `local_stack.ps1` |
| API만 | `start_api.sh` (gunicorn) | `start_api.ps1` (Flask) |
| www 압축 해제 | `decompress_webgl_build.sh` | `decompress_webgl_build.ps1` |
| index 패치 | `patch_unity_webgl_index.sh` | `patch_unity_webgl_index.ps1` |
| nginx 설정 | `nginx/local.conf` | `nginx/local.windows.conf` |

---

## WebGL 최적화 (deploy)

WebGL deploy OOM(`abort("OOM")`) 대응으로 적용한 내용입니다. **셰이더 교체/단순화는 하지 않았습니다.**

### 배경

- 증상: 브라우저 WebGL에서 wasm 힙 한도 초과 → `loader.js` OOM, predict/시뮬 직후 클라이언트 요청 중단
- API는 정상(200) — 서버 문제가 아니라 **클라이언트 메모리** 이슈
- **화면:** 구 클릭 시 1구, 시뮬 시 current+next **2구만** `SetActive`
- **메모리:** 25구 바이너리/C++ 버퍼는 Start 시 전량 로드(변경 없음). WebGL은 **mesh만 lazy load**

### Player Settings

| 항목 | 값 | 비고 |
|------|-----|------|
| `webGLThreadsSupport` | **1 (ON)** | Cesium deploy **필수**. OFF 시 `CesiumCreditSystem` 링크 에러 |
| `webGLInitialMemorySize` | 2048 MB | |
| `webGLMaximumMemorySize` | 8192 MB | |

deploy 서버 nginx(`smartcity.conf`)에는 COOP/COEP 헤더 필수 (SharedArrayBuffer / pthread).

### 적용 코드

#### 건물 GPU 데이터 (`BuildingManager`)

| 항목 | 내용 |
|------|------|
| Material 런타임 복제 | `new Material()` — `.mat` 에셋 dirty 방지 |
| 텍스처 업로드 버퍼 재사용 | `Color[]` 매번 할당 → `_pixelUploadBuffer` 재사용 |
| 정전 연출 부분 업로드 | 전체 `SetPixels` → 변경 인덱스만 `SetPixel` |
| WebGL `Apply()` 빈도 | 3배치마다 1회 GPU 반영 |
| WebGL 텍스처 포맷 | `RGFloat` → `RGHalf` (GPU 메모리 ~50%) |
| WebGL lazy mesh | 25구 전부 mesh 생성 → **선택/시뮬 구만** 생성 |
| WebGL 정전 배치 | `buildingsPerBatch` 400+, 간격 확대 |
| 정렬 | WebGL: predict 시 25구 전체 정렬 대신 **정전 직전 해당 구만** `RebuildSortedIndices` |

#### UI 블러 (`WebGLRuntimeTuning`)

| 항목 | 내용 |
|------|------|
| BackgroundBlurGroup 통합 | 좌/우 2개 → primary 1개 (Cesium 배경 캡처 1회) |
| 해상도 | RenderDownscale ≥4, BlurDownscale ≥3 |
| 갱신 | `ManualOnly` — 레이아웃 변경 시에만 blur |
| BlurRadius 상한 | 20 |

#### Cesium (`WebGLRuntimeTuning`)

| 항목 | 값 |
|------|-----|
| `maximumScreenSpaceError` | ≥48 |
| `maximumCachedBytes` | 128MB cap |
| `maximumSimultaneousTileLoads` | ≤8 |
| `preloadAncestors` / `preloadSiblings` | off |

#### 관측 (`WebGLMemoryDiagnostics`)

Console 태그: `[MemDiag:predict-applied]`, `[MemDiag:blackout-start-XXX]`, `[MemDiag:sim-step-N-XXX]` 등

로그 필드: `meshes`, `verts`, `estMeshMB`, `buildings`, `dataTex`, `unityAllocMB`, `monoUsedMB`

### 검증 방법

1. WebGL deploy 빌드 → `smartcity.conf` COOP/COEP 환경에서 서빙
2. Chrome DevTools Console → `[MemDiag:` 필터
3. 단계별 `unityAllocMB` / `estMeshMB` 비교:
   - `lazy-default-district` → `selected-*` → `predict-applied` → `blackout-start-*`
4. OOM 재현 시 **어느 태그 직후** 터지는지 기록

### 남은 병목 (추가 최적화 후보)

1. 25구 **native 데이터 lazy load** — mesh lazy만 적용, C++ 버퍼는 전량 로드
2. **구 단위 데이터 텍스처 분할** — 16384×N 단일 텍스처 대신 구별/청크
3. **비활성 구 mesh 언로드** — 다른 구 전환 시 `Destroy(mesh)`
4. WebGL blur off 또는 uGUI 대체
5. Cesium SSE/캐시 추가 튜닝 (화질 vs 메모리)

### 셰이더

- `BuildingUsage.shader` **변경 없음**
- 정전/히트맵은 동일 셰이더 + `_BuildingDataTex` 값만 갱신
- OOM 주범(코드/로그 기준): mesh, Cesium, blur RT, 대용량 텍스처 업로드/GC
