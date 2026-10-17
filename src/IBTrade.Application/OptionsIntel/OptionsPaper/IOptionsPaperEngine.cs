namespace IBTrade.Application.OptionsIntel.OptionsPaper;

public interface IOptionsPaperEngine
{
    bool IsRunning { get; }
    OptionsPaperStatus GetStatus();
    void UpdateSettings(OptionsPaperSettings settings);
    void Start();
    void Stop();
    Task TickAsync(CancellationToken ct = default);
    void LoadPersisted();
}
