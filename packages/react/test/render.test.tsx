import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  Button,
  Embed,
  Image,
  Scroll,
  Slider,
  Text,
  TextInput,
  Toggle,
  UdomRenderError,
  View,
  renderToUDOM
} from "../src/index.js";

const expected = JSON.parse(
  await readFile(new URL("./fixtures/basic.udom.json", import.meta.url), "utf8")
);
const expectedControls = JSON.parse(
  await readFile(
    new URL("./fixtures/controls.udom.json", import.meta.url),
    "utf8"
  )
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
      <Button
        id="continue"
        on={{ activate: "menu.continue", focus: "controls.focusContinue" }}
      >
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

test("renders every interactive primitive to conformant UDOM", () => {
  const document = renderToUDOM(
    <View id="controls">
      <Toggle
        id="music"
        checked={false}
        disabled={false}
        bind={{ checked: "settings.music" }}
        on={{ change: "settings.setMusic", focus: "controls.focusMusic" }}
      >
        <Text id="music-label">Music</Text>
      </Toggle>
      <Slider
        id="volume"
        value={0}
        min={0}
        max={100}
        step={0}
        disabled={false}
        bind={{ value: "settings.volume" }}
        on={{ change: "settings.setVolume", blur: "controls.blurVolume" }}
      />
      <TextInput
        id="profile-name"
        value=""
        placeholder="Name"
        multiline={false}
        readOnly={false}
        disabled={false}
        bind={{ value: "profile.name" }}
        on={{
          change: "profile.setName",
          submit: "profile.saveName",
          focus: "controls.focusName",
          blur: "controls.blurName"
        }}
      />
      <Scroll
        id="gallery"
        axis="both"
        initialOffset={{ x: 12, y: 24 }}
        bind={{ offset: "gallery.offset" }}
        on={{ scroll: "gallery.setOffset" }}
      >
        <View id="gallery-content">
          <Text id="gallery-label">Gallery</Text>
        </View>
      </Scroll>
      <Embed
        id="avatar-preview"
        object="world.avatarPreview"
        fallbackLabel="Avatar unavailable"
      />
      <Text id="empty-status" value="" />
    </View>,
    {
      viewport: {
        width: 800,
        height: 600
      }
    }
  );

  assert.deepEqual(document, expectedControls);
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
    /accepts only @html2vrc\/react primitives/
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
