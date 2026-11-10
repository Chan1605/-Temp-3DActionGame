using UnityEngine;

// 플레이어 효과음. 전투·스킬 이벤트를 받아 소리만 담당 (PlayerVfx와 같은 방식)
//  - [변경] 베기: 칼이 적에게 맞았을 때만 (허공 휘두르기는 무음). 한 번에 여럿을 베도 한 번만
//  - [변경] Q 차지: 누르는 순간부터 반복음 (일반 차지·풀차지·발도 자세 모두). 차지 단계·표식이 찰수록 커지고 높아짐
//  - 발도 자세 진입: 진입음을 반복음 위에 겹쳐 한 번 (반복음은 끊기지 않음)
// 연결은 Tools/Setup/Player Sfx. 슬롯을 비우면 그 소리만 꺼짐
public class PlayerSfx : MonoBehaviour
{
    [SerializeField] PlayerSkill skill;
    [SerializeField] WeaponHitbox weapon; // [변경] 휘두르기 대신 적중으로 재생

    [Header("Hit")]
    [SerializeField] AudioClip[] swingClips; // 이름은 그대로 (연결된 값 유지), 적중 소리로 사용
    [SerializeField, Range(0f, 1f)] float swingVolume = 0.8f;
    [SerializeField] Vector2 swingPitch = new Vector2(0.97f, 1.03f);
    [SerializeField] float heavyPitch = 0.9f;     // 강한 공격은 조금 낮고 묵직하게
    [SerializeField] float hitSoundInterval = 0.06f; // 이 시간 안에 여러 명을 베면 한 번만

    [Header("Charge / Draw Stance")]
    [SerializeField] AudioClip stanceStart;
    [SerializeField] AudioClip stanceLoop;
    [SerializeField, Range(0f, 1f)] float stanceStartVolume = 0.9f;
    [SerializeField] Vector2 stanceLoopVolume = new Vector2(0.55f, 1f); // 처음 → 가득 찼을 때
    [SerializeField] Vector2 stanceLoopPitch = new Vector2(1f, 1.12f);
    [SerializeField] float loopFadeIn = 0.08f;  // [변경] 반복음이 바로 시작하되 첫 순간 튀지 않게
    [SerializeField] float stanceFadeOut = 0.15f;
    [SerializeField] int maxChargeLevel = 2;    // [변경] 일반 차지 단계 → 채움 정도 (스킬 데이터 기본 최대 2단계)

    AudioSource oneShot;
    AudioSource stanceStartSource;
    AudioSource stanceLoopSource;

    int lastSwing = -1;
    float lastHitTime = -1f;

    bool charging;      // [변경] HoldStarted ~ HoldEnded (일반 차지·발도 자세·회피 직후 풀차지 포함)
    int chargeLevel;
    bool wasStance;
    float loopFade;     // 0~1

    void Awake()
    {
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>(true);

        oneShot = CreateSource("Sfx_OneShot", false);
        stanceStartSource = CreateSource("Sfx_StanceStart", false);
        stanceLoopSource = CreateSource("Sfx_StanceLoop", true);
    }

    void OnEnable()
    {
        if (weapon != null) weapon.OnHit += OnWeaponHit;
        if (skill != null)
        {
            skill.HoldStarted += OnHoldStarted;
            skill.ChargeLevelChanged += OnChargeLevelChanged;
            skill.HoldEnded += OnHoldEnded;
        }
    }

    void OnDisable()
    {
        if (weapon != null) weapon.OnHit -= OnWeaponHit;
        if (skill != null)
        {
            skill.HoldStarted -= OnHoldStarted;
            skill.ChargeLevelChanged -= OnChargeLevelChanged;
            skill.HoldEnded -= OnHoldEnded;
        }
        charging = false;
        StopCharge();
    }

    AudioSource CreateSource(string objectName, bool loop)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f; // 플레이어 소리는 항상 화면 중심이라 2D
        return source;
    }

    void Update() => UpdateCharge();

    // ===== 적중 ===== [변경]
    void OnWeaponHit(IDamageable target, DamageInfo info, HitResult result)
    {
        if (result != HitResult.Hit || swingClips == null || swingClips.Length == 0) return;
        if (Time.unscaledTime - lastHitTime < hitSoundInterval) return;
        lastHitTime = Time.unscaledTime;

        int i = Random.Range(0, swingClips.Length);
        if (swingClips.Length > 1 && i == lastSwing) i = (i + 1) % swingClips.Length;
        lastSwing = i;
        if (swingClips[i] == null) return;

        bool heavy = info.Weight != AttackWeight.Light;
        oneShot.pitch = Random.Range(swingPitch.x, swingPitch.y) * (heavy ? heavyPitch : 1f);
        oneShot.PlayOneShot(swingClips[i], swingVolume * AudioOptions.SfxVolume);
    }

    // ===== Q 차지 · 발도 자세 ===== [변경]
    void OnHoldStarted()
    {
        charging = true;
        chargeLevel = 0;
        if (stanceLoop == null) return;
        if (!stanceLoopSource.isPlaying)
        {
            stanceLoopSource.clip = stanceLoop;
            stanceLoopSource.volume = 0f;
            stanceLoopSource.Play();
            loopFade = 0f;
        }
    }

    void OnChargeLevelChanged(int level) => chargeLevel = level;

    void OnHoldEnded() => charging = false;

    void UpdateCharge()
    {
        bool stance = skill != null && skill.InDrawStance && !skill.StanceAutoDraw;
        // 핵심: 발도 자세에 들어가는 순간 진입음을 반복음 위에 겹침 → 진입음이 끝나길 기다리지 않음
        if (stance && !wasStance && stanceStart != null)
        {
            stanceStartSource.pitch = 1f;
            stanceStartSource.PlayOneShot(stanceStart, stanceStartVolume * AudioOptions.SfxVolume);
        }
        wasStance = stance;

        if (!stanceLoopSource.isPlaying) return;

        float speed = charging ? 1f / Mathf.Max(0.01f, loopFadeIn) : 1f / Mathf.Max(0.01f, stanceFadeOut);
        loopFade = Mathf.MoveTowards(loopFade, charging ? 1f : 0f, Time.unscaledDeltaTime * speed);

        float fill = Mathf.Clamp01(chargeLevel / (float)Mathf.Max(1, maxChargeLevel));
        if (stance)
        {
            float marks = skill.StanceMarkCount / (float)Mathf.Max(1, skill.StanceMaxMarks);
            fill = Mathf.Max(fill, Mathf.Clamp01((1f - skill.StanceRemaining01) * 0.6f + marks));
        }

        stanceLoopSource.volume = Mathf.Lerp(stanceLoopVolume.x, stanceLoopVolume.y, fill) * loopFade * AudioOptions.SfxVolume;
        stanceLoopSource.pitch = Mathf.Lerp(stanceLoopPitch.x, stanceLoopPitch.y, fill);

        if (!charging && loopFade <= 0f) StopCharge();
    }

    void StopCharge()
    {
        if (stanceLoopSource != null) stanceLoopSource.Stop();
        loopFade = 0f;
    }
}
