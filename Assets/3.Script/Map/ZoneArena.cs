using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 구역 진행: 플레이어가 아레나 안으로 들어오면 입구 결계를 닫고, 웨이브를 모두 처치하면 출구 결계를 연다
// 한 씬에 구역을 이어 붙이므로, 구역에 다가가면 하늘·안개·해를 이 구역 테마로 바꿈
// 웨이브 스폰 + 체크포인트: 전투 중 사망하면 입구 밖에서 부활하고 구역을 처음부터 다시 시작
public class ZoneArena : MonoBehaviour
{
    enum State { Waiting, Fighting, Cleared }

    [Serializable]
    public class Wave
    {
        public GameObject enemyPrefab;
        [Min(1)] public int count = 2;
    }

    [SerializeField] float radius = 16f;
    [SerializeField] float enterMargin = 2f;
    [SerializeField] GameObject entranceBarrier;
    [SerializeField] GameObject exitBarrier;
    [SerializeField] bool logState = true;

    // 웨이브: 플레이어 반대편 벽 근처에 부채꼴로 등장, 전부 쓰러지면 waveInterval 뒤 다음 웨이브
    [Header("Waves")]
    [SerializeField] List<Wave> waves = new List<Wave>();
    [SerializeField] float firstWaveDelay = 0.8f;
    [SerializeField] float waveInterval = 2f;
    [SerializeField] float spawnRadius = 10f; // [변경] 중심에서 등장 거리 (14m → 10m, 걸어오는 공백 줄임)
    [SerializeField] float spawnHold = 0.6f;  // [변경] 등장 직후 무방비 시간
    [SerializeField] float spawnSpread = 35f;

    [Header("Checkpoint")]
    [SerializeField] Transform respawnPoint;
    [SerializeField] float respawnDelay = 1.5f;

    [Header("Atmosphere")]
    [SerializeField] ZoneTheme theme;
    [SerializeField] float atmosphereRadius = 29f;
    [SerializeField] float atmosphereBlendTime = 1.5f;

    static ZoneArena activeAtmosphere; // 지금 분위기를 쥐고 있는 구역
    static Light sun;

    readonly List<EnemySwordsman> preplaced = new List<EnemySwordsman>(); // 씬에 미리 둔 적 (첫 웨이브 앞에 처리)
    readonly List<EnemySwordsman> spawned = new List<EnemySwordsman>();   // 웨이브로 만든 적 (재시작 시 삭제)
    readonly List<EnemySwordsman> current = new List<EnemySwordsman>();   // 지금 웨이브
    Transform player;
    PlayerHealth playerHealth;
    State state;
    int waveIndex;        // 다음에 낼 웨이브 번호
    float waveTimer;      // 0보다 크면 다음 웨이브까지 대기 중
    bool respawning;
    Coroutine atmosphereRoutine;

    public event Action Cleared;
    public event Action<int> WaveStarted; // 이후 UI용 (1부터)

    // ===== HUD용 =====
    // 어느 구역인지 몰라도 구독할 수 있게 정적 이벤트로 알림
    public static event Action<ZoneArena> AnyEntered;
    public static event Action<ZoneArena> AnyCleared;
    // [변경] 연출용 (ZoneFx): 적 등장 위치 / 웨이브 마지막 적이 쓰러진 위치 · 구역 마지막 웨이브인지
    public static event Action<Vector3> AnyEnemySpawned;
    public static event Action<ZoneArena, Vector3, bool> AnyWaveCleared;
    public static ZoneArena Fighting { get; private set; } // 지금 전투 중인 구역 (없으면 null)

