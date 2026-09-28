# 风险清单（Phase 0）

状态图例：开放 / 缓解中 / 已缓解 / 已接受。评审基准：2026-09-28。

| ID | 风险 | 影响 | 概率 | 等级 | 缓解措施 | 状态 |
|---|---|---|---|---|---|---|
| R01 | **全机无 .NET SDK**（PATH/注册表/安装目录均空；`~/.dotnet` 仅孤儿 sentinel）；Build Tools 2026 未装任何托管组件，其 MSBuild 无法构建 SDK 风格项目 | 阻塞 Phase 1 编译 | 已证实 | 高 | 提案 `winget install Microsoft.DotNet.SDK.10`（待批准，不擅自执行）；批准后复验 `dotnet --info` | **开放（待批准）** |
| R02 | 锁死 .NET 8 的机械决策：.NET 8 LTS 2026-11-10 到期（距今日 6 周） | 新项目落在临期运行时 | 已规避 | 高 | 已按实测改选 .NET 10 LTS + Windows App SDK 1.8.x（支持 net10.0 TFM） | 已缓解 |
| R03 | Windows App SDK 1.8.x 的小版本漂移 / unpackaged+SelfContained 构建问题（XAML 编译、部署） | Phase 1 首个编译验收受阻 | 中 | 高 | Phase 1 第一个里程碑即"空壳 App build+run"；若 dotnet CLI 构建受阻，回退用 BuildTools 18.8 MSBuild + 补装托管组件 | 开放 |
| R04 | `realIcon` URL 含 `&amp;` HTML 实体（实测发现，Skill 未记载） | 图片下载 403/签名失败 | 高（不处理必现） | 中 | URL 规范化管线：HTML 解码 → path percent-encode；用真实 URL 夹具单测 | 已缓解（设计内） |
| R05 | 产品自带 `ptId` ≠ 分类 `ptId`，若误用会导致整库归类错误 | 数据正确性（第一原则） | 高（不处理必现） | 高 | 归类唯一依据 = byCategory 列表成员关系；基准测试冻结 2026-09-28 全量归类 | 已缓解（设计内） |
| R06 | CDN Content-Type 谎报（text/plain）+ 非 ASCII URL | 扩展名误判 / 请求异常 | 高 | 中 | 文件头魔数判定（PNG/JPEG/GIF87a/89a/RIFF-WEBP）+ path percent-encode | 已缓解（设计内） |
| R07 | `aux.*` Windows 保留设备名：应用数据目录同样无法创建 | 同步中断/图片丢失 | 必现（2 个型号） | 中 | DB 存真实名，磁盘安全名 `aux_.` 映射，访问走 ImagePath；单测覆盖 | 已缓解（设计内） |
| R08 | ID 复用判定含主观性，误判会覆盖/丢失旧图（违反 Skill 红线） | 历史数据丢失 | 中 | 高 | 严格按 Skill §4 顺序纯函数化；"拿不准按 ID 复用"；判定依据入 SyncChanges；基准测试 | 已缓解（设计内） |
| R09 | 首启全量下载 10,546 图（≈581MB）耗时/流量 | 首次体验差、失败率高 | 高 | 中 | Seed Package 优先（Release 附件），导入幂等可断点；API 仅增量 | 已缓解（设计内） |
| R10 | 正式软件误依赖本机 icons 仓库路径 | 用户机器上不可运行 | 设计期 | 高 | 运行时只认 Seed Package 与 API；icons 仓库仅开发期三用途（架构文档 §0） | 已缓解（设计内） |
| R11 | 同步覆盖用户数据（收藏/计数/历史） | 用户数据丢失 | 设计期 | 高 | 官方/用户表结构性分离 + 官方列白名单 UPDATE；同步引擎无用户表写权限 | 已缓解（设计内） |
| R12 | 1 万+图虚拟化滚动卡顿/内存持续增长 | 核心体验不达标 | 中 | 高 | ItemsRepeater+虚拟化布局；磁盘缩略图 + LRU 解码缓存（预算制）；Phase 18/23 专项（1000/5000/10000/30000） | 开放 |
| R13 | SQLite 库损坏/迁移失败 | 数据不可用 | 低 | 高 | 启动 integrity_check；迁移前自动备份；三选项恢复（备份/重建索引/Seed 重开）；绝不静默删库 | 已缓解（设计内） |
| R14 | 同步中途失败产生"半残"状态 | 数据口径破坏 | 中 | 高 | 变更集事务化；先文件后 DB、失败回滚改名；sha 幂等续传 | 已缓解（设计内） |
| R15 | CSV 解析（UTF-8 BOM、引号字段、特殊字符型号名） | 种子导入错行 | 中 | 中 | 正规 CSV 解析器；字段级单测；导入后计数核对 manifest | 已缓解（设计内） |
| R16 | FTS5 依赖与 EF Core 兼容性 | 搜索降级 | 低 | 低 | 首版索引+LIKE（万行级足够），ISearchService 接口预留升级位 | 已接受 |
| R17 | 两个仓库分工漂移（应用代码混入 icons 仓库、或应用直接改仓库文件） | 数据仓库口径破坏 | 设计期 | 高 | 修正规则 2/3 写入架构文档 §0；应用对 icons 仓库只读；seed 工具独立于运行时 | 已缓解（设计内） |
| R18 | gh 已登录 iop666 而新建远端仓库尚未创建 | 无备份远端 | 已证实 | 低 | 待批准后 `gh repo create iop666/mijia-product-gallery`（公开/私有由用户定） | 开放（待批准） |

## 待批准事项汇总

1. `winget install Microsoft.DotNet.SDK.10`（R01；不安装则 Phase 1 无法编译）；
2. `gh repo create iop666/mijia-product-gallery`（R18）；
3. 两项均批准后，Phase 0 即视为完全收口，进入 Phase 1（Core 模型 + ProductCompareRules 基准测试）。
