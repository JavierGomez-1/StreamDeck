using Ahsoka.Core;
using Ahsoka.Services.Bluetooth;
using Ahsoka.Services.Device;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ahsoka.CS.StreamDeck;

public sealed class BluetoothConnectionManager
{
    private readonly object syncRoot = new();
    private BluetoothServiceClient? bluetoothClient;
    private DeviceServiceClient? deviceClient;
    private int adapterIndex = -1;
    private bool started;
    private string startupError = "";

    public List<BluetoothConnectionProfile> GetPairedDevices()
    {
        if (OperatingSystem.IsWindows())
            return new List<BluetoothConnectionProfile>();

        if (!EnsureStarted(out _))
            return BluetoothCtlDevices("paired-devices");

        try
        {
            var devices = bluetoothClient!.RequestPairedDevices(adapterIndex);
            return devices.PairedDevices
                .Where(device => !string.IsNullOrWhiteSpace(device.DeviceMacAddress))
                .Select(device => new BluetoothConnectionProfile
                {
                    Id = SafeId(device.DeviceMacAddress),
                    Label = string.IsNullOrWhiteSpace(device.DeviceName) ? device.DeviceMacAddress : device.DeviceName,
                    MacAddress = device.DeviceMacAddress,
                    CssClass = "purple"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"Bluetooth paired list failed: {ex.Message}");
            return BluetoothCtlDevices("paired-devices");
        }
    }

    public List<BluetoothConnectionProfile> GetDiscoveredDevices()
    {
        if (OperatingSystem.IsWindows())
            return new List<BluetoothConnectionProfile>();

        if (!EnsureStarted(out _))
            return BluetoothCtlDevices("devices");

        return BluetoothCtlDevices("devices");
    }

    public DeckActionResult StartDiscovery()
    {
        if (OperatingSystem.IsWindows())
            return new DeckActionResult(false, "Bluetooth directo solo corre en S70.");

        if (!EnsureStarted(out string error))
            return BluetoothCtl("bluetoothctl power on >/dev/null 2>&1; timeout 8 bluetoothctl scan on >/dev/null 2>&1 &", "Buscando dispositivos Bluetooth.");

        try
        {
            bluetoothClient!.StartDiscovery(adapterIndex);
            return new DeckActionResult(true, "Buscando dispositivos Bluetooth.");
        }
        catch (Exception ex)
        {
            AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"Bluetooth scan failed: {ex.Message}");
            return new DeckActionResult(false, $"Bluetooth: {ex.Message}");
        }
    }

    public Task<DeckActionResult> ConnectAsync(string macAddress)
    {
        return Task.Run(() => Connect(macAddress));
    }

