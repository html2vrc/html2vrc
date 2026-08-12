# HTML 입력 0.1

이 기능은 브라우저를 Unity에 넣는 기능이 아니다. 작은 정적 HTML 부분집합을 UDOM 0.1로 번역한 뒤, 기존 UDOM Importer와 같은 Unity 네이티브 UI 생성기를 사용한다.

## 지원 요소

| HTML | 변환 결과 |
| --- | --- |
| `main`, `section`, `div`, `header`, `footer`, `nav`, `article` | `Panel` |
| `h1`, `h2`, `h3`, `p`, `span` | `Text` |
| `img` | `Image` |
| `button` | `Button` |
| `ul`, `ol` | 세로 `Panel` |
| `ul data-scroll="true"` | `ScrollView` |
| `li` | 배경을 가진 `Panel`과 Text |
| `embed` 또는 `data-embed-slot` | `Embed` |

모든 UI 요소에는 `id`가 필요하다. 이 값이 UDOM 안정 ID가 되므로 HTML을 수정하고 다시 생성해도 같은 Unity GameObject를 갱신할 수 있다. Button과 `li`의 내부 Text에는 `{id}-label`이라는 결정적인 ID가 붙는다.

## 문서와 Canvas 설정

`body` 또는 최상위 UI 요소에 다음 값을 둘 수 있다.

```html
<body
  data-document-id="world-settings-html"
  data-document-name="World Settings"
  data-canvas-size="1200 800"
  data-canvas-scale="0.01">
</body>
```

## 지원하는 인라인 CSS

- `width`, `height`: 숫자 또는 `px`. `aspect-ratio`와 반대 축의 고정 크기가 있으면 한 축 `auto` 지원
- `min-width`, `min-height`: 숫자 또는 `px`
- `max-width`, `max-height`: 숫자, `px` 또는 `none`
- `aspect-ratio`: 양수 또는 `width / height`
- `left`, `top`: 숫자 또는 `px`
- `transform-origin`: 1~2개의 숫자, `px`, percentage 또는 `left | center | right | top | bottom`
- `transform`: `translate`, `translateX/Y`, `rotate`, `scale`, `scaleX/Y` 함수 또는 `none`
- `display: flex | block | none`
- `visibility: visible | hidden`: Layout과 GameObject를 유지하고 hidden에서 입력 차단
- `opacity`: 0~1 숫자 또는 0%~100%. 0이어도 CSS처럼 입력은 유지
- `z-index`: `auto` 또는 정수. 같은 부모 안의 paint 순서에만 적용
- `position: static | absolute`: absolute는 `left`, `top`을 부모 왼쪽 위 기준으로 적용
- `overflow: visible | hidden`: 한 값 또는 x·y 순서의 두 값
- `overflow-x`, `overflow-y: visible | hidden`: 최종 두 축이 같은 값일 때만 지원
- `flex-direction: row | row-reverse | column | column-reverse`
- `flex-wrap: nowrap | wrap | wrap-reverse`
- `justify-content: flex-start | start | center | flex-end | end | space-between | space-around | space-evenly`
- `align-items: normal | flex-start | start | center | flex-end | end | stretch`
- `align-content: normal | flex-start | start | center | flex-end | end | stretch | space-between | space-around`
- `align-self: auto | normal | flex-start | start | center | flex-end | end | stretch`
- `gap`: 1~2개의 숫자 또는 `px` 값. 두 값이면 row, column 순서
- `row-gap`, `column-gap`: 숫자 또는 `px`
- `padding`, `margin`: CSS의 1~4개 값
- `background-color`, `color`: `#RRGGBB` 또는 `#RRGGBBAA`
- `background`, `background-image`: 단일 `linear-gradient(...)`, `radial-gradient(...)`, `conic-gradient(...)`, `none`; `background`은 단일 hex color와 `transparent`도 지원
- `linear-gradient`: 기본 아래 방향, cardinal `to top | right | bottom | left` 또는 숫자·deg·rad·grad·turn angle, 두 개 이상의 hex·transparent·currentColor stop과 선택적 0%~100% 위치
- `radial-gradient`: 생략하거나 `ellipse <rx> <ry>`로 쓰는 타원, `circle <r>` 또는 shape을 생략한 단일 px 원형 반지름, 선택적 `at <position>`, 두 개 이상의 linear-gradient와 같은 color stop
- `conic-gradient`: 선택적 `from <angle>` 다음 선택적 `at <position>`, 두 개 이상의 hex·transparent·currentColor stop과 선택적 0%~100% 또는 0~360도 angle 위치
- `border`: 0 이상 숫자·`px` width, `solid | none`, hex color·`transparent | currentColor`의 shorthand
- `border-width`, `border-style`, `border-color`: CSS 순서의 1~4개 값
- `border-top|right|bottom|left` 및 각 edge의 `-width`, `-style`, `-color` longhand
- `border-radius`: CSS 모서리 순서의 1~4개 0 이상 숫자 또는 `px`
- `border-top-left|top-right|bottom-right|bottom-left-radius`: 단일 원형 radius
- `box-shadow`: 쉼표로 나눈 다중 layer. 선택적 `inset`, X/Y offset, 선택적 blur·spread와 hex color·`transparent | currentColor`, 또는 `none`
- `font-size`: 숫자 또는 `px`
- `font-weight`: `normal`, `bold` 또는 1~1000 정수. 600 이상은 Bold
- `font-style: normal | italic`
- `line-height`: `normal`, 양수 unitless 배수, 양수 percentage 또는 양수 `px`
- `letter-spacing`: `normal`, 유한한 양수·0·음수 숫자 또는 `px`
- `white-space: normal | nowrap | pre | pre-wrap | pre-line`
- `text-overflow: clip | ellipsis`
- `text-align: left | start | center | right | end | justify`
- `object-fit: fill | contain | cover | none`: `<img>` 콘텐츠의 크기 맞춤
- `object-position`: `<img>`의 1~2개 숫자, `px`, percentage 또는 `left | center | right | top | bottom` 위치
- `flex-grow`, `flex-shrink`: 0 이상의 숫자
- `flex-basis`: `auto`, 숫자, `px` 또는 percentage
- `order`: 정수

