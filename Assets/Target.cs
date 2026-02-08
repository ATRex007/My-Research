using UnityEngine;

public class Target : MonoBehaviour
{
    [Tooltip("移動範囲の基準となる床オブジェクト")]
    public Transform field; 
    
    [Tooltip("床の端からどれくらい内側に配置するか")]
    public float padding = 4.0f; 

    // エージェントから呼ばれる関数
    public void RandomizePosition()
    {
        if (field == null)
        {
            // fieldが設定されていなければ、とりあえず今の位置の周辺で適当に動く
            float range = 15.0f;
            float x = Random.Range(-range, range);
            float z = Random.Range(-range, range);
            transform.localPosition = new Vector3(x, transform.localPosition.y, z);
            return;
        }

        // Field（床）の大きさを取得（PlaneならScale 1 = 10m、CubeならScale 1 = 1m）
        Bounds bounds = field.GetComponent<Renderer>().bounds;

        // 配置可能な範囲を計算 (端ギリギリにならないようにpaddingを引く)
        float minX = bounds.min.x + padding;
        float maxX = bounds.max.x - padding;
        float minZ = bounds.min.z + padding;
        float maxZ = bounds.max.z - padding;

        // ランダムな座標を生成
        float newX = Random.Range(minX, maxX);
        float newZ = Random.Range(minZ, maxZ);

        // 高さは変えずに移動
        transform.position = new Vector3(newX, transform.position.y, newZ);
    }
}