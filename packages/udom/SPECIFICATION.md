# UDOM 0.1 초기 명세

상태: Draft  
대상 버전: `0.1`  
작성일: 2026-07-27

UDOM은 웹 UI의 구조, 레이아웃, 시각 표현과 인터랙션 연결 지점을 전달하기 위한 JSON 기반 중간 형식이다.

이 문서는 UDOM의 의미를 정의한다. 웹사이트에서 UDOM을 추론하는 방법이나 Unity에서 UDOM을 그리는 방법은 정의하지 않는다.

---

## 1. 규범적 용어

이 문서의 `MUST`, `MUST NOT`, `SHOULD`, `SHOULD NOT`, `MAY`는 요구 수준을 나타내는 규범적 용어다.

- `MUST`: 호환 구현이 반드시 따라야 한다.
- `MUST NOT`: 호환 구현이 절대 수행하면 안 된다.
- `SHOULD`: 특별한 이유가 없다면 따라야 한다.
- `SHOULD NOT`: 특별한 이유가 없다면 피해야 한다.
- `MAY`: 구현이 선택할 수 있다.

“명시되지 않았다”는 것은 Renderer가 임의로 의미를 추측해도 된다는 뜻이 아니다. 이 문서에 기본값이 정의되어 있다면 기본값을 사용하고, 그렇지 않다면 진단을 생성해야 한다.

## 2. 목표와 비목표

### 목표

UDOM은 다음 정보를 상호 운용 가능한 형태로 전달한다.

- UI 요소와 텍스트의 순서 있는 계층
- 안정적인 노드 식별자
- 디자인 좌표계와 목표 viewport
- Absolute와 Flex 레이아웃
- 배경, gradient, border, radius, shadow와 opacity
- 텍스트와 이미지
- 버튼, 토글, 슬라이더, 입력과 스크롤
- 외부 GameObject를 위한 Embed
- 상태와 동작을 연결하기 위한 상징적 Binding
- 외부 이미지와 Font 리소스
- 확장, 생성 도구와 애플리케이션별 메타데이터

### 비목표

UDOM 0.1은 다음을 정의하지 않는다.

- HTML, CSS와 JavaScript 전체
- CSS selector, cascade와 브라우저 스타일 계산
- 브라우저 DOM API
- 임의의 실행 코드
- 네트워크 요청과 웹 탐색
- Shader, Material과 Unity 컴포넌트 선택
- Udon 또는 UdonSharp 구현
- CSS Grid와 table layout
- 여러 viewport breakpoint
- 애니메이션과 transition

## 3. 설계 기반

### DOM에서 가져온 원칙

