using System;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

public class CurrentDataTestUI : MonoBehaviour
{
    [Header("Current Data")]
    [SerializeField] private TMP_Text TMP_temp; // 기온
    // [SerializeField] private TMP_Text currentHum; // 습도
    // [SerializeField] private TMP_Text currentRainfall; // 강수량
    // [SerializeField] private TMP_Text currentWindSpeed; // 풍속

    [Header("Data Manager")]
    [SerializeField] private DataManager dataManager;

    private void OnEnable()
    {
        if (dataManager == null) return;
        dataManager.OnCurrentDataUpdated += DataUpdated; // 실시간 데이터 업데이트 시 화면상 데이터도 변경될 수 있도록

    }
    private void OnDisable()
    {
        if (dataManager == null) return;
        dataManager.OnCurrentDataUpdated -= DataUpdated;
    }

    public void DataUpdated(JObject weather)
    {
        TMP_temp.text = $"{weather["temperature"]}°C";
        // currentHum.text = $"{weather["humidity"]}%";
        // currentRainfall.text = weather["rainfall"]?.ToString();
        // currentWindSpeed.text = $"{weather["wind_speed"]}m/s";
    }
}
