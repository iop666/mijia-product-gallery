# 米家产品示例图库 · 架构文档（Phase 0）

> 项目：mijia-product-gallery —— Windows 原生米家产品素材图库 / 产品图管理器
> 版本：v0.1.0 目标口径 · 文档状态：Phase 0 产出，进入 Phase 1 前的最终架构确认
> 上游数据规范：TIOPWXNL-mijia-product-icons Skill（数据规则最高依据，冲突即暂停重设计）

---

## 0. 仓库关系与数据依赖边界（修正规则 2/3/4）

两个**完全独立**的 GitHub 仓库：

```
iop666/mijia-product-icons      = 米家产品数据/图片快照仓库（纯数据，本应用不改其任何文件）
iop666/mijia-product-gallery    = Windows 应用仓库（本仓库）
```

mijia-product-icons 的三种合法用途（仅此三种）：

1. **开发阶段 Seed 数据源**：开发/测试时直接读取本机检出路径；
2. **测试基准数据源**：以 2026-09-28 快照为冻结夹具，锁定数据规则测试；
3. **初始数据包生成源**：用开发期工具把快照转换成 Seed Package 随应用 Release 分发。

**正式发布的软件不得依赖用户电脑上的任何 Git 仓库路径。**运行时用户侧链路只有：

```
Seed Package（随 Release 提供）
    ↓ 首次启动导入
本地 SQLite + Images（%LOCALAPPDATA%，应用私有）
    ↓ 首次可用
后台米家百科 API 增量同步（永不覆盖用户数据）
```

---

## 1. 开发环境确认与选型（修正规则 1）

### 1.1 实测环境（2026-09-28）

| 检查项 | 结果 |
|---|---|
| `dotnet --info` / `--list-sdks` / `--list-runtimes` | 命令不存在（不在 PATH） |
| `C:\Program Files\dotnet`、`(x86)\dotnet`、注册表 `HKLM\SOFTWARE\dotnet\Setup` | 均不存在 → **全机无 .NET SDK** |
| `~/.dotnet` | 仅 10.0.400/401 孤儿 sentinel 文件（SDK 曾被临时使用后移除），无实际作用 |
| MSBuild | VS 生成工具 2026（18.8.2），路径 `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools` |
| BuildTools 工作负载 | **无** ManagedDesktop 工作负载、**无** Microsoft.NetCore.Component.SDK → 该 MSBuild 不能构建 SDK 风格 .NET 项目 |
| Windows SDK | 10.0.26100 已安装（可编译 `net10.0-windows10.0.19041.0`/`22621` 目标） |
| Windows App SDK 运行时（Appx） | 未安装（SelfContained 部署下非必需） |
| winget | 可用，`Microsoft.DotNet.SDK.10`(10.0.401) / `Microsoft.DotNet.SDK.8`(8.0.425) 均在源中 |
| git / gh | git 正常；gh 2.100.0 已登录 iop666 |

### 1.2 选型结论（基于实测，非机械锁定）

| 项 | 选择 | 依据 |
|---|---|---|
| .NET | **.NET 10（LTS，当前 10.0.401）** | .NET 8 LTS 于 2026-11-10 到期（距今 6 周），新项目不应建立在临期运行时上；Windows App SDK 1.8.x 稳定线已支持 `net10.0-windows10.0.*` TFM |
| UI 框架 | WinUI 3 / Windows App SDK **1.8.x 稳定版** | 2026-09 官方当前稳定渠道 |
| TFM | `net10.0-windows10.0.19041.0` | 应用不需要 Win11 独占 API，19041 下限兼容性最广；本机 26100 SDK 可编译 |
| 部署形态 | **unpackaged + SelfContained**（`WindowsPackageType=None`、`SelfContained=true`、`WindowsAppSDKSelfContained=true`） | 免 MSIX 签名、免安装 WinAppSDK 运行时，`dotnet build` 即可构建，v0.1.0 以 zip 便携包发布 |
| ORM | EF Core 10 + Microsoft.Data.Sqlite | 总纲指定 SQLite + EF Core |
| JSON | System.Text.Json | 总纲指定 |
| 图片解码/缩略图 | **SkiaSharp**（Infrastructure 层） | 纯 NuGet、无 WinRT 依赖、可在 xUnit 中直接测试 PNG/JPG/GIF/WEBP 解码与 WebP 缩略图编码；UI 层只显示磁盘文件 |
| DI / 日志 | Microsoft.Extensions.DependencyInjection / Logging（文件日志 + 滚动） | 总纲指定 |
| 测试 | xUnit | 总纲指定 |

