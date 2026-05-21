using Ahsoka.Core;
using Ahsoka.Core.Dispatch;
using Ahsoka.Core.Drawing;
using Ahsoka.Core.Drawing.Base;
using Ahsoka.Core.Drawing.Utility;
using Ahsoka.Services.System;

namespace Ahsoka.CS.StreamDeck;

public class Program
{
    public static void Main()
    {
        AhsokaLogging.LoggingVerbosity = AhsokaVerbosity.Medium | AhsokaVerbosity.Performance;

        var configStore = new DeckConfigStore(GetDeckConfigPath());
        var catalogStore = new AppCatalogStore(GetConfigPath("apps.json"));
        var connectivityStore = new ConnectivityConfigStore(GetConfigPath("connections.json"));
        var themeStore = new UiThemeStore(GetConfigPath("theme.json"));
        var pcCompanionClient = new PcCompanionClient(GetConfigPath("pc.json"));
        var wifiConnectionManager = new WiFiConnectionManager();
        var bluetoothConnectionManager = new BluetoothConnectionManager();
        var actionRunner = new DeckActionRunner(pcCompanionClient, wifiConnectionManager, bluetoothConnectionManager);
        var ui = new StreamDeckUi(configStore, catalogStore, connectivityStore, themeStore, pcCompanionClient, wifiConnectionManager, bluetoothConnectionManager, actionRunner);

        SystemServiceClient systemClient = new();
        Dispatcher.Default.AddStartupItem(systemClient);

        DrawingWindow window = ui.Start();

        ApplicationContext.ReleaseStartup();
        Dispatcher.Default.StartAndRun(window.Invoke);

        window.ShowAndRun();

        Dispatcher.Default.Stop();
        ApplicationContext.Exit();
    }

    private static string GetDeckConfigPath()
    {
        return GetConfigPath("deck.json");
    }

    private static string GetConfigPath(string fileName)
    {
        string seedPath = Path.Combine(AppContext.BaseDirectory, fileName);

        if (OperatingSystem.IsWindows())
            return seedPath;

        string configPath = Path.Combine(AppContext.BaseDirectory, "config", fileName);
        if (!File.Exists(configPath) && File.Exists(seedPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.Copy(seedPath, configPath);
        }

        return configPath;
    }
}
