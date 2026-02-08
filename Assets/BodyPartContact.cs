using UnityEngine;

public class BodyPartContact : MonoBehaviour
{
    private Parasaurolophus agent;

    void Start()
    {
        agent = GetComponentInParent<Parasaurolophus>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (agent == null) return;

        // 1. 地面に触れたら「転倒」
        if (collision.gameObject.CompareTag("field"))
        {
            // 自分の名前 (gameObject.name) を引数に渡す
            agent.ReportFall(gameObject.name);
        }
    }
}