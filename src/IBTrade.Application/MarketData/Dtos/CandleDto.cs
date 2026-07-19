namespace IBTrade.Application.MarketData.Dtos;

/// <summary>对外暴露的单根历史 K 线（清洗后的数据，与 TWS 实现解耦）。</summary>
public record CandleDto(DateTime Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);
