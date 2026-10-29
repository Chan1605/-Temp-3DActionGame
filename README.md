# -Temp-3DActionGame

> 약·강 공격을 섞는 **분기 콤보**와 **벽력일섬** 스킬을 중심으로 한 3D 근접 액션 게임 · Unity 개인 프로젝트 (개발 중)

검은신화: 오공의 간파처럼 타격 순간의 손맛(히트스톱, 카메라, 이펙트)을 레퍼런스로 삼되, 게임의 중심은 다양한 콤보와 스킬로 풀어가는 근접 전투입니다.

## 개발 환경

| 항목 | 내용 |
| --- | --- |
| Engine | Unity 6 (6000.2.8f1), URP |
| Language | C# |
| Input | Input System |
| Tween | DOTween |

## 현재 구현

### 이동
- 카메라 기준 이동 · 달리기, 바라보는 방향으로만 속도를 내서 급선회 시 미끄러지지 않음
- Idle / Walk / Run / Sprint 4단계 블렌드 트리, 클립 원래 속도 대비 재생 배속을 맞춰 발 미끄러짐 감소

### 분기 콤보
- 약공격(□) · 강공격(△) 입력에 따라 다음 공격이 갈라지는 콤보 트리
- 콤보 연결은 코드가 아니라 `AttackData`(ScriptableObject)끼리의 참조로 구성 → 에셋 연결만으로 새 콤보 추가
- 입력 버퍼, 판정 구간, 다음 타 전환 시점을 정규화 시간으로 데이터에 기입
- 판정이 끝나면 이동 입력으로 즉시 캔슬, 공격 중에만 클립 루트 모션을 적용해 앞으로 내딛으며 베기

| 입력 | 콤보 |
| --- | --- |
| □□□□ | 기본 4연격 |
| □□□△ | 3연격 → 강한 마무리 |
| □□△△ | 2연격 → 강공격 2연타 |
| □△△ | 1타 → 강공격 분기 |
| △△△ | 강공격 3연격 |

### 벽력일섬 (Q)
- **즉발 / 차지**: 짧게 누르면 바로, 길게 누르면 슬로모션 속에서 단계별로 사거리 증가
- **돌진**: 대상 뒤로 0.1초 만에 이동, 벽 앞에서는 멈춤, 적이 없어도 정면으로 발동
- **연쇄**: 착지 직후 다시 누르면 아직 베지 않은 다음 적에게 이어서 돌진 (최대 6연)
- **지연 베기**: 경로에 걸린 적은 마지막 착지 후 한 박자 늦게 한꺼번에 처리 + 히트스톱 · 카메라 셰이크
- 수치는 `SkillData`(ScriptableObject)로 분리

### 판정 구조
- 공격자는 `DamageInfo`만 전달하고, 피격자(`IDamageable`)가 자기 상태로 결과(Hit / Countered / Ignored)를 결정
- 콤보·스킬·허수아비가 서로의 구체 타입을 모르고 인터페이스로만 연결

## 조작 (키보드·마우스)

| 입력 | 행동 |
| --- | --- |
| WASD | 이동 |
| Shift | 달리기 |
| 마우스 | 카메라 |
| 좌클릭 | 약공격 (□) |
| 우클릭 | 강공격 (△) |
| Q | 벽력일섬 (짧게 = 즉발, 길게 = 차지, 착지 후 다시 = 연쇄) |

## 실행 방법

이 레포에는 라이선스상 재배포할 수 없는 유료/외부 에셋이 포함되어 있지 않습니다.
프로젝트를 실행하려면 아래 에셋을 직접 임포트해야 합니다.

1. [Sword Animations Pack](https://assetstore.unity.com/packages/3d/animations/sword-animations-pack-256301) → `Assets/ThirdParty/SwordAnimationPack`
2. [DOTween (HOTween v2)](https://assetstore.unity.com/packages/tools/animation/dotween-hotween-v2-27676) → `Assets/Plugins/Demigiant`
3. `ThirdParty/SwordAnimationPack/Model/9CG_Sword.FBX` 의 Rig를 **Humanoid (Create From This Model)** 로 변경 후 Apply
4. `Assets/1.Scene/SampleScene` 열기
5. 메뉴에서 아래 셋업을 순서대로 한 번 실행 (ThirdParty 클립의 Loop Time 등 레포에 포함되지 않는 설정을 다시 맞춤)
   - `Tools/Setup/Player Locomotion`
   - `Tools/Setup/Combat Test`
   - `Tools/Setup/Thunder Skill`

## 에디터 셋업 메뉴

| 메뉴 | 하는 일 |
| --- | --- |
| Player Locomotion | Animator Controller · 로코모션 블렌드 트리 생성, Player 루트·카메라 연결 |
| Combat Test | 콤보 트리(AttackData 12개)와 Animator 상태 생성, 히트박스·허수아비 배치 |
| Thunder Skill | 스킬 Animator 상태 · SkillData · 트레일 생성, 연쇄 테스트용 허수아비 배치 |
| Equip Katana | 카타나 모델 장착, 머티리얼 생성, 히트박스를 날에 맞춤 |
| Flip Katana Edge | 칼날 방향 반전 |
| Remove Old Sword | 기존 검 제거 전 히트박스를 분리 |

## 폴더 구조

```
Assets/
  1.Scene/        씬
  2.Model/        모델, URP 설정, 프리팹(Player), 무기
  3.Script/       스크립트 (Player / Combat / Camera / Enemy / Editor)
  4.Sprite/       UI 이미지
  5.Animation/    Animator Controller
  6.Materials/    머티리얼
  7.PhysiceMaterials/
  8.Audio/        사운드
  9.Font/         폰트
  10.Input/       Input Actions
  11.Data/        AttackData, SkillData (ScriptableObject)
  ThirdParty/     외부 에셋 (레포 미포함)
```

## 사용 에셋 및 라이선스

| 에셋 | 제작 | 라이선스 |
| --- | --- | --- |
| Sword Animations Pack | Studio9CG | Unity Asset Store EULA (레포 미포함) |
| DOTween | Demigiant | Unity Asset Store (레포 미포함) |
| Pretendard | 길형진 (orioncactus) | SIL Open Font License 1.1 |
| Zenitsu Katana (임시 무기 모델) | 출처 기입 필요 | 확인 필요 · 개인 학습용 임시 사용, 공개 빌드 전 교체 예정 |