> **待批准事项（不擅自执行）**：`winget install Microsoft.DotNet.SDK.10`。批准后在 Phase 1 开始前执行并复验 `dotnet --info`。

---

## 2. 系统架构

```
mijia-product-gallery/
├─ src/
│  ├─ MijiaProductGallery.App/            WinUI 3 壳（唯一引用 Infrastructure 与 UI 框架的工程）
│  │   ├─ Views/  ViewModels/  Controls/  Converters/  Services/  Assets/
│  │   └─ 平台实现：Clipboard、DragDrop、窗口/生命周期（需窗口上下文，放 App 层）
│  ├─ MijiaProductGallery.Core/           纯 C#，零平台依赖
│  │   ├─ Models/  Interfaces/  Enums/  Rules/
│  │   └─ Rules/ProductCompareRules 为纯函数（重点测试层）
│  ├─ MijiaProductGallery.Infrastructure/ 平台无关实现
│  │   ├─ Database/  Repositories/  Http/  Images/  Sync/  FileSystem/  Logging/  Backup/
│  └─ MijiaProductGallery.Tests/          xUnit（Core 规则 / Database / Sync / Image / 合集）
├─ tools/SeedPackTool/                    开发期控制台工具：icons 仓库快照 → seed-*.zip
└─ docs/
```

原则：UI 只负责 UI；ViewModel 持状态；Core 持模型与业务规则；Infrastructure 持数据库/网络/文件。业务逻辑不进 `Page.xaml.cs`；同步代码不进 Page；不用临时脚本替代正式同步模块。

## 3. 官方数据 vs 用户数据（修正规则 5，结构性隔离）

同步引擎对用户数据**结构性免疫**，而非仅靠代码纪律：

| 域 | 表 | 谁可写 |
|---|---|---|
| **官方数据** | Products、SyncState、SyncRuns、SyncChanges | 仅同步引擎（显式列白名单 UPDATE） |
| **用户数据** | Favorites、ProductUsages、Collections、CollectionItems、UsageEvents、SearchHistories、AppSettings | 仅 UI/服务层；同步引擎不触碰 |

- Products 表**不设** IsFavorite/使用计数列（对总纲 Phase 1 字段清单的有意偏离，理由：把"同步不覆盖用户数据"变成表结构保证）。收藏在 `Favorites` 表，网格读模型用 LEFT JOIN 投影。
- 同步更新 Products 只允许出现官方列（Name/Brand/Category/Image*/Sha256/IsAvailable/UpdateTime/LastSeenTime…），EF 层用显式列清单 UPDATE，不整行覆盖。
- 下架 = `IsAvailable=false`，永不删除；图片永不因下架/换图/复用而删除。

## 4. 数据库设计（SQLite + EF Core 10）

库文件：`%LOCALAPPDATA%\MijiaProductGallery\database\gallery.db`（WAL 模式）。详细数据库设计（ER、索引、迁移、恢复）以 [DATABASE.md](DATABASE.md) 为准。

```
Products ──1:1── ProductUsages        Collections ──1:N── CollectionItems ──N:1── Products
    │                                     
    ├──1:N── Favorites（ProductId 唯一）  UsageEvents ──N:1── Products
    └──1:N── SyncChanges（按型号）        SearchHistories / AppSettings(KV) / SyncState(单行) / SyncRuns
```

