# HTML2VRC 프로젝트 마감 보고서

작성일: 2026-08-13
작업 브랜치: `codex/unity-canonical-udom`
기준 구현 커밋: `2dd0a04 Add TSX source live reload`

## 요약

HTML2VRC는 제한된 정적 HTML 또는 React primitive로 작성한 UI를 canonical UDOM 0.1로 표현하고, 이를 수정 가능한 Unity 네이티브 UI와 안전하게 연결된 VRChat UdonSharp 동작으로 생성하는 기술 검증 프로토타입까지 완성했다.

핵심 경로인 “HTML/React → UDOM → Unity UI → 재생성 → VRChat PC Build & Test”는 성립했다. 브라우저 미리보기와 TSX source live reload까지 추가했으며, 최종 Node 검사에서 UDOM 39/39와 React 16/16이 모두 통과했다.

다만 범용 웹사이트 변환기나 완전한 React runtime은 아니다. Quest/VR 헤드셋, 일반 Editor ClientSim 수동 조작, 네트워크 동기화 등은 확인하지 못했거나 이번 프로토타입 범위에서 제외했다.

## 완료한 작업

### 1. 저장소와 공통 계약

- 기존 HTML2VRC 작업은 개인 원격 저장소 `https://github.com/nupamo/skill.git`에 통합된 상태다.
- UDOM 0.1 schema, TypeScript 타입, validator, valid/invalid conformance fixture를 한 계약으로 정리했다.
- 같은 canonical fixture를 Node와 Unity가 공유하도록 구성해 두 구현의 해석 차이를 회귀 테스트로 확인할 수 있게 했다.
- `origin/main` 이후 보고서 작성 직전까지 구현 커밋 51개를 기능 단위로 분리해 남겼다.

### 2. React 저작 환경

- `View`, `Text`, `Image`, `Button`, `Toggle`, `Slider`, `TextInput`, `Scroll`, `Embed` primitive를 구현했다.
- JSX tree를 결정론적인 ID와 canonical UDOM으로 내보내며, 잘못된 bare text·임의 React element·UDOM 위반을 명확히 거부한다.
- style reference 병합, viewport, resource, binding, symbolic event를 보존한다.
- canonical UDOM을 독립형 HTML로 렌더링하는 브라우저 미리보기를 구현했다.
- 로컬 asset allowlist, active URL 차단, HTML escaping, SSE 기반 live reload와 오류 화면 복구를 추가했다.
- `.js`, `.jsx`, `.ts`, `.tsx` source가 UDOM을 export하면 격리된 자식 프로세스에서 평가하고, dependency 변경을 감지해 다시 로드한다.
- source 실행 timeout과 Windows 자식 프로세스 종료/임시 폴더 잠금 문제를 해결했다.

### 3. Unity canonical UDOM importer와 renderer

- UDOM을 Unity 내부 모델로 정규화하고 Canvas, TextMeshProUGUI, Image, Button, Toggle, Slider, TMP_InputField, ScrollRect와 Embed 구조를 생성한다.
- 안정 ID를 사용해 반복 재생성 시 GameObject와 asset GUID를 재사용한다.
- 생성기 소유 오브젝트와 사용자 소유 오브젝트를 구분하고, 외부 GameObject reference와 사용자 Button listener를 보존한다.
- viewport fit, flex layout, grow/shrink/basis, justify/align, order, wrap, absolute positioning, min/max/aspect ratio, overflow, z-index를 지원한다.
- text flow, whitespace, line height, letter spacing, font weight/style, inline run과 font resource를 지원한다.
- image fitting/position, opacity/visibility, border/radius, shadow, linear/radial/conic gradient, 2D transform을 Unity UI material과 wrapper로 렌더링한다.
- 지원하지 않거나 모호한 계약은 임의 추측 대신 validation error 또는 명시적 diagnostic으로 남긴다.

### 4. 제한형 HTML/CSS 입력

- `div`, 제목·문단, 목록, `img`, `button`, checkbox/range/text input, `textarea`, ScrollView, Embed에 해당하는 제한형 HTML을 UDOM으로 변환한다.
- flex, gap, order, position, size constraint, overflow, transform, object-fit, border/radius, shadow, gradient, text flow, inline text style, opacity/visibility/z-index를 source order에 맞게 해석한다.
- `script`, `onclick`, 임의 JavaScript와 지원하지 않는 CSS 문법은 실행하거나 묵시적으로 무시하지 않고 오류로 처리한다.
- HTML 전용 sample scene 생성과 VRChat Build & Test 경로를 마련했다.

### 5. VRChat 안전 경계와 실행 검증

- 임의 JavaScript를 Udon으로 변환하지 않고, 사전에 정의된 symbolic action만 고정된 UdonSharp 동작에 연결한다.
- VRC Scene Descriptor, spawn, Pipeline Manager, 공식 layer/collision 설정과 `VRCUiShape`가 포함된 PC sample world를 생성한다.
- PC `.vrcw` 빌드와 VRChat SDK Build & Test를 통과했다.
- HTML sample과 기존 UDOM sample의 버튼을 VRChat 클라이언트에서 사용자가 직접 눌러 외부 Light 토글과 Panel 닫기 동작을 확인했다.
- 재생성 뒤에도 외부 reference가 유지되는 경로를 자동 테스트와 수동 실행으로 검증했다.

