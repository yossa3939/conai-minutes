# ConAI

<p align="center">
  <a href="LICENSE"><img alt="License MIT" src="https://img.shields.io/badge/LICENSE-MIT-yellow.svg?style=for-the-badge"></a>
  <a href="https://dotnet.microsoft.com/"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4.svg?style=for-the-badge&logo=dotnet&logoColor=white"></a>
  <a href="https://learn.microsoft.com/aspnet/core/razor-pages/"><img alt="ASP.NET Core" src="https://img.shields.io/badge/ASP.NET_CORE-Razor_Pages-5C2D91.svg?style=for-the-badge"></a>
  <a href="https://www.sqlite.org/"><img alt="SQLite" src="https://img.shields.io/badge/DATABASE-SQLite-003B57.svg?style=for-the-badge&logo=sqlite&logoColor=white"></a>
  <a href="https://tailwindcss.com/"><img alt="Tailwind CSS" src="https://img.shields.io/badge/UI-Tailwind_CSS-06B6D4.svg?style=for-the-badge&logo=tailwindcss&logoColor=white"></a>
  <a href="https://ai.google.dev/"><img alt="Gemini API" src="https://img.shields.io/badge/AI-Gemini_API-4285F4.svg?style=for-the-badge&logo=googlegemini&logoColor=white"></a>
</p>

<p align="center">
  <a href="#できること">できること</a> ·
  <a href="#技術スタック">技術スタック</a> ·
  <a href="#すぐ動かす">すぐ動かす</a> ·
  <a href="#使い方">使い方</a> ·
  <a href="#設定">設定</a> ·
  <a href="#サーバへ配置する">配置</a> ·
  <a href="#ライセンス">ライセンス</a>
</p>

会議の音声から文字起こしと議事録を作る、単独で動く ASP.NET Core アプリケーションです。ブラウザだけで録音でき、SaaS に音声を預けずに自分のサーバの中で完結します。

<p align="center"><em>個人が作っている独立したプロジェクトです。Google および Gemini とは関係がありません。</em></p>

## できること

- **録音しながら文字起こしする。** ブラウザのマイクから録り、話しているそばから文字が画面に増えていきます
- **相手の声も一緒に録る。** タブや画面の音声を混ぜて録音できるので、オンライン会議でも自分と相手の両方が文字になります
- **あとから音声や動画を渡す。** mp3、m4a、mp4、mov など主要な形式を受け付けます。pdf や docx を参考資料として添えると、議事録の材料になります
- **議事録の形を選ぶ。** 見出しの並びをテンプレートで決められます。既定で「標準」と「簡潔」が入り、どちらも自由に書き換えられます
- **会議を横断して探し、質問する。** SQLite の全文検索で会議をまたいで探し、見つかった議事録を根拠に AI が答えます。答えには根拠にした会議名が並びます
- **議事録ができたら通知する。** Slack、Discord、Microsoft Teams、Google Chat の Webhook に送れます
- **翻訳しながら記録する。** 話した言葉と訳した言葉の両方を残せます
- **API キーをブラウザに渡さない。** Gemini API はサーバからだけ呼びます。アップロードしたファイルは webroot の外に GUID 名で置きます

## 技術スタック

| 分類 | 使っているもの |
|---|---|
| フレームワーク | ASP.NET Core 10（Razor Pages ＋ Minimal API） |
| 言語 | C# / JavaScript（ES モジュール） |
| UI | Razor Pages によるサーバ描画 |
| スタイル | Tailwind CSS 4 |
| データベース | SQLite（全文検索は FTS5） |
| ORM | Entity Framework Core 10 |
| 認証 | ASP.NET Core Identity |
| AI | Google Gemini API（Live API と生成 API）／ Google.GenAI |
| 形態素解析 | LibNMeCab ＋ IPAdic（検索語と議事録の分かち書き） |
| Markdown | Markdig |
| Office 文書 | DocumentFormat.OpenXml（参考資料の docx、pptx、xlsx を読む） |
| メール送信 | MailKit |
| 日付入力 | flatpickr |
| フォーム検証 | jQuery Validation（Identity の画面で使う） |
| テスト | xUnit ／ Moq ／ node:test ／ Playwright |

## すぐ動かす

必要なものは .NET 10 SDK、Node.js 20 以上、Gemini API キーの 3 つです。Node は Tailwind CSS のビルドに使うため、無いとビルドが止まります。

```bash
git clone https://github.com/yossa3939/conai-minutes.git
cd conai-minutes
cp src/ConAI.Web/appsettings.json.sample src/ConAI.Web/appsettings.json
dotnet run --project src/ConAI.Web
```

API キーは `appsettings.json` の `Gemini:ApiKey` に書くか、環境変数 `GEMINI_API_KEY` に入れます。開発中はユーザーシークレットも使えます。

```bash
cd src/ConAI.Web
dotnet user-secrets init
dotnet user-secrets set "Gemini:ApiKey" "<APIキー>"
```

