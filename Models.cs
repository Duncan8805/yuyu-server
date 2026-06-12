using Microsoft.EntityFrameworkCore;

namespace Yuyu.Api;

// ── 資料表 ──

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class Account
{
    public int UserId { get; set; }
    public string Id { get; set; } = "";       // 前端產生的字串 ID（與 UserId 組成複合主鍵）
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public int SortOrder { get; set; }
}

public class Txn
{
    public int UserId { get; set; }
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";     // in=收入分配 / out=支出 / tr=轉帳
    public string AccountId { get; set; } = "";
    public string? ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? Category { get; set; }      // 大分類（支出才有）
    public string? Sub { get; set; }           // 子分類
    public string? Note { get; set; }
    public string Date { get; set; } = "";     // YYYY-MM-DD
    public long Ts { get; set; }               // 同日排序用
}

public class Setting
{
    public int UserId { get; set; }            // 主鍵：一人一列
    public string Json { get; set; } = "{}";   // 使用者設定（含每月分配範本），JSON 字串
}

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Txn> Txns => Set<Txn>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // MySQL/TiDB 的索引鍵不能是 longtext，字串欄位必須給長度（SQLite 會忽略，無副作用）
        b.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(254);
            e.Property(u => u.PasswordHash).HasMaxLength(64);
            e.Property(u => u.Salt).HasMaxLength(32);
            e.HasIndex(u => u.Email).IsUnique();
        });
        b.Entity<Account>(e =>
        {
            e.HasKey(a => new { a.UserId, a.Id });
            e.Property(a => a.Id).HasMaxLength(40);
            e.Property(a => a.Name).HasMaxLength(50);
            e.Property(a => a.Color).HasMaxLength(9);
        });
        b.Entity<Setting>(e =>
        {
            e.HasKey(s => s.UserId);
        });
        b.Entity<Txn>(e =>
        {
            e.HasKey(t => new { t.UserId, t.Id });
            e.HasIndex(t => new { t.UserId, t.Date });
            e.Property(t => t.Id).HasMaxLength(40);
            e.Property(t => t.Type).HasMaxLength(8);
            e.Property(t => t.AccountId).HasMaxLength(40);
            e.Property(t => t.ToAccountId).HasMaxLength(40);
            e.Property(t => t.Category).HasMaxLength(20);
            e.Property(t => t.Sub).HasMaxLength(30);
            e.Property(t => t.Note).HasMaxLength(500);
            e.Property(t => t.Date).HasMaxLength(10);
            e.Property(t => t.Amount).HasPrecision(14, 2);
        });
    }
}

// ── API DTO（System.Text.Json 預設 camelCase，與前端欄位一一對應）──

public record AuthReq(string? Email, string? Password);
public record AccountDto(string Id, string Name, string Color);
public record TxnDto(string Id, string Type, string AccountId, string? ToAccountId,
                     decimal Amount, string? Category, string? Sub, string? Note,
                     string Date, long Ts);
public record SyncPayload(List<AccountDto>? Accounts, List<TxnDto>? Txns,
                          System.Text.Json.JsonElement? Settings);
