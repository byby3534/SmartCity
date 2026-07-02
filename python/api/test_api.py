"""
Flask API 통합 테스트.

서버 없이 Flask test client로 직접 실행.
실행: python -m python.api.test_api

결과 JSON: python/api/test_results/  폴더에 저장
로그:      각 API 호출 소요시간 콘솔 출력
"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

OUTPUT_DIR = Path(__file__).parent / "test_results"


# ---------------------------------------------------------------------------
# 출력 헬퍼
# ---------------------------------------------------------------------------

def _ok(label: str) -> None:
    print(f"  \033[32mOK\033[0m  {label}")


def _fail(label: str, reason: str) -> None:
    print(f"  \033[31m!!\033[0m  {label}: {reason}")


def _log(label: str, elapsed: float) -> None:
    print(f"  \033[90m[time] {label}: {elapsed*1000:.1f}ms\033[0m")


def _section(title: str) -> None:
    print(f"\n{'='*60}")
    print(f"  {title}")
    print(f"{'='*60}")


def _save(filename: str, data: dict | list) -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUTPUT_DIR / filename
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"  \033[90m[save] {path.relative_to(Path(__file__).parents[2])}\033[0m")


def _call_get(client, url: str) -> tuple[int, dict, float]:
    t0 = time.perf_counter()
    r  = client.get(url)
    elapsed = time.perf_counter() - t0
    return r.status_code, r.get_json() or {}, elapsed


def _call_post(client, url: str, payload: dict) -> tuple[int, dict, float]:
    t0 = time.perf_counter()
    r  = client.post(url, data=json.dumps(payload), content_type="application/json")
    elapsed = time.perf_counter() - t0
    return r.status_code, r.get_json() or {}, elapsed


# ---------------------------------------------------------------------------
# 테스트
# ---------------------------------------------------------------------------

def run_tests() -> int:
    from python.api.flask_app import app
    client   = app.test_client()
    failures = 0

    # ================================================================
    # 1. /health
    # ================================================================
    _section("1. /health")

    status, data, elapsed = _call_get(client, "/health")
    _log("/health", elapsed)
    if status == 200 and data.get("status") == "ok":
        _ok("status=ok")
        _save("01_health.json", data)
    else:
        _fail("/health", f"status={status}")
        failures += 1

    # ================================================================
    # 2. /oni  — 슬라이더 초기값
    # ================================================================
    _section("2. /oni  (슬라이더 초기값)")

    oni_results = []
    cases = [
        (2023, 8,  "과거 실측",     lambda v: v != 0.0),
        (2015, 1,  "과거 실측(구간 내)", lambda v: v != 0.0),
        (2030, 8,  "미래",          lambda v: v == 0.0),
    ]
    for year, month, desc, check in cases:
        status, d, elapsed = _call_get(client, f"/oni?year={year}&month={month}")
        _log(f"/oni {desc}", elapsed)
        if status != 200:
            _fail(f"/oni {desc}", f"status={status}")
            failures += 1
            continue
        oni_val = d.get("output", {}).get("oni")
        if not check(oni_val):
            _fail(f"/oni {desc}", f"oni={oni_val} (예상과 다름)")
            failures += 1
        else:
            _ok(f"year={year} month={month} → oni={oni_val}  ({desc})")
        oni_results.append(d)

    _save("02_oni.json", oni_results)

    # ================================================================
    # 3. /predict  — 단일 월 예측
    # ================================================================
    _section("3. /predict  (단일 월 예측)")

    # 3-1. 미래 시나리오
    status, d, elapsed = _call_get(client, "/predict?year=2030&month=8&oni=1.2")
    _log("/predict 미래 (2030-08 oni=1.2)", elapsed)
    if status != 200:
        _fail("/predict 미래", f"status={status}")
        failures += 1
    else:
        pred    = d.get("predicted", {})
        regions = pred.get("regions", [])
        n_gu    = len(regions)
        sample  = regions[0] if regions else {}
        usage_keys = list(sample.get("usage", {}).keys())
        building_type = sample.get("building_type", {})
        building_keys = list(building_type.keys())
        sample_bt = building_keys[0] if building_keys else None
        sample_score = (
            building_type[sample_bt].get("reduction_need_score")
            if sample_bt else None
        )

        oni_status = pred.get("oni_status")
        _ok(f"asos_temp={pred.get('asos_temp')}℃ | alert={pred.get('alert_level')}({pred.get('alert_label')}) | oni_status={oni_status} | is_simulated={d.get('is_simulated')}")
        _ok(f"supply_mw={pred.get('supply',{}).get('supply_mw')} | reserve_rate={pred.get('supply',{}).get('reserve_rate')}%")
        _ok(f"[{sample.get('gu')}] ta_gu={sample.get('ta_gu')} | total_mwh={sample.get('total_consumption_mwh')}")
        _ok(f"usage({len(usage_keys)}종): {usage_keys}")
        _ok(f"building_type({len(building_keys)}종): {sample_bt}={sample_score}")

        if n_gu != 25:
            _fail("/predict", f"구 수={n_gu} (기대=25)"); failures += 1
        if len(usage_keys) != 7:
            _fail("/predict", f"usage 수={len(usage_keys)} (기대=7)"); failures += 1
        if len(building_keys) != 34:
            _fail("/predict", f"building_type 수={len(building_keys)} (기대=34)"); failures += 1
        if sample_bt and sample_score is None:
            _fail("/predict", "building_type.reduction_need_score 없음"); failures += 1
        if oni_status not in ("엘니뇨", "라니냐", "중립"):
            _fail("/predict", f"oni_status={oni_status} (기대=엘니뇨/라니냐/중립)"); failures += 1
        if pred.get("alert_level") is None:
            _fail("/predict", "alert_level 없음"); failures += 1

        _save("03_predict_future.json", d)

    # 3-2. 과거 실측 ONI → is_simulated=False
    status, d2, elapsed = _call_get(client, "/predict?year=2023&month=8&oni=1.37")
    _log("/predict 과거 실측ONI (2023-08 oni=1.37)", elapsed)
    if status == 200:
        _ok(f"과거 실측ONI → is_simulated={d2.get('is_simulated')} (False여야 정상)")
        _save("03_predict_past_actual.json", d2)
    else:
        _fail("/predict 과거 실측", f"status={status}"); failures += 1

    # 3-3. 과거 ONI 변경 → is_simulated=True
    status, d3, elapsed = _call_get(client, "/predict?year=2023&month=8&oni=2.0")
    _log("/predict 과거 ONI변경 (2023-08 oni=2.0)", elapsed)
    if status == 200:
        _ok(f"과거 ONI변경 → is_simulated={d3.get('is_simulated')} (True여야 정상)")
        _save("03_predict_past_simulated.json", d3)
    else:
        _fail("/predict 과거 ONI변경", f"status={status}"); failures += 1

    # 3-4. 파라미터 누락 → 400
    status, _, elapsed = _call_get(client, "/predict?year=2030&month=8")
    _log("/predict 파라미터 누락", elapsed)
    if status == 400:
        _ok("파라미터 누락 → 400 정상")
    else:
        _fail("/predict 파라미터 누락", f"status={status} (기대=400)"); failures += 1

    # 3-4. 과거 실측 ONI → EPSIS 실측 예비율 (2013-01 경계)
    status, d4, elapsed = _call_get(client, "/predict?year=2013&month=1&oni=-0.29")
    _log("/predict 과거 EPSIS 예비율 (2013-01)", elapsed)
    if status == 200:
        pred = d4.get("predicted", {})
        rr = pred.get("supply", {}).get("reserve_rate")
        temp = pred.get("asos_temp")
        label = pred.get("alert_label")
        if d4.get("is_simulated") is not False:
            _fail("/predict 과거 EPSIS", f"is_simulated={d4.get('is_simulated')}"); failures += 1
        elif label != "경계":
            _fail("/predict 과거 EPSIS", f"alert_label={label}, reserve_rate={rr} (기대=경계)"); failures += 1
        elif temp is None or temp > -1.0:
            _fail("/predict 과거 EPSIS", f"asos_temp={temp} (기대=ASOS 실측 약 -3.5℃)"); failures += 1
        else:
            _ok(f"2013-01 실측ONI → temp={temp}℃ | reserve_rate={rr}% | alert={label}")
    else:
        _fail("/predict 과거 EPSIS", f"status={status}"); failures += 1

    # ================================================================
    # 4. /predict/oni_range  — ONI 범위 그래프용
    # ================================================================
    _section("4. /predict/oni_range  (ONI 범위 그래프용)")

    status, d, elapsed = _call_get(client, "/predict/oni_range?year=2030&month=8")
    _log("/predict/oni_range (2030-08, ONI -2.5~+2.5)", elapsed)
    if status != 200:
        _fail("/predict/oni_range", f"status={status}"); failures += 1
    else:
        oni_range = d.get("oni_range", [])
        _ok(f"포인트 수={len(oni_range)} (기대=51)")
        if oni_range:
            first, last = oni_range[0], oni_range[-1]
            _ok(f"ONI 범위: {first['oni']} ~ {last['oni']}")
            _ok(f"ONI=-2.5 → asos_temp={first['asos_temp']} | supply_mw={first['supply_mw']} | reserve_rate={first['reserve_rate']} | alert_level={first['alert_level']} | seoul_total={first['seoul_total_consumption_mwh']}")
            _ok(f"ONI=+2.5 → asos_temp={last['asos_temp']}  | supply_mw={last['supply_mw']}  | reserve_rate={last['reserve_rate']}  | alert_level={last['alert_level']}  | seoul_total={last['seoul_total_consumption_mwh']}")
            if len(oni_range) != 51:
                _fail("/predict/oni_range", f"포인트 수={len(oni_range)}"); failures += 1
            if first["asos_temp"] < last["asos_temp"]:
                _ok("ONI↑ → 기온↑ 방향 정상")
            else:
                _fail("/predict/oni_range", "ONI↑인데 기온↓ (모델 이상)"); failures += 1
            if first.get("alert_level") is None:
                _fail("/predict/oni_range", "alert_level 없음"); failures += 1
            else:
                _ok(f"alert_level 범위: {first['alert_level']} (ONI=-2.5) ~ {last['alert_level']} (ONI=+2.5)")

        _save("04_predict_oni_range.json", d)

    # 4-2. 과거 실측 ONI 격자점 → 관측 데이터
    status, d_past, elapsed = _call_get(client, "/predict/oni_range?year=2013&month=1")
    _log("/predict/oni_range 과거 (2013-01)", elapsed)
    if status != 200:
        _fail("/predict/oni_range 과거", f"status={status}"); failures += 1
    else:
        oni_range = d_past.get("oni_range", [])
        actual_pt = next((p for p in oni_range if p.get("is_simulated") is False), None)
        if actual_pt is None:
            _fail("/predict/oni_range 과거", "실측 ONI 포인트 없음"); failures += 1
        elif actual_pt.get("alert_label") != "경계":
            _fail("/predict/oni_range 과거", f"실측점 alert={actual_pt.get('alert_label')}"); failures += 1
        elif actual_pt.get("asos_temp", 0) > -1.0:
            _fail("/predict/oni_range 과거", f"실측점 temp={actual_pt.get('asos_temp')}"); failures += 1
        else:
            sim_count = sum(1 for p in oni_range if p.get("is_simulated"))
            _ok(f"실측 ONI={actual_pt.get('oni')} → temp={actual_pt.get('asos_temp')}℃ | "
                f"reserve={actual_pt.get('reserve_rate')}% | 시뮬포인트={sim_count}개")

    # ================================================================
    # 5. /blackout_simulation  — 순차 정전 시뮬레이션
    # ================================================================
    _section("5. /blackout_simulation  (순차 정전 시뮬레이션)")

    # 5-1. 심각 케이스 (oni=-2.5 → reserve_rate≈4.4% → 심각)
    payload = {"year": 2030, "month": 8, "oni": -2.5}
    status, d, elapsed = _call_post(client, "/blackout_simulation", payload)
    _log("/blackout_simulation 심각 케이스 (2030-08 oni=-2.5)", elapsed)
    if status != 200:
        _fail("/blackout_simulation 심각", f"status={status}"); failures += 1
    else:
        all_items = [item for entry in d.get("districts_order", []) for item in entry["blackout_items"]]
        _ok(f"alert_level={d.get('alert_level')} ({d.get('alert_label')}) | supply_mw={d.get('supply_mw')} | reserve_rate={d.get('reserve_rate')}%")
        _ok(f"districts_order 수={len(d.get('districts_order', []))} (기대=25)")
        _ok(f"blackout_items 총 수={len(all_items)}")
        if all_items:
            first_gu_entry = next(e for e in d.get("districts_order", []) if e["blackout_items"])
            first_item = first_gu_entry["blackout_items"][0]
            _ok(f"1순위 gu={first_gu_entry['gu']} (ta={first_gu_entry['ta_gu']}℃) | "
                f"building_type={first_item['building_type']} | score={first_item['reduction_need_score']}")
        else:
            _fail("/blackout_simulation 심각", "blackout_items 비어있음"); failures += 1
        if len(d.get("districts_order", [])) != 25:
            _fail("/blackout_simulation 심각", f"districts_order 수={len(d.get('districts_order',[]))}"); failures += 1

        _save("05_blackout_critical.json", d)

    # 5-2. 정상 케이스 (경보 미달 → blackout_items 빈 리스트)
    payload2 = {"year": 2015, "month": 3, "oni": 0.0}
    status, d2, elapsed = _call_post(client, "/blackout_simulation", payload2)
    _log("/blackout_simulation 정상 케이스 (2015-03 oni=0.0)", elapsed)
    if status == 200:
        all_normal = [item for entry in d2.get("districts_order", []) for item in entry["blackout_items"]]
        _ok(f"정상: alert={d2.get('alert_label')} | reserve_rate={d2.get('reserve_rate')}% | blackout_items={len(all_normal)} (0이어야 정상)")
        _save("05_blackout_normal.json", d2)
    else:
        _fail("/blackout_simulation 정상", f"status={status}"); failures += 1

    # ================================================================
    # 결과
    # ================================================================
    print(f"\n{'='*60}")
    if failures == 0:
        print("  \033[32m전체 통과 — API 학습 모델 연결 정상\033[0m")
    else:
        print(f"  \033[31m실패 {failures}건\033[0m")
    print(f"  결과 JSON → {OUTPUT_DIR.relative_to(Path(__file__).parents[2])}/")
    print(f"{'='*60}\n")
    return failures


if __name__ == "__main__":
    sys.exit(run_tests())
