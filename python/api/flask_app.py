"""
Flask REST API — Unity ↔ Python 브릿지.

엔드포인트:
  GET  /health
      반환: {"status": "ok"}

  GET  /oni?year=2025&month=8
      [입력] year, month
      슬라이더 초기값용. 과거 실측 ONI 반환, 미래면 0.0 반환.
      반환: {"year": 2025, "month": 8, "oni": 1.37}

  GET  /predict?year=2030&month=8&oni=1.2
      [입력] year, month, oni (슬라이더값)
      [예측] 전체 25구 결과 한 번에 반환.
      ONI 슬라이더 변경 시마다 호출 (Unity 측 디바운싱 권장: 300ms).
      - 과거 + 실측 ONI(is_simulated=false): ASOS·구별기온·KEPCO 소비·EPSIS 공급 실측
      - 미래 또는 ONI 변경 시: 회귀/XGB 모델 예측
      반환:
        {
          "input":  {"year": 2030, "month": 8, "oni": 1.2},
          "is_simulated": true,    ← 2026년 이후 또는 ONI 실측과 다를 때 true
          "predicted": {
            "asos_temp": 28.4,     ← [예측] 서울 기준기온
            "alert_level": 4,      ← 공급 경보단계 (0=정상, 1=관심, 2=주의, 3=경계, 4=심각)
            "alert_label": "심각", ← 경보단계 한국어 레이블
            "oni_status": "엘니뇨", ← ONI 상태 (엘니뇨/라니냐/중립)
            "supply": {"supply_mw": 89500.0, "reserve_rate": 12.3},
            "regions": [
              {
                "gu": "강남구",
                "ta_gu": 29.8,
                "total_consumption_mwh": 4821.3,
                "usage": {                       ← 7개 용도별 소비량
                  "주택용": {"consumption_mwh": 412.3},
                  "산업용": {"consumption_mwh": 890.1},
                  ...
                },
                "building_type": {               ← 34개 건물유형별 수요감축 필요도
                  "공장": {"reduction_need_score": 4.57},
                  "교육연구시설": {"reduction_need_score": 0.58},
                  ...
                }
              }, ...
            ]
          }
        }

  GET  /predict/oni_range?year=2030&month=8
      [입력] year, month
      [예측] ONI -2.5 ~ +2.5 (0.1 간격). 과거 연월은 실측 ONI 지점을 배열에 삽입.
      - 실측 ONI 포인트: ASOS·KEPCO·EPSIS 관측값 (is_simulated=false)
      - 나머지 포인트: 모델 예측 (is_simulated=true)
      반환:
        {
          "input": {"year": 2030, "month": 8},
          "oni_range": [
            {
              "oni": -2.5,
              "asos_temp": 24.1,
              "supply_mw": 87000.0,
              "reserve_rate": 14.2,
              "alert_level": 4,
              "seoul_total_consumption_mwh": 125400.0,
              "regions": [
                {"gu": "강남구", "ta_gu": 25.0, "total_consumption_mwh": 4821.3},
                ...
              ]
            },
            ...총 51개
          ]
        }

  POST /blackout_simulation
      body: {year, month, oni}
      반환: {alert_level, alert_label, blackout_items: [...]}
"""

from __future__ import annotations

import calendar
import os

from dotenv import load_dotenv
from flask import Flask, jsonify, request

load_dotenv()

app = Flask(__name__)
app.json.sort_keys = False   # dict 삽입 순서 유지 (building_type score 내림차순 등)

# ---------------------------------------------------------------------------
# 모델/파라미터 로드 (서버 기동 시 1회)
# ---------------------------------------------------------------------------

_temp_model         = None
_consumption_model  = None
_consumption_encoders = None
_supply_model       = None
_monthly_offset: dict | None = None   # {(gu, month): offset_degC}
_ws_clim: dict | None = None          # {month: 평년풍속}


def _lazy_load() -> None:
    global _temp_model, _consumption_model, _consumption_encoders
    global _supply_model, _monthly_offset, _ws_clim

    if _temp_model is not None:
        return

    from python.train.temp_trend import load_model as load_temp
    from python.train.consumption_xgb import load_model as load_cons
    from python.train.supply_regression import load_model as load_supply
    from python.preprocess.gu_offset import load_gu_params

    _temp_model                             = load_temp()
    _consumption_model, _consumption_encoders = load_cons()
    _supply_model                           = load_supply()
    _monthly_offset, _ws_clim              = load_gu_params()


