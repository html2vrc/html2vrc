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
| `layout.mode: flex` | 단일 line은 `Vertical`/`Horizontal` Layout Group, wrap은 계산된 top-left Rect |
| `viewport.width`, `viewport.height`, `pixelRatio`, `fit` | 디자인 크기와 Unity Canvas 배치 |

Canonical 길이의 숫자와 부모 크기 기준 백분율, 기본 flex 방향과 간격, cross-axis 정렬, padding/margin, 단일 색상과 선형 gradient 배경, 기본 텍스트 스타일과 font weight를 변환한다. `styleRefs`는 배열 순서대로 깊은 병합한 뒤 노드의 inline `style`로 마지막 덮어쓴다. canonical `image` resource는 Texture2D/RawImage, `sprite` resource는 Sprite/Image로 생성한다.

Canonical flex의 `row-reverse`, `column-reverse`와 정수 `flexItem.order`는 source children 배열을 바꾸지 않고 내부 `reverseChildren`, `flexOrder`로 보존한다. Unity 생성 시에만 `(order, 원본 자식 인덱스)`로 안정 정렬한 뒤 reverse main axis를 적용하므로 같은 order는 트리 순서를 유지한다. 실제 형제는 직접 노드 또는 margin·transform·shadow layout wrapper 단위로 재배치되며 안정 ID와 GameObject를 재생성 사이에 유지한다.

Canonical flex의 `justify` 여섯 값은 내부 `justifyContent`와 main-axis를 포함한 `childAlignment`로 보존한다. `start`, `center`, `end`는 고정 gap을 유지한 채 물리 anchor를 선택한다. `space-between`은 남은 공간을 자식 사이에, `space-around`는 자식 수로, `space-evenly`는 자식 수+1로 나누어 기존 gap에 더한다. Around/evenly는 center anchor가 양끝의 절반 gap 또는 한 gap을 만들도록 하므로 별도 spacer GameObject가 필요 없다. Reverse 방향에서는 main-start와 main-end의 물리 anchor를 뒤집고 기존 안정 형제 재배치 규칙을 그대로 사용한다. Grow가 남은 공간을 모두 소비하거나 overflow가 있으면 분배 gap을 추가하지 않는다.

Canonical `flexItem.grow`, 기본값 1의 `shrink`, 숫자·percentage·`auto` `basis`는 내부 `flexibleWidth/Height`, `flexShrink`, `flexBasis`, `flexBasisIsPercent`로 보존한다. 부모 content box에서 percentage basis를 해석하고 basis가 auto면 해당 주축 size를 사용한다. 양의 여유 공간은 grow 비율로 최대값까지, 부족한 공간은 `shrink × 초기 content size` 비율로 최소값까지 분배한다. Min/max에 닿은 항목은 고정하고 남은 공간을 활성 항목에 반복 재분배하며 margin과 gap은 축소하지 않는다. 결과가 0인 size는 canonical non-negative Length 의미에 따라 유효하다. ScrollView의 활성 축은 콘텐츠가 viewport보다 커야 하므로 main·cross axis shrink/stretch 선계산에서 overflow를 보존한다.

Canonical `flexItem.alignSelf`의 `auto`, `start`, `center`, `end`, `stretch`는 내부 `alignSelf`와 `alignSelfMargin`으로 보존한다. `auto`는 부모 `alignItems`를 따르고 나머지는 항목별 교차축 정렬을 override한다. Unity Layout Group에는 항목별 정렬 속성이 없으므로, 원래 canonical margin은 그대로 두고 남는 교차축 공간만 합성 margin으로 계산해 기존 `<node-id>::__margin` 안정 wrapper에 더한다. `stretch`는 원래 margin을 제외한 content box를 min/max로 제한하며, start·center·end는 원래 크기와 비대칭 margin을 보존한다. 항목이 교차축보다 커지면 음수 합성 margin으로 overflow 방향을 유지하고, row/column 및 reverse 주축의 형제 순서에는 영향을 주지 않는다.