| 表 | 关键列 | 索引/约束 |
|---|---|---|
| Products | Id, Model, Name, Brand, Category, SubCategory(预留，接口无此数据), ImageFileName, ImagePath(相对路径), ImageUrl, ImageFormat, ImageWidth/Height, FileSize, Sha256, IsAvailable, CreateTime/UpdateTime(API 原始), FirstSeenTime, LastSeenTime | Model 唯一；Name、Brand、Category、Sha256、IsAvailable |
| Favorites | ProductId PK/FK, CreatedAt | CreatedAt |
| ProductUsages | ProductId PK/FK, CopyCount, DragCount, ViewCount, TotalUseCount, LastUsedAt | TotalUseCount、LastUsedAt |
| Collections | Id, Name, CreatedAt, UpdatedAt, SortOrder | Name 唯一 |
| CollectionItems | CollectionId+ProductId 唯一, AddedAt, SortOrder | 双 FK |
| UsageEvents | Id, ProductId FK, Type(View/Copy/Drag), UsedAt | UsedAt、(ProductId,UsedAt)；默认保留 100 条，超限清理 |
| SearchHistories | Id, Query, CreatedAt | CreatedAt；去重上限 50 |
| AppSettings | Key PK, Value(Json) | — |
| SyncState | 单行 Id=1：LastSyncTime, LastSuccessfulSyncTime, Status, Stage, ErrorMessage, SnapshotDate | — |
| SyncRuns | Id, StartedAt, EndedAt, Status, 触发方式, CountsJson, ErrorMessage | StartedAt |
| SyncChanges | Id, SyncRunId FK, Model, ChangeType(New/Delisted/CategoryChanged/NameTweaked/ImageChanged/IdReused), DetailJson(含判定依据) | Model、ChangeType |

- **FTS5**：首版不启用。1 万行级用索引 + 参数化 LIKE 足够；`ISearchService` 接口预留 FTS5 升级位（Phase 18 性能阶段复评）。
- **损坏策略**：启动时 `PRAGMA integrity_check` 快检；损坏→明确报错横幅，提供「从 Backups 恢复 / 重建索引 / 以 Seed 重开图库」三个选项，**绝不静默删库**。
- 全部跨表写入走事务；同步批量落库按变更集分事务。

## 5. 同步设计（Phase 4）

### 5.1 数据源（2026-09-28 实测确认）

- 分类：`GET https://home.mi.com/cgi-op/api/v1/baike/productCategories/V1?pageSize=20&pageIndex=N`，翻页至不足一页；`data.list[]{ptId,name}`（ptId=-10000 为「新上线」货架）。
- 分类全量产品：`GET .../baike/products/byCategory/V1?ptId=<ptId>`，产品在 **`data.productSimpleVoList`**（不是 `list`）。
- 字段：`model`（=文件名主名）、`name`、`brand`、`realIcon`、`createTime`/`updateTime`（Unix 秒）、`pdId`。
- 约束：浏览器 UA；限速 ~0.4s/请求（可配置）；超时 30s；3 次指数退避重试 + 抖动；全程可取消。

### 5.2 实测陷阱（Skill 未记载，已纳入实现与测试项）

1. **`realIcon` URL 含 `&amp;` HTML 实体**（实测 Aqara 网关 M1S 等条目），请求前必须 HTML 解码；
2. **产品对象自带 `ptId` ≠ 分类 `ptId`**（实测 `lumi.gateway.acn01` 自带 ptId=12 照明，实际属路由网关 16）→ **归类唯一依据 = 该型号出现在哪个 byCategory 返回列表**；
3. URL 含非 ASCII 必须对 path percent-encode；CDN Content-Type 不可信（Skill §3）。

### 5.3 归类口径（与仓库口径一致）

分类名以分类接口 `name` 为准（ptId 顺序变化不影响）。「新上线」是滚动上新货架：货架型号若同时出现在真实大类列表 → 归真实大类；仅出现在货架 → 归「新上线」。一个型号只属一个大类。

### 5.4 同步流程（17 步，可断点续传）

```
获取分类 → 逐类获取产品 → 标准化 → 与本地对比(ProductCompareRules 纯函数)
→ 差异分类：新增/下架/归类变化/名称微调/换图/ID复用
→ 下载图片(并发≤8，每张3次重试，临时文件原子改名)
→ 文件头校验(PNG/JPEG/GIF87a/89a/RIFF-WEBP) + 非空 + SHA256
→ 按变更集分事务落库(官方列白名单) → .old 链改名(两处同步) → 缩略图队列
→ SyncChanges/SyncRuns 记录 → 更新 SyncState
```

失败语义：任一步失败保留旧状态、旧图；下载失败下次续传（已存在且 sha 一致的文件跳过）；网络恢复后可重试；**禁止"半残状态"**——DB 变更与文件改名在同一逻辑变更集内，先文件后 DB、DB 失败回滚文件改名。

