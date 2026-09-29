# mijia-product-gallery

Windows 原生米家产品素材图库 / 产品图管理器（WinUI 3）。

- 浏览 / 搜索 / 筛选 / 排序米家百科产品示例样图
- 分页 / 连续滚动两种浏览模式，页码直接跳转与完整键盘操作
- 原生复制到剪贴板、原生文件拖拽到 Explorer / Photoshop / PowerPoint 等
- 收藏、合集、随机浏览、使用统计、最近使用
- 后台增量同步米家百科，自动识别新增 / 下架 / 换图 / 归类变化 / ID 复用
- 本地 SQLite + 原图 + 缩略图缓存，完整离线可用，支持备份恢复
- 浅色 / 深色 / 灰色三主题实时切换，高 DPI 与无障碍支持

## 仓库关系

| 仓库 | 职责 |
|---|---|
| [iop666/mijia-product-icons](https://github.com/iop666/mijia-product-icons) | 米家产品数据/图片快照（纯数据仓库，本应用只读） |
| **iop666/mijia-product-gallery**（本仓库） | Windows 应用 |

数据规则依据：TIOPWXNL-mijia-product-icons Skill。运行时链路：Seed Package 首启导入 → 本地 SQLite + Images → 后台 API 增量同步；不依赖用户机器上的任何 Git 仓库。

## 开发

- .NET 10 LTS + Windows App SDK 2.5.x（WinUI 3，unpackaged + SelfContained）
- SQLite + EF Core · System.Text.Json · SkiaSharp（缩略图）· xUnit
- `tools/SeedPackTool`：开发期把 mijia-product-icons 快照转成 `seed-<date>.zip` 初始数据包

## License

The source code of Mijia Product Gallery is licensed under the
[MIT License](LICENSE).

### Third-Party Content

This repository contains the application source code only. It does not
include bundled Xiaomi/Mijia product images or a product-data snapshot.

The application may retrieve product information and images from
third-party services at runtime. Such third-party content is not covered
by this project's MIT License and remains subject to the rights and terms
of the respective copyright or trademark owners.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for details.

### 数据与图片版权

运行时从米家百科获取的产品信息与示例样图，版权归小米公司及相关权利人所有，仅用于个人学习、研究与软件测试，请勿商用。
