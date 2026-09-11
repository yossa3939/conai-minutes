namespace ConAI.Web.Services;

public interface IMarkdownRenderer
{
    string ToHtml(string markdown);

    /// <summary>Markdown の記号を落とした平文。txt でのダウンロードに使う。</summary>
    string ToPlainText(string markdown);
}
