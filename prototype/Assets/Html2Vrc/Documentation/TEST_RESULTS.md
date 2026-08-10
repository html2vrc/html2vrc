# 기술 검증 결과

검증 일자: 2026-07-25

검증 환경: Windows 11, Unity 2022.3.22f1, TextMeshPro 3.0.6, Unity UI 1.0.0, VRChat Worlds SDK 3.10.1, 통합 UdonSharp/ClientSim

## Canonical UDOM → Unity 통합 검증

추가 검증 일자: 2026-08-10

- `packages/react/test/fixtures/basic.udom.json`을 바꾸지 않고 Unity 호환 계층에 직접 입력했다.
- canonical `asset`, `viewport`, `element/view`, `text`, `image`, `button`, flex column과 gap이 Unity 내부 모델로 정규화되는 것을 확인했다.
- 정규화된 문서가 실제 TextMeshProUGUI, Image, Button을 생성하는 것을 Unity Test Framework로 확인했다.
- canonical 명세에 없는 element 이름을 명확한 검증 오류로 거부하는 회귀 테스트를 유지한다.
- 상대 image URI `assets/logo.png`는 Unity `Assets/` 경로가 아니므로 경고를 남기고 참조를 추측하지 않는다.
- canonical `on.activate` 심볼은 임의 코드로 실행하지 않고, 안전한 binding이 없는 Button 경고로 유지한다.
- shared `styleRefs`를 배열 순서대로 병합하고 inline `style`이 마지막에 덮어쓰는 것을 Node와 Unity 공유 fixture로 확인했다.
- 공식 `settings.udom.json` 전체를 읽어 gradient·radius·font 폴백 진단, cross-axis 정렬, font weight, Image, Unity Toggle을 생성했다.
- Toggle의 `bind.checked`와 `on.change`는 임의 코드로 실행하지 않고 binding manifest가 필요하다는 경고를 남긴다.
- `CanonicalRelativeImage.udom.json`의 문서 상대 URI를 실제 Texture2D로 해석해 RawImage에 연결했다. `Assets/` 밖으로 나가는 상대 경로는 차단하는 회귀도 함께 확인했다.
- canonical Slider의 min/max/value/step/disabled, `bind.value`, `on.change` 진단을 정규화하고 Unity Slider, fill, handle을 생성했다. min/max가 뒤집힌 공식 invalid fixture도 Unity importer가 거부한다.
- canonical text-input의 value/placeholder/multiline/readOnly/disabled와 `bind.value`, change/submit/focus/blur 진단을 정규화하고 native TMP_InputField, viewport, text, placeholder를 생성했다.
- canonical scroll의 vertical/horizontal/both, design-unit initialOffset, `bind.offset`, scroll/focus/blur 진단을 정규화하고 양축 native ScrollRect 콘텐츠 크기와 좌표 변환을 확인했다. 기존 legacy Horizontal layout의 축 추론도 유지한다.
- canonical viewport의 pixelRatio와 contain/cover/stretch/none을 보존하고, Renderer 목표 Canvas override에 따른 uniform/non-uniform scale, cover/none clipping, override 해제와 안정 wrapper 재생성을 확인했다.
- React exporter가 Toggle, Slider, TextInput, Scroll과 Embed를 canonical 속성·binding·event로 출력하고, 같은 fixture를 Unity가 네이티브 컨트롤로 생성하는 것을 확인했다. 빈 문자열과 0/false 값, Button/Toggle의 focus·blur, 연결 상태에 반응하는 Embed fallbackLabel도 포함한다.

자동 검증 결과:

- `npm run check`: UDOM conformance 19/19, React 5/5, TypeScript typecheck와 build 통과
- Unity EditMode `Html2Vrc.Tests.UdomPrototypeTests`: **20/20 통과, 실패 0**
- Unity 종료 코드 0, C# 컴파일 오류 0
- Windows CRLF에서 기존 HTML 재생성 테스트가 문자열을 교체하지 못하던 문제도 함께 수정했다.

## 제한형 HTML 입력 0.1 추가 검증

- 실행 명령: Unity Batch Mode, `EditMode`, `Html2Vrc.Tests.UdomPrototypeTests`
- 결과: **10개 중 10개 통과, 실패 0**
- 결과 파일: `Artifacts/html-editmode-results-2.xml` (Git 제외 폴더)

새로 확인한 항목:

1. 샘플 `WorldSettings.html`이 오류 없이 UDOM 0.1로 변환된다.
2. 변환 결과에 Panel, TextMeshPro용 Text, Button Binding, ScrollView와 Embed가 포함된다.
3. HTML에서 생성한 UI도 안정 ID로 재생성되고 중복 노드가 생기지 않는다.
4. 재생성 후 `world-light` 외부 GameObject 참조가 유지된다.
5. `onclick` 같은 JavaScript 이벤트와 ID가 없는 요소는 명확한 오류로 거부된다.
6. 기존 UDOM 생성, 버튼 동작, 저장 Scene과 바닥 검증도 함께 통과했다.

