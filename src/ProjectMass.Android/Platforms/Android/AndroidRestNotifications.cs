using Android.App;
using Android.Content;
using ProjectMass.Services;

namespace ProjectMass.Mobile;

internal sealed class AndroidRestNotifications : IRestNotifications
{
    private static Context Context => global::Android.App.Application.Context;
    private static NotificationManager? Notifications => Context.GetSystemService(Context.NotificationService) as NotificationManager;
    internal const string DeadlineKey = "rest_notification_deadline";
    internal const string ChannelId = "rest_timer";
    private const int NotificationId = 10;

    public bool Enabled => Notifications is { } manager && manager.AreNotificationsEnabled()
        && manager.GetNotificationChannel(ChannelId)?.Importance != NotificationImportance.None;

    public async Task<bool> RequestAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
        return Enabled;
    }

    public void Update(DateTimeOffset? deadline)
    {
        if (Context.GetSystemService(Context.AlarmService) is not AlarmManager alarms) return;
        using var intent = new Intent(Context, typeof(RestNotificationReceiver));
        using var operation = PendingIntent.GetBroadcast(Context, NotificationId, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        if (operation is null) return;
        if (!Enabled || deadline is null)
        {
            Preferences.Default.Remove(DeadlineKey);
            alarms.Cancel(operation);
            Notifications?.Cancel(NotificationId);
            return;
        }

        var milliseconds = deadline.Value.ToUnixTimeMilliseconds();
        Preferences.Default.Set(DeadlineKey, milliseconds);
        Notifications?.Cancel(NotificationId);
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarms.CanScheduleExactAlarms())
            alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, milliseconds, operation);
        else
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, milliseconds, operation);
    }

    public void NotifyElapsed(DateTimeOffset deadline) =>
        MainThread.BeginInvokeOnMainThread(() => Deliver(Context, deadline.ToUnixTimeMilliseconds()));

    internal static void Deliver(Context context, long deadline)
    {
        if (deadline <= 0 || deadline != Preferences.Default.Get(DeadlineKey, 0L)
            || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < deadline) return;
        Preferences.Default.Remove(DeadlineKey);
        if (Notifications is not { } manager || !manager.AreNotificationsEnabled()) return;
        using var channel = new NotificationChannel(ChannelId, "Trainingspausen", NotificationImportance.Default);
        manager.CreateNotificationChannel(channel);
        using var launch = new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop);
        using var openApp = PendingIntent.GetActivity(context, NotificationId, launch, PendingIntentFlags.Immutable);
        using var notification = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)!
            .SetContentTitle("Trainingspause vorbei")!
            .SetContentText("Du kannst mit deinem Training weitermachen.")!
            .SetContentIntent(openApp)!
            .SetAutoCancel(true)!
            .Build();
        manager.Notify(NotificationId, notification);
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class RestNotificationReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null)
            AndroidRestNotifications.Deliver(context, Preferences.Default.Get(AndroidRestNotifications.DeadlineKey, 0L));
    }
}
