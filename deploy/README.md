# SmartCity 배포 가이드

WebGL 클라이언트와 Flask API를 **같은 도메인**으로 서빙하는 구성입니다.

```
브라우저
  └─ nginx :80 (또는 로컬 :8080)
       ├─ /          → www/          (Unity WebGL 빌드)
       └─ /api/*     → gunicorn:5001 (Flask, /api 접두사 제거 후 전달)
```

Unity WebGL의 `ApiClient`는 빌드 시 `serverUrl = "/api"`를 사용합니다.

---

## 1. 서버 준비

### 필수 파일 (gitignore — 서버에 수동 배치)

| 경로 | 용도 |
|------|------|
| `python/model/artifacts/*.pkl` | ML 모델 |
| `data/output/gu_offset_params.pkl` | 구별 기온 오프셋 |
| `data/file/*` | 원본 CSV |

### 환경 변수

```bash
cp deploy/env.example .env
# KMA_API_KEY, FLASK_PORT 등 편집
```

---

## 2. API 실행 (gunicorn)

```bash
pip install -r requirements.txt
chmod +x deploy/scripts/*.sh
./deploy/scripts/start_api.sh
```

헬스체크: `curl http://127.0.0.1:5001/health`

개발용 (단독 Flask):

```bash
FLASK_DEBUG=1 python -m python.api.flask_app
```

---

## 3. WebGL 빌드 배치

Unity → **File → Build Settings → WebGL → Build**  
산출물을 `www/`에 복사:

```
www/
├── index.html
├── Build/
└── TemplateData/
```

---

## 4. 로컬 통합 테스트 (nginx + API)

```bash
brew install nginx   # macOS
./deploy/scripts/local_stack.sh
```

| URL | 설명 |
|-----|------|
| http://localhost:8080/ | WebGL 정적 파일 |
| http://localhost:8080/api/health | API 프록시 |

`www/index.html`이 없으면 placeholder가 자동 생성됩니다.

---

## 5. 프로덕션 nginx

```bash
# 경로 수정 후
sudo cp deploy/nginx/smartcity.conf /etc/nginx/sites-available/smartcity
sudo ln -s /etc/nginx/sites-available/smartcity /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
```

`smartcity.conf`에서 수정할 항목:

- `server_name` — 실제 도메인
- `root` — `/opt/smartcity/www` 등 WebGL 경로

systemd 예시 (gunicorn):

```ini
[Unit]
Description=SmartCity Flask API
After=network.target

[Service]
User=www-data
WorkingDirectory=/opt/smartcity/repo
EnvironmentFile=/opt/smartcity/repo/.env
ExecStart=/opt/smartcity/venv/bin/gunicorn -c deploy/gunicorn/gunicorn.conf.py
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

---

## 6. Cesium Ion

WebGL 배포 URL을 [Cesium Ion](https://ion.cesium.com) 대시보드 **Allowed URLs**에 등록하세요.

---

## 7. 건물 로드 합동 테스트 체크리스트

BuildingManager 담당자와 함께 확인:

- [ ] Mac 에디터 Play — `Plugins/macOS/SeoulBuildingProcessor.bundle` 로드
- [ ] `Resources/Districts/*.bytes` 존재
- [ ] 구역별 메쉬 생성 (`District_Chunk_*` GameObject)
- [ ] WebGL Build 후 브라우저 — `Plugins/WebGL/SeoulBuildingProcessor.a` 링크 (`.o`는 빌드 중간 산출물, WebGL 비활성)
- [ ] 정전 시뮬레이션 시 건물 셰이더 반응
- [ ] Cesium 지형 위 건물 높이 정상

---

## 폴더 구조

```
deploy/
├── wsgi.py
├── env.example
├── gunicorn/gunicorn.conf.py
├── nginx/
│   ├── smartcity.conf    # 프로덕션
│   └── local.conf        # 로컬 테스트 템플릿
└── scripts/
    ├── start_api.sh
    └── local_stack.sh

www/                      # WebGL 빌드 출력 (gitignore)
```