주의:

- HTML 전용 샘플 Scene을 별도로 생성하고 VRChat Build & Test로 실행했다.
- HTML 화면에만 있는 설명 문구가 VRChat 안에 표시되어 기존 UDOM Scene을 재사용한 것이 아님을 확인했다.
- HTML 샘플의 `Toggle World Light`와 `Close Panel`을 사용자가 VRChat에서 직접 정상 동작 확인했다.
- VRChat 로그에도 조명 상태가 `OFF → ON → OFF`로 바뀐 기록이 남아 실제 외부 Light 연결 동작을 확인했다.
- 기존 UDOM 샘플의 두 버튼도 앞선 수동 검증에서 정상 동작했다.
- 첫 Batch 실행 중 SDK 임베디드 폴더가 비어 UdonSharp를 찾지 못했지만, Creator Companion 로컬 캐시의 공식 3.10.1 패키지를 복구한 뒤 컴파일과 테스트가 통과했다.

## 자동 회귀 검증

HTML2VRC 전용 Unity Test Framework 테스트 20개를 실행한다. 검증 범위는 다음과 같다.

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
11. React exporter의 canonical fixture를 Unity 내부 모델로 정규화하고 네이티브 UI 생성
12. canonical 명세에 없는 요소를 묵시하지 않고 명확한 오류로 거부
13. canonical shared style 배열 병합 순서와 inline style 우선순위를 실제 TextMeshPro 결과까지 확인
14. 공식 canonical settings fixture의 시각 폴백 진단과 Unity Toggle 생성, 상징적 bind/on 비실행 보장
15. UDOM source asset 기준 상대 image URI의 Texture2D/RawImage 연결과 `Assets/` 경계 탈출 차단
16. canonical Slider의 native control/fill/handle 생성, 정수 step과 범위 검증, symbolic binding/event 비실행 보장
17. canonical text-input의 단일행/다중행·읽기전용·비활성 상태와 native TMP_InputField 내부 구조, symbolic binding/event 비실행 보장
18. canonical scroll의 vertical/horizontal/both 축, 양축 콘텐츠 크기, initialOffset 좌표 변환, legacy horizontal 호환과 symbolic binding/event 비실행 보장
19. canonical viewport의 pixelRatio 보존과 contain/cover/stretch/none target Canvas scale, clipping, override 해제 및 안정 wrapper 재생성
20. React control fixture의 Toggle, Slider, TextInput, Scroll, Embed 네이티브 생성과 빈 값 보존, focus/blur 진단, 동적 Embed fallback 표시

최종 자동 테스트 결과:

- 전체 20개
- 통과 20개
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
- 기존 UDOM 샘플에서 수정된 두 버튼의 실제 동작을 사용자가 확인했다.

HTML 입력 전용 재검증:

- `WorldSettings.html`을 직접 읽어 `WorldSettingsHtmlSample.unity`를 새로 만드는 전용 경로를 추가했다.
- 전용 Scene에서 Camera, Light, VRC Scene Descriptor, spawn, 바닥 Collider, HTML 생성 UI와 `world-light` 외부 참조를 확인했다.
- `Build & Test HTML Sample World`가 VRChat 데스크톱 클라이언트를 정상 실행했다.
- VRChat 화면에서 HTML 전용 설명 문구, TextMeshPro 글자, 버튼, ScrollView 목록이 표시됐다.
- 사용자가 `Toggle World Light`와 `Close Panel`을 직접 눌러 둘 다 정상 작동함을 확인했다.
- VRChat 로그의 `HTML2VRC_LIGHT_STATE` 기록으로 외부 Light의 활성 상태가 실제로 바뀌었음을 추가 확인했다.

월드 진입 시 VRChat 클라이언트 내부의 난독화된 `Start()` 스택에서 `NullReferenceException` 한 건이 기록된 적이 있다. HTML2VRC Udon program에는 `Start` 이벤트가 없어 직접 원인으로 확인되지는 않았지만, 클라이언트 콘솔 오류이므로 불안정 항목으로 남긴다.

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

- 일반 Unity Editor에서 사람이 조작하는 ClientSim
- VR 헤드셋 가독성
- Android/Quest 빌드와 성능
- 네트워크 동기화와 다중 사용자 권한
- 외부 Sprite를 사용한 샘플

## 결론

“제한된 사람이 읽을 수 있는 HTML → UDOM → 수정 가능한 Unity 네이티브 UI → 고정된 UdonSharp 동작 → 외부 참조를 유지하는 재생성 → VRChat PC 월드 실행” 경로는 성립했다.

다음 핵심 검증은 HTML을 수정한 뒤 같은 Scene을 재생성·재빌드해 외부 연결이 실제 VRChat 실행까지 유지되는지 확인하는 것이다. 그다음 Sprite, 더 복잡한 레이아웃과 VR 헤드셋 가독성을 순서대로 검증하는 편이 맞다.
