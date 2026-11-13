using UnityEngine;

// 플레이어가 이 지점 근처(반경)에 들어오면 다음 씬으로 (물리 트리거 없이 거리로 확인)
// spawnAt: 다음 씬에서 플레이어를 옮길 오브젝트 경로 (예: Zone_4_Boss/Generated/RespawnPoint)
//        setFlag: 넘어가기 전에 RunState에 남길 표시 · skipIfFlag: 그 표시가 있으면 이 출구는 꺼짐 (던전은 한 번만)
public class SceneExit : MonoBehaviour
{
    [SerializeField] string sceneName = "InGame"; // [변경] 본편 씬 이름 변경
    [SerializeField] float radius = 3.5f;
    [SerializeField] string spawnAt = "";
    [SerializeField] string setFlag = "";
    [SerializeField] string skipIfFlag = "";

    Transform player;

    void Start()
    {
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
    }

    void Update()
    {
        if (player == null || SceneTransition.Busy || GamePause.IsPaused) return;
        if (!string.IsNullOrEmpty(skipIfFlag) && RunState.HasFlag(skipIfFlag)) return;
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > radius * radius) return;
        if (!string.IsNullOrEmpty(setFlag)) RunState.SetFlag(setFlag);
        SceneTransition.Load(sceneName, true, spawnAt);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
