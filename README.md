# Allagan Tools（InventoryTools）

[Allagan Tools](https://github.com/Critical-Impact/InventoryTools) 的台服移植版，追蹤角色/僱員/公會儲物櫃的所有道具，並提供製作規劃、市場整合與各式資料查詢視窗。

## 功能

- **道具追蹤**：統整所有角色、僱員、公會儲物櫃（含跨伺服器/跨帳號）內的道具數量與位置
- **篩選系統**：自訂複雜搜尋條件建立道具清單，可在背包中即時高亮顯示符合條件的道具
- **製作規劃**：`/crafts` 開啟製作清單視窗，自動計算所需材料並可整合背包與僱員的庫存；缺料列會標出哪個 NPC 商人有賣，並可請 Lifestream 帶你到商人面前；`/craftoverlay` 開啟製作用小型疊加視窗
- **從遊戲加入製作清單**：開著公會合建的零件視窗或大國防聯軍的籌備任務視窗時，製作清單視窗的「從遊戲加入」選單可以一鍵把需要的道具加進目前的清單
- **製作清單收集進度通知**（預設關閉）：照清單收集時在聊天視窗顯示還差多少、完成時播放音效；在清單設定的「通知」分類開啟
- **市場佈告板整合**：透過 Universalis 查詢各伺服器的市場價格與成交數，可設定排除的伺服器
- **貨幣兌換價值排行**：`/atcurrency`（主視窗的「視窗」選單也有）。選一種貨幣（神典石、軍票、宇宙信用點數等），列出能換的東西每 1 單位值多少 Gil、每日成交與資料時間；可切換「淨值優先／賣得完優先」，並勾選只看哪些角色的持有量。只讀市價快取，按「查價」才會查詢，不會替你兌換或上架
- **道具詳細視窗**：`/moreinfo`（別名 `/itemwindow`）顯示道具的商人、取得來源等資訊
- **裝備建議**：`/atrecommend`（別名 `/atr`）依角色職業/等級推薦可裝備的更好裝備
- **資料查詢視窗**：`/duties`（別名 `/atduties`，任務）、`/mobs`（別名 `/atmobs`，怪物）、`/atnpcs`（NPC）、`/airships`（飛空艇）、`/submarines`（潛水艇）、`/retainerventures`（僱員探險）
- **提示視窗整合**：滑鼠懸停道具即可查看其收納位置；可另外顯示這個道具在哪些精選清單裡（預設關閉）
- **主要指令**：`/allagantools`（別名 `/atools`，開啟道具清單視窗）、`/openlist <清單名稱>`（開關單一清單視窗）、`/athighlight`（別名 `/atf`，切換清單高亮）、`/atclearhighlights`、`/atcloselists`（`/atclosefilters`）、`/atclearfilter`、`/atclearall`、`/atconfig`（設定視窗）、`/athelp`（說明視窗）
- **背包快滿提醒**（需要 TataruPraise）：背包空位跨過設定的門檻時請塔塔露念一句；AutoRetainer、Artisan 自動執行期間不念
- **給其他插件的 IPC**：其他插件可以查持有數量、製作清單與篩選清單的內容（`AllaganTools.*`），例如 Marketbuddy 用它避開製作清單上的材料、TCToolbox 用它列出製作清單的缺料

## 台服版的修改

- 介面繁體中文化（公會、公會儲物櫃、陸行鳥鞍囊等用台服官方譯名）
- 市場查價改為逐伺服器判定台服是否有資料、新增「排除的伺服器」設定（已停止營運的伺服器不再查價），並修正查價大量失敗與「N 日成交數」卡在 20
- 新增「貨幣兌換價值排行」視窗
- 製作清單缺料列標出哪個 NPC 商人有賣，可請 Lifestream 直接前往（需 ItemVendorLocation）；新增「從遊戲加入」選單與收集進度通知；公會商店物品的公會點數成本改用商店標價
- 道具提示框可顯示所屬的精選清單
- 新增給其他插件查持有數量的 IPC（依分類合計、依角色分列）
- 背包快滿時可請 TataruPraise 提醒
- 庫存、設定與歷史記錄改成不會寫到一半就中斷的存檔方式；庫存變動停下來後才存檔，紀錄檔讀壞時先留備份再覆寫
- 修正收藏品判定讀錯來源（部分修正在共用函式庫 CriticalCommonLib）
- 介面寬度跟著 Dalamud 的字型大小放大（貨幣兌換價值排行）；多處降低記憶體用量

## 安裝

1. 在遊戲內輸入 `/xlsettings` 開啟「Dalamud 設定」，到「試驗性功能」分頁的「第三方插件倉庫」（Custom Plugin Repositories）貼上
   `https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json`
   勾選啟用後按「儲存變更」。
2. 輸入 `/xlplugins` 開啟「插件管理員」（Plugin Installer），搜尋「Allagan Tools」安裝。

請不要安裝上游（國際服）版本，它不是對著台服客戶端做的。

## 需要搭配的插件

- 選用：[ItemVendorLocation](https://github.com/ffxiv-tc-port/ItemVendorLocation)＋[Lifestream](https://github.com/ffxiv-tc-port/Lifestream)（製作清單的商人位置與前往）、[TataruPraise](https://github.com/ffxiv-tc-port/TataruPraise)（背包快滿提醒）

原作者：[Critical-Impact](https://github.com/Critical-Impact)

授權：GPL-3.0（見 LICENSE）