[WHATWG DOM](https://dom.spec.whatwg.org/)과 마찬가지로 UDOM 문서는 유한한 계층 트리다.

- 문서는 하나의 root를 가진다.
- root를 제외한 노드는 정확히 하나의 부모를 가진다.
- 자식은 순서가 있으며 배열 순서는 의미가 있다.
- 순회 순서는 별도 언급이 없다면 preorder depth-first다.
- Element와 Text는 서로 다른 노드 종류다.

UDOM은 브라우저 DOM의 API, Shadow DOM, mutation observer와 event propagation을 복제하지 않는다.

### glTF에서 가져온 원칙

[glTF 2.0](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html)과 유사하게 UDOM은 다음을 사용한다.

- `asset.version`과 `asset.minVersion`
- URI로 참조하는 외부 리소스
- `extensionsUsed`와 `extensionsRequired`
- 객체별 `extensions`
- 구현이 무시할 수 있는 `extras`
- 명세와 JSON Schema의 공동 관리

glTF의 배열 인덱스 참조는 사용하지 않는다. UDOM은 AI 수정, Git diff와 재생성에 유리하도록 안정적인 문자열 ID를 사용한다.

## 4. JSON 표현

UDOM 문서는 UTF-8로 인코딩된 JSON 객체여야 한다.

- 권장 파일 확장자는 `.udom.json`이다.
- 모든 숫자는 유한한 JSON number여야 한다.
- `NaN`, 양의 무한대와 음의 무한대는 허용하지 않는다.
- 객체 속성 순서는 의미가 없다.
- 배열 순서는 의미가 있다.
- 문자열 비교는 별도 언급이 없다면 대소문자를 구분한다.
- 알 수 없는 핵심 속성을 Renderer가 조용히 무시하면 안 된다.

UDOM 0.1은 binary container를 정의하지 않는다.

## 5. 최상위 객체

| 속성 | 형식 | 필수 | 의미 |
|---|---|---:|---|
| `asset` | AssetInfo | 예 | UDOM 버전과 생성 정보 |
| `viewport` | Viewport | 예 | 디자인 좌표계의 기준 영역 |
| `root` | Node | 예 | 문서의 유일한 root |
| `styles` | StyleDefinition[] | 아니오 | 공유 가능한 스타일 |
| `resources` | Resource[] | 아니오 | 이미지와 Font 등의 외부 리소스 |
| `extensionsUsed` | string[] | 아니오 | 문서가 사용하는 모든 확장 |
| `extensionsRequired` | string[] | 아니오 | 해석에 반드시 필요한 확장 |
| `extensions` | object | 아니오 | 최상위 확장 데이터 |
| `extras` | any | 아니오 | 애플리케이션별 데이터 |

최상위 객체에는 `asset`, `viewport`와 `root`가 정확히 하나씩 있어야 한다.

### 최소 문서

```json
{
  "asset": {
    "version": "0.1"
  },
  "viewport": {
    "width": 1280,
    "height": 720
  },
  "root": {
    "type": "element",
    "id": "root",
    "name": "view"
  }
}
```

## 6. AssetInfo

| 속성 | 형식 | 필수 | 의미 |
|---|---|---:|---|
| `version` | string | 예 | 문서가 대상으로 하는 UDOM `major.minor` 버전 |
| `minVersion` | string | 아니오 | 문서를 해석하는 데 필요한 최소 UDOM 버전 |
| `generator` | string | 아니오 | 문서를 생성한 도구와 버전 |
| `copyright` | string | 아니오 | 표시 가능한 저작권 정보 |
| `extensions` | object | 아니오 | AssetInfo 확장 데이터 |
| `extras` | any | 아니오 | 애플리케이션별 데이터 |

`version`과 `minVersion`은 `^[0-9]+\.[0-9]+$` 형식을 따라야 한다.

`minVersion`은 `version`보다 높을 수 없다.

예:

```json
{
  "asset": {
    "version": "0.1",
    "generator": "html2vrc/skill 0.1"
  }
}
```

## 7. Viewport와 좌표계

Viewport는 문서가 설계된 기준 평면을 정의한다.

| 속성 | 형식 | 필수 | 기본값 | 의미 |
|---|---|---:|---:|---|
| `width` | number | 예 | - | 디자인 영역 너비 |
| `height` | number | 예 | - | 디자인 영역 높이 |
| `pixelRatio` | number | 아니오 | `1` | 원본 캡처의 device pixel ratio |
| `fit` | enum | 아니오 | `"contain"` | 목표 Canvas와 비율이 다를 때의 배치 |
| `extensions` | object | 아니오 | - | Viewport 확장 데이터 |
| `extras` | any | 아니오 | - | 애플리케이션별 데이터 |

`width`, `height`와 `pixelRatio`는 0보다 커야 한다.

`fit` 값:

- `contain`: 전체 viewport를 유지하며 안에 맞춘다.
- `cover`: 목표 영역을 채우고 넘치는 부분을 자를 수 있다.
- `stretch`: 가로와 세로를 독립적으로 늘린다.
- `none`: 디자인 단위를 그대로 사용한다.

UDOM 좌표계는 다음과 같다.

- 원점은 viewport의 왼쪽 위다.
- X축 양의 방향은 오른쪽이다.
- Y축 양의 방향은 아래쪽이다.
- 길이의 기본 단위는 device pixel이 아니라 UDOM design unit이다.
- 단위 없는 숫자 하나는 design unit 하나를 뜻한다.
- 양의 회전은 화면을 바라볼 때 시계 방향이다.
- Renderer는 이 좌표계를 Unity 좌표계로 결정론적으로 변환해야 한다.

## 8. 길이와 비율

Length 값은 다음 중 하나다.

- number: UDOM design unit
- percentage string: `"25%"`, `"100%"`
- `"auto"`

음수가 허용되는지는 각 속성이 정의한다. 크기와 padding은 음수가 될 수 없다.

0은 `"0%"`와 다르다. 0은 design unit이고 `"0%"`는 부모 영역에 대한 비율이다.

UDOM 0.1은 `px`, `em`, `rem`, `vw`, `vh`와 `calc()` 문자열을 허용하지 않는다. Synthesizer는 이를 design unit, percentage 또는 명시적인 계산 결과로 정규화해야 한다.

## 9. 노드 트리

Node는 ElementNode 또는 TextNode다.

모든 노드는 다음 규칙을 따라야 한다.

- `id`는 문서 전체에서 유일한 비어 있지 않은 문자열이어야 한다.
- `id`는 재생성 후에도 가능한 한 유지되어야 한다.
- 동일한 노드가 트리의 두 위치에 나타나면 안 된다.
- 자식 배열의 순서는 시각 순서와 기본 순회 순서에 영향을 준다.
- 순환 참조를 만들 수 없다.

권장 ID는 사람이 읽을 수 있는 kebab-case다.

```text
settings-panel
profile-avatar
music-toggle
```

Renderer는 ID를 Unity GameObject와 진단 위치를 추적하는 안정적인 키로 사용할 수 있다.

### ElementNode

| 속성 | 형식 | 필수 | 의미 |
|---|---|---:|---|
| `type` | `"element"` | 예 | 노드 종류 |
| `id` | string | 예 | 문서 전체에서 유일한 ID |
| `name` | ElementName | 예 | 요소의 의미 |
| `properties` | object | 아니오 | 요소별 속성 |
| `styleRefs` | string[] | 아니오 | 공유 스타일 ID 목록 |
| `style` | Style | 아니오 | 이 노드에 직접 적용하는 스타일 |
| `children` | Node[] | 아니오 | 순서 있는 자식 |
| `bind` | object | 아니오 | 값 Binding |
| `on` | object | 아니오 | 이벤트 Binding |
| `extensions` | object | 아니오 | 노드 확장 데이터 |
| `extras` | any | 아니오 | 애플리케이션별 데이터 |

### TextNode

| 속성 | 형식 | 필수 | 의미 |
|---|---|---:|---|
| `type` | `"text"` | 예 | 노드 종류 |
| `id` | string | 예 | 문서 전체에서 유일한 ID |
| `value` | string | 예 | 표시할 Unicode 문자열 |
| `styleRefs` | string[] | 아니오 | 공유 스타일 ID 목록 |
| `style` | Style | 아니오 | Layout과 Text 스타일 |
| `bind` | object | 아니오 | `value` Binding |
| `extensions` | object | 아니오 | 노드 확장 데이터 |
| `extras` | any | 아니오 | 애플리케이션별 데이터 |

TextNode는 `children`을 가질 수 없다.

## 10. 핵심 Element

UDOM 0.1의 핵심 `name`은 다음과 같다.

| 이름 | 목적 | 주요 속성 |
|---|---|---|
| `view` | 일반 레이아웃과 시각 컨테이너 | 없음 |
| `image` | 이미지 또는 Sprite 표시 | `resource`, `fit`, `position` |
| `button` | 실행 가능한 버튼 | `disabled` |
| `toggle` | boolean 상태 제어 | `checked`, `disabled` |
| `slider` | 연속 또는 단계 값 제어 | `value`, `min`, `max`, `step`, `disabled` |
| `text-input` | 텍스트 입력 | `value`, `placeholder`, `multiline`, `readOnly`, `disabled` |
| `scroll` | 스크롤 viewport | `axis`, `initialOffset` |
| `embed` | 외부 GameObject 배치 지점 | `object`, `fallbackLabel` |

### 공통 규칙

- `view`, `button`, `toggle`, `slider`, `text-input`과 `scroll`은 자식을 가질 수 있다.
- `image`와 `embed`는 0.1에서 자식을 가질 수 없다.
- 컨트롤에 자식이 있으면 그 자식은 컨트롤의 사용자 정의 시각 구조다.
- 컨트롤에 자식이 없으면 Renderer가 프로파일의 기본 시각 구조를 제공할 수 있다.
- 사용자 정의 자식은 부모 컨트롤의 인터랙션 영역 안에 포함된다.
- `disabled`의 기본값은 `false`다.
- 핵심 Element에 정의되지 않은 `properties`를 추가하면 안 된다. 새 의미는 `extensions`로 추가한다.

### 요소별 속성

| Element | 속성 | 형식 | 필수 | 기본값 |
|---|---|---|---:|---:|
| `image` | `resource` | Resource ID | 예 | - |
| `image` | `fit` | enum | 아니오 | `contain` |
| `image` | `position` | X/Y Length | 아니오 | 중앙 |
| `button` | `disabled` | boolean | 아니오 | `false` |
| `toggle` | `checked` | boolean | 아니오 | `false` |
| `toggle` | `disabled` | boolean | 아니오 | `false` |
| `slider` | `value` | number | 아니오 | `min` |
| `slider` | `min` | number | 아니오 | `0` |
| `slider` | `max` | number | 아니오 | `1` |
| `slider` | `step` | number | 아니오 | `0` |
| `slider` | `disabled` | boolean | 아니오 | `false` |
| `text-input` | `value` | string | 아니오 | `""` |
| `text-input` | `placeholder` | string | 아니오 | `""` |
| `text-input` | `multiline` | boolean | 아니오 | `false` |
| `text-input` | `readOnly` | boolean | 아니오 | `false` |
| `text-input` | `disabled` | boolean | 아니오 | `false` |
| `scroll` | `axis` | enum | 아니오 | `vertical` |
| `scroll` | `initialOffset` | X/Y number | 아니오 | `{ "x": 0, "y": 0 }` |
| `embed` | `object` | Binding ID | 예 | - |
| `embed` | `fallbackLabel` | string | 아니오 | `""` |

`view`에는 핵심 `properties`가 없다.

### image

```json
{
  "type": "element",
  "id": "profile-image",
  "name": "image",
  "properties": {
    "resource": "image.profile",
    "fit": "cover",
    "position": {
      "x": "50%",
      "y": "50%"
    }
  }
}
```

`fit` 값은 `fill`, `contain`, `cover`, `none`이다. 기본값은 `contain`이다.

`position` 기본값은 `{ "x": "50%", "y": "50%" }`다.

### toggle

```json
{
  "type": "element",
  "id": "music-toggle",
  "name": "toggle",
  "properties": {
    "checked": true
  },
  "bind": {
    "checked": "settings.musicEnabled"
  },
  "on": {
    "change": "settings.setMusicEnabled"
  }
}
```

`checked` 기본값은 `false`다. `checked` Binding이 있으면 초기 `checked`는 Binding이 값을 제공하기 전 표시할 초기값이다.

### slider

`min` 기본값은 `0`, `max` 기본값은 `1`, `step` 기본값은 `0`, `value` 기본값은 `min`이다.

`step`이 0이면 연속 값이다. `max`는 `min`보다 커야 하고 `value`는 범위 안에 있어야 한다.

### scroll

`axis` 값은 `vertical`, `horizontal`, `both`다. 기본값은 `vertical`이다.

Scroll 요소는 하나의 콘텐츠 자식을 갖는 것을 권장한다. 여러 자식이 있으면 Renderer는 순서를 유지하는 암묵적인 콘텐츠 컨테이너를 만들 수 있으며 이 사실을 진단에 기록해야 한다.

### embed

`object`는 외부 GameObject 또는 Prefab을 해석하기 위한 상징적 Binding ID다.

Renderer는 Embed 내부 오브젝트를 소유하거나 재생성하면 안 된다. Embed의 Rect와 연결 관계만 관리한다.

## 11. 스타일 모델

UDOM은 CSS selector와 cascade를 정의하지 않는다.

최종 스타일은 다음 순서로 병합한다.

1. UDOM 기본값
2. `styleRefs`에 적힌 공유 스타일을 배열 순서대로 적용
3. 노드의 inline `style` 적용

뒤에 적용된 값이 앞의 같은 값을 덮어쓴다. 객체는 속성 단위로 재귀 병합하고 배열은 전체를 교체한다.

UDOM 0.1은 스타일 상속을 정의하지 않는다. Synthesizer는 렌더링에 필요한 텍스트 스타일을 각 TextNode 또는 `styleRefs`에 명시해야 한다.

### StyleDefinition

```json
{
  "id": "card",
  "style": {
    "paint": {
      "backgrounds": [
        {
          "type": "color",
          "color": "#20243AFF"
        }
      ],
      "radius": {
        "topLeft": 24,
        "topRight": 24,
        "bottomRight": 24,
        "bottomLeft": 24
      }
    }
  }
}
```

| 속성 | 형식 | 필수 | 의미 |
|---|---|---:|---|
| `id` | string | 예 | 문서 내 유일한 스타일 ID |
| `style` | Style | 예 | 적용할 스타일 |
| `extensions` | object | 아니오 | 스타일 정의 확장 |
| `extras` | any | 아니오 | 애플리케이션별 데이터 |

Style은 `layout`, `paint`, `text`와 `transform`을 가질 수 있다.

## 12. Layout

Layout은 노드 자신의 박스와 자식 배치 방법을 정의한다.

### 공통 속성

| 속성 | 형식 | 기본값 | 의미 |
|---|---|---:|---|
| `mode` | `absolute`, `flex`, `none` | `absolute` | 자식 배치 알고리즘 |
| `position` | `flow`, `absolute` | `flow` | 부모 배치 참여 방식 |
| `x` | Length | `0` | absolute 위치 X |
| `y` | Length | `0` | absolute 위치 Y |
| `width` | Length | `"auto"` | 박스 너비 |
| `height` | Length | `"auto"` | 박스 높이 |
| `minWidth` | Length | `0` | 최소 너비 |
| `minHeight` | Length | `0` | 최소 높이 |
| `maxWidth` | Length | `"auto"` | 최대 너비 |
| `maxHeight` | Length | `"auto"` | 최대 높이 |
| `margin` | Edges | 모두 `0` | 바깥 여백 |
| `padding` | Edges | 모두 `0` | 안쪽 여백 |
| `overflowX` | enum | `visible` | 가로 overflow |
| `overflowY` | enum | `visible` | 세로 overflow |
| `zIndex` | integer | `0` | 같은 stacking context의 그리기 순서 |
| `aspectRatio` | number | 없음 | width / height |
| `flex` | FlexContainer | 없음 | Flex 자식 배치 |
| `flexItem` | FlexItem | 없음 | Flex 자식으로서의 속성 |

`mode: none`인 노드는 렌더링과 레이아웃에서 제외된다.

`overflowX`, `overflowY` 값은 `visible`, `hidden`, `scroll`이다.

`position: absolute`인 노드는 부모 Flex 흐름에 참여하지 않는다.

### Edges

Edges는 `top`, `right`, `bottom`, `left`를 가진다. 생략된 값은 0이다.

```json
{
  "top": 12,
  "right": 16,
  "bottom": 12,
  "left": 16
}
```

### FlexContainer

| 속성 | 기본값 | 허용 값 |
|---|---:|---|
| `direction` | `row` | `row`, `row-reverse`, `column`, `column-reverse` |
| `wrap` | `nowrap` | `nowrap`, `wrap`, `wrap-reverse` |
| `justify` | `start` | `start`, `center`, `end`, `space-between`, `space-around`, `space-evenly` |
| `alignItems` | `stretch` | `start`, `center`, `end`, `stretch` |
| `alignContent` | `stretch` | `start`, `center`, `end`, `stretch`, `space-between`, `space-around` |
| `rowGap` | `0` | 음수가 아닌 Length |
| `columnGap` | `0` | 음수가 아닌 Length |

### FlexItem

| 속성 | 기본값 | 의미 |
|---|---:|---|
| `grow` | `0` | 남은 공간 증가 비율 |
| `shrink` | `1` | 부족한 공간 축소 비율 |
| `basis` | `"auto"` | 초기 주축 크기 |
| `alignSelf` | `auto` | 개별 교차축 정렬 |
| `order` | `0` | Flex 시각 순서 |

`order`가 같으면 트리의 자식 순서를 유지한다.

## 13. Paint

Paint는 박스 안에 그릴 시각적 의미를 정의한다.

| 속성 | 형식 | 기본값 |
|---|---|---:|
| `visible` | boolean | `true` |
| `opacity` | number | `1` |
| `backgrounds` | Background[] | 빈 배열 |
| `border` | Border | 없음 |
| `radius` | Radius | 모두 `0` |
| `shadows` | Shadow[] | 빈 배열 |

`opacity`는 0 이상 1 이하여야 하며 자식 결과에도 적용된다.

`backgrounds`는 배열 앞에서 뒤로, 아래에서 위로 그린다.

### Color

UDOM 핵심 Color 형식은 sRGB `#RRGGBBAA` 문자열이다.

- `#FFFFFFFF`: 불투명 흰색
- `#00000000`: 완전 투명 검정

축약형, 이름 색상, `rgb()`와 `hsl()` 문자열은 0.1 핵심 형식이 아니다. Synthesizer가 `#RRGGBBAA`로 정규화해야 한다.

### Background

지원되는 `type`:

- `color`
- `linear-gradient`
- `radial-gradient`
- `conic-gradient`
- `image`

#### color

```json
{
  "type": "color",
  "color": "#20243AFF"
}
```

#### linear-gradient

```json
{
  "type": "linear-gradient",
  "angle": 135,
  "stops": [
    { "position": 0, "color": "#171A2BFF" },
    { "position": 0.55, "color": "#4C2C72FF" },
    { "position": 1, "color": "#B14B8EFF" }
  ]
}
```

- `angle`은 degree이며 기본값은 180이다.
- 0도는 아래에서 위로 진행한다.
- 90도는 왼쪽에서 오른쪽으로 진행한다.
- 양의 각도는 시계 방향이다.
- `stops`는 두 개 이상이어야 한다.
- stop `position`은 0 이상 1 이하여야 한다.
- position은 감소하지 않는 순서여야 한다.

#### radial-gradient

주요 속성:

- `center`: `{ "x": "50%", "y": "50%" }`
- `radius`: `{ "x": "50%", "y": "50%" }`
- `stops`: GradientStop[]

#### conic-gradient

주요 속성:

- `center`: `{ "x": "50%", "y": "50%" }`
- `angle`: 시작 각도
- `stops`: GradientStop[]

- `center`의 기본값은 `{ "x": "50%", "y": "50%" }`다.
- `angle`의 기본값은 `0`이다.
- `0`도는 중심에서 위쪽으로 향하는 선에서 시작한다.
- 양의 각도와 stop 진행 방향은 화면을 바라볼 때 시계 방향이다.

#### image background

주요 속성:

- `resource`: Resource ID
- `fit`: `fill`, `contain`, `cover`, `none`
- `position`: X/Y Length
- `repeat`: `none`, `x`, `y`, `both`

### Border

Border는 `top`, `right`, `bottom`, `left` EdgeBorder를 가진다.

EdgeBorder:

| 속성 | 기본값 | 의미 |
|---|---:|---|
| `width` | `0` | 음수가 아닌 design unit |
| `color` | `#00000000` | sRGB 색상 |
| `style` | `solid` | 0.1에서는 `solid`만 지원 |

### Radius

Radius는 `topLeft`, `topRight`, `bottomRight`, `bottomLeft`를 가진다. 값은 음수가 아닌 Length다.

### Shadow

| 속성 | 기본값 | 의미 |
|---|---:|---|
| `offsetX` | `0` | 오른쪽 방향 offset |
| `offsetY` | `0` | 아래 방향 offset |
| `blur` | `0` | 음수가 아닌 blur 반경 |
| `spread` | `0` | shadow 확장 |
| `color` | `#00000080` | shadow 색상 |
| `inset` | `false` | 내부 shadow 여부 |

Renderer가 특정 Shadow를 정확히 표현할 수 없다면 조용히 제거하지 말고 폴백과 손실을 진단해야 한다.

## 14. Text 스타일

Text 스타일은 TextNode에 적용한다.

| 속성 | 형식 | 기본값 |
|---|---|---:|
| `font` | Resource ID | Renderer 기본 Font |
| `fontSize` | number | `16` |
| `fontWeight` | integer | `400` |
| `fontStyle` | enum | `normal` |
| `color` | Color | `#000000FF` |
| `lineHeight` | number 또는 `"normal"` | `"normal"` |
| `letterSpacing` | number | `0` |
| `align` | enum | `start` |
| `verticalAlign` | enum | `top` |
| `wrap` | enum | `wrap` |
| `overflow` | enum | `clip` |
| `preserveWhitespace` | boolean | `false` |

`fontWeight`는 1 이상 1000 이하다.

`fontStyle`은 `normal`, `italic`이다.

`align`은 `start`, `center`, `end`, `justify`다.

`verticalAlign`은 `top`, `middle`, `bottom`이다.

`wrap`은 `wrap`, `nowrap`이다.

`overflow`는 `visible`, `clip`, `ellipsis`다.

브라우저와 Renderer의 Font metric 차이로 줄바꿈 또는 overflow가 달라질 수 있다. Renderer는 결과가 Layout 박스를 벗어나면 진단할 수 있어야 한다. 원본에서 스크롤이 없었다는 이유만으로 콘텐츠를 화면 밖에 방치하면 안 된다.

## 15. Transform

Transform 스타일은 다음 구조를 가진다.

```json
{
  "origin": {
    "x": "50%",
    "y": "50%"
  },
  "operations": [
    {
      "type": "translate",
      "x": 20,
      "y": 0
    },
    {
      "type": "rotate",
      "degrees": 15
    },
    {
      "type": "scale",
      "x": 1,
      "y": 1
    }
  ]
}
```

Transform operation은 배열 순서대로 적용한다. Transform은 Layout이 계산한 박스의 크기나 형제 배치에는 영향을 주지 않고, 계산된 박스를 그리는 단계에만 적용한다.

지원 operation:

- `translate`
- `rotate`
- `scale`

양의 `rotate.degrees`는 화면을 바라볼 때 시계 방향이다. Renderer는 Unity의 회전 방향을 그대로 노출하지 않고 UDOM 좌표계로 변환해야 한다.

`origin`의 기본값은 `{ "x": "50%", "y": "50%" }`다. `translate.x/y`는 생략하거나 `auto`이면 `0`이고, percentage는 노드 자신의 최종 Layout 박스에서 대응하는 축을 기준으로 계산한다. `scale.x/y`의 기본값은 `1`이다.

UDOM 0.1은 3D transform, skew와 임의 matrix를 핵심으로 정의하지 않는다.

## 16. Resource

Resource는 트리 밖에서 공유되는 외부 자산이다.

공통 속성:

| 속성 | 형식 | 필수 | 의미 |
|---|---|---:|---|
| `id` | string | 예 | 문서 내 유일한 Resource ID |
| `type` | enum | 예 | `image`, `sprite`, `font` |
| `uri` | string | 예 | UDOM 파일 기준 URI |
| `mimeType` | string | 아니오 | 리소스 media type |
| `hash` | string | 아니오 | 내용 검증용 hash |
| `extensions` | object | 아니오 | Resource 확장 |
| `extras` | any | 아니오 | 애플리케이션별 데이터 |

Image와 Sprite는 다음 속성을 추가할 수 있다.

- `width`: 원본 픽셀 너비
- `height`: 원본 픽셀 높이

Resource ID 예:

```text
image.profile
sprite.icon-settings
font.inter-semibold
```

상대 URI를 권장한다. Renderer가 원격 URI를 지원하지 않는다면 로컬 파일로 변환하도록 명확히 진단해야 한다.

Resource의 `width`와 `height`가 있으면 Image의 고유 크기와 aspect ratio 계산에 사용한다. Synthesizer가 화면에서 관찰한 크기를 원본 리소스 크기로 기록하면 안 된다.

## 17. Binding과 이벤트

UDOM은 실행 코드를 포함하지 않는다. 대신 호스트가 해석할 상징적 식별자를 제공한다.

### bind

`bind`는 노드의 값 slot과 외부 상태 식별자를 연결한다.

예:

```json
{
  "bind": {
    "checked": "settings.musicEnabled",
    "visible": "settings.advancedVisible"
  }
}
```

핵심 공통 slot:

- `visible`
- `enabled`

요소별 slot:

- TextNode: `value`
- `toggle`: `checked`
- `slider`: `value`
- `text-input`: `value`
- `scroll`: `offset`

Binding 식별자의 내부 문법과 Udon 구현은 UDOM 핵심 명세의 범위가 아니다.

### on

`on`은 의미 있는 UI 이벤트와 외부 action 식별자를 연결한다.

```json
{
  "on": {
    "activate": "profile.open",
    "change": "settings.setMusicEnabled"
  }
}
```

핵심 이벤트:

- `activate`: button 또는 실행 가능한 요소
- `change`: toggle, slider와 text-input 값 변경
- `submit`: text-input 입력 확정
- `scroll`: scroll offset 변경
- `focus`
- `blur`

Renderer는 Binding ID를 임의의 메서드 이름이나 GameObject 경로로 해석하면 안 된다. 별도의 Binding manifest 또는 사용자 설정이 이를 실제 Unity/Udon 대상에 연결한다.

## 18. extensions와 extras

### extensions

`extensions`는 핵심 명세 밖의 기능을 구조화해 추가한다.

확장 이름은 충돌을 피하기 위해 다음 형식을 권장한다.

```text
VENDOR_feature_name
```

HTML2VRC 프로젝트가 관리하는 실험적 확장은 `H2VRC_` 접두사를 사용할 수 있다.

확장을 사용하는 문서는 최상위 `extensionsUsed`에 이름을 포함해야 한다.

문서를 올바르게 해석하거나 렌더링하기 위해 확장이 반드시 필요하면 `extensionsRequired`에도 포함해야 한다.

- `extensionsRequired`는 `extensionsUsed`의 부분집합이어야 한다.
- 알 수 없는 optional 확장은 무시할 수 있지만 진단하는 것을 권장한다.
- 알 수 없는 required 확장이 있으면 Renderer는 문서 렌더링을 실패시켜야 한다.
- 핵심 속성과 같은 의미를 가진 데이터를 확장으로 중복하면 안 된다.

### extras

`extras`는 렌더링 의미에 영향을 주지 않는 애플리케이션별 데이터를 담는다.

예:

- 원본 URL
- CSS selector
- React 컴포넌트 경로
- AI 판단 근거와 confidence
- 편집기 상태

Renderer는 `extras`를 무시해도 동일한 시각과 인터랙션 의미를 유지해야 한다.

렌더링에 필요한 데이터를 `extras`에 넣으면 안 된다.

## 19. 검증 규칙

호환 Validator는 최소한 다음을 검사해야 한다.

### 문서

- 필수 최상위 속성 존재
- 지원 가능한 `asset.version`
- 유효한 viewport 크기
- 중복된 Node, Style과 Resource ID
- 존재하지 않는 style 또는 resource 참조
- 알 수 없는 required extension
- `extensionsRequired`가 `extensionsUsed`의 부분집합인지 여부

### 트리

- 정확히 하나의 root
- 노드 ID의 유일성
- Element와 Text 구조
- leaf Element의 자식 존재
- 컨트롤별 속성 형식과 범위

### Layout과 Paint

- 음수가 될 수 없는 크기
- 잘못된 percentage와 `"auto"` 사용
- 0~1 범위를 벗어난 opacity와 gradient stop
- 감소하는 gradient stop
- 잘못된 색상 형식
- Scroll 요소와 overflow 의미 충돌
- 지원되지 않는 Transform operation

### Binding

- 요소에 존재하지 않는 slot
- 요소가 발생시키지 않는 event
- 비어 있는 Binding과 action ID

Validator는 가능하면 다음 문맥을 진단에 포함해야 한다.

- 안정적인 오류 코드
- JSON Pointer
- Node ID
- 실제 값과 기대한 값
- 적용된 기본값
- 폴백 여부
- 해결 제안
- UDOM과 Renderer 버전

UDOM 0.1의 구조적 계약은
[`schemas/udom-0.1.schema.json`](./schemas/udom-0.1.schema.json)에,
언어 독립적인 적합성 예제는 [`fixtures`](./fixtures)에 함께 관리한다.
JSON Schema를 통과한 문서만 ID, 참조, 확장, Binding과 값 범위 같은
의미 검사를 수행한다. 공통 검증 순서와 진단 코드 규칙은
[`CONFORMANCE.md`](./CONFORMANCE.md)를 따른다.

## 20. 버전 호환성

UDOM 문서 버전은 `major.minor` 형식이다.

0.x 동안에는 실험을 위해 호환성을 깨는 변경이 발생할 수 있다.

1.0 이후의 기본 정책:

- major 변경은 호환성을 깰 수 있다.
- minor 변경은 기존 문서를 깨지 않는 기능 추가다.
- 명세 문서의 patch 변경은 의미를 바꾸지 않는 설명, 오탈자와 Schema 수정이다.

Renderer는 자신이 지원하는 범위보다 높은 `minVersion`의 문서를 렌더링하면 안 된다.

`version`이 더 높지만 `minVersion`이 Renderer 지원 범위 안에 있고 required extension을 모두 지원한다면 Renderer는 문서를 처리할 수 있다.

## 21. 전체 예시

```json
{
  "asset": {
    "version": "0.1",
    "generator": "html2vrc/skill 0.1"
  },
  "viewport": {
    "width": 1200,
    "height": 720,
    "fit": "contain"
  },
  "styles": [
    {
      "id": "label",
      "style": {
        "text": {
          "font": "font.inter",
          "fontSize": 30,
          "fontWeight": 500,
          "color": "#FFFFFFFF"
        }
      }
    }
  ],
  "resources": [
    {
      "id": "font.inter",
      "type": "font",
      "uri": "assets/Inter-VariableFont.ttf",
      "mimeType": "font/ttf"
    },
    {
      "id": "image.profile",
      "type": "image",
      "uri": "assets/profile.png",
      "mimeType": "image/png",
      "width": 1024,
      "height": 1024
    }
  ],
  "root": {
    "type": "element",
    "id": "settings-screen",
    "name": "view",
    "style": {
      "layout": {
        "mode": "flex",
        "width": "100%",
        "height": "100%",
        "padding": {
          "top": 64,
          "right": 72,
          "bottom": 64,
          "left": 72
        },
        "flex": {
          "direction": "column",
          "rowGap": 32
        }
      },
      "paint": {
        "backgrounds": [
          {
            "type": "linear-gradient",
            "angle": 135,
            "stops": [
              { "position": 0, "color": "#171A2BFF" },
              { "position": 1, "color": "#35245DFF" }
            ]
          }
        ]
      }
    },
    "children": [
      {
        "type": "text",
        "id": "settings-title",
        "value": "Settings",
        "style": {
          "text": {
            "font": "font.inter",
            "fontSize": 52,
            "fontWeight": 700,
            "color": "#FFFFFFFF"
          }
        }
      },
      {
        "type": "element",
        "id": "profile-card",
        "name": "view",
        "style": {
          "layout": {
            "mode": "flex",
            "height": 160,
            "padding": {
              "top": 24,
              "right": 28,
              "bottom": 24,
              "left": 28
            },
            "flex": {
              "direction": "row",
              "alignItems": "center",
              "columnGap": 24
            }
          },
          "paint": {
            "backgrounds": [
              {
                "type": "color",
                "color": "#FFFFFF14"
              }
            ],
            "radius": {
              "topLeft": 24,
              "topRight": 24,
              "bottomRight": 24,
              "bottomLeft": 24
            }
          }
        },
        "children": [
          {
            "type": "element",
            "id": "profile-image",
            "name": "image",
            "properties": {
              "resource": "image.profile",
              "fit": "cover"
            },
            "style": {
              "layout": {
                "width": 104,
                "height": 104
              },
              "paint": {
                "radius": {
                  "topLeft": 52,
                  "topRight": 52,
                  "bottomRight": 52,
                  "bottomLeft": 52
                }
              }
            }
          },
          {
            "type": "text",
            "id": "profile-name",
            "value": "Nupamo",
            "styleRefs": ["label"],
            "style": {
              "layout": {
                "flexItem": {
                  "grow": 1
                }
              }
            }
          },
          {
            "type": "element",
            "id": "music-toggle",
            "name": "toggle",
            "properties": {
              "checked": true
            },
            "style": {
              "layout": {
                "width": 88,
                "height": 48
              }
            },
            "bind": {
              "checked": "settings.musicEnabled"
            },
            "on": {
              "change": "settings.setMusicEnabled"
            }
          }
        ]
      }
    ]
  },
  "extras": {
    "source": {
      "url": "https://example.com/settings"
    }
  }
}
```

## 22. 0.1 이후 열려 있는 결정

- 여러 화면과 상태 variant를 하나의 문서에 포함할 것인가?
- responsive breakpoint를 핵심 명세로 만들 것인가?
- 상태별 스타일을 UDOM에 포함할 것인가?
- 애니메이션을 별도 명세로 둘 것인가?
- 복합 Font fallback과 rich text를 어떻게 표현할 것인가?
- border gradient와 여러 background blending을 핵심에 포함할 것인가?
- 접근성 역할을 핵심 Element와 별도로 표현할 것인가?
- Binding manifest의 표준 형식을 UDOM 저장소가 소유할 것인가?
- source provenance를 공식 확장으로 만들 것인가?

이 결정들은 Renderer 구현 이름을 UDOM에 노출하지 않고, 기존 문서의 의미를 가능한 한 유지하는 방향으로 이루어져야 한다.
