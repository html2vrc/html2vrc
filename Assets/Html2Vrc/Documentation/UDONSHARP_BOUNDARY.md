# UdonSharp 연결 구조

이 프로젝트에는 VRChat Worlds SDK 3.10.1과 SDK에 통합된 UdonSharp가 설치되어 있다. UDOM 파서와 레이아웃 생성 코어는 VRChat SDK에 직접 의존하지 않고, VRChat 실행 부분만 별도 어셈블리로 분리한다.

## 실제 동작 경로

1. UDOM Button은 `action`과 `targetSlot`만 표현한다.
2. `UdomBuilder`는 VRChat 프로젝트에서 `UdomUdonSafeAction : UdonSharpBehaviour`를 만든다.
3. Button의 Persistent OnClick은 UdonSharp 프록시가 아니라 실제 `VRC.Udon.UdonBehaviour.SendCustomEvent("Execute")`를 호출한다.
4. `UdomUdonSafeAction.Execute()`는 미리 허용한 enum 동작만 실행한다.
5. `UdomGeneratedRoot`의 외부 슬롯이 바뀌면 Editor 연결 계층이 Udon 프록시와 backing behaviour에 대상 참조를 다시 복사한다.
6. 재생성할 때 안정 ID가 같은 Button과 UdonBehaviour를 재사용하므로 외부 대상과 사용자 Persistent Listener가 유지된다.

지원하는 동작은 `ToggleActive`, `SetActive`, `SetInactive`, `ClosePanel`, `ToggleLight`다. 임의 JavaScript, 문자열로 지정한 메서드 실행, AI가 매번 생성하는 Udon 코드는 허용하지 않는다.

## 어셈블리 경계

- `Html2Vrc.Runtime`: UDOM 데이터, 생성 마커, 외부 참조 레지스트리, SDK가 없을 때 쓰는 Unity 폴백 동작
- `Html2Vrc.VRChat.Runtime`: UdonSharp가 컴파일하는 작은 안전 동작 전용 어셈블리
- `Html2Vrc.Editor`: UDOM 생성기, UdonSharp program asset 준비, proxy/backing 동기화, VRChat 빌드 검증

UdonSharp 컴파일러가 일반 Unity 메타데이터 컴포넌트까지 번역하지 않도록 전용 어셈블리를 좁게 유지하는 것이 핵심이다.

## 확인된 것과 남은 경계

- UdonSharp program asset 생성과 컴파일: 확인
- 생성 Button의 UdonBehaviour 이벤트 연결: 확인
- 외부 Light 참조를 Udon backing behaviour에 반영: 확인
- Unity Play Mode에서 Button이 Light와 Panel 상태 변경: 확인
- VRChat SDK PC 월드 번들 빌드: 확인
- Batch Mode ClientSim: 시작과 초기화는 확인했지만 입력 장치 부재로 SDK 내부 예외 발생
- 실제 VRChat 클라이언트 Build & Test 실행, 로컬 번들 로딩, Udon 구성: 확인
- 실제 클라이언트에서 패널을 보고 Button 클릭: 아바타 내부 시점에 가려 미확인
- Quest, 네트워크 동기화, 다중 사용자 권한: 미확인

다음 단계에서는 이 경계를 넓히기보다, 기본 아바타나 정상 시점으로 같은 로컬 테스트 월드에 들어가 패널 표시와 고정 Behaviour의 Button 클릭을 먼저 확인해야 한다.
