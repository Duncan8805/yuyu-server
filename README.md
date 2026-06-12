# 餘裕｜帳戶餘額記帳（全端版：登入 + 多人 + 多裝置同步）

## 架構
```
ASP.NET Core 8 Minimal API ─ 同時服務前端 PWA（wwwroot）與 API
└─ SQLite（yuyu.db）        ─ 資料庫：使用者、帳戶、交易，依 UserId 隔離
瀏覽器 localStorage          ─ 離線快取，恢復連線自動同步
```

## 資料放哪裡？
- **本機開發**：SQLite（`yuyu.db`），零設定，`dotnet run` 就能跑
- **正式環境（免費雲端、不休眠）**：TiDB Cloud Starter — MySQL 相容、免費 5 GiB
  與 5,000 萬 RU/月、免綁卡，不像 Supabase 閒置 7 天會暫停專案
- **快取**：每台裝置的 localStorage，離線也能記帳，連上線後整份快照同步回伺服器
- **同步策略**：整份快照覆蓋（last-write-wins），個人記帳量級最簡單可靠；
  若未來多人同時編輯同一帳本才需要改成逐筆 CRUD + 版本號

## 使用 TiDB Cloud（免費雲端 DB）的設定步驟
1. 到 https://tidbcloud.com 註冊（可用 GitHub / Google 帳號，不用信用卡）
2. 建立一個 **Starter**（免費）cluster，區域選 Tokyo / Singapore（離台灣近）
3. 在 cluster 頁面點 **Connect**，選 General → .NET / MySqlConnector，
   會給你 Host、Port（4000）、User（有一段亂碼前綴，要整段帶上）、Password
4. 先在 TiDB 的 SQL Editor（或任何 MySQL client）執行 `CREATE DATABASE yuyu;`
5. 改 `appsettings.json`：
   ```json
   "Db": { "Provider": "mysql" },
   "ConnectionStrings": {
     "Default": "Server=gateway01.ap-southeast-1.prod.aws.tidbcloud.com;Port=4000;User ID=xxxxxx.root;Password=你的密碼;Database=yuyu;SslMode=VerifyFull;"
   }
   ```
   TiDB 強制 TLS，`SslMode=VerifyFull` 必須加（MySqlConnector 會用系統 CA 驗證，不用另外下載憑證）
6. `dotnet run`，啟動時 EnsureCreated 會自動在 TiDB 建好資料表
7. 正式部署時連線字串建議改用環境變數 `ConnectionStrings__Default` 注入，不要進版控

切回本機 SQLite：`"Db": { "Provider": "sqlite" }` 即可，兩邊資料表結構相同。

## 本機啟動
```bash
cd server
dotnet run
# 開 http://localhost:5000（或終端機顯示的埠號）
```
第一次啟動會自動建立 yuyu.db。註冊帳號即可使用，每位使用者各自獨立。

## API 一覽
| Method | Path               | 說明 |
|--------|--------------------|------------------------------|
| POST   | /api/auth/register | 註冊 {email, password}，回 JWT |
| POST   | /api/auth/login    | 登入，回 JWT（30 天效期）       |
| GET    | /api/data          | 取得自己的帳戶 + 交易（需 Bearer token）|
| PUT    | /api/data          | 整份快照存檔（需 Bearer token）|

## 正式部署注意
1. **JWT 金鑰**：appsettings.json 的 `Jwt:Key` 已隨機產生，部署前建議再換一組，
   或改用環境變數 `Jwt__Key` 注入，不要進版控
2. **HTTPS 必要**（PWA 安裝與 Service Worker 都要求）：
   最省事是前面擋一層 Caddy 或 Nginx + Let's Encrypt 反向代理到 Kestrel
3. **發佈**：`dotnet publish -c Release -o out`，把 out/ 丟到伺服器跑
   `./Yuyu.Api`（Linux 可包成 systemd service）
4. **資料庫切換**：已內建 SQLite 與 MySQL(TiDB) 雙 provider，改 Db:Provider 即可
5. **資料庫備份**：cron 定期 `cp yuyu.db backup/yuyu-$(date +%F).db` 即可

## 既有資料庫升級（v2 → v3：新增 Settings 表）
EnsureCreated 只在「資料庫全空」時建表，不會幫既有資料庫補新表。
已在用 TiDB 的話，到 SQL Editor 跑一次：
```sql
CREATE TABLE yuyu.Settings (
  UserId int NOT NULL,
  Json longtext NOT NULL,
  CONSTRAINT PK_Settings PRIMARY KEY (UserId)
);
```
本機 SQLite 開發庫直接刪掉 yuyu.db 重啟即可。

## 安全設計
- 密碼以 PBKDF2（SHA-256、100,000 次迭代、隨機 salt）雜湊儲存，不存明文
- 登入失敗不區分「帳號不存在／密碼錯誤」，避免帳號列舉
- 密碼比對使用固定時間比較（FixedTimeEquals）
- API 一律驗 JWT，使用者只能讀寫自己 UserId 的資料
