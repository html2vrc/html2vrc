# Unity 렌더러 UDOM 0.1 규격

이 문서는 Unity 프로토타입의 내부 렌더러 모델과 기존 JSON 프로필을 정의한다. UDOM은 HTML이나 CSS가 아니며, Unity 네이티브 UI를 만들기 위한 작고 제한된 중간 표현이다.

## Canonical UDOM 0.1 호환 입력

Unity Importer는 이 문서의 기존 형식과 함께 `packages/udom` JSON Schema가 정의한 canonical UDOM 0.1을 자동 감지한다. `@html2vrc/react`가 만든 `asset`, `viewport`, `root`, `resources` 구조는 Unity 내부 모델로 정규화된 뒤 기존 생성기를 그대로 사용한다.

| Canonical UDOM | Unity 내부 노드 |
| --- | --- |
| `element/view` | `Panel` |
| `text` | `Text` / TextMeshProUGUI |
| `element/image` | `Image` |
| `element/button` | `Button` |
| `element/toggle` | `Toggle` |
| `element/slider` | `Slider` |
| `element/text-input` | `TextInput` / `TMP_InputField` |
| `element/scroll` | `ScrollView` |
| `element/embed` | `Embed` |
| `layout.mode: flex` | `Vertical` 또는 `Horizontal` Layout Group |
| `viewport.width`, `viewport.height`, `pixelRatio`, `fit` | 디자인 크기와 Unity Canvas 배치 |

Canonical 길이의 숫자와 부모 크기 기준 백분율, 기본 flex 방향과 간격, cross-axis 정렬, padding/margin, 단일 색상과 선형 gradient 배경, 기본 텍스트 스타일과 font weight를 변환한다. `styleRefs`는 배열 순서대로 깊은 병합한 뒤 노드의 inline `style`로 마지막 덮어쓴다. canonical `image` resource는 Texture2D/RawImage, `sprite` resource는 Sprite/Image로 생성한다.

Canonical image의 `fit`은 `fill`, `contain`, `cover`, `none`을 모두 지원하며 생략 시 `contain`이다. resource에 선언된 width/height를 원본 크기로 사용하고, 없으면 로드한 Texture2D 또는 Sprite 크기를 사용한다. 이미지 콘텐츠는 `<node-id>::__image-content` 안정 ID의 내부 자식에 배치한다. 사각형은 부모 RectMask2D, radius가 있으면 부모의 SDF Image와 stencil Mask로 자른다. `position.x/y`의 백분율은 `(박스 크기 - 콘텐츠 크기) × 백분율`, 숫자는 왼쪽·위 기준 design-unit 오프셋, `auto`는 남는 공간의 가운데로 해석한다.

Canonical viewport 크기는 디자인 좌표계를 유지한다. UDOM Importer의 선택적 Renderer 목표 Canvas 크기가 다르면 `contain`은 작은 축 비율, `cover`는 큰 축 비율, `stretch`는 축별 비율, `none`은 1:1 scale을 적용한다. `cover`와 넘칠 수 있는 `none`은 생성기 소유의 안정적인 viewport wrapper에서 클리핑한다. 목표 크기 override를 끄면 Canvas는 디자인 viewport 크기로 돌아간다.

Canonical `paint.visible`과 0~1 `paint.opacity`는 CanvasGroup으로 노드와 자식 결과 전체에 적용한다. `visible: false`는 GameObject를 비활성화하지 않아 Flex/Layout 공간을 유지하지만 interactable과 raycast는 차단한다. `opacity: 0`은 시각적으로만 투명하므로 canonical 의미대로 입력 상태는 유지한다. 사용자가 이미 추가한 CanvasGroup은 원래 alpha와 입력 설정을 캡처해 canonical opacity를 곱하고, 해당 스타일이 사라지면 원래 설정으로 복원한다.

Canonical `paint.border`는 왼쪽·위·오른쪽·아래 edge별 `width`, `color`, `solid` style을 보존한다. Unity 생성기는 `<node-id>::__border` overlay 아래에 각 edge를 Image로 만들며 LayoutGroup에서 제외한다. 위·아래 edge가 전체 폭을 차지하고 왼쪽·오른쪽 edge는 그 사이를 채우므로 네 색과 폭이 겹치지 않는다. 폭이 0이거나 완전히 투명한 edge는 만들지 않고, 모든 edge가 사라지면 overlay도 제거한다.

