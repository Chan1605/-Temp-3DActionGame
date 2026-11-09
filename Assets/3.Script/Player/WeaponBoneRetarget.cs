using UnityEngine;

// 칼 쥔 뼈(weapon_r) 손 기준 보정
// 애니메이션 팩 클립은 weapon_r을 손(hand_r) 기준으로 움직이는데, 그 값은 예전 모델의 손 뼈 축 기준
// → 애니메이터가 값을 쓴 뒤 "예전 손 축 → 새 손 축" 회전을 앞에 곱해, 어떤 클립이든 칼이 예전 모델과 같은 궤적으로 움직이게 함
//   (손 안에서 칼을 돌리는 털기·납도 동작까지 그대로 따라감)
// 칼 판정(WeaponHitbox)·궤적이 LateUpdate에서 칼 위치를 읽으므로 그보다 먼저 실행
// 보정값은 Tools/Setup/Samurai Weapon Grip Fix가 두 모델의 같은 자세를 비교해 넣음. 미세 조정은 weapon_r/WeaponAdjust로
// [변경] 기본 꺼짐: Animation Picker 미리보기(보정 없음 + WeaponAdjust Y -135)가 더 자연스러워서. 비교하려면 applyOffset을 켬
[DefaultExecutionOrder(-100)]
public class WeaponBoneRetarget : MonoBehaviour
{
    [SerializeField] Transform bone;                                   // weapon_r
    [SerializeField] Quaternion handRotationOffset = Quaternion.identity; // 새 손 축 기준으로 본 예전 손 축
    [SerializeField] bool applyOffset = false; // [변경]

    Vector3 writtenPosition;
    Quaternion writtenRotation;
    bool hasWritten;

    void LateUpdate()
    {
        if (!applyOffset || bone == null) return; // [변경]

        // 이번 프레임 애니메이터가 값을 쓰지 않았으면(우리가 쓴 값 그대로면) 다시 곱하지 않음 → 보정이 누적되지 않게
        if (hasWritten && bone.localPosition == writtenPosition && bone.localRotation == writtenRotation) return;

        writtenRotation = handRotationOffset * bone.localRotation;
        writtenPosition = handRotationOffset * bone.localPosition;
        bone.localRotation = writtenRotation;
        bone.localPosition = writtenPosition;
        hasWritten = true;
    }
}
