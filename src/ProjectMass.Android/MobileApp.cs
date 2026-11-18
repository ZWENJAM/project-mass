using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ProjectMass.Mobile;

public sealed class MobileApp : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(ProjectMass.App) });
        view.BlazorWebViewInitialized += (_, args) =>
        {
            var platform = args.WebView;
            // The permission callback can outlive the tap that requested the muted camera preview.
            if (platform.Settings is { } settings) settings.MediaPlaybackRequiresUserGesture = false;
            platform.SetWebChromeClient(new CameraChromeClient(platform.WebChromeClient));
        };
        var page = new ContentPage { Content = view, SafeAreaEdges = SafeAreaEdges.All };
        page.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#F2F3F5"), Color.FromArgb("#15181C"));
        return new Window(page);
    }
}
