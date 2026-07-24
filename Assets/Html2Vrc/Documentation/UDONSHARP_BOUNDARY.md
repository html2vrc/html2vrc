# UdonSharp 교체 경계

이 프로토타입을 만든 시점의 저장소에는 VRChat Worlds SDK와 UdonSharp가 없었다. 요청에 따라 패키지를 임의 설치하지 않았고, 동작 검증은 `UdomSafeAction : MonoBehaviour`로 수행한다.

현재 UDOM과 생성기 사이의 경계는 의도적으로 좁다.

1. UDOM Button은 `action`과 `targetSlot`만 표현한다.
2. `UdomBuilder`는 Button의 OnClick을 `UdomSafeAction.Execute()`에 연결한다.
3. `UdomSafeAction`은 정해진 enum 동작만 실행한다.
4. 외부 대상은 `UdomGeneratedRoot`의 직렬화된 슬롯 레지스트리에서 찾는다.

VRChat 프로젝트로 옮길 때는 아래 작업만 별도 패키지/조건부 컴파일 계층에서 수행하면 된다.

- `UdomSafeAction`과 같은 직렬화 필드를 가진 `UdonSharpBehaviour` 구현 추가
- Editor 생성기가 SDK 존재 여부를 감지해 MonoBehaviour 또는 UdonSharp 구현 중 하나를 선택
- UdonSharp 프로그램 컴파일과 VRChat ClientSim/Build & Test 검증
- 네트워크 동기화가 필요한 새 동작은 별도 명세로 추가

UDOM 파서나 레이아웃 생성기를 다시 만들 필요는 없다. 임의 JavaScript 또는 AI 생성 Udon 코드를 끼워 넣는 확장점은 의도적으로 제공하지 않는다.
