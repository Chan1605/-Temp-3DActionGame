using UnityEngine;

// 보스(EnemyKind.Boss) 흑뢰: 검병 상태 머신 위에 패턴을 얹음
// 맞아도 경직이 없고 체간(자세)만 쌓임 → 가득 차면 체간 붕괴(처형 기회). 처형은 즉사 대신 최대 체력 비율 피해 → 기상
// 가까이: 연속 베기(firstAttack 연쇄, 간파·패링 가능) 또는 잡기(붉은 예고, 회피로만 피함)
// 중거리: 도약 내려찍기(바닥에 범위 예고) · 2페이즈: 검기(멀리 날아오는 참격)
// 체력 phase2Ratio 이하 → 포효(가까우면 밀쳐냄) 후 2페이즈: 동작이 빨라지고 쿨타임이 짧아짐
public partial class EnemySwordsman
{
    static readonly int BossIntroHash = Animator.StringToHash("Boss_Intro");
    static readonly int BossRoarHash = Animator.StringToHash("Boss_Roar");
    static readonly int BossGrabHash = Animator.StringToHash("Boss_Grab"); // 예전 1종 (Boss_Grab_N이 없을 때)
    static readonly int[] BossGrabHashes = // 잡기 짝 모션 3종 (처형 1·2·3과 같은 클립)
    {
        Animator.StringToHash("Boss_Grab_1"), Animator.StringToHash("Boss_Grab_2"), Animator.StringToHash("Boss_Grab_3")
    };
    static readonly int BossLeapAirHash = Animator.StringToHash("Boss_LeapAir");
    static readonly int BossLeapSlamHash = Animator.StringToHash("Boss_LeapSlam");
    static readonly int BossStaggerHash = Animator.StringToHash("Boss_Stagger");
    static readonly int BossGetUpHash = Animator.StringToHash("Boss_GetUp");

    [Header("Boss Look")]
    [SerializeField] Color grabTellColor = new Color(2.2f, 0.12f, 0.08f);
    [SerializeField] Color phase2Tint = new Color(0.32f, 0.05f, 0.06f);
    [SerializeField] Color postureBreakColor = new Color(1.4f, 1.1f, 0.3f);
    [SerializeField] Material telegraphMaterial; // 도약 범위 표시 (비우면 실행 중에 만듦)
    [SerializeField] GameObject waveVisual;      // 검기 모양 (비우면 단순 판)
    [SerializeField] GameObject waveHitVfx;

    public static EnemySwordsman ActiveBoss { get; private set; }
    public static event System.Action<EnemySwordsman> BossPhaseChanged;

    float posture;
    float lastPostureHit;
    bool postureBroken;
    bool phase2, pendingPhase2, introPlayed;
    float grabReadyTime, leapReadyTime, waveReadyTime;

    bool grabConnected, grabDodged, grabHitDone, grabCinematic;
    float grabKillTime = 0.55f, grabEndTime = 0.95f;
    int grabMotion;    // 이번 잡기의 모션 번호 (0~2)
    int grabStateHash;
    PlayerHealth grabbedPlayer;
    BossGrabTell grabTell; // [변경] 양손 붉은 빛 예고
    static readonly Color GrabLabelColor = new Color(1f, 0.25f, 0.18f);

    // [변경] 잡기 예고 세기 (0이면 꺼짐): 예고 동안 0→1, 달려드는 동안 1
    public float GrabTell01 =>
        state == State.BossGrabWindup ? Mathf.Clamp01(stateTime / Mathf.Max(data.GrabWindup, 0.01f)) :
        state == State.BossGrab && !grabConnected && stateTime <= data.GrabLungeTime + 0.1f ? 1f : 0f;

