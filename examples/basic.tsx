import { Button, Image, Text, View, renderToUDOM } from "../src/index.js";

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
    <Text id="title">Hello VRChat</Text>
    <Image id="logo" src="image.logo" fit="contain" />
    <Button id="continue" on={{ activate: "menu.continue" }}>
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

console.log(JSON.stringify(document, null, 2));
