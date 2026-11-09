using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 실행하면 자동으로 붙는 구역 컬링 (씬 배치·셋업 불필요, 구역을 다시 생성해도 그대로 동작)
//  - 카메라에서 먼 구역의 소품(나무·바위·풀·파티클)과 지형 그리기를 끔 → 안개 너머 수백 개 렌더러·파티클 비용 제거
//  - 풀·돌길은 그림자를 끔 (작아서 티가 안 나는데 그림자 캐스케이드마다 다시 그려짐)
public class ZoneCulling : MonoBehaviour
{
    const string GeneratedName = "Generated"; // ZoneGenerator가 만드는 자식 이름
    static readonly string[] PropGroups = { "Trees", "Cover", "WallRocks", "InnerRocks", "PathStones", "Landmarks", "Particles" };
    static readonly string[] NoShadowGroups = { "Cover", "PathStones" };

    [SerializeField] float keepDistance = 60f;  // 구역 가장자리까지 이 거리 안이면 켬
    [SerializeField] float hideMargin = 10f;    // 경계에서 깜빡이지 않게 끌 때는 조금 더 멀어져야 끔
    [SerializeField] float checkInterval = 0.25f;

    class Zone
    {
        public string name;
        public Rect area;
        public Terrain terrain;
        public readonly List<GameObject> groups = new List<GameObject>();
        public bool visible = true;
    }

    struct Pending
    {
        public Zone zone;
        public GameObject go;
    }

    readonly List<Zone> zones = new List<Zone>();
    readonly Queue<Pending> pending = new Queue<Pending>();
    Transform cam;
    float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindObjectsByType<ZoneArena>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0) return;
        new GameObject("ZoneCulling").AddComponent<ZoneCulling>();
    }

    void Start()
    {
        foreach (ZoneArena arena in FindObjectsByType<ZoneArena>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Transform generated = arena.transform.Find(GeneratedName);
            if (generated == null) continue;

            Terrain terrain = generated.GetComponentInChildren<Terrain>(true);
            if (terrain == null || terrain.terrainData == null) continue;

            Vector3 p = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            var zone = new Zone { name = arena.name, area = new Rect(p.x, p.z, size.x, size.z), terrain = terrain };

            foreach (string groupName in PropGroups)
            {
                Transform group = generated.Find(groupName);
                if (group != null) zone.groups.Add(group.gameObject);
            }
            foreach (string groupName in NoShadowGroups)
            {
                Transform group = generated.Find(groupName);
                if (group == null) continue;
                foreach (Renderer r in group.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = ShadowCastingMode.Off;
            }
            zones.Add(zone);
        }

        Refresh(true); // 시작할 때는 먼 구역을 한 번에 끔
    }

    void Update()
    {
        // 켜기는 한 프레임에 한 묶음씩 → 구역 경계에서 수백 개가 한꺼번에 켜지며 생기는 끊김 분산
        if (pending.Count > 0)
        {
            Pending p = pending.Dequeue();
            // 기다리는 사이 다시 멀어졌으면 켜지 않음
            if (p.zone.visible && p.go != null && !p.go.activeSelf) p.go.SetActive(true);
        }

        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = checkInterval;
        Refresh(false);
    }

    void Refresh(bool immediate)
    {
        if (cam == null)
        {
            Camera main = Camera.main;
            if (main == null) return;
            cam = main.transform;
        }

        Vector3 c = cam.position;
        foreach (Zone zone in zones)
        {
            float distance = DistanceToArea(zone.area, c.x, c.z);
            bool want = zone.visible ? distance <= keepDistance + hideMargin : distance <= keepDistance;
            if (want == zone.visible) continue;

            zone.visible = want;
            if (zone.terrain != null) zone.terrain.enabled = want; // 지형 그리기만 끔 (TerrainCollider는 그대로)

            // 카메라가 이미 구역 안이면(재시작·순간이동) 기다리지 않고 바로 켬
            bool now = immediate || !want || distance <= 0f;
            foreach (GameObject group in zone.groups)
            {
                if (now) { if (group.activeSelf != want) group.SetActive(want); }
                else pending.Enqueue(new Pending { zone = zone, go = group });
            }
        }
    }

    static float DistanceToArea(Rect r, float x, float z)
    {
        float dx = Mathf.Max(r.xMin - x, 0f, x - r.xMax);
        float dz = Mathf.Max(r.yMin - z, 0f, z - r.yMax);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
