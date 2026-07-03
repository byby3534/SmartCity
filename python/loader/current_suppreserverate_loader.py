import os
import requests
import pandas as pd
import xml.etree.ElementTree as ET
from dotenv import load_dotenv

load_dotenv()

POWER_API_KEY = os.getenv("POWER_API_KEY")
POWER_URL = "https://openapi.kpx.or.kr/openapi/sukub5mMaxDatetime/getSukub5mMaxDatetime"


def current_suppreserverate_loader():
    params = {
        "serviceKey": POWER_API_KEY,
        "numOfRows": 1   # 최신 1건 요청 (API 기본 정렬 기준에 따름)
    }

    response = requests.get(POWER_URL, params=params, timeout=10)
    response.raise_for_status()

    root = ET.fromstring(response.content)

    result_code = root.findtext(".//resultCode")
    result_msg = root.findtext(".//resultMsg")

    if result_code and result_code != "00":
        raise ValueError(f"API error {result_code}: {result_msg}")

    items = root.findall(".//item")

    if not items:
        raise ValueError("전력거래소 응답에 데이터가 없습니다.")

    rows = []

    for item in items:
        rows.append({
            child.tag: child.text
            for child in item
        })

    df = pd.DataFrame(rows)

    df["suppReserveRate"] = pd.to_numeric(
        df["suppReserveRate"],
        errors="coerce"
    )

    df["baseDatetime"] = pd.to_datetime(
        df["baseDatetime"],
        format="%Y%m%d%H%M%S",
        errors="coerce"
    )

    # 혹시 여러 건이 오더라도 최신 데이터 사용
    df = df.sort_values("baseDatetime", ascending=False)

    row = df.iloc[0]

    return {
        "baseDatetime": None if pd.isna(row["baseDatetime"]) else row["baseDatetime"].strftime("%Y-%m-%d %H:%M:%S"),
        "suppReserveRate": None if pd.isna(row["suppReserveRate"]) else float(row["suppReserveRate"]),
    }