`data-layout="vertical|horizontal|none"`으로 레이아웃을 명시할 수도 있다.

HTML에서 만든 flex 트리도 canonical UDOM importer와 같은 크기·정렬 해석기를 사용한다. 따라서 grow/shrink/basis의 비율 재분배, min/max 고정과 남는 공간 재분배, 한 축 auto aspect ratio, reverse 순서, justify의 남는 공간 분배, wrap line 분할, align-content, 축별 gap, align-self와 wrap/nowrap 전환 시 안정 GameObject 수명주기가 두 입력 경로에서 동일하다. 지원 값이 아닌 `baseline`, 임의 keyword, `calc(...)`, 소수 order와 음수 grow/shrink는 추측하지 않고 오류로 반환한다.

CSS transform 함수는 웹 규칙대로 선언의 오른쪽부터 좌표에 적용된다. Converter는 함수 목록을 역전해 array-order인 canonical transform wrapper에 저장한다. Translate는 숫자·`px`·percentage, rotate는 숫자·`deg`·`rad`·`grad`·`turn`, scale은 유한한 숫자를 지원한다. Transform을 `none`으로 바꾸면 원래 노드를 유지한 채 생성 wrapper만 제거한다.

`<img src>`는 Unity `Assets/` 안의 일반 Texture2D와 Sprite를 자동 구분한다. `object-position` 한 값은 CSS처럼 다른 축을 가운데로 두며, 두 값은 `bottom right`처럼 세로·가로 순서로 써도 각 축에 맞춰 정규화한다. Percentage는 이미지 박스에서 남는 공간을 기준으로 계산하므로 `cover`에서 음수가 되는 잘린 영역도 같은 규칙으로 정렬된다.

Border shorthand와 longhand는 inline 선언의 source-order대로 합성한다. 생략된 shorthand width는 CSS `medium`에 대응하는 3 design unit, 생략된 color는 최종 `color`, 생략된 style은 `none`이다. 결과는 canonical의 단일 SDF border overlay와 rounded stencil mask를 사용한다. HTML subset의 `width`·`height`는 Unity의 최종 Rect 크기이므로 padding과 border를 포함하는 `border-box` 의미다.

