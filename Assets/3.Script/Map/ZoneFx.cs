using System.Collections;
using DigitalRuby.ThunderAndLightning;
using UnityEngine;

// 구역 전투 연출: 적은 낙뢰와 함께 등장, 웨이브 마지막 적이 쓰러지면 잠깐 슬로 + 카메라 당기기 + 낙뢰
// ZoneArena의 정적 이벤트만 구독 → 구역 코드는 연출을 모름
public class ZoneFx : MonoBehaviour
{
    [SerializeField] GameObject boltPrefab;
    [SerializeField] ThirdPersonCamera followCamera;
    [SerializeField] PlayerSkill skill;

    [Header("Spawn")]
    [SerializeField] LightningBoltStyle spawnStyle = new LightningBoltStyle
    {
        width = new Vector2(0.12f, 0.2f), duration = new Vector2(0.25f, 0.35f), count = new Vector2Int(2, 3),
        generations = 6, chaos = 0.25f, forkedness = 0.5f, intensity = 2.5f, glowIntensity = 2f, glowWidth = 6f,
        glowColor = new Color(0.55f, 0.35f, 1f), lightRange = 10f, lightIntensity = 3f, lightPercent = 1f,
        lightColor = new Color(0.65f, 0.5f, 1f)
    };
    [SerializeField] float spawnBoltHeight = 14f;
    [SerializeField] float spawnShake = 0.05f;

    [Header("Wave Finish")]
    [SerializeField] LightningBoltStyle finishStyle = new LightningBoltStyle
    {
        width = new Vector2(0.2f, 0.3f), duration = new Vector2(0.35f, 0.45f), count = new Vector2Int(3, 4),
        generations = 6, chaos = 0.2f, forkedness = 0.6f, intensity = 3f, glowIntensity = 2.5f, glowWidth = 8f,
        lightRange = 16f, lightIntensity = 4f, lightPercent = 1f
    };
    [SerializeField] float finishSlowScale = 0.25f;
    [SerializeField] float finishSlowTime = 0.5f;   // 실제 시간
    [SerializeField] float finishZoom = 0.2f;
    [SerializeField] float zoneFinishSlowTime = 0.9f; // 구역 마지막 웨이브는 조금 더 길게

    LightningBoltPrefabScriptBase spawnBolt;
    LightningBoltPrefabScriptBase finishBolt;
    Coroutine slowRoutine;

    void Awake()
    {
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        if (skill == null) skill = FindAnyObjectByType<PlayerSkill>();
        spawnBolt = LightningFactory.Create(boltPrefab, "ZoneSpawnLightning", spawnStyle);
        finishBolt = LightningFactory.Create(boltPrefab, "ZoneFinishLightning", finishStyle);
    }

    void OnEnable()
    {
        ZoneArena.AnyEnemySpawned += OnEnemySpawned;
        ZoneArena.AnyWaveCleared += OnWaveCleared;
    }

    void OnDisable()
    {
        ZoneArena.AnyEnemySpawned -= OnEnemySpawned;
        ZoneArena.AnyWaveCleared -= OnWaveCleared;
        RestoreTime();
    }

    void OnEnemySpawned(Vector3 pos)
    {
        if (spawnBolt != null) spawnBolt.Trigger(pos + Vector3.up * spawnBoltHeight, pos);
        if (followCamera != null) followCamera.Shake(spawnShake, 0.12f);
    }

    void OnWaveCleared(ZoneArena zone, Vector3 pos, bool final)
    {
        if (finishBolt != null) finishBolt.Trigger(pos + Vector3.up * spawnBoltHeight, pos);
        if (followCamera != null)
        {
            followCamera.Zoom(finishZoom, final ? zoneFinishSlowTime : finishSlowTime);
            followCamera.Shake(0.12f, 0.2f);
        }

        // 일섬이 시간 배속을 쥐고 있으면(차지·일괄 베기) 그쪽 연출에 맡김
        if (skill != null && skill.IsActive) return;
        if (!Mathf.Approximately(Time.timeScale, 1f)) return;

        if (slowRoutine != null) StopCoroutine(slowRoutine);
        slowRoutine = StartCoroutine(Slow(final ? zoneFinishSlowTime : finishSlowTime));
    }

    // 핵심: 슬로 도중 일섬이 시작되면 일섬이 슬로 값을 저장했다가 되돌리므로, 일섬이 끝날 때까지 기다린 뒤 1로 복구
    IEnumerator Slow(float realtime)
    {
        Time.timeScale = finishSlowScale;
        yield return new WaitForSecondsRealtime(realtime);
        while (skill != null && skill.IsActive) yield return null;
        RestoreTime();
        slowRoutine = null;
    }

    void RestoreTime()
    {
        if (Mathf.Approximately(Time.timeScale, finishSlowScale)) Time.timeScale = 1f;
    }
}
