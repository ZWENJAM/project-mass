using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.LifecycleEvents;
using ProjectMass.Services;

namespace ProjectMass.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<MobileApp>();
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<BackupLifecycle>();
        builder.Services.AddSingleton<SafDocuments>();
        builder.Services.AddSingleton<BackupFiles>();
        builder.Services.AddSingleton<IWebDavTransport, NativeWebDavTransport>();
        builder.Services.AddSingleton<IPlatformFiles, AndroidPlatformFiles>();
        builder.Services.AddSingleton<IRestNotifications, AndroidRestNotifications>();
        builder.Services.AddScoped<INativeBackups, AndroidBackups>();
        builder.Services.AddProjectMassCore(_ => new HttpClient(new PackagedSeedHandler())
        {
            BaseAddress = new Uri("https://project-mass.local/")
        }, new ProjectMassOptions(Native: true, DisplayVersion: Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString));

        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android =>
            android.OnPause(_ => backupLifecycle?.Pause())));

        var app = builder.Build();
        backupLifecycle = app.Services.GetRequiredService<BackupLifecycle>();
        return app;
    }

    private static BackupLifecycle? backupLifecycle;
}
