namespace MijiaProductGallery.Core.Interfaces;

/// <summary>一个可用的数据库备份文件（位于 backups 目录）。</summary>
public sealed record BackupEntry
{
    /// <summary>备份文件名（含扩展名，不含目录）。</summary>
    public required string FileName { get; init; }

    /// <summary>文件大小（字节）。</summary>
    public required long SizeBytes { get; init; }

    /// <summary>备份创建时间（Unix 秒）。</summary>
    public required long CreatedUnix { get; init; }
}

/// <summary>备份文件校验失败（损坏、缺表或不是 SQLite 文件）。</summary>
public sealed class BackupValidationException(string message) : InvalidOperationException(message);

/// <summary>
/// 数据库备份/恢复契约：
/// 备份 = VACUUM INTO 单文件一致性快照；恢复 = 校验备份 + 当前库安全快照 + 原子替换。
/// 图片目录不在备份范围（独立目录，体积大；恢复数据库后缺失图片按占位显示）。
/// </summary>
public interface IBackupService
{
    /// <summary>立即创建数据库备份（VACUUM INTO，含全部表与用户数据）。</summary>
    Task<BackupEntry> CreateBackupAsync(string? label = null, CancellationToken cancellationToken = default);

    /// <summary>列出 backups 目录下全部备份（按创建时间降序）。</summary>
    Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>删除指定备份文件。</summary>
    Task DeleteBackupAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从备份恢复：先校验备份完整性，再为当前库创建安全快照，最后原子替换。
    /// 任一步失败抛出异常且当前库保持不变。
    /// </summary>
    Task RestoreAsync(string fileName, CancellationToken cancellationToken = default);
}