# ---------------------------------------------------------------------------
# 헬퍼
# ---------------------------------------------------------------------------

# 실측 ONI (서버 기동 시 1회 로드)
_oni_actual: dict[tuple[int, int], float] | None = None

def _get_oni_actual() -> dict[tuple[int, int], float]:
    global _oni_actual
    if _oni_actual is None:
        from python.loader.oni_loader import load_oni
        oni_df = load_oni()
        _oni_actual = {
            (int(r["year"]), int(r["month"])): float(r["oni"])
            for _, r in oni_df.iterrows()
        }
    return _oni_actual


def _is_simulated(year: int, month: int, oni: float) -> bool:
    """
    입력값이 실제 과거 기록과 다른 '시뮬레이션'인지 판별.
    - 미래(데이터 없는 연월): True
    - 과거인데 ONI를 실측과 다르게 설정: True
    - 과거 실측 ONI 그대로: False
    """
    actual = _get_oni_actual()
    if (year, month) not in actual:
        return True  # 미래
    return bool(abs(float(actual[(year, month)]) - oni) > 0.05)


def _resolve_oni(year: int, month: int, oni: float) -> float:
    """과거 실측 ONI가 있으면 그 값을 사용 (슬라이더 초기 로드·실측 모드)."""
    actual = _get_oni_actual().get((year, month))
    if actual is not None and not _is_simulated(year, month, oni):
        return round(actual, 2)
    return oni


def _resolve_month_result(year: int, month: int, oni_input: float) -> tuple[bool, float, dict]:
    """
    실측 ONI → 관측 데이터 전체, 시뮬 ONI → 모델 예측.
    반환: (is_simulated, oni_for_model, result_dict)
    """
    from python.loader.observed_month import build_observed_month

    simulated     = _is_simulated(year, month, oni_input)
    oni_for_model = _resolve_oni(year, month, oni_input)

    if not simulated:
        observed = build_observed_month(year, month)
        if observed is not None:
            return bool(simulated), float(oni_for_model), observed

    return bool(simulated), float(oni_for_model), _predict_one_month(year, month, oni_for_model)


def _build_oni_range_values(year: int, month: int) -> list[float]:
    """ONI -2.5~+2.5 (0.1). 과거 실측 ONI가 있으면 가장 가까운 격자점을 실측값으로 치환."""
    import numpy as np

    values = [round(v, 1) for v in np.arange(-2.5, 2.6, 0.1)]
    actual = _get_oni_actual().get((year, month))
    if actual is None:
        return values

    actual_r = round(actual, 2)
    idx = min(range(len(values)), key=lambda i: abs(values[i] - actual_r))
    if abs(values[idx] - actual_r) <= 0.05:
        values[idx] = actual_r
    return values


def _oni_status(oni: float) -> str:
    """ONI 값 → 엘니뇨/라니냐/중립 판별 (±0.5 기준)."""
    if oni >= 0.5:
        return "엘니뇨"
    elif oni <= -0.5:
        return "라니냐"
    else:
        return "중립"


def _asos_cdd_hdd(ta_asos: float, year: int, month: int) -> tuple[float, float]:
    """ASOS 예측 기온 → 월간 CDD/HDD (공급량 모델 입력용)."""
    days = calendar.monthrange(year, month)[1]
    cdd  = max(0.0, ta_asos - 24.0) * days
    hdd  = max(0.0, 18.0 - ta_asos) * days
    return round(cdd, 2), round(hdd, 2)