初回の起動でマイグレーションが走り、`App_Data` の下に SQLite の DB とアップロード置き場ができます。ブラウザで `/Identity/Account/Register` からアカウントを作り、`/Meetings` で会議を作ってください。

## 使い方

### 録音して文字起こしする

会議を作るときに Live モードを選ぶと、編集画面に録音カードが出ます。録音の入口は 3 つから選べます。

| 選択肢 | 取るもの | 使う場面 |
|---|---|---|
| マイクのみ | マイク | 対面の会議 |
| マイク＋PC 音声 | マイクと共有したタブや画面の音声 | オンライン会議に自分も参加する |
| PC 音声のみ | 共有したタブや画面の音声 | 聞くだけの会議やウェビナー |

PC 音声を含む選択では、録音の開始時にブラウザの共有ダイアログが開きます。音声の共有をオンにしてください。この機能は Chrome と Edge で動きます。Firefox と Safari はタブの音声を返さないため選べません。

マイクと PC 音声は 1 本に混ぜてから送るので、保存される録音にも相手の声が入ります。スピーカーで聞くと相手の声をマイクも拾って二重に文字起こしされるため、ヘッドホンをお勧めします。相手の声を録音することは、会議の参加者に伝えてください。

### ファイルから作る

ファイルモードを選ぶと、添付した音声や動画から生成のときに文字起こしします。添付欄は「音声・動画」と「参考資料」に分かれていて、欄を間違えるとブラウザがその場で弾きます。分割した録音を複数上げたときは、表の「No」が読ませる順序になります。

### 議事録を探す

ナビゲーションの「議事録を探す」から、持っている会議を横断して探せます。入力欄は 1 つで、「探す」は索引を引き、「回答を作る」は AI が答えを作ります。検索が 0 件でも回答は作れて、そのときは AI が全体から根拠を選びます。

会議を編集してから索引に載るまで、最大 20 秒ほどかかります。その間は残り件数が画面に出ます。

## 設定

[`appsettings.json.sample`](src/ConAI.Web/appsettings.json.sample) をコピーして使います。設定できる項目は次のとおりです。

### Gemini

| キー | 既定 | 内容 |
|---|---|---|
| `Gemini:Provider` | `Google` | `Google` / `Fake`。`Fake` はキーなしで起動でき、決まった応答を返す |
| `Gemini:ApiKey` | なし | `Provider=Google` のとき必須。空なら環境変数 `GEMINI_API_KEY` を読む。どちらも無ければ起動時に停止する |
| `Gemini:LiveModel` | なし | **必須**。録音しながらの文字起こしに使う |
| `Gemini:LiveTranslateModel` | なし | **必須**。翻訳モードでの録音に使う |
| `Gemini:GenerateModel` | なし | **必須**。議事録を作る段と、絞り込んだ議事録から答えを書く段に使う |
| `Gemini:SelectModel` | なし | **必須**。質問に関係する議事録を絞り込む段に使う |
| `Gemini:GenerateThinkingLevel` | 空（未指定） | 議事録を作る段の思考量。`MINIMAL` / `LOW` / `MEDIUM` / `HIGH`。空なら送らずモデルに任せる |
| `Gemini:SelectThinkingLevel` | 空（未指定） | 絞り込む段の思考量。値は上と同じ |
| `Gemini:ChatThinkingLevel` | 空（未指定） | 答えを書く段の思考量。値は上と同じ |
| `Gemini:InlineLimitMb` | 20 | この値以下の添付は本文に載せて送り、超えると Files API へ上げる |
| `Gemini:UseSessionResumption` | false | Live セッションを継ぎ足すときに resumption ハンドルを使うか |

### アプリ

| キー | 既定 | 内容 |
|---|---|---|
| `Auth:AllowSelfRegistration` | true | `false` で登録ページを 404 にする |
| `Storage:DataRoot` | `App_Data`（コンテンツルート相対） | SQLite、アップロード、Data Protection キーの置き場 |
| `Upload:MaxFileSizeMb` | 200 | 添付 1 ファイルの上限。上げるときは IIS の `maxAllowedContentLength` も同じ値に揃える |
| `Upload:MaxExtractedTextChars` | 2000000 | 参考資料から取り出すテキストの上限。zip 爆弾で展開結果が膨らむのを止める |
| `Live:AllowedOrigins` | 空（自ホストのみ） | WebSocket の Origin 許可リスト。逆プロキシの後ろに置くときは外向きのオリジンを入れる |
| `Live:MaxSessionsPerUser` | 1 | 1 人が同時に張れる Live セッションの数 |
| `AllowedHosts` | `localhost;127.0.0.1` | 受け付ける Host ヘッダ。セミコロン区切り。**本番では公開するホスト名にする。** `*` のままだと、偽の Host ヘッダでパスワード再設定リンクの宛先を書き換えられる |

### 通知（Webhook）

