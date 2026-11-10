using UnityEngine;
using UnityEngine.InputSystem;

// 화톳불(체크포인트): 가까이에서 E → 체력 회복 + 스킬트리 열기
// 불빛은 실제 시간으로 일렁임 (일시정지 중에도 살아 있게). 생성·배치는 Tools/Setup/Skill Tree + Shrines
public class Shrine : MonoBehaviour
{
    [SerializeField] float interactRange = 2.6f;
    [SerializeField] Light fireLight;
    [SerializeField] float flickerAmount = 0.25f;
    [SerializeField] float flickerSpeed = 7f;
    [SerializeField] bool logRest;

    Transform player;
    PlayerHealth health;
    float baseIntensity;
    bool inRange;
    float seed;

    void Awake()
    {
        if (fireLight != null) baseIntensity = fireLight.intensity;
        seed = Random.value * 100f;
    }

    void Start()
    {
        health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
    }

    void OnDisable()
    {
        if (SkillTreeMenu.Instance != null) SkillTreeMenu.Instance.SetPrompt(this, false);
    }

    void Update()
    {
        if (fireLight != null)
        {
            float t = Time.unscaledTime * flickerSpeed + seed;
            float n = Mathf.PerlinNoise(t, seed) * 2f - 1f;
            fireLight.intensity = baseIntensity * (1f + n * flickerAmount);
        }

        SkillTreeMenu menu = SkillTreeMenu.Instance;
        if (player == null || menu == null) return;

        Vector3 d = player.position - transform.position;
        d.y = 0f;
        bool near = d.sqrMagnitude <= interactRange * interactRange && (health == null || !health.IsDead);
        if (near != inRange)
        {
            inRange = near;
            menu.SetPrompt(this, near);
        }

        if (!near || GamePause.IsPaused || menu.IsOpen) return;
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        // 핵심: 휴식 = 체력 회복 → 스킬트리
        if (health != null) health.HealFull();
        menu.Open();
        if (logRest) Debug.Log($"[Shrine] {name}에서 휴식");
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.75f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
