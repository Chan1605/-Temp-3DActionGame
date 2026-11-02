using UnityEngine;

// 저스트 회피·간파가 "곧 들어올 공격"을 판단할 때 쓰는 적 쪽 정보
public interface IAttackThreat
{
    // targetPosition을 향한 공격이 window초(실제 시간) 안에 판정에 들어가거나 지금 판정 중이면 true
    bool IsAboutToHit(Vector3 targetPosition, float window);
    bool IsParryable { get; }
    HitResult ReceiveParry(GameObject parrier);
}