    public string DisplayNumber => theme != null ? theme.displayNumber : "";
    public string DisplayName => theme != null && !string.IsNullOrEmpty(theme.displayName) ? theme.displayName : name;
    public int CurrentWave => waveIndex;
    public int WaveCount => waves.Count;
    public int AliveCount
    {
        get
        {
            int n = 0;
            foreach (EnemySwordsman e in current)
                if (e != null && e.CanBeTargeted) n++;
            return n;
        }
    }

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
            if (playerHealth != null) playerHealth.Died += OnPlayerDied;
        }

        // 미리 둔 적은 이 구역 소속으로 묶고, 테스트용 자동 부활을 끔
        foreach (EnemySwordsman e in FindObjectsByType<EnemySwordsman>(FindObjectsSortMode.None))
        {
            if (FlatDistance(e.transform.position) > radius + 2f) continue;
            e.DisableRespawn();
            e.Died += OnEnemyDied; // [변경]
            preplaced.Add(e);
        }

        ResetZone(false);

        // 시작 위치가 이 구역이면 전환 없이 바로 적용
        if (player != null && FlatDistance(player.position) <= atmosphereRadius) ApplyAtmosphere(0f);
    }

    void Update()
    {
        if (activeAtmosphere != this && player != null && FlatDistance(player.position) <= atmosphereRadius) ApplyAtmosphere(atmosphereBlendTime);

        switch (state)
        {
            case State.Waiting:
                if (player != null && FlatDistance(player.position) <= radius - enterMargin) BeginFight();
                break;
            case State.Fighting:
                if (!respawning) UpdateWaves();
                break;
        }
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
    }

    void BeginFight()
    {
        state = State.Fighting;
        Fighting = this;
        SetActive(entranceBarrier, true);

        // 미리 둔 적이 있으면 그들이 첫 상대, 없으면 잠시 뒤 1웨이브
        current.Clear();
        foreach (EnemySwordsman e in preplaced)
        {
            if (e == null) continue;
            e.Alert();
            current.Add(e);
        }
        waveTimer = current.Count == 0 && waves.Count > 0 ? firstWaveDelay : 0f;
        if (logState) Debug.Log($"[Zone] {name} 전투 시작 · 웨이브 {waves.Count}개");
    }

    // 핵심: 지금 무리가 모두 쓰러지면 → 대기 → 다음 웨이브, 웨이브가 더 없으면 클리어
    void UpdateWaves()
    {
        if (waveTimer > 0f)
        {
            waveTimer -= Time.deltaTime;
            if (waveTimer <= 0f) SpawnWave(waves[waveIndex++]);
            return;
        }

        if (!AllDefeated()) return;

        if (waveIndex < waves.Count) waveTimer = current.Count > 0 ? waveInterval : firstWaveDelay;
        else Clear();
    }

    // 플레이어 반대편 벽 쪽에 부채꼴로 세움 → 등 뒤에서 갑자기 나오지 않게
    void SpawnWave(Wave wave)
    {
        current.Clear();
        if (wave.enemyPrefab == null || wave.enemyPrefab.GetComponent<EnemySwordsman>() == null)
        {
            Debug.LogWarning($"[Zone] {name} {waveIndex}웨이브 프리팹이 비었거나 EnemySwordsman이 없습니다.");
            return;
        }

        Vector3 toPlayer = player != null ? player.position - transform.position : Vector3.forward;
        float baseAngle = Mathf.Atan2(toPlayer.z, toPlayer.x) + Mathf.PI;
        float step = spawnSpread * Mathf.Deg2Rad;

        for (int i = 0; i < wave.count; i++)
        {
            float a = baseAngle + (i - (wave.count - 1) * 0.5f) * step;
            Vector3 pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spawnRadius;
            pos.y = GroundY(pos);

            Vector3 look = transform.position - pos;
            look.y = 0f;
            GameObject go = Instantiate(wave.enemyPrefab, pos, Quaternion.LookRotation(look.normalized, Vector3.up));
            var enemy = go.GetComponent<EnemySwordsman>();
            enemy.DisableRespawn();
            enemy.Alert();
            enemy.HoldFor(spawnHold); // [변경]
            enemy.Died += OnEnemyDied;
            AnyEnemySpawned?.Invoke(pos);
            spawned.Add(enemy);
            current.Add(enemy);
        }

        WaveStarted?.Invoke(waveIndex);
        if (logState) Debug.Log($"[Zone] {name} {waveIndex}/{waves.Count} 웨이브 · 적 {wave.count}명");
    }

    float GroundY(Vector3 pos)
    {
        Ray ray = new Ray(pos + Vector3.up * 20f, Vector3.down);
        return Physics.Raycast(ray, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y + 0.05f : transform.position.y;
    }

    void Clear()
    {
        state = State.Cleared;
        SetActive(exitBarrier, false);
        if (Fighting == this) Fighting = null;
        Cleared?.Invoke();
        AnyCleared?.Invoke(this);
        if (logState) Debug.Log($"[Zone] {name} 클리어 · 출구 열림");
    }

    // [변경] 지금 무리의 마지막 적이 쓰러진 순간 → 마무리 연출 신호 (웨이브 진행은 UpdateWaves가 담당)
    void OnEnemyDied(EnemySwordsman enemy)
    {
        if (state != State.Fighting || !current.Contains(enemy) || !AllDefeated()) return;
        AnyWaveCleared?.Invoke(this, enemy.transform.position, waveIndex >= waves.Count);
    }

    bool AllDefeated()
    {
        foreach (EnemySwordsman e in current)
            if (e != null && e.CanBeTargeted) return false;
        return true;
    }

    // ===== 체크포인트 =====
    void OnPlayerDied()
    {
        if (state != State.Fighting || respawning) return;
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        respawning = true;
        if (logState) Debug.Log($"[Zone] {name} 사망 → {respawnDelay}초 뒤 입구에서 다시 시작");
        yield return new WaitForSecondsRealtime(respawnDelay);

        ResetZone(true);
        TeleportPlayer();
        if (playerHealth != null) playerHealth.Revive();
        respawning = false;
    }

    // 웨이브로 만든 적은 지우고, 미리 둔 적은 처음 자리로 되돌린 뒤 대기 상태로
    void ResetZone(bool resetPreplaced)
    {
        foreach (EnemySwordsman e in spawned)
            if (e != null) Destroy(e.gameObject);
        spawned.Clear();
        current.Clear();

        if (resetPreplaced)
            foreach (EnemySwordsman e in preplaced)
                if (e != null) e.ResetToHome();

        waveIndex = 0;
        waveTimer = 0f;
        SetActive(entranceBarrier, false);
        SetActive(exitBarrier, true);
        state = State.Waiting;
        if (Fighting == this) Fighting = null;
    }

    // CharacterController는 켜진 채로 위치를 바꾸면 덮어써지므로 잠깐 끄고 옮김
    void TeleportPlayer()
    {
        if (player == null || respawnPoint == null) return;

        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);
        if (cc != null) cc.enabled = true;
    }

    float FlatDistance(Vector3 position)
    {
        Vector3 d = position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    // ===== 분위기 전환 =====
    void ApplyAtmosphere(float blendTime)
    {
        if (theme == null) return;
        // StopAllCoroutines는 부활 코루틴까지 멈추므로 분위기 코루틴만 멈춤
        if (activeAtmosphere != null && activeAtmosphere.atmosphereRoutine != null) activeAtmosphere.StopCoroutine(activeAtmosphere.atmosphereRoutine);
        activeAtmosphere = this;
        atmosphereRoutine = StartCoroutine(BlendAtmosphere(blendTime));
        AnyEntered?.Invoke(this); // 구역 이름 배너
    }

    // 하늘은 섞을 수 없어서 시작할 때 교체, 안개·해는 blendTime 동안 보간
    IEnumerator BlendAtmosphere(float blendTime)
    {
        if (theme.skybox != null && RenderSettings.skybox != theme.skybox)
        {
            RenderSettings.skybox = theme.skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            DynamicGI.UpdateEnvironment();
        }

        RenderSettings.fog = theme.fog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        Light light = FindSun();

        Color fogFrom = RenderSettings.fogColor;
        float densityFrom = RenderSettings.fogDensity;
        Color sunFrom = light != null ? light.color : Color.white;
        Quaternion rotFrom = light != null ? light.transform.rotation : Quaternion.identity;
        Quaternion rotTo = Quaternion.Euler(theme.sunEuler);

        for (float t = 0f; t < blendTime; t += Time.unscaledDeltaTime)
        {
            SetAtmosphere(Mathf.SmoothStep(0f, 1f, t / blendTime), fogFrom, densityFrom, sunFrom, rotFrom, rotTo, light);
            yield return null;
        }
        SetAtmosphere(1f, fogFrom, densityFrom, sunFrom, rotFrom, rotTo, light);
        if (logState) Debug.Log($"[Zone] {name} 분위기 적용");
    }

    void SetAtmosphere(float k, Color fogFrom, float densityFrom, Color sunFrom, Quaternion rotFrom, Quaternion rotTo, Light light)
    {
        RenderSettings.fogColor = Color.Lerp(fogFrom, theme.fogColor, k);
        RenderSettings.fogDensity = Mathf.Lerp(densityFrom, theme.fogDensity, k);
        if (light == null) return;
        light.color = Color.Lerp(sunFrom, theme.sunColor, k);
        light.transform.rotation = Quaternion.Slerp(rotFrom, rotTo, k);
    }

    static Light FindSun()
    {
        if (sun != null) return sun;
        sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        return sun;
    }

    static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        const int segments = 48;
        Vector3 prev = transform.position + new Vector3(radius, 0.1f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 next = transform.position + new Vector3(Mathf.Cos(a) * radius, 0.1f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
