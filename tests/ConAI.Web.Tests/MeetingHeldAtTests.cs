using System.Net;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

/// <summary>開催日時は秒まで扱う。表示は 3 画面とも同じ形式。
/// 入力欄はブラウザの datetime-local を使わず（日本語 Windows の Chrome で「()」が混ざる）、
/// flatpickr を付けた文字入力で、値も表示と同じ yyyy/MM/dd HH:mm:ss にする。</summary>
public class MeetingHeldAtTests : IClassFixture<ConAIWebApplicationFactory>
{
    private const string FormatError = "開催日時は yyyy/MM/dd HH:mm:ss の形式で入力してください。";

    private readonly ConAIWebApplicationFactory _factory;

    public MeetingHeldAtTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Meeting> CreateAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        return await service.CreateAsync(ownerId, new Meeting
        {
            Title = "日時の確認",
            HeldAt = new DateTime(2026, 8, 28, 9, 5, 7, DateTimeKind.Unspecified)
        }, CancellationToken.None);
    }

    private async Task<Meeting?> LoadAsync(Guid id, string ownerId)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        return await service.GetAsync(id, ownerId, CancellationToken.None);
    }

    private static Guid IdFromEditLocation(HttpResponseMessage response)
    {
        var location = response.Headers.Location!.ToString();
        return Guid.Parse(location["/Meetings/Edit/".Length..]);
    }

    [Theory]
    [InlineData("/Meetings")]
    [InlineData("/Meetings/Details/{0}")]
    [InlineData("/Meetings/Delete/{0}")]
    public async Task 開催日時を秒まで表示する(string pathTemplate)
    {
        var owner = "heldat-" + pathTemplate.Length;
        var meeting = await CreateAsync(owner);
        var client = _factory.CreateClientAs(owner);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(string.Format(pathTemplate, meeting.Id)));

        Assert.Contains("2026/08/28 09:05:07", html);
    }

    [Fact]
    public async Task 作成画面の開催日時は書式付きの文字入力になり_flatpickrを読み込む()
    {
        var client = _factory.CreateClientAs("heldat-create");

        var html = await client.GetStringAsync("/Meetings/Create");

        Assert.Contains("data-conai-datetime", html);
        Assert.Contains("placeholder=\"yyyy/MM/dd HH:mm:ss\"", html);
        Assert.DoesNotContain("datetime-local", html);
        // asp-append-version により /lib/flatpickr/dist/flatpickr.min.<hash>.css の形で出力される
        Assert.Matches("""href="/lib/flatpickr/dist/flatpickr\.min\.[a-z0-9]+\.css""", html);
        Assert.Matches("""src="/lib/flatpickr/dist/flatpickr\.min\.[a-z0-9]+\.js""", html);
        Assert.Matches("""src="/lib/flatpickr/dist/l10n/ja\.[a-z0-9]+\.js""", html);
        Assert.Matches("""src="/js/held-at-picker\.[a-z0-9]+\.js""", html);
    }

    [Fact]
    public async Task 編集画面の開催日時は保存済みの値を表示と同じ書式で入れる()
    {
        var meeting = await CreateAsync("heldat-edit");
        var client = _factory.CreateClientAs("heldat-edit");

        var html = await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}");

        Assert.Contains("data-conai-datetime", html);
        Assert.Contains("value=\"2026/08/28 09:05:07\"", html);
        Assert.DoesNotContain("datetime-local", html);
        Assert.Matches("""href="/lib/flatpickr/dist/flatpickr\.min\.[a-z0-9]+\.css""", html);
        Assert.Matches("""src="/js/held-at-picker\.[a-z0-9]+\.js""", html);
    }

    [Theory]
    [InlineData("2026/08/28 09:05:07", 9, 5, 7)]
    [InlineData("2026/8/28 9:05", 9, 5, 0)]
    public async Task 作成POSTの開催日時を書式どおりに保存する(string heldAt, int hour, int minute, int second)
    {
        var owner = "heldat-post-" + second;
        var client = _factory.CreateClientAs(owner);
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Meetings/Create"));

        var response = await client.PostAsync("/Meetings/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "日時つき",
            ["Input.HeldAt"] = heldAt,
            ["Input.TargetLanguage"] = "ja",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var loaded = await LoadAsync(IdFromEditLocation(response), owner);
        Assert.Equal(new DateTime(2026, 8, 28, hour, minute, second), loaded!.HeldAt);
        Assert.Equal(DateTimeKind.Unspecified, loaded.HeldAt!.Value.Kind);
    }

    [Fact]
    public async Task 作成POSTの開催日時が空なら未設定のまま保存する()
    {
        var client = _factory.CreateClientAs("heldat-empty");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Meetings/Create"));

        var response = await client.PostAsync("/Meetings/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "日時なし",
            ["Input.HeldAt"] = "",
            ["Input.TargetLanguage"] = "ja",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var loaded = await LoadAsync(IdFromEditLocation(response), "heldat-empty");
        Assert.Null(loaded!.HeldAt);
    }

    [Fact]
    public async Task 作成POSTの開催日時が書式に合わなければ作成画面に戻り理由と入力値を示す()
    {
        var client = _factory.CreateClientAs("heldat-invalid");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Meetings/Create"));

        var response = await client.PostAsync("/Meetings/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "日時が変",
            ["Input.HeldAt"] = "2026-08-28 abc",
            ["Input.TargetLanguage"] = "ja",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(FormatError, html);
        Assert.Contains("value=\"2026-08-28 abc\"", html);
    }

    [Fact]
    public async Task 編集POSTで開催日時を書式どおりに保存する()
    {
        var meeting = await CreateAsync("heldat-edit-post");
        var client = _factory.CreateClientAs("heldat-edit-post");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "日時の確認",
                ["Basic.HeldAt"] = "2026/08/28 10:11:12",
                ["Basic.TargetLanguage"] = "ja",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var loaded = await LoadAsync(meeting.Id, "heldat-edit-post");
        Assert.Equal(new DateTime(2026, 8, 28, 10, 11, 12), loaded!.HeldAt);
    }

    [Fact]
    public async Task 編集POSTの開催日時が書式に合わなければ編集画面に戻り理由を示す()
    {
        var meeting = await CreateAsync("heldat-edit-invalid");
        var client = _factory.CreateClientAs("heldat-edit-invalid");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "日時の確認",
                ["Basic.HeldAt"] = "8月28日",
                ["Basic.TargetLanguage"] = "ja",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(FormatError, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));

        var loaded = await LoadAsync(meeting.Id, "heldat-edit-invalid");
        Assert.Equal(new DateTime(2026, 8, 28, 9, 5, 7), loaded!.HeldAt);
    }
}