## 최종 검증 결과

| 검사 | 결과 | 비고 |
| --- | ---: | --- |
| UDOM Node conformance | 39/39 통과 | `npm run check` |
| React package tests | 16/16 통과 | renderer, preview, server, TSX reload 포함 |
| TypeScript | 통과 | typecheck와 build |
| npm package dry-run | 통과 | 29 files, TSX source runner 포함 |
| Unity EditMode | 59/59 통과 | 2026-08-13 기록, 종료 코드 0 |
| VRChat PC world build | 통과 | 로컬 `.vrcw` 생성 |
| VRChat Build & Test | 통과 | 로컬 bundle load와 Udon 구성 확인 |
| 브라우저 시각 확인 | 통과 | settings와 text-flow fixture 확인 |

이번 마감에서 직접 다시 실행한 검사는 Node 전체 검사와 npm package dry-run이다. Unity 결과는 `prototype/Assets/Html2Vrc/Documentation/TEST_RESULTS.md`에 기록된 마지막 전체 실행을 기준으로 했다.

## 하려다가 완료하지 못했거나 확인하지 못한 항목

| 항목 | 상태 | 이유 또는 현재 판단 |
| --- | --- | --- |
| Headless ClientSim 전체 성공 | 실패 | Batch Mode에 입력 장치가 없어 ClientSim 3.10.1 내부 player controller가 `NullReferenceException`을 반복했다. HTML2VRC assertion 실패는 아니지만 성공으로 처리하지 않았다. |
| 일반 Unity Editor ClientSim 수동 조작 | 미확인 | 사람이 Editor Play Mode에서 직접 조작하는 검증이 남았다. |
| VR 헤드셋 가독성·조작성 | 미확인 | 실제 HMD 검증 환경에서 실행하지 않았다. |
| Android/Quest build와 성능 | 미확인 | PC world build만 검증했다. |
| 네트워크 동기화·다중 사용자 권한 | 범위 제외 | 이 프로토타입은 로컬 UI 생성과 안전 동작 경계 검증에 집중했다. |
| 외부 Sprite sample | 미확인 | Texture2D와 프로젝트 asset 경로는 검증했지만 별도 Sprite sample 실행은 남았다. |
| 완전한 HTML/CSS/JavaScript 호환 | 미지원 | 제한형 정적 subset만 의도적으로 지원한다. 범용 브라우저 엔진을 Unity에 복제하지 않았다. |
| React function component·Hook·state 보존 HMR | 미지원 | 현재는 primitive tree의 정적 UDOM export와 source 재실행 방식 live reload다. |
| VRChat client 내부 `Start()` 예외 1건의 원인 | 미확인 | HTML2VRC Udon program에는 해당 이벤트가 없어 직접 원인으로 확인되지 않았지만 불안정 기록으로 유지했다. |
| npm 공개 배포·GitHub branch push/merge | 미실행 | 로컬 구현과 검증까지만 마쳤다. 원격 변경은 별도 승인과 release 판단이 필요한 단계다. |
| GitHub organization 삭제 | 미실행 | 현재 원격은 이미 개인 `nupamo/skill` 저장소를 가리킨다. organization 삭제는 복구가 어려운 별도 관리 작업이라 이 구현 작업에서 실행하지 않았다. |

## 보존한 사용자 작업

다음 Unity 변경은 이번 구현에 포함하거나 수정하지 않았고, 커밋에서도 제외했다.

- `prototype/ProjectSettings/ProjectSettings.asset`
- `prototype/Assets/Html2Vrc/Tests/TestCase.unity`와 `.meta`
- `prototype/Assets/Html2Vrc/Tests/TestCaseAssets/`와 `.meta`

따라서 마감 뒤 작업 트리에 위 파일들이 남아 있는 것은 의도된 상태다.

## 재개한다면 권장 순서

1. 사용자 Unity 변경을 별도 commit 또는 폐기 여부로 정리한다.
2. 일반 Unity Editor에서 ClientSim 수동 조작과 VR 헤드셋 가독성을 확인한다.
3. Quest build, frame time, texture/material memory를 측정한다.
4. 외부 Sprite와 더 복잡한 실제 UI 한 건을 end-to-end sample로 추가한다.
5. 필요할 때만 React component/HMR 범위를 설계한다. 현재 정적 계약을 깨지 않도록 UDOM export 경계는 유지한다.
6. 검토 후 `codex/unity-canonical-udom`을 push하고 PR 또는 merge를 진행한다.

## 최종 판단

프로젝트는 “아이디어와 문서만 있는 상태”를 벗어나, 자동 테스트와 실제 Unity/VRChat 실행 증거가 있는 기능성 프로토타입 상태다. PC 기반 제한형 제작 흐름은 다음 개발자가 이어받을 수 있을 정도로 코드, sample, 문서, 회귀 테스트가 갖춰졌다.

반면 범용 웹 호환, Quest, HMD, network 기능까지 포함한 production-ready 제품으로 보기는 이르다. 이 경계를 유지한 채 이번 작업을 마감한다.
