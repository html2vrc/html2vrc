using System;

namespace Html2Vrc
{
    [Serializable]
    public sealed class UdomDocument
    {
        public string schemaVersion;
        public string id;
        public string name;
        public UdomCanvas canvas = new UdomCanvas();
        public UdomNode root;
    }

    [Serializable]
    public sealed class UdomCanvas
    {
        public string renderMode = "WorldSpace";
        public float[] size = { 1200f, 800f };
        public float scale = 0.01f;
        public float viewportPixelRatio = 1f;
        public string viewportFit = "none";
    }

    [Serializable]
    public sealed class UdomNode
    {
        public string id;
        public string type;
        public string name;
        public string text;
        public string sprite;
        public string texture;
        public string imageFit = "fill";
        public string imagePositionX = "50%";
        public string imagePositionY = "50%";
        public float[] imageIntrinsicSize = { 0f, 0f };
        public bool interactable = true;
        public bool toggleValue;
        public float sliderValue;
        public float sliderMin;
        public float sliderMax = 1f;
        public float sliderStep;
        public string textInputValue;
        public string textInputPlaceholder;
        public bool textInputMultiline;
        public bool textInputReadOnly;
        public bool scrollAxisExplicit;
        public bool scrollHorizontal;
        public bool scrollVertical = true;
        public float[] scrollInitialOffset = { 0f, 0f };
        public UdomStyle style = new UdomStyle();
        public UdomBinding binding;
        public UdomEmbed embed;
        public UdomNode[] children = Array.Empty<UdomNode>();
    }

    [Serializable]
    public sealed class UdomStyle
    {
        public bool visible = true;
        public float opacity = 1f;
        public float[] position = { 0f, 0f };
        public float[] size = { 100f, 100f };
        public string layout = "None";
        public float[] padding = { 0f, 0f, 0f, 0f };
        public float[] margin = { 0f, 0f, 0f, 0f };
        public float spacing;
        public string backgroundColor = "#00000000";
        public string textColor = "#FFFFFFFF";
        public float fontSize = 24f;
        public string alignment = "MiddleLeft";
        public string fontStyle = "Normal";
        public string childAlignment = "UpperLeft";
        public float flexibleWidth;
        public float flexibleHeight;
    }

    [Serializable]
    public sealed class UdomBinding
    {
        public string action;
        public string targetSlot;
    }

    [Serializable]
    public sealed class UdomEmbed
    {
        public string targetSlot;
        public string fallbackLabel;
    }
}
