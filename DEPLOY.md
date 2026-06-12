# 部署到 Koyeb（免費、不休眠）

## 為什麼選 Koyeb
- 免費方案的 Web Service **常駐執行，不會閒置睡眠**（跟你的 TiDB 一樣），
  不像許多免費平台閒置 15 分鐘就睡、第一個請求要等 30~60 秒醒來
- 自動提供 HTTPS（PWA 安裝、Service Worker 都需要）
- 直接讀 Dockerfile 部署，不用額外設定 build 指令

## 整體流程
```
你的程式碼 → GitHub repo → Koyeb 讀 Dockerfile 建置 → 跑在 https://xxx.koyeb.app
                                                              ↓
                                                      連線到 TiDB Cloud（雲端 DB）
```

---

## 步驟 1：把程式碼放上 GitHub

> appsettings.json 含密碼/金鑰，已在 .gitignore 排除，**不會**被推上去。

```bash
cd server
git init
git add .
git commit -m "init"
```
到 GitHub 建一個新的 **private** repo，照畫面指示把上面的 commit push 上去：
```bash
git remote add origin https://github.com/你的帳號/yuyu-server.git
git branch -M main
git push -u origin main
```

---

## 步驟 2：到 Koyeb 建立 Web Service

1. 到 https://www.koyeb.com 用 GitHub 帳號註冊登入（免信用卡）
2. 點 **Create Service** → Source 選 **GitHub** → 選你剛建立的 repo
3. Builder 選 **Dockerfile**（Koyeb 會自動偵測到 server 資料夾裡的 Dockerfile）
4. Instance 選 **Free**
5. **Port** 設成 `8080`（要跟 Dockerfile 裡 `EXPOSE 8080` 一致）

---

## 步驟 3：設定環境變數（取代 appsettings.json）

在 Koyeb 的 **Environment variables** 區塊新增以下變數（對應到 appsettings.json 的設定，
`:` 換成 `__`）：

| Key | Value |
|---|---|
| `Db__Provider` | `mysql` |
| `ConnectionStrings__Default` | `Server=gateway01.ap-northeast-1.prod.aws.tidbcloud.com;Port=4000;User ID=28DibocSD1PzG2Z.root;Password=你的TiDB密碼;Database=yuyu;SslMode=VerifyFull;` |
| `Jwt__Key` | 換一組新的隨機字串（見下方「換 JWT 金鑰」），不要沿用本機那把 |

> 標記為 **Secret** 的變數內容不會顯示在畫面或 log 上，密碼和金鑰建議都設成 Secret。

---

## 步驟 4：Deploy

按 **Deploy**，等建置完成（第一次含下載 .NET image，大概 2-5 分鐘）。
完成後 Koyeb 會給你一個網址，例如：
```
https://yuyu-xxxxx.koyeb.app
```
打開它，應該會看到「餘裕」的登入畫面。

---

## 換 JWT 金鑰

正式環境的 JWT 金鑰不要沿用本機開發的那把。產生一組新的（在自己電腦執行）：

```bash
# Windows PowerShell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```
或用任何「產生隨機字串」工具，越長越好，貼到 Koyeb 的 `Jwt__Key`。

> ⚠️ 換金鑰後，原本所有人的登入 token 會失效（要重新登入），但帳號密碼和資料不受影響。

---

## 之後更新程式

修改程式碼後：
```bash
git add .
git commit -m "更新內容"
git push
```
Koyeb 預設會自動偵測 push 並重新部署。

---

## 驗證清單
- [ ] 開 Koyeb 給的網址，能看到登入畫面（不是錯誤頁）
- [ ] 註冊新帳號，能成功登入
- [ ] 記一筆支出，到 TiDB SQL Editor 跑 `SELECT * FROM yuyu.Txns;` 看得到新紀錄
- [ ] 手機瀏覽器開網址 → 加入主畫面 → PWA 可離線開啟