Canonical flex의 `wrap`과 `wrap-reverse`는 order가 적용된 항목을 부모 content box의 주축 크기와 주축 gap으로 안정적으로 line에 나눈다. 각 line 안에서 grow/shrink와 min/max를 먼저 재분배하고 justify와 항목별 alignSelf를 적용한 뒤, 모든 line의 교차축 크기에 `alignContent`의 start·center·end·stretch·space-between·space-around를 적용한다. `rowGap`과 `columnGap`은 물리 축을 따르므로 flex 방향이 바뀌어도 의미가 유지된다. Wrap-reverse는 line의 교차축 진행만 뒤집고 source/order 순서는 보존한다. Unity Layout Group은 여러 line과 항목별 교차축 좌표를 직접 표현할 수 없으므로 wrapped 부모는 계산된 최종 Rect를 top-left 좌표로 굽고 자식의 `LayoutElement.ignoreLayout`을 켠다. 같은 canonical 입력의 반복 생성은 GameObject와 wrapper를 재사용한다.

Canonical `position: absolute`는 내부 `positionAbsolute`로 보존한다. 해당 항목은 부모 flex의 main-axis 크기 계산, grow 재분배와 gap 개수에서 제외하고 Unity `LayoutElement.ignoreLayout`을 켠다. 숫자와 percentage `x/y`는 부모 design box를 기준으로 미리 해석한 뒤 RectTransform의 top-left anchor와 pivot에서 `(x, -y)`로 적용한다. Margin이 있으면 margin wrapper가 좌표와 외곽 크기를 소유하고, transform·shadow wrapper는 그 안쪽 box를 채운다. Absolute/flow 전환 시에도 같은 wrapper와 노드를 재사용한다.

Canonical 정수 `zIndex`는 같은 부모가 만든 stacking context에서 낮은 값부터 높은 값 순으로 그리며 동률은 기존 시각 순서를 유지한다. 일반·absolute 부모는 source 순서, flex 부모는 order와 reverse가 적용된 순서가 동률 기준이다. 서로 다른 zIndex를 가진 flex 형제는 Unity Layout Group의 hierarchy 순서가 레이아웃까지 바꾸지 않도록 먼저 기존 flex 계산기로 최종 top-left Rect를 굽는다. 그다음 직접 노드 또는 margin·transform·shadow의 가장 바깥 안정 wrapper를 `(zIndex, 기존 시각 순서)`로 재배치한다. 따라서 absolute 항목은 flex flow에서 계속 제외되고, zIndex를 모두 같게 되돌리면 native Layout Group으로 복원하면서 GameObject를 재사용한다.

Canonical `layout.mode`의 생략 기본값은 `absolute`다. Explicit `mode: none`은 내부 `displayNone`으로 보존하고 해당 노드와 subtree를 flex 크기·gap 계산, GameObject 생성, paint 갱신과 신규 external slot 수집에서 제외한다. 이전 생성 결과가 있으면 안정 ID prune 단계에서 margin·transform·shadow wrapper와 모든 자식을 제거한다. 이미 사용자가 연결한 external slot entry와 target은 프로젝트 계약에 따라 보존하며 target을 삭제하거나 reparent하지 않는다. Root가 none이어도 생성기 root와 viewport wrapper는 유지되어 다음 재생성이 안전하다.

Canonical layout의 `overflowX`와 `overflowY`가 모두 `hidden`이면 내부 `clipContent`로 보존하고 사각형 노드에 VRChat-safe `RectMask2D`를 추가한다. Radius가 있는 노드는 기존 stencil `Mask`가 같은 양축 clipping을 담당한다. Hidden에서 양축 `visible`로 전환하면 노드와 자식을 재사용하면서 생성기 소유 clip component만 제거한다. 한 축만 hidden인 조합과 일반 노드의 `scroll`은 `RectMask2D`가 축별 clip을 지원하지 않아 손실 없이 표현할 수 없으므로 검증 오류로 반환한다.

