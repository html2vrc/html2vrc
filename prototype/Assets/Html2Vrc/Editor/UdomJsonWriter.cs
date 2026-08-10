using System;
using System.Globalization;
using System.Text;

namespace Html2Vrc.Editor
{
    internal static class UdomJsonWriter
    {
        public static string Write(UdomDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var writer = new Writer();
            writer.BeginObject();
            writer.StringProperty("schemaVersion", document.schemaVersion);
            writer.StringProperty("id", document.id);
            writer.StringProperty("name", document.name);
            writer.PropertyName("canvas");
            WriteCanvas(writer, document.canvas);
            writer.PropertyName("root");
            WriteNode(writer, document.root, 0);
            writer.EndObject();
            return writer.ToString();
        }

        private static void WriteCanvas(Writer writer, UdomCanvas canvas)
        {
            if (canvas == null)
            {
                writer.Null();
                return;
            }

            writer.BeginObject();
            writer.StringProperty("renderMode", canvas.renderMode);
            writer.FloatArrayProperty("size", canvas.size);
            writer.FloatProperty("scale", canvas.scale);
            writer.FloatProperty("viewportPixelRatio", canvas.viewportPixelRatio);
            writer.StringProperty("viewportFit", canvas.viewportFit);
            writer.EndObject();
        }

        private static void WriteNode(Writer writer, UdomNode node, int depth)
        {
            if (node == null)
            {
                writer.Null();
                return;
            }

            if (depth > 64)
            {
                throw new InvalidOperationException("UDOM 노드 중첩은 64단계를 넘을 수 없다.");
            }

            writer.BeginObject();
            writer.StringProperty("id", node.id);
            writer.StringProperty("type", node.type);
            writer.OptionalStringProperty("name", node.name);
            writer.OptionalStringProperty("text", node.text);
            writer.OptionalStringProperty("sprite", node.sprite);
            writer.PropertyName("style");
            WriteStyle(writer, node.style);

            if (node.binding != null)
            {
                writer.PropertyName("binding");
                writer.BeginObject();
                writer.OptionalStringProperty("action", node.binding.action);
                writer.OptionalStringProperty("targetSlot", node.binding.targetSlot);
                writer.EndObject();
            }

            if (node.embed != null)
            {
                writer.PropertyName("embed");
                writer.BeginObject();
                writer.OptionalStringProperty("targetSlot", node.embed.targetSlot);
                writer.EndObject();
            }

            var children = node.children ?? Array.Empty<UdomNode>();
            if (children.Length > 0)
            {
                writer.PropertyName("children");
                writer.BeginArray();
                for (var index = 0; index < children.Length; index++)
                {
                    writer.ArrayValuePrefix();
                    WriteNode(writer, children[index], depth + 1);
                }

                writer.EndArray();
            }

            writer.EndObject();
        }

        private static void WriteStyle(Writer writer, UdomStyle style)
        {
            if (style == null)
            {
                writer.Null();
                return;
            }

            writer.BeginObject();
            writer.FloatArrayProperty("position", style.position);
            writer.FloatArrayProperty("size", style.size);
            writer.StringProperty("layout", style.layout);
            writer.FloatArrayProperty("padding", style.padding);
            writer.FloatArrayProperty("margin", style.margin);
            writer.FloatProperty("spacing", style.spacing);
            writer.StringProperty("backgroundColor", style.backgroundColor);
            writer.StringProperty("textColor", style.textColor);
            writer.FloatProperty("fontSize", style.fontSize);
            writer.StringProperty("alignment", style.alignment);
            writer.FloatProperty("flexibleWidth", style.flexibleWidth);
            writer.FloatProperty("flexibleHeight", style.flexibleHeight);
            writer.EndObject();
        }

        private sealed class Writer
        {
            private readonly StringBuilder builder = new StringBuilder(4096);
            private int indent;
            private bool needsComma;

            public void BeginObject()
            {
                builder.Append('{');
                indent++;
                needsComma = false;
            }

            public void EndObject()
            {
                indent--;
                if (needsComma)
                {
                    NewLine();
                }

                builder.Append('}');
                needsComma = true;
            }

            public void BeginArray()
            {
                builder.Append('[');
                indent++;
                needsComma = false;
            }

            public void EndArray()
            {
                indent--;
                if (needsComma)
                {
                    NewLine();
                }

                builder.Append(']');
                needsComma = true;
            }

            public void PropertyName(string name)
            {
                Prefix();
                AppendEscaped(name);
                builder.Append(": ");
                needsComma = false;
            }

            public void StringProperty(string name, string value)
            {
                PropertyName(name);
                if (value == null)
                {
                    Null();
                }
                else
                {
                    AppendEscaped(value);
                    needsComma = true;
                }
            }

            public void OptionalStringProperty(string name, string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    StringProperty(name, value);
                }
            }

            public void FloatProperty(string name, float value)
            {
                PropertyName(name);
                AppendFloat(value);
                needsComma = true;
            }

            public void FloatArrayProperty(string name, float[] values)
            {
                PropertyName(name);
                if (values == null)
                {
                    Null();
                    return;
                }

                builder.Append('[');
                for (var index = 0; index < values.Length; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(", ");
                    }

                    AppendFloat(values[index]);
                }

                builder.Append(']');
                needsComma = true;
            }

            public void ArrayValuePrefix()
            {
                Prefix();
                needsComma = false;
            }

            public void Null()
            {
                builder.Append("null");
                needsComma = true;
            }

            public override string ToString()
            {
                return builder.ToString();
            }

            private void Prefix()
            {
                if (needsComma)
                {
                    builder.Append(',');
                }

                NewLine();
            }

            private void NewLine()
            {
                builder.AppendLine();
                builder.Append(' ', indent * 2);
            }

            private void AppendFloat(float value)
            {
                builder.Append(value.ToString("0.########", CultureInfo.InvariantCulture));
            }

            private void AppendEscaped(string value)
            {
                builder.Append('"');
                for (var index = 0; index < value.Length; index++)
                {
                    var character = value[index];
                    switch (character)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (character < 0x20)
                            {
                                builder.Append("\\u");
                                builder.Append(((int)character).ToString("x4"));
                            }
                            else
                            {
                                builder.Append(character);
                            }

                            break;
                    }
                }

                builder.Append('"');
            }
        }
    }
}
