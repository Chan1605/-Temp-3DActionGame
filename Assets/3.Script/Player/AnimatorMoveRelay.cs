using System;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class AnimatorMoveRelay : MonoBehaviour
{
    Animator animator;

    public event Action<Vector3, Quaternion> Moved;

    void Awake() => animator = GetComponent<Animator>();

    // 이 콜백이 있으면 루트 모션은 자동 적용되지 않고, 구독한 쪽이 필요할 때만 직접 적용
    void OnAnimatorMove() => Moved?.Invoke(animator.deltaPosition, animator.deltaRotation);
}