| キー | 既定 | 内容 |
|---|---|---|
| `Notifications:Provider` | `Http` | `Http` / `Fake`。`Fake` は送信せず記録だけ残す |
| `Notifications:PublicBaseUrl` | 空 | 通知に載せるリンクの基点。空ならリンクを入れない |
| `Notifications:ExcerptChars` | 1500 | 通知に載せる議事録冒頭の文字数。0 で抜粋なし |
| `Notifications:MaxRetries` | 3 | 一時的な失敗を再送する回数 |
| `Notifications:TimeoutSeconds` | 10 | 1 回の送信の待ち時間 |
| `Notifications:MaxEndpointsPerUser` | 10 | 1 人が登録できる宛先の数 |
| `Notifications:MaxConcurrentMeetings` | 4 | 送信ワーカーが同時に扱う会議の数。1 件が再送を繰り返しても後ろの通知を止めないための幅 |

### メール（パスワード再設定）

| キー | 既定 | 内容 |
|---|---|---|
| `Smtp:Host` | 空 | SMTP サーバのホスト名。`Smtp:FromAddress` とともに空ならメールを送らない |
| `Smtp:Port` | 587 | SMTP サーバのポート番号 |
| `Smtp:Security` | `StartTls` | `StartTls` / `SslOnConnect` / `None`。`None` は再設定リンクが平文で流れるため閉域網限定 |
| `Smtp:UserName` | 空 | 認証に使うユーザー名。空なら認証なしで繋ぐ |
| `Smtp:FromAddress` | 空 | 差出人のメールアドレス。`Smtp:Host` とともに空ならメールを送らない |
| `Smtp:FromName` | `議事録` | 差出人の表示名 |
| `Smtp:TimeoutSeconds` | 30 | 1 回の送信の待ち時間 |

`Smtp:Host` と `Smtp:FromAddress` が両方空なら、メールでの再設定を使わない状態でアプリは動きます。どちらか一方だけを設定して起動すると、その場で検出して停止します。パスワードの欄は見本にありません（欄があると平文で書かれてしまうため）。ユーザーシークレットか環境変数 `Smtp__Password` で渡してください。

見本にはこのほかに `Logging` の節がありますが、これは ASP.NET Core 標準のログ設定で、ConAI が足した項目ではありません。

モデル名の 4 つに既定値はありません。使う版は見本にだけ書いてあるので、`appsettings.json.sample` をコピーすればそのまま入ります。未設定のまま起動しようとすると、その場で検出して停止します。

`SelectModel` と `GenerateModel` が分かれているのは、「議事録を探す」の回答が 2 段構えだからです。まず `SelectModel` が会議の山から質問に関係しそうな議事録を絞り込み、次に `GenerateModel` が絞り込まれた議事録を読んで答えを書きます。前段は候補を選ぶだけなので軽いモデルで足り、後段は文章の質が要ります。キーを分けておくと、この振り分けをコードに触らずに変えられます。同じ値を入れておいても構いません。

答えを書く段はモデルこそ `GenerateModel` と共用ですが、思考量だけは `ChatThinkingLevel` で別に振れます。

環境変数で与えるときは、階層の区切りを `__`（アンダースコア 2 つ）にします（`Gemini__ApiKey`、`Storage__DataRoot` など）。パスワードと API キーは `appsettings.json` に書かず、ユーザーシークレットか環境変数で渡してください。

ログインできなくなったときは、Web サーバを立てずに復旧できます。

```bash
dotnet run --project src/ConAI.Web -- list-users
dotnet run --project src/ConAI.Web -- reset-password user@example.com
```

## サーバへ配置する

ASP.NET Core が動く環境なら `dotnet publish` の出力をそのまま置けます。Windows の IIS へ置く場合は、次の 4 点を押さえてください。

- **WebSocket Protocol を有効にする。** 無効のままだと録音中の接続がハンドシェイクで失敗します
- **このアプリ専用のアプリケーションプールを作る。** 他のアプリと共有すると、リサイクルの巻き添えで録音中のセッションが切れます
- **`Storage:DataRoot` を発行フォルダの外に向ける。** 中に置くと、上書き配置で会議も添付も消えます。アプリケーションプールの ID にそのフォルダの読み書き権限を与えてください
- **上書きは `app_offline.htm` を置いて止めてから行う。** 置いたファイルを消すと再開します

`appsettings.json` は発行フォルダに置いたままにして、配置の対象から外してください。`AllowedHosts` を実際のホスト名にすることと、`Upload:MaxFileSizeMb` を上げるときに IIS 側の `maxAllowedContentLength` も揃えることも忘れずに。

## 使用しているオープンソース

分かち書きに使う LibNMeCab と IPAdic 辞書は `GPL-2.0-or-later OR LGPL-2.1-or-later` の選択制で配布されています。ConAI は **LGPL-2.1** を選択します。発行物は DLL が分離したフレームワーク依存の形なので、利用者はライブラリを差し替えて再リンクできます。ライセンスの全文は辞書パッケージ同梱の `COPYING` にあります。

そのほかの依存はすべて MIT、Apache-2.0、BSD のいずれかです。同梱している jQuery、jQuery Validation、flatpickr は、各フォルダに LICENSE を置いています。

## ライセンス

[MIT](LICENSE)
