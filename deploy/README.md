# SmartCity 실행 가이드

사전 세팅(`.env`, 모델, nginx 등)은 완료된 상태를 가정합니다.

---

## WebGL 빌드 전 (Unity 캐시 정리)

```bash
./deploy/scripts/clean_unity_webgl_cache.sh
cd SeoulBuildingProcessor && ./build_webgl.sh && cd ..
```

Unity → **WebGL Build** → 출력을 `www/`에 복사 (`Build/`, `TemplateData/`, `index.html`).

---

## WebGL 빌드 후 (www 후처리)

```bash
./deploy/scripts/decompress_webgl_build.sh www/Build
./deploy/scripts/patch_unity_webgl_index.sh www/index.html
```

`www1`에 빌드했다면 먼저: `rsync -a www1/ www/`

---

## 로컬 실행 (WebGL + API)

```bash
./deploy/scripts/local_stack.sh
```

| URL                              | 용도  |
| -------------------------------- | ----- |
| http://localhost:8080/           | WebGL |
| http://localhost:8080/api/health | API   |

종료: `Ctrl+C`

---

## API만 실행

```bash
./deploy/scripts/start_api.sh
```

확인: `curl http://127.0.0.1:5001/health`

---

## 프로덕션

```bash
./deploy/scripts/start_api.sh          # 또는 systemd gunicorn
# nginx: deploy/nginx/smartcity.conf 적용 후 reload
```

WebGL 정적 파일 → 서버 `www/`  
Cesium Ion **Allowed URLs**에 배포 URL 등록.
