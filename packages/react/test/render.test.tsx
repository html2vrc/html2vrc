import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { Button, Image, Text, UdomRenderError, View, renderToUDOM } from "../src/index.js";

const expected = JSON.parse(
  await readFile(new URL("./fixtures/basic.udom.json", import.meta.url), "utf8")
);

test("renders the minimal primitive set to conformant UDOM", () => {
  const document = renderToUDOM(
    <View
      id="screen"
      style={{
        layout: {
          mode: "flex",
          width: "100%",
          height: "100%",
          flex: {
            direction: "column",
            rowGap: 24
          }
        }
      }}
    >
      <Text
        id="title"
        style={{
          text: {
            fontSize: 48,
            color: "#FFFFFFFF"
          }
        }}
      >
        Hello VRChat
      </Text>
      <Image id="logo" src="image.logo" fit="contain" />
      <Button id="continue" on={{ activate: "menu.continue" }}>
        <Text id="continue-label">Continue</Text>
      </Button>
    </View>,
    {
      viewport: {
        width: 1200,
        height: 720,
        fit: "contain"
      },
      resources: [
        {
          id: "image.logo",
          type: "image",
          uri: "assets/logo.png",
          mimeType: "image/png",
          width: 512,
          height: 512
        }
      ]
    }
  );

  assert.deepEqual(document, expected);
});

test("generates deterministic IDs from keys and tree paths", () => {
  const render = () =>
    renderToUDOM(
      <View>
        <Text key="greeting">Hello</Text>
        <Button>
          <Text>Continue</Text>
        </Button>
      </View>,
      {
        viewport: {
          width: 1280,
          height: 720
        }
      }
    );

  const first = render();
  const second = render();

  assert.deepEqual(first, second);
  assert.equal(first.root.id, "view-0");
  assert.equal(first.root.type, "element");
  assert.equal(first.root.children?.[0]?.id, "text-0-greeting");
  assert.equal(first.root.children?.[1]?.id, "button-0-1");
  const button = first.root.children?.[1];
  assert.equal(button?.type, "element");
  assert.equal(button.children?.[0]?.id, "text-0-1-0");
});

test("surfaces UDOM conformance diagnostics", () => {
  assert.throws(
    () =>
      renderToUDOM(
        <View>
          <Image src="missing-resource" />
        </View>,
        {
          viewport: {
            width: 1280,
            height: 720
          }
        }
      ),
    (error: unknown) => {
      assert.ok(error instanceof UdomRenderError);
      assert.equal(error.diagnostics[0]?.code, "H2VRC-RESOURCE-002");
      assert.equal(error.diagnostics[0]?.nodeId, "image-0-0");
      return true;
    }
  );
});

test("rejects arbitrary React and bare text", () => {
  function App() {
    return <View />;
  }

  assert.throws(
    () =>
      renderToUDOM(<App />, {
        viewport: {
          width: 1280,
          height: 720
        }
      }),
    /accepts only View, Text, Image, Button/
  );

  assert.throws(
    () =>
      renderToUDOM(<View>bare text</View>, {
        viewport: {
          width: 1280,
          height: 720
        }
      }),
    /Wrap strings and numbers in <Text>/
  );
});
