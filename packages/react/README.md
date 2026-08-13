# HTML2VRC React

> React JSX로 UDOM 0.1 문서를 만들고 브라우저에서 확인하는 저작 도구.

`@html2vrc/react`는 제한된 JSX primitive를 UDOM으로 변환하고 `@html2vrc/udom` reference validator로 결과를 즉시 검사한다. 같은 canonical UDOM을 독립형 HTML로 렌더링하거나 로컬 live-reload 서버에서 확인할 수 있다. React나 브라우저 런타임은 Unity 결과물에 포함되지 않는다.

현재 목표는 UDOM 명세의 첫 실제 producer를 제공해, Unity Renderer를 만들기 전에 명세의 불편한 지점을 찾는 것이다.

## 상태

현재 구현은 **0.1 실험 단계**다.

지원:

- `View`
- `Text`
- `Image`
- `Button`
- `Toggle`
- `Slider`
- `TextInput`
- `Scroll`
- `Embed`
- React Fragment
- inline `style`과 `styleRefs`
- 이미지 Resource 참조
- 문자열 Binding과 event 참조
- 명시적 ID와 결정적 자동 ID
- UDOM conformance 자동 검증
- canonical UDOM 브라우저 미리보기
- UDOM 문서와 상대 asset 변경 시 live reload
- default-export UDOM을 만드는 JS·TS·JSX·TSX source live reload

아직 지원하지 않음:

- 임의의 React function/class component
- Hook, state와 effect
- 임의의 브라우저 DOM, HTML과 CSS
- React 상태를 보존하는 HMR
- 함수 event handler
- Unity와 VRChat 출력

## 사용

```tsx
import * as React from "react";
import {
  Button,
  Image,
  Text,
  View,
  renderToUDOM
} from "@html2vrc/react";

const document = renderToUDOM(
  <View
    id="screen"
    style={{
      layout: {
        mode: "flex",
        width: "100%",
        height: "100%",
        flex: {
          direction: "column"
        }
      }
    }}
  >
    <Text id="title">Hello VRChat</Text>
    <Image id="logo" src="image.logo" />
    <Button
      id="continue"
      on={{ activate: "menu.continue" }}
    >
      <Text id="continue-label">Continue</Text>
    </Button>
  </View>,
  {
    viewport: {
      width: 1200,
      height: 720
    },
    resources: [
      {
        id: "image.logo",
        type: "image",
        uri: "assets/logo.png"
      }
    ]
  }
);
```

`on`은 JavaScript 함수를 받지 않는다. 값은 Renderer가 별도 Binding manifest에서 해석할 상징적인 ID다.

## 브라우저 미리보기

`renderPreviewToHTML()`은 canonical UDOM 0.1을 먼저 검증한 다음 독립형 HTML 문자열을 만든다. `styleRefs`와 inline style, viewport fit, paint layer, font, 2D transform과 모든 0.1 primitive를 반영한다. `bind`와 `on`은 진단용 `data-*` 속성으로만 표시하며 실행하지 않는다.

```ts
import {
  renderPreviewToHTML,
  renderToUDOM
} from "@html2vrc/react";

const document = renderToUDOM(tree, options);
const html = renderPreviewToHTML(document, {
  title: "World settings"
});
```

로컬 개발에서는 UDOM 파일과 같은 폴더 아래에서 `resources`에 선언된 상대 이미지·font만 제공하고, UDOM 또는 같은 폴더 아래의 asset이 바뀌면 연결된 브라우저를 자동으로 새로고침한다.

```bash
npm run preview -- ./path/to/screen.udom.json
npm run preview -- ./path/to/screen.udom.json --port 5173 --title "Settings"
```

React source를 직접 미리 보려면 module이 canonical UDOM을 default export하면 된다. `.js`, `.jsx`, `.ts`, `.tsx`를 지원하며 같은 폴더 아래의 import dependency가 바뀌어도 격리된 새 프로세스에서 module을 다시 실행한다.

```tsx
import * as React from "react";
import {
  Text,
  View,
  renderToUDOM
} from "@html2vrc/react";

export default renderToUDOM(
  <View id="screen">
    <Text id="title">Hello VRChat</Text>
  </View>,
  { viewport: { width: 1200, height: 720 } }
);
```

```bash
npm run preview -- ./screen.tsx
```

기본 주소는 `http://127.0.0.1:4173/`이다. 외부 접근을 의도한 경우에만 `--host`를 지정한다. source 평가는 기본 10초 뒤 중단되며 `--source-timeout`으로 조정할 수 있다. JS·TS module은 로컬 코드를 실제로 실행하므로 신뢰하는 source만 연다. 컴파일이나 UDOM 검증이 실패하면 오류 화면을 유지하고 다음 저장 때 자동 복구한다.

이 흐름은 source와 dependency를 다시 실행하는 live reload다. React component state를 보존하는 HMR, 임의의 function component와 Hook 지원은 아직 제공하지 않는다.

서버를 다른 도구 안에서 제어하려면 Node 전용 subpath를 사용한다.

```ts
import {
  loadPreviewDocument,
  startPreviewServer
} from "@html2vrc/react/preview-server";

const document = await loadPreviewDocument("./screen.tsx");

const preview = await startPreviewServer({
  file: "./screen.tsx",
  port: 0
});
console.log(preview.url);
await preview.close();
```

### 컨트롤

컨트롤 prop은 canonical UDOM 속성과 같은 의미를 가진다. `false`, `0`, 빈 문자열도 생략하지 않고 출력한다. 컨트롤 자식은 사용자 정의 시각 구조가 되며 `Image`와 `Embed`는 자식을 받지 않는다.

```tsx
<Toggle
  id="music"
  checked={false}
  bind={{ checked: "settings.music" }}
  on={{ change: "settings.setMusic" }}
>
  <Text>Music</Text>
</Toggle>
<Slider id="volume" min={0} max={100} value={50} step={1} />
<TextInput id="name" value="" placeholder="Name" />
<Scroll id="gallery" axis="both" initialOffset={{ x: 0, y: 24 }}>
  <View>{/* scroll content */}</View>
</Scroll>
<Embed
  id="avatar"
  object="world.avatar"
  fallbackLabel="Avatar unavailable"
/>
```

`object`, `bind`와 `on` 값은 실행 코드나 GameObject 경로가 아니라 별도 manifest가 해석할 상징적 ID다.

## ID

UDOM은 모든 노드에 문서 전체에서 유일하고 안정적인 ID를 요구한다.

- `id`를 직접 지정하는 방식을 권장한다.
- 생략하면 primitive 이름, React key와 트리 경로를 사용해 결정적으로 생성한다.
- 목록에는 React `key`를 사용해야 형제 삽입 후에도 ID가 안정적이다.
- 자동 ID는 빠른 프로토타입용이며 외부 Binding 대상에는 명시적 `id`를 사용해야 한다.

## 오류 처리

생성된 문서가 UDOM Schema 또는 의미 규칙을 통과하지 못하면 `renderToUDOM()`은 `UdomRenderError`를 던진다.

```ts
try {
  renderToUDOM(tree, options);
} catch (error) {
  if (error instanceof UdomRenderError) {
    console.error(error.diagnostics);
  }
}
```

진단에는 UDOM 오류 코드, JSON Pointer와 가능한 경우 Node ID가 포함된다.

## 개발

```bash
npm install
npm run check
npm pack --dry-run
```

CI는 Node.js 20과 22에서 typecheck, test와 build를 실행한다.

## 라이선스

MIT