### 5.5 ID 复用判定（Skill §4 顺序，纯函数实现）

1. sha256 相同 → 无变化；
2. sha 不同 + 名称/品牌未本质变化 → **换图**（原地替换，记 ImageChanged）;
3. sha 不同 + 名称/品牌指向明显不同产品（或 createTime 为新建）→ **ID 复用**：旧图改 `.old.png`/`.old1.png`/`.old2.png`…（`.old` 永远是最近被替换者），新图占用原文件名，SyncChanges 记录新旧名称与 sha256；
4. **拿不准一律按 ID 复用**（保旧图优先）。

### 5.6 自动同步

默认每天 1 次；可选：每次启动 / 6 小时 / 每天 / 每周 / 关闭。距上次成功同步不足间隔则跳过。后台线程执行，不阻塞 UI；同步中心显示阶段（获取分类→获取产品→比较→下载→缩略图→落库→完成）、统计（新增/下架/换图/归类/ID复用/异常图）与失败原因/阶段/重试按钮。

## 6. 图片生命周期（Phase 3，详细设计见 [IMAGE_SYSTEM.md](IMAGE_SYSTEM.md)，已实现）

`Infrastructure/Images/`：`ImageStore`（临时文件 → 文件头判定 → 完整解码 → SHA256 → 原子落位，入口 StoreNew / Replace / ReplaceWithHistory 与对比规则一一对应）与 `ThumbnailService`（`{安全主名}.{sha8}.webp`，SHA 绑定、损坏自愈、可全量重建）。

```
来源(API realIcon / Seed Package)
  → 下载/解包 → 文件头判定扩展名(不信 Content-Type) → 非空校验 → SHA256
  → 原子写入 images\<model>.<ext>（临时名+move）
  → 入库(官方列) → 异步生成缩略图 thumbnails\<model>.<sha8>.webp
  → [换图] sha 变化：旧图按 .old 链改名，新图占原名，缩略图随 sha8 键自动失效重建
  → [ID复用] 同上且 SyncChanges 留痕 → [下架] 文件与记录原样保留
```

- `aux.*` 保留名：DB 存真实文件名；磁盘安全名映射 `aux.` → `aux_.`（如 `aux_.aircondition.hc1.png`），所有磁盘访问走 `ImagePath`。导出（复制/拖拽）内容按流处理不受影响；落盘到用户目标时按 Windows 约束使用安全名。
- 原图不可变：缩略图永不覆盖原图；缓存损坏不删原图；缩略图可随时全量重建。
- 图片读取全异步；解码在后台线程（SkiaSharp），UI 只收 BitmapImage(文件路径) 或已缓存解码结果。

## 7. Seed Package 设计（修正规则 4，新增）

### 7.1 包格式 `seedpack v1`

```
seed-2026-09-28.zip
├─ manifest.json    { schemaVersion:1, snapshotDate:"2026-09-28", generator:"SeedPackTool x.y",
│                     source:"iop666/mijia-product-icons@tag 2026-09-28",
│                     productCount:10547, imageCount:10546, auxCount:2, delistedCount:14 }
├─ products.jsonl   每行一个产品（UTF-8 无 BOM）：
│                   { model, name, brand, category, isAvailable, imageFileName, imageFormat,
│                     sha256, width, height, fileLength, createTime, updateTime,
│                     oldImages:[{fileName,sha256}] }
└─ images/<model>.<ext>   平铺，含 .old*.png 历史图与 aux.* 保留名图（zip 内合法）
```

### 7.2 生成（开发期，`tools/SeedPackTool`）

输入 = mijia-product-icons 本机检出（读 `00_总清单.csv` + `下架清单.csv` + 两棵图树，UTF-8 BOM 用正规 CSV 解析器）；逐图算 SHA256 与尺寸；输出 zip + 校验统计（与 Skill §8 自检同口径：零孤儿/零缺失/零 0 字节）。工具只在本仓库开发分支存在，随 app Release 挂 `seed-<date>.zip` 附件。

### 7.3 导入（应用内，首次初始化）