    private DeckActionResult Connect(string macAddress)
    {
        macAddress = (macAddress ?? "").Trim();
        if (string.IsNullOrWhiteSpace(macAddress))
            return new DeckActionResult(false, "Configura la direccion Bluetooth.");

        if (OperatingSystem.IsWindows())
            return new DeckActionResult(false, "Bluetooth directo solo corre en S70.");

        if (!EnsureStarted(out string error))
            return BluetoothCtl(BuildConnectCommand(macAddress), $"Bluetooth conectado: {macAddress}");

        try
        {
            var devices = bluetoothClient!.RequestPairedDevices(adapterIndex);
            var device = devices.PairedDevices.FirstOrDefault(item =>
                string.Equals(item.DeviceMacAddress, macAddress, StringComparison.OrdinalIgnoreCase));

            if (device == null)
                return BluetoothCtl(BuildConnectCommand(macAddress), $"Bluetooth conectado: {macAddress}");

            var request = new ProfileServicesRequest { DeviceInfo = device };
            request.RequestedServiceTypes.Add(BluetoothProfileServiceType.AudioSinkProfileService);
            bluetoothClient.StartProfileServices(request);
            return new DeckActionResult(true, $"Bluetooth conectado: {device.DeviceName}");
        }
        catch (Exception ex)
        {
            AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"Bluetooth connect failed for {macAddress}: {ex.Message}");
            return new DeckActionResult(false, $"Bluetooth: {ex.Message}");
        }
    }

    private bool EnsureStarted(out string error)
    {
        error = "";
        if (OperatingSystem.IsWindows())
        {
            error = "Bluetooth directo solo corre en S70.";
            return false;
        }

        lock (syncRoot)
        {
            if (started)
                return true;

            if (!string.IsNullOrWhiteSpace(startupError))
            {
                error = startupError;
                return false;
            }

            try
            {
                deviceClient = new DeviceServiceClient();
                AhsokaRuntime.Default.StartEndPoints(deviceClient);

                var request = new DeviceInfoRequest { FeatureSearch = SupportedFeatures.BluetoothClassic };
                var devices = deviceClient.GetDevices(request);
                var onboardDevice = devices.Devices.FirstOrDefault();
                if (onboardDevice != null)
                    deviceClient.EnableDevice(onboardDevice);

                bluetoothClient = new BluetoothServiceClient();
                AhsokaRuntime.Default.StartEndPoints(bluetoothClient);

                var configuration = bluetoothClient.GetAdapterConfiguration();
                if (configuration.AdapterConfigurations.Count == 0)
                {
                    startupError = "No encontre adaptador Bluetooth.";
                    error = startupError;
                    return false;
                }

                adapterIndex = configuration.AdapterConfigurations.First().AdapterIndex;
                var adapterState = bluetoothClient.RequestAdapterState(adapterIndex);
                if (!adapterState.IsEnabled)
                    bluetoothClient.EnableAdapter(adapterIndex, BluetoothOperationMode.ModeApplication);

                adapterState.IsEnabled = true;
                adapterState.IsDiscoverable = true;
                adapterState.IsPairable = true;
                bluetoothClient.SetAdapterState(adapterState);
                started = true;
                return true;
            }
            catch (Exception ex)
            {
                startupError = $"Bluetooth: {ex.Message}";
                error = startupError;
                AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"Bluetooth start failed: {ex.Message}");
                return false;
            }
        }
    }

    private static string SafeId(string value)
    {
        string safe = new string((value ?? "bt").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "bt" : safe.ToLowerInvariant();
    }

    private static DeckActionResult BluetoothCtl(string command, string successMessage)
    {
        if (OperatingSystem.IsWindows())
            return new DeckActionResult(false, "Bluetooth directo solo corre en S70.");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
                return new DeckActionResult(false, "No se pudo ejecutar bluetoothctl.");

            if (!process.WaitForExit(15000))
                return new DeckActionResult(true, successMessage);

            string output = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
            if (process.ExitCode == 0)
                return new DeckActionResult(true, string.IsNullOrWhiteSpace(output) ? successMessage : FirstLine(output), output);

            return new DeckActionResult(false, string.IsNullOrWhiteSpace(output) ? "bluetoothctl fallo." : FirstLine(output), output);
        }
        catch (Exception ex)
        {
            return new DeckActionResult(false, $"bluetoothctl: {ex.Message}");
        }
    }

    private static List<BluetoothConnectionProfile> BluetoothCtlDevices(string listCommand)
    {
        if (OperatingSystem.IsWindows())
            return new List<BluetoothConnectionProfile>();

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"bluetoothctl power on >/dev/null 2>&1; bluetoothctl {listCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null || !process.WaitForExit(5000))
                return new List<BluetoothConnectionProfile>();

            string output = process.StandardOutput.ReadToEnd();
            return output
                .Replace("\r", "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseBluetoothCtlDevice)
                .Where(profile => profile != null)
                .Select(profile => profile!)
                .GroupBy(profile => profile.MacAddress, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        catch (Exception ex)
        {
            AhsokaLogging.LogMessage(AhsokaVerbosity.Low, $"bluetoothctl devices failed: {ex.Message}");
            return new List<BluetoothConnectionProfile>();
        }
    }

    private static BluetoothConnectionProfile? ParseBluetoothCtlDevice(string line)
    {
        const string prefix = "Device ";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string rest = line[prefix.Length..].Trim();
        int firstSpace = rest.IndexOf(' ');
        string mac = firstSpace < 0 ? rest : rest[..firstSpace];
        string label = firstSpace < 0 ? mac : rest[(firstSpace + 1)..].Trim();
        mac = SanitizeMac(mac);
        if (string.IsNullOrWhiteSpace(mac))
            return null;

        return new BluetoothConnectionProfile
        {
            Id = SafeId(mac),
            Label = string.IsNullOrWhiteSpace(label) ? mac : label,
            MacAddress = mac,
            CssClass = "purple"
        };
    }

    private static string BuildConnectCommand(string macAddress)
    {
        string mac = SanitizeMac(macAddress);
        return $"bluetoothctl power on >/dev/null 2>&1; bluetoothctl agent on >/dev/null 2>&1; bluetoothctl default-agent >/dev/null 2>&1; bluetoothctl trust {mac} >/dev/null 2>&1; bluetoothctl connect {mac} || (bluetoothctl pair {mac}; bluetoothctl trust {mac}; bluetoothctl connect {mac})";
    }

    private static string SanitizeMac(string macAddress)
    {
        return new string((macAddress ?? "").Where(c => char.IsLetterOrDigit(c) || c == ':' || c == '-').ToArray());
    }

    private static string FirstLine(string value)
    {
        string line = value.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        line = line.Trim();
        return line.Length <= 100 ? line : line[..99] + ".";
    }
}
