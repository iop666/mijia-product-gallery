# 数据库设计 · DATABASE.md

> 适用：mijia-product-gallery 本地 SQLite 数据层（EF Core 10 + Microsoft.Data.Sqlite）
> 文件位置：`%LOCALAPPDATA%\MijiaProductGallery\database\gallery.db`（WAL 模式）
> 数据规模基线：当前 10,547 产品；设计余量按 20,000+ 产品、十万级使用事件评估
> 原则：**图片永不入库**——SQLite 只存 `ImagePath / Sha256 / ImageWidth / ImageHeight / ImageFormat / FileSize` 元数据；图片文件独立于 `images\` 目录管理

---

## 1. ER 图与表清单

```mermaid
erDiagram
    PRODUCTS ||--o| FAVORITES : "收藏（用户）"
    PRODUCTS ||--o| PRODUCT_USAGES : "计数（用户）"
    PRODUCTS ||--o{ USAGE_EVENTS : "使用事件（用户）"
    PRODUCTS ||--o{ COLLECTION_ITEMS : "加入合集（用户）"
    COLLECTIONS ||--o{ COLLECTION_ITEMS : "包含"

    SYNC_RUNS ||--o{ SYNC_CHANGES : "产生（Model 为弱关联文本，无外键）"
    SYNC_STATE ||--|| SYNC_STATE : "单行（Id=1）"

    PRODUCTS {
        int Id PK
        text Model UK "型号，唯一"
        text Name
        text Brand
        text Category
        text SubCategory "预留"
        text ImageFileName "原始文件名（含 aux. 真实名）"
        text ImagePath "磁盘相对路径（安全名映射后）"
        text ImageUrl
        int ImageFormat
        int ImageWidth
        int ImageHeight
        int FileSize
        text Sha256
        int IsAvailable "下架=0，记录与图片保留"
        int CreateTimeUnix "官网原始"
        int UpdateTimeUnix "官网原始"
        int FirstSeenUnix "本地事实"
        int LastSeenUnix "本地事实"
    }
    FAVORITES {
        int ProductId PK_FK "CASCADE"
        int CreatedUnix
    }
    PRODUCT_USAGES {
        int ProductId PK_FK "CASCADE"
        int CopyCount
        int DragCount
        int ViewCount
        int TotalUseCount
        int LastUsedUnix "NULL=从未使用"
    }
    USAGE_EVENTS {
        int Id PK
        int ProductId FK "CASCADE"
        int Type "0=View 1=Copy 2=Drag"
        int UsedUnix
    }
    COLLECTIONS {
        int Id PK
        text Name UK "合集名唯一"
        int CreatedUnix
        int UpdatedUnix
        int SortOrder
    }
    COLLECTION_ITEMS {
        int CollectionId PK_FK "CASCADE"
        int ProductId PK_FK "CASCADE"
        int AddedUnix
        int SortOrder
    }
    SEARCH_HISTORIES {
        int Id PK
        text Query UK "去重，重复搜索刷新时间"
        int CreatedUnix
    }
    APP_SETTINGS {
        text Key PK
        text Value "JSON"
    }
    SYNC_STATE {
        int Id PK "恒为 1"
        int LastSyncUnix "NULL"
        int LastSuccessfulSyncUnix "NULL"
        int Status
        int Stage
        text ErrorMessage "NULL"
        text SnapshotDate "NULL"
    }
    SYNC_RUNS {
        int Id PK
        int StartedUnix
        int EndedUnix "NULL"
        int Status
        int Stage
        int Trigger
        text CountsJson "SyncRunCounts 序列化"
        text ErrorMessage "NULL"
    }
    SYNC_CHANGES {
        int Id PK
        int SyncRunId FK
        text Model "弱关联，无外键（历史不随产品消失）"
        int ChangeType
        text DetailJson "ProductChange 序列化（含判定依据）"
    }
```

### 表清单与数据域归属

| 域 | 表 | 说明 |
|---|---|---|
| 官方数据 | `Products` | 官网数据镜像 + 两个本地事实字段（FirstSeen/LastSeen） |
| 用户数据 | `Favorites`、`Collections`、`CollectionItems`、`ProductUsages`、`UsageEvents`、`SearchHistories`、`AppSettings` | 仅 UI/服务层写入 |
| 同步数据 | `SyncState`（单行）、`SyncRuns`、`SyncChanges` | 仅同步引擎写入；`SyncChanges.Model` 为文本，历史留痕不随产品增删变化 |

> **ViewHistory 的实现口径**：不再单设 ViewHistory 表。"最近查看"由 `UsageEvents` 中 `Type=View` 的行承载，与复制/拖拽共用同一张事件表（同一查询即可产出"最近使用"混合流，也可按 Type 过滤）；查看计数在 `ProductUsages.ViewCount`。此为对总纲表清单的合并式实现，功能完整覆盖。

## 2. 数据隔离规则（结构性保证）

| 操作者 | 可写 | 禁止写入 |
|---|---|---|
| 同步引擎 | `Products` 的**官方列白名单**（Name、Brand、Category、SubCategory、ImageFileName、ImagePath、ImageUrl、ImageFormat、ImageWidth、ImageHeight、FileSize、Sha256、IsAvailable、CreateTimeUnix、UpdateTimeUnix、LastSeenUnix）+ `SyncState`/`SyncRuns`/`SyncChanges` | `Favorites`、`Collections`、`CollectionItems`、`ProductUsages`、`UsageEvents`、`SearchHistories`、`AppSettings`；以及 `Products.FirstSeenUnix`（本地事实） |
| UI / 服务层 | 用户数据各表；`Products` 仅经用户行为间接产生 LastUsed 相关记录（存于用户表） | `SyncState`/`SyncRuns`/`SyncChanges` |

实现要点：

1. **列白名单 UPDATE**：`ProductRepository.UpdateOfficialFieldsAsync` 使用 EF `ExecuteUpdate` 显式列出全部官方列，一次调用不可能触碰用户表或 `FirstSeenUnix`；
2. **用户状态不在 Products 上**：收藏/计数/历史均为独立表，同步"整行替换 Products"的写法在结构上不存在；
3. **隔离测试**：产品官方更新前后，收藏行、计数行、事件行逐字段比对不变（见 `ProductRepositoryIsolationTests`）。

## 3. 索引设计

| 表 | 索引 | 支撑场景 |
|---|---|---|
| Products | `Model` 唯一索引 | 按型号查重/取行（同步热路径） |
| Products | `Name`、`Brand`、`Category`、`IsAvailable` | 筛选与排序（大类过滤、在架过滤、A-Z 排序） |
| Products | `Sha256` | 换图/复用批量校验 |
| Favorites | `CreatedUnix` | 按收藏时间排序 |
| ProductUsages | `TotalUseCount`、`LastUsedUnix` | "使用最多/最近使用"排序 |
| UsageEvents | `UsedUnix`、`(ProductId, UsedUnix)` | 最近使用流（默认保留 100 条）、单产品历史 |
| Collections | `Name` 唯一 | 合集重名校验 |
| CollectionItems | 复合主键 `(CollectionId, ProductId)`、`ProductId` 索引 | 防重复加入；反查"产品在哪些合集" |
| SearchHistories | `Query` 唯一、`CreatedUnix` | 搜索历史去重 + 时间序 |
| SyncRuns | `StartedUnix` | 同步历史列表 |
| SyncChanges | `Model`、`ChangeType`、`(SyncRunId)` | 单型号变更追溯、按类型统计 |

FTS5：本期不启用。1～3 万行时"索引 + LIKE"即可保持即时搜索；`ISearchService` 契约已隔离搜索实现，未来升级 FTS5 只需替换实现（新表 `ProductsFts` 与触发器同步，详见迁移计划）。

## 4. Migration 策略

- **版本权威**：EF Core `__EFMigrationsHistory` 表即 SchemaVersion 唯一权威，不另设版本号，避免双源漂移；当前版本 = 已应用迁移的最高一个。
- **首次安装**：库文件不存在 → 直接 `Migrate()` 建库到最新；随后写入 `SyncState` 单行与默认设置。
- **升级**：启动时 `GetPendingMigrations()` 非空 → **先自动备份**当前库到 `backups\gallery-pre-migration-<时间戳>.db`（复制前 `PRAGMA wal_checkpoint(TRUNCATE)` 收敛 WAL），再应用迁移。
- **降级（新版→旧版）**：启动时比对 `__EFMigrationsHistory` 与程序集已知迁移，发现未知（更新）迁移 → 抛出明确异常并停止启动，**不删改任何数据**；提示用户升级应用或手工恢复备份。
- **迁移失败恢复**：备份先行保证可退；失败后应用不可用库 → 下次启动检测到未完成迁移会重新尝试；用户可选择从 `backups\` 恢复（恢复前再自动备份当前现场）。
- **写入规约**：所有迁移只允许"加表/加列/加索引/回填默认值"；**禁止**在迁移中删除用户数据列或改列语义，需要时走"新增列 + 数据回填 + 旧列停用"三步式。

## 5. 删除策略

| 对象 | 规则 |
|---|---|
| Products | **永不删除**（含下架型号、无图型号）；唯一"消失"途径是合集/事件等引用表的级联清理 |
| Favorites / CollectionItems | 用户主动移除 = 删除关联行；Products 本体不动 |
| Collections | 删除合集级联删除其 CollectionItems（外键 CASCADE），不影响 Products/Favorites |
| UsageEvents | 只增；超过保留上限（默认 100）由清理服务删最旧行 |
| SearchHistories | 去重上限（默认 50），超出删最旧；用户可整体清空 |
| SyncRuns/SyncChanges | 留痕保留；后续若需瘦身按"保留最近 N 轮"归档删除，禁止删除仍在引用区间外的产品信息 |
| 图片文件 | 不因任何数据库操作删除；缩略图可随时全量重建 |

## 6. 数据恢复设计

1. **损坏检测**：每次启动先用原生连接跑 `PRAGMA quick_check`；结果非 `ok` → 抛出损坏异常，**应用绝不自动修复/删除/覆盖库文件**；
2. **用户确认流程**：损坏 → 明确错误页（错误摘要 + 备份列表）→ 用户三选一：
   - 从指定备份恢复（执行前自动把当前损坏库改名移入 `backups\corrupted-<时间戳>.db` 留证）；
   - 从 Seed Package 重建官方数据（用户数据表随之新建为空）；
   - 暂不处理（只读模式进入，允许导出）；
3. **自动备份时机**：迁移前（见 §4）、恢复前、以及设置中的周期备份；备份 = `wal_checkpoint(TRUNCATE)` 后整文件复制到 `backups\`；
4. **恢复动作 = 换文件**：`gallery.db` 被备份文件替换前必须先保留现场，且全程在应用明确提示下进行。

## 7. 与仓库/图片的关系

- 图片为独立文件（`images\`），库中仅存元数据；`ImagePath` 一律为相对路径，展示/导出时经 `DatabasePaths` 解析，避免绝对路径迁移问题；
- `aux.` 保留名型号：`ImageFileName` 存真实名，`ImagePath` 指向安全名（`aux_.…`），映射规则见 `FileNameRules`；
- 数据库与图片目录同根（`%LOCALAPPDATA%\MijiaProductGallery\`），备份/恢复/Seed 重建按目录整体对待。
