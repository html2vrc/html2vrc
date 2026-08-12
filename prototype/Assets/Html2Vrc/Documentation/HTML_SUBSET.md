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
- `font-size`
- `text-align: left | center | right`
- `flex-grow`, `flex-shrink`: 0 이상의 숫자
- `flex-basis`: `auto`, 숫자, `px` 또는 percentage
- `order`: 정수

`data-layout="vertical|horizontal|none"`으로 레이아웃을 명시할 수도 있다.

HTML에서 만든 flex 트리도 canonical UDOM importer와 같은 크기·정렬 해석기를 사용한다. 따라서 grow/shrink/basis의 비율 재분배, min/max 고정과 남는 공간 재분배, 한 축 auto aspect ratio, reverse 순서, justify의 남는 공간 분배, wrap line 분할, align-content, 축별 gap, align-self와 wrap/nowrap 전환 시 안정 GameObject 수명주기가 두 입력 경로에서 동일하다. 지원 값이 아닌 `baseline`, 임의 keyword, `calc(...)`, 소수 order와 음수 grow/shrink는 추측하지 않고 오류로 반환한다.

CSS transform 함수는 웹 규칙대로 선언의 오른쪽부터 좌표에 적용된다. Converter는 함수 목록을 역전해 array-order인 canonical transform wrapper에 저장한다. Translate는 숫자·`px`·percentage, rotate는 숫자·`deg`·`rad`·`grad`·`turn`, scale은 유한한 숫자를 지원한다. Transform을 `none`으로 바꾸면 원래 노드를 유지한 채 생성 wrapper만 제거한다.

CSS 전체 호환은 목표가 아니다. `flex-basis`와 transform 이외의 percentage 길이, 양축이 모두 auto이거나 반대 고정 축·aspect-ratio가 없는 크기 auto, `aspect-ratio: auto`, `calc`, flex shorthand, transform의 matrix·skew·perspective와 3~4값 origin, `align-content: space-evenly`, `overflow: auto | scroll`, visible/hidden이 섞인 축별 overflow, Grid, 선택자, 외부 스타일시트, 애니메이션과 지원 목록 밖의 속성은 오류로 표시한다.

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
- `<img src>`는 인터넷 주소가 아니라 Unity의 `Assets/` 아래 Sprite 경로만 허용한다.
- CSS 상속, class 선택자, 웹폰트, 반응형 레이아웃은 없다.
- HTML 화면과 브라우저 픽셀 결과가 완전히 같다는 의미는 아니다.
