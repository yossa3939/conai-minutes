using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ConAI.Web;

/// <summary>画面から届く日時の文字列を読む。
/// 入力欄は flatpickr が yyyy/MM/dd HH:mm:ss で埋めるが、手で打つこともできるため、
/// ゼロ埋めなしと秒なしも受け付ける。ブラウザやサーバのカルチャには依存させない。</summary>
public static class DateTimeInput
{
    private static readonly string[] Formats = ["yyyy/M/d H:mm:ss", "yyyy/M/d H:mm"];

    public static bool TryParse(string? text, out DateTime value) =>
        DateTime.TryParseExact(text?.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    public static string FormatError(string displayName) =>
        $"{displayName}は {DisplayFormats.HeldAt} の形式で入力してください。";
}

/// <summary>DateTime / DateTime? のフォーム入力を <see cref="DateTimeInput"/> の書式で結び付ける。
/// 既定の結び付けはリクエストのカルチャで解釈するため、環境によって通る書式が変わる。</summary>
public sealed class DateTimeInputModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.UnderlyingOrModelType == typeof(DateTime) ? new DateTimeInputModelBinder() : null;
}

public sealed class DateTimeInputModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var text = result.FirstValue;

        if (string.IsNullOrWhiteSpace(text))
        {
            if (bindingContext.ModelMetadata.IsReferenceOrNullableType)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            else
            {
                bindingContext.ModelState.TryAddModelError(
                    bindingContext.ModelName,
                    bindingContext.ModelMetadata.ModelBindingMessageProvider.ValueMustNotBeNullAccessor(result.ToString()));
            }

            return Task.CompletedTask;
        }

        if (DateTimeInput.TryParse(text, out var value))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        else
        {
            var displayName = bindingContext.ModelMetadata.DisplayName ?? bindingContext.ModelMetadata.Name ?? bindingContext.ModelName;
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, DateTimeInput.FormatError(displayName));
        }

        return Task.CompletedTask;
    }
}
