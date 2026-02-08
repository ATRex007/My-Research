using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class MassDistributor : EditorWindow
{
    private GameObject rootObject;
    private float totalTargetMass = 3000f; 
    
    // --- 密度の設定項目 ---
    // Hips/Root: 内臓と筋肉の塊 (一番重い)
    private float rootDensityFactor = 1.1f; 
    // Spine/Chest: 肺と気嚢 (軽い)
    private float spineDensityFactor = 0.7f; 
    // Head/Neck: 中空構造 (軽い)
    private float headNeckDensityFactor = 0.6f;
    
    // 【分離】Limbs: 後肢・前肢 (筋肉の塊なので重い)
    private float limbDensityFactor = 1.1f; 
    // 【分離】Tail: バランサー (少し軽く調整できるようにする)
    private float tailDensityFactor = 0.9f; 

    [MenuItem("Tools/Dinosaur Mass Distributor")]
    public static void ShowWindow()
    {
        GetWindow<MassDistributor>("Mass Distributor");
    }

    void OnGUI()
    {
        GUILayout.Label("恐竜の質量自動配分ツール (尾・四肢分離版)", EditorStyles.boldLabel);
        
        rootObject = (GameObject)EditorGUILayout.ObjectField("Root Object", rootObject, typeof(GameObject), true);
        totalTargetMass = EditorGUILayout.FloatField("Total Mass (kg)", totalTargetMass);
        
        GUILayout.Space(10);
        GUILayout.Label("部位ごとの密度調整 (1.0 = 水と同じ)", EditorStyles.boldLabel);
        
        rootDensityFactor = EditorGUILayout.Slider("Root (骨盤・内臓)", rootDensityFactor, 0.5f, 1.5f);
        spineDensityFactor = EditorGUILayout.Slider("Spine (胸部・肺)", spineDensityFactor, 0.1f, 1.5f);
        headNeckDensityFactor = EditorGUILayout.Slider("Head/Neck (頭首)", headNeckDensityFactor, 0.1f, 1.5f);
        
        GUILayout.Space(5);
        // ここを分けました
        limbDensityFactor = EditorGUILayout.Slider("Limbs (手足)", limbDensityFactor, 0.5f, 1.5f);
        tailDensityFactor = EditorGUILayout.Slider("Tail (尻尾)", tailDensityFactor, 0.1f, 1.5f);

        GUILayout.Space(20);

        if (GUILayout.Button("質量を計算して適用する"))
        {
            CalculateAndApplyMass();
        }
    }

    void CalculateAndApplyMass()
    {
        if (rootObject == null)
        {
            Debug.LogError("ルートオブジェクトを選択してください！");
            return;
        }

        ArticulationBody[] allBodies = rootObject.GetComponentsInChildren<ArticulationBody>();
        
        float totalWeightedVolume = 0f;
        List<BodyData> bodyDataList = new List<BodyData>();

        foreach (var ab in allBodies)
        {
            float myVolume = GetVolumeForArticulationBody(ab);

            if (myVolume <= 0.0001f) continue;

            // --- 名前判定ロジック ---
            float density = limbDensityFactor; // デフォルトは手足
            string name = ab.name.ToLower();

            if (name.Contains("hip") || name.Contains("pelvis") || name.Contains("root") || name.Contains("cog"))
            {
                density = rootDensityFactor;
            }
            else if (name.Contains("spine") || name.Contains("chest") || name.Contains("torso") || name.Contains("body"))
            {
                density = spineDensityFactor;
            }
            else if (name.Contains("head") || name.Contains("neck") || name.Contains("jaw"))
            {
                density = headNeckDensityFactor;
            }
            else if (name.Contains("tail")) // 尻尾を判定
            {
                density = tailDensityFactor;
            }
            // それ以外（Thigh, Knee, Ankle, Foot, Armなど）は Limbs

            float weightedVolume = myVolume * density;
            totalWeightedVolume += weightedVolume;
            
            bodyDataList.Add(new BodyData { 
                articulationBody = ab, 
                weightedVolume = weightedVolume,
                debugDensity = density
            });
        }

        if (totalWeightedVolume <= 0) return;

        Undo.RecordObjects(allBodies, "Apply Mass");

        Debug.Log("--- 質量配分結果 ---");
        foreach (var data in bodyDataList)
        {
            float ratio = data.weightedVolume / totalWeightedVolume;
            float assignedMass = totalTargetMass * ratio;
            
            data.articulationBody.mass = assignedMass;
            
            Debug.Log($"[{data.articulationBody.name}] Density: {data.debugDensity:F1} -> Mass: {assignedMass:F2} kg ({ratio*100:F1}%)");
        }

        Debug.Log($"<color=green>完了！尾と四肢を別々に計算しました。</color>");
    }

    float GetVolumeForArticulationBody(ArticulationBody targetAb)
    {
        float totalVol = 0f;
        Collider[] childColliders = targetAb.GetComponentsInChildren<Collider>();

        foreach (var col in childColliders)
        {
            ArticulationBody parentAb = col.GetComponentInParent<ArticulationBody>();
            if (parentAb == targetAb)
            {
                totalVol += CalculateSingleColliderVolume(col);
            }
        }
        return totalVol;
    }

    float CalculateSingleColliderVolume(Collider col)
    {
        Vector3 scale = col.transform.lossyScale;
        
        if (col is BoxCollider box)
        {
            return (box.size.x * scale.x) * (box.size.y * scale.y) * (box.size.z * scale.z);
        }
        else if (col is CapsuleCollider capsule)
        {
            float heightScale = scale.y;
            float radiusScaleMax = Mathf.Max(scale.x, scale.z);

            if (capsule.direction == 0) { heightScale = scale.x; radiusScaleMax = Mathf.Max(scale.y, scale.z); }
            else if (capsule.direction == 2) { heightScale = scale.z; radiusScaleMax = Mathf.Max(scale.x, scale.y); }

            float radius = capsule.radius * radiusScaleMax;
            float height = capsule.height * heightScale;
            float cylinderHeight = Mathf.Max(0, height - (2 * radius));
            
            return (Mathf.PI * radius * radius * cylinderHeight) + ((4f / 3f) * Mathf.PI * radius * radius * radius);
        }
        else if (col is SphereCollider sphere)
        {
            float radius = sphere.radius * Mathf.Max(scale.x, scale.y, scale.z);
            return (4f / 3f) * Mathf.PI * radius * radius * radius;
        }
        return 0f;
    }

    struct BodyData
    {
        public ArticulationBody articulationBody;
        public float weightedVolume;
        public float debugDensity;
    }
}