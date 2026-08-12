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
        public float[] minSize = { 0f, 0f };
        public float[] maxSize = { -1f, -1f };
        public bool[] autoSize = { false, false };
        public float aspectRatio;
        public string aspectRatioMode = "None";
        public string layout = "None";
        public float[] padding = { 0f, 0f, 0f, 0f };
        public float[] margin = { 0f, 0f, 0f, 0f };
        public float spacing;
        public string backgroundColor = "#00000000";
        public string backgroundType = "color";
        public float backgroundGradientAngle = 180f;
        public float[] backgroundGradientPositions = Array.Empty<float>();
        public string[] backgroundGradientColors = Array.Empty<string>();
        public float[] backgroundGradientCenter = { 50f, 50f };
        public bool[] backgroundGradientCenterIsPercent = { true, true };
        public float[] backgroundGradientRadius = { 50f, 50f };
        public bool[] backgroundGradientRadiusIsPercent = { true, true };
        public float[] cornerRadius = { 0f, 0f, 0f, 0f };
        public float[] cornerRadiusPercent = { -1f, -1f, -1f, -1f };
        public float[] borderWidth = { 0f, 0f, 0f, 0f };
        public string[] borderColor = { "#00000000", "#00000000", "#00000000", "#00000000" };
        public float[] shadowOffsets = Array.Empty<float>();
        public float[] shadowBlurs = Array.Empty<float>();
        public float[] shadowSpreads = Array.Empty<float>();
        public string[] shadowColors = Array.Empty<string>();
        public bool[] shadowInsets = Array.Empty<bool>();
        public string textColor = "#FFFFFFFF";
        public float fontSize = 24f;
        public float lineHeight = -1f;
        public float letterSpacing;
        public bool textWrap = true;
        public string textOverflow = "Ellipsis";
        public bool preserveWhitespace;
        public string alignment = "MiddleLeft";
        public string fontStyle = "Normal";
        public string childAlignment = "UpperLeft";
        public bool stretchChildrenWidth;
        public bool stretchChildrenHeight;
        public bool useResolvedChildrenWidth;
        public bool useResolvedChildrenHeight;
        public bool reverseChildren;
        public int flexOrder;
        public float flexibleWidth;
        public float flexibleHeight;
        public float[] transformOrigin = { 50f, 50f };
        public bool[] transformOriginIsPercent = { true, true };
        public string[] transformOperationTypes = Array.Empty<string>();
        public float[] transformOperationValues = Array.Empty<float>();
        public bool[] transformOperationValuesArePercent = Array.Empty<bool>();
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