查找顺序：exe 同目录 → `%LOCALAPPDATA%\MijiaProductGallery\seed\` → 用户手选。导入流程：manifest 校验 → 计数核对 → 临时库建 Products（isAvailable 按包还原）→ 逐图按 sha 断点续拷到 `images\`（已存在且 sha 同则跳过）→ 原子换库 → 后台补缩略图。幂等可中断；导入中可取消且不留半库。种子快照日期与 API 差距由首次后台增量同步收敛。无 Seed 且无网络 → 空图库 + 离线横幅，可稍后导入/同步，**不卡启动页**。

## 8. UI 信息架构（Phase 6+，本阶段只定结构）

```
MainWindow（NavigationView 壳，标题栏集成搜索框，全屏/最大化优先）
├─ 初始化页        种子导入/在线初始化进度；断网→空图库+离线状态
├─ 图库页(默认)    命令栏：搜索 | 排序 | 随机 | 收藏 | 更多(筛选面板) | 清空筛选 | 筛选 Chip
│                 网格：ItemsRepeater + UniformGridLayout（虚拟化，禁一次性建全部控件）
│                 卡片：图/中文名/品牌/分类/使用次数/收藏标；Hover、Pressed、Selected、
│                       Favorite、Loading、Error 六态齐备
│                 右滑筛选面板：大类/小类/品牌/收藏/合集/下架/格式/使用次数/更新时间；
│                       开合不丢筛选状态；Chip 形如 [厨房电器 ×]；清空筛选需确认且可撤销
├─ 产品详情        全量元数据（图/名称/型号/ID/品牌/大类小类/格式/尺寸/大小/SHA256/本地路径/
│                 官方 URL/创建/更新/最后同步/四项计数/收藏）+ 复制/拖拽/收藏/加合集/打开位置
├─ 收藏与合集      全部收藏为默认根；新建/重命名/删除/排序/加入/移除；产品可属多合集
├─ 最近使用        最近查看/复制/拖拽（UsageEvents，默认 100 条），可清空
├─ 随机浏览        随机 10/20/50/全部 × 范围(当前筛选/大类/品牌/收藏/全部) × 排除(已看过/
│                 已收藏/最近使用/下架)；明确退出按钮
├─ 同步中心        数据源/上次同步/状态(空闲/同步中/成功/失败)/统计/立即同步/暂停/重试
└─ 设置            常规(开机启动/启动同步/自动同步) 图库(卡片尺寸/缩略图质量/默认排序)
                  行为(复制/拖拽/查看是否计数、双击行为) 数据(目录/备份/恢复/清缓存/重建索引)
                  同步(周期/超时/重试) 关于(版本/数据源/最后同步/GitHub)
