using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;

/// <summary>Renders a Markdown string to the terminal using Spectre.Console.</summary>
static class MarkdownRenderer
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

    public static void Write(string markdown)
    {
        foreach (var block in Markdown.Parse(markdown, Pipeline))
            RenderBlock(block, 0);
    }

    static void RenderBlock(Block block, int indent)
    {
        switch (block)
        {
            case HeadingBlock h:
                AnsiConsole.MarkupLine(h.Level <= 2
                    ? $"[bold underline yellow]{Inlines(h.Inline)}[/]"
                    : $"[bold yellow]{Inlines(h.Inline)}[/]");
                AnsiConsole.WriteLine();
                break;

            case ParagraphBlock p:
                AnsiConsole.MarkupLine($"{Pad(indent)}{Inlines(p.Inline)}");
                if (indent == 0) AnsiConsole.WriteLine();
                break;

            case ListBlock list:
                var n = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var bullet = list.IsOrdered ? $"{n++}." : "•";
                    var first = true;
                    foreach (var child in item)
                    {
                        if (first && child is ParagraphBlock p2)
                            AnsiConsole.MarkupLine($"{Pad(indent)}[blue]{bullet}[/] {Inlines(p2.Inline)}");
                        else
                            RenderBlock(child, indent + 1);
                        first = false;
                    }
                }
                if (indent == 0) AnsiConsole.WriteLine();
                break;

            case MdTable table:
                RenderTable(table);
                break;

            case FencedCodeBlock or CodeBlock:
                var leaf = (LeafBlock)block;
                var code = string.Join("\n", leaf.Lines.Lines.Take(leaf.Lines.Count).Select(l => l.ToString()));
                AnsiConsole.Write(new Panel(new Text(code)).Border(BoxBorder.Rounded).BorderColor(Color.Grey));
                AnsiConsole.WriteLine();
                break;

            case QuoteBlock q:
                foreach (var child in q) RenderBlock(child, indent + 1);
                break;

            case ThematicBreakBlock:
                AnsiConsole.Write(new Rule().RuleStyle("grey"));
                break;

            case ContainerBlock c:
                foreach (var child in c) RenderBlock(child, indent);
                break;
        }
    }

    static string Pad(int indent) => new(' ', indent * 2);

    // Spectre's Table assumes emoji are 2 columns wide; some terminals draw them 1 wide, which breaks the borders.
    // So tables are drawn here with the width the terminal really uses (measured once, default 2).
    static readonly Lazy<int> EmojiWidth = new(MeasureEmojiWidth);

    static int MeasureEmojiWidth()
    {
        try
        {
            if (Console.IsOutputRedirected) return 2;
            var (before, row) = Console.GetCursorPosition();
            Console.Write(char.ConvertFromUtf32(0x1F947));
            var (after, rowAfter) = Console.GetCursorPosition();
            Console.Write((char)13 + "    " + (char)13);
            return rowAfter == row && after - before == 1 ? 1 : 2;
        }
        catch
        {
            return 2;
        }
    }

    static int Width(string text)
    {
        var width = 0;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var el = (string)e.Current;
            var cp = char.ConvertToUtf32(el, 0);
            width += cp >= 0x1F000 || (cp is >= 0x2600 and <= 0x27BF) ? EmojiWidth.Value : 1;
        }
        return width;
    }

    static string Plain(ContainerInline? container)
    {
        var sb = new StringBuilder();
        if (container is null) return "";
        foreach (var inline in container.Descendants())
        {
            if (inline is LiteralInline lit) sb.Append(lit.Content.ToString());
            else if (inline is CodeInline code) sb.Append(code.Content);
        }
        return sb.ToString();
    }

    static void RenderTable(MdTable table)
    {
        var rows = table.OfType<MdTableRow>()
            .Select(r => r.OfType<MdTableCell>()
                .Select(c => string.Join(" ", c.OfType<ParagraphBlock>().Select(p => Plain(p.Inline))))
                .ToArray())
            .ToList();
        var cols = rows.Max(r => r.Length);
        var widths = Enumerable.Range(0, cols)
            .Select(i => rows.Max(r => i < r.Length ? Width(r[i]) : 0))
            .ToArray();

        string Line(string left, string mid, string right) =>
            left + string.Join(mid, widths.Select(w => new string('─', w + 2))) + right;

        string Row(string[] cells, bool header) =>
            "│" + string.Join("│", widths.Select((w, i) =>
            {
                var text = i < cells.Length ? cells[i] : "";
                var cell = " " + Markup.Escape(text) + new string(' ', w - Width(text)) + " ";
                return header ? $"[bold]{cell}[/]" : cell;
            })) + "│";

        AnsiConsole.MarkupLine(Line("╭", "┬", "╮"));
        AnsiConsole.MarkupLine(Row(rows[0], true));
        AnsiConsole.MarkupLine(Line("├", "┼", "┤"));
        foreach (var row in rows.Skip(1)) AnsiConsole.MarkupLine(Row(row, false));
        AnsiConsole.MarkupLine(Line("╰", "┴", "╯"));
        AnsiConsole.WriteLine();
    }

    static string Inlines(ContainerInline? container)
    {
        var sb = new StringBuilder();
        if (container is null) return "";
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    sb.Append(Markup.Escape(lit.Content.ToString()));
                    break;
                case EmphasisInline em:
                    var style = em.DelimiterCount >= 2 ? "bold" : "italic";
                    sb.Append($"[{style}]{Inlines(em)}[/]");
                    break;
                case CodeInline code:
                    sb.Append($"[green]{Markup.Escape(code.Content)}[/]");
                    break;
                case LineBreakInline:
                    sb.Append('\n');
                    break;
                case LinkInline link:
                    sb.Append($"[blue underline]{Inlines(link)}[/]");
                    break;
                case ContainerInline c:
                    sb.Append(Inlines(c));
                    break;
            }
        }
        return sb.ToString();
    }
}
