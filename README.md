# mijia-product-gallery

Windows 原生米家产品素材图库 / 产品图管理器（WinUI 3）。

- 浏览 / 搜索 / 筛选 / 排序米家百科产品示例样图
- 原生复制到剪贴板、原生文件拖拽到 Explorer / Photoshop / PowerPoint 等
- 收藏、合集、随机浏览、使用统计、最近使用
- 后台增量同步米家百科，自动识别新增 / 下架 / 换图 / 归类变化 / ID 复用
- 本地 SQLite + 原图 + 缩略图缓存，完整离线可用，支持备份恢复

## 仓库关系

| 仓库 | 职责 |
|---|---|
| [iop666/mijia-product-icons](https://github.com/iop666/mijia-product-icons) | 米家产品数据/图片快照（纯数据仓库，本应用只读） |
| **iop666/mijia-product-gallery**（本仓库） | Windows 应用 |

数据规则依据：TIOPWXNL-mijia-product-icons Skill。运行时链路：Seed Package 首启导入 → 本地 SQLite + Images → 后台 API 增量同步；不依赖用户机器上的任何 Git 仓库。

## 状态

Phase 0（架构设计与环境确认）已完成，见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) 与 [docs/RISKS.md](docs/RISKS.md)。代码尚未开始，按 Phase 1（Core 模型与数据规则）→ Phase 2（SQLite）→ Phase 3（图片系统）→ Phase 4（同步引擎）→ UI 的顺序推进。

## 开发（规划）

- .NET 10 LTS + Windows App SDK 1.8.x（WinUI 3，unpackaged + SelfContained）
- SQLite + EF Core · System.Text.Json · SkiaSharp（缩略图）· xUnit
- `tools/SeedPackTool`：开发期把 mijia-product-icons 快照转成 `seed-<date>.zip` 初始数据包

## 版权

产品图片版权归小米公司及相关产品厂商所有，仅用于个人学习、研究与软件测试，请勿商用。
