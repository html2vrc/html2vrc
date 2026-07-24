# 기술 검증 결과

검증 일자: 2026-07-25

검증 환경: Windows 11, Unity 2022.3.22f1, TextMeshPro 3.0.6, Unity UI 1.0.0, VRChat Worlds SDK 3.10.1, 통합 UdonSharp/ClientSim

## 자동 회귀 검증

HTML2VRC 전용 Unity Test Framework 테스트 7개를 실행한다. 검증 범위는 다음과 같다.

1. 샘플 UDOM Validation 성공과 잘못된 속성/중복 ID 거부
2. Canvas, TextMeshProUGUI, Image, Button, ScrollRect, LayoutGroup과 VRChat용 `VRCUiShape` 생성
3. 생성 Button의 Persistent Listener가 `VRC.Udon.UdonBehaviour.SendCustomEvent`를 가리키는지 확인
4. Edit Mode와 Play Mode에서 외부 Light, 외부 GameObject, 자기 Panel 상태 변경
5. Embed가 외부 오브젝트를 소유하거나 자식으로 옮기지 않는지 확인
6. 텍스트와 배경색 재생성 반영
7. 재생성 뒤 외부 슬롯, 사용자 오브젝트, 사용자 Button Listener 유지
8. 반복 재생성 시 안정 ID와 GameObject 중복 방지
9. Scroll Content와 Viewport 배치 확인
10. 저장된 샘플 Scene의 VRC Scene Descriptor, spawn, 사용자 소유 BoxCollider 바닥, 외부 참조, 핵심 UI, Missing Script 부재 확인

최종 실행 결과:

- 전체 7개
- 통과 7개
- 실패 0개
- 건너뜀 0개
- Unity 종료 코드 0
- C# 컴파일 오류, NullReferenceException, MissingReferenceException, AssertionException 없음

## UdonSharp와 VRChat 빌드

- `UdomUdonSafeAction` 전용 UdonSharp assembly definition과 program asset을 생성했다.
- UdonSharp 동기 컴파일 후 program asset에 컴파일 오류가 없음을 확인했다.
- 샘플 Scene에 `VRCSceneDescriptor`, player spawn, `PipelineManager`가 포함된다.
- VRChat 공식 레이어와 Collision Matrix를 적용했다.
- VRChat SDK 공개 `IVRCSdkWorldBuilderApi.Build()`로 PC 월드 번들을 만들었다.
- 빌드 결과: 성공
- 생성 형식: `scene-standalonewindows64-worldsettingssample-*.vrcw`
- 업로드는 수행하지 않았다.

이 결과는 생성된 UI와 Udon 프로그램이 VRChat SDK의 Validation과 로컬 월드 빌드 파이프라인을 통과했다는 뜻이다.

## VRChat Build & Test

`IVRCSdkWorldBuilderApi.BuildAndTest()` 실행 결과:

- SDK Build & Test 종료 코드 0
- VRChat 데스크톱 클라이언트 실행 확인
- 명령줄이 생성한 `WorldSettingsSample` 로컬 `.vrcw`를 가리키는지 확인
- VRChat 로그에서 `Entering Room: Local Test`, `Loading asset bundle: WorldSettingsSample`, `Loaded asset bundle`, `Configuring Udon` 확인
- 20×20m BoxCollider 바닥을 추가한 뒤 Scene 재생성, 자동 테스트, Build & Test를 다시 통과
- 바닥은 UDOM 생성 루트 밖에 있어 패널 Regenerate의 삭제 대상이 아님
- 첫 수동 확인에서 버튼이 반응하지 않는 원인을 Canvas의 `VRCUiShape` 누락으로 재현했다.
- 누락을 잡는 회귀 테스트를 먼저 실패시킨 뒤, VRChat 월드 공간 Canvas 생성/재생성 시 `VRCUiShape`를 자동 추가하도록 수정하고 7개 테스트와 Build & Test를 다시 통과했다.
- 실제 패널 표시는 사용자 확인, 수정된 빌드에서의 Button 클릭 결과는 사용자 재확인 대기

바닥의 Scene 저장과 빌드 포함은 확인했지만, 실제 캐릭터가 바닥 위에 서는 장면은 사용자 입력을 건드리지 않고 확인할 수 없어 미확인으로 남긴다. 또한 월드 진입 시 VRChat 클라이언트 내부의 난독화된 `Start()` 스택에서 `NullReferenceException` 한 건이 기록됐다. HTML2VRC Udon program에는 `Start` 이벤트가 없어 직접 원인으로 확인되지는 않았지만, 클라이언트 콘솔 오류이므로 불안정 항목으로 남긴다.

## 시각 검증

샘플 Scene을 배치 모드에서 다시 만들고 1600×900 PNG를 렌더링했다.

- 제목과 설명 TextMeshPro 텍스트 표시
- 상단 Image와 Panel 배경색 표시
- 두 Button과 라벨 표시
- ScrollView 안의 네 항목 표시와 Viewport clipping 확인

로컬 검증 이미지는 `Artifacts/WorldSettingsSample.png`에 생성되며 Git에서는 제외한다.

## ClientSim 결과

ClientSim이 VRC Scene Descriptor를 인식해 시작했고, 로컬 플레이어를 만들고 지정한 spawn으로 이동한 뒤 `ClientSim Initialized`까지 기록한 것은 확인했다.

하지만 Unity Batch Mode에서는 입력 장치가 만들어지지 않아 ClientSim 3.10.1 내부 `ClientSimPlayerController.GetMovementInput()`에서 `NullReferenceException`이 반복됐다. 따라서 헤드리스 ClientSim 전체 테스트 결과는 **실패**다. HTML2VRC Button assertion 실패가 아니라 SDK 입력 시뮬레이션 경로의 실패지만, 콘솔 오류이므로 성공으로 처리하지 않는다.

## 미확인

- 실제 VRChat 클라이언트에서 생성 패널 표시와 Button 클릭
- 실제 VRChat 캐릭터가 추가한 바닥 Collider 위에 멈추는지
- 일반 Unity Editor에서 사람이 조작하는 ClientSim
- VR 헤드셋 가독성
- Android/Quest 빌드와 성능
- 네트워크 동기화와 다중 사용자 권한
- 외부 Sprite를 사용한 샘플

## 결론

“사람이 읽을 수 있는 UDOM → 수정 가능한 Unity 네이티브 UI → 고정된 UdonSharp 동작 → 외부 참조를 유지하는 재생성 → VRChat PC 월드 번들” 경로는 성립했다.

남은 가장 큰 불확실성은 실제 VRChat 클라이언트 안에서의 상호작용이다. 다음 검증은 새 기능 추가보다 기본 아바타로 Build & Test 재실행, 실제 버튼 조작, 재생성 후 재빌드 순서로 진행하는 것이 맞다.
