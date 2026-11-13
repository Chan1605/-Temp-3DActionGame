using UnityEngine;

// 구역을 돌파하면 출구 쪽 목표 표식(나침반 + 거리 표시)을 켜고, 다음 구역에 다가가면 끔
// [변경] 출구 문에 다다르면 표식을 다음 구역 입구로 옮김 (문을 지난 뒤 표식이 등 뒤에 남아 있던 문제)
public class ZoneObjective : MonoBehaviour
{
    [SerializeField] ZoneArena arena;
    [SerializeField] GameObject marker; // HUDNavigationElement가 붙은 오브젝트. 꺼 두었다가 켜면 그때 표식이 만들어짐
    [SerializeField] float handoffDistance = 8f; // [변경] 출구 표식과 이 거리 안이면 다음 입구로 넘김

    Transform player;
    Vector3 nextEntrance;
    bool hasNext, handedOff;

    void Awake()
    {
        if (arena == null) arena = GetComponent<ZoneArena>();
        if (marker != null) marker.SetActive(false);
    }

    void Start()
    {
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
        hasNext = FindNextEntrance(out nextEntrance);
    }

    void OnEnable()
    {
        if (arena != null) arena.Cleared += Show;
        ZoneArena.AnyEntered += OnZoneEntered;
    }

    void OnDisable()
    {
        if (arena != null) arena.Cleared -= Show;
        ZoneArena.AnyEntered -= OnZoneEntered;
    }

    void Show()
    {
        if (marker != null) marker.SetActive(true);
    }

    void OnZoneEntered(ZoneArena zone)
    {
        if (zone != arena && marker != null) marker.SetActive(false);
    }

    void Update()
    {
        if (handedOff || !hasNext || player == null || marker == null || !marker.activeSelf) return;
        Vector3 d = player.position - marker.transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > handoffDistance * handoffDistance) return;

        // 껐다 켜야 내비게이션 표식이 새 자리로 다시 만들어짐
        marker.SetActive(false);
        marker.transform.position = nextEntrance;
        marker.SetActive(true);
        handedOff = true;
    }

    // 다음 구역 = 이 구역 출구 문에서 가장 가까운 다른 구역의 입구 문
    bool FindNextEntrance(out Vector3 position)
    {
        position = Vector3.zero;
        if (arena == null) return false;
        Transform exit = arena.transform.Find("Generated/Gate_Exit");
        if (exit == null) return false;

        float best = float.MaxValue;
        foreach (ZoneArena other in FindObjectsByType<ZoneArena>(FindObjectsSortMode.None))
        {
            if (other == arena) continue;
            Transform entrance = other.transform.Find("Generated/Gate_Entrance");
            if (entrance == null) continue;
            float dist = (entrance.position - exit.position).sqrMagnitude;
            if (dist >= best) continue;
            best = dist;
            position = entrance.position + Vector3.up * 2.5f + Vector3.back * 2f; // 출구 표식과 같은 높이, 문 바로 바깥
        }
        return best < float.MaxValue;
    }
}
