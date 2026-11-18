using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectMass.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplySystemTheme();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ApplySystemTheme();
    }

    private void ApplySystemTheme()
    {
        if (Window is not { } window) return;
        var dark = (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && window.InsetsController is { } controller)
        {
            const int lightStatusBars = 8;
            controller.SetSystemBarsAppearance(dark ? 0 : lightStatusBars, lightStatusBars);
        }
        else
        {
#pragma warning disable CS0618, CA1422 // System bar appearance API on Android 8–10.
            window.DecorView.SystemUiVisibility = dark ? StatusBarVisibility.Visible : (StatusBarVisibility)SystemUiFlags.LightStatusBar;
#pragma warning restore CS0618, CA1422
        }
    }

#pragma warning disable CS0672, CS0618, CA1422 // SAF result delivery works on all supported Android versions.
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        IPlatformApplication.Current?.Services.GetRequiredService<SafDocuments>().Complete(requestCode, resultCode, data);
    }
#pragma warning restore CS0672, CS0618, CA1422
}
