using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MarginaliaMod
{
    // Turns a MediaWiki article's HTML into TextMeshPro rich text: headings, emphasis,
    // lists, flattened tables, and <link="Title"> tags for links to other articles.
    // Images, edit links, references and navigation boxes are dropped.
    public static class WikiHtml
    {
        public const string LinkColor = "#D9B36C";
        private const string HeadingColor = "#B8944F";

        private sealed class Node
        {
            public string Tag;
            public string Attrs = "";
            public string Text;
            public Node Parent;
            public readonly List<Node> Children = new List<Node>();
        }

        private static readonly Regex Token = new Regex(
            @"<!--.*?-->|<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>|[^<]+|<",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly HashSet<string> VoidTags = new HashSet<string>
            { "br", "img", "hr", "wbr", "input", "meta", "link", "source", "col", "area" };

        private static readonly HashSet<string> SkippedTags = new HashSet<string>
            { "script", "style", "img", "figure", "figcaption", "noscript", "audio", "video", "map", "math", "svg", "input", "button" };

        private static readonly string[] SkippedClasses =
            { "mw-editsection", "reference", "references", "mw-references-wrap", "navbox", "toc", "noprint", "printfooter", "gallery", "thumb", "mw-empty-elt" };

        private static readonly string[] NonArticlePrefixes =
            { "File:", "Category:", "Special:", "Template:", "User:", "User talk:", "Help:", "Fandom:", "Talk:", "Module:", "MediaWiki:" };

        public static string ToRichText(string html)
        {
            var writer = new Writer(false);
            RenderChildren(Parse(html), writer);
            return writer.Finish();
        }

        // ---- Parsing: a forgiving tree builder, good enough for MediaWiki's tidy output.

        private static Node Parse(string html)
        {
            var root = new Node { Tag = "#root" };
            var current = root;
            foreach (Match m in Token.Matches(html))
            {
                if (m.Value.StartsWith("<!--"))
                    continue;
                if (!m.Groups[2].Success)
                {
                    current.Children.Add(new Node { Tag = "#text", Text = m.Value, Parent = current });
                    continue;
                }

                var tag = m.Groups[2].Value.ToLowerInvariant();
                if (m.Groups[1].Value == "/")
                {
                    // Close the nearest matching open element; ignore strays.
                    for (var n = current; n != root; n = n.Parent)
                    {
                        if (n.Tag == tag)
                        {
                            current = n.Parent;
                            break;
                        }
                    }
                    continue;
                }

                var attrs = m.Groups[3].Value;
                var node = new Node { Tag = tag, Attrs = attrs, Parent = current };
                current.Children.Add(node);
                if (!VoidTags.Contains(tag) && !attrs.TrimEnd().EndsWith("/"))
                    current = node;
            }
            return root;
        }

        private static string Attr(Node node, string name)
        {
            var m = Regex.Match(node.Attrs, @"(?:^|\s)" + name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
            if (!m.Success)
                return null;
            var value = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            return WebUtility.HtmlDecode(value);
        }

        private static bool HasClass(Node node, params string[] classes)
        {
            var value = Attr(node, "class");
            if (string.IsNullOrEmpty(value))
                return false;
            var own = value.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            return own.Any(classes.Contains);
        }

        // ---- Rendering

        private static void RenderChildren(Node node, Writer w)
        {
            foreach (var child in node.Children)
                Render(child, w);
        }

        private static void Render(Node n, Writer w)
        {
            if (n.Tag == "#text")
            {
                w.Text(WebUtility.HtmlDecode(n.Text));
                return;
            }
            if (SkippedTags.Contains(n.Tag) || HasClass(n, SkippedClasses))
                return;

            switch (n.Tag)
            {
                case "br":
                    w.Break(1);
                    break;
                case "p":
                case "blockquote":
                case "dl":
                    w.Break(2);
                    RenderChildren(n, w);
                    w.Break(2);
                    break;
                case "div":
                case "center":
                    w.Break(1);
                    RenderChildren(n, w);
                    w.Break(1);
                    break;
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    RenderHeading(n, w);
                    break;
                case "ul":
                case "ol":
                    w.Break(1);
                    w.Depth++;
                    RenderChildren(n, w);
                    w.Depth--;
                    w.Break(1);
                    break;
                case "li":
                    w.Break(1);
                    w.Raw(new string(' ', System.Math.Max(0, w.Depth - 1) * 4) + "• ");
                    RenderChildren(n, w);
                    break;
                case "dt":
                    w.Break(1);
                    Wrap(n, w, "<b>", "</b>");
                    break;
                case "dd":
                    w.Break(1);
                    w.Raw("    ");
                    RenderChildren(n, w);
                    break;
                case "b":
                case "strong":
                    Wrap(n, w, "<b>", "</b>");
                    break;
                case "i":
                case "em":
                    Wrap(n, w, "<i>", "</i>");
                    break;
                case "small":
                    Wrap(n, w, "<size=85%>", "</size>");
                    break;
                case "a":
                    RenderLink(n, w);
                    break;
                case "table":
                    RenderTable(n, w);
                    break;
                case "hr":
                    w.Break(2);
                    break;
                default:
                    RenderChildren(n, w);
                    break;
            }
        }

        private static void Wrap(Node n, Writer w, string open, string close)
        {
            w.Raw(open);
            RenderChildren(n, w);
            w.Raw(close);
        }

        private static void RenderHeading(Node n, Writer w)
        {
            int level = n.Tag[1] - '0';
            string size = level <= 2 ? "135%" : level == 3 ? "118%" : "105%";
            w.Break(2);
            w.Raw("<size=" + size + "><b><color=" + HeadingColor + ">");
            RenderChildren(n, w);
            w.Raw("</color></b></size>");
            w.Break(1);
        }

        private static void RenderLink(Node n, Writer w)
        {
            var href = Attr(n, "href") ?? "";
            var title = Attr(n, "title");
            bool internalArticle = href.StartsWith("/wiki/") && !string.IsNullOrEmpty(title) &&
                                   !HasClass(n, "new") && !NonArticlePrefixes.Any(p => title.StartsWith(p));
            if (!internalArticle)
            {
                RenderChildren(n, w);
                return;
            }
            w.Raw("<link=\"" + title.Replace("\"", "'") + "\"><color=" + LinkColor + ">");
            RenderChildren(n, w);
            w.Raw("</color></link>");
        }

        // Tables become lines: a two-cell row reads "Key: value" (as in infoboxes), wider
        // rows are joined with dots, and cells holding only decoration are dropped.
        private static void RenderTable(Node table, Writer w)
        {
            w.Break(2);
            foreach (var row in Rows(table))
            {
                var cells = row.Children
                    .Where(c => c.Tag == "td" || c.Tag == "th")
                    .Select(c => (header: c.Tag == "th", text: RenderCell(c, w.Depth)))
                    .Where(c => !IsBlank(c.text))
                    .ToList();
                if (cells.Count == 0)
                    continue;

                w.Break(1);
                if (cells.Count == 1)
                    w.Raw(cells[0].header ? "<b>" + cells[0].text + "</b>" : cells[0].text);
                else if (cells.All(c => c.header))
                    w.Raw("<b>" + string.Join("  ·  ", cells.Select(c => c.text)) + "</b>");
                else if (cells.Count == 2 && Tags.Replace(cells[0].text, "").Length <= MaxKeyLength)
                    w.Raw("<b>" + cells[0].text + "</b>: " + cells[1].text);
                else
                    w.Raw(string.Join("  ·  ", cells.Select(c => c.text)));
            }
            w.Break(2);
        }

        // Longer first cells are content, not a label.
        private const int MaxKeyLength = 40;

        private static IEnumerable<Node> Rows(Node node)
        {
            foreach (var child in node.Children)
            {
                if (child.Tag == "tr")
                    yield return child;
                else if (child.Tag == "thead" || child.Tag == "tbody" || child.Tag == "tfoot")
                    foreach (var row in Rows(child))
                        yield return row;
            }
        }

        private static string RenderCell(Node cell, int depth)
        {
            var w = new Writer(true) { Depth = depth };
            RenderChildren(cell, w);
            return w.Finish();
        }

        private static readonly Regex Tags = new Regex("<[^>]*>");
        private static readonly Regex Decoration = new Regex("[\\s“”„\"'·•,]+");

        private static bool IsBlank(string rich)
        {
            return Decoration.Replace(Tags.Replace(rich, ""), "").Length == 0;
        }

        // ---- Output

        // Accumulates rich text. Block writers turn breaks into newlines; inline writers
        // (table cells) turn them into ", " so a cell stays on one line.
        private sealed class Writer
        {
            private static readonly Regex Whitespace = new Regex(@"\s+");
            private static readonly Regex ExtraBlankLines = new Regex(@"\n{3,}");
            private static readonly Regex TrailingSeparator = new Regex(@",\s*((?:</[^>]+>)*)\s*$");
            private static readonly Regex RepeatedSeparator = new Regex(@",(?:\s*,)+");
            private static readonly Regex EmptyPair = new Regex(@"<(b|i|size|color|link)(=[^>]*)?>\s*</\1>");
            private static readonly Regex LeadingSeparator = new Regex(@"^\s*((?:<[^/][^>]*>)*)\s*,\s*");

            private readonly StringBuilder _sb = new StringBuilder();
            private readonly bool _inline;

            public int Depth;

            public Writer(bool inline)
            {
                _inline = inline;
            }

            private bool AtBoundary =>
                _sb.Length == 0 || _sb[_sb.Length - 1] == '\n' || _sb[_sb.Length - 1] == ' ';

            public void Text(string text)
            {
                text = Whitespace.Replace(text, " ");
                if (text.Length == 0)
                    return;
                if (text[0] == ' ' && AtBoundary)
                    text = text.TrimStart();
                // A zero-width space after '<' stops TMP reading article text as a tag.
                _sb.Append(text.Replace("<", "<​"));
            }

            public void Raw(string text)
            {
                _sb.Append(text);
            }

            public void Break(int lines)
            {
                while (_sb.Length > 0 && _sb[_sb.Length - 1] == ' ')
                    _sb.Length--;
                if (_sb.Length == 0)
                    return;
                if (_inline)
                {
                    if (_sb[_sb.Length - 1] == ',')
                        _sb.Append(' ');
                    else
                        _sb.Append(", ");
                    return;
                }
                int have = 0;
                for (int i = _sb.Length - 1; i >= 0 && _sb[i] == '\n'; i--)
                    have++;
                for (; have < lines; have++)
                    _sb.Append('\n');
            }

            public string Finish()
            {
                var text = _sb.ToString();
                for (string before = null; before != text;)
                {
                    before = text;
                    text = EmptyPair.Replace(text, "");
                }
                if (_inline)
                {
                    text = RepeatedSeparator.Replace(text, ",");
                    text = TrailingSeparator.Replace(text.Trim(), "$1");
                    return LeadingSeparator.Replace(text, "$1").Trim();
                }
                return ExtraBlankLines.Replace(text, "\n\n").Trim();
            }
        }
    }
}
