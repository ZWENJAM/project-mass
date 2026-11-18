using Android.OS;
using Android.Webkit;
using Microsoft.Maui.ApplicationModel;
using WebView = Android.Webkit.WebView;

namespace ProjectMass.Mobile;

internal sealed class CameraChromeClient(WebChromeClient? original) : WebChromeClient
{
    private PermissionRequest? pending;

    public override bool OnShowFileChooser(WebView? webView, global::Android.Webkit.IValueCallback? filePathCallback,
        FileChooserParams? fileChooserParams) =>
        original?.OnShowFileChooser(webView, filePathCallback, fileChooserParams) ?? false;

    public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg) =>
        original?.OnCreateWindow(view, isDialog, isUserGesture, resultMsg) ?? false;

    public override void OnPermissionRequest(PermissionRequest? request)
    {
        if (request is null) return;
        var origin = request.Origin;
        if (origin?.Scheme != "https" || origin.Host is not ("0.0.0.1" or "0.0.0.0") ||
            request.GetResources()?.Contains(PermissionRequest.ResourceVideoCapture) != true || pending is not null)
        {
            request.Deny();
            return;
        }
        pending = request;
        _ = GrantAsync(request);
    }

    public override void OnPermissionRequestCanceled(PermissionRequest? request)
    {
        if (ReferenceEquals(pending, request)) pending = null;
    }

    private async Task GrantAsync(PermissionRequest request)
    {
        var granted = false;
        try { granted = await Permissions.RequestAsync<Permissions.Camera>() == PermissionStatus.Granted; }
        catch (PermissionException) { }
        if (!ReferenceEquals(pending, request)) return;
        pending = null;
        if (granted) request.Grant([PermissionRequest.ResourceVideoCapture]);
        else request.Deny();
    }
}
