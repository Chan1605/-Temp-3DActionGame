using UnityEngine;

// 구역을 돌파하면 출구 쪽 목표 표식(나침반 + 거리 표시)을 켜고, 다음 구역에 다가가면 끔
public class ZoneObjective : MonoBehaviour
{
    [SerializeField] ZoneArena arena;
    [SerializeField] GameObject marker; // HUDNavigationElement가 붙은 오브젝트. 꺼 두었다가 켜면 그때 표식이 만들어짐

    void Awake()
    {
        if (arena == null) arena = GetComponent<ZoneArena>();
        if (marker != null) marker.SetActive(false);
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
}