Canonical image의 `fit`은 `fill`, `contain`, `cover`, `none`을 모두 지원하며 생략 시 `contain`이다. resource에 선언된 width/height를 원본 크기로 사용하고, 없으면 로드한 Texture2D 또는 Sprite 크기를 사용한다. 이미지 콘텐츠는 `<node-id>::__image-content` 안정 ID의 내부 자식에 배치한다. 사각형은 부모 RectMask2D, radius가 있으면 부모의 SDF Image와 stencil Mask로 자른다. `position.x/y`의 백분율은 `(박스 크기 - 콘텐츠 크기) × 백분율`, 숫자는 왼쪽·위 기준 design-unit 오프셋, `auto`는 남는 공간의 가운데로 해석한다.

Canonical viewport 크기는 디자인 좌표계를 유지한다. UDOM Importer의 선택적 Renderer 목표 Canvas 크기가 다르면 `contain`은 작은 축 비율, `cover`는 큰 축 비율, `stretch`는 축별 비율, `none`은 1:1 scale을 적용한다. `cover`와 넘칠 수 있는 `none`은 생성기 소유의 안정적인 viewport wrapper에서 클리핑한다. 목표 크기 override를 끄면 Canvas는 디자인 viewport 크기로 돌아간다.

Canonical `paint.visible`과 0~1 `paint.opacity`는 CanvasGroup으로 노드와 자식 결과 전체에 적용한다. `visible: false`는 GameObject를 비활성화하지 않아 Flex/Layout 공간을 유지하지만 interactable과 raycast는 차단한다. `opacity: 0`은 시각적으로만 투명하므로 canonical 의미대로 입력 상태는 유지한다. 사용자가 이미 추가한 CanvasGroup은 원래 alpha와 입력 설정을 캡처해 canonical opacity를 곱하고, 해당 스타일이 사라지면 원래 설정으로 복원한다.

Canonical `paint.border`는 왼쪽·위·오른쪽·아래 edge별 `width`, `color`, `solid` style을 보존한다. Unity 생성기는 `<node-id>::__border` overlay 아래에 각 edge를 Image로 만들며 LayoutGroup에서 제외한다. 위·아래 edge가 전체 폭을 차지하고 왼쪽·오른쪽 edge는 그 사이를 채우므로 네 색과 폭이 겹치지 않는다. 폭이 0이거나 완전히 투명한 edge는 만들지 않고, 모든 edge가 사라지면 overlay도 제거한다.

Canonical `linear-gradient`는 임의 각도와 두 개 이상의 color stop을 보존한다. Unity 생성기는 1025×1 RGBA LUT Texture와 UI stencil·clip을 지원하는 Material을 만들고, 노드 크기를 반영한 축으로 LUT를 샘플링한다. canonical 각도 `0`은 아래에서 위, `90`은 왼쪽에서 오른쪽이다. source TextAsset이 있으면 생성 에셋은 `Assets/Html2VrcGenerated/Gradients` 아래에서 source GUID와 node ID 기반 안정 경로로 갱신되고, raw document 생성은 저장되지 않는 임시 에셋을 사용한다. 결과 GameObject에는 VRChat 검사를 통과하지 못할 사용자 런타임 컴포넌트를 추가하지 않는다.

Canonical `radial-gradient`는 같은 LUT와 별도 VRChat-safe UI Shader로 렌더링한다. `center.x/y`와 타원형 `radius.x/y`는 design-unit과 percentage 여부를 내부 모델에 함께 보존하고, percentage는 Layout이 끝난 최종 Rect의 width/height 축을 기준으로 해석한다. 생략되거나 `auto`인 축은 canonical 기본값 `50%`를 사용한다. Material과 LUT는 linear gradient와 같은 안정 경로를 사용하며 두 gradient 종류 사이를 전환해도 에셋 GUID를 유지한다.

Canonical `conic-gradient`는 같은 LUT와 전용 UI Shader로 렌더링한다. 생략된 시작 `angle`은 `0`이며 중심에서 위쪽으로 향하는 선을 기준으로 stop을 시계 방향으로 순회한다. `center.x/y`의 design-unit·percentage 보존과 최종 Layout Rect 계산, 안정 Material/LUT 경로 및 radius SDF 합성은 radial gradient와 동일하다.

