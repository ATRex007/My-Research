using UnityEngine;
using System.Collections.Generic;

public class ShowCenterOfMass : MonoBehaviour
{
    [Header("設定")]
    [Tooltip("重心計算の対象となるルートオブジェクト（空ならこのオブジェクト自身）")]
    public GameObject targetRoot;
    
    [Tooltip("重心マーカーの色")]
    public Color markerColor = Color.red;
    
    [Tooltip("重心マーカーの大きさ")]
    public float markerSize = 0.5f;

    [Tooltip("地面への投影線を表示するか")]
    public bool showGroundProjection = true;

    // 内部計算用
    private Vector3 currentCoM;
    private float currentTotalMass;

    void OnDrawGizmos()
    {
        // プレイ中、またはエディタで対象が設定されている場合に描画
        CalculateCenterOfMass();
        DrawCoMMarkers();
    }

    void CalculateCenterOfMass()
    {
        GameObject root = targetRoot != null ? targetRoot : this.gameObject;
        
        // 子階層にある全てのArticulationBodyを取得
        ArticulationBody[] bodies = root.GetComponentsInChildren<ArticulationBody>();

        if (bodies.Length == 0) return;

        Vector3 weightedPositionSum = Vector3.zero;
        float totalMass = 0f;

        foreach (var body in bodies)
        {
            // ArticulationBodyの「ワールド空間での重心位置」を取得
            Vector3 bodyCoM = body.worldCenterOfMass;
            
            // 重み付き位置を加算 (位置 × 質量)
            weightedPositionSum += bodyCoM * body.mass;
            totalMass += body.mass;
        }

        if (totalMass > 0)
        {
            // 重心の公式: Σ(mi * ri) / Σmi
            currentCoM = weightedPositionSum / totalMass;
            currentTotalMass = totalMass;
        }
    }

    void DrawCoMMarkers()
    {
        if (currentTotalMass <= 0) return;

        Gizmos.color = markerColor;

        // 1. 重心位置に球体を描画
        Gizmos.DrawSphere(currentCoM, markerSize);

        // 2. 地面への投影線（バランス確認用）
        if (showGroundProjection)
        {
            Vector3 groundPoint = currentCoM;
            groundPoint.y = 0; // 地面の高さ（Y=0と仮定）

            // 線を引く
            Gizmos.DrawLine(currentCoM, groundPoint);
            
            // 地面地点に円盤を描く
            Gizmos.color = new Color(markerColor.r, markerColor.g, markerColor.b, 0.5f);
            Gizmos.DrawSphere(groundPoint, markerSize * 0.5f);
        }
    }
    
    // GUI（画面上の文字）で質量を表示したい場合
    void OnGUI()
    {
        // 画面左上に質量を表示
        GUI.color = Color.black;
        GUILayout.Label($"  Total Mass: {currentTotalMass:F1} kg");
    }
}