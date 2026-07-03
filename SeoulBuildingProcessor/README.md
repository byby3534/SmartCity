# SeoulBuildingProcessor

서울 건물 메시/버퍼 처리용 **네이티브 C++ 플러그인**입니다.  
Unity `BuildingManager`가 `[DllImport]`로 호출합니다.

| 플랫폼 | 산출물 | 스크립트 |
|--------|--------|----------|
| macOS (에디터) | `Assets/Plugins/macOS/SeoulBuildingProcessor.bundle` | `build_mac.sh` |
| WebGL | `Assets/Plugins/WebGL/SeoulBuildingProcessor.a` | `build_webgl.sh` |

WebGL에서는 **`.a`만** 링크 대상입니다. `.o`는 중간 산출물이며, Unity 인스펙터에서 WebGL은 **비활성화**해 두세요.

---

## WebGL 네이티브 플러그인 빌드

```bash
cd SeoulBuildingProcessor
./build_webgl.sh
```

Unity Hub에 **WebGL Build Support**가 설치되어 있어야 합니다.  
다른 에디터 버전을 쓰면:

```bash
UNITY_VERSION=6000.3.18f1 ./build_webgl.sh
```

---

## 멀티스레딩 켜기 / 끄기

### ⚠️ Cesium for Unity 사용 시 (현재 프로젝트)

**WebGL 빌드에서 Multithreading을 끄면 빌드가 실패합니다.**  
캐시를 지워도 동일합니다. Cesium 네이티브(`libCesiumForUnityNative-Runtime.a`)가 pthread / Wasm 예외를 **필수**로 요구합니다.

빌드 실패 예 (공식 문서와 동일):

```
wasm-ld: error: ...CesiumCreditSystem.cpp.o: undefined symbol: __wasm_lpad_context
wasm-ld: error: ...CesiumCreditSystem.cpp.o: undefined symbol: _Unwind_CallPersonality
```

→ [Cesium Supported Platforms — Web](https://cesium.com/learn/cesium-unity/ref-doc/supported-platforms.html)

| 목적 | Multithreading |
|------|----------------|
| **WebGL 빌드 (Cesium 포함)** | **반드시 ON** |
| `http://localhost:8080` 실행 | ON + nginx COOP/COEP |
| `http://192.168.x.x:8080` (같은 Wi‑Fi) | 빌드는 ON 유지. **런타임**은 HTTP+IP에서 멀티스레딩 불가 → **HTTPS** 또는 화면 공유 |

같은 Wi‑Fi 공유를 위해 Multithreading을 끄는 것은 **이 프로젝트에서는 선택지가 아닙니다.**

---

### 스크립트를 다시 수정해야 하나?

**아니요.** `build_webgl.sh`는 환경 변수로 분기합니다.  
**커맨드 + Unity Player Settings**만 맞추면 됩니다.

| 단계 | Cesium WebGL 빌드 (권장·필수) |
|------|-------------------------------|
| 1. 네이티브 재빌드 | `USE_PTHREADS=1 ./build_webgl.sh` |
| 2. Unity Player Settings | **Enable Native C/C++ Multithreading** 켜기 |
| 3. Target WebAssembly 2023 | **켜기** (Unity 6 + Cesium 조합) |
| 4. WebGL 캐시 삭제 | `./deploy/scripts/clean_unity_webgl_cache.sh` |
| 5. Unity WebGL Build | `www/`에 복사 |

| 단계 | Multithreading **OFF** (Cesium 없을 때만) |
|------|------------------------------------------|
| 1. 네이티브 재빌드 | `./build_webgl.sh` |
| 2. Unity | Multithreading 끄기 |

`SeoulBuildingProcessor.cpp`는 `pthread` / `std::thread`를 **사용하지 않습니다**.  
`USE_PTHREADS`는 **Unity·Cesium 링크 플래그와 맞추기 위한 것**입니다.

---

## 설정 변경 후 (필수)

Player Settings에서 **Multithreading**, **WebAssembly 2023**, **Exceptions** 등을 바꾼 뒤에는  
이전 `Library` 산출물이 남아 링크 오류가 날 수 있습니다.

예: `undefined symbol: __wasm_lpad_context`, `_Unwind_CallPersonality` (Cesium 캐시 불일치)

**Unity를 종료한 뒤:**

```bash
# 저장소 루트에서
./deploy/scripts/clean_unity_webgl_cache.sh
./SeoulBuildingProcessor/build_webgl.sh   # 또는 USE_PTHREADS=1 ...
```

그다음 Unity에서 WebGL을 **처음부터 다시** 빌드하세요.

---

## Unity Player Settings 위치

**Edit → Project Settings → Player → WebGL → Publishing Settings**

- **Enable Native C/C++ Multithreading** — 위 표와 `USE_PTHREADS`를 **같이** 맞출 것
- **Target WebAssembly 2023** — 설정을 바꿨다면 반드시 캐시 삭제 후 재빌드
- **Enable Exceptions** — 보통 *Explicitly Thrown Exceptions Only* 유지. 링크가 계속 실패하면 *None* 시도

---

## 배포 / 로컬 서버 (요약)

| 접속 URL | 빌드 | 브라우저 실행 |
|----------|------|----------------|
| `http://localhost:8080` | Multithreading **ON** | OK (nginx COOP/COEP) |
| `http://192.168.x.x:8080` | Multithreading **ON** (빌드 필수) | **실패** — HTTP+IP는 Secure Context 아님 |
| `https://192.168.x.x:8080` | Multithreading **ON** | 가능 (자체 서명 인증서 등) |

WebGL 빌드 후:

```bash
# www/에 Build 복사 후
./deploy/scripts/local_stack.sh
```

---

## 파일 설명

| 파일 | 설명 |
|------|------|
| `SeoulBuildingProcessor.cpp` | 건물 바이너리 로드, 메시 생성, 렌더 버퍼 |
| `build_webgl.sh` | Emscripten으로 `.o` / `.a` 생성 (`USE_PTHREADS` 지원) |
| `build_mac.sh` | macOS 에디터용 번들 빌드 |
| `pch.h` | Windows 전용 프리컴파일 헤더 |
