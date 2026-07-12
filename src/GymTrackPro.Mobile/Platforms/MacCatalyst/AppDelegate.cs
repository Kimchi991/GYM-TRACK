using Foundation;
using UIKit;

namespace GymTrackPro.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        if (!AppleBackupExclusion.Apply(out var errorMessage))
        {
            Console.Error.WriteLine($"CRITICAL: Local cache backup exclusion failed; launch aborted. {errorMessage}");
            return false;
        }

        return base.FinishedLaunching(application, launchOptions);
    }
}
