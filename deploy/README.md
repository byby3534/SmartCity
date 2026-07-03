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