`box-shadow`의 처음 두 length는 오른쪽·아래 방향 offset이며 blur는 음수가 아닌 값, spread는 음수를 허용한다. 생략한 blur/spread는 0, color는 최종 `color`다. CSS는 첫 shadow를 위에 그리지만 canonical은 뒤 shadow를 위에 그리므로 Converter가 layer 배열을 역전한다. 각 layer는 radius-aware SDF Image가 되고 outer shadow는 레이아웃 밖에서, inset shadow는 배경 위·콘텐츠와 border 아래에서 그려진다.

`linear-gradient`의 생략된 첫·마지막 stop은 0%·100%이고 그 사이의 연속 생략 stop은 양옆 위치 사이에 균등 배치한다. 뒤 stop 위치가 앞보다 작으면 CSS 규칙대로 앞 위치까지 올린다. 결과는 canonical의 1025×1 LUT와 VRChat-safe UI Material을 사용하며 radius mask와 합성된다. `background-image: none`은 현재 단색 background-color를 유지하고 gradient 에셋만 제거한다.

`radial-gradient`는 명시한 X/Y 반지름과 중심의 숫자·`px`·percentage 혼합 단위를 canonical radial paint에 그대로 보존한다. `left | center | right | top | bottom` 중심 keyword와 세로·가로 순서 전환도 지원한다. Geometry 전체를 생략하거나 `at`만 쓰면 canonical 기본 중심과 X/Y 반지름 `50% 50%`를 사용한다. `circle`은 최종 Rect에서도 같은 절대 반지름을 유지하도록 percentage가 아닌 단일 px 값이 필요하다. 브라우저 크기에 따라 계산해야 하는 `closest-side | farthest-side | closest-corner | farthest-corner`와 shape만 쓴 암시적 원형 크기는 지원하지 않는다. Color stop 보간·fix-up·currentColor, LUT, rounded mask와 `none` 재생성 수명주기는 linear-gradient와 같다.

`conic-gradient`의 생략한 시작 각도는 위쪽 기준 0°이고 양의 방향은 시계 방향이다. `from`은 숫자를 degree로 보거나 `deg | rad | grad | turn`을 받아 0~360°로 정규화하며, 생략한 중심은 `50% 50%`다. `at`은 radial과 같은 mixed-unit 위치 및 keyword 축 전환을 사용한다. Stop 위치는 percentage 또는 0~360도 angle을 canonical 0~1로 바꾸고, 생략 stop 보간과 감소 위치 fix-up은 다른 gradient와 공유한다. Pixel stop, color hint, double-position stop은 지원하지 않는다.

`font-weight` 숫자는 canonical UDOM과 같은 600 경계로 TMP Bold 여부를 정하며 `font-style: italic`과 `BoldItalic`으로 조합한다. 상대 굵기 `bolder | lighter`, 소수·범위 밖 weight와 `oblique`는 상속 또는 기울기 각도를 손실 없이 계산할 수 없어 오류로 반환한다. `<strong> | <b>`는 Bold, `<em> | <i>`는 Italic, `<small>`은 부모 크기의 5/6인 구조화 text run을 만들며 서로 중첩할 수 있다. `<span>`은 스타일 없는 인라인 그룹이다. Run의 평문을 합치면 항상 Text의 `text`와 같고, Unity Builder만 검증된 run을 TMP 태그로 조립한다. 일반 텍스트의 `<`, `</noparse>`, `<color=...>` 같은 문자열은 별도로 격리하므로 TMP 명령으로 해석되지 않는다. Unitless `line-height`는 최종 `font-size`의 배수이고 percentage도 최종 font-size를 기준으로 계산하므로 두 선언의 source-order와 무관하다. `normal`은 TMP font metric을 사용하고 절대값은 실제 TMP FontAsset에서 원하는 baseline 간격이 나오도록 line spacing으로 환산한다. Letter spacing은 design unit에서 TMP em 상대값으로 변환한다. `white-space`는 공백 보존과 wrapping을 함께 정하며, `pre-line`은 줄바꿈을 유지하면서 각 줄의 연속 공백을 축약한다. `<br>`은 모든 모드에서 강제 줄바꿈이고 혼합 콘텐츠의 원래 순서를 유지한다. `dir`은 지원하지 않으므로 `start`·`end` 정렬은 LTR 기준 left·right다. Button과 `li`의 파생 label은 부모의 font style·font size·line height·letter spacing·wrap·overflow·whitespace와 구조화 run을 복사한다.

