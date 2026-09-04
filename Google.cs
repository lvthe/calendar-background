using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deskcal;

/// <summary>
/// client_id / client_secret cua OAuth client. Doc tu file, KHONG nhung vao code:
/// repo nay co the public, va secret nam trong git thi coi nhu lo vinh vien.
/// File nam o %LOCALAPPDATA%\deskcal\google.json, da cho vao .gitignore.
///
/// Voi app desktop thi "secret" nay khong thuc su bi mat duoc — ai cung giai nen exe ra
/// doc duoc. Google biet dieu do; thu bao ve luong dang nhap la PKCE, khong phai secret.
/// </summary>
public sealed record GoogleConfig(string ClientId, string ClientSecret)
{
    public static string Path_ => System.IO.Path.Combine(Paths.Dir, "google.json");

    public static GoogleConfig? Load()
    {
        try
        {
            if (!File.Exists(Path_)) return null;
            return Parse(File.ReadAllText(Path_));
        }
        catch (Exception e) { Log.Write($"google: doc google.json loi: {e.Message}"); return null; }
    }

    /// <summary>
    /// Nhan ca hai dang: file Google cho tai ve nguyen xi ({"installed":{...}}) va dang
    /// phang tu go tay ({"clientId":...}). Nhan luon dang nguyen xi de nguoi dung khoi
    /// phai sua tay — sua tay la co cho sai.
    /// </summary>
    public static GoogleConfig? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // {"web":{...}} la client loai Web application — sai loai, luong loopback se bi
        // Google tu choi. Bao ro o day chu de no chet luc dang nhap thi rat kho doan.
        if (root.TryGetProperty("web", out _))
        {
            Log.Write("google: google.json la client loai 'Web application'. " +
                      "Phai tao lai loai 'Desktop app'.");
            return null;
        }

        var src = root.TryGetProperty("installed", out var ins) ? ins : root;

        string? id = Str(src, "client_id") ?? Str(src, "clientId");
        string? secret = Str(src, "client_secret") ?? Str(src, "clientSecret");
        if (id is not { Length: > 0 } || secret is not { Length: > 0 })
        {
            Log.Write("google: google.json thieu client_id hoac client_secret.");
            return null;
        }
        return new GoogleConfig(id, secret);
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Token da lay ve. Luu canh DB, chi nguoi dung hien tai doc duoc.</summary>
public sealed class GoogleToken
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_at")] public DateTime ExpiresAt { get; set; }

    /// <summary>Coi nhu het han som 2 phut, khoi dinh ca truong hop dang goi thi het.</summary>
    public bool Stale => DateTime.UtcNow >= ExpiresAt.AddMinutes(-2);

    public static string Path_ => System.IO.Path.Combine(Paths.Dir, "google-token.json");

    public static GoogleToken? Load()
    {
        try
        {
            if (!File.Exists(Path_)) return null;
            return JsonSerializer.Deserialize<GoogleToken>(File.ReadAllText(Path_));
        }
        catch (Exception e) { Log.Write($"google: doc token loi: {e.Message}"); return null; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Dir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(this));
        }
        catch (Exception e) { Log.Write($"google: ghi token loi: {e.Message}"); }
    }

    public static void Forget()
    {
        try { if (File.Exists(Path_)) File.Delete(Path_); }
        catch (Exception e) { Log.Write($"google: xoa token loi: {e.Message}"); }
    }
}

/// <summary>
/// OAuth 2.0 cho app desktop: mo trinh duyet cho nguoi dung dong y, nhan ma tra ve qua
/// mot HTTP listener tam tren loopback, doi lay token.
///
/// CLAUDE.md ghi "khong server, khong browser, khong localhost". Day la ngoai le co y,
/// va la ngoai le DUY NHAT: Google khong cho cach nao khac de app desktop lay token cho
/// tai khoan ca nhan. Listener chi song vai giay luc dang nhap roi tat, bind vao
/// 127.0.0.1 nen khong lo ra mang.
/// </summary>
public static class GoogleAuth
{
    const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// calendar.events de doc/ghi su kien, calendar.readonly de liet ke danh sach lich.
    /// Khong xin scope 'calendar' day du vi no cho ca quyen xoa nguyen mot lich.
    /// </summary>
    public const string Scope =
        "https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.readonly";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // ---------- PKCE ----------

