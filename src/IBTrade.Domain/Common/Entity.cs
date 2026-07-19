namespace IBTrade.Domain.Common;

/// <summary>
/// 实体基类：所有领域实体的共同父类，统一主键与审计字段。
/// </summary>
public abstract class Entity
{
    public long Id { get; set; }

    /// <summary>创建时间（UTC）</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>最后更新时间（UTC）</summary>
    public DateTime? UpdatedAtUtc { get; set; }
}