def _predict_one_month(year: int, month: int, oni: float) -> dict:
    """
    단일 (year, month, oni) → 예측 결과 dict.
    /predict 와 /predict/oni_range 양쪽에서 공용 사용.
    cdd/hdd는 내부 연산에만 사용하고 출력에서 제외.
    """
    from python.train.temp_trend import predict as pred_temp
    from python.train.supply_regression import predict as pred_supply
    from python.train.consumption_xgb import predict_all_districts
    from python.preprocess.gu_offset import predict_gu_temp
    from python.loader.mapping_loader import get_building_score_map

    USAGE_ORDER = ["가로등", "교육용", "농사용", "산업용", "심야", "일반용", "주택용"]
    building_score_map = get_building_score_map(year, month)

    ta_asos            = pred_temp(year, month, oni, _temp_model)
    cdd_asos, hdd_asos = _asos_cdd_hdd(ta_asos, year, month)   # 내부 연산용
    supply_info        = pred_supply(year, month, oni, cdd_asos, hdd_asos, _supply_model)
    gu_temps           = predict_gu_temp(ta_asos, month, _monthly_offset, multiplier=1.0)
    cons_df            = predict_all_districts(
        year=year, month=month, oni=oni,
        gu_temps=gu_temps, usage_types=USAGE_ORDER,
        model=_consumption_model, encoders=_consumption_encoders,
    )

    regions = []
    for gu, ta_gu in sorted(gu_temps.items()):   # 가나다순
        gu_rows = cons_df[cons_df["district"] == gu]

        usage_dict = {
            ut: {"consumption_mwh": round(float(
                gu_rows.loc[gu_rows["usage_type"] == ut, "consumption_mwh"].values[0]
            ), 2)}
            for ut in USAGE_ORDER
            if ut in gu_rows["usage_type"].values
        }

        total_mwh = round(float(gu_rows["consumption_mwh"].sum()), 2)

        bt_scores = building_score_map.get(gu, {})
        building_type_dict = {
            bt: {"reduction_need_score": round(score, 4)}
            for bt, score in sorted(bt_scores.items())
        }

        regions.append({
            "gu":                    gu,
            "ta_gu":                 round(ta_gu, 2),
            "total_consumption_mwh": total_mwh,
            "usage":                 usage_dict,
            "building_type":         building_type_dict,
        })

    return {
        "asos_temp": round(ta_asos, 2),
        "supply": {
            "supply_mw":    round(supply_info["supply_mw"], 1),
            "reserve_rate": round(supply_info["reserve_rate"], 2),
        },
        "regions": regions,
    }


# ---------------------------------------------------------------------------
# 엔드포인트
# ---------------------------------------------------------------------------

@app.get("/health")
def health():
    return jsonify({"status": "ok"})


@app.get("/oni")
def get_oni():
    """
    슬라이더 초기값 조회.  GET /oni?year=2025&month=8

    [입력] year, month
    [반환] {"year": 2025, "month": 8, "oni": 1.37}
      - 과거(실측 ONI 존재): 실측값 반환
      - 미래(데이터 없음):   0.0 반환
    """
    try:
        year  = int(request.args["year"])
        month = int(request.args["month"])
    except (KeyError, ValueError) as e:
        return jsonify({"error": f"파라미터 오류: {e}"}), 400

    actual  = _get_oni_actual()
    oni_val = actual.get((year, month))
    return jsonify({
        "input":  {"year": year, "month": month},
        "output": {"oni": round(oni_val, 2) if oni_val is not None else 0.0},
    })


@app.get("/predict")
def predict():
    """
    단일 월 예측.  GET /predict?year=2030&month=8&oni=1.2
    """
    _lazy_load()
    try:
        year      = int(request.args["year"])
        month     = int(request.args["month"])
        oni_input = float(request.args["oni"])
    except (KeyError, ValueError) as e:
        return jsonify({"error": f"파라미터 오류: {e}"}), 400

    from python.simulation.alert_level import get_alert_level

    simulated, oni_for_model, base = _resolve_month_result(year, month, oni_input)
    alert = get_alert_level(base["supply"]["reserve_rate"])

    predicted = {
        "asos_temp":   base["asos_temp"],
        "alert_level": int(alert),
        "alert_label": alert.label_ko,
        "oni_status":  _oni_status(oni_for_model),
        "supply":      base["supply"],
        "regions":     base["regions"],
    }

    return jsonify({
        "input":        {"year": year, "month": month, "oni": oni_for_model},
        "is_simulated": bool(simulated),
        "predicted":    predicted,
    })


