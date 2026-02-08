using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class FallStatsLogger : MonoBehaviour
{
    [Header("設定")]
    public string fileName = "FallStatistics.csv";
    
    [Tooltip("指定回数を超えたら自動停止するか (検証モード用)")]
    public bool stopAtMaxSamples = false;
    
    [Tooltip("自動停止する転倒回数")]
    public int maxSamples = 10000;

    [Tooltip("何回記録するごとにCSVを更新するか (0なら終了時のみ)")]
    public int saveInterval = 100;

    // 内部変数
    private Dictionary<string, int> fallCounts = new Dictionary<string, int>();
    private int totalFallCount = 0;

    // シングルトン的運用（シーンに1つあればOK）
    public static FallStatsLogger Instance { get; private set; }

    void Awake()
    {
        // どこからでもアクセスできるようにする
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void RecordFall(string partName)
    {
        // カウント加算
        if (fallCounts.ContainsKey(partName)) fallCounts[partName]++;
        else fallCounts.Add(partName, 1);

        totalFallCount++;

        // --- 1. 定期保存 (学習中のリアルタイム収集用) ---
        if (saveInterval > 0 && totalFallCount % saveInterval == 0)
        {
            SaveToCsv();
        }

        // --- 2. 自動停止 (検証モード用) ---
        if (stopAtMaxSamples && totalFallCount >= maxSamples)
        {
            Debug.Log($"<color=yellow>目標サンプル数 ({maxSamples}) に達しました。集計を終了します。</color>");
            SaveToCsv();
            QuitUnity();
        }
    }

    void OnApplicationQuit()
    {
        SaveToCsv();
    }

    public void SaveToCsv()
    {
        if (fallCounts.Count == 0) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"TotalFalls,{totalFallCount}"); // 総数も記録
        sb.AppendLine("PartName,FallCount,Percentage");

        var sortedStats = fallCounts.OrderByDescending(x => x.Value);

        foreach (var stat in sortedStats)
        {
            double percent = (double)stat.Value / totalFallCount * 100.0;
            sb.AppendLine($"{stat.Key},{stat.Value},{percent:F2}%");
        }

        string path = Path.Combine(Application.dataPath, fileName);
        // 書き込みエラー防止（ファイルが開かれている場合など）
        try
        {
            File.WriteAllText(path, sb.ToString());
            // 学習中はログがうるさいので、定期保存時はログを出さない
            // Debug.Log($"転倒統計保存: {totalFallCount}件"); 
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"CSV保存失敗: {e.Message}");
        }
    }

    void QuitUnity()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}