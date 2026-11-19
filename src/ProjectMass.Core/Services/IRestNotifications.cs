namespace ProjectMass.Services;

public interface IRestNotifications
{
    bool Enabled { get; }
    Task<bool> RequestAsync();
    void Update(DateTimeOffset? deadline);
    void NotifyElapsed(DateTimeOffset deadline);
}