Canonical `paint.radius`는 `[topLeft, topRight, bottomRight, bottomLeft]` design-unit 배열로 보존한다. percentage는 박스의 짧은 변을 기준으로 해석하고, 같은 변에 닿는 두 radius의 합이 변보다 크면 네 값을 같은 비율로 줄인다. 단색 배경은 rounded-corner SDF Material, linear/radial/conic gradient는 같은 SDF 계산을 합성한 gradient Material을 사용한다. 자식이 있는 노드는 Unity `Mask`로 같은 곡선을 stencil에 기록하며, 투명한 image 부모는 Graphic을 표시하지 않은 채 자식만 자른다. source TextAsset 기반 Material은 `Assets/Html2VrcGenerated/RoundedCorners` 아래 source GUID와 node ID 기반 안정 경로로 갱신된다.

Canonical `paint.border`는 `[left, top, right, bottom]` 폭·색 배열을 단일 `<node-id>::__border` Image와 VRChat-safe SDF Material로 렌더링한다. Outer contour는 정규화된 circular corner radius를 사용하고, inner contour는 각 edge 폭만큼 inset한 box와 corner별 X/Y radius로 계산하므로 비대칭 폭에서도 타원형 안쪽 곡선을 유지한다. 서로 다른 edge 색의 join은 outer corner와 inner corner를 잇는 폭 비율 경계로 선택한다. overlay는 `ignoreLayout`, raycast off이며 source TextAsset 기반 Material은 `Assets/Html2VrcGenerated/Borders` 아래 안정 경로로 재사용한다.

Canonical `transform`은 `origin`과 배열 순서의 `translate`, `rotate`, `scale`을 내부 `transformOrigin`, `transformOriginIsPercent`, `transformOperationTypes`, `transformOperationValues`, `transformOperationValuesArePercent` 배열로 정규화한다. 각 operation은 안정 ID의 RectTransform wrapper 하나로 생성해 비균일 scale과 rotate가 섞인 순서도 정확히 보존한다. percentage origin과 translate는 flex와 margin이 끝난 노드 자신의 최종 Layout Rect를 기준으로 갱신하고, transform wrapper 밖의 layout wrapper가 형제 배치 공간을 유지한다. 양의 회전은 화면 기준 시계 방향이다.

Canonical `paint.shadows`는 `shadowOffsets`, `shadowBlurs`, `shadowSpreads`, `shadowColors`, `shadowInsets` 배열로 outer·inset 순서와 값을 보존한다. 렌더러는 각 visible shadow를 `<node-id>::__shadow-<index>` 안정 ID의 별도 Image와 VRChat-safe SDF Material로 만들고 radius-aware blur와 spread를 그린다. Outer shadow는 shadow layout wrapper 안에서 노드 뒤에 겹치므로 시각적 확장이 형제 배치나 raycast 영역을 바꾸지 않으며, transform이 있으면 노드와 같은 operation wrapper를 상속한다. Inset shadow는 보통 노드의 `ignoreLayout` 첫 자식으로 background 위·일반 콘텐츠와 border 아래에 놓인다. TMP Graphic이 노드 자체에 있는 Text는 같은 layer 순서를 지키도록 shadow layout wrapper 안에서 inset Image를 Text 노드 바로 앞의 sibling으로 둔다. 두 경로 모두 원래 box radius SDF로 잘리고, 모든 shadow Image는 raycast를 끄며 Material과 GameObject를 재생성 사이에 안정적으로 재사용한다.

Canonical text는 `lineHeight`, `letterSpacing`, `align: justify`, `verticalAlign`, `wrap`, `overflow`, `preserveWhitespace`를 내부 text metric과 flow 필드로 정규화한다. 숫자 line height는 TMP FontAsset의 face line height와 point size에서 필요한 추가 spacing을 계산해 baseline 간격을 design unit에 맞춘다. Letter spacing은 design unit을 TMP의 font-size-relative em 값으로 환산한다. `clip`은 TMP masking, `visible`은 overflow, `ellipsis`는 ellipsis 모드에 대응하며, whitespace 보존이 꺼져 있으면 연속 공백과 줄바꿈을 한 칸으로 축약한다. 생략된 canonical 기본값은 16px, 검정, top/start, wrap, clip이다.

