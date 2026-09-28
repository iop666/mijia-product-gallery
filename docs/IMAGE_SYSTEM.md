# 本地图片资源系统 · IMAGE_SYSTEM.md

> 适用：mijia-product-gallery 图片目录管理、原图生命周期、.old 链、缩略图缓存
> 数据规范依据：TIOPWXNL-mijia-product-icons Skill §3/§4（扩展名按文件头判定、.old 链、aux 保留名）
> 实现位置：`Infrastructure/Images/`（`ImageStore`、`ThumbnailService`）+ `Core/Rules/ImageHeaderRules`

---

## 1. 目录布局（冻结）

```
%LOCALAPPDATA%\MijiaProductGallery\
├─ database\gallery.db
├─ images\                     原图，一张一个文件，永不变质
│   ├─ zhimi.heater.za1.png
│   ├─ chuangmi.camera.029a02.old.png      ID 复用历史（第 1 代）
│   ├─ chuangmi.camera.029a02.old1.png     第 2 代
│   └─ aux_.aircondition.hc1.png           保留名安全映射（真实名 aux.aircondition.hc1.png）
├─ thumbnails\                 缩略图缓存，可随时全删重建
│   └─ zhimi.heater.za1.a1b2c3d4.webp      {安全主名}.{sha8}.webp
├─ backups\
└─ logs\
```

## 2. 命名与 ImagePath 规则（冻结）

| 层 | 值 | 示例 |
|---|---|---|
| `ImageFileName`（数据库） | **官网真实文件名**（含真实扩展名、真实 `aux.` 前缀） | `aux.aircondition.hc1.png` |
| 磁盘文件名（images\ 内） | **Windows 安全名**：保留设备名主名后加 `_` | `aux_.aircondition.hc1.png` |
| `ImagePath`（数据库） | 相对数据根的路径，`/` 分隔，一律指向磁盘安全名 | `images/aux_.aircondition.hc1.png` |

- **扩展名以内容为准**：URL 或来源给出的扩展名不可信（该 CDN 常返回 `text/plain`）。存储扩展名由文件头判定结果决定，与来源不一致时自动纠正（如来源 `.png` 实为 JPEG → 存 `model.jpg`）。可识别格式：PNG / JPEG / GIF / WEBP（魔数表见 §6）。
- 保留名判定按主名（首个 `.` 之前）不区分大小写：`AUX/CON/PRN/NUL/COM1-9/LPT1-9`。

## 3. 原图生命周期（冻结）

所有写入路径都是同一条安全链，**禁止任何绕过验证的直接覆盖**：

```
输入流
 ↓ 内存缓冲（上限 20 MB，超限拒绝）
 ↓ 空文件检查（0 字节拒绝）
 ↓ 文件头魔数判定格式（Unknown 拒绝）
 ↓ 完整解码校验（伪图/截断/损坏在此拒绝）
 ↓ 计算 SHA256 + 尺寸 + 字节数
 ↓ 写临时文件（同目录 .tmp-<随机>）
 ↓ File.Move 原子落位（同卷原子）
 ↓ 返回元数据（调用方随后写数据库）
```

验证失败 → 删除临时文件并抛出异常，**既有原图保证一个字节不变**。

三个入口（与对比规则一一对应，Phase 4 同步引擎只做映射）：

| 入口 | 对应变更 | 磁盘行为 |
|---|---|---|
| `StoreNewAsync(model, stream)` | 新增型号 / 无图补图 | 原子新文件落位；同 SHA 已存在 → 返回 Unchanged；存在但 SHA 不同 → 按"验证后替换"处理（数据库无原图记录，无从留史） |
| `ReplaceAsync(model, currentFile, stream)` | 官方换图（NameChanged/ImageChanged） | 同 SHA → Unchanged；SHA 不同 → 新图经安全链**原地替换**原文件名，旧图不留存 |
| `ReplaceWithHistoryAsync(model, currentFile, stream)` | ID 复用 | 同 SHA → Unchanged；SHA 不同 → 旧图 `File.Move` 进 .old 链链尾，新图占用原文件名；返回 `ReplacedByOldFileName` |

## 4. .old 链（冻结，与 Skill §4 一致）

链式追加、永不洗牌，`.old` 后缀无数字者最早；新被替换的图取最大代数 +1：

```
初次入库              device.png
第 1 次 ID 复用       device.old.png      + device.png（新）
第 2 次 ID 复用       device.old.png
                      device.old1.png     + device.png（新）
第 3 次 ID 复用       device.old.png
                      device.old1.png
                      device.old2.png     + device.png（新）
```

- 代数扫描基于磁盘现有 `{主名}.old*{扩展名}` 文件（`FileNameRules.GetNextOldGeneration`）；
- aux 模型的链条使用**安全名**（`aux_.xxx.old.png`），数据库 `ImageFileName` 始终保留真实名；
- 下架、换图、缩略图清理均**不会**触碰 .old 历史图。

## 5. 缩略图系统

- **命名**：`thumbnails\{安全主名}.{sha8}.webp`，sha8 = SHA256 前 8 位十六进制 → **SHA 绑定**：原图更换（新 SHA）自动指向新缩略图名，旧文件由清理流程删除；
- **生成**：SkiaSharp 解码原图 → 最长边 > 480px 等比缩到 480（可配置）→ WebP 质量 80（可配置）→ 临时文件 + 原子落位；
- **自愈**：命中缓存时校验缩略图可解码，损坏（截断/乱写）→ 删除重建；
- **可重建**：整目录可随时清空，重建只读原图，原图零风险；
- **失败语义**：原图缺失/不可解码 → 明确异常，绝不生成空白缩略图冒充成功。

## 6. 校验规则

| 检查 | 规则 |
|---|---|
| 空文件 | 0 字节拒绝 |
| PNG | `89 50 4E 47 0D 0A 1A 0A` |
| JPEG | `FF D8 FF` |
| GIF | `GIF87a` / `GIF89a` |
| WEBP | `RIFF` @0 且 `WEBP` @8 |
| 完整性 | 头部合法后必须完整解码成功（拦住伪图、截断、坏块） |
| 上限 | 20 MB（超出拒绝，正常样图 ≈ 0.1–0.5 MB） |

## 7. 测试矩阵（Phase 3 验收范围）

| 类别 | 用例 |
|---|---|
| 格式 | PNG / JPEG / GIF / WEBP 全部通过并给出正确格式与尺寸 |
| 扩展名纠正 | JPEG 内容 + `.png` 来源名 → 落盘 `.jpg` |
| 拒绝 | 0 字节、伪图（PNG 魔数 + 随机字节）、截断 PNG、超限 |
| 生命周期 | 首次入库、重复下载同 SHA（Unchanged）、换图替换（无 .old）、ID 复用（进 .old）、`.old→.old1→.old2` 链、aux 全链安全名 |
| 原图不可变 | 验证失败后原文件字节级不变 |
| 缩略图 | 按 sha8 落位、命中缓存、SHA 变更换名、损坏自愈重建、全程原图不变 |

## 8. 与数据库的衔接

图片系统只管磁盘与字节，**不写数据库**；返回的 `ImageStoreResult`（ImageFileName / ImagePath / 格式 / 尺寸 / 字节数 / SHA256 / 旧图去向）由同步引擎（Phase 4）与 Seed 导入（Phase 5）经仓储的官方列白名单落库，保持"文件先行、元数据随后"的一致顺序。
