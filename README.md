# 餘裕｜帳戶餘額記帳（純前端 + TiDB Data Service 版）

**架構**：純前端 PWA 直接連 TiDB Cloud。沒有自架後端、不會休眠、永久免費。
**多人**：每人一組 TiDB API Key，資料用 Public Key 自動隔離，互相看不到。

```
你的瀏覽器（PWA）──→ TiDB Cloud Data Service ──→ TiDB（DB）
                            ↑ 自動帶上你的 Public Key 當隔離鑰匙
```

## 一次性設定（你自己做一次，約 15 分鐘）

### 第一步：在 TiDB Cloud 建好資料表
到 SQL Editor 跑（這是新表結構，依 owner 區分使用者資料）：
```sql
CREATE TABLE IF NOT EXISTS yuyu.UserData (
  Owner     varchar(200) NOT NULL PRIMARY KEY,
  Accounts  longtext     NOT NULL,
  Txns      longtext     NOT NULL,
  Settings  longtext     NOT NULL
);
```

> 之前舊版的 Users / Accounts / Txns 表用不到了，可以保留也可刪：
> `DROP TABLE IF EXISTS yuyu.Users, yuyu.Accounts, yuyu.Txns, yuyu.Settings;`

### 第二步：建立 Data App 與兩個端點
1. TiDB Cloud 左邊選單 **Data Service** → **Create Data App** → 取名 `yuyu`，選你的 cluster
2. 建端點 **get_data**：
   - Method：POST
   - Path：`/get_data`
   - Parameters：新增一個 `owner`，type=string，required=yes
   - SQL：
     ```sql
     SELECT Accounts AS accounts, Txns AS txns, Settings AS settings
     FROM yuyu.UserData WHERE Owner = ${owner};
     ```
3. 建端點 **put_data**：
   - Method：POST
   - Path：`/put_data`
   - Parameters：四個都 type=string、required=yes：`owner`、`accounts_json`、`txns_json`、`settings_json`
   - SQL：
     ```sql
     INSERT INTO yuyu.UserData (Owner, Accounts, Txns, Settings)
     VALUES (${owner}, ${accounts_json}, ${txns_json}, ${settings_json})
     ON DUPLICATE KEY UPDATE
       Accounts = VALUES(Accounts),
       Txns = VALUES(Txns),
       Settings = VALUES(Settings);
     ```
4. 兩個端點都按 **Deploy**

### 第三步：複製 App Endpoint 網址
Data App 首頁複製 **App Endpoint** 網址（形如 `https://xxxx.data.tidbcloud.com/api/v1beta/app/dataapp-xxxxx/endpoint`），之後每個人都用同一個 Endpoint 網址，差別在 API Key。

### 第四步：CORS（讓瀏覽器能跨網域呼叫）
Data App 設定頁 → **CORS** → 加入你的 PWA 網域（部署到 GitHub Pages 後填，例 `https://你的帳號.github.io`），開發時可加 `http://localhost:5500`。

## 為每位使用者建立一組 API Key（每多一個人做一次）

1. Data App 設定頁 → **API Keys** → **Create API Key**
2. **Description 取一個能認出是誰的名字**，例如 `duncan`、`wife`、`mom`
3. 角色選 **ReadAndWrite**
4. 複製這組的 **Public Key** 與 **Private Key**（Private Key 只顯示一次！）
5. 把三樣東西交給對方：Endpoint 網址、Public Key、Private Key

對方在 PWA 貼上這三樣資訊，第一次連線時系統會用他的 Public Key 建立一筆專屬資料列，從此他的資料只有他自己看得到、改得到。

## 部署 PWA 到 GitHub Pages（永久免費託管）
1. 把這個資料夾的全部檔案 push 到 GitHub repo（public 或 private 都可）
2. Settings → Pages → Branch 選 `main` / root → Save
3. 等 1-2 分鐘，拿到網址 `https://你的帳號.github.io/repo名`
4. 手機開網址 → 瀏覽器選單「加入主畫面」即可像 App 使用

## 第一次開啟
打開 PWA 網址，輸入：
- **Endpoint 網址**：第三步複製的 App Endpoint
- **Public Key** / **Private Key**：你那組 API Key

驗證成功後金鑰存在裝置上，之後自動帶上。換新裝置時再貼一次即可。

## 多人使用的安全模型
- 每個 API Key = 一個人 = 一份獨立資料；換 Key 等於換帳號
- Public Key 兼任「使用者識別碼」：前端自動用它當 WHERE 條件
- 沒有對方的 Private Key 就算知道 Public Key 也呼叫不了 API
- 不要把 Private Key 貼到 GitHub / 聊天截圖 / 公開的地方
- 萬一某人金鑰外洩：到 TiDB Data App → API Keys 把那組 Revoke，重新給他一組新的

## 金鑰換新後想保留資料
revoke 後重發新 Key 會換掉 Public Key，預設新 Key 第一次連會建空白資料列。要把舊資料搬到新 Key，到 SQL Editor 跑：
```sql
UPDATE yuyu.UserData SET Owner = '新的PublicKey' WHERE Owner = '舊的PublicKey';
```