@app.get("/predict/oni_range")
def predict_oni_range():
    """
    ONI 범위 그래프용. GET /predict/oni_range?year=2030&month=8

    /predict 와 동일: 실측 ONI 격자점 → 관측 데이터, 나머지 → 모델 예측.
    """
    try:
        year  = int(request.args["year"])
        month = int(request.args["month"])
    except (KeyError, ValueError) as e:
        return jsonify({"error": f"파라미터 오류: {e}"}), 400

    from python.loader.observed_month import build_observed_month
    from python.simulation.alert_level import get_alert_level

    oni_values     = _build_oni_range_values(year, month)
    observed_month = build_observed_month(year, month)
    actual_oni     = _get_oni_actual().get((year, month))

    if any(_is_simulated(year, month, v) for v in oni_values):
        _lazy_load()

    oni_range_data = []
    for oni_val in oni_values:
        simulated = _is_simulated(year, month, oni_val)
        if not simulated and observed_month is not None:
            r = observed_month
        else:
            r = _predict_one_month(year, month, oni_val)

        seoul_total = round(sum(reg["total_consumption_mwh"] for reg in r["regions"]), 2)
        alert = get_alert_level(r["supply"]["reserve_rate"])
        oni_range_data.append({
            "oni":                          float(oni_val),
            "is_simulated":                 bool(simulated),
            "asos_temp":                    float(r["asos_temp"]),
            "supply_mw":                    float(r["supply"]["supply_mw"]),
            "reserve_rate":                 float(r["supply"]["reserve_rate"]),
            "alert_level":                  int(alert),
            "alert_label":                  alert.label_ko,
            "seoul_total_consumption_mwh":  float(seoul_total),
            "regions": [
                {
                    "gu":                    reg["gu"],
                    "ta_gu":                 float(reg["ta_gu"]),
                    "total_consumption_mwh": float(reg["total_consumption_mwh"]),
                }
                for reg in r["regions"]
            ],
        })

    return jsonify({
        "input": {
            "year":       year,
            "month":      month,
            "actual_oni": round(actual_oni, 2) if actual_oni is not None else None,
        },
        "oni_range": oni_range_data,
    })


@app.post("/blackout_simulation")
def blackout_simulation():
    """
    POST /blackout_simulation  body: {year, month, oni}

    단계별 blackout_items:
      정상/관심 : 빈 리스트
      주의      : 공공시설(의료·발전·공공용 등) 경고 목록 (is_public=true)
      경계/심각 : reduction_need_score 내림차순 감축 목표 달성까지 순차 나열
    """
    _lazy_load()
    data = request.get_json(force=True)

    try:
        year      = int(data["year"])
        month     = int(data["month"])
        oni_input = float(data["oni"])
    except (KeyError, ValueError) as e:
        return jsonify({"error": f"파라미터 오류: {e}"}), 400

    from python.simulation.alert_level import get_alert_level
    from python.simulation.blackout import run_blackout
    from python.loader.mapping_loader import get_building_score_map

    simulated, oni_for_model, predicted = _resolve_month_result(year, month, oni_input)
    supply_info = predicted["supply"]
    alert       = get_alert_level(supply_info["reserve_rate"])

    bldg_score = get_building_score_map(year, month)
    result     = run_blackout(predicted["regions"], bldg_score, alert)

    # blackout_items을 구별로 그룹핑 (순서 유지)
    items_by_gu: dict[str, list] = {}
    for item in result["blackout_items"]:
        items_by_gu.setdefault(item["gu"], []).append({
            "building_type":        item["building_type"],
            "reduction_need_score": item["reduction_need_score"],
        })

    # 구별 ta_gu · 소비량 조회
    ta_by_gu = {r["gu"]: round(r["ta_gu"], 2) for r in predicted["regions"]}
    consumption_by_gu = {
        r["gu"]: round(r["total_consumption_mwh"], 2) for r in predicted["regions"]
    }

    districts_order = [
        {
            "gu":                    gu,
            "ta_gu":                 ta_by_gu.get(gu, 0.0),
            "total_consumption_mwh": consumption_by_gu.get(gu, 0.0),
            "blackout_items":        items_by_gu.get(gu, []),
        }
        for gu in result["districts_order"]
    ]

    return jsonify({
        "input":              {"year": year, "month": month, "oni": oni_for_model},
        "is_simulated":       bool(simulated),
        "alert_level":        int(alert),
        "alert_label":        alert.label_ko,
        "supply_mw":          supply_info["supply_mw"],
        "reserve_rate":       supply_info["reserve_rate"],
        "districts_affected": result["districts_affected"],
        "districts_order":    districts_order,
    })

## --------- 추가 ------------
@app.get("/weather/current")
def weather_current():
    """
    실시간 서울 기상 데이터.
    GET /weather/current
    GET /weather/current?stn=108
    """
    try:
        stn = int(request.args.get("stn", 108))
    except ValueError:
        return jsonify({"error": "stn은 숫자여야 합니다."}), 400

    try:
        from python.loader.current_weather_loader import load_current_weather

        weather = load_current_weather()

        return jsonify({
            "status": "ok",
            "weather": weather
        })

    except Exception as e:
        return jsonify({
            "status": "error",
            "message": str(e)
        }), 500


if __name__ == "__main__":
    port = int(os.getenv("FLASK_PORT", 5001))
    app.run(host="0.0.0.0", port=port, debug=True)