Canonical font resource URI가 기존 `.asset` TMP Font Asset을 가리키면 이를 Text와 TextInput의 text·placeholder에 직접 연결한다. `.ttf` 또는 `.otf` Unity Font를 가리키면 source GUID와 이름으로 `Assets/Html2VrcGenerated/Fonts` 아래의 dynamic TMP Font Asset을 한 번 생성해 문서와 노드 사이에서 공유한다. 동일 source는 재생성해도 같은 asset GUID를 유지한다. 누락되었거나 지원하지 않는 font 형식은 명시적 경고와 함께 TMP Settings의 기본 font로 폴백하며, 생성된 custom font가 기본 후보로 선택되지는 않는다.

Resource URI는 canonical 명세대로 UDOM TextAsset이 있는 폴더를 기준으로 해석한다. `Assets/`로 시작하는 절대 Unity 에셋 경로도 지원한다. `..`로 정규화하더라도 결과가 `Assets/` 밖으로 나가면 참조하지 않고 경고를 남긴다.

아직 지원하지 않는 canonical 기능은 묵시하지 않고 검증 오류나 명시적 폴백 경고로 반환한다. Text 노드 자체의 linear/radial/conic gradient와 radius는 TMP 텍스트와 별도 박스 graphic이 필요하므로 첫 stop 색 또는 square corner와 경고를 사용한다. Embed의 gradient와 radius도 외부 오브젝트가 graphic을 소유하므로 첫 stop 또는 square corner로 진단한다. source asset 경로가 없는 raw JSON 검증에서는 상대 resource URI를 추측하지 않고 빈 Image와 기본 TMP font 경고를 사용한다. `bind`와 `on`의 symbolic ID는 임의 로직으로 실행하지 않고, 안전한 Unity/Udon binding manifest가 없다는 경고로 남는다. Button, Toggle, Slider, Text input과 Scroll의 canonical focus/blur도 같은 방식으로 보존·진단한다. Slider의 step은 정수 범위의 `1`일 때 Unity `wholeNumbers`로 적용하고 그 외의 step은 아직 경고와 연속 Slider 폴백을 사용한다. Text input은 빈 문자열을 포함한 value와 placeholder, multiline, readOnly, disabled를 native `TMP_InputField`로 적용한다.

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
- `backgroundGradientAngle`: linear gradient에서는 `0`이 아래→위, `90`이 왼쪽→오른쪽인 진행 각도이고, conic gradient에서는 위쪽 기준 시계 방향 시작 각도.
- `backgroundGradientPositions`, `backgroundGradientColors`: 위치가 0~1로 정렬된 두 개 이상의 대응 color stop 배열.
- `backgroundGradientCenter`: radial/conic gradient의 X/Y 중심값. `backgroundGradientRadius`는 radial gradient의 X/Y 반지름.
- `backgroundGradientCenterIsPercent`, `backgroundGradientRadiusIsPercent`: 대응 X/Y 값이 최종 Rect 축 기준 percentage인지 보존하는 내부 boolean 배열.
- `cornerRadius`: 내부 정규화 형식의 `[topLeft, topRight, bottomRight, bottomLeft]` design-unit 반지름.
- `cornerRadiusPercent`: 같은 순서의 percentage 배열. `-1`은 대응 `cornerRadius`가 절대값임을 뜻하고, 0 이상은 최종 Rect의 짧은 변을 기준으로 해석한다.
- `transformOrigin`, `transformOriginIsPercent`: canonical transform origin의 X/Y 값과 percentage 여부.
- `transformOperationTypes`, `transformOperationValues`, `transformOperationValuesArePercent`: 배열 순서를 유지한 translate/rotate/scale과 operation당 X/Y 슬롯.
- `shadowOffsets`, `shadowBlurs`, `shadowSpreads`, `shadowColors`, `shadowInsets`: canonical shadow 배열 순서를 보존하는 offset X/Y와 layer별 paint 값.
- `lineHeight`, `letterSpacing`: canonical text의 design-unit baseline 간격과 글자 사이 간격. `lineHeight: -1`은 font의 normal metric을 뜻한다.
- `textWrap`, `textOverflow`, `preserveWhitespace`: canonical wrap/nowrap, visible/clip/ellipsis와 whitespace 보존 여부.
- `fontAssetPath`: canonical font resource에서 해석한 Unity `Assets/` 경로. 기존 TMP Font Asset 또는 TMP asset을 생성할 TTF/OTF source를 가리킨다.
- `positionAbsolute`: canonical 노드가 부모 flex 흐름에서 빠지고 top-left `x/y` 좌표를 사용하는지 나타내는 내부 플래그.
- `zIndex`: 같은 부모 안의 canonical 정수 paint 순서. 낮은 값이 먼저, 높은 값이 나중에 그려진다.
- `displayNone`: canonical `layout.mode: none`으로 인해 노드와 subtree가 렌더링·레이아웃에서 제외되는지 나타내는 내부 플래그.
- `stretchChildrenWidth`, `stretchChildrenHeight`: canonical flex의 기본 `alignItems: stretch`를 Unity Layout Group의 cross-axis 제어로 보존하는 내부 플래그.
- `justifyContent`: canonical flex의 `start`, `center`, `end`, `space-between`, `space-around`, `space-evenly`를 보존하는 내부 enum 문자열.
- `alignContent`, `flexWrap`: canonical flex line 묶음의 교차축 정렬과 `nowrap`/`wrap`/`wrap-reverse`를 보존하는 내부 enum 문자열.
- `rowGap`, `columnGap`: canonical flex의 물리 행·열 gap을 design unit으로 보존한다. 기존 `spacing`은 현재 주축 gap을 유지한다.
- `useResolvedChildPositions`, `useResolvedPosition`: wrapped 부모가 Unity Layout Group 대신 계산된 top-left 자식 Rect를 사용하고, 각 항목이 그 좌표를 소유하는지 나타내는 내부 플래그.
- `alignSelf`: canonical flex item의 `auto`, `start`, `center`, `end`, `stretch`를 보존하는 내부 enum 문자열. `alignSelfMargin`은 Unity 항목별 교차축 정렬에 사용하는 `[left, top, right, bottom]` 합성 margin이며 원래 `margin`과 분리된다.
- `reverseChildren`, `flexOrder`: canonical reverse main axis와 order-modified flex 순서를 보존하는 내부 플래그와 정수.
- `flexShrink`: canonical flex shrink 비율. `-1`은 shrink 선계산을 사용하지 않는 기존 내부 문서 sentinel이고 canonical 기본값은 `1`이다.
- `flexBasis`, `flexBasisIsPercent`: canonical 주축 basis 값과 percentage 여부. `-1`은 `auto`를 뜻한다.
- 색상: Unity HTML 색상 형식 `#RRGGBB` 또는 `#RRGGBBAA`.
- `alignment`: `TopLeft`, `Top`, `TopRight`, `TopJustified`, `Left`, `Center`, `Right`, `Justified`, `BottomLeft`, `Bottom`, `BottomRight`, `BottomJustified`, `MiddleLeft`, `MiddleRight`.
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

HTML 입력의 flex-direction, justify-content, align-items, align-self, order, grow/shrink/basis는 canonical importer와 같은 정규화·크기 해석 경로를 공유한다. `display: none`은 `displayNone`, `position: absolute`와 left/top은 `positionAbsolute` 및 top-left 좌표로 변환하므로 HTML 재생성도 canonical wrapper와 안정 ID 규칙을 그대로 따른다.

JavaScript, 조건식, 반복문, 상태 관리, 애니메이션, 임의 컴포넌트 생성, 네트워크 동기화는 오류 또는 범위 밖 기능이다. 알 수 없는 JSON 속성도 Validation 오류로 처리한다.
