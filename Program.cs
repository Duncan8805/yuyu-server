using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Yuyu.Api;

var builder = WebApplication.CreateBuilder(args);

// 雲端平台（如 Koyeb）會用 PORT 環境變數指定容器要監聽的埠號
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ── 資料庫：Db:Provider 切換 sqlite（本機開發）或 mysql（TiDB Cloud / MySQL 正式環境）──
var dbProvider = builder.Configuration["Db:Provider"] ?? "sqlite";
var connStr = builder.Configuration.GetConnectionString("Default") ?? "Data Source=yuyu.db";
builder.Services.AddDbContext<AppDb>(o =>
{
    if (dbProvider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        // TiDB 相容 MySQL 8.0 協定，明確指定版本以避免啟動時 AutoDetect 連線
        o.UseMySql(connStr, new MySqlServerVersion(new Version(8, 0, 11)));
    else
        o.UseSqlite(connStr);
});

// ── JWT 驗證 ──
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("請在 appsettings.json 設定 Jwt:Key（至少 32 字元的隨機字串）");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ClockSkew = TimeSpan.FromMinutes(2),
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// 啟動時建立資料庫（個人專案用 EnsureCreated 即可；要演進 schema 再換 Migrations）
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDb>().Database.EnsureCreated();

app.UseDefaultFiles();   // wwwroot/index.html 即前端 PWA
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// ── 工具 ──
static string Hash(string password, byte[] salt) =>
    Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32));

string IssueToken(User u)
{
    var token = new JwtSecurityToken(
        claims: new[] { new Claim("uid", u.Id.ToString()), new Claim("email", u.Email) },
        expires: DateTime.UtcNow.AddDays(30),
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static int CurrentUid(ClaimsPrincipal p) => int.Parse(p.FindFirstValue("uid")!);

// ── 註冊 ──
app.MapPost("/api/auth/register", async (AuthReq req, AppDb db) =>
{
    var email = (req.Email ?? "").Trim().ToLowerInvariant();
    var password = req.Password ?? "";
    if (!email.Contains('@') || email.Length > 254)
        return Results.BadRequest(new { message = "Email 格式不正確" });
    if (password.Length < 8)
        return Results.BadRequest(new { message = "密碼至少需要 8 碼" });
    if (await db.Users.AnyAsync(u => u.Email == email))
        return Results.Conflict(new { message = "這個 Email 已經註冊過了，請直接登入" });

    var salt = RandomNumberGenerator.GetBytes(16);
    var user = new User
    {
        Email = email,
        Salt = Convert.ToBase64String(salt),
        PasswordHash = Hash(password, salt),
        CreatedAt = DateTime.UtcNow,
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    // 新用戶給三個預設帳戶，跟前端首次使用體驗一致
    db.Accounts.AddRange(
        new Account { UserId = user.Id, Id = "a1", Name = "儲蓄",     Color = "#11533C", SortOrder = 0 },
        new Account { UserId = user.Id, Id = "a2", Name = "彈性運用", Color = "#1F5FA8", SortOrder = 1 },
        new Account { UserId = user.Id, Id = "a3", Name = "日常花費", Color = "#C99A3B", SortOrder = 2 });
    await db.SaveChangesAsync();

    return Results.Ok(new { token = IssueToken(user), email });
});

// ── 登入 ──
app.MapPost("/api/auth/login", async (AuthReq req, AppDb db) =>
{
    var email = (req.Email ?? "").Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    // 帳號不存在與密碼錯誤回同一訊息，避免被列舉帳號
    if (user is null)
        return Results.Json(new { message = "Email 或密碼不正確" }, statusCode: 401);

    var hash = Hash(req.Password ?? "", Convert.FromBase64String(user.Salt));
    var ok = CryptographicOperations.FixedTimeEquals(
        Convert.FromBase64String(hash), Convert.FromBase64String(user.PasswordHash));
    if (!ok)
        return Results.Json(new { message = "Email 或密碼不正確" }, statusCode: 401);

    return Results.Ok(new { token = IssueToken(user), email = user.Email });
});

// ── 取得使用者全部資料 ──
app.MapGet("/api/data", async (ClaimsPrincipal p, AppDb db) =>
{
    var uid = CurrentUid(p);
    var accounts = await db.Accounts.Where(a => a.UserId == uid)
        .OrderBy(a => a.SortOrder)
        .Select(a => new AccountDto(a.Id, a.Name, a.Color))
        .ToListAsync();
    var txns = await db.Txns.Where(t => t.UserId == uid)
        .Select(t => new TxnDto(t.Id, t.Type, t.AccountId, t.ToAccountId,
                                t.Amount, t.Category, t.Sub, t.Note, t.Date, t.Ts))
        .ToListAsync();
    var settingRow = await db.Settings.FirstOrDefaultAsync(s => s.UserId == uid);
    System.Text.Json.JsonElement? settings = settingRow is null
        ? null
        : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(settingRow.Json);
    return Results.Ok(new { accounts, txns, settings });
}).RequireAuthorization();

// ── 儲存使用者全部資料（整份快照覆蓋：與前端「狀態即存檔」模式對齊，個人量級最穩） ──
app.MapPut("/api/data", async (SyncPayload payload, ClaimsPrincipal p, AppDb db) =>
{
    if (payload.Accounts is null || payload.Txns is null)
        return Results.BadRequest(new { message = "缺少 accounts 或 txns" });
    if (payload.Accounts.Count > 100 || payload.Txns.Count > 200_000)
        return Results.BadRequest(new { message = "資料量超出限制" });

    var uid = CurrentUid(p);
    await using var tx = await db.Database.BeginTransactionAsync();

    db.Accounts.RemoveRange(db.Accounts.Where(a => a.UserId == uid));
    db.Txns.RemoveRange(db.Txns.Where(t => t.UserId == uid));
    db.Settings.RemoveRange(db.Settings.Where(s => s.UserId == uid));
    if (payload.Settings is { ValueKind: not System.Text.Json.JsonValueKind.Null
                                     and not System.Text.Json.JsonValueKind.Undefined } je)
        db.Settings.Add(new Setting { UserId = uid, Json = je.GetRawText() });

    var order = 0;
    db.Accounts.AddRange(payload.Accounts.Select(a => new Account
    {
        UserId = uid, Id = a.Id, Name = a.Name, Color = a.Color, SortOrder = order++,
    }));
    db.Txns.AddRange(payload.Txns.Select(t => new Txn
    {
        UserId = uid, Id = t.Id, Type = t.Type, AccountId = t.AccountId,
        ToAccountId = t.ToAccountId, Amount = t.Amount, Category = t.Category,
        Sub = t.Sub, Note = t.Note, Date = t.Date, Ts = t.Ts,
    }));

    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return Results.Ok(new { saved = true, at = DateTime.UtcNow });
}).RequireAuthorization();

app.Run();