Opacity는 CanvasGroup alpha로 자식 전체에 곱해진다. Visibility hidden도 GameObject를 비활성화하지 않아 flex 공간과 안정 ID를 유지하지만 CanvasGroup의 interactable·raycast를 끈다. Z-index가 형제마다 다르면 flex 좌표를 먼저 고정한 뒤 낮은 정수부터 높은 정수 순으로 가장 바깥 margin·transform·shadow wrapper를 배치한다. 모두 auto/0으로 돌아오면 native Layout Group을 복원한다.

CSS 전체 호환은 목표가 아니다. `flex-basis`, transform, object-position과 radial/conic geometry 이외의 percentage 길이, 양축이 모두 auto이거나 반대 고정 축·aspect-ratio가 없는 크기 auto, `aspect-ratio: auto`, `calc`, flex shorthand, transform의 matrix·skew·perspective와 3~4값 origin, dashed/dotted/double border, percentage border width, percentage 및 `/` 타원형 radius, named/rgb/hsl color, percentage shadow length, linear-gradient의 corner 방향·px stop·color hint·double-position stop, radial-gradient의 size keyword·암시적 circle 크기, conic-gradient의 px stop·color hint·double-position stop, repeating gradient와 다중 background, 상대 font weight, oblique font style, 인라인 요소의 임의 CSS와 rich-text 색상·font family, `line-height: calc(...)`, percentage letter-spacing, `white-space: break-spaces`, 다중 text-overflow 값, `visibility: collapse`와 hidden 부모 안에서 자식 visible로 다시 표시하는 override, 중첩 stacking context·isolation, `object-fit: scale-down`, object-position의 3~4값 edge offset, `align-content: space-evenly`, `overflow: auto | scroll`, visible/hidden이 섞인 축별 overflow, Grid, 선택자, 외부 스타일시트, 애니메이션과 지원 목록 밖의 속성은 오류로 표시하거나 계약 밖으로 둔다.

## 안전한 버튼 동작

JavaScript 대신 기존 UDOM 안전 동작 이름을 사용한다.

```html
<button
  id="toggle-light"
  data-action="ToggleLight"
  data-target-slot="world-light">
  Toggle World Light
</button>
```

지원 동작은 `ToggleActive`, `SetActive`, `SetInactive`, `ToggleLight`, `ClosePanel`이다. `onclick`, `<script>`, `<style>`은 오류로 거부한다. HTML에서 새 Udon 코드를 만들지 않고, 이미 검증된 하나의 UdonSharp Behaviour에 연결한다.

## 사용법

1. `Assets/Html2Vrc/Samples/WorldSettings.html`을 선택한다.
2. `Tools > HTML2VRC > HTML Importer (Preview)`를 연다.
3. `HTML 검증 / 변환`을 누른다.
4. 필요하면 `UDOM JSON 저장`으로 사람이 읽을 수 있는 중간 결과를 저장한다.
5. `Generate / Regenerate`를 누른다.
6. 생성 루트의 `External References`에서 `world-light` 슬롯에 Light를 연결한다.

HTML 파일 자체가 생성 루트의 Source Asset으로 기록된다. 같은 HTML 파일로 다시 생성하면 안정 ID를 기준으로 기존 노드를 갱신하고 외부 참조를 유지한다.

## 현재 한계

- 일반 웹사이트 URL, React, JavaScript, 외부 CSS를 읽지 않는다.
- HTML5 오류 복구 전체를 구현한 브라우저급 파서가 아니다. 태그 짝과 따옴표가 올바른 정적 HTML을 전제로 한다.
- `<img src>`는 인터넷 주소가 아니라 Unity의 `Assets/` 아래 Texture2D 또는 Sprite 경로만 허용한다.
- CSS 상속, class 선택자, 웹폰트, 반응형 레이아웃은 없다.
- HTML 화면과 브라우저 픽셀 결과가 완전히 같다는 의미는 아니다.