    bool comboStop;                       // 이번 타에서 연타를 끊음
    float pendingDelay, holdLeft, holdAt;  // 다음 타에 걸 지연 / 지금 타의 남은 멈춤 / 멈추는 지점(정규화)
    AttackData delayFor;                  // 지연을 걸 타 (거리 때문에 연타가 끊기면 다음 연타 첫 타에 잘못 걸리지 않게)
    static readonly System.Collections.Generic.HashSet<EnemyData> introSeen = new System.Collections.Generic.HashSet<EnemyData>(); // 재도전 때 등장 연출 짧게

    Vector3 leapFrom, leapTo;
    bool leapLooping;
    BossTelegraph telegraph;
    ThirdPersonCamera bossCamera;

    public bool IsBoss => data != null && data.Kind == EnemyKind.Boss;
    public string BossName => data != null ? data.DisplayName : name;
    public float Health01 => data != null ? Mathf.Clamp01(health / Mathf.Max(1f, data.MaxHealth * healthScale)) : 0f;
    public float Posture01 => IsBoss && data.MaxPosture > 0f ? Mathf.Clamp01(posture / data.MaxPosture) : 0f;
    public bool IsPostureBroken => postureBroken;
    public bool IsPhase2 => phase2;
    public float Phase2Ratio => data != null ? data.Phase2Ratio : 0.5f;

    float BossSpeed => IsBoss && phase2 ? data.Phase2Speed : 1f;
    float CooldownScale => IsBoss && phase2 ? data.Phase2CooldownScale : 1f;
    bool IsBossRanged => state == State.BossLeapWindup || state == State.BossLeapAir;
    Color BaseTint => IsBoss && phase2 ? phase2Tint : bodyTint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBossStatics()
    {
        ActiveBoss = null;
        BossPhaseChanged = null;
        introSeen.Clear();
    }

    void InitBoss()
    {
        posture = 0f;
        postureBroken = false;
        phase2 = pendingPhase2 = introPlayed = false;
        grabReadyTime = leapReadyTime = waveReadyTime = 0f;
        ReleaseGrabbedPlayer();
        DestroyTelegraph();
    }

    void OnDisable()
    {
        TokensOnDisable();
        if (!IsBoss) return;
        ReleaseGrabbedPlayer();
        DestroyTelegraph();
        if (ActiveBoss == this) ActiveBoss = null;
    }

    // 매 프레임: 체간 자연 회복, 붕괴 경직이 끝나면 체간 초기화
    void UpdateBoss(float dt)
    {
        if (!IsBoss) return;
        if (postureBroken && state != State.Stun && state != State.Executed)
        {
            postureBroken = false;
            posture = 0f;
        }
        if (!postureBroken && posture > 0f && localTime - lastPostureHit >= data.PostureRegenDelay)
            posture = Mathf.Max(0f, posture - data.PostureRegenRate * dt);
    }

    // 추적 중 보스 전용 판단. true면 이번 프레임은 보스가 행동을 정함
    bool UpdateBossChase(float dist)
    {
        if (!IsBoss) return false;

        if (!introPlayed)
        {
            introPlayed = true;
            ActiveBoss = this;
            bossCamera = FindAnyObjectByType<ThirdPersonCamera>();
            moveSpeed = 0f;
            bool retry = !introSeen.Add(data); // 재도전이면 칼 뽑는 등장을 건너뛰고 바로 싸움
            if (!retry && animator.HasState(0, BossIntroHash)) animator.CrossFadeInFixedTime(BossIntroHash, 0.15f, 0, 0f);
            stunDuration = retry ? 0.3f : data.IntroTime;
            attackReadyTime = localTime + stunDuration + 0.3f;
            SetState(State.Stun);
            Log("등장");
            return true;
        }
        if (posture >= data.MaxPosture && !postureBroken)
        {
            BreakPosture(-transform.forward);
            return true;
        }
        if (pendingPhase2)
        {
            StartRoar();
            return true;
        }
        if (localTime < attackReadyTime || dist <= data.AttackRange + 1f) return false;

        bool canLeap = data.LeapAttack != null && localTime >= leapReadyTime && dist >= data.LeapMinDistance && dist <= data.LeapMaxDistance;
        bool canWave = phase2 && data.WaveAttack != null && localTime >= waveReadyTime && dist >= data.WaveMinDistance;
        if (canLeap && canWave)
        {
            if (Random.value < 0.5f) canWave = false;
            else canLeap = false;
        }
        if (canLeap)
        {
            StartLeapWindup();
            return true;
        }
        if (canWave)
        {
            waveReadyTime = localTime + data.WaveCooldown * CooldownScale;
            StartAttack(data.WaveAttack);
            Log("검기");
            return true;
        }
        return false;
    }

