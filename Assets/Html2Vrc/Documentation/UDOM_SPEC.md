# UDOM 0.1 최소 규격

이 문서는 첫 번째 기술 검증 프로토타입이 실제로 읽는 JSON 형식을 정의한다. UDOM은 HTML이나 CSS가 아니며, Unity 네이티브 UI를 만들기 위한 작고 제한된 중간 표현이다.

## 최상위 구조

```json
{
  "schemaVersion": "0.1",
  "id": "world-settings",
  "name": "World Settings",
  "canvas": {
    "renderMode": "WorldSpace",
    "size": [1200, 800],
    "scale": 0.01
  },
  "root": {
    "id": "settings-panel",
    "type": "Panel"
  }
}
```

- `schemaVersion`: 현재는 `0.1`만 지원한다.
- `id`: 문서의 안정적인 ID다.
- `name`: Unity에서 표시할 문서 이름이다.
- `canvas.renderMode`: `WorldSpace` 또는 `ScreenSpaceOverlay`.
- `canvas.size`: Canvas의 픽셀 기준 너비와 높이.
- `canvas.scale`: World Space Canvas의 Unity 월드 스케일.
- `root`: 하나의 루트 UI 노드.

## 노드

모든 노드는 아래 필드를 사용할 수 있다.

| 필드 | 의미 |
| --- | --- |
| `id` | 문서 전체에서 유일하고 재생성 후에도 바뀌지 않는 ID |
| `type` | `Panel`, `Text`, `Image`, `Button`, `ScrollView`, `Embed` |
| `name` | Unity Hierarchy 표시 이름 |
| `text` | Text 노드의 내용 |
| `sprite` | `Assets/`로 시작하는 Sprite 에셋 경로 |
| `style` | 위치, 크기, 색상, 레이아웃 |
| `binding` | Button의 안전한 동작 |
| `embed` | 외부 GameObject 슬롯 |
| `children` | 자식 노드 배열 |

`id`는 영문자나 숫자로 시작하고 영문자, 숫자, `.`, `_`, `-`만 사용한다. 같은 문서에서 중복 ID는 오류다.

## 스타일

```json
{
  "style": {
    "position": [0, 0],
    "size": [1000, 700],
    "layout": "Vertical",
    "padding": [32, 32, 32, 32],
    "margin": [0, 0, 0, 12],
    "spacing": 16,
    "backgroundColor": "#182033F2",
    "textColor": "#FFFFFFFF",
    "fontSize": 36,
    "alignment": "MiddleLeft",
    "flexibleWidth": 1,
    "flexibleHeight": 0
  }
}
```

- `position`: `[x, y]`. 레이아웃 그룹 밖에서 사용한다.
- `size`: `[width, height]`.
- `layout`: `None`, `Vertical`, `Horizontal`.
- `padding`, `margin`: `[left, top, right, bottom]`.
- `spacing`: 레이아웃 자식 사이 간격.
- 색상: Unity HTML 색상 형식 `#RRGGBB` 또는 `#RRGGBBAA`.
- `alignment`: `TopLeft`, `Top`, `TopRight`, `Left`, `Center`, `Right`, `BottomLeft`, `Bottom`, `BottomRight`, `MiddleLeft`, `MiddleRight`.
- `flexibleWidth`, `flexibleHeight`: 레이아웃 안에서 남는 공간을 차지하는 정도.

Panel은 배경 Image와 선택적 Vertical/Horizontal Layout Group을 만든다. Image는 Sprite가 없으면 단색 블록으로 동작한다. ScrollView의 `style.layout`은 스크롤 Content 배치를 결정한다.

## 안전한 Binding

```json
{
  "binding": {
    "action": "ToggleLight",
    "targetSlot": "world-light"
  }
}
```

지원 동작:

- `ToggleActive`: 외부 GameObject 활성 상태 반전
- `SetActive`: 외부 GameObject 활성화
- `SetInactive`: 외부 GameObject 비활성화
- `ToggleLight`: 외부 대상의 `Light.enabled` 반전
- `ClosePanel`: 버튼을 포함하는 가장 가까운 Panel 비활성화

`targetSlot`은 생성된 Canvas 루트의 `UdomGeneratedRoot.externalReferences`에서 Unity 오브젝트와 연결한다. UDOM에는 Unity 인스턴스 ID나 임의 코드가 들어가지 않는다.

## Embed

```json
{
  "id": "light-embed",
  "type": "Embed",
  "embed": {
    "targetSlot": "world-light"
  }
}
```

Embed는 외부 오브젝트를 소유하거나 자식으로 옮기지 않는다. 생성된 Anchor가 슬롯 참조를 표시하며, 외부 오브젝트는 사용자가 계속 소유한다. 그러므로 UDOM을 재생성해도 외부 오브젝트와 참조가 유지된다.

## 재생성 규칙

1. `UdomGeneratedNode.stableId`로 기존 GameObject를 찾는다.
2. 같은 ID가 있으면 GameObject와 사용자 추가 컴포넌트를 유지한 채 생성기가 관리하는 컴포넌트만 갱신한다.
3. 새 ID는 생성한다.
4. UDOM에서 사라진 ID 중 `UdomGeneratedNode`가 붙은 오브젝트만 제거한다.
5. 생성 마커가 없는 사용자 오브젝트와 외부 슬롯 대상은 제거하거나 재배치하지 않는다.
6. Button의 생성기 소유 리스너만 교체하고 사용자가 추가한 다른 Persistent Listener는 유지한다.

## 명시적으로 지원하지 않는 것

HTML/CSS 파싱, JavaScript, 조건식, 반복문, 상태 관리, 애니메이션, 임의 컴포넌트 생성, 네트워크 동기화는 오류 또는 범위 밖 기능이다. 알 수 없는 JSON 속성도 Validation 오류로 처리한다.
