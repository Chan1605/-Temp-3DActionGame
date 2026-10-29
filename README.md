# -Temp-3DActionGame

> 타이밍을 맞춘 **간파(패링)** 의 손맛에 집중한 3D 액션 게임 · Unity 개인 프로젝트 (개발 중)

검은신화: 오공의 간파 시스템에서 영감을 받아, 적의 공격에 맞춰 반격하는 순간의 피드백(히트스톱, 카메라, 이펙트)을 설계·구현하는 것을 목표로 합니다.

## 개발 환경

| 항목 | 내용 |
| --- | --- |
| Engine | Unity 6 (6000.2.8f1), URP |
| Language | C# |
| Input | Input System |
| Tween | DOTween |

## v1 목표 범위

- 플레이어: 콤보 공격, 간파 + 기세 게이지, 회피(무적 구간), 락온
- 적: 일반 적, 보스 1체 (2페이즈)
- 피드백: 히트스톱, 카메라 임펄스, 타격 이펙트·사운드
- 확장성: 공격·무기 데이터(ScriptableObject), 피격 판정 규칙, 피드백 레이어 분리

## 실행 방법

이 레포에는 라이선스상 재배포할 수 없는 유료/외부 에셋이 포함되어 있지 않습니다.
프로젝트를 실행하려면 아래 에셋을 직접 임포트해야 합니다.

1. [Sword Animations Pack](https://assetstore.unity.com/packages/3d/animations/sword-animations-pack-256301) → `Assets/ThirdParty/SwordAnimationPack`
2. [DOTween (HOTween v2)](https://assetstore.unity.com/packages/tools/animation/dotween-hotween-v2-27676) → `Assets/Plugins/Demigiant`
3. `Assets/1.Scene/SampleScene` 열기

## 폴더 구조

```
Assets/
  1.Scene/        씬
  2.Model/        모델, URP 설정, 프리팹
  3.Script/       스크립트
  4.Sprite/       UI 이미지
  5.Animation/    Animator Controller, 애니메이션
  6.Materials/    머티리얼
  7.PhysiceMaterials/
  8.Audio/        사운드
  9.Font/         폰트
  10.Input/       Input Actions
  ThirdParty/     외부 에셋 (레포 미포함)
```

## 사용 에셋 및 라이선스

| 에셋 | 제작 | 라이선스 |
| --- | --- | --- |
| Sword Animations Pack | Studio9CG | Unity Asset Store EULA (레포 미포함) |
| DOTween | Demigiant | Unity Asset Store (레포 미포함) |
| Pretendard | 길형진 (orioncactus) | SIL Open Font License 1.1 |
