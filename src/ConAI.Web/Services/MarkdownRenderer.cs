using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ConAI.Web.Services;

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    public string ToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        // Markdig はサニタイザを持たず、リンクの URL スキームも検証しない。
        // [x](javascript:...) と <javascript:...> が <a href="javascript:..."> として出てしまうため、
        // 両方の書式を担う 2 つの描画器（LinkInlineRenderer と AutolinkInlineRenderer）を、
        // スキームを見るものに置き換える。片方だけだと、もう一方の書式から素通りする。
        renderer.ObjectRenderers.Replace<LinkInlineRenderer>(new SafeLinkInlineRenderer());
        renderer.ObjectRenderers.Replace<AutolinkInlineRenderer>(new SafeAutolinkInlineRenderer());
        renderer.Render(Markdown.Parse(markdown, Pipeline));
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>
    /// 見出し・段落・箇条書きは 1 行ずつ、表は 1 行 1 レコードでセルをタブ区切りにする。
    /// Markdig 標準の ToPlainText は表のセルと後続の段落を 1 行に連結してしまうため、表だけ独自に描画する。
    /// </summary>
    public string ToPlainText(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer)
        {
            EnableHtmlForBlock = false,
            EnableHtmlForInline = false,
            EnableHtmlEscape = false
        };
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<HtmlTableRenderer>(new PlainTextTableRenderer());
        renderer.Render(Markdown.Parse(markdown, Pipeline));
        writer.Flush();
        return writer.ToString();
    }

    private sealed class PlainTextTableRenderer : HtmlObjectRenderer<Table>
    {
        protected override void Write(HtmlRenderer renderer, Table table)
        {
            renderer.EnsureLine();
            foreach (var rowObject in table)
            {
                var first = true;
                foreach (var cellObject in (TableRow)rowObject)
                {
                    if (!first)
                    {
                        renderer.Write('\t');
                    }

                    first = false;
                    foreach (var block in (TableCell)cellObject)
                    {
                        if (block is LeafBlock leaf)
                        {
                            renderer.WriteLeafInline(leaf);
                        }
                        else
                        {
                            renderer.Write(block);
                        }
                    }
                }

                renderer.WriteLine();
            }
        }
    }

    /// <summary>
    /// href と src に出してよいのは http、https、mailto と相対 URL だけ。
    /// それ以外のスキーム（javascript: や data: など）のリンクと画像は要素を出さず、
    /// ラベル（画像なら代替文字列）を本文の文字として残す。
    /// CSP とは独立した第二の防壁であり、議事録と答えの両方の経路を守る。
    /// </summary>
    private sealed class SafeLinkInlineRenderer : LinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, LinkInline link)
        {
            if (IsAllowedUrl(link.Url))
            {
                base.Write(renderer, link);
                return;
            }

            renderer.WriteChildren(link);
        }
    }

    /// <summary>
    /// 山括弧オートリンク（&lt;scheme:...&gt;）を担う SafeLinkInlineRenderer の counterpart。
    /// こちらも同じ許可リストでスキームを検査し、弾いた URL は要素にせず文字として残す。
    /// </summary>
    private sealed class SafeAutolinkInlineRenderer : AutolinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, AutolinkInline link)
        {
            // メールのオートリンクは Url にスキームが付かない生のアドレスで入り、
            // mailto: はこの描画器が前置する。素で判定に渡すと相対 URL 扱いで通ってしまい、
            // 結果は同じでも判定の理由が誤るため、先に見て明示的に許可する。
            if (link.IsEmail || IsAllowedUrl(link.Url))
            {
                base.Write(renderer, link);
                return;
            }

            // オートリンクにラベルは無く、表示文字列は URL 自体である。
            renderer.WriteEscape(link.Url);
        }
    }

    private static bool IsAllowedUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return true;
        }

        // ブラウザは URL 内の ASCII の空白と制御文字を取り除いてからスキームを決める。
        // スキーム名の途中に挟まった java&#9;script: のような宛先は、
        // 取り除かないと絶対 URI として読めず相対 URL と誤判定されるため、
        // 判定の前に U+0020 以下を取り除いてから Uri に渡す。出力には元の文字列を使う。
        var schemeCandidate = new string(url.Where(c => c > ' ').ToArray());

        // 絶対 URI として読めるものは、許可したスキームのものだけを通す。
        // 絶対 URI として読めないものは相対 URL でありスキームを持たないので、そのまま通す。
        return !Uri.TryCreate(schemeCandidate, UriKind.Absolute, out var uri)
            || uri.Scheme is "http" or "https" or "mailto";
    }
}