Canonical `linear-gradient`는 임의 각도와 두 개 이상의 color stop을 보존한다. Unity 생성기는 1025×1 RGBA LUT Texture와 UI stencil·clip을 지원하는 Material을 만들고, 노드 크기를 반영한 축으로 LUT를 샘플링한다. canonical 각도 `0`은 아래에서 위, `90`은 왼쪽에서 오른쪽이다. source TextAsset이 있으면 생성 에셋은 `Assets/Html2VrcGenerated/Gradients` 아래에서 source GUID와 node ID 기반 안정 경로로 갱신되고, raw document 생성은 저장되지 않는 임시 에셋을 사용한다. 결과 GameObject에는 VRChat 검사를 통과하지 못할 사용자 런타임 컴포넌트를 추가하지 않는다.

Canonical `paint.radius`는 `[topLeft, topRight, bottomRight, bottomLeft]` design-unit 배열로 보존한다. percentage는 박스의 짧은 변을 기준으로 해석하고, 같은 변에 닿는 두 radius의 합이 변보다 크면 네 값을 같은 비율로 줄인다. 단색 배경은 rounded-corner SDF Material, linear-gradient는 같은 SDF 계산을 합성한 gradient Material을 사용한다. 자식이 있는 노드는 Unity `Mask`로 같은 곡선을 stencil에 기록하며, 투명한 image 부모는 Graphic을 표시하지 않은 채 자식만 자른다. source TextAsset 기반 Material은 `Assets/Html2VrcGenerated/RoundedCorners` 아래 source GUID와 node ID 기반 안정 경로로 갱신된다.

Resource URI는 canonical 명세대로 UDOM TextAsset이 있는 폴더를 기준으로 해석한다. `Assets/`로 시작하는 절대 Unity 에셋 경로도 지원한다. `..`로 정규화하더라도 결과가 `Assets/` 밖으로 나가면 참조하지 않고 경고를 남긴다.

아직 지원하지 않는 canonical 기능은 묵시하지 않고 검증 오류나 명시적 폴백 경고로 반환한다. 현재 변환과 그림자는 오류로 거부한다. radial/conic gradient는 첫 color stop, font resource는 프로젝트 기본 TMP font로 폴백하고 각각 경고를 남긴다. Text 노드 자체의 linear-gradient와 radius는 TMP 텍스트와 별도 박스 graphic이 필요하므로 첫 stop 색 또는 square corner와 경고를 사용한다. Embed radius도 외부 오브젝트가 graphic을 소유하므로 square corner로 진단한다. source asset 경로가 없는 raw JSON 검증에서는 상대 resource URI를 추측하지 않고 빈 Image와 경고를 사용한다. `bind`와 `on`의 symbolic ID는 임의 로직으로 실행하지 않고, 안전한 Unity/Udon binding manifest가 없다는 경고로 남는다. Button, Toggle, Slider, Text input과 Scroll의 canonical focus/blur도 같은 방식으로 보존·진단한다. Slider의 step은 정수 범위의 `1`일 때 Unity `wholeNumbers`로 적용하고 그 외의 step은 아직 경고와 연속 Slider 폴백을 사용한다. Text input은 빈 문자열을 포함한 value와 placeholder, multiline, readOnly, disabled를 native `TMP_InputField`로 적용한다.

## 최상위 구조