    // 가까이서 공격을 고르는 순간 한 번만 굴림 → 잡기 / 아니면 평소 연속 베기
    bool TryStartGrab()
    {
        if (!IsBoss || data.GrabAttack == null || localTime < grabReadyTime || Random.value >= data.GrabChance) return false;
        moveSpeed = 0f;
        grabReadyTime = localTime + data.GrabCooldown * CooldownScale;
        SetState(State.BossGrabWindup);
        if (grabTell == null) grabTell = BossGrabTell.Create(this, animator); // [변경] 손 빛 + 머리 위 글자
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.4f * transform.lossyScale.y, "잡기!", GrabLabelColor);
        Log("잡기 예고");
        return true;
    }

    // ===== 연타 끊기 · 지연 베기 =====
    // 핵심: 타가 시작될 때 이번 타에서 끊을지, 다음(마지막) 타 직전에 멈칫할지를 미리 정함
    // → 간파·저스트 회피 판정(TimeUntilHit)이 같은 값을 읽어 "읽을 수 있는" 변칙이 됨
    void OnBossAttackStarted(AttackData attack)
    {
        holdLeft = 0f;
        comboStop = false;
        if (!IsBoss || IsWaveAttack(attack) || attack == data.GrabAttack)
        {
            pendingDelay = 0f;
            return;
        }

        holdLeft = attack == delayFor ? pendingDelay : 0f; // 앞 타에서 정해 둔 지연을 이번 타에 적용
        holdAt = attack.HitStart * 0.85f;
        pendingDelay = 0f;
        delayFor = null;

        AttackData next = attack.GetNext(AttackInput.Light);
        if (next == null) return;
        comboStop = ComboIndex(attack) >= 1 && Random.value < data.ComboStopChance;
        if (!comboStop && next.GetNext(AttackInput.Light) == null && Random.value < data.DelayChance)
        {
            pendingDelay = Random.Range(data.DelayTime.x, data.DelayTime.y);
            delayFor = next;
        }
    }

    int ComboIndex(AttackData attack)
    {
        AttackData a = data.FirstAttack;
        for (int i = 0; a != null && i < 8; i++, a = a.GetNext(AttackInput.Light))
            if (a == attack) return i;
        return -1;
    }

    // 판정 직전(holdAt)에 닿으면 멈춘 채 대상을 바라봄. 애니메이터도 거의 멈춤 (칼을 든 자세 유지)
    bool UpdateBossHold(float dt)
    {
        if (holdLeft <= 0f || currentAttack == null || stateTime / currentAttack.Duration < holdAt) return false;
        holdLeft -= dt;
        stateTime -= dt;
        animator.speed *= 0.04f;
        FaceTarget(dt, 0.6f);
        return true;
    }

    float BossHoldLeft() => IsBoss ? Mathf.Max(0f, holdLeft) : 0f;
    float BossPendingDelay() => IsBoss ? pendingDelay : 0f;
    bool BossStopsCombo() => IsBoss && comboStop;

    // ===== 잡기 =====
    void UpdateBossGrabWindup(float dt)
    {
        moveSpeed = 0f;
        FaceTarget(dt, 1.2f);
        if (stateTime < data.GrabWindup) return;

        grabConnected = grabDodged = grabHitDone = false;
        PickGrabMotion();
        var exec = target.GetComponent<PlayerExecution>();
        PlayerExecution.MotionSetup m = exec != null ? exec.GetMotion(grabMotion) : null;
        if (m != null)
        {
            grabKillTime = m.killTime;
            grabEndTime = m.endTime;
        }
        animator.CrossFadeInFixedTime(grabStateHash, 0.05f, 0, 0f);
        SetState(State.BossGrab);
    }

    // 보스·플레이어 둘 다 짝 상태가 있는 번호 중 무작위. 하나도 없으면 예전 Boss_Grab / Grabbed
    void PickGrabMotion()
    {
        var player = target.GetComponent<PlayerHealth>();
        int start = Random.Range(0, BossGrabHashes.Length);
        for (int k = 0; k < BossGrabHashes.Length; k++)
        {
            int i = (start + k) % BossGrabHashes.Length;
            if (!animator.HasState(0, BossGrabHashes[i]) || player == null || !player.HasGrabbedMotion(i)) continue;
            grabMotion = i;
            grabStateHash = BossGrabHashes[i];
            return;
        }
        grabMotion = 0;
        grabStateHash = BossGrabHash;
    }

    // 잡기 모션 진행도: 실제 애니메이터 상태에서 읽음 (클립마다 길이가 달라서)
    float GrabNormalized()
    {
        AnimatorStateInfo now = animator.GetCurrentAnimatorStateInfo(0);
        if (now.shortNameHash == grabStateHash) return now.normalizedTime;
        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
        if (next.shortNameHash == grabStateHash) return next.normalizedTime;
        return stateTime / Mathf.Max(0.01f, data.GrabAttack.Duration);
    }

    // 핵심: 짧게 돌진하며 손이 닿는 거리면 잡음 → 플레이어를 처형 짝 자리로 옮겨 같은 시점부터 짝 모션
    // 닿지 않거나 회피로 흘리면 헛잡음 → 무방비
    void UpdateBossGrab(float dt)
    {
        float k = GrabNormalized();
        if (!grabConnected)
        {
            moveSpeed = stateTime < data.GrabLungeTime ? data.GrabLungeSpeed : 0f;
            if (stateTime <= data.GrabLungeTime + 0.1f)
            {
                if (!grabDodged && InGrabReach()) TryConnectGrab(stateTime); // 보스가 모션을 시작한 뒤 흐른 시간
                return;
            }

            moveSpeed = 0f;
            stunDuration = data.GrabMissStun;
            attackReadyTime = localTime + data.GrabMissStun + 0.3f;
            animator.CrossFadeInFixedTime(LocomotionHash, 0.2f, 0, 0f);
            SetState(State.Stun);
            Log("헛잡음");
            return;
        }

        moveSpeed = 0f;
        if (!grabHitDone && k >= grabKillTime) GrabHit();
        if (k >= grabEndTime || stateTime > 8f) FinishGrab();
    }

    bool InGrabReach()
    {
        Vector3 to = target.position - transform.position;
        if (Mathf.Abs(to.y) > 1.2f) return false;
        to.y = 0f;
        return to.magnitude <= data.GrabRange * transform.lossyScale.x && Vector3.Angle(transform.forward, to) <= 60f;
    }

    void TryConnectGrab(float elapsed)
    {
        var player = target.GetComponent<PlayerHealth>();
        if (player == null) return;

        GrabVictimPose(out Vector3 pos, out Quaternion rot);
        if (!player.TryBeginGrab(gameObject, pos, rot, elapsed, animator.speed, grabMotion)) // 같은 번호의 당하는 쪽 모션
        {
            grabDodged = true; // 회피 무적 등으로 흘림 → 이번 잡기는 더 시도하지 않음
            return;
        }

        grabConnected = true;
        grabbedPlayer = player;
        if (bossCamera != null)
        {
            grabCinematic = true;
            bossCamera.BeginCinematic(Vector3.Lerp(transform.position, pos, 0.5f) + Vector3.up * 1.3f, transform.eulerAngles.y + 70f, 10f, 4.8f);
        }
        Log($"잡기 성공 · 모션 {grabMotion + 1}");
    }

    // 플레이어 처형 짝 자리를 그대로 씀 (보스가 처형하는 쪽). 덩치만큼 거리를 늘림
    void GrabVictimPose(out Vector3 pos, out Quaternion rot)
    {
        float s = transform.lossyScale.x;
        var exec = target.GetComponent<PlayerExecution>();
        if (exec != null)
        {
            exec.GetVictimPose(transform.position, transform.rotation, grabMotion, out pos, out rot);
            pos = transform.position + (pos - transform.position) * s;
        }
        else
        {
            pos = transform.position + transform.forward * 1.2f * s;
            rot = Quaternion.LookRotation(-transform.forward, Vector3.up);
        }
        pos.y = target.position.y;
    }

    void GrabHit()
    {
        grabHitDone = true;
        if (grabbedPlayer == null) return;
        grabbedPlayer.GrabHit(new DamageInfo
        {
            Attacker = gameObject,
            Damage = data.GrabDamage * damageScale,
            Weight = AttackWeight.Heavy,
            KnockDown = true,
            HitPoint = target.position + Vector3.up * 1.2f,
            Direction = transform.forward
        });
        if (bossCamera != null) bossCamera.Shake(0.22f, 0.25f);
    }

    void FinishGrab()
    {
        ReleaseGrabbedPlayer();
        attackReadyTime = localTime + data.AttackCooldown * CooldownScale;
        PlayMoveState(0.3f);
        SetState(State.Chase);
    }

    void ReleaseGrabbedPlayer()
    {
        if (grabCinematic && bossCamera != null) bossCamera.EndCinematic();
        grabCinematic = false;
        if (grabbedPlayer != null) grabbedPlayer.ReleaseGrab();
        grabbedPlayer = null;
        grabConnected = false;
    }

    // ===== 도약 내려찍기 =====
    void StartLeapWindup()
    {
        moveSpeed = 0f;
        leapReadyTime = localTime + data.LeapCooldown * CooldownScale;
        leapTo = target.position;
        DestroyTelegraph();
        telegraph = BossTelegraph.Create(telegraphMaterial, data.LeapRadius, leapTo);
        if (animator.HasState(0, ChargeWindupHash)) animator.CrossFadeInFixedTime(ChargeWindupHash, 0.1f, 0, 0f);
        SetState(State.BossLeapWindup);
        Log("도약 예고");
    }

    // 예고 동안 범위가 플레이어를 따라다니다가 뛰는 순간 고정
    void UpdateBossLeapWindup(float dt)
    {
        moveSpeed = 0f;
        FaceTarget(dt, 1.5f);
        leapTo = target.position;
        float total = data.LeapWindup + data.LeapAirTime;
        if (telegraph != null)
        {
            telegraph.SetPosition(leapTo);
            telegraph.SetProgress(stateTime / total);
        }
        if (stateTime < data.LeapWindup) return;

        leapFrom = transform.position;
        Vector3 flat = Flat(leapTo - leapFrom);
        if (flat.magnitude > data.LeapMaxDistance + 2f) leapTo = leapFrom + flat.normalized * (data.LeapMaxDistance + 2f);
        if (telegraph != null)
        {
            telegraph.SetPosition(leapTo);
            leapTo.y = telegraph.transform.position.y - 0.05f; // 플레이어가 점프 중이어도 바닥에 착지
        }
        controller.enabled = false;
        leapLooping = false;
        animator.CrossFadeInFixedTime(BossLeapAirHash, 0.08f, 0, 0f);
        SetState(State.BossLeapAir);
    }

    void UpdateBossLeapAir(float dt)
    {
        moveSpeed = 0f;
        float k = Mathf.Clamp01(stateTime / Mathf.Max(0.05f, data.LeapAirTime));
        Vector3 pos = Vector3.Lerp(leapFrom, leapTo, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * data.LeapHeight);
        transform.position = pos;
        Vector3 dir = Flat(leapTo - leapFrom);
        if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        if (telegraph != null) telegraph.SetProgress((data.LeapWindup + stateTime) / (data.LeapWindup + data.LeapAirTime));
        if (!leapLooping && k >= 0.5f)
        {
            leapLooping = true;
            animator.CrossFadeInFixedTime(BossLeapSlamHash, 0.1f, 0, 0f); // 떨어지며 내려찍는 동작
        }
        if (k >= 1f) LeapLand();
    }

    void LeapLand()
    {
        transform.position = leapTo;
        controller.enabled = true;
        DestroyTelegraph();

        Vector3 to = target.position - leapTo;
        if (Mathf.Abs(to.y) < 2f && Flat(to).magnitude <= data.LeapRadius)
        {
            var damageable = target.GetComponentInParent<IDamageable>();
            damageable?.ReceiveDamage(new DamageInfo
            {
                Attacker = gameObject,
                Damage = data.LeapDamage * damageScale,
                Weight = AttackWeight.Heavy,
                KnockDown = true,
                HitPoint = target.position + Vector3.up,
                Direction = Flat(to).sqrMagnitude > 0.01f ? Flat(to).normalized : transform.forward
            });
        }
        if (bossCamera != null) bossCamera.Shake(0.3f, 0.3f);
        attackReadyTime = localTime + data.LeapRecovery + 0.3f;
        SetState(State.BossLeapLand);
    }

    void UpdateBossLeapLand()
    {
        moveSpeed = 0f;
        if (stateTime < data.LeapRecovery) return;
        PlayMoveState(0.25f);
        SetState(State.Chase);
    }

    void DestroyTelegraph()
    {
        if (telegraph != null) Destroy(telegraph.gameObject);
        telegraph = null;
    }

    // ===== 검기 =====
    bool IsWaveAttack(AttackData attack)
    {
        if (!IsBoss || attack == null) return false;
        AttackData a = data.WaveAttack;
        for (int i = 0; a != null && i < 6; i++, a = a.GetNext(AttackInput.Light))
            if (a == attack) return true;
        return false;
    }

    void FireWave()
    {
        Vector3 dir = Flat(target.position - transform.position);
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        float s = transform.lossyScale.x;
        Vector3 origin = transform.position + Vector3.up * 1.1f * s + dir * 0.8f * s;
        BossSwordWave.Fire(gameObject, origin, dir, data.WaveSpeed, data.WaveRange, data.WaveWidth, new DamageInfo
        {
            Attacker = gameObject,
            Damage = data.WaveDamage * damageScale,
            Weight = AttackWeight.Heavy
        }, waveVisual, waveHitVfx, telegraphMaterial);
    }

    // ===== 2페이즈 포효 =====
    void StartRoar()
    {
        pendingPhase2 = false;
        phase2 = true;
        if (swingActive) EndSwing();
        currentAttack = null;
        moveSpeed = 0f;
        if (animator.HasState(0, BossRoarHash)) animator.CrossFadeInFixedTime(BossRoarHash, 0.15f, 0, 0f);
        waveReadyTime = localTime + data.RoarTime + 1f;
        SetState(State.BossRoar);

        DamageEvents.RaiseLabel(transform.position + Vector3.up * 3f, "분노", new Color(1f, 0.25f, 0.2f));
        if (bossCamera != null)
        {
            bossCamera.Shake(0.25f, 0.6f);
            bossCamera.Punch(0.12f, 0.15f, 0.4f, 0.5f, 0f);
        }

        // 가까이 있으면 밀쳐 넘어뜨림 (피해는 작게)
        Vector3 to = target.position - transform.position;
        if (Flat(to).magnitude <= data.RoarPushRadius)
        {
            var damageable = target.GetComponentInParent<IDamageable>();
            damageable?.ReceiveDamage(new DamageInfo
            {
                Attacker = gameObject,
                Damage = 5f * damageScale,
                Weight = AttackWeight.Heavy,
                KnockDown = true,
                HitPoint = target.position + Vector3.up,
                Direction = Flat(to).normalized
            });
        }
        BossPhaseChanged?.Invoke(this);
        Log("2페이즈");
    }

    void UpdateBossRoar()
    {
        moveSpeed = 0f;
        if (stateTime < data.RoarTime) return;
        attackReadyTime = localTime + 0.4f;
        PlayMoveState(0.25f);
        SetState(State.Chase);
    }

    // ===== 피해 · 체간 =====
    HitResult BossReceiveDamage(DamageInfo info)
    {
        float max = data.MaxHealth * healthScale;
        float damage = info.Damage;
        if (info.Weight == AttackWeight.Execution) damage = max * data.ExecutionDamageRatio;
        else if (info.InstantKill) damage = max * data.InstantKillDamageRatio;

        DamageInfo shown = info;
        shown.Damage = damage;
        shown.InstantKill = false;
        if (shown.Weight == AttackWeight.Execution) shown.Weight = AttackWeight.Heavy; // 처형 피해도 숫자로 보여 줌
        DamageEvents.Raise(transform.position + Vector3.up * 2.6f, shown);

        health -= damage;
        Flash(hitColor);
        if (state != State.Executed) Flinch(info.Direction, info.Weight != AttackWeight.Light); // 보스도 맞으면 움찔
        if (health <= 0f)
        {
            Die();
            return HitResult.Hit;
        }
        if (!phase2 && health <= max * data.Phase2Ratio) pendingPhase2 = true;
        if (state == State.Executed) return HitResult.Hit;

        AddPosture(info.PostureDamage * data.PostureTakenScale, info.Direction); // 일섬 한 방(체간 100)에 바로 붕괴하던 것 → 배율로 낮춤
        return HitResult.Hit;
    }

    // 간파·패링: 체간이 크게 쌓이고 잠깐 휘청 (붕괴면 처형 기회)
    HitResult BossReceiveParry(GameObject parrier)
    {
        Vector3 dir = Flat(transform.position - parrier.transform.position).normalized;
        Flash(parryColor);
        AddPosture(data.ParryPosture, dir);
        if (!postureBroken) EnterStun(true, dir, data.ParryStunTime);
        Log($"간파당함 · 체간 {posture:0}/{data.MaxPosture:0}");
        return HitResult.Countered;
    }

    void AddPosture(float amount, Vector3 direction)
    {
        if (amount <= 0f || postureBroken) return;
        posture = Mathf.Min(data.MaxPosture, posture + amount);
        lastPostureHit = localTime;
        if (posture >= data.MaxPosture && CanBreakNow()) BreakPosture(direction); // 지금 못 끊는 동작이면 추적으로 돌아올 때 붕괴
    }

    bool CanBreakNow() =>
        state != State.BossRoar && state != State.BossLeapAir && state != State.Executed && state != State.Dead
        && !(state == State.BossGrab && grabConnected);

    void BreakPosture(Vector3 hitDirection)
    {
        if (swingActive) EndSwing();
        currentAttack = null;
        DestroyTelegraph();
        if (!controller.enabled) controller.enabled = true;

        postureBroken = true;
        posture = data.MaxPosture;
        moveSpeed = 0f;
        stunDuration = data.PostureBreakStun;
        attackReadyTime = Mathf.Max(attackReadyTime, localTime + stunDuration);
        if (animator.HasState(0, BossStaggerHash)) animator.CrossFadeInFixedTime(BossStaggerHash, 0.05f, 0, 0f);
        animLockUntil = localTime + stunDuration;
        Push(hitDirection, data.HeavyKnockback);
        SetState(State.Stun);
        OpenExecution(stunDuration);

        Flash(postureBreakColor);
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.8f, "체간 붕괴!", DamageEvents.BreakColor);
        if (bossCamera != null) bossCamera.Shake(0.18f, 0.25f);
        Log($"체간 붕괴 · {stunDuration:F1}초");
    }

    // ===== 처형 뒤 기상 =====
    bool BossExecutedFinished()
    {
        if (!IsBoss || health <= 0f || stateTime < 0.5f) return false;
        if (stateTime > 10f) return true;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        foreach (int hash in ExecutedHashes)
            if (info.shortNameHash == hash) return info.normalizedTime >= 0.95f && !animator.IsInTransition(0);
        return false;
    }

    void BossGetUp()
    {
        executed = false;
        executableUntil = -1f;
        controller.enabled = true;
        postureBroken = false;
        posture = 0f;
        if (animator.HasState(0, BossGetUpHash)) animator.CrossFadeInFixedTime(BossGetUpHash, 0.15f, 0, 0f);
        stunDuration = 1.6f;
        attackReadyTime = localTime + 2f;
        SetState(State.Stun);
        Log("기상");
    }

    void BossOnDie()
    {
        if (!IsBoss) return;
        ReleaseGrabbedPlayer();
        DestroyTelegraph();
    }

    // ===== 간파·저스트 회피 판정용 =====
    float BossTimeUntilHit()
    {
        if (!IsBoss) return -1f;
        switch (state)
        {
            case State.BossGrabWindup: return Mathf.Max(0f, data.GrabWindup - stateTime);
            case State.BossGrab: return grabConnected || grabDodged ? -1f : 0f;
            case State.BossLeapWindup: return Mathf.Max(0f, data.LeapWindup - stateTime) + data.LeapAirTime;
            case State.BossLeapAir: return Mathf.Max(0f, data.LeapAirTime - stateTime);
        }
        return -1f;
    }

    AttackData BossUpcomingAttack()
    {
        if (!IsBoss) return null;
        if (state == State.BossGrabWindup || state == State.BossGrab) return data.GrabAttack;
        if (IsBossRanged) return data.LeapAttack;
        return null;
    }

    bool BossColor(out Color color)
    {
        color = BaseTint;
        if (!IsBoss) return false;
        float pulse = 0.5f + 0.5f * Mathf.Sin(localTime * 18f);
        switch (state)
        {
            case State.BossGrabWindup:
                float k = Mathf.Clamp01(stateTime / Mathf.Max(data.GrabWindup, 0.01f));
                color = Color.Lerp(BaseTint, grabTellColor, (0.4f + 0.6f * k) * (0.7f + 0.3f * pulse)); // [변경] 예고 첫 순간부터 붉게 (k²는 앞 절반이 거의 안 보였음)
                return true;
            case State.BossGrab:
                if (grabConnected) return false;
                color = grabTellColor;
                return true;
            case State.BossLeapWindup:
                color = Color.Lerp(BaseTint, windupColor, Mathf.Clamp01(stateTime / Mathf.Max(data.LeapWindup, 0.01f)));
                return true;
            case State.BossRoar:
                color = Color.Lerp(phase2Tint, grabTellColor, pulse);
                return true;
        }
        if (state == State.Attack && holdLeft > 0f && currentAttack != null && stateTime / currentAttack.Duration >= holdAt) // 지연 베기 멈칫 = 주황 예고
        {
            color = Color.Lerp(BaseTint, windupColor, 0.5f + 0.5f * pulse);
            return true;
        }
        if (postureBroken && state == State.Stun)
        {
            color = Color.Lerp(BaseTint, postureBreakColor, 0.35f + 0.35f * pulse);
            return true;
        }
        return false;
    }
}
