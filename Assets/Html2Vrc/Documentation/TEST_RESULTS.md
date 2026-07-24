# 기술 검증 결과

검증 일자: 2026-07-25

검증 환경: Windows 11, Unity 2022.3.22f1, TextMeshPro 3.0.6, Unity UI 1.0.0

## 자동 검증

Unity Test Framework 결과:

- 전체 7개
- 통과 7개
- 실패 0개
- 건너뜀 0개
- Play Mode 전환 포함

검증한 항목:

1. 샘플 UDOM이 오류/경고 없이 Validation을 통과한다.
2. 알 수 없는 JSON 속성과 중복 안정 ID를 명확한 오류로 거부한다.
3. Canvas, TextMeshProUGUI, Image, Button, ScrollRect, LayoutGroup이 생성된다.
4. Button OnClick이 Edit Mode와 Play Mode에서 외부 Light를 실제로 켜고 끈다.
5. ToggleActive Button이 외부 GameObject의 활성 상태를 실제로 반전한다.
6. ClosePanel Button이 Play Mode에서 생성 패널을 비활성화한다.
7. Embed Anchor가 외부 Light를 참조하되 외부 오브젝트를 자식으로 옮기지 않는다.
8. 텍스트와 배경색을 바꾼 JSON으로 재생성하면 기존 GameObject에 변경이 반영된다.
9. 재생성 뒤 외부 슬롯 참조와 사용자가 추가한 Button listener가 유지된다.
10. 생성 마커가 없는 사용자 GameObject가 재생성 뒤에도 유지된다.
11. 같은 JSON을 반복 재생성해도 새 노드가 생기거나 안정 ID가 중복되지 않는다.
12. Scroll Content가 실제 높이를 가지며 첫 항목이 Viewport와 교차한다.
13. 저장된 샘플 Scene을 다시 열어 외부 Light 참조, 핵심 UI 컴포넌트, Missing Script 부재를 확인한다.

최종 테스트 실행 결과는 로컬 `Artifacts/FinalVerificationTests.xml`에 남겼다. `Artifacts/`는 일회성 검증 산출물이므로 Git에서는 제외한다.

## 시각 검증

`Tools > HTML2VRC > Build Sample World Settings Scene`과 같은 빌더를 배치 모드에서 실행해 샘플 Scene과 1600×900 PNG를 만들었다.

확인 결과:

- 제목과 설명 TextMeshPro 텍스트가 보인다.
- 상단 강조 Image와 패널 배경색이 보인다.
- 두 Button과 라벨이 보인다.
- ScrollView 안에 네 개 항목이 보이며 마지막 항목 일부가 Viewport에서 잘려 스크롤 가능한 구조임을 확인했다.
- 캡처 이미지는 로컬 `Artifacts/WorldSettingsSample.png`에 남겼다.

## 컴파일과 로그

- Unity 배치 컴파일 종료 코드: 0
- 최종 전체 테스트 종료 코드: 0
- 샘플 Scene/PNG 빌드 종료 코드: 0
- 최종 테스트와 샘플 빌드 로그에서 C# 컴파일 오류, NullReferenceException, MissingReferenceException, Assertion을 찾지 못했다.
- Unity 라이선스 클라이언트가 시작 시 서명/액세스 토큰 메시지를 기록했지만 즉시 라이선스를 갱신했고 모든 작업은 종료 코드 0으로 끝났다.
- 종료 시 `Curl error 42: Callback aborted`가 기록될 수 있으며, Batch Mode 종료 중 네트워크 요청 취소 메시지로 기능 실패는 아니었다.

## 미확인

- VRChat Worlds SDK와 UdonSharp는 이 프로젝트에 설치되어 있지 않다.
- 따라서 UdonSharp 프로그램 컴파일, ClientSim, VRChat Build & Test는 미확인이다.
- 실제 VR 헤드셋 가독성, Quest 성능, 네트워크 동기화는 미확인이다.
- Sprite 경로 지원은 구현 및 Validation 대상이지만 샘플은 외부 Sprite 에셋을 사용하지 않는다.

## 결론

Unity 전용 범위에서는 “사람이 읽을 수 있는 UDOM → 수정 가능한 네이티브 UI → 안전한 Button 동작 → 외부 참조를 유지하는 재생성” 경로가 실제로 성립했다.

VRChat 제품 가능성까지 확인됐다고 보기는 이르다. 다음 검증은 별도 VRChat Worlds SDK 프로젝트에서 같은 생성 코어를 사용하고 `UdomSafeAction`만 UdonSharp 구현으로 교체해 ClientSim과 Build & Test를 통과시키는 것이다.
