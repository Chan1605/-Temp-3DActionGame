using UnityEngine;

// 플레이어가 이 지점 근처(반경)에 들어오면 다음 씬으로 (물리 트리거 없이 거리로 확인)
public class SceneExit : MonoBehaviour
{
    [SerializeField] string sceneName = "SampleScene";
    [SerializeField] float radius = 3.5f;

    Transform player;

    void Start()
    {
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
    }

    void Update()
    {
        if (player == null || SceneTransition.Busy || GamePause.IsPaused) return;
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude <= radius * radius) SceneTransition.Load(sceneName);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
