"""
과거 실측 ONI 연월용 — 모델 없이 관측 데이터만으로 /predict 응답 구조를 조립.

데이터 소스:
  - ASOS 월평균 기온 (서울 108)
  - temperature_gu_monthly.csv 구별 기온
  - EPSIS 월별 공급·예비율
  - KEPCO 매핑 용도별 소비량 + 건물유형 reduction_need_score
"""

from __future__ import annotations

from pathlib import Path

import pandas as pd

USAGE_ORDER = ["가로등", "교육용", "농사용", "산업용", "심야", "일반용", "주택용"]

ROOT = Path(__file__).resolve().parents[2]
TEMP_GU_CSV = ROOT / "data" / "output" / "temperature_gu_monthly.csv"

_asos_monthly: dict[tuple[int, int], float] | None = None
_temp_gu: dict[tuple[int, int, str], float] | None = None
_supply_monthly: dict[tuple[int, int], dict[str, float]] | None = None


def _load_asos_monthly() -> dict[tuple[int, int], float]:
    global _asos_monthly
    if _asos_monthly is None:
        from python.loader.asos_daily import load_monthly

        _asos_monthly = {
            (int(r["year"]), int(r["month"])): float(r["ta_mean"])
            for _, r in load_monthly().iterrows()
        }
    return _asos_monthly


def _load_temp_gu() -> dict[tuple[int, int, str], float]:
    global _temp_gu
    if _temp_gu is None:
        df = pd.read_csv(TEMP_GU_CSV, encoding="utf-8-sig")
        gu_col = "gu" if "gu" in df.columns else "district"
        _temp_gu = {
            (int(r["year"]), int(r["month"]), str(r[gu_col]).strip()): float(r["ta_mean"])
            for _, r in df.iterrows()
        }
    return _temp_gu


def _load_supply_monthly() -> dict[tuple[int, int], dict[str, float]]:
    global _supply_monthly
    if _supply_monthly is None:
        from python.loader.supply_loader import load_monthly

        _supply_monthly = {
            (int(r["year"]), int(r["month"])): {
                "supply_mw":    float(r["supply_mw_mean"]),
                "reserve_rate": float(r["reserve_rate_mean"]),
            }
            for _, r in load_monthly().iterrows()
        }
    return _supply_monthly


def has_observed_month(year: int, month: int) -> bool:
    """실측 기온·공급·구별 소비가 모두 있는 연월인지."""
    key = (year, month)
    if key not in _load_asos_monthly():
        return False
    if key not in _load_supply_monthly():
        return False

    from python.loader.mapping_loader import get_usage_mwh_map

    usage = get_usage_mwh_map(year, month)
    if not usage:
        return False

    gu_count = sum(1 for (y, m, _) in _load_temp_gu() if y == year and m == month)
    return gu_count >= 20


def build_observed_month(year: int, month: int) -> dict | None:
    """
    _predict_one_month() 와 동일한 dict 구조.
    데이터가 부족하면 None.
    """
    if not has_observed_month(year, month):
        return None

    from python.loader.mapping_loader import get_building_score_map, get_usage_mwh_map

    asos_temp = round(_load_asos_monthly()[(year, month)], 2)
    supply    = _load_supply_monthly()[(year, month)]
    usage_map = get_usage_mwh_map(year, month)
    building_score_map = get_building_score_map(year, month)
    temp_gu   = _load_temp_gu()

    districts = sorted({
        gu
        for (y, m, gu) in temp_gu
        if y == year and m == month
    })
    if not districts:
        districts = sorted({d for (d, _) in usage_map})

    regions = []
    for gu in districts:
        ta_gu = temp_gu.get((year, month, gu))
        if ta_gu is None:
            continue

        usage_dict: dict[str, dict[str, float]] = {}
        for ut in USAGE_ORDER:
            mwh = usage_map.get((gu, ut))
            if mwh is not None:
                usage_dict[ut] = {"consumption_mwh": round(float(mwh), 2)}

        if not usage_dict:
            continue

        total_mwh = round(sum(u["consumption_mwh"] for u in usage_dict.values()), 2)
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

    if len(regions) < 20:
        return None

    return {
        "asos_temp": round(asos_temp, 2),
        "supply": {
            "supply_mw":    round(supply["supply_mw"], 1),
            "reserve_rate": round(supply["reserve_rate"], 2),
        },
        "regions": regions,
    }