    /// <summary>Chuoi ngau nhien 43-128 ky tu, theo RFC 7636.</summary>
    public static string NewVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    /// <summary>challenge = BASE64URL(SHA256(verifier)) — phuong thuc S256.</summary>
    public static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Base64 kieu URL: khong padding, +/ doi thanh -_.</summary>
    public static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---------- dung URL ----------

    public static string AuthUrl(string clientId, string redirect, string challenge, string state)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirect,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            // offline + consent: bat buoc de Google tra ve refresh_token. Thieu
            // prompt=consent thi lan dang nhap thu hai tro di KHONG co refresh_token,
            // va app chet sau mot tieng ma khong hieu tai sao.
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };
        return AuthEndpoint + "?" + string.Join("&",
            q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    // ---------- dang nhap ----------

    /// <summary>
    /// Mo trinh duyet, doi nguoi dung dong y, tra ve token. Nem neu nguoi dung tu choi
    /// hoac qua thoi gian cho.
    /// </summary>
    public static async Task<GoogleToken> SignInAsync(GoogleConfig cfg, CancellationToken ct = default)
    {
        int port = FreePort();
        string redirect = $"http://127.0.0.1:{port}/";
        string verifier = NewVerifier();
        string state = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();

        var url = AuthUrl(cfg.ClientId, redirect, Challenge(verifier), state);
        Log.Write($"google: mo trinh duyet de dang nhap, loopback :{port}");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(3), ct);
        var q = ctx.Request.QueryString;
        string? code = q["code"], gotState = q["state"], err = q["error"];

        await Reply(ctx, err is null && code is not null
            ? "<h2>Xong</h2><p>Quay lai deskcal được rồi. Đóng tab này.</p>"
            : $"<h2>Không xong</h2><p>{WebUtility.HtmlEncode(err ?? "thiếu mã")}</p>");
        listener.Stop();

        if (err is not null) throw new InvalidOperationException($"Google trả về lỗi: {err}");
        if (code is null) throw new InvalidOperationException("Google không trả về mã.");
        // chong CSRF: ma tra ve phai kem dung state minh gui di
        if (gotState != state) throw new InvalidOperationException("state khong khop.");

        return await ExchangeAsync(cfg, code, verifier, redirect, ct);
    }

    static async Task Reply(HttpListenerContext ctx, string html)
    {
        var body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><body style=\"font-family:system-ui;padding:3rem\">{html}");
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = body.Length;
        await ctx.Response.OutputStream.WriteAsync(body);
        ctx.Response.Close();
    }

    /// <summary>Hoi he dieu hanh mot cong con trong, roi tra lai ngay.</summary>
    static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    // ---------- doi / lam moi token ----------

    static async Task<GoogleToken> ExchangeAsync(GoogleConfig cfg, string code, string verifier,
                                                 string redirect, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = cfg.ClientId,
            ["client_secret"] = cfg.ClientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirect,
        };
        return await PostAsync(form, keepRefresh: null, ct);
    }

    /// <summary>Access token song 1 tieng; refresh token doi lay cai moi khong can nguoi dung.</summary>
    public static async Task<GoogleToken> RefreshAsync(GoogleConfig cfg, GoogleToken old,
                                                       CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = cfg.ClientId,
            ["client_secret"] = cfg.ClientSecret,
            ["refresh_token"] = old.RefreshToken,
            ["grant_type"] = "refresh_token",
        };
        // Lan refresh Google KHONG gui lai refresh_token — giu lai cai cu, khong thi
        // lan sau mat token va phai bat nguoi dung dang nhap lai.
        return await PostAsync(form, keepRefresh: old.RefreshToken, ct);
    }

    static async Task<GoogleToken> PostAsync(Dictionary<string, string> form, string? keepRefresh,
                                             CancellationToken ct)
    {
        using var res = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google tu choi ({(int)res.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new GoogleToken
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? "",
            RefreshToken = root.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString() ?? keepRefresh ?? ""
                : keepRefresh ?? "",
            ExpiresAt = DateTime.UtcNow.AddSeconds(
                root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600),
        };
    }
}