```

排序（持久化）：名称/型号 ±A-Z、使用最多/最少、最近/最久使用、最近/最早更新、最新/最早添加、随机。搜索（持久化）：名称/型号/品牌/ID/大类/小类/文件名模糊，实时更新，历史记录。快捷键：Ctrl+F、Ctrl+C、Enter、Esc。

## 9. 拖拽设计（Windows 原生 OLE，Phase 7）

- 触发：卡片 PointerPressed ≥300ms 长按或移动超阈值 → `StartDragAsync(DataPackage)`（WinUI 3 → 系统 OLE DoDragDrop，非控件位移）。
- 载荷：`SetStorageItems(StorageFile)`（CF_HDROP，真实磁盘文件，Photoshop/PowerPoint/Word/Explorer 均收）+ 目标效果 Copy。aux 模型出安全名（§6）。
- 结果：`StartDragAsync` 返回的 DataPackageOperation 含 Copy → `DragCount+1`、`TotalUseCount+1`、写 UsageEvents(Drag)。取消/失败不计数。
- 大文件不复制不压缩，直接给原路径；拖拽期间 UI 不卡（异步启动，拖起后立即返回）。

## 10. Clipboard 设计（Phase 7）

- 复制图片：DataPackage 同时 `SetBitmap(RandomAccessStreamReference)`（位图内容，粘进图像软件）与 `SetStorageItems`（可粘成文件），双格式由目标程序自取；`SetContentAsync` 成功 → `CopyCount+1`、`TotalUseCount+1`、UsageEvents(Copy)。
- 复制型号/名称/ID：`SetText`，同样计数为 Copy（各选项可在设置中关闭计数）。
- 打开文件位置：`explorer.exe /select,<path>`；aux 路径用安全名。

## 11. 缓存设计（Phase 3/18）

- **磁盘缩略图缓存**：`thumbnails\<model>.<sha8>.webp`（最长边 480、质量 80，均可在设置调整）；键含 sha8 → 换图自动失效；可整体清理/重建，重建只读原图。
- **内存解码缓存**：LRU，默认预算 256MB（按卡片尺寸估算容量可配），退出即释放；快速滚动不得持续增长（Phase 23 专项验证）。
- **加载管线**：可见项优先的异步队列（Channel + 优先级 = 视口距离）；命中顺序 内存 → 磁盘缩略图 → 原图解码并回写缩略图；失败/超时 → 卡片 Error 态 + 重试；详情页才加载原图。

## 12. 错误恢复设计（Phase 21）

| 类别 | 表现 | 恢复 |
|---|---|---|
| 网络 | 图库页离线横幅「无法连接米家百科」 | 稍后同步/手动重试；浏览不受影响 |
| API 失败 | 同步中心显示失败阶段+原因+重试 | 指数退避自动重试 3 次 → 转手动 |
| 图片失败 | 单图 Error 态；同步侧保留旧图 | 下次同步续传；缩略图可重建 |
| 数据库 | 启动 integrity_check 失败 → 明确报错 | 备份恢复 / 重建索引 / Seed 重开；绝不静默覆盖 |
| 文件系统 | aux/长路径/权限错误逐项提示 | 安全名映射 + 路径规范化 + 重试 |

原则：不崩溃退出、不静默失败；任何失败不破坏既有数据；日志（滚动文件）记录全链路。

## 13. 数据迁移策略

- **Schema**：EF Core Migrations；升级前自动复制 db 文件到 `backups\pre-migration-<ver>.db`；迁移失败回滚至备份并报错。
- **Seed 包**：`schemaVersion` 前向兼容导入（未知字段忽略）；老包可导入新版应用，导入后由 API 同步收敛。
- **设置**：AppSettings 带 `schema.version` 键，升级做显式默认值合并，不整库重置。
- **图片目录**：文件名规则变更（如 aux 安全名）通过 ImagePath 列迁移，不动磁盘旧文件。

## 14. 安全与数据完整性（Phase 24）

网络白名单仅 `home.mi.com` 与 `*.mi-img.com`（CDN），不引入第三方网络服务；不执行远程脚本、不下载可执行文件；图片按文件头验证；型号文件名白名单 `^[A-Za-z0-9._-]+$` + 路径规范化，防穿越；不覆盖用户自定义文件；DB 写入事务化；同步失败不损坏旧数据。Skill 冲突即暂停重确认（第一原则：数据正确性 > UI）。

## 15. 测试基线（Phase 1 即建）

- **数据规则冻结夹具**：以 2026-09-28 快照（10,547 产品 CSV + 10,546 图清单 + 录制的 API 响应样本）为基线，ProductCompareRules 全部判定（新增/下架/归类/换图/ID 复用/.old 链/名称微调）做基准测试——先把"官网数据变化 → 本地应该发生什么变化"这一层测试死，GUI 之上再搭 UI。
- Image：PNG/JPG/GIF/WEBP/伪图/0 字节/`&amp;` URL/非 ASCII URL；Sync：正常/网络失败/API 失败/图片失败/中途失败/重试/重复同步；DB：CRUD/迁移/索引/损坏；合集：全生命周期；性能：1000/5000/10000/30000 模拟数据。

## 16. Phase 1～25 执行顺序

维持总纲顺序（0→25），纳入修正：Phase 5 首次初始化 = Seed 导入优先；Phase 25 发布物 = SelfContained zip + seed 附件。每阶段按总纲三十八节格式输出验收报告，Build/Test 不通过不进下一阶段。

---

### 已批准并完成的事项

1. .NET SDK 10.0.401 已安装（`dotnet --info`/`--list-sdks`/`--list-runtimes` 复验通过，运行时 10.0.12，未安装额外 workload）；
2. GitHub 私有仓库 `iop666/mijia-product-gallery` 已创建并绑定 remote（main 为默认分支，稳定后转 Public）；
3. Windows App SDK 具体小版本在 UI 工程（后续阶段）创建时以 NuGet 实际解析为准。
