using System.Collections.Generic;
using UnityEngine;

// 칼 표시: 전투 자세 = 손에 든 칼, 평상시 = 허리 칼집 속 칼 (Samurai 모델의 weapon/katana, 골반에 붙은 스킨 메시)
// 애니메이션 팩 클립은 평상시에도 weapon_r로 칼을 허리 쪽에 옮기는데, 그 값이 예전 모델의 손 뼈 기준이라 새 모델에선 엉뚱한 곳에 보임
// → 평상시엔 손의 칼을 그리지 않고 칼집 속 칼을 보여줌. 납도 모션은 클립 그대로 끝까지 보여준 뒤(swapAt) 바꿈
// [변경] 칼집 끌어당기기 시도 코드(pullIntoSheath·insertStart·칼집 측정) 삭제 → 표시 전환만 남김
// renderer.forceRenderingOff만 써서, 일섬 중 몸 숨기기(enabled)·칼 종류 전환(active)과 겹치지 않음
// PlayerStance가 실행 중 자동으로 붙임. 칼집 속 칼이 없는 모델이면 아무것도 하지 않음
public class SheathedSwordVisual : MonoBehaviour
{
    [SerializeField] string handSwordRoot = "weapon_r";
    [SerializeField] string sheathedSwordPath = "weapon/katana";
    [SerializeField, Range(0f, 1f)] float swapAt = 0.92f; // 납도 모션이 끝나는 지점(PlayerStance.sheatheEnd)에서 칼집 속 칼로 바꿈

    PlayerStance stance;
    readonly List<Renderer> handRenderers = new List<Renderer>();
    Renderer sheathed;
    int shownInHand = -1; // -1 = 아직 적용 전

    void Awake()
    {
        stance = GetComponent<PlayerStance>();
        Collect();
    }

    void Collect()
    {
        Animator animator = GetComponentInChildren<Animator>(true);
        if (animator == null) return;
        Transform model = animator.transform;

        Transform sheathedTransform = model.Find(sheathedSwordPath);
        if (sheathedTransform == null) return;
        sheathedTransform.gameObject.SetActive(true); // 스킨 셋업에서 꺼 둔 칼집 속 칼
        sheathed = sheathedTransform.GetComponent<Renderer>();

        Transform hand = FindDeep(model, handSwordRoot);
        if (hand == null) return;
        foreach (Renderer r in hand.GetComponentsInChildren<Renderer>(true))
        {
            if (r is TrailRenderer || r is ParticleSystemRenderer) continue;
            if (r.GetComponentInParent<WeaponHitbox>(true) != null) continue; // 판정 쪽은 건드리지 않음
            handRenderers.Add(r);
        }
    }

    void LateUpdate()
    {
        if (sheathed == null || stance == null) return;

        // 핵심: 전투 자세이거나, 납도 모션이 아직 끝나기 전이면 손에 든 칼
        float progress = stance.SheatheProgress;
        bool inHand = stance.InCombatStance || (progress >= 0f && progress < swapAt);

        int state = inHand ? 1 : 0;
        if (state == shownInHand) return;
        shownInHand = state;

        foreach (Renderer r in handRenderers)
            if (r != null) r.forceRenderingOff = !inHand;
        sheathed.forceRenderingOff = inHand;
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
