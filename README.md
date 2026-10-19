# Allagan Tools（InventoryTools）

[Allagan Tools](https://github.com/Critical-Impact/InventoryTools) 的台服移植版，追蹤角色/僱員/公會倉庫的所有道具，並提供製作規劃、市場整合與各式資料查詢視窗。

## 功能

- **道具追蹤**：統整所有角色、僱員、公會倉庫（含跨伺服器/跨帳號）內的道具數量與位置
- **篩選系統**：自訂複雜搜尋條件建立道具清單，可在背包中即時高亮顯示符合條件的道具
- **製作規劃**：`/crafts` 開啟製作清單視窗，自動計算所需材料並可整合背包/倉庫庫存；缺料可標出「哪裡買」並請 Lifestream 帶你到 NPC 商人；`/craftoverlay` 開啟製作用小型疊加視窗
- **市場佈告板整合**：透過 Universalis 查詢各伺服器的市場價格與成交數，可設定排除的伺服器
- **道具詳細視窗**：`/moreinfo`（別名 `/itemwindow`）顯示道具的商人、取得來源等資訊
- **裝備建議**：`/atrecommend`（別名 `/atr`）依角色職業/等級推薦可裝備的更好裝備
- **資料查詢視窗**：`/duties`（別名 `/atduties`，任務）、`/mobs`（別名 `/atmobs`，怪物）、`/atnpcs`（NPC）、`/airships`（飛空艇）、`/submarines`（潛水艇）、`/retainerventures`（僱員探險）
- **提示視窗整合**：滑鼠懸停道具即可查看其收納位置
- **主要指令**：`/allagantools`（別名 `/atools`，開啟道具清單視窗）、`/openlist <清單名稱>`（開關單一清單視窗）、`/athighlight`（別名 `/atf`，切換清單高亮）、`/atclearhighlights`、`/atcloselists`（`/atclosefilters`）、`/atclearfilter`、`/atclearall`、`/atconfig`（設定視窗）、`/athelp`（說明視窗）
- **背包快滿提醒**（選用）：背包空位跨過設定的門檻時請塔塔露念一句；AutoRetainer、Artisan 自動執行期間不念

## 台服版的修改

- 介面繁體中文化（公會、陸行鳥鞍囊等用台服官方譯名）
- 市場查價改為逐伺服器判定台服是否有資料、新增「排除的伺服器」設定（已停止營運的伺服器不再查價），並修正查價大量失敗與「N 日成交數」卡在 20
- 製作清單缺料列標出「哪裡買」，可請 Lifestream 直接前往（需 ItemVendorLocation）
- 背包快滿時可請 TataruPraise 提醒
- 庫存資料改成變動停下來後才存檔，存檔被中斷也不會留下壞檔
- 修正收藏品判定讀錯來源（部分修正在共用函式庫 CriticalCommonLib）
- 多處降低記憶體用量

## 需要搭配的插件

- 選用：ItemVendorLocation＋Lifestream（製作清單的「哪裡買」）、TataruPraise（背包快滿提醒）

## 授權

依照原專案授權條款發布，詳見 [LICENSE](LICENSE)。
