using System.Text.Json;
using System.Text.Json.Nodes;
using ConAI.Web.Configuration;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Gemini;

public sealed class GoogleGeminiContentClient : IGeminiContentClient
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly GeminiOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GoogleGeminiContentClient> _logger;

    public GoogleGeminiContentClient(
        IOptions<GeminiOptions> options,
        TimeProvider timeProvider,
        ILogger<GoogleGeminiContentClient> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        var client = new Client(apiKey: _options.ApiKey);
        var uploaded = new List<string>();

        try
        {
            var parts = new List<Part> { Part.FromText(request.Prompt) };
            var totalBytes = request.Files.Sum(file => (long)file.Bytes.Length);

            foreach (var file in request.Files)
            {
                if (totalBytes <= _options.InlineLimitBytes)
                {
                    parts.Add(Part.FromBytes(file.Bytes, file.MimeType));
                    continue;
                }

                var remote = await UploadAndWaitAsync(client, file, cancellationToken);
                uploaded.Add(remote.Name!);
                parts.Add(Part.FromUri(remote.Uri!, remote.MimeType!));
            }

            var config = new GenerateContentConfig
            {
                SystemInstruction = new Content { Parts = [Part.FromText(request.SystemInstruction)] },
                ResponseMimeType = "application/json",
                ResponseJsonSchema = BuildSchema(request.IncludeTranscription, request.IncludeTranslatedTranscription),
                Temperature = 0.2f,
                ThinkingConfig = ThinkingLevels.Build(_options.GenerateThinkingLevel)
            };

            var response = await client.Models.GenerateContentAsync(
                request.Model,
                [new Content { Role = "user", Parts = parts }],
                config,
                cancellationToken);

            var json = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("Gemini から本文が返りませんでした。");

            var payload = JsonSerializer.Deserialize<GenerationPayload>(json, PayloadOptions)
                ?? throw new InvalidOperationException("Gemini の応答を解釈できませんでした。");

            return new GenerationResult(
                request.IncludeTranscription ? payload.Transcription ?? string.Empty : string.Empty,
                payload.Minutes ?? string.Empty,
                request.IncludeTranslatedTranscription ? payload.TranslatedTranscription : null);
        }
        finally
        {
            foreach (var name in uploaded)
            {
                try
                {
                    await client.Files.DeleteAsync(name, cancellationToken: cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Failed to delete uploaded file {Name}", name);
                }
            }
        }
    }

    private async Task<Google.GenAI.Types.File> UploadAndWaitAsync(
        Client client,
        GeminiFilePart file,
        CancellationToken cancellationToken)
    {
        var uploaded = await client.Files.UploadAsync(
            file.Bytes,
            file.FileName,
            new UploadFileConfig { MimeType = file.MimeType, DisplayName = file.FileName },
            cancellationToken);

        var deadline = _timeProvider.GetUtcNow().AddMinutes(10);

        while (uploaded.State == FileState.Processing)
        {
            if (_timeProvider.GetUtcNow() > deadline)
            {
                throw new TimeoutException($"{file.FileName} の処理が 10 分以内に終わりませんでした。");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), _timeProvider, cancellationToken);
            uploaded = await client.Files.GetAsync(uploaded.Name!, cancellationToken: cancellationToken);
        }

        if (uploaded.State != FileState.Active)
        {
            throw new InvalidOperationException($"{file.FileName} をアップロードできませんでした（状態: {uploaded.State}）。");
        }

        return uploaded;
    }

    private static JsonObject BuildSchema(bool includeTranscription, bool includeTranslatedTranscription)
    {
        var properties = new JsonObject
        {
            ["minutes"] = new JsonObject { ["type"] = "string" }
        };

        var required = new JsonArray("minutes");

        if (includeTranscription)
        {
            properties["transcription"] = new JsonObject { ["type"] = "string" };
            required.Add("transcription");
        }

        if (includeTranslatedTranscription)
        {
            properties["translatedTranscription"] = new JsonObject { ["type"] = "string" };
            required.Add("translatedTranscription");
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
    }

    public async Task<SelectionResult> SelectAsync(SelectionRequest request, CancellationToken cancellationToken)
    {
        var client = new Client(apiKey: _options.ApiKey);

        var config = new GenerateContentConfig
        {
            SystemInstruction = new Content { Parts = [Part.FromText(request.SystemInstruction)] },
            ResponseMimeType = "application/json",
            ResponseJsonSchema = BuildSelectionSchema(),
            // 選抜は毎回同じ答えに寄せたい。揺らぐと同じ質問で根拠が変わる。
            Temperature = 0.2f,
            ThinkingConfig = ThinkingLevels.Build(_options.SelectThinkingLevel)
        };

        var response = await client.Models.GenerateContentAsync(
            request.Model,
            [new Content { Role = "user", Parts = [Part.FromText(request.Prompt)] }],
            config,
            cancellationToken);

        var json = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini から本文が返りませんでした。");

        var payload = JsonSerializer.Deserialize<SelectionPayload>(json, PayloadOptions)
            ?? throw new InvalidOperationException("Gemini の応答を解釈できませんでした。");

        return new SelectionResult(payload.MeetingNumbers ?? []);
    }

    public async Task<ChatResult> AskAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var client = new Client(apiKey: _options.ApiKey);

        var config = new GenerateContentConfig
        {
            SystemInstruction = new Content { Parts = [Part.FromText(request.SystemInstruction)] },
            ResponseMimeType = "application/json",
            ResponseJsonSchema = BuildAnswerSchema(),
            Temperature = 0.2f,
            ThinkingConfig = ThinkingLevels.Build(_options.ChatThinkingLevel)
        };

        var response = await client.Models.GenerateContentAsync(
            request.Model,
            [new Content { Role = "user", Parts = [Part.FromText(request.Prompt)] }],
            config,
            cancellationToken);

        var json = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini から本文が返りませんでした。");

        var payload = JsonSerializer.Deserialize<ChatPayload>(json, PayloadOptions)
            ?? throw new InvalidOperationException("Gemini の応答を解釈できませんでした。");

        return new ChatResult(payload.Answer ?? string.Empty, payload.UsedMeetingNumbers ?? []);
    }

    private static JsonObject BuildSelectionSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["meetingNumbers"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "integer" }
            }
        },
        ["required"] = new JsonArray("meetingNumbers")
    };

    private static JsonObject BuildAnswerSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["answer"] = new JsonObject { ["type"] = "string" },
            ["usedMeetingNumbers"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "integer" }
            }
        },
        ["required"] = new JsonArray("answer", "usedMeetingNumbers")
    };

    private sealed record GenerationPayload(string? Transcription, string? Minutes, string? TranslatedTranscription);

    private sealed record ChatPayload(string? Answer, int[]? UsedMeetingNumbers);

    private sealed record SelectionPayload(int[]? MeetingNumbers);
}