```json
{
  "schemaVersion": "0.1",
  "id": "world-settings",
  "name": "World Settings",
  "canvas": {
    "renderMode": "WorldSpace",
    "size": [1200, 800],
    "scale": 0.01,
    "viewportPixelRatio": 1,
    "viewportFit": "none"
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
- `canvas.viewportPixelRatio`: canonical viewport의 원본 pixel ratio. design unit 크기에는 다시 곱하지 않는다.
- `canvas.viewportFit`: `contain`, `cover`, `stretch`, `none`. 기존 내부 JSON 기본값은 `none`이다.
- `root`: 하나의 루트 UI 노드.

## 노드

모든 노드는 아래 필드를 사용할 수 있다.

| 필드 | 의미 |
| --- | --- |
| `id` | 문서 전체에서 유일하고 재생성 후에도 바뀌지 않는 ID |
| `type` | `Panel`, `Text`, `Image`, `Button`, `Toggle`, `Slider`, `TextInput`, `ScrollView`, `Embed` |
| `name` | Unity Hierarchy 표시 이름 |
| `text` | Text 노드의 내용 |
| `sprite` | `Assets/`로 시작하는 Sprite 에셋 경로 |
| `texture` | `Assets/`로 시작하는 Texture2D 에셋 경로 |
| `imageFit` | Image의 `fill`, `contain`, `cover`, `none` 배치 방식 |
| `imagePositionX`, `imagePositionY` | Image 콘텐츠의 숫자·백분율·`auto` 위치 |
| `imageIntrinsicSize` | Image resource의 원본 `[width, height]` |
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
    "backgroundType": "color",
    "cornerRadius": [24, 24, 24, 24],
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
- `borderWidth`: 내부 정규화 형식의 `[left, top, right, bottom]` edge 폭.
- `borderColor`: 내부 정규화 형식의 `[left, top, right, bottom]` edge 색.
- `backgroundType`: `color` 또는 `linear-gradient`.
- `backgroundGradientAngle`: canonical degree 각도. `0`은 아래→위, `90`은 왼쪽→오른쪽.
- `backgroundGradientPositions`, `backgroundGradientColors`: 위치가 0~1로 정렬된 두 개 이상의 대응 color stop 배열.
- `cornerRadius`: 내부 정규화 형식의 `[topLeft, topRight, bottomRight, bottomLeft]` design-unit 반지름.
- `cornerRadiusPercent`: 같은 순서의 percentage 배열. `-1`은 대응 `cornerRadius`가 절대값임을 뜻하고, 0 이상은 최종 Rect의 짧은 변을 기준으로 해석한다.
- `stretchChildrenWidth`, `stretchChildrenHeight`: canonical flex의 기본 `alignItems: stretch`를 Unity Layout Group의 cross-axis 제어로 보존하는 내부 플래그.
- 색상: Unity HTML 색상 형식 `#RRGGBB` 또는 `#RRGGBBAA`.
- `alignment`: `TopLeft`, `Top`, `TopRight`, `Left`, `Center`, `Right`, `BottomLeft`, `Bottom`, `BottomRight`, `MiddleLeft`, `MiddleRight`.
- `flexibleWidth`, `flexibleHeight`: 레이아웃 안에서 남는 공간을 차지하는 정도.

Panel은 배경 Image와 선택적 Vertical/Horizontal Layout Group을 만든다. Image의 배경색은 루트 Image에, Texture2D/RawImage 또는 Sprite/Image 콘텐츠는 마스크된 내부 자식에 배치한다. ScrollView의 `style.layout`은 스크롤 Content 배치를 결정한다. Canonical scroll의 axis는 ScrollRect의 horizontal/vertical 축으로, 왼쪽 위 기준 design-unit `initialOffset`은 Content의 `(-x, +y)` anchored position으로 변환한다.

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
    "targetSlot": "world-light",
    "fallbackLabel": "World light unavailable"
  }
}
```

Embed는 외부 오브젝트를 소유하거나 자식으로 옮기지 않는다. 생성된 Anchor가 슬롯 참조를 표시하며, 외부 오브젝트는 사용자가 계속 소유한다. 그러므로 UDOM을 재생성해도 외부 오브젝트와 참조가 유지된다. Canonical `fallbackLabel`은 생성기 소유 TextMeshPro 자식으로 보존되며, 슬롯 참조가 없을 때만 활성화된다.

## 재생성 규칙

1. `UdomGeneratedNode.stableId`로 기존 GameObject를 찾는다.
2. 같은 ID가 있으면 GameObject와 사용자 추가 컴포넌트를 유지한 채 생성기가 관리하는 컴포넌트만 갱신한다.
3. 새 ID는 생성한다.
4. UDOM에서 사라진 ID 중 `UdomGeneratedNode`가 붙은 오브젝트만 제거한다.
5. 생성 마커가 없는 사용자 오브젝트와 외부 슬롯 대상은 제거하거나 재배치하지 않는다.
6. Button의 생성기 소유 리스너만 교체하고 사용자가 추가한 다른 Persistent Listener는 유지한다.

## 명시적으로 지원하지 않는 것

UDOM 자체는 HTML/CSS가 아니다. 별도 HTML 입력 0.1 도구가 제한된 정적 HTML과 일부 인라인 CSS를 이 규격으로 번역할 수 있지만, 일반 HTML/CSS 호환 파서는 아니다.

JavaScript, 조건식, 반복문, 상태 관리, 애니메이션, 임의 컴포넌트 생성, 네트워크 동기화는 오류 또는 범위 밖 기능이다. 알 수 없는 JSON 속성도 Validation 오류로 처리한다.
