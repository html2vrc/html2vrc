import * as React from "react";
import {
  Embed,
  Scroll,
  Slider,
  Text,
  TextInput,
  Toggle,
  View,
  renderToUDOM
} from "../src/index.js";

const document = renderToUDOM(
  <View id="settings">
    <Toggle
      id="music"
      checked={false}
      bind={{ checked: "settings.music" }}
      on={{ change: "settings.setMusic" }}
    >
      <Text>Music</Text>
    </Toggle>
    <Slider id="volume" value={50} min={0} max={100} step={1} />
    <TextInput id="display-name" value="" placeholder="Display name" />
    <Scroll id="gallery" axis="both" initialOffset={{ x: 0, y: 24 }}>
      <View id="gallery-content">
        <Text>Gallery content</Text>
      </View>
    </Scroll>
    <Embed
      id="avatar-preview"
      object="world.avatarPreview"
      fallbackLabel="Avatar unavailable"
    />
  </View>,
  {
    viewport: {
      width: 800,
      height: 600
    }
  }
);

export default document;
