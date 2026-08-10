# HTML2VRC React

> React JSX로 UDOM 0.1 문서를 만드는 정적 exporter.

`@html2vrc/react`는 React를 Unity에서 실행하거나 브라우저 DOM으로 렌더링하지 않는다. 제한된 JSX primitive를 UDOM으로 변환하고 `@html2vrc/udom` reference validator로 결과를 즉시 검사한다.

현재 목표는 UDOM 명세의 첫 실제 producer를 제공해, Unity Renderer를 만들기 전에 명세의 불편한 지점을 찾는 것이다.

## 상태

현재 구현은 **0.1 실험 단계**다.

지원:

- `View`
- `Text`
- `Image`
- `Button`
- React Fragment
- inline `style`과 `styleRefs`
- 이미지 Resource 참조
- 문자열 Binding과 event 참조
- 명시적 ID와 결정적 자동 ID
- UDOM conformance 자동 검증

아직 지원하지 않음:

- 임의의 React function/class component
- Hook, state와 effect
- 브라우저 DOM, HTML과 CSS
- 함수 event handler
- Unity와 VRChat 출력
- Toggle, Slider, TextInput, Scroll과 Embed

## 사용

```tsx
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
