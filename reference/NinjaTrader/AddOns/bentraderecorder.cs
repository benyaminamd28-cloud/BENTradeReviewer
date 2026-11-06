// BEN TRADE RECORDER - V3 VIDEO ENGINE REBUILD
    // Base: hardened V3 source SHA256 B77ED554A60BF23FC7176D8F44114D9A1EC45247C70BFA44949F0903B055EAE7
    // Video engine only: unified multi-monitor capture, GPU composition, segmented output.
    // Trading, execution, journal, database and analytics logic are not intentionally changed.

    #region Using declarations


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.Custom.AddOns
{
    #region Win32 Multi-Monitor Detection Engine

    public class MonitorInfo
    {
        public int Index { get; set; }
        public string DeviceName { get; set; }
        public string FriendlyName { get; set; }
        public string DeviceId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }

        public string SelectionId =>
            !string.IsNullOrWhiteSpace(DeviceId)
                ? DeviceId
                : DeviceName;

        public string Description =>
            $"{(string.IsNullOrWhiteSpace(FriendlyName) ? "Unknown monitor" : FriendlyName)} " +
            $"— Display {Index + 1} [{DeviceName}] — {Width}x{Height} at ({X},{Y}) " +
            (IsPrimary ? "— Primary" : string.Empty);
    }

    internal static class DisplayManager
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DISPLAY_DEVICE
        {
            public int cb;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            public int StateFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        public delegate bool MonitorEnumProc(
            IntPtr hMonitor,
            IntPtr hdcMonitor,
            ref RECT lprcMonitor,
            IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumProc lpfnEnum,
            IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(
            IntPtr hMonitor,
            ref MONITORINFOEX lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool EnumDisplayDevices(
            string lpDevice,
            uint iDevNum,
            ref DISPLAY_DEVICE lpDisplayDevice,
            uint dwFlags);

        public const int MONITORINFOF_PRIMARY = 1;

        public static List<MonitorInfo> GetMonitors()
        {
            var list = new List<MonitorInfo>();
            int idx = 0;

            EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
                {
                    var mi = new MONITORINFOEX
                    {
                        cbSize = Marshal.SizeOf(typeof(MONITORINFOEX))
                    };

                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        int width = Math.Abs(mi.rcMonitor.Right - mi.rcMonitor.Left);
                        int height = Math.Abs(mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                        bool primary =
                            (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;

                        string friendlyName;
                        string deviceId;
                        GetFriendlyMonitorDetails(
                            mi.szDevice,
                            out friendlyName,
                            out deviceId);

                        int outputIndex = ParseOutputIndex(mi.szDevice, idx);
                        idx++;

                        list.Add(new MonitorInfo
                        {
                            Index = outputIndex,
                            DeviceName = mi.szDevice ?? $"Display{idx}",
                            FriendlyName = friendlyName,
                            DeviceId = deviceId,
                            X = mi.rcMonitor.Left,
                            Y = mi.rcMonitor.Top,
                            Width = width,
                            Height = height,
                            IsPrimary = primary
                        });
                    }

                    return true;
                },
                IntPtr.Zero);

            if (list.Count == 0)
            {
                list.Add(new MonitorInfo
                {
                    Index = 0,
                    DeviceName = "PrimaryDisplay",
                    FriendlyName = "Primary display (Windows did not return a monitor model)",
                    DeviceId = string.Empty,
                    X = 0,
                    Y = 0,
                    Width = 1920,
                    Height = 1080,
                    IsPrimary = true
                });
            }

            return list
                .OrderBy(x => x.Index)
                .ToList();
        }

        private static int ParseOutputIndex(
            string deviceName,
            int fallback)
        {
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                int start = deviceName.LastIndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    string suffix = deviceName.Substring(start + "DISPLAY".Length);
                    int number;
                    if (int.TryParse(suffix, out number) && number > 0)
                        return number - 1;
                }
            }

            return fallback;
        }

        private static void GetFriendlyMonitorDetails(
            string displayDeviceName,
            out string friendlyName,
            out string deviceId)
        {
            friendlyName = string.Empty;
            deviceId = string.Empty;

            for (uint monitorIndex = 0; monitorIndex < 8; monitorIndex++)
            {
                var monitorDevice = new DISPLAY_DEVICE
                {
                    cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE))
                };

                if (!EnumDisplayDevices(
                    displayDeviceName,
                    monitorIndex,
                    ref monitorDevice,
                    0))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(deviceId) &&
                    !string.IsNullOrWhiteSpace(monitorDevice.DeviceID))
                {
                    deviceId = monitorDevice.DeviceID.Trim();
                }

                if (string.IsNullOrWhiteSpace(friendlyName) &&
                    !string.IsNullOrWhiteSpace(monitorDevice.DeviceString) &&
                    !monitorDevice.DeviceString.Equals(
                        "Generic PnP Monitor",
                        StringComparison.OrdinalIgnoreCase))
                {
                    friendlyName = monitorDevice.DeviceString.Trim();
                }

                if (!string.IsNullOrWhiteSpace(friendlyName))
                    break;
            }

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                string registryName = ReadFriendlyNameFromRegistry(deviceId);
                if (!string.IsNullOrWhiteSpace(registryName))
                    friendlyName = registryName;
            }

            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                string hardwareId = GetMonitorHardwareId(deviceId);
                friendlyName = string.IsNullOrWhiteSpace(hardwareId)
                    ? displayDeviceName
                    : "Monitor " + hardwareId;
            }
        }

        private static string ReadFriendlyNameFromRegistry(string deviceId)
        {
            try
            {
                string[] parts = (deviceId ?? string.Empty).Split('\\');
                if (parts.Length < 3 ||
                    !parts[0].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                string displayPath =
                    @"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1];

                using (RegistryKey displayKey = Registry.LocalMachine.OpenSubKey(displayPath))
                {
                    if (displayKey == null)
                        return string.Empty;

                    foreach (string instanceName in displayKey.GetSubKeyNames())
                    {
                        string instancePath = displayPath + "\\" + instanceName;
                        using (RegistryKey instance = Registry.LocalMachine.OpenSubKey(instancePath))
                        {
                            if (instance == null)
                                continue;

                            string friendly = instance.GetValue("FriendlyName") as string;
                            string parsedFriendly = ExtractFriendlyModelName(friendly);
                            if (!string.IsNullOrWhiteSpace(parsedFriendly))
                                return parsedFriendly;

                            using (RegistryKey parameters = instance.OpenSubKey("Device Parameters"))
                            {
                                byte[] edid = parameters?.GetValue("EDID") as byte[];
                                string edidName = ReadEdidMonitorName(edid);
                                if (!string.IsNullOrWhiteSpace(edidName))
                                    return edidName;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string ExtractFriendlyModelName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string name = value.Trim();
            int open = name.LastIndexOf('(');
            int close = name.LastIndexOf(')');
            if (open >= 0 && close > open)
                name = name.Substring(open + 1, close - open - 1).Trim();

            if (name.StartsWith("@", StringComparison.Ordinal))
                return string.Empty;

            return name.Equals("Generic Monitor", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : name;
        }

        private static string ReadEdidMonitorName(byte[] edid)
        {
            if (edid == null || edid.Length < 128)
                return string.Empty;

            for (int descriptor = 0; descriptor < 4; descriptor++)
            {
                int offset = 54 + descriptor * 18;
                if (offset + 18 > edid.Length ||
                    edid[offset] != 0 || edid[offset + 1] != 0 ||
                    edid[offset + 2] != 0 || edid[offset + 3] != 0xFC)
                    continue;

                string name = Encoding.ASCII.GetString(edid, offset + 5, 13)
                    .Trim('\0', '\r', '\n', ' ', '\t');
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            return string.Empty;
        }

        private static string GetMonitorHardwareId(string deviceId)
        {
            string[] parts = (deviceId ?? string.Empty).Split('\\');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                return string.Empty;

            return parts[1].Trim();
        }
    }

    #endregion

    #region Contract Specification

    public struct ContractSpec
    {
        public string DisplayName { get; set; }
        public string RootSymbol { get; set; }
        public double TickSize { get; set; }
        public double TickValue { get; set; }
        public string CurrencySymbol { get; set; }
        public string Source { get; set; }

        public bool IsValid
        {
            get
            {
                return TickSize > 0 && TickValue > 0 &&
                       !double.IsNaN(TickSize) && !double.IsInfinity(TickSize) &&
                       !double.IsNaN(TickValue) && !double.IsInfinity(TickValue) &&
                       !string.IsNullOrWhiteSpace(CurrencySymbol) &&
                       !string.IsNullOrWhiteSpace(Source) &&
                       !Source.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
            }
        }

        public ContractSpec(
            string displayName,
            string rootSymbol,
            double tickSize,
            double tickValue,
            string currencySymbol,
            string source)
        {
            DisplayName = displayName;
            RootSymbol = rootSymbol;
            TickSize = tickSize > 0 && !double.IsNaN(tickSize) && !double.IsInfinity(tickSize) ? tickSize : 0.0;
            TickValue = tickValue > 0 && !double.IsNaN(tickValue) && !double.IsInfinity(tickValue) ? tickValue : 0.0;
            CurrencySymbol = string.IsNullOrWhiteSpace(currencySymbol)
                ? string.Empty
                : currencySymbol;
            Source = string.IsNullOrWhiteSpace(source)
                ? "Unknown"
                : source;
        }
    }

    public static class ContractLookup
    {
        private static readonly Dictionary<string, ContractSpec> Table =
            new Dictionary<string, ContractSpec>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["MES"] = new ContractSpec(
                    "Micro E-mini S&P 500",
                    "MES",
                    0.25,
                    1.25,
                    "$",
                    "Catalog Fallback"),

                ["MNQ"] = new ContractSpec(
                    "Micro E-mini Nasdaq-100",
                    "MNQ",
                    0.25,
                    0.50,
                    "$",
                    "Catalog Fallback"),

                ["ES"] = new ContractSpec(
                    "E-mini S&P 500",
                    "ES",
                    0.25,
                    12.50,
                    "$",
                    "Catalog Fallback"),

                ["NQ"] = new ContractSpec(
                    "E-mini Nasdaq-100",
                    "NQ",
                    0.25,
                    5.00,
                    "$",
                    "Catalog Fallback"),

                ["YM"] = new ContractSpec(
                    "E-mini Dow",
                    "YM",
                    1.0,
                    5.00,
                    "$",
                    "Catalog Fallback"),

                ["RTY"] = new ContractSpec(
                    "E-mini Russell 2000",
                    "RTY",
                    0.1,
                    5.00,
                    "$",
                    "Catalog Fallback"),

                ["6A"] = new ContractSpec(
                    "Australian Dollar",
                    "6A",
                    0.00005,
                    5.00,
                    "$",
                    "Catalog Fallback"),

                ["6B"] = new ContractSpec(
                    "British Pound",
                    "6B",
                    0.0001,
                    6.25,
                    "$",
                    "Catalog Fallback"),

                ["6C"] = new ContractSpec(
                    "Canadian Dollar",
                    "6C",
                    0.00005,
                    5.00,
                    "$",
                    "Catalog Fallback"),

                ["6E"] = new ContractSpec(
                    "Euro FX",
                    "6E",
                    0.00005,
                    6.25,
                    "$",
                    "Catalog Fallback"),

                ["CL"] = new ContractSpec(
                    "Crude Oil",
                    "CL",
                    0.01,
                    10.00,
                    "$",
                    "Catalog Fallback"),

                ["GC"] = new ContractSpec(
                    "Gold",
                    "GC",
                    0.10,
                    10.00,
                    "$",
                    "Catalog Fallback"),

                ["MGC"] = new ContractSpec(
                    "Micro Gold",
                    "MGC",
                    0.10,
                    1.00,
                    "$",
                    "Catalog Fallback"),

                ["SI"] = new ContractSpec(
                    "Silver",
                    "SI",
                    0.005,
                    25.00,
                    "$",
                    "Catalog Fallback"),

                ["HG"] = new ContractSpec(
                    "Copper",
                    "HG",
                    0.0005,
                    12.50,
                    "$",
                    "Catalog Fallback"),

                ["UB"] = new ContractSpec(
                    "Ultra U.S. Treasury Bond",
                    "UB",
                    0.03125,
                    31.25,
                    "$",
                    "Catalog Fallback"),

                ["ZB"] = new ContractSpec(
                    "U.S. Treasury Bond",
                    "ZB",
                    0.03125,
                    31.25,
                    "$",
                    "Catalog Fallback"),

                ["ZN"] = new ContractSpec(
                    "10-Year U.S. T-Note",
                    "ZN",
                    0.015625,
                    15.625,
                    "$",
                    "Catalog Fallback"),

                ["ZF"] = new ContractSpec(
                    "5-Year U.S. T-Note",
                    "ZF",
                    0.0078125,
                    7.8125,
                    "$",
                    "Catalog Fallback"),

                ["ZT"] = new ContractSpec(
                    "2-Year U.S. T-Note",
                    "ZT",
                    0.0078125,
                    15.625,
                    "$",
                    "Catalog Fallback")
            };

        public static ContractSpec Resolve(
            Instrument instrument,
            string fallbackFullName)
        {
            string root = ExtractRoot(instrument, fallbackFullName);

            // Unknown contracts are deliberately invalid. Never manufacture a
            // plausible-looking PnL/R using generic tick defaults.
            double tickSize = 0.0;
            double tickValue = 0.0;
            string currency = string.Empty;
            string displayName = root;
            string source = "Unknown";

            ContractSpec catalogSpec;

            if (Table.TryGetValue(root, out catalogSpec))
            {
                displayName = catalogSpec.DisplayName;
                tickSize = catalogSpec.TickSize;
                tickValue = catalogSpec.TickValue;
                currency = catalogSpec.CurrencySymbol;
                source = catalogSpec.Source;
            }

            if (instrument != null &&
                instrument.MasterInstrument != null)
            {
                double masterTick =
                    instrument.MasterInstrument.TickSize;

                double masterPointValue =
                    instrument.MasterInstrument.PointValue;

                if (masterTick > 0 && masterPointValue > 0 &&
                    !double.IsNaN(masterTick) && !double.IsInfinity(masterTick) &&
                    !double.IsNaN(masterPointValue) && !double.IsInfinity(masterPointValue))
                {
                    tickSize = masterTick;
                    tickValue = masterPointValue * masterTick;
                    source = "NinjaTrader MasterInstrument";
                }
                else if (tickSize > 0 && tickValue > 0)
                {
                    source = "Catalog Fallback (MasterInstrument incomplete)";
                }

                if (!string.IsNullOrWhiteSpace(
                    instrument.MasterInstrument.Name))
                {
                    displayName =
                        instrument.MasterInstrument.Name;
                }
            }

            return new ContractSpec(
                displayName,
                root,
                tickSize,
                tickValue,
                currency,
                source);
        }

        private static string ExtractRoot(
            Instrument instrument,
            string fallback)
        {
            if (instrument != null &&
                instrument.MasterInstrument != null &&
                !string.IsNullOrWhiteSpace(
                    instrument.MasterInstrument.Name))
            {
                return instrument.MasterInstrument.Name
                    .Trim()
                    .ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                string clean = fallback.Trim();

                int spaceIndex =
                    clean.IndexOf(' ');

                if (spaceIndex > 0)
                    clean =
                        clean.Substring(0, spaceIndex);

                return clean.ToUpperInvariant();
            }

            return "UNKNOWN";
        }
    }

    #endregion

    #region Settings

    [Serializable]
    public class BENTradeRecorderSettings
    {
        public bool EnableRecorder { get; set; } = true;

        public string SaveFolder { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments),
                "BEN_TradeRecorder");

        public string FFmpegPath { get; set; } = string.Empty;

		public int PreTradeSeconds { get; set; } = 60;
        public int PostTradeSeconds { get; set; } = 5;
        public int FrameRate { get; set; } = 30;
        public int VideoQuality { get; set; } = 21;
        public string SelectedEncoder { get; set; } = "Auto";
        public string SelectedMonitors { get; set; } = "ALL";
        public string LayoutMode { get; set; } = "Horizontal";

        private static readonly string ConfigPath =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8",
                "BENTradeRecorder_Config.json");

        public static BENTradeRecorderSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json =
                        File.ReadAllText(ConfigPath);

                    var result =
                        BENJsonHelper.Deserialize(json);

                    if (result != null)
                        return result;
                }
            }
            catch
            {
                // Settings failure must not crash NinjaTrader.
            }

            var defaults =
                new BENTradeRecorderSettings();

            defaults.Save();

            return defaults;
        }

        public void Save()
        {
            try
            {
                string dir =
                    Path.GetDirectoryName(ConfigPath);

                if (!string.IsNullOrEmpty(dir) &&
                    !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(
                    ConfigPath,
                    BENJsonHelper.Serialize(this));
            }
            catch
            {
                // Non-critical settings persistence failure.
            }
        }
    }

    internal static class BENJsonHelper
    {
        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        public static string Serialize(
            BENTradeRecorderSettings s)
        {
            return "{\n" +
                   $"  \"EnableRecorder\": {s.EnableRecorder.ToString().ToLowerInvariant()},\n" +
                   $"  \"PreTradeSeconds\": {s.PreTradeSeconds},\n" +
                   $"  \"SaveFolder\": \"{Escape(s.SaveFolder)}\",\n" +
                   $"  \"FFmpegPath\": \"{Escape(s.FFmpegPath)}\",\n" +
                   $"  \"PostTradeSeconds\": {s.PostTradeSeconds},\n" +
                   $"  \"FrameRate\": {s.FrameRate},\n" +
                   $"  \"VideoQuality\": {s.VideoQuality},\n" +
                   $"  \"SelectedEncoder\": \"{Escape(s.SelectedEncoder)}\",\n" +
                   $"  \"SelectedMonitors\": \"{Escape(s.SelectedMonitors)}\",\n" +
                   $"  \"LayoutMode\": \"{Escape(s.LayoutMode)}\"\n" +
                   "}";
        }

        public static BENTradeRecorderSettings Deserialize(
            string json)
        {
            var s =
                new BENTradeRecorderSettings();

            if (string.IsNullOrWhiteSpace(json))
                return s;

            string[] lines =
                json.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (string raw in lines)
            {
                string line =
                    raw.Trim().TrimEnd(',');

                try
                {
                    if (line.StartsWith(
                        "\"EnableRecorder\":"))
                    {
                        s.EnableRecorder =
                            bool.Parse(
                                line.Split(':')[1].Trim());
                    }
                    else if (line.StartsWith(
                        "\"PreTradeSeconds\":"))
                    {
                        s.PreTradeSeconds =
                            Math.Max(
                                60,
                                Math.Min(
                                    600,
                                    int.Parse(
                                        line.Split(':')[1].Trim(),
                                        CultureInfo.InvariantCulture)));
                    }
                    else if (line.StartsWith(
                        "\"SaveFolder\":"))
                    {
                        s.SaveFolder =
                            ExtractStringValue(line);
                    }
                    else if (line.StartsWith(
                        "\"FFmpegPath\":"))
                    {
                        s.FFmpegPath =
                            ExtractStringValue(line);
                    }
                    else if (line.StartsWith(
                        "\"PostTradeSeconds\":"))
                    {
                        s.PostTradeSeconds =
                            int.Parse(
                                line.Split(':')[1].Trim(),
                                CultureInfo.InvariantCulture);
                    }
                    else if (line.StartsWith(
                        "\"FrameRate\":"))
                    {
                        s.FrameRate =
                            int.Parse(
                                line.Split(':')[1].Trim(),
                                CultureInfo.InvariantCulture);
                    }
                    else if (line.StartsWith(
                        "\"VideoQuality\":"))
                    {
                        s.VideoQuality =
                            int.Parse(
                                line.Split(':')[1].Trim(),
                                CultureInfo.InvariantCulture);
                    }
                    else if (line.StartsWith(
                        "\"SelectedEncoder\":"))
                    {
                        s.SelectedEncoder =
                            ExtractStringValue(line);
                    }
                    else if (line.StartsWith(
                        "\"SelectedMonitors\":"))
                    {
                        s.SelectedMonitors =
                            ExtractStringValue(line);
                    }
                    else if (line.StartsWith(
                        "\"LayoutMode\":"))
                    {
                        s.LayoutMode =
                            ExtractStringValue(line);
                    }
                }
                catch
                {
                    // Preserve defaults for malformed individual settings.
                }
            }

            return s;
        }

        private static string ExtractStringValue(
            string line)
        {
            int colon =
                line.IndexOf(':');

            if (colon < 0)
                return string.Empty;

            string value =
                line.Substring(colon + 1)
                    .Trim();

            if (value.StartsWith("\""))
                value = value.Substring(1);

            if (value.EndsWith("\""))
                value =
                    value.Substring(
                        0,
                        value.Length - 1);

            return value
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }
    }

    #endregion

    #region FFmpeg

    public static class FFmpegFinder
    {
        public static string ResolveFFmpegPath(
            string customPath)
        {
            if (!string.IsNullOrWhiteSpace(customPath) &&
                File.Exists(customPath))
            {
                return customPath;
            }

            string path1 =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments),
                    "FFmpeg",
                    "ffmpeg.exe");

            if (File.Exists(path1))
                return path1;

            const string path2 =
                @"C:\ffmpeg\bin\ffmpeg.exe";

            if (File.Exists(path2))
                return path2;

            const string path3 =
                @"C:\ffmpeg\ffmpeg.exe";

            if (File.Exists(path3))
                return path3;

            string pathEnv =
                Environment.GetEnvironmentVariable("PATH");

            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string p in
                    pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string candidate =
                            Path.Combine(
                                p.Trim(),
                                "ffmpeg.exe");

                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch
                    {
                    }
                }
            }

            return string.Empty;
        }
    }

    public static class FFmpegEncoderDetector
    {
        private static readonly ConcurrentDictionary<string, List<string>> Verified =
            new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, byte> Probing =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        public static void WarmUpAsync(string ffmpegPath)
        {
            if (string.IsNullOrWhiteSpace(ffmpegPath) ||
                !File.Exists(ffmpegPath) ||
                Verified.ContainsKey(ffmpegPath) ||
                !Probing.TryAdd(ffmpegPath, 0))
                return;

            Task.Run(() =>
            {
                try
                {
                    var list = new List<string>();
                    if (Probe(ffmpegPath, "h264_nvenc")) list.Add("h264_nvenc");
                    if (Probe(ffmpegPath, "h264_qsv")) list.Add("h264_qsv");
                    if (Probe(ffmpegPath, "h264_amf")) list.Add("h264_amf");
                    list.Add("libx264");
                    Verified[ffmpegPath] = list;
                }
                catch
                {
                }
                finally
                {
                    byte ignored;
                    Probing.TryRemove(ffmpegPath, out ignored);
                }
            });
        }

        public static bool ProbeEncoderNow(string ffmpegPath, string encoder)
        {
            string ignored;
            return ProbeEncoderNow(ffmpegPath, encoder, out ignored);
        }

        public static bool ProbeEncoderNow(string ffmpegPath, string encoder, out string diagnostic)
        {
            diagnostic = string.Empty;
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                diagnostic = "FFmpeg executable does not exist: " + (ffmpegPath ?? "<null>");
                return false;
            }
            if (string.IsNullOrWhiteSpace(encoder))
            {
                diagnostic = "Encoder name is empty.";
                return false;
            }
            return Probe(ffmpegPath, encoder, out diagnostic);
        }

        private static bool Probe(string ffmpegPath, string encoder)
        {
            string ignored;
            return Probe(ffmpegPath, encoder, out ignored);
        }

        private static bool Probe(string ffmpegPath, string encoder, out string diagnostic)
        {
            diagnostic = string.Empty;
            Process process = null;
            try
            {
                string encoderOptions = string.Equals(encoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                    ? " -preset p2"
                    : string.Empty;

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments =
                        "-hide_banner -nostdin -loglevel error " +
                        "-f lavfi -i testsrc2=size=1280x720:rate=30 " +
                        "-frames:v 30 -an -pix_fmt yuv420p " +
                        "-c:v " + encoder + encoderOptions + " -f null -",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                process = Process.Start(psi);
                if (process == null)
                {
                    diagnostic = "Process.Start returned null.";
                    return false;
                }

                // Capture the actual FFmpeg stderr. The previous implementation drained
                // the pipe but discarded every line, hiding the real NVENC failure reason.
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20000))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch
                    {
                    }

                    string timeoutOutput = string.Empty;
                    try { timeoutOutput = stderrTask.GetAwaiter().GetResult(); } catch { }
                    diagnostic = "Encoder probe timed out after 20 seconds." +
                        (string.IsNullOrWhiteSpace(timeoutOutput) ? string.Empty : " FFmpeg: " + CompactDiagnostic(timeoutOutput));
                    return false;
                }

                string stderr = string.Empty;
                try { stderr = stderrTask.GetAwaiter().GetResult(); } catch (Exception readError)
                {
                    diagnostic = "Could not read FFmpeg stderr: " + readError.Message;
                }

                if (process.ExitCode != 0)
                {
                    diagnostic = "FFmpeg exit code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) +
                        (string.IsNullOrWhiteSpace(stderr) ? ". No stderr was returned." : ". " + CompactDiagnostic(stderr));
                    return false;
                }

                diagnostic = "PASS: " + encoder + " encoded 30 test frames.";
                return true;
            }
            catch (Exception ex)
            {
                diagnostic = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (process != null)
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }

        private static string CompactDiagnostic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string compact = value.Trim();
            const int maxLength = 2400;
            if (compact.Length > maxLength)
                compact = compact.Substring(compact.Length - maxLength, maxLength);
            return compact;
        }

        public static string ResolveFast(string requested, string ffmpegPath)
        {
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
                return "libx264";

            List<string> verified;
            if (!Verified.TryGetValue(ffmpegPath, out verified))
            {
                WarmUpAsync(ffmpegPath);
                return "libx264";
            }

            string choice = requested ?? "Auto";
            if (choice.Equals("Auto", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(choice))
            {
                if (verified.Contains("h264_nvenc")) return "h264_nvenc";
                if (verified.Contains("h264_qsv")) return "h264_qsv";
                if (verified.Contains("h264_amf")) return "h264_amf";
                return "libx264";
            }

            if (choice.IndexOf("nvenc", StringComparison.OrdinalIgnoreCase) >= 0 && verified.Contains("h264_nvenc")) return "h264_nvenc";
            if (choice.IndexOf("qsv", StringComparison.OrdinalIgnoreCase) >= 0 && verified.Contains("h264_qsv")) return "h264_qsv";
            if (choice.IndexOf("amf", StringComparison.OrdinalIgnoreCase) >= 0 && verified.Contains("h264_amf")) return "h264_amf";
            if (choice.IndexOf("x264", StringComparison.OrdinalIgnoreCase) >= 0) return "libx264";
            return "libx264";
        }
    }
    /// <summary>
    /// Strict GPU-resident capture/composition path.
    /// Pixel operations stay in D3D11/CUDA hardware frames; the host CPU still
    /// performs process control, timestamps exposed by FFmpeg, and filesystem I/O.
    /// This path is intentionally NVIDIA-only until equivalent D3D11/QSV/AMF
    /// composition graphs can be runtime-probed end-to-end on the target machine.
    /// </summary>
    internal static class BENGpuCompositePipeline
    {
        public const int OutputWidth = 1920;
        public const int OutputHeight = 1080;

        public static bool IsCpuSelection(string selected)
        {
            string value = selected ?? string.Empty;
            return value.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("x264", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsAutoSelection(string selected)
        {
            return string.IsNullOrWhiteSpace(selected) ||
                   selected.IndexOf("Auto", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsNvencSelection(string selected)
        {
            return selected != null &&
                   (selected.IndexOf("NVENC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    selected.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    selected.Equals("GPU", StringComparison.OrdinalIgnoreCase));
        }

        public static string GetUnsupportedSelectionReason(string selected)
        {
            if (selected != null &&
                (selected.IndexOf("QSV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 selected.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "Intel QSV is not enabled for the strict multi-monitor GPU compositor in this build. " +
                       "The complete D3D11-to-QSV composition path has not been validated; no CPU fallback was performed.";
            }

            if (selected != null &&
                (selected.IndexOf("AMF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 selected.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "AMD AMF is not enabled for the strict multi-monitor GPU compositor in this build. " +
                       "A GPU-resident composition path has not been validated; no CPU fallback was performed.";
            }

            return "Unsupported encoder selection. Choose Auto, NVIDIA NVENC, or CPU x264.";
        }

        public static bool TryProbe(
            string ffmpegPath,
            List<MonitorInfo> monitors,
            int fps,
            int quality,
            string layoutMode,
            Action<string> log,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                diagnostic = "FFmpeg executable not found.";
                return false;
            }

            if (monitors == null || monitors.Count < 1 || monitors.Count > 4)
            {
                diagnostic = "The GPU compositor supports one to four monitors.";
                return false;
            }

            string encoderDiagnostic;
            if (!FFmpegEncoderDetector.ProbeEncoderNow(ffmpegPath, "h264_nvenc", out encoderDiagnostic))
            {
                diagnostic = "The h264_nvenc runtime probe failed. " + encoderDiagnostic;
                log?.Invoke("[GPU ENCODER PROBE] FAIL | FFmpeg=" + ffmpegPath + " | " + diagnostic);
                return false;
            }
            log?.Invoke("[GPU ENCODER PROBE] " + encoderDiagnostic + " | FFmpeg=" + ffmpegPath);

            string args = BuildCommonArguments(monitors, fps, quality, layoutMode) +
                          " -frames:v 2 -f null -";
            return RunProbeProcess(ffmpegPath, args, TimeSpan.FromSeconds(18), log, out diagnostic);
        }

        public static string BuildSegmentArguments(
            List<MonitorInfo> monitors,
            int fps,
            int quality,
            string outputPattern,
            int segmentSeconds,
            string layoutMode)
        {
            int safeFps = Math.Max(15, Math.Min(60, fps));
            int safeQuality = Math.Max(14, Math.Min(35, quality));
            int safeSegmentSeconds = Math.Max(1, Math.Min(10, segmentSeconds));

            return BuildCommonArguments(monitors, safeFps, safeQuality, layoutMode) +
                   " -fps_mode cfr -r " + safeFps.ToString(CultureInfo.InvariantCulture) +
                   " -g " + (safeFps * safeSegmentSeconds).ToString(CultureInfo.InvariantCulture) +
                   " -keyint_min " + (safeFps * safeSegmentSeconds).ToString(CultureInfo.InvariantCulture) +
                   " -forced-idr 1 -force_key_frames \"expr:gte(t,n_forced*" + safeSegmentSeconds.ToString(CultureInfo.InvariantCulture) + ")\"" +
                   " -f segment -segment_format mp4" +
                   " -segment_time " + safeSegmentSeconds.ToString(CultureInfo.InvariantCulture) +
                   " -segment_time_delta 0.03 -reset_timestamps 1 -strftime 1 " +
                   Quote(outputPattern);
        }

        public static string BuildLiveArguments(
            List<MonitorInfo> monitors,
            int fps,
            int quality,
            string outputFile,
            string layoutMode)
        {
            int safeFps = Math.Max(15, Math.Min(60, fps));
            return BuildCommonArguments(monitors, safeFps, quality, layoutMode) +
                   " -fps_mode cfr -r " + safeFps.ToString(CultureInfo.InvariantCulture) +
                   " -g " + safeFps.ToString(CultureInfo.InvariantCulture) +
                   " -keyint_min " + safeFps.ToString(CultureInfo.InvariantCulture) +
                   " -forced-idr 1 -movflags +faststart " + Quote(outputFile);
        }

        public static string BuildClipArguments(
            string concatList,
            string stagedOutput,
            double trimOffsetSeconds,
            double durationSeconds,
            int fps,
            int quality)
        {
            int safeFps = Math.Max(15, Math.Min(60, fps));
            int safeQuality = Math.Max(14, Math.Min(35, quality));
            string offset = Math.Max(0, trimOffsetSeconds).ToString("F3", CultureInfo.InvariantCulture);
            string duration = Math.Max(0.1, durationSeconds).ToString("F3", CultureInfo.InvariantCulture);

            // NVDEC keeps decoding on the NVIDIA device; NVENC writes the final MP4.
            // No filtergraph is used here because the rolling segments are already composed.
            return "-y -hide_banner -nostdin -loglevel warning " +
                   // The input is an FFmpeg concat-demuxer manifest, not a media file.
                   // safe=0 is required because the manifest contains absolute Windows paths.
                   "-f concat -safe 0 -hwaccel cuda -hwaccel_output_format cuda -i " + Quote(concatList) +
                   " -ss " + offset + " -t " + duration + " -an" +
                   " -c:v h264_nvenc -preset p5 -tune hq -rc vbr -cq " + safeQuality +
                   " -b:v 0 -fps_mode cfr -r " + safeFps.ToString(CultureInfo.InvariantCulture) +
                   " -g " + safeFps.ToString(CultureInfo.InvariantCulture) +
                   " -movflags +faststart " + Quote(stagedOutput);
        }

        private static string BuildCommonArguments(
            List<MonitorInfo> monitors,
            int fps,
            int quality,
            string layoutMode)
        {
            string graph = BuildFilterGraph(monitors, fps, layoutMode);
            int safeQuality = Math.Max(14, Math.Min(35, quality));

            return "-y -hide_banner -nostdin -loglevel warning " +
                   // ddagrab requires a D3D11VA device context. CUDA is initialized
                   // separately and is derived from the duplicated D3D11 textures.
                   "-init_hw_device d3d11va=d3d11:0 " +
                   "-init_hw_device cuda=gpu:0,primary_ctx=1 -filter_hw_device d3d11 " +
                   "-filter_complex_threads 1 -filter_complex " + Quote(graph) +
                   " -map \"[outv]\" -an" +
                   " -c:v h264_nvenc -preset p5 -tune hq -rc vbr -cq " + safeQuality +
                   " -b:v 0 -profile:v high";
        }

        private static string BuildFilterGraph(List<MonitorInfo> monitors, int fps, string layoutMode)
        {
            if (monitors == null || monitors.Count < 1 || monitors.Count > 4)
                throw new ArgumentException("The GPU compositor accepts one to four monitors.", nameof(monitors));

            var graph = new StringBuilder(2048);
            var boxes = new List<Rectangle>(monitors.Count);

            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle box = GetCell(monitors.Count, i, layoutMode);
                boxes.Add(box);
                MonitorInfo monitor = monitors[i];
                int fitWidth;
                int fitHeight;
                FitInside(monitor.Width, monitor.Height, box.Width, box.Height, out fitWidth, out fitHeight);

                // DXGI output indices are derived from the Windows DISPLAYn ordering.
                // The end-to-end probe below is mandatory and prevents a silent
                // fallback when the selected adapter/output cannot be duplicated.
                graph.Append("ddagrab=output_idx=")
                    .Append(Math.Max(0, monitor.Index).ToString(CultureInfo.InvariantCulture))
                    .Append(":framerate=").Append(Math.Max(15, Math.Min(60, fps)).ToString(CultureInfo.InvariantCulture))
                    .Append(":draw_mouse=1,")
                    .Append("hwmap=derive_device=cuda:mode=read+direct,")
                    .Append("scale_cuda=").Append(fitWidth.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(fitHeight.ToString(CultureInfo.InvariantCulture))
                    .Append(":interp_algo=lanczos:format=nv12[v")
                    .Append(i.ToString(CultureInfo.InvariantCulture)).Append("];" );
            }

            graph.Append("color=c=black:s=")
                .Append(OutputWidth.ToString(CultureInfo.InvariantCulture)).Append('x')
                .Append(OutputHeight.ToString(CultureInfo.InvariantCulture))
                .Append(":r=").Append(Math.Max(15, Math.Min(60, fps)).ToString(CultureInfo.InvariantCulture))
                // Generate one black frame on the host, upload it once, then loop
                // references to the CUDA frame. Do not regenerate/upload a full
                // 1080p background on every output frame.
                .Append(",trim=end_frame=1,format=nv12,hwupload_cuda=device=0,loop=loop=-1:size=1:start=0[bg];");

            string current = "bg";
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle box = boxes[i];
                MonitorInfo monitor = monitors[i];
                int fitWidth;
                int fitHeight;
                FitInside(monitor.Width, monitor.Height, box.Width, box.Height, out fitWidth, out fitHeight);
                int x = box.X + Math.Max(0, (box.Width - fitWidth) / 2);
                int y = box.Y + Math.Max(0, (box.Height - fitHeight) / 2);
                string next = "mix" + i.ToString(CultureInfo.InvariantCulture);

                graph.Append('[').Append(current).Append("][v")
                    .Append(i.ToString(CultureInfo.InvariantCulture)).Append("]")
                    .Append("overlay_cuda=x=").Append(x.ToString(CultureInfo.InvariantCulture))
                    .Append(":y=").Append(y.ToString(CultureInfo.InvariantCulture))
                    .Append(":shortest=1[").Append(next).Append("];");
                current = next;
            }

            graph.Append('[').Append(current).Append("]null[outv]");
            return graph.ToString();
        }

        private static Rectangle GetCell(int count, int index, string layoutMode)
        {
            if (count == 1)
                return new Rectangle(0, 0, OutputWidth, OutputHeight);

            if (string.Equals(layoutMode, "Vertical", StringComparison.OrdinalIgnoreCase))
                return new Rectangle(0, index * (OutputHeight / count), OutputWidth, OutputHeight / count);

            if (count == 2)
                return new Rectangle(index * (OutputWidth / 2), 0, OutputWidth / 2, OutputHeight);

            return new Rectangle(
                (index % 2) * (OutputWidth / 2),
                (index / 2) * (OutputHeight / 2),
                OutputWidth / 2,
                OutputHeight / 2);
        }

        private static void FitInside(
            int sourceWidth,
            int sourceHeight,
            int boxWidth,
            int boxHeight,
            out int width,
            out int height)
        {
            int sw = Math.Max(2, sourceWidth);
            int sh = Math.Max(2, sourceHeight);
            double scale = Math.Min(boxWidth / (double)sw, boxHeight / (double)sh);
            width = Math.Max(2, Math.Min(boxWidth, (int)(Math.Floor(sw * scale / 2.0) * 2.0)));
            height = Math.Max(2, Math.Min(boxHeight, (int)(Math.Floor(sh * scale / 2.0) * 2.0)));
        }

        private static bool RunProbeProcess(
            string executable,
            string arguments,
            TimeSpan timeout,
            Action<string> log,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            Process process = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                process = Process.Start(psi);
                if (process == null)
                {
                    diagnostic = "FFmpeg did not start for the GPU graph probe.";
                    return false;
                }

                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    try { process.Kill(); process.WaitForExit(2000); } catch { }
                    diagnostic = "GPU graph probe timed out. Check Desktop Duplication access, adapter selection and CUDA/D3D11 interop.";
                    return false;
                }

                string stderr = stderrTask.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                {
                    diagnostic = string.IsNullOrWhiteSpace(stderr)
                        ? "FFmpeg rejected the GPU capture/composition/encoding graph (exit code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ")."
                        : stderr.Trim();
                    return false;
                }

                log?.Invoke("[GPU PIPELINE PROBE] PASS | ddagrab -> D3D11/CUDA direct map -> CUDA scale/overlay -> NVENC");
                return true;
            }
            catch (Exception ex)
            {
                diagnostic = ex.Message;
                return false;
            }
            finally
            {
                if (process != null)
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }

        public static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private struct Rectangle
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Width;
            public readonly int Height;

            public Rectangle(int x, int y, int width, int height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }
        }
    }

    public class VideoRecordingInstance : IDisposable
    {
        private Process _process;
        private readonly object _lock =
            new object();

        private volatile bool _recording;
        private volatile bool _gracefulStop;

        public bool IsRecording
        {
            get { return _recording; }
        }

        public DateTime RecordingStartTime
        {
            get;
            private set;
        }

        public DateTime RecordingStopTime
        {
            get;
            private set;
        }

        public bool Start(
            string ffmpegExe,
            string outputFilePath,
            int fps,
            int quality,
            string encoderOption,
            string selectedMonitors,
            string layoutMode,
            Action<string> log)
        {
            lock (_lock)
            {
                if (_recording)
                    return true;

                if (string.IsNullOrWhiteSpace(ffmpegExe) ||
                    !File.Exists(ffmpegExe))
                {
                    log?.Invoke(
                        "ERROR: FFmpeg executable not found.");

                    return false;
                }

                string selected = encoderOption ?? "Auto";
                bool useCpu = BENGpuCompositePipeline.IsCpuSelection(selected);
                bool useAuto = BENGpuCompositePipeline.IsAutoSelection(selected);
                if (!useCpu && !useAuto && !BENGpuCompositePipeline.IsNvencSelection(selected))
                {
                    log?.Invoke("[VIDEO] " + BENGpuCompositePipeline.GetUnsupportedSelectionReason(selected));
                    return false;
                }

                List<MonitorInfo> monitors = ResolveSelectedMonitors(selectedMonitors);
                if (monitors.Count == 0)
                {
                    log?.Invoke("[VIDEO] No configured monitor is connected.");
                    return false;
                }

                string encoder = "libx264";
                if (!useCpu)
                {
                    string probeDiagnostic;
                    bool gpuReady = BENGpuCompositePipeline.TryProbe(
                        ffmpegExe, monitors, fps, quality, layoutMode, log, out probeDiagnostic);
                    if (gpuReady)
                        encoder = "h264_nvenc";
                    else if (!useAuto)
                    {
                        log?.Invoke("[VIDEO GPU MODE REFUSED] " + probeDiagnostic);
                        return false;
                    }
                    else
                    {
                        log?.Invoke("[VIDEO AUTO MODE] GPU graph unavailable; switching explicitly to the CPU pipeline: " + probeDiagnostic);
                    }
                }

                return Launch(
                    ffmpegExe, outputFilePath, fps, quality, encoder,
                    selectedMonitors, layoutMode, log);
            }
        }

        private static List<MonitorInfo> ResolveSelectedMonitors(string selectedMonitors)
        {
            List<MonitorInfo> all = DisplayManager.GetMonitors();
            if (string.IsNullOrWhiteSpace(selectedMonitors) ||
                selectedMonitors.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                return all.OrderBy(x => x.Index).ToList();

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var indices = new HashSet<int>();
            foreach (string part in selectedMonitors.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = part.Trim();
                int index;
                if (int.TryParse(token, out index)) indices.Add(index);
                else if (!string.IsNullOrWhiteSpace(token)) wanted.Add(token);
            }

            return all.Where(m => indices.Contains(m.Index) || wanted.Contains(m.SelectionId) ||
                                  wanted.Contains(m.DeviceName) || wanted.Contains(m.FriendlyName))
                       .OrderBy(m => m.Index).ToList();
        }

        private bool Launch(
            string ffmpegExe,
            string outputFilePath,
            int fps,
            int quality,
            string encoder,
            string selectedMonitors,
            string layoutMode,
            Action<string> log)
        {
            try
            {
                Cleanup();

                string directory =
                    Path.GetDirectoryName(
                        outputFilePath);

                if (!string.IsNullOrEmpty(directory) &&
                    !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                List<MonitorInfo> target = ResolveSelectedMonitors(selectedMonitors);
                if (target.Count == 0)
                {
                    log?.Invoke("ERROR: None of the configured monitors are connected.");
                    return false;
                }

                string arguments = string.Equals(encoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                    ? BENGpuCompositePipeline.BuildLiveArguments(target, fps, quality, outputFilePath, layoutMode)
                    : BuildCommand(target, layoutMode, fps, encoder, quality, outputFilePath);

                var psi =
                    new ProcessStartInfo
                    {
                        FileName = ffmpegExe,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                var process =
                    new Process
                    {
                        StartInfo = psi,
                        EnableRaisingEvents = true
                    };

                process.ErrorDataReceived +=
                    (sender, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            log?.Invoke(
                                "[FFmpeg] " + e.Data);
                        }
                    };

                process.Exited +=
                    (sender, e) =>
                    {
                        lock (_lock)
                        {
                            if (_process == process)
                            {
                                _recording = false;

                                if (!_gracefulStop)
                                {
                                    log?.Invoke(
                                        "[FFmpeg] Unexpected process termination.");
                                }
                            }
                        }
                    };

                _gracefulStop = false;

                if (!process.Start())
                    return false;

                _process = process;

                try
                {
                    process.PriorityClass =
                        ProcessPriorityClass.BelowNormal;
                }
                catch
                {
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                RecordingStartTime =
                    DateTime.Now;

                _recording = true;

                log?.Invoke(
                    $"Recording Active (Encoder: {encoder}, FPS: {fps}, Quality: {quality}, Monitors: {target.Count})");

                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke(
                    "ERROR FFmpeg Launch: " +
                    ex.Message);

                Cleanup();
                return false;
            }
        }

        private string BuildCommand(
            List<MonitorInfo> monitors,
            string layoutMode,
            int fps,
            string encoder,
            int quality,
            string outputFilePath)
        {
            if (monitors == null || monitors.Count < 1 || monitors.Count > 4)
                throw new ArgumentException("CPU capture supports one to four selected monitors.", nameof(monitors));

            if (!string.Equals(encoder, "libx264", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The CPU capture path is restricted to libx264. GPU encoders must use the verified GPU graph.");

            int safeFps = Math.Max(15, Math.Min(60, fps));
            int safeQuality = Math.Max(14, Math.Min(35, quality));
            bool vertical = string.Equals(layoutMode, "Vertical", StringComparison.OrdinalIgnoreCase);
            int cellWidth = monitors.Count == 1 || vertical ? BENGpuCompositePipeline.OutputWidth : BENGpuCompositePipeline.OutputWidth / 2;
            int cellHeight = monitors.Count == 1 ? BENGpuCompositePipeline.OutputHeight :
                vertical ? BENGpuCompositePipeline.OutputHeight / monitors.Count :
                monitors.Count == 2 ? BENGpuCompositePipeline.OutputHeight : BENGpuCompositePipeline.OutputHeight / 2;

            var inputs = new StringBuilder(monitors.Count * 128);
            var filter = new StringBuilder(monitors.Count * 256 + 512);
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorInfo monitor = monitors[i];
                int width = Math.Max(2, monitor.Width & ~1);
                int height = Math.Max(2, monitor.Height & ~1);
                inputs.Append("-f gdigrab -framerate ")
                    .Append(safeFps.ToString(CultureInfo.InvariantCulture))
                    .Append(" -draw_mouse 1 -offset_x ")
                    .Append(monitor.X.ToString(CultureInfo.InvariantCulture))
                    .Append(" -offset_y ")
                    .Append(monitor.Y.ToString(CultureInfo.InvariantCulture))
                    .Append(" -video_size ").Append(width.ToString(CultureInfo.InvariantCulture))
                    .Append('x').Append(height.ToString(CultureInfo.InvariantCulture))
                    .Append(" -i desktop ");

                filter.Append('[').Append(i.ToString(CultureInfo.InvariantCulture)).Append(":v]")
                    .Append("scale=").Append(cellWidth.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(cellHeight.ToString(CultureInfo.InvariantCulture))
                    .Append(":force_original_aspect_ratio=decrease:flags=lanczos,")
                    .Append("pad=").Append(cellWidth.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(cellHeight.ToString(CultureInfo.InvariantCulture))
                    .Append(":(ow-iw)/2:(oh-ih)/2:color=black,")
                    .Append("setsar=1,format=yuv420p[v")
                    .Append(i.ToString(CultureInfo.InvariantCulture)).Append("];" );
            }

            if (monitors.Count == 1)
            {
                filter.Append("[v0]null[outv]");
            }
            else if (vertical)
            {
                for (int i = 0; i < monitors.Count; i++)
                    filter.Append("[v").Append(i.ToString(CultureInfo.InvariantCulture)).Append(']');
                filter.Append("vstack=inputs=").Append(monitors.Count.ToString(CultureInfo.InvariantCulture)).Append("[outv]");
            }
            else if (monitors.Count == 2)
            {
                filter.Append("[v0][v1]hstack=inputs=2[outv]");
            }
            else if (monitors.Count == 3)
            {
                filter.Append("color=c=black:s=").Append(cellWidth.ToString(CultureInfo.InvariantCulture))
                    .Append('x').Append(cellHeight.ToString(CultureInfo.InvariantCulture))
                    .Append(":r=").Append(safeFps.ToString(CultureInfo.InvariantCulture)).Append("[blank];")
                    .Append("[v0][v1]hstack=inputs=2[top];")
                    .Append("[v2][blank]hstack=inputs=2[bottom];")
                    .Append("[top][bottom]vstack=inputs=2[outv]");
            }
            else
            {
                filter.Append("[v0][v1]hstack=inputs=2[top];")
                    .Append("[v2][v3]hstack=inputs=2[bottom];")
                    .Append("[top][bottom]vstack=inputs=2[outv]");
            }

            string encoderArguments = "-c:v libx264 -preset ultrafast -crf " +
                safeQuality.ToString(CultureInfo.InvariantCulture) + " -pix_fmt yuv420p";
            return "-y -hide_banner -nostdin -loglevel warning " + inputs +
                "-filter_complex_threads 1 -filter_complex " + BENGpuCompositePipeline.Quote(filter.ToString()) +
                " -map \"[outv]\" -an " + encoderArguments +
                " -fps_mode cfr -r " + safeFps.ToString(CultureInfo.InvariantCulture) +
                " -g " + safeFps.ToString(CultureInfo.InvariantCulture) +
                " -keyint_min " + safeFps.ToString(CultureInfo.InvariantCulture) +
                " -movflags +faststart " + BENGpuCompositePipeline.Quote(outputFilePath);
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_recording || _process == null)
                    return;

                _gracefulStop = true;
                RecordingStopTime = DateTime.Now;

                try
                {
                    if (!_process.HasExited)
                    {
                        try
                        {
                            _process.StandardInput.WriteLine("q");
                            _process.StandardInput.Flush();
                        }
                        catch
                        {
                        }

                        if (!_process.WaitForExit(8000))
                        {
                            try
                            {
                                _process.Kill();
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    Cleanup();
                }
            }
        }

        private void Cleanup()
        {
            if (_process != null)
            {
                try
                {
                    _process.Dispose();
                }
                catch
                {
                }

                _process = null;
            }

            _recording = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    #endregion

    #region Raw Data Models

    public enum TradeDirection
    {
        Long,
        Short
    }

    public enum SessionState
    {
        Active,
        Closing,
        Completed
    }

    public class FillRecord
    {
        public string ExecutionId { get; }
        public DateTime Time { get; }
        public string AccountName { get; }
        public string InstrumentFullName { get; }
        public string OrderId { get; }
        public string OrderName { get; }
        public OrderAction Action { get; }
        public MarketPosition ExecutionMarketPosition { get; }
        public bool IsEntry { get; }
        public bool IsClassified { get; }
        public string FillRole { get; }
        public int Quantity { get; }
        public double Price { get; }
        public double Commission { get; }
        public double Rate { get; }

        public FillRecord(
            string executionId,
            DateTime time,
            string accountName,
            string instrumentFullName,
            string orderId,
            string orderName,
            OrderAction action,
            MarketPosition executionMarketPosition,
            bool isEntry,
            bool isClassified,
            string fillRole,
            int quantity,
            double price,
            double commission,
            double rate)
        {
            ExecutionId = executionId;
            Time = time;
            AccountName = accountName;
            InstrumentFullName = instrumentFullName;
            OrderId = orderId ?? string.Empty;
            OrderName = orderName ?? string.Empty;
            Action = action;
            ExecutionMarketPosition = executionMarketPosition;
            IsEntry = isEntry;
            IsClassified = isClassified;
            FillRole = fillRole ?? "Unknown";
            Quantity = quantity;
            Price = price;
            Commission = commission;
            Rate = rate;
        }
    }

    internal class OpenEntryTracker
    {
        public FillRecord SourceFill { get; }
        public int RemainingQuantity { get; set; }

        public OpenEntryTracker(
            FillRecord sourceFill)
        {
            SourceFill = sourceFill;
            RemainingQuantity =
                sourceFill.Quantity;
        }
    }

    public class OrderEventRecord
    {
        public long Sequence { get; set; }
        public DateTime Timestamp { get; set; }
        public string AccountName { get; set; }
        public string InstrumentFullName { get; set; }
        public string OrderId { get; set; }
        public string OrderName { get; set; }
        public string Oco { get; set; }
        public OrderAction Action { get; set; }
        public OrderType OrderType { get; set; }
        public OrderState State { get; set; }
        public double LimitPrice { get; set; }
        public double StopPrice { get; set; }
        public int Quantity { get; set; }
        public int Filled { get; set; }
        public double AverageFillPrice { get; set; }
        public string Comment { get; set; }
    }

    public class InitialTarget
    {
        public string LogicalKey { get; }
        public string Name { get; }
        public double LimitPrice { get; }
        public int Quantity { get; }
        public DateTime FirstSeen { get; }

        public double RewardTicks { get; internal set; }
        public double ExpectedR { get; internal set; }
        public double RewardUSD { get; internal set; }

        public InitialTarget(
            string logicalKey,
            string name,
            double limitPrice,
            int quantity,
            DateTime firstSeen)
        {
            LogicalKey = logicalKey;
            Name = name;
            LimitPrice = limitPrice;
            Quantity = quantity;
            FirstSeen = firstSeen;
        }
    }

    internal class TargetOrderState
    {
        public string OrderId { get; }
        public string Name { get; }
        public string LogicalKey { get; }
        public double InitialLimitPrice { get; }
        public int InitialQuantity { get; }
        public DateTime FirstSeen { get; }

        public double CurrentLimitPrice { get; set; }
        public int CurrentQuantity { get; set; }

        public TargetOrderState(
            string orderId,
            string name,
            string logicalKey,
            double initialLimitPrice,
            int initialQuantity,
            DateTime firstSeen)
        {
            OrderId = orderId;
            Name = name;
            LogicalKey = logicalKey;
            InitialLimitPrice = initialLimitPrice;
            InitialQuantity = initialQuantity;
            FirstSeen = firstSeen;

            CurrentLimitPrice =
                initialLimitPrice;

            CurrentQuantity =
                initialQuantity;
        }
    }

    public class PriceModificationRecord
    {
        public DateTime Timestamp { get; set; }
        public string OrderId { get; set; }
        public string OrderName { get; set; }
        public string Type { get; set; }
        public double OldPrice { get; set; }
        public double NewPrice { get; set; }
        public int OldQuantity { get; set; }
        public int NewQuantity { get; set; }
        public string Note { get; set; }
    }

    public struct MarketSample
    {
        public DateTime Time { get; }
        public double Last { get; }
        public double Bid { get; }
        public double Ask { get; }
        public long Volume { get; }

        public MarketSample(
            DateTime time,
            double last,
            double bid,
            double ask,
            long volume)
        {
            Time = time;
            Last = last;
            Bid = bid;
            Ask = ask;
            Volume = volume;
        }
    }

    #endregion

    // =========================================================
    // BEN 60 SECOND PRE-ROLL ROLLING SCREEN RECORDER
    // =========================================================
    public class BENRollingPreRollRecorder : IDisposable
    {
        private readonly BENTradeRecorderSettings _settings;
        private readonly Action<string> _log;
        private readonly object _sync = new object();
        // Serialize final clip composition so simultaneous trade exits cannot
        // launch unbounded FFmpeg decode/filter/encode jobs on the same machine.
        private readonly SemaphoreSlim _clipBuildSemaphore = new SemaphoreSlim(1, 1);

        private readonly Dictionary<int, Process> _processes =
            new Dictionary<int, Process>();

        private readonly HashSet<Process> _clipProcesses =
            new HashSet<Process>();

        private readonly List<MonitorInfo> _recordingMonitors =
            new List<MonitorInfo>();

        private string _segmentFolder;
        private bool _gpuCompositeMode;
        private string _activeEncoder = "libx264";
        private const int GpuCompositeProcessKey = -1;

        private Timer _cleanupTimer;

        private bool _running;
        private bool _gracefulStop;
        private string _failureReason = string.Empty;
        private bool _disposed;
        private int _restartAttempts;
        private long _stopGeneration;
        private DateTime _bufferCacheTimeUtc = DateTime.MinValue;
        private int _bufferCacheValue;
        private string _activeSettingsSignature = string.Empty;
        private DateTime _lastRestartUtc = DateTime.MinValue;

        // One-second segments are retained until synchronized multi-monitor
        // timeline tests prove that longer segments preserve alignment.
        private readonly int _segmentSeconds = 1;

        private readonly Dictionary<string, DateTime> _tradeProtections =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        // File cleanup is best-effort and local to the recorder. TradeSession
        // has its own helper; this class must not depend on that private method.
        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Temporary video artifacts are cleaned on a best-effort basis.
            }
        }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                    return _running;
            }
        }

        public string FailureReason
        {
            get
            {
                lock (_sync)
                    return _failureReason;
            }
        }

        public int GetBufferedSeconds()
        {
            lock (_sync)
            {
                DateTime cacheNow = DateTime.UtcNow;
                if (_bufferCacheTimeUtc != DateTime.MinValue &&
                    (cacheNow - _bufferCacheTimeUtc).TotalMilliseconds < 750)
                    return _bufferCacheValue;

                int value = CalculateBufferedSeconds_NoLock();
                _bufferCacheValue = value;
                _bufferCacheTimeUtc = cacheNow;
                return value;
            }
        }

        private int CalculateBufferedSeconds_NoLock()
        {
                if (!_running || _recordingMonitors.Count == 0)
                    return 0;

                if (_gpuCompositeMode)
                    return CalculateCompositeBufferedSeconds_NoLock(DateTime.Now);

                DateTime now = DateTime.Now;
                DateTime latestAllowed = now.AddSeconds(-1);
                TimeSpan staleAfter = TimeSpan.FromSeconds(
                    Math.Max(4, _segmentSeconds * 5));

                // Read each monitor's completed-segment index once. Readiness is
                // measured from the same common end timestamp across every monitor;
                // otherwise different latest timestamps can falsely inflate coverage.
                var completedByMonitor = new Dictionary<int, HashSet<long>>();
                DateTime commonEnd = DateTime.MaxValue;

                foreach (MonitorInfo monitor in _recordingMonitors)
                {
                    Process process;
                    bool exited;
                    try
                    {
                        exited = !_processes.TryGetValue(monitor.Index, out process) ||
                                 process == null || process.HasExited;
                    }
                    catch
                    {
                        return 0;
                    }

                    if (exited)
                        return 0;

                    string folder = Path.Combine(_segmentFolder, "M" + monitor.Index);
                    if (!Directory.Exists(folder))
                        return 0;

                    string[] files;
                    try { files = Directory.GetFiles(folder, "seg_*.mp4"); }
                    catch { return 0; }

                    var timestamps = new HashSet<long>();
                    DateTime latestForMonitor = DateTime.MinValue;
                    foreach (string file in files)
                    {
                        DateTime timestamp;
                        try
                        {
                            if (TryParseSegmentTime(Path.GetFileNameWithoutExtension(file), out timestamp) &&
                                timestamp <= latestAllowed && new FileInfo(file).Length > 0)
                            {
                                timestamps.Add(timestamp.Ticks);
                                if (timestamp > latestForMonitor)
                                    latestForMonitor = timestamp;
                            }
                        }
                        catch { }
                    }

                    if (latestForMonitor == DateTime.MinValue || now - latestForMonitor > staleAfter)
                        return 0;

                    completedByMonitor[monitor.Index] = timestamps;
                    if (latestForMonitor < commonEnd)
                        commonEnd = latestForMonitor;
                }

                if (commonEnd == DateTime.MaxValue || completedByMonitor.Count != _recordingMonitors.Count)
                    return 0;

                int minimumCommonCoverage = int.MaxValue;
                foreach (HashSet<long> timestamps in completedByMonitor.Values)
                {
                    int contiguousSeconds = 0;
                    DateTime expected = commonEnd;
                    while (timestamps.Contains(expected.Ticks))
                    {
                        contiguousSeconds += _segmentSeconds;
                        expected = expected.AddSeconds(-_segmentSeconds);
                    }
                    minimumCommonCoverage = Math.Min(minimumCommonCoverage, contiguousSeconds);
                }

                if (minimumCommonCoverage <= 0 || minimumCommonCoverage == int.MaxValue)
                    return 0;

                return Math.Max(0, Math.Min(
                    Math.Max(60, _settings.PreTradeSeconds),
                    minimumCommonCoverage));
        }

        private int CalculateCompositeBufferedSeconds_NoLock(DateTime now)
        {
            Process process;
            try
            {
                if (!_processes.TryGetValue(GpuCompositeProcessKey, out process) ||
                    process == null || process.HasExited)
                    return 0;
            }
            catch { return 0; }

            string folder = Path.Combine(_segmentFolder ?? string.Empty, "COMPOSITE");
            if (!Directory.Exists(folder)) return 0;

            var completed = new HashSet<DateTime>();
            DateTime latestAllowed = now.AddSeconds(-1);
            DateTime latest = DateTime.MinValue;
            try
            {
                foreach (string file in Directory.GetFiles(folder, "seg_*.mp4"))
                {
                    DateTime timestamp;
                    if (!TryParseSegmentTime(Path.GetFileNameWithoutExtension(file), out timestamp) ||
                        timestamp > latestAllowed)
                        continue;

                    try
                    {
                        if (new FileInfo(file).Length <= 0) continue;
                        completed.Add(timestamp);
                        if (timestamp > latest) latest = timestamp;
                    }
                    catch { }
                }
            }
            catch { return 0; }

            if (latest == DateTime.MinValue ||
                now - latest > TimeSpan.FromSeconds(Math.Max(5, _segmentSeconds * 5)))
                return 0;

            int contiguous = 0;
            DateTime expected = latest;
            while (completed.Contains(expected))
            {
                contiguous += _segmentSeconds;
                expected = expected.AddSeconds(-_segmentSeconds);
            }

            return Math.Max(0, Math.Min(Math.Max(60, _settings.PreTradeSeconds), contiguous));
        }

        public DateTime RecordingStartTime
        {
            get;
            private set;
        }

        public DateTime RecordingStopTime
        {
            get;
            private set;
        }

        public BENRollingPreRollRecorder(
            BENTradeRecorderSettings settings,
            Action<string> log)
        {
            _settings = settings ?? BENTradeRecorderSettings.Load();
            _log = log;
        }

        private string BuildSettingsSignature()
        {
            return string.Join("|", new[]
            {
                _settings.SaveFolder ?? string.Empty,
                _settings.FFmpegPath ?? string.Empty,
                _settings.SelectedEncoder ?? "Auto",
                _settings.SelectedMonitors ?? "ALL",
                _settings.LayoutMode ?? "Horizontal",
                _settings.FrameRate.ToString(CultureInfo.InvariantCulture),
                _settings.VideoQuality.ToString(CultureInfo.InvariantCulture),
                _settings.PreTradeSeconds.ToString(CultureInfo.InvariantCulture)
            });
        }

        private bool HasDiskReserve(string path, long minimumFreeBytes, out long freeBytes)
        {
            freeBytes = 0;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string root = Path.GetPathRoot(fullPath);
                if (string.IsNullOrWhiteSpace(root))
                    return false;
                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                    return false;
                freeBytes = drive.AvailableFreeSpace;
                return freeBytes >= minimumFreeBytes;
            }
            catch
            {
                return false;
            }
        }

        public bool Start()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    _failureReason = "Recorder has already been disposed.";
                    return false;
                }

                string requestedSignature = BuildSettingsSignature();
                if (_running && string.Equals(requestedSignature, _activeSettingsSignature, StringComparison.Ordinal))
                    return true;

                if (_running)
                {
                    _log?.Invoke("[PRE-ROLL] Settings changed; performing controlled recorder restart.");
                    if (_cleanupTimer != null)
                    {
                        _cleanupTimer.Dispose();
                        _cleanupTimer = null;
                    }
                    _running = false;
                    StopProcesses_NoLock(false);
                }

                StopProcesses_NoLock(false);

                if (_cleanupTimer != null)
                {
                    _cleanupTimer.Dispose();
                    _cleanupTimer = null;
                }

                _failureReason = string.Empty;

                long freeDiskBytes;
                const long minimumPreRollFreeBytes = 2L * 1024L * 1024L * 1024L;
                if (!HasDiskReserve(_settings.SaveFolder, minimumPreRollFreeBytes, out freeDiskBytes))
                {
                    _failureReason = "Insufficient or unverified free disk space. At least 2 GiB must remain available before pre-roll can start.";
                    _log?.Invoke("[PRE-ROLL] " + _failureReason);
                    return false;
                }

                string ffmpeg =
                    FFmpegFinder.ResolveFFmpegPath(
                        _settings.FFmpegPath);

                if (string.IsNullOrWhiteSpace(ffmpeg) ||
                    !File.Exists(ffmpeg))
                {
                    _failureReason = "FFmpeg executable not found.";
                    _log?.Invoke(
                        "[PRE-ROLL] " + _failureReason);

                    return false;
                }

                List<MonitorInfo> all =
                    DisplayManager.GetMonitors();

                List<MonitorInfo> target =
                    ResolveTargetMonitors(all);

                if (target.Count == 0)
                {
                    _failureReason =
                        "No configured monitor was found. Check monitor names in Settings.";
                    _log?.Invoke(
                        "[PRE-ROLL] " + _failureReason);

                    return false;
                }

                _segmentFolder =
                    Path.Combine(
                        _settings.SaveFolder,
                        "_BEN_PRE_ROLL");

                Directory.CreateDirectory(_segmentFolder);
                CleanupOldSegments();

                _recordingMonitors.Clear();
                _recordingMonitors.AddRange(target);
                _gpuCompositeMode = false;
                _activeEncoder = "libx264";

                string selectedEncoder = _settings.SelectedEncoder ?? "Auto";
                bool cpuSelected = BENGpuCompositePipeline.IsCpuSelection(selectedEncoder);
                bool autoSelected = BENGpuCompositePipeline.IsAutoSelection(selectedEncoder);
                bool gpuSelected = BENGpuCompositePipeline.IsNvencSelection(selectedEncoder);

                if (!cpuSelected && !autoSelected && !gpuSelected)
                {
                    _failureReason = BENGpuCompositePipeline.GetUnsupportedSelectionReason(selectedEncoder);
                    _log?.Invoke("[PRE-ROLL] " + _failureReason);
                    return false;
                }

                bool useGpu = false;
                if (!cpuSelected)
                {
                    string gpuDiagnostic;
                    useGpu = BENGpuCompositePipeline.TryProbe(
                        ffmpeg, _recordingMonitors, _settings.FrameRate,
                        _settings.VideoQuality, _settings.LayoutMode, _log, out gpuDiagnostic);

                    if (!useGpu && !autoSelected)
                    {
                        _failureReason = "GPU mode was explicitly selected, but the complete GPU capture/composition/encode probe failed. " + gpuDiagnostic;
                        _log?.Invoke("[PRE-ROLL GPU MODE REFUSED] " + _failureReason);
                        return false;
                    }

                    if (!useGpu && autoSelected)
                        _log?.Invoke("[PRE-ROLL AUTO] Complete GPU graph unavailable; using the explicit CPU pipeline. " + gpuDiagnostic);
                }

                string encoder = useGpu ? "h264_nvenc" : "libx264";
                bool startedAllMonitors = false;

                try
                {
                    _gracefulStop = false;
                    _running = true;

                    if (useGpu)
                    {
                        _gpuCompositeMode = true;
                        _activeEncoder = "h264_nvenc";
                        string compositeFolder = Path.Combine(_segmentFolder, "COMPOSITE");
                        Directory.CreateDirectory(compositeFolder);
                        string pattern = Path.Combine(compositeFolder, "seg_%Y%m%d_%H%M%S.mp4");
                        startedAllMonitors = StartGpuCompositeProcess(
                            ffmpeg, _recordingMonitors, _settings.FrameRate,
                            _settings.VideoQuality, pattern, _segmentSeconds, _settings.LayoutMode);
                        if (!startedAllMonitors)
                        {
                            _failureReason = "The GPU composite recorder failed to start after a successful preflight. No CPU fallback is permitted in explicit GPU mode.";
                            StopProcesses_NoLock(false);
                            _running = false;
                            _gpuCompositeMode = false;
                        }
                    }
                    else
                    {
                        _gpuCompositeMode = false;
                        _activeEncoder = "libx264";
                        startedAllMonitors = true;
                        foreach (MonitorInfo monitor in _recordingMonitors)
                        {
                            if (!StartMonitorProcess(
                                    ffmpeg, monitor, _settings.FrameRate,
                                    _settings.VideoQuality, "libx264"))
                            {
                                _failureReason = "Failed to start CPU capture for monitor " + monitor.DeviceName;
                                startedAllMonitors = false;
                                break;
                            }

                            Process startedProcess;
                            try
                            {
                                startedAllMonitors = _processes.TryGetValue(monitor.Index, out startedProcess) &&
                                    startedProcess != null && !startedProcess.HasExited;
                            }
                            catch { startedAllMonitors = false; }

                            if (!startedAllMonitors)
                            {
                                _failureReason = "CPU FFmpeg capture exited during startup for monitor " + monitor.DeviceName;
                                break;
                            }
                        }
                    }

                    if (!startedAllMonitors)
                    {
                        _failureReason = string.IsNullOrWhiteSpace(_failureReason)
                            ? "FFmpeg could not start on the configured monitors."
                            : _failureReason;
                        _log?.Invoke("[PRE-ROLL] " + _failureReason);
                        StopProcesses_NoLock(false);
                        _running = false;
                        return false;
                    }

                    _failureReason = string.Empty;

                    RecordingStartTime = DateTime.Now;
                    _activeSettingsSignature = requestedSignature;

                    _cleanupTimer =
                        new Timer(
                            state =>
                            {
                                try
                                {
                                    lock (_sync)
                                    {
                                        if (_running)
                                        {
                                            long availableBytes;
                                            if (!HasDiskReserve(_settings.SaveFolder, 1536L * 1024L * 1024L, out availableBytes))
                                            {
                                                _failureReason = "Pre-roll stopped to preserve disk reserve; available disk space is below 1.5 GiB or cannot be verified.";
                                                _running = false;
                                                _cleanupTimer?.Dispose();
                                                _cleanupTimer = null;
                                                StopProcesses_NoLock(false);
                                                _log?.Invoke("[PRE-ROLL] " + _failureReason);
                                                return;
                                            }

                                            int bufferedSeconds = GetBufferedSeconds();
                                            if (bufferedSeconds >= 3)
                                                _restartAttempts = 0;

                                            if (DateTime.Now - RecordingStartTime > TimeSpan.FromSeconds(20) &&
                                                bufferedSeconds == 0)
                                            {
                                                _failureReason = "FFmpeg is running but no complete monitor segments are being produced.";
                                                _running = false;
                                                _gracefulStop = true;
                                                _cleanupTimer?.Dispose();
                                                _cleanupTimer = null;
                                                StopProcesses_NoLock(false);
                                                _log?.Invoke("[PRE-ROLL] " + _failureReason);
                                                ScheduleRestart_NoLock();
                                            }
                                            else
                                            {
                                                CleanupOldSegments();
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                }
                            },
                            null,
                            2000,
                            2000);

                    _log?.Invoke(
                        "[PRE-ROLL] Recording pipeline started. " +
                        $"Mode={(_gpuCompositeMode ? "GPU D3D11/CUDA composite" : "CPU GDI/software composition")} | " +
                        $"Monitors={_recordingMonitors.Count} | Output={( _gpuCompositeMode ? "1920x1080" : GetOutputResolutionText(_recordingMonitors))} | " +
                        $"FPS={_settings.FrameRate} | Encoder={encoder} | Segment={_segmentSeconds}s");

                    if (_gpuCompositeMode)
                    {
                        _log?.Invoke("[PRE-ROLL GPU] Unified composite segments: " + Path.Combine(_segmentFolder, "COMPOSITE"));
                        foreach (MonitorInfo monitor in _recordingMonitors)
                            _log?.Invoke("[PRE-ROLL GPU] DXGI output_idx=" + monitor.Index.ToString(CultureInfo.InvariantCulture) +
                                " | " + monitor.DeviceName + " | " + monitor.Width + "x" + monitor.Height);
                    }
                    else
                    {
                        foreach (MonitorInfo monitor in _recordingMonitors)
                            _log?.Invoke("[PRE-ROLL CPU] MONITOR " + monitor.DeviceName + ": " +
                                monitor.Width + "x" + monitor.Height + " -> " +
                                Path.Combine(_segmentFolder, "M" + monitor.Index));
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _failureReason = ex.Message;
                    _log?.Invoke(
                        "[PRE-ROLL] Start ERROR: " +
                        ex.Message);

                    StopProcesses_NoLock(false);
                    _running = false;
                    _recordingMonitors.Clear();
                    return false;
                }
            }
        }

        private List<MonitorInfo> ResolveTargetMonitors(
            List<MonitorInfo> all)
        {
            var target =
                new List<MonitorInfo>();

            if (string.IsNullOrWhiteSpace(_settings.SelectedMonitors) ||
                _settings.SelectedMonitors.Equals(
                    "ALL",
                    StringComparison.OrdinalIgnoreCase))
            {
                return all
                    .OrderBy(x => x.Index)
                    .ToList();
            }

            var wantedNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var legacyIndices =
                new HashSet<int>();

            foreach (string part in
                     _settings.SelectedMonitors.Split(
                         new[] { ',', ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string token = part.Trim();
                int legacyIndex;

                if (int.TryParse(token, out legacyIndex))
                    legacyIndices.Add(legacyIndex);
                else if (!string.IsNullOrWhiteSpace(token))
                    wantedNames.Add(token);
            }

            foreach (MonitorInfo monitor in all)
            {
                if (wantedNames.Contains(monitor.SelectionId) ||
                    wantedNames.Contains(monitor.DeviceName) ||
                    wantedNames.Contains(monitor.FriendlyName) ||
                    legacyIndices.Contains(monitor.Index))
                    target.Add(monitor);
            }

            return target
                .OrderBy(x => x.Index)
                .ToList();
        }

        private bool StartGpuCompositeProcess(
            string ffmpeg,
            List<MonitorInfo> monitors,
            int fps,
            int quality,
            string outputPattern,
            int segmentSeconds,
            string layoutMode)
        {
            Process process = null;
            try
            {
                string arguments = BENGpuCompositePipeline.BuildSegmentArguments(
                    monitors, fps, quality, outputPattern, segmentSeconds, layoutMode);

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                process = new Process { StartInfo = psi, EnableRaisingEvents = false };
                string lastDiagnostic = string.Empty;
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    string line = e.Data;
                    if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Interlocked.Exchange(ref lastDiagnostic, line);
                        _log?.Invoke("[GPU COMPOSITOR] " + line);
                    }
                };

                process.Exited += (sender, e) =>
                {
                    bool unexpected = false;
                    lock (_sync)
                    {
                        Process current;
                        if (_processes.TryGetValue(GpuCompositeProcessKey, out current) &&
                            ReferenceEquals(current, process) && !_gracefulStop)
                        {
                            unexpected = true;
                            string diagnostic = Interlocked.CompareExchange(ref lastDiagnostic, null, null);
                            string exitCode = "unknown";
                            try { exitCode = process.ExitCode.ToString(CultureInfo.InvariantCulture); } catch { }
                            _failureReason = "GPU composite FFmpeg exited unexpectedly (exit code " + exitCode + ")." +
                                (string.IsNullOrWhiteSpace(diagnostic) ? string.Empty : " " + diagnostic);
                            _running = false;
                            _gracefulStop = true;
                            if (_cleanupTimer != null) { _cleanupTimer.Dispose(); _cleanupTimer = null; }
                            StopProcesses_NoLock(false);
                        }
                    }

                    if (unexpected)
                    {
                        _log?.Invoke("[PRE-ROLL GPU] " + _failureReason);
                        lock (_sync) { if (!_disposed) ScheduleRestart_NoLock(); }
                    }
                };

                if (!process.Start())
                {
                    process.Dispose();
                    return false;
                }

                try { process.PriorityClass = ProcessPriorityClass.Normal; } catch { }
                process.BeginErrorReadLine();
                _processes[GpuCompositeProcessKey] = process;
                process.EnableRaisingEvents = true;

                _log?.Invoke("[GPU COMPOSITOR] One FFmpeg process owns all monitor captures, CUDA composition and NVENC encoding.");
                return true;
            }
            catch (Exception ex)
            {
                if (process != null)
                {
                    try { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } } catch { }
                    try { process.Dispose(); } catch { }
                }
                _log?.Invoke("[GPU COMPOSITOR] Launch error: " + ex.Message);
                return false;
            }
        }

        private bool StartMonitorProcess(
            string ffmpeg,
            MonitorInfo monitor,
            int fps,
            int quality,
            string encoder)
        {
            Process process = null;
            try
            {
                string monitorFolder =
                    Path.Combine(
                        _segmentFolder,
                        "M" + monitor.Index);

                Directory.CreateDirectory(
                    monitorFolder);

                string pattern =
                    Path.Combine(
                        monitorFolder,
                        "seg_%Y%m%d_%H%M%S.mp4");

                string captureInput =
                    BuildMonitorCaptureInput(monitor, fps);

                string encoderArguments =
                    BuildEncoderArguments(encoder, quality);

                string arguments =
                    "-y " + captureInput +
                    encoderArguments + " " +
                    "-g 30 " +
                    "-keyint_min 30 " +
                    "-forced-idr 1 " +
                    "-force_key_frames \"expr:gte(t,n_forced*1)\" " +
                    "-f segment " +
                    $"-segment_time {_segmentSeconds} " +
                    "-segment_time_delta 0.05 " +
                    "-reset_timestamps 1 " +
                    "-strftime 1 " +
                    $"\"{pattern}\"";

                var psi =
                    new ProcessStartInfo
                    {
                        FileName = ffmpeg,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = false,
                        WindowStyle =
                            ProcessWindowStyle.Hidden
                    };

                process =
                    new Process
                    {
                        StartInfo = psi,
                        EnableRaisingEvents = false
                    };

                int monitorIndex = monitor.Index;
                string lastDiagnostic = string.Empty;

                process.ErrorDataReceived +=
                    (sender, e) =>
                    {
                        if (string.IsNullOrWhiteSpace(e.Data))
                            return;

                        string line = e.Data;

                        if (line.IndexOf(
                                "error",
                                StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf(
                                "failed",
                                StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf(
                                "cannot",
                                StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf(
                                "denied",
                                StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf(
                                "access",
                                StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf(
                                "permission",
                                StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf(
                                "unsupported",
                                StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf(
                                "unable",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Interlocked.Exchange(ref lastDiagnostic, line);
                            _log?.Invoke(
                                $"[PRE-ROLL M{monitorIndex}] {line}");
                        }
                    };

                process.Exited +=
                    (sender, e) =>
                    {
                        bool unexpected = false;

                        lock (_sync)
                        {
                            Process current;
                            if (_processes.TryGetValue(monitorIndex, out current) &&
                                ReferenceEquals(current, process) &&
                                !_gracefulStop)
                            {
                                unexpected = true;
                                string diagnostic =
                                    Interlocked.CompareExchange(
                                        ref lastDiagnostic,
                                        null,
                                        null);
                                string exitCode = "unknown";
                                try
                                {
                                    exitCode = process.ExitCode.ToString(
                                        CultureInfo.InvariantCulture);
                                }
                                catch
                                {
                                }

                                _failureReason =
                                    "FFmpeg exited unexpectedly for monitor " +
                                    monitor.DeviceName +
                                    " (exit code " + exitCode + ")" +
                                    (string.IsNullOrWhiteSpace(diagnostic)
                                        ? "."
                                        : ": " + diagnostic);
                                _running = false;
                                _gracefulStop = true;
                                if (_cleanupTimer != null)
                                {
                                    _cleanupTimer.Dispose();
                                    _cleanupTimer = null;
                                }
                                StopProcesses_NoLock(false);
                            }
                        }

                        if (unexpected)
                        {
                            _log?.Invoke("[PRE-ROLL] " + _failureReason);
                            lock (_sync)
                            {
                                // Process exit invalidates the whole synchronized set.
                                // Restart all monitors together so their timelines remain comparable.
                                if (!_disposed)
                                    ScheduleRestart_NoLock();
                            }
                        }
                    };

                if (!process.Start())
                {
                    process.Dispose();
                    return false;
                }

                try
                {
                    process.PriorityClass =
                        ProcessPriorityClass.BelowNormal;
                }
                catch
                {
                }

                process.BeginErrorReadLine();

                _processes[monitor.Index] =
                    process;

                process.EnableRaisingEvents = true;

                return true;
            }
            catch (Exception ex)
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }

                _log?.Invoke(
                    $"[PRE-ROLL] Monitor {monitor.Index} launch error: {ex.Message}");

                return false;
            }
        }

        internal static string BuildMonitorCaptureInput(
            MonitorInfo monitor,
            int fps)
        {
            if (monitor == null)
                throw new ArgumentNullException(nameof(monitor));

            int width = Math.Max(2, monitor.Width & ~1);
            int height = Math.Max(2, monitor.Height & ~1);

            return "-f gdigrab " +
                $"-framerate {Math.Max(1, fps)} " +
                $"-offset_x {monitor.X} " +
                $"-offset_y {monitor.Y} " +
                $"-video_size {width}x{height} " +
                "-i desktop ";
        }

        private static string BuildEncoderArguments(
            string encoder,
            int quality)
        {
            int q = Math.Max(1, quality);
            if (string.Equals(encoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase))
                return $"-c:v h264_nvenc -preset p2 -tune ll -rc vbr -cq {q} -b:v 0 -pix_fmt yuv420p";
            if (string.Equals(encoder, "h264_qsv", StringComparison.OrdinalIgnoreCase))
                return $"-c:v h264_qsv -preset veryfast -global_quality {q} -pix_fmt yuv420p";
            if (string.Equals(encoder, "h264_amf", StringComparison.OrdinalIgnoreCase))
                return $"-c:v h264_amf -quality balanced -rc cqp -qp_i {q} -qp_p {q} -pix_fmt yuv420p";

            return $"-c:v libx264 -preset ultrafast -crf {q} -pix_fmt yuv420p";
        }

        public void ProtectFrom(
            string tradeId,
            DateTime tradeTime)
        {
            if (string.IsNullOrWhiteSpace(tradeId))
                return;

            lock (_sync)
            {
                DateTime earliest =
                    tradeTime.AddSeconds(
                        -Math.Max(
                            60,
                            _settings.PreTradeSeconds));

                _tradeProtections[tradeId] = earliest;
            }

            _log?.Invoke(
                $"[PRE-ROLL] Buffer protected for {tradeId} from " +
                $"{tradeTime.AddSeconds(-Math.Max(60, _settings.PreTradeSeconds)):HH:mm:ss.fff}");
        }

        public void ReleaseProtection(
            string tradeId)
        {
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(tradeId))
                    _tradeProtections.Remove(tradeId);

                CleanupOldSegments();
            }
        }

        private static string ResolveFFprobePath(string ffmpegPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(ffmpegPath);
                string adjacent = Path.Combine(directory ?? string.Empty, "ffprobe.exe");
                if (File.Exists(adjacent)) return adjacent;

                string pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrWhiteSpace(pathEnv))
                {
                    foreach (string item in pathEnv.Split(Path.PathSeparator))
                    {
                        try
                        {
                            string candidate = Path.Combine(item.Trim(), "ffprobe.exe");
                            if (File.Exists(candidate)) return candidate;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        public bool ValidateExistingVideo(
            string outputPath,
            double expectedDuration,
            Action<string> tradeLog)
        {
            string ffmpegPath = FFmpegFinder.ResolveFFmpegPath(_settings.FFmpegPath);
            return ValidateVideoOutput(ffmpegPath, outputPath, expectedDuration, tradeLog);
        }

        private bool ValidateVideoOutput(
            string ffmpegPath,
            string outputPath,
            double expectedDuration,
            Action<string> tradeLog)
        {
            try
            {
                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1024)
                    return false;

                string ffprobe = ResolveFFprobePath(ffmpegPath);
                if (string.IsNullOrWhiteSpace(ffprobe))
                {
                    tradeLog?.Invoke("[VIDEO VALIDATION] ffprobe.exe was not found; refusing to mark the clip committed.");
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = ffprobe,
                    Arguments = "-v error -select_streams v:0 -show_entries stream=codec_name,width,height -show_entries format=duration -of default=noprint_wrappers=1 \"" + outputPath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process process = Process.Start(psi))
                {
                    if (process == null) return false;
                    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(20000))
                    {
                        try { process.Kill(); process.WaitForExit(2000); } catch { }
                        tradeLog?.Invoke("[VIDEO VALIDATION] ffprobe timed out.");
                        return false;
                    }

                    string stdout = stdoutTask.GetAwaiter().GetResult();
                    string stderr = stderrTask.GetAwaiter().GetResult();
                    if (process.ExitCode != 0)
                    {
                        tradeLog?.Invoke("[VIDEO VALIDATION] ffprobe rejected output: " + stderr.Trim());
                        return false;
                    }

                    double actualDuration = double.NaN;
                    int width = 0;
                    int height = 0;
                    string codecName = string.Empty;
                    foreach (string rawLine in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int separator = rawLine.IndexOf('=');
                        if (separator <= 0) continue;
                        string key = rawLine.Substring(0, separator).Trim();
                        string value = rawLine.Substring(separator + 1).Trim();
                        if (key.Equals("duration", StringComparison.OrdinalIgnoreCase))
                            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out actualDuration);
                        else if (key.Equals("width", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out width);
                        else if (key.Equals("height", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
                        else if (key.Equals("codec_name", StringComparison.OrdinalIgnoreCase))
                            codecName = value;
                    }

                    if (string.IsNullOrWhiteSpace(codecName) || width <= 0 || height <= 0 ||
                        double.IsNaN(actualDuration) || double.IsInfinity(actualDuration))
                    {
                        tradeLog?.Invoke("[VIDEO VALIDATION] ffprobe did not report a valid video stream, codec, dimensions and duration.");
                        return false;
                    }

                    double tolerance = Math.Max(1.0, expectedDuration * 0.03);
                    if (actualDuration + tolerance < expectedDuration)
                    {
                        tradeLog?.Invoke("[VIDEO VALIDATION] Duration too short. Expected=" + expectedDuration.ToString("F3", CultureInfo.InvariantCulture) +
                            " Actual=" + actualDuration.ToString("F3", CultureInfo.InvariantCulture));
                        return false;
                    }

                    if (_gpuCompositeMode &&
                        (!codecName.Equals("h264", StringComparison.OrdinalIgnoreCase) ||
                         width != BENGpuCompositePipeline.OutputWidth ||
                         height != BENGpuCompositePipeline.OutputHeight))
                    {
                        tradeLog?.Invoke("[VIDEO VALIDATION] GPU output rejected: expected H.264 1920x1080, received " +
                            codecName + " " + width + "x" + height + ".");
                        return false;
                    }

                    tradeLog?.Invoke("[VIDEO VALIDATION] PASS | Codec=" + codecName +
                        " | Resolution=" + width + "x" + height +
                        " | Duration=" + actualDuration.ToString("F3", CultureInfo.InvariantCulture));
                    return true;
                }
            }
            catch (Exception ex)
            {
                tradeLog?.Invoke("[VIDEO VALIDATION ERROR] " + ex.Message);
                return false;
            }
        }

        private static bool PromoteFileAtomically(string temporaryPath, string destinationPath)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    string backup = destinationPath + ".previous";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Replace(temporaryPath, destinationPath, backup);
                    if (File.Exists(backup)) File.Delete(backup);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
                return File.Exists(destinationPath) && new FileInfo(destinationPath).Length >= 1024;
            }
            catch
            {
                return false;
            }
        }

        private bool WaitForStableSegments(
            List<MonitorInfo> monitors,
            DateTime requestedEnd,
            TimeSpan timeout,
            Action<string> tradeLog)
        {
            if (_gpuCompositeMode)
                return WaitForStableCompositeSegment(requestedEnd, timeout, tradeLog);

            var previousLengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var stableCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            DateTime deadline = DateTime.UtcNow + timeout;
            bool allStable = false;

            while (DateTime.UtcNow < deadline)
            {
                allStable = true;
                foreach (MonitorInfo monitor in monitors)
                {
                    string folder = Path.Combine(_segmentFolder ?? string.Empty, "M" + monitor.Index);
                    string latest = null;
                    DateTime latestTime = DateTime.MinValue;
                    try
                    {
                        if (Directory.Exists(folder))
                        {
                            foreach (string file in Directory.GetFiles(folder, "seg_*.mp4"))
                            {
                                DateTime stamp;
                                if (TryParseSegmentTime(Path.GetFileNameWithoutExtension(file), out stamp) &&
                                    stamp <= requestedEnd && stamp > latestTime)
                                {
                                    latestTime = stamp;
                                    latest = file;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (latest == null)
                    {
                        allStable = false;
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(latest);
                        long oldLength;
                        if (previousLengths.TryGetValue(latest, out oldLength) && oldLength == info.Length &&
                            (DateTime.Now - info.LastWriteTime).TotalMilliseconds >= 250)
                        {
                            int count;
                            stableCounts.TryGetValue(latest, out count);
                            stableCounts[latest] = count + 1;
                            if (stableCounts[latest] < 2)
                                allStable = false;
                        }
                        else
                        {
                            previousLengths[latest] = info.Length;
                            stableCounts[latest] = 0;
                            allStable = false;
                        }
                    }
                    catch
                    {
                        allStable = false;
                    }
                }

                if (allStable)
                    return true;

                Thread.Sleep(100);
            }

            tradeLog?.Invoke("[VIDEO] Stable segment wait reached its bounded timeout.");
            return false;
        }

        private bool WaitForStableCompositeSegment(
            DateTime requestedEnd,
            TimeSpan timeout,
            Action<string> tradeLog)
        {
            string folder = Path.Combine(_segmentFolder ?? string.Empty, "COMPOSITE");
            DateTime deadline = DateTime.UtcNow + timeout;
            DateTime requiredSegmentStart = requestedEnd.AddTicks(-1);
            requiredSegmentStart = new DateTime(
                requiredSegmentStart.Year, requiredSegmentStart.Month, requiredSegmentStart.Day,
                requiredSegmentStart.Hour, requiredSegmentStart.Minute, requiredSegmentStart.Second);
            string previousFile = null;
            long previousLength = -1;
            int stablePasses = 0;

            while (DateTime.UtcNow < deadline)
            {
                string latest = null;
                DateTime latestTime = DateTime.MinValue;
                try
                {
                    if (Directory.Exists(folder))
                    {
                        foreach (string file in Directory.GetFiles(folder, "seg_*.mp4"))
                        {
                            DateTime stamp;
                            if (TryParseSegmentTime(Path.GetFileNameWithoutExtension(file), out stamp) &&
                                stamp > latestTime)
                            {
                                latestTime = stamp;
                                latest = file;
                            }
                        }
                    }
                }
                catch { }

                // A closed segment before the requested post-roll boundary is not
                // sufficient. Wait until the segment that covers the final requested
                // second has appeared and its MP4 container has stopped changing.
                if (latest != null && latestTime >= requiredSegmentStart)
                {
                    try
                    {
                        var info = new FileInfo(latest);
                        if (string.Equals(latest, previousFile, StringComparison.OrdinalIgnoreCase) &&
                            info.Length == previousLength &&
                            (DateTime.Now - info.LastWriteTime).TotalMilliseconds >= 250)
                        {
                            stablePasses++;
                            if (stablePasses >= 2) return true;
                        }
                        else
                        {
                            previousFile = latest;
                            previousLength = info.Length;
                            stablePasses = 0;
                        }
                    }
                    catch { stablePasses = 0; }
                }
                else
                {
                    previousFile = null;
                    previousLength = -1;
                    stablePasses = 0;
                }

                Thread.Sleep(100);
            }

            tradeLog?.Invoke("[VIDEO] Composite stream did not reach and close the requested post-roll segment before timeout.");
            return false;
        }

        public bool BuildTradeClip(
            DateTime entryTime,
            DateTime exitTime,
            string outputFile,
            Action<string> tradeLog)
        {
            if (!_clipBuildSemaphore.Wait(TimeSpan.FromMinutes(3)))
            {
                tradeLog?.Invoke("[VIDEO] Clip queue is busy; this trade will be retried without starting another FFmpeg composition job.");
                return false;
            }

            try
            {
            if (entryTime == DateTime.MinValue ||
                exitTime == DateTime.MinValue ||
                exitTime < entryTime)
            {
                tradeLog?.Invoke(
                    "[VIDEO] Invalid entry/exit time.");
                return false;
            }

            string ffmpeg =
                FFmpegFinder.ResolveFFmpegPath(
                    _settings.FFmpegPath);

            if (string.IsNullOrWhiteSpace(ffmpeg) ||
                !File.Exists(ffmpeg))
            {
                tradeLog?.Invoke(
                    "[VIDEO] FFmpeg unavailable.");
                return false;
            }

            DateTime start =
                entryTime.AddSeconds(
                    -Math.Max(
                        60,
                        _settings.PreTradeSeconds));

            DateTime end =
                exitTime.AddSeconds(
                    Math.Max(
                        5,
                        _settings.PostTradeSeconds));

            lock (_sync)
            {
                if (_disposed)
                {
                    tradeLog?.Invoke("[VIDEO] Recorder is shutting down; clip creation cancelled.");
                    return false;
                }
            }

            List<MonitorInfo> monitors;

            lock (_sync)
            {
                monitors =
                    _recordingMonitors
                        .Select(x => new MonitorInfo
                        {
                            Index = x.Index,
                            DeviceName = x.DeviceName,
                            FriendlyName = x.FriendlyName,
                            DeviceId = x.DeviceId,
                            X = x.X,
                            Y = x.Y,
                            Width = x.Width,
                            Height = x.Height,
                            IsPrimary = x.IsPrimary
                        })
                        .ToList();
            }

            if (monitors.Count == 0)
            {
                monitors =
                    ResolveTargetMonitors(
                        DisplayManager.GetMonitors());
            }

            if (monitors.Count == 0)
            {
                tradeLog?.Invoke(
                    "[VIDEO] No recording monitors available.");
                return false;
            }

            if (_gpuCompositeMode)
                return BuildGpuCompositeTradeClip(ffmpeg, start, end, outputFile, tradeLog);

            if (!WaitForStableSegments(monitors, end, TimeSpan.FromSeconds(8), tradeLog))
            {
                // Do not compose from a segment whose write completion could not
                // be established. The journal continues independently and a later
                // bounded finalization retry may rebuild the clip.
                tradeLog?.Invoke("[VIDEO] Segment stabilization failed; refusing to compose from potentially active files.");
                return false;
            }

            DateTime protectedFrom;
            lock (_sync)
            {
                protectedFrom = _tradeProtections.Count == 0
                    ? DateTime.MaxValue
                    : _tradeProtections.Values.Min();
            }

            tradeLog?.Invoke(
                "[VIDEO] PRE-ROLL STATE | " +
                $"Running={_running} | " +
                $"Folder={_segmentFolder} | " +
                $"Monitors={monitors.Count} | " +
                $"Start={start:yyyy-MM-dd HH:mm:ss.fff} | " +
                $"End={end:yyyy-MM-dd HH:mm:ss.fff} | " +
                $"ProtectedFrom={protectedFrom:yyyy-MM-dd HH:mm:ss.fff}");

            List<SegmentGroup> groups =
                GetSynchronizedSegments(
                    monitors,
                    start,
                    end,
                    tradeLog);

            if (groups.Count == 0)
            {
                tradeLog?.Invoke(
                    "[VIDEO] No synchronized PRE-ROLL segments found.");
                return false;
            }

            tradeLog?.Invoke(
                "[VIDEO] SYNCED SEGMENTS | " +
                $"Count={groups.Count} | " +
                $"First={groups[0].Timestamp:yyyy-MM-dd HH:mm:ss} | " +
                $"Last={groups[groups.Count - 1].Timestamp:yyyy-MM-dd HH:mm:ss}");

            long availableVideoBytes;
            if (!HasDiskReserve(Path.GetDirectoryName(outputFile) ?? _settings.SaveFolder,
                    1024L * 1024L * 1024L, out availableVideoBytes))
            {
                tradeLog?.Invoke("[VIDEO] Clip creation refused to preserve at least 1 GiB of free disk space.");
                return false;
            }

            string tempDir =
                Path.Combine(
                    Path.GetDirectoryName(outputFile) ?? _settings.SaveFolder,
                    "_BEN_VIDEO_TEMP");

            Directory.CreateDirectory(tempDir);

            var concatLists =
                new List<string>();
            var temporarySegments =
                new List<string>();

            try
            {
                int availableSegments =
                    groups.Sum(x => x.Files.Count);

                if (availableSegments == 0)
                {
                    tradeLog?.Invoke(
                        "[VIDEO] No captured monitor segments exist in the requested trade window.");
                    return false;
                }

                int missingSegments =
                    groups.Sum(x => monitors.Count - x.Files.Count);

                var blackSegmentByMonitor =
                    new Dictionary<int, string>();

                if (missingSegments > 0)
                {
                    tradeLog?.Invoke(
                        $"[VIDEO] Coverage gaps detected: {missingSegments} monitor-second(s). " +
                        "Missing footage will be shown as black in the final video.");
                }

                foreach (MonitorInfo monitor in monitors)
                {
                    string blackSegment = null;

                    if (groups.Any(x => !x.Files.ContainsKey(monitor.Index)))
                    {
                        blackSegment =
                            Path.Combine(
                                tempDir,
                                "black_M" + monitor.Index + "_" +
                                Guid.NewGuid().ToString("N") + ".mp4");

                        if (!CreateBlackSegment(
                                ffmpeg,
                                blackSegment,
                                monitor,
                                tradeLog))
                        {
                            tradeLog?.Invoke(
                                $"[VIDEO] Could not create a black gap marker for monitor {monitor.Index}.");
                            return false;
                        }

                        temporarySegments.Add(blackSegment);
                        blackSegmentByMonitor[monitor.Index] = blackSegment;
                    }

                    string listPath =
                        Path.Combine(
                            tempDir,
                            "concat_" +
                            monitor.Index + "_" +
                            Guid.NewGuid().ToString("N") +
                            ".txt");

                    var lines =
                        new List<string>();

                    foreach (SegmentGroup group in groups)
                    {
                        string file;

                        if (!group.Files.TryGetValue(
                                monitor.Index,
                                out file))
                        {
                            file = blackSegmentByMonitor[monitor.Index];
                        }

                        string safe =
                            file.Replace(
                                "'",
                                "'\\''");

                        lines.Add(
                            "file '" +
                            safe +
                            "'");
                    }

                    // UTF-8 without BOM preserves non-ASCII user paths; the concat demuxer
                    // rejects the BOM emitted by Encoding.UTF8.
                    File.WriteAllLines(
                        listPath,
                        lines,
                        new UTF8Encoding(false));

                    concatLists.Add(listPath);
                }

                double trimOffset =
                    (
                        start -
                        groups[0].Timestamp
                    ).TotalSeconds;

                if (trimOffset < 0)
                    trimOffset = 0;

                double duration =
                    (
                        end -
                        start
                    ).TotalSeconds;

                if (duration <= 0)
                {
                    tradeLog?.Invoke(
                        "[VIDEO] Invalid clip duration.");
                    return false;
                }

                string filter =
                    BuildCompositionFilter(
                        monitors,
                        trimOffset,
                        duration);

                var inputArguments = new StringBuilder(concatLists.Count * 64);
                for (int i = 0; i < concatLists.Count; i++)
                {
                    inputArguments.Append("-f concat -safe 0 -i \"")
                        .Append(concatLists[i])
                        .Append("\" ");
                }
                string inputs = inputArguments.ToString();

                string trimEnd =
                    (trimOffset + duration)
                        .ToString(
                            "F3",
                            CultureInfo.InvariantCulture);

                string trimStart =
                    trimOffset.ToString(
                        "F3",
                        CultureInfo.InvariantCulture);

                // BuildCompositionFilter embeds the same trim values into each input.
                string outputDirectory =
                    Path.GetDirectoryName(outputFile);

                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(
                        outputDirectory);
                }

                string encoder = string.IsNullOrWhiteSpace(_activeEncoder)
                    ? "libx264"
                    : _activeEncoder;
                string stagedOutput = outputFile + ".partial." + Guid.NewGuid().ToString("N") + ".mp4";
                TryDeleteFile(stagedOutput);

                string args =
                    $"-y {inputs}" +
                    $"-filter_complex \"{filter}\" " +
                    "-map \"[outv]\" " +
                    "-an " +
                    BuildEncoderArguments(encoder, _settings.VideoQuality) + " " +
                    "-g 30 " +
                    "-keyint_min 30 " +
                    "-forced-idr 1 " +
                    "-force_key_frames \"expr:gte(t,n_forced*1)\" " +
                    $"-t {duration.ToString("F3", CultureInfo.InvariantCulture)} " +
                    $"\"{stagedOutput}\"";

                tradeLog?.Invoke(
                    "[VIDEO] FINALIZE COMPOSE | " +
                    $"Monitors={monitors.Count} | " +
                    $"ResolutionTarget={GetOutputResolutionText(monitors)} | " +
                    $"TrimStart={trimStart}s | " +
                    $"TrimEnd={trimEnd}s | " +
                    $"Duration={duration:F3}s");

                bool ok = RunFFmpeg(ffmpeg, args, tradeLog);

                if (!ok && !string.Equals(encoder, "libx264", StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(stagedOutput);
                    tradeLog?.Invoke("[VIDEO] Hardware encoding failed; retrying with CPU x264.");
                    string cpuArgs = args.Replace(
                        BuildEncoderArguments(encoder, _settings.VideoQuality),
                        BuildEncoderArguments("libx264", _settings.VideoQuality));
                    ok = RunFFmpeg(ffmpeg, cpuArgs, tradeLog);
                }

                if (ok)
                {
                    ok = ValidateVideoOutput(ffmpeg, stagedOutput, duration, tradeLog);
                    if (ok)
                        ok = PromoteFileAtomically(stagedOutput, outputFile);
                    if (!ok)
                        tradeLog?.Invoke("[VIDEO] Staged MP4 failed validation or atomic promotion; existing final video was preserved.");
                }

                TryDeleteFile(stagedOutput);
                if (ok)
                {
                    tradeLog?.Invoke(
                        "[VIDEO] Trade_Video.mp4 validated and committed: " +
                        $"{start:yyyy-MM-dd HH:mm:ss.fff} -> " +
                        $"{end:yyyy-MM-dd HH:mm:ss.fff}");
                }

                return ok;
            }
            finally
            {
                foreach (string list in concatLists)
                {
                    try
                    {
                        if (File.Exists(list))
                            File.Delete(list);
                    }
                    catch
                    {
                    }
                }

                foreach (string segment in temporarySegments)
                {
                    try
                    {
                        if (File.Exists(segment))
                            File.Delete(segment);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    CleanupOldSegments();
                }
                catch
                {
                }
            }
        
            }
            finally
            {
                _clipBuildSemaphore.Release();
            }
        }

        private bool BuildGpuCompositeTradeClip(
            string ffmpeg,
            DateTime start,
            DateTime end,
            string outputFile,
            Action<string> tradeLog)
        {
            if (end <= start) return false;
            TimeSpan postRollWait = TimeSpan.FromSeconds(
                Math.Max(8, Math.Min(65, _settings.PostTradeSeconds + 5)));
            if (!WaitForStableCompositeSegment(end, postRollWait, tradeLog))
                return false;

            long availableBytes;
            if (!HasDiskReserve(Path.GetDirectoryName(outputFile) ?? _settings.SaveFolder,
                    1024L * 1024L * 1024L, out availableBytes))
            {
                tradeLog?.Invoke("[VIDEO GPU] Clip creation refused to preserve at least 1 GiB of free disk space.");
                return false;
            }

            string folder = Path.Combine(_segmentFolder ?? string.Empty, "COMPOSITE");
            var segmentMap = new SortedDictionary<DateTime, string>();
            try
            {
                foreach (string file in Directory.GetFiles(folder, "seg_*.mp4"))
                {
                    DateTime timestamp;
                    if (TryParseSegmentTime(Path.GetFileNameWithoutExtension(file), out timestamp))
                    {
                        try { if (new FileInfo(file).Length > 0) segmentMap[timestamp] = file; } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                tradeLog?.Invoke("[VIDEO GPU] Cannot enumerate composite segments: " + ex.Message);
                return false;
            }

            DateTime first = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second);
            DateTime lastExclusive = end.AddTicks(-1);
            DateTime last = new DateTime(lastExclusive.Year, lastExclusive.Month, lastExclusive.Day,
                lastExclusive.Hour, lastExclusive.Minute, lastExclusive.Second);

            var selected = new List<string>();
            for (DateTime t = first; t <= last; t = t.AddSeconds(_segmentSeconds))
            {
                string file;
                if (!segmentMap.TryGetValue(t, out file))
                {
                    tradeLog?.Invoke("[VIDEO GPU] Missing composite segment at " +
                        t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                        "; refusing to silently compress the timeline.");
                    return false;
                }
                selected.Add(file);
            }

            if (selected.Count == 0) return false;

            string tempDir = Path.Combine(Path.GetDirectoryName(outputFile) ?? _settings.SaveFolder, "_BEN_VIDEO_TEMP");
            Directory.CreateDirectory(tempDir);
            string listPath = Path.Combine(tempDir, "gpu_concat_" + Guid.NewGuid().ToString("N") + ".txt");
            string stagedOutput = outputFile + ".partial." + Guid.NewGuid().ToString("N") + ".mp4";

            try
            {
                var lines = new List<string>(selected.Count);
                foreach (string file in selected)
                    lines.Add("file '" + file.Replace("'", "'\''") + "'");
                File.WriteAllLines(listPath, lines, new UTF8Encoding(false));

                double trimOffset = Math.Max(0, (start - first).TotalSeconds);
                double duration = (end - start).TotalSeconds;
                string args = BENGpuCompositePipeline.BuildClipArguments(
                    listPath, stagedOutput, trimOffset, duration,
                    _settings.FrameRate, _settings.VideoQuality);

                tradeLog?.Invoke("[VIDEO GPU] FINALIZE | one already-composed 1920x1080 stream | " +
                    "Segments=" + selected.Count.ToString(CultureInfo.InvariantCulture) +
                    " | Encoder=h264_nvenc | Decode=NVDEC | Composition=not repeated");

                bool ok = RunFFmpeg(ffmpeg, args, tradeLog);
                if (ok) ok = ValidateVideoOutput(ffmpeg, stagedOutput, duration, tradeLog);
                if (ok) ok = PromoteFileAtomically(stagedOutput, outputFile);
                if (!ok)
                    tradeLog?.Invoke("[VIDEO GPU] Final clip failed GPU encode, stream validation or atomic commit. No CPU fallback was attempted.");
                else
                    tradeLog?.Invoke("[VIDEO GPU] Validated 1920x1080 NVENC clip committed: " + outputFile);
                return ok;
            }
            catch (Exception ex)
            {
                tradeLog?.Invoke("[VIDEO GPU] Clip build failed: " + ex.Message);
                return false;
            }
            finally
            {
                TryDeleteFile(listPath);
                TryDeleteFile(stagedOutput);
                try { CleanupOldSegments(); } catch { }
            }
        }

        private string BuildCompositionFilter(
            List<MonitorInfo> monitors,
            double trimOffset,
            double duration)
        {
            string trimStart =
                trimOffset.ToString(
                    "F3",
                    CultureInfo.InvariantCulture);

            string trimEnd =
                (trimOffset + duration).ToString(
                    "F3",
                    CultureInfo.InvariantCulture);

            var f =
                new StringBuilder();

            int cellWidth;
            int cellHeight;
            GetCompositionCellSize(monitors, out cellWidth, out cellHeight);

            for (int i = 0;
                 i < monitors.Count;
                 i++)
            {
                f.Append(
                    $"[{i}:v]trim=start={trimStart}:end={trimEnd}," +
                    "setpts=PTS-STARTPTS," +
                    $"scale={cellWidth}:{cellHeight}:force_original_aspect_ratio=decrease," +
                    $"pad={cellWidth}:{cellHeight}:(ow-iw)/2:(oh-ih)/2:color=black," +
                    "setsar=1,format=yuv420p[v" + i + "];" );
            }

            if (monitors.Count == 1)
            {
                f.Append(
                    "[v0]null[outv]");

                return f.ToString();
            }

            bool vertical =
                string.Equals(
                    _settings.LayoutMode,
                    "Vertical",
                    StringComparison.OrdinalIgnoreCase);

            if (vertical)
            {
                for (int i = 0;
                     i < monitors.Count;
                     i++)
                {
                    f.Append(
                        $"[v{i}]");
                }

                f.Append(
                    $"vstack=inputs={monitors.Count}[outv]");

                return f.ToString();
            }

            if (monitors.Count == 2)
            {
                f.Append(
                    "[v0][v1]hstack=inputs=2[outv]");

                return f.ToString();
            }

            if (monitors.Count == 3)
            {
                f.Append(
                    $"color=c=black:s={cellWidth}x{cellHeight}:r={Math.Max(15, _settings.FrameRate)}[blank];");
                f.Append(
                    "[v0][v1]hstack=inputs=2[top];");
                f.Append(
                    "[v2][blank]hstack=inputs=2[bottom];");
                f.Append(
                    "[top][bottom]vstack=inputs=2[outv]");

                return f.ToString();
            }

            if (monitors.Count == 4)
            {
                f.Append(
                    "[v0][v1]hstack=inputs=2[top];");
                f.Append(
                    "[v2][v3]hstack=inputs=2[bottom];");
                f.Append(
                    "[top][bottom]vstack=inputs=2[outv]");

                return f.ToString();
            }

            // Defensive fallback for unusual monitor counts.
            for (int i = 0;
                 i < monitors.Count;
                 i++)
            {
                f.Append(
                    $"[v{i}]");
            }

            f.Append(
                $"hstack=inputs={monitors.Count}[outv]");

            return f.ToString();
        }

        private string GetOutputResolutionText(
            List<MonitorInfo> monitors)
        {
            if (monitors.Count == 0)
                return "UNKNOWN";

            int width;
            int height;
            GetCompositionCellSize(monitors, out width, out height);

            if (monitors.Count == 1)
                return $"{width}x{height}";

            bool vertical =
                string.Equals(
                    _settings.LayoutMode,
                    "Vertical",
                    StringComparison.OrdinalIgnoreCase);

            if (vertical)
            {
                return $"{width}x{height * monitors.Count}";
            }

            if (monitors.Count == 2)
                return $"{width * 2}x{height}";

            return $"{width * 2}x{height * 2}";
        }

        private void GetCompositionCellSize(
            List<MonitorInfo> monitors,
            out int width,
            out int height)
        {
            if (monitors == null || monitors.Count == 0)
            {
                width = BENGpuCompositePipeline.OutputWidth;
                height = BENGpuCompositePipeline.OutputHeight;
                return;
            }

            bool vertical = string.Equals(_settings.LayoutMode, "Vertical", StringComparison.OrdinalIgnoreCase);
            width = monitors.Count == 1 || vertical
                ? BENGpuCompositePipeline.OutputWidth
                : BENGpuCompositePipeline.OutputWidth / 2;
            height = monitors.Count == 1
                ? BENGpuCompositePipeline.OutputHeight
                : vertical
                    ? BENGpuCompositePipeline.OutputHeight / monitors.Count
                    : monitors.Count == 2
                        ? BENGpuCompositePipeline.OutputHeight
                        : BENGpuCompositePipeline.OutputHeight / 2;
        }

        private List<SegmentGroup> GetSynchronizedSegments(
            List<MonitorInfo> monitors,
            DateTime start,
            DateTime end,
            Action<string> tradeLog)
        {
            var perMonitor =
                new Dictionary<int, Dictionary<DateTime, string>>();

            foreach (MonitorInfo monitor in monitors)
            {
                string folder =
                    Path.Combine(
                        _segmentFolder,
                        "M" + monitor.Index);

                var map =
                    new Dictionary<DateTime, string>();

                if (Directory.Exists(folder))
                {
                    foreach (string file in
                             Directory.GetFiles(
                                 folder,
                                 "seg_*.mp4"))
                    {
                        DateTime t;

                        if (!TryParseSegmentTime(
                                Path.GetFileNameWithoutExtension(file),
                                out t))
                        {
                            continue;
                        }

                        try
                        {
                            if (new FileInfo(file).Length <= 0)
                                continue;
                        }
                        catch
                        {
                            continue;
                        }

                        DateTime segmentEnd =
                            t.AddSeconds(_segmentSeconds);

                        if (segmentEnd >= start &&
                            t <= end)
                        {
                            map[t] = file;
                        }
                    }
                }

                tradeLog?.Invoke(
                    $"[VIDEO] MONITOR {monitor.Index} SEGMENTS | " +
                    $"Folder={folder} | Count={map.Count} | " +
                    $"First={(map.Count > 0 ? map.Keys.Min().ToString("yyyy-MM-dd HH:mm:ss") : "NONE")} | " +
                    $"Last={(map.Count > 0 ? map.Keys.Max().ToString("yyyy-MM-dd HH:mm:ss") : "NONE")}");

                perMonitor[monitor.Index] = map;
            }

            if (perMonitor.Count == 0)
                return new List<SegmentGroup>();

            var result =
                new List<SegmentGroup>();

            DateTime firstRequired = new DateTime(
                start.Year, start.Month, start.Day,
                start.Hour, start.Minute, start.Second);
            DateTime endExclusive = end.AddTicks(-1);
            DateTime lastRequired = new DateTime(
                endExclusive.Year, endExclusive.Month, endExclusive.Day,
                endExclusive.Hour, endExclusive.Minute, endExclusive.Second);

            for (DateTime timestamp = firstRequired;
                 timestamp <= lastRequired;
                 timestamp = timestamp.AddSeconds(_segmentSeconds))
            {
                var group =
                    new SegmentGroup
                    {
                        Timestamp = timestamp
                    };

                foreach (MonitorInfo monitor in monitors)
                {
                    string file;

                    if (perMonitor[monitor.Index].TryGetValue(timestamp, out file))
                        group.Files[monitor.Index] = file;
                }

                result.Add(group);
            }

            int missingMonitorSeconds =
                result.Sum(x => monitors.Count - x.Files.Count);

            if (missingMonitorSeconds > 0)
            {
                tradeLog?.Invoke(
                    $"[VIDEO] Timeline preserved with {missingMonitorSeconds} missing monitor-second(s); " +
                    "the finalizer will mark those gaps as black.");
            }

            return result;
        }

        private bool CreateBlackSegment(
            string ffmpeg,
            string outputFile,
            MonitorInfo monitor,
            Action<string> log)
        {
            int fps = Math.Max(15, _settings.FrameRate);
            int width = Math.Max(2, monitor.Width & ~1);
            int height = Math.Max(2, monitor.Height & ~1);

            string colorInput =
                $"-f lavfi -i \"color=c=black:s={width}x{height}:r={fps}\" ";

            string encoder = string.IsNullOrWhiteSpace(_activeEncoder)
                ? "libx264"
                : _activeEncoder;

            string args =
                "-y " + colorInput +
                "-t 1.000 -an " +
                BuildEncoderArguments(encoder, _settings.VideoQuality) + " " +
                $"-r {fps} -g 30 -keyint_min 30 -movflags +faststart \"{outputFile}\"";

            bool ok = RunFFmpeg(ffmpeg, args, log);

            if (!ok && !string.Equals(encoder, "libx264", StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke(
                    $"[VIDEO] Hardware encoder failed while creating a black gap marker for monitor {monitor.Index}; retrying with CPU x264.");

                string hardwareArgs =
                    BuildEncoderArguments(encoder, _settings.VideoQuality);

                args = args.Replace(
                    hardwareArgs,
                    BuildEncoderArguments("libx264", _settings.VideoQuality));

                ok = RunFFmpeg(ffmpeg, args, log);
            }

            if (!ok)
                return false;

            try
            {
                return File.Exists(outputFile) && new FileInfo(outputFile).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseSegmentTime(
            string name,
            out DateTime value)
        {
            string s =
                name.StartsWith(
                    "seg_",
                    StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(4)
                    : name;

            return DateTime.TryParseExact(
                s,
                "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value);
        }

        private bool RunFFmpeg(
            string ffmpeg,
            string args,
            Action<string> log)
        {
            Process process = null;
            try
            {
                var psi =
                    new ProcessStartInfo
                    {
                        FileName = ffmpeg,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = true,
                        RedirectStandardInput = false,
                        WindowStyle =
                            ProcessWindowStyle.Hidden
                    };

                lock (_sync)
                {
                    if (_disposed)
                        return false;

                    process = Process.Start(psi);
                    if (process == null)
                        return false;

                    _clipProcesses.Add(process);
                }

                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(180000))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch
                    {
                    }

                    log?.Invoke("[VIDEO FFmpeg] Timed out after 180 seconds and was stopped.");
                    return false;
                }

                string stderr = stderrTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0)
                {
                    log?.Invoke(
                        "[VIDEO FFmpeg] " +
                        stderr.Trim());

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                    }
                }

                log?.Invoke("[VIDEO FFmpeg ERROR] " + ex.Message);
                return false;
            }
            finally
            {
                if (process != null)
                {
                    lock (_sync)
                        _clipProcesses.Remove(process);

                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void CleanupOldSegments()
        {
            lock (_sync)
            {
            if (string.IsNullOrWhiteSpace(
                    _segmentFolder) ||
                !Directory.Exists(
                    _segmentFolder))
            {
                return;
            }

            DateTime cutoff =
                DateTime.Now.AddSeconds(
                    -Math.Max(
                        62,
                        _settings.PreTradeSeconds + 2));

            if (_tradeProtections.Count > 0)
            {
                DateTime earliestProtected =
                    _tradeProtections.Values.Min();

                if (earliestProtected < cutoff)
                    cutoff = earliestProtected;
            }

            var segmentFolders = Directory.GetDirectories(_segmentFolder, "M*").ToList();
            string compositeFolder = Path.Combine(_segmentFolder, "COMPOSITE");
            if (Directory.Exists(compositeFolder)) segmentFolders.Add(compositeFolder);

            foreach (string monitorFolder in segmentFolders)
            {
                foreach (string file in Directory.GetFiles(monitorFolder, "seg_*.mp4"))
                {
                    try
                    {
                        DateTime t;
                        if (!TryParseSegmentTime(Path.GetFileNameWithoutExtension(file), out t)) continue;
                        if (t < cutoff) File.Delete(file);
                    }
                    catch { }
                }
            }
            }
        }

        private void StopProcesses_NoLock(bool stopClipProcesses)
        {
            _gracefulStop = true;

            List<Process> recorders = _processes.Values
                .Where(process => process != null)
                .Distinct()
                .ToList();

            // Signal every FFmpeg process before waiting for any one process.
            // This keeps shutdown time bounded when several monitors are active.
            foreach (Process process in recorders)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.StandardInput.WriteLine("q");
                        process.StandardInput.Flush();
                    }
                }
                catch
                {
                }
            }

            WaitForProcesses_NoLock(recorders, TimeSpan.FromSeconds(2));

            foreach (Process process in recorders)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                }
            }

            WaitForProcesses_NoLock(recorders, TimeSpan.FromSeconds(1));

            foreach (Process process in recorders)
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                }
            }

            _processes.Clear();

            if (!stopClipProcesses)
                return;

            StopClipProcesses_NoLock();
        }

        private void StopClipProcesses_NoLock()
        {
            List<Process> clips = _clipProcesses
                .Where(process => process != null)
                .Distinct()
                .ToList();

            foreach (Process process in clips)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                catch
                {
                }

                try
                {
                    process.Dispose();
                }
                catch
                {
                }
            }

            _clipProcesses.Clear();
        }

        private static void WaitForProcesses_NoLock(
            IEnumerable<Process> processes,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                bool anyRunning = false;

                foreach (Process process in processes)
                {
                    try
                    {
                        if (process != null && !process.HasExited)
                        {
                            anyRunning = true;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                if (!anyRunning)
                    return;

                Thread.Sleep(50);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                Interlocked.Increment(ref _stopGeneration);

                if (_cleanupTimer != null)
                {
                    try
                    {
                        _cleanupTimer.Dispose();
                    }
                    catch
                    {
                    }

                    _cleanupTimer = null;
                }

                if (!_running &&
                    _processes.Count == 0 &&
                    _clipProcesses.Count == 0)
                {
                    return;
                }

                RecordingStopTime = DateTime.Now;

                StopProcesses_NoLock(false);

                _running = false;

                _log?.Invoke(
                    "[PRE-ROLL] Recording process stopped.");
            }
        }

        public void Dispose()
        {
            lock (_sync)
                _disposed = true;

            Stop();
            lock (_sync)
                StopClipProcesses_NoLock();
        }

        private void ScheduleRestart_NoLock()
        {
            if (_restartAttempts >= 3)
            {
                _log?.Invoke("[PRE-ROLL] Automatic restart limit reached; check FFmpeg and monitor permissions.");
                return;
            }

            _restartAttempts++;
            int restartDelayMs = Math.Min(30000, 5000 * (1 << Math.Min(_restartAttempts - 1, 2)));
            _lastRestartUtc = DateTime.UtcNow;
            long generation = Interlocked.Read(ref _stopGeneration);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(restartDelayMs);
                if (Interlocked.Read(ref _stopGeneration) != generation)
                    return;

                lock (_sync)
                {
                    if (!_running)
                        Start();
                }
            });
        }

        private class SegmentGroup
        {
            public DateTime Timestamp;
            public Dictionary<int, string> Files =
                new Dictionary<int, string>();
        }
    }
    #region Trade Session

    public class TradeSession : IDisposable
    {
        public string TradeId { get; }
        public string InstrumentFullName { get; }
        public string AccountName { get; }
        public TradeDirection Direction { get; }
        public SessionState State { get; set; }
        public bool ExecutionCorrectionsRequireReview { get { return _executionCorrectionsRequireReview; } }
        public ContractSpec Spec { get; }
        public int PreTradeSeconds { get; }
        public int PostTradeSeconds { get; }

        // ---------------------------------------------------------
        // INITIAL IMMUTABLE TRADE SETUP
        // ---------------------------------------------------------

        public int InitialQuantity { get; private set; }
        public double InitialEntryPrice { get; private set; }
        public DateTime InitialEntryTime { get; private set; }
        // Local wall-clock timestamps are used by the screen recorder. They
        // intentionally remain separate from exchange execution timestamps.
        public DateTime CaptureEntryTime { get; private set; }
        public DateTime CaptureExitTime { get; private set; }
        public double InitialStopPrice { get; private set; }
        public double InitialRiskTicks { get; private set; }
        public double InitialRiskUSD { get; private set; }

        public string InitialRiskStatus { get; private set; }
        public string CommissionStatus { get; private set; }

        public int InitialEntryOrderQuantity { get; private set; }
        public string InitialEntryOrderId { get; private set; }

        public List<InitialTarget> InitialTargets
        {
            get;
        }

        // ---------------------------------------------------------
        // DYNAMIC STOP / TARGET HISTORY
        // ---------------------------------------------------------

        public double CurrentStopPrice { get; private set; }

        public List<PriceModificationRecord>
            StopModificationHistory { get; }

        public List<PriceModificationRecord>
            TargetModificationHistory { get; }

        // ---------------------------------------------------------
        // RAW EVENTS
        // ---------------------------------------------------------

        public List<FillRecord> AllFills
        {
            get;
        }

        public List<OrderEventRecord> OrderEvents
        {
            get;
        }

        // ---------------------------------------------------------
        // POSITION / PNL STATE
        // ---------------------------------------------------------

        public int TotalEntryQuantity
        {
            get;
            private set;
        }

        public int TotalExitQuantity
        {
            get;
            private set;
        }

        public int CurrentOpenQuantity
        {
            get
            {
                return Math.Max(
                    0,
                    TotalEntryQuantity -
                    TotalExitQuantity);
            }
        }

        public double WeightedAverageEntryPrice
        {
            get;
            private set;
        }

        public double WeightedAverageExitPrice
        {
            get;
            private set;
        }

        public DateTime LastExitTime
        {
            get;
            private set;
        }

        public long TotalRealizedTicks
        {
            get;
            private set;
        }

        public decimal TotalRealizedGrossPnL
        {
            get;
            private set;
        }

        public decimal TotalActualCommission
        {
            get;
            private set;
        }

        public bool HasActualCommission
        {
            get;
            private set;
        }

        public bool BootstrapIncomplete { get; private set; }
        public int BootstrapPositionQuantity { get; private set; }
        public double BootstrapAveragePrice { get; private set; }
        public DateTime BootstrapTimestamp { get; private set; }

        public double RealizedR
        {
            get;
            private set;
        }

        public string RealizedRStatus
        {
            get;
            private set;
        }

        public int UnmatchedExitQuantity
        {
            get;
            private set;
        }

        public int UnclassifiedFillQuantity
        {
            get;
            private set;
        }

        // ---------------------------------------------------------
        // MAE / MFE
        // ---------------------------------------------------------

        public double HighPriceSinceEntry
        {
            get;
            private set;
        }

        public double LowPriceSinceEntry
        {
            get;
            private set;
        }

        private readonly List<MarketSample>
            _marketSamples;

        private double _latestLast =
            double.NaN;

        private double _latestBid =
            double.NaN;

        private double _latestAsk =
            double.NaN;

        private readonly List<OpenEntryTracker>
            _openEntries;

        // Each physical target order is tracked independently.
        private readonly Dictionary<string, TargetOrderState>
            _targetOrders;

        // ---------------------------------------------------------
        // VIDEO
        // ---------------------------------------------------------

        public string DateFolderName { get; }
        public string FolderPath { get; }
        public string VideoFilePath { get; }

        public VideoRecordingInstance VideoRecorder
        {
            get;
        }

        // ---------------------------------------------------------
        // INTERNAL
        // ---------------------------------------------------------

        private readonly object _sessionLock =
            new object();

        private readonly Instrument _instrument;

        private DateTime _firstExitTime =
            DateTime.MinValue;

        private bool _firstEntryEstablished;

        private bool _initialRiskLocked;
        private bool _initialQuantityAuthoritative;
        private int _initialRiskQuantitySnapshot;
        private double _initialRiskEntrySnapshot = double.NaN;
        private double _initialRiskStopSnapshot = double.NaN;

        private int _commissionedFillCount;
        private readonly HashSet<string> _executionIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _executionIdentityRequiresReview;

        // Finalization is a journal commit, not merely a position-state change.
        private readonly object _logSync = new object();
        private sealed class DurableWriteRequest
        {
            public string Path;
            public string Content;
            public string HeaderIfEmpty;
            public bool Critical;
            public bool CreateNew;
            public ManualResetEvent Barrier;
        }

        private readonly BlockingCollection<DurableWriteRequest> _durableWriteQueue =
            new BlockingCollection<DurableWriteRequest>(new ConcurrentQueue<DurableWriteRequest>(), 8192);
        private readonly ManualResetEvent _durableWriterStopped = new ManualResetEvent(false);
        private Thread _durableWriterThread;
        private long _criticalDurableWriteFailures;
        private int _marketBatchSequence;
        private bool _finalizationInProgress;
        private bool _journalCommitted;
        private bool _checkpointCommitted;
        private volatile bool _executionCorrectionsRequireReview;
        private int _finalizationAttemptCount;

        private volatile bool _disposed;

        public TradeSession(
            string accountName,
            Instrument instrument,
            string instrumentFullName,
            TradeDirection direction,
            string rootFolder,
            int preTradeSeconds = 60,
            int postTradeSeconds = 5)
        {
            AccountName =
                accountName ?? "Account";

            _instrument =
                instrument;

            InstrumentFullName =
                instrumentFullName ??
                (instrument != null
                    ? instrument.FullName
                    : "Instrument");

            Direction =
                direction;

            PreTradeSeconds = Math.Max(60, Math.Min(600, preTradeSeconds));
            PostTradeSeconds = Math.Max(1, Math.Min(60, postTradeSeconds));

            State =
                SessionState.Active;

            InitialEntryTime =
                DateTime.MinValue;

            InitialEntryPrice =
                0.0;

            InitialQuantity =
                0;

            InitialEntryOrderQuantity =
                0;

            InitialRiskStatus =
                "UNKNOWN";

            CommissionStatus =
                "UNKNOWN";

            RealizedRStatus =
                "UNKNOWN";

            InitialTargets =
                new List<InitialTarget>();

            StopModificationHistory =
                new List<PriceModificationRecord>();

            TargetModificationHistory =
                new List<PriceModificationRecord>();

            AllFills =
                new List<FillRecord>();

            OrderEvents =
                new List<OrderEventRecord>();

            _openEntries =
                new List<OpenEntryTracker>();

            _targetOrders =
                new Dictionary<string, TargetOrderState>(
                    StringComparer.OrdinalIgnoreCase);

            _marketSamples =
                new List<MarketSample>();

            HighPriceSinceEntry =
                double.MinValue;

            LowPriceSinceEntry =
                double.MaxValue;

            Spec =
                ContractLookup.Resolve(
                    instrument,
                    instrumentFullName);

            DateFolderName =
                DateTime.Now.ToString(
                    "ddMMMMyyyy",
                    CultureInfo.InvariantCulture);

            string dateFolder =
                Path.Combine(
                    rootFolder,
                    DateFolderName);

            string suffix =
                Guid.NewGuid()
                    .ToString("N")
                    .Substring(0, 4)
                    .ToUpperInvariant();

            TradeId =
                $"{Spec.RootSymbol}_{Direction.ToString().ToUpperInvariant()}_" +
                $"{DateTime.Now:HHmmssfff}_{suffix}";

            FolderPath =
                Path.Combine(
                    dateFolder,
                    TradeId);

            VideoFilePath =
                Path.Combine(
                    FolderPath,
                    "Trade_Video.mp4");

            Directory.CreateDirectory(
                FolderPath);

            _durableWriterThread = new Thread(DurableWriterLoop);
            _durableWriterThread.IsBackground = true;
            _durableWriterThread.Name = "BEN Durable Writer " + TradeId;
            _durableWriterThread.Start();

            VideoRecorder =
                new VideoRecordingInstance();

            SubscribeMarketData();

            WriteLog(
                "=== Trade Session Created ===");

            WriteLog(
                $"TradeId={TradeId} | Instrument={InstrumentFullName} | Direction={Direction}");
            WriteLog("[SESSION IDENTITY] Current provider identity scope is Account|Instrument. Independent strategies sharing this exact scope cannot be distinguished reliably without a strategy/order-lineage identifier; do not run overlapping strategies on the same account/instrument until lineage support is validated.");

            WriteLog(
                $"ContractSpec: TickSize={Spec.TickSize} " +
                $"TickValue={Spec.CurrencySymbol}{Spec.TickValue:F4} " +
                $"Source={Spec.Source}");
        }

        public void MarkBootstrapIncomplete(int signedQuantity, double averagePrice, DateTime timestamp)
        {
            lock (_sessionLock)
            {
                BootstrapIncomplete = true;
                BootstrapPositionQuantity = signedQuantity;
                BootstrapAveragePrice = averagePrice;
                BootstrapTimestamp = timestamp;
                State = SessionState.Active;
                WriteLog("[POSITION BOOTSTRAP] Existing exposure observed without authoritative entry fills. " +
                    "Quantity=" + signedQuantity + " AveragePrice=" + averagePrice.ToString(CultureInfo.InvariantCulture) +
                    " Timestamp=" + FormatTime(timestamp) + ". PnL is not reconstructed.");
            }
        }

        // =========================================================
        // ORDER UPDATE
        // =========================================================

        public bool HasRecordedOrderId(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                return false;

            lock (_sessionLock)
            {
                return AllFills.Any(fill => string.Equals(
                           fill.OrderId, orderId, StringComparison.OrdinalIgnoreCase)) ||
                       OrderEvents.Any(order => string.Equals(
                           order.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void RegisterOrderUpdate(
            Order order,
            OrderEventArgs args)
        {
            if (order == null)
                return;

            lock (_sessionLock)
            {
                long sequence =
                    OrderEvents.Count == 0
                        ? 1
                        : OrderEvents[
                              OrderEvents.Count - 1].Sequence + 1;

                DateTime timestamp =
                    args != null
                        ? args.Time
                        : DateTime.Now;

                var record =
                    new OrderEventRecord
                    {
                        Sequence = sequence,
                        Timestamp = timestamp,
                        AccountName = AccountName,
                        InstrumentFullName =
                            InstrumentFullName,
                        OrderId =
                            order.OrderId ?? string.Empty,
                        OrderName =
                            order.Name ?? string.Empty,
                        Action =
                            order.OrderAction,
                        OrderType =
                            order.OrderType,
                        State =
                            args != null
                                ? args.OrderState
                                : order.OrderState,
                        LimitPrice =
                            args != null &&
                            args.LimitPrice > 0
                                ? args.LimitPrice
                                : order.LimitPrice,
                        StopPrice =
                            args != null &&
                            args.StopPrice > 0
                                ? args.StopPrice
                                : order.StopPrice,
                        Quantity =
                            args != null &&
                            args.Quantity > 0
                                ? args.Quantity
                                : order.Quantity,
                        Filled =
                            args != null &&
                            args.Filled > 0
                                ? args.Filled
                                : order.Filled,
                        AverageFillPrice =
                            args != null &&
                            args.AverageFillPrice > 0
                                ? args.AverageFillPrice
                                : order.AverageFillPrice,
                        Comment =
                            args != null
                                ? args.Comment ?? string.Empty
                                : string.Empty
                    };

                // Every relevant order event remains in the in-memory ledger and
                // is also appended to a durable per-trade event file.
                OrderEvents.Add(record);
                PersistOrderEvent(record);

                HandleEntryOrderFact(
                    order,
                    record);

                HandleExitOrderFact(
                    order,
                    record);
            }
        }

        private void HandleEntryOrderFact(
            Order order,
            OrderEventRecord record)
        {
            bool isEntryOrder =
                (Direction == TradeDirection.Long &&
                 order.OrderAction == OrderAction.Buy) ||

                // Short entries on this connection arrive as Sell.
                // The NinjaTrader order name can be empty.
                (Direction == TradeDirection.Short &&
                 (order.OrderAction == OrderAction.SellShort ||
                  order.OrderAction == OrderAction.Sell));

            if (!isEntryOrder)
                return;

            // The FIRST entry order establishes the intended
            // initial trade quantity.
            //
            // Example:
            //
            // one physical entry order = Qty 4
            // execution notifications =
            // 1 + 1 + 2
            //
            // InitialQuantity MUST be 4.
            if (string.IsNullOrWhiteSpace(order.OrderId))
            {
                InitialRiskStatus = "UNKNOWN - ENTRY ORDER ID MISSING";
                return;
            }

            if (string.IsNullOrWhiteSpace(
                    InitialEntryOrderId))
            {
                InitialEntryOrderId =
                    order.OrderId;

                if (record.Quantity > 0)
                {
                    InitialEntryOrderQuantity =
                        record.Quantity;

                    if (InitialQuantity == 0)
                    {
                        InitialQuantity =
                            record.Quantity;
                    }
                    _initialQuantityAuthoritative = true;
                }
            }
            else if (string.Equals(
                         InitialEntryOrderId,
                         order.OrderId,
                         StringComparison.OrdinalIgnoreCase))
            {
                if (record.Quantity > 0 && record.Quantity != InitialEntryOrderQuantity)
                {
                    int previousQuantity = InitialEntryOrderQuantity;
                    InitialEntryOrderQuantity = record.Quantity;
                    InitialQuantity = record.Quantity;
                    _initialQuantityAuthoritative = true;
                    WriteLog("[INITIAL ENTRY ORDER QUANTITY UPDATED] " + previousQuantity + " -> " +
                        InitialEntryOrderQuantity + " | Risk will be recalculated from the latest order snapshot.");
                }
            }

            if (_initialQuantityAuthoritative && InitialEntryPrice > 0 && InitialStopPrice > 0)
                TryLockRiskFromKnownFacts("Entry order quantity confirmed");
        }

        private void HandleExitOrderFact(
            Order order,
            OrderEventRecord record)
        {
            bool exitDirection =
                (Direction ==
                    TradeDirection.Long &&
                 order.OrderAction ==
                    OrderAction.Sell) ||

                (Direction ==
                    TradeDirection.Short &&
                 (order.OrderAction == OrderAction.BuyToCover ||
                  order.OrderAction == OrderAction.Buy));

            if (!exitDirection)
                return;

            bool isStop =
                order.OrderType ==
                    OrderType.StopMarket ||

                order.OrderType ==
                    OrderType.StopLimit ||

                record.StopPrice > 0 ||

                ContainsIgnoreCase(
                    record.OrderName,
                    "Stop") ||

                ContainsIgnoreCase(
                    record.OrderName,
                    "SL");

            if (isStop &&
                record.StopPrice > 0)
            {
                HandleStopFact(
                    record);
            }

            bool isTarget =
                (order.OrderType ==
                    OrderType.Limit) ||

                (order.OrderType ==
                    OrderType.MIT) ||

                ContainsIgnoreCase(
                    record.OrderName,
                    "Target") ||

                ContainsIgnoreCase(
                    record.OrderName,
                    "TP") ||

                ContainsIgnoreCase(
                    record.OrderName,
                    "Profit");

            if (isTarget &&
                record.LimitPrice > 0)
            {
                HandleTargetFact(
                    record);
            }
        }

        private void HandleStopFact(
            OrderEventRecord record)
        {
            if (!_initialRiskLocked)
            {
                // Stop arrived before Entry:
                // preserve raw stop now,
                // lock risk only when entry exists.
                InitialStopPrice =
                    record.StopPrice;

                CurrentStopPrice =
                    record.StopPrice;

                if (InitialEntryPrice > 0)
                {
                    LockInitialRisk(
                        record.StopPrice,
                        "OrderUpdate");
                }

                return;
            }

            if (CurrentStopPrice <= 0)
            {
                CurrentStopPrice =
                    record.StopPrice;

                return;
            }

            if (PricesEqual(
                    CurrentStopPrice,
                    record.StopPrice))
            {
                // Same state replay. Keep raw OrderEvent,
                // but do NOT create a modification.
                return;
            }

            string classification =
                DetermineStopAdjustmentType(
                    record.StopPrice);

            StopModificationHistory.Add(
                new PriceModificationRecord
                {
                    Timestamp =
                        record.Timestamp,
                    OrderId =
                        record.OrderId,
                    OrderName =
                        record.OrderName,
                    Type =
                        "Stop",
                    OldPrice =
                        CurrentStopPrice,
                    NewPrice =
                        record.StopPrice,
                    OldQuantity =
                        record.Quantity,
                    NewQuantity =
                        record.Quantity,
                    Note =
                        classification
                });

            WriteLog(
                $"[STOP MODIFIED] " +
                $"{CurrentStopPrice:F4} -> {record.StopPrice:F4} " +
                $"({classification}) | " +
                $"InitialRisk={InitialRiskTicks:F2} ticks");

            CurrentStopPrice =
                record.StopPrice;
        }

        private void HandleTargetFact(
            OrderEventRecord record)
        {
            string orderId =
                record.OrderId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(orderId))
                return;

            string logicalKey =
                BuildLogicalTargetKey(
                    record.OrderName,
                    record.LimitPrice);

            TargetOrderState state;

            if (!_targetOrders.TryGetValue(
                    orderId,
                    out state))
            {
                state =
                    new TargetOrderState(
                        orderId,
                        record.OrderName,
                        logicalKey,
                        record.LimitPrice,
                        Math.Max(0, record.Quantity),
                        record.Timestamp);

                _targetOrders.Add(
                    orderId,
                    state);

                WriteLog(
                    $"[TARGET ORDER OBSERVED] " +
                    $"{record.OrderName} " +
                    $"OrderId={orderId} " +
                    $"Qty={record.Quantity} " +
                    $"Price={record.LimitPrice:F4}");
            }
            else
            {
                bool priceChanged =
                    !PricesEqual(
                        state.CurrentLimitPrice,
                        record.LimitPrice);

                bool qtyChanged =
                    state.CurrentQuantity !=
                    record.Quantity;

                if (priceChanged ||
                    qtyChanged)
                {
                    double oldPrice =
                        state.CurrentLimitPrice;

                    int oldQty =
                        state.CurrentQuantity;

                    TargetModificationHistory.Add(
                        new PriceModificationRecord
                        {
                            Timestamp =
                                record.Timestamp,
                            OrderId =
                                record.OrderId,
                            OrderName =
                                record.OrderName,
                            Type =
                                "Target",
                            OldPrice =
                                oldPrice,
                            NewPrice =
                                record.LimitPrice,
                            OldQuantity =
                                oldQty,
                            NewQuantity =
                                record.Quantity,
                            Note =
                                "Target Modification"
                        });

                    state.CurrentLimitPrice =
                        record.LimitPrice;

                    state.CurrentQuantity =
                        record.Quantity;

                    WriteLog(
                        $"[TARGET MODIFIED] " +
                        $"{record.OrderName}: " +
                        $"Price {oldPrice:F4}->{record.LimitPrice:F4}, " +
                        $"Qty {oldQty}->{record.Quantity}");
                }
            }

            // IMPORTANT:
            //
            // InitialTargets are NOT mutated here.
            //
            // They are rebuilt from the physical target-order
            // snapshots at finalization.
            //
            // This allows:
            //
            // Target1 order A Qty1
            // Target1 order B Qty1
            //
            // => logical Target1 Qty2
            //
            // while retaining both physical order IDs.
        }

        private string BuildLogicalTargetKey(
            string name,
            double price)
        {
            string cleanName =
                (name ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            string priceKey =
                Math.Round(
                    price,
                    10,
                    MidpointRounding.AwayFromZero)
                .ToString(
                    "0.##########",
                    CultureInfo.InvariantCulture);

            return cleanName +
                   "|" +
                   priceKey;
        }

        // =========================================================
        // EXECUTION UPDATE
        // =========================================================

        public bool RegisterFill(
            Execution execution,
            Operation operation = Operation.Add,
            int quantityOverride = 0,
            bool? forceEntry = null,
            double commissionOverride = double.NaN)
        {
            if (execution == null)
                return false;

            lock (_sessionLock)
            {
                if (operation != Operation.Add)
                {
                    _executionCorrectionsRequireReview = true;
                    PersistExecutionCorrection(execution, operation);
                    WriteLog(
                        "[EXECUTION CORRECTION REQUIRES RECONCILIATION] Operation=" +
                        operation + " | ExecutionId=" + (execution.ExecutionId ?? string.Empty) +
                        " | PnL is marked UNKNOWN until execution history is reconciled.");
                    return false;
                }
                string executionId = execution.ExecutionId ?? string.Empty;

                if (string.IsNullOrWhiteSpace(executionId))
                {
                    // Do not create a random replacement ID: replaying this event
                    // would then look like a different fill and double-count PnL.
                    _executionIdentityRequiresReview = true;
                    WriteLog("INTEGRITY_ERROR ExecutionId is missing; event is retained but final accounting requires review.");
                }
                else if (!_executionIds.Add(executionId))
                {
                    WriteLog("[DUPLICATE EXECUTION IGNORED] ExecutionId=" + executionId);
                    return false;
                }

                Account account =
                    execution.Account;

                string accountName =
                    account != null
                        ? account.Name
                        : AccountName;

                string instrumentName =
                    execution.Instrument != null
                        ? execution.Instrument.FullName
                        : InstrumentFullName;

                bool isEntry = false;
                bool isClassified;
                if (forceEntry.HasValue)
                {
                    isEntry = forceEntry.Value;
                    isClassified = true;
                }
                else
                {
                    isClassified = execution.Order != null &&
                        TryClassifyAction(
                            execution.Order.OrderAction,
                            execution.MarketPosition,
                            execution.Order.Name,
                            out isEntry);
                }

                if (!isClassified)
                    isEntry = false;

                int acceptedQuantity = quantityOverride > 0
                    ? quantityOverride
                    : Math.Max(0, execution.Quantity);
                if (acceptedQuantity <= 0 || acceptedQuantity > Math.Max(0, execution.Quantity))
                {
                    WriteLog("INTEGRITY_ERROR Invalid execution quantity override. Original=" + execution.Quantity + " Override=" + quantityOverride);
                    _executionIdentityRequiresReview = true;
                    return false;
                }
                double acceptedCommission = double.IsNaN(commissionOverride)
                    ? execution.Commission
                    : commissionOverride;

                OrderAction action =
                    execution.Order != null
                        ? execution.Order.OrderAction
                        : default(OrderAction);

                string fillRole =
                    !isClassified
                        ? "Unclassified"
                        : isEntry
                            ? "Entry"
                            : "Exit";

                var fill =
                    new FillRecord(
                        executionId,
                        execution.Time,
                        accountName,
                        instrumentName,
                        execution.Order != null
                            ? execution.Order.OrderId
                            : string.Empty,
                        execution.Order != null
                            ? execution.Order.Name
                            : "Fill",
                        action,
                        execution.MarketPosition,
                        isEntry,
                        isClassified,
                        fillRole,
                        acceptedQuantity,
                        execution.Price,
                        acceptedCommission,
                        execution.Rate);

                AllFills.Add(fill);
                // Enqueue the raw execution before derived calculations. The finalization barrier
                // waits for the dedicated writer to durably flush this queue before commit.
                PersistFillEvent(fill);

                // Actual commission
                if (fill.Commission > 0)
                {
                    _commissionedFillCount++;
                    TotalActualCommission +=
                        Convert.ToDecimal(
                            fill.Commission,
                            CultureInfo.InvariantCulture);

                    HasActualCommission = true;
                    CommissionStatus = "KNOWN";
                }

                if (!isClassified)
                {
                    UnclassifiedFillQuantity +=
                        fill.Quantity;

                    WriteLog(
                        "INTEGRITY_ERROR Unclassified execution; " +
                        "Order was missing or action did not match trade direction. " +
                        $"ExecutionId={fill.ExecutionId}, Quantity={fill.Quantity}, " +
                        $"Action={(execution.Order != null ? execution.Order.OrderAction.ToString() : "UNKNOWN")}");
                }
                else if (isEntry)
                    ProcessEntryFill(fill);
                else
                    ProcessExitFill(fill);

                return true;
            }
        }

        private bool TryClassifyAction(
            OrderAction action,
            MarketPosition executionMarketPosition,
            string orderName,
            out bool isEntry)
        {
            isEntry = false;

            if (Direction == TradeDirection.Long)
            {
                if (action == OrderAction.Buy)
                {
                    isEntry = true;
                    return true;
                }

                if (action == OrderAction.Sell)
                    return true;

                return false;
            }

            if (Direction == TradeDirection.Short)
            {
                if (action == OrderAction.SellShort)
                {
                    isEntry = true;
                    return true;
                }

                if (action == OrderAction.BuyToCover)
                    return true;

                // Some providers expose a short-covering execution as Buy.
                // A post-fill Short or Flat state means this Buy reduced/closed
                // the existing short; a post-fill Long state is left to the
                // explicit reversal split path.
                if (action == OrderAction.Buy &&
                    (executionMarketPosition == MarketPosition.Short ||
                     executionMarketPosition == MarketPosition.Flat))
                    return true;

                // This connection reports short opening executions as Sell.
                // BuyToCover remains the short exit action.
                if (action == OrderAction.Sell &&
                    executionMarketPosition == MarketPosition.Short)
                {
                    isEntry = true;

                    WriteLog(
                        "[CLASSIFICATION FALLBACK] " +
                        "Sell + Short classified as SHORT ENTRY.");

                    return true;
                }

                return false;
            }

            return false;
        }

        private void ProcessEntryFill(
            FillRecord fill)
        {
            if (CaptureEntryTime == DateTime.MinValue)
                CaptureEntryTime = DateTime.Now;

            if (!_firstEntryEstablished)
            {
                _firstEntryEstablished =
                    true;

                InitialEntryTime =
                    fill.Time;

                InitialEntryPrice =
                    fill.Price;

                HighPriceSinceEntry =
                    fill.Price;

                LowPriceSinceEntry =
                    fill.Price;

                // If the first entry order quantity
                // is already known, use that.
                //
                // This fixes:
                //
                // Order Qty 4
                // Execution 1
                // Execution 1
                // Execution 2
                //
                // => InitialQuantity = 4
                if (InitialEntryOrderQuantity > 0)
                {
                    InitialQuantity =
                        InitialEntryOrderQuantity;
                    _initialQuantityAuthoritative = true;
                }
                else
                {
                    // This is a provisional display quantity only. Do not freeze
                    // InitialRisk until the first entry order quantity is known.
                    if (InitialQuantity == 0)
                        InitialQuantity = fill.Quantity;
                    _initialQuantityAuthoritative = false;
                    InitialRiskStatus = "PROVISIONAL - AWAITING ENTRY ORDER QUANTITY";
                }

                if (string.IsNullOrWhiteSpace(InitialEntryOrderId) && !string.IsNullOrWhiteSpace(fill.OrderId))
                    InitialEntryOrderId = fill.OrderId;

                TryLockRiskFromKnownFacts(
                    "Entry received");
            }
            else
            {
                // Do NOT increase InitialQuantity
                // merely because another entry fill arrives.
                //
                // Scale-ins are raw fills, not automatically
                // part of the original trade quantity.
            }

            TotalEntryQuantity +=
                fill.Quantity;

            decimal currentVolume =
                Convert.ToDecimal(
                    TotalEntryQuantity -
                    fill.Quantity) *
                Convert.ToDecimal(
                    WeightedAverageEntryPrice);

            decimal newVolume =
                Convert.ToDecimal(
                    fill.Quantity) *
                Convert.ToDecimal(
                    fill.Price);

            decimal totalQty =
                Convert.ToDecimal(
                    TotalEntryQuantity);

            WeightedAverageEntryPrice =
                totalQty > 0
                    ? Convert.ToDouble(
                        (currentVolume + newVolume) /
                        totalQty)
                    : 0.0;

            _openEntries.Add(
                new OpenEntryTracker(fill));

            // If the initial order update was received
            // after the execution, upgrade quantity here.
            if (InitialEntryOrderQuantity >
                InitialQuantity)
            {
                InitialQuantity =
                    InitialEntryOrderQuantity;

                TryLockRiskFromKnownFacts(
                    "Entry order quantity resolved");
            }

            WriteLog(
                $"[ENTRY FILL] " +
                $"{fill.Quantity} @ {fill.Price:F4} | " +
                $"Open={CurrentOpenQuantity} | " +
                $"AvgEntry={WeightedAverageEntryPrice:F4} | " +
                $"InitialQty={InitialQuantity}");
        }

        private void ProcessExitFill(
            FillRecord fill)
        {
            CaptureExitTime = DateTime.Now;

            LastExitTime =
                fill.Time;

            if (_firstExitTime ==
                DateTime.MinValue)
            {
                _firstExitTime =
                    fill.Time;
            }

            TotalExitQuantity +=
                fill.Quantity;

            decimal previousExitVolume =
                Convert.ToDecimal(
                    TotalExitQuantity -
                    fill.Quantity) *
                Convert.ToDecimal(
                    WeightedAverageExitPrice);

            decimal newExitVolume =
                Convert.ToDecimal(
                    fill.Quantity) *
                Convert.ToDecimal(
                    fill.Price);

            decimal totalExitQty =
                Convert.ToDecimal(
                    TotalExitQuantity);

            WeightedAverageExitPrice =
                totalExitQty > 0
                    ? Convert.ToDouble(
                        (previousExitVolume +
                         newExitVolume) /
                        totalExitQty)
                    : 0.0;

            int remaining =
                fill.Quantity;

            long contributionTicks = 0;

            decimal contributionPnL = 0m;

            while (remaining > 0 &&
                   _openEntries.Count > 0)
            {
                OpenEntryTracker tracker =
                    _openEntries[0];

                int matchQuantity =
                    Math.Min(
                        tracker.RemainingQuantity,
                        remaining);

                decimal priceDifference =
                    Direction ==
                    TradeDirection.Long
                        ? Convert.ToDecimal(
                            fill.Price -
                            tracker.SourceFill.Price)
                        : Convert.ToDecimal(
                            tracker.SourceFill.Price -
                            fill.Price);

                decimal ticksDecimal =
                    Spec.IsValid
                        ? priceDifference / Convert.ToDecimal(Spec.TickSize)
                        : 0m;

                long ticksPerContract =
                    (long)Math.Round(
                        ticksDecimal,
                        MidpointRounding.AwayFromZero);

                decimal pnl = Spec.IsValid
                    ? Convert.ToDecimal(ticksPerContract) *
                      Convert.ToDecimal(Spec.TickValue) *
                      Convert.ToDecimal(matchQuantity)
                    : 0m;

                contributionTicks +=
                    ticksPerContract *
                    matchQuantity;

                contributionPnL +=
                    pnl;

                remaining -=
                    matchQuantity;

                tracker.RemainingQuantity -=
                    matchQuantity;

                if (tracker.RemainingQuantity <= 0)
                {
                    _openEntries.RemoveAt(0);
                }
            }

            if (remaining > 0)
            {
                UnmatchedExitQuantity +=
                    remaining;

                WriteLog(
                    $"INTEGRITY_ERROR " +
                    $"Unmatched exit quantity={remaining} " +
                    $"for ExecutionId={fill.ExecutionId}");
            }

            TotalRealizedTicks +=
                contributionTicks;

            TotalRealizedGrossPnL +=
                contributionPnL;

            WriteLog(
                $"[EXIT FILL] " +
                $"{fill.Quantity} @ {fill.Price:F4} | " +
                $"MatchedTicks={contributionTicks} | " +
                $"GrossPnL={Spec.CurrencySymbol}{contributionPnL:F2} | " +
                $"Open={CurrentOpenQuantity}");
        }

        // =========================================================
        // INITIAL RISK
        // =========================================================

        private void TryLockRiskFromKnownFacts(
            string reason)
        {
            if (!Spec.IsValid)
            {
                InitialRiskStatus = "UNKNOWN - INVALID CONTRACT SPEC";
                return;
            }

            if (InitialEntryPrice <= 0)
                return;

            if (InitialStopPrice <= 0)
                return;

            if (InitialQuantity <= 0)
                return;

            if (!_initialQuantityAuthoritative)
            {
                InitialRiskStatus = "PROVISIONAL - AWAITING ENTRY ORDER QUANTITY";
                return;
            }

            LockInitialRisk(
                InitialStopPrice,
                reason);
        }

        private void LockInitialRisk(
            double stopPrice,
            string reason)
        {
            if (!Spec.IsValid || InitialQuantity <= 0)
            {
                InitialRiskStatus = "UNKNOWN - INVALID CONTRACT OR QUANTITY";
                return;
            }

            if (_initialRiskLocked &&
                _initialRiskQuantitySnapshot == InitialQuantity &&
                Math.Abs(_initialRiskEntrySnapshot - InitialEntryPrice) < 0.00000001 &&
                Math.Abs(_initialRiskStopSnapshot - stopPrice) < 0.00000001)
                return;

            bool isRecalculation = _initialRiskLocked;
            double signedRiskDistance;

            if (Direction ==
                TradeDirection.Long)
            {
                signedRiskDistance =
                    InitialEntryPrice -
                    stopPrice;
            }
            else
            {
                signedRiskDistance =
                    stopPrice -
                    InitialEntryPrice;
            }

            // Invalid risk geometry.
            if (signedRiskDistance <= 0)
            {
                _initialRiskLocked = false;
                _initialRiskQuantitySnapshot = 0;
                _initialRiskEntrySnapshot = double.NaN;
                _initialRiskStopSnapshot = double.NaN;
                InitialRiskTicks = 0;
                InitialRiskUSD = 0;
                InitialRiskStatus =
                    "UNKNOWN";

                WriteLog(
                    "INTEGRITY_ERROR Invalid initial stop geometry. " +
                    $"Entry={InitialEntryPrice:F4} " +
                    $"Stop={stopPrice:F4}");

                return;
            }

            InitialStopPrice =
                stopPrice;

            CurrentStopPrice =
                stopPrice;

            InitialRiskTicks =
                signedRiskDistance /
                Spec.TickSize;

            InitialRiskUSD =
                InitialRiskTicks *
                Spec.TickValue *
                InitialQuantity;

            InitialRiskStatus =
                "KNOWN";

            _initialRiskLocked = true;
            _initialRiskQuantitySnapshot = InitialQuantity;
            _initialRiskEntrySnapshot = InitialEntryPrice;
            _initialRiskStopSnapshot = stopPrice;

            WriteLog(
                (isRecalculation ? "[INITIAL RISK RECALCULATED] " : "[INITIAL RISK LOCKED] ") +
                $"Entry={InitialEntryPrice:F4} " +
                $"Stop={InitialStopPrice:F4} " +
                $"Risk={InitialRiskTicks:F2} ticks " +
                $"{Spec.CurrencySymbol}{InitialRiskUSD:F2} " +
                $"via {reason}");

            RecalculateDerivedTargets();
        }

        // =========================================================
        // TARGET DERIVATION
        // =========================================================

        private void RebuildInitialTargets()
        {
            var grouped =
                new Dictionary<string, TargetAccumulator>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (TargetOrderState state
                     in _targetOrders.Values)
            {
                if (state.InitialLimitPrice <= 0)
                    continue;

                TargetAccumulator accumulator;

                if (!grouped.TryGetValue(
                        state.LogicalKey,
                        out accumulator))
                {
                    accumulator =
                        new TargetAccumulator
                        {
                            LogicalKey =
                                state.LogicalKey,
                            Name =
                                state.Name,
                            LimitPrice =
                                state.InitialLimitPrice,
                            Quantity =
                                0,
                            FirstSeen =
                                state.FirstSeen
                        };

                    grouped.Add(
                        state.LogicalKey,
                        accumulator);
                }

                // IMPORTANT:
                // aggregate physical target orders
                // into the logical target plan.
                accumulator.Quantity +=
                    Math.Max(
                        0,
                        state.InitialQuantity);
            }

            var ordered =
                grouped.Values
                    .OrderBy(
                        x => x.FirstSeen)
                    .ThenBy(
                        x => x.LimitPrice)
                    .ToList();

            InitialTargets.Clear();

            foreach (TargetAccumulator acc
                     in ordered)
            {
                InitialTargets.Add(
                    new InitialTarget(
                        acc.LogicalKey,
                        acc.Name,
                        acc.LimitPrice,
                        acc.Quantity,
                        acc.FirstSeen));
            }

            RecalculateDerivedTargets();
        }

        private void RecalculateDerivedTargets()
        {
            foreach (InitialTarget target
                     in InitialTargets)
            {
                double rewardDistance =
                    Direction ==
                    TradeDirection.Long
                        ? target.LimitPrice -
                          InitialEntryPrice
                        : InitialEntryPrice -
                          target.LimitPrice;

                if (!Spec.IsValid)
                {
                    target.RewardTicks = 0;
                    target.ExpectedR = 0;
                    target.RewardUSD = 0;
                    continue;
                }

                target.RewardTicks =
                    rewardDistance /
                    Spec.TickSize;

                target.RewardUSD =
                    target.RewardTicks *
                    Spec.TickValue *
                    target.Quantity;

                target.ExpectedR =
                    InitialRiskTicks > 0
                        ? target.RewardTicks /
                          InitialRiskTicks
                        : 0;
            }
        }

        private class TargetAccumulator
        {
            public string LogicalKey;
            public string Name;
            public double LimitPrice;
            public int Quantity;
            public DateTime FirstSeen;
        }

        // =========================================================
        // REALIZED R
        // =========================================================

        private void CalculateRealizedR()
        {
                        if (!Spec.IsValid || _executionCorrectionsRequireReview ||
                _executionIdentityRequiresReview || InitialRiskTicks <= 0 ||
                InitialQuantity <= 0 ||
                Spec.TickSize <= 0)
            {
                RealizedRStatus =
                    "UNKNOWN";

                RealizedR =
                    0.0;

                return;
            }

            decimal totalRContribution =
                0m;

            int accountedExitQuantity =
                0;

            foreach (FillRecord fill
                     in AllFills)
            {
                if (!fill.IsClassified || fill.IsEntry)
                    continue;

                if (fill.Quantity <= 0)
                    continue;

                double directionalTicks =
                    Direction ==
                    TradeDirection.Long
                        ? (fill.Price -
                           InitialEntryPrice) /
                          Spec.TickSize
                        : (InitialEntryPrice -
                           fill.Price) /
                          Spec.TickSize;

                decimal exitR =
                    Convert.ToDecimal(
                        directionalTicks) /
                    Convert.ToDecimal(
                        InitialRiskTicks);

                totalRContribution +=
                    exitR *
                    Convert.ToDecimal(
                        fill.Quantity);

                accountedExitQuantity +=
                    fill.Quantity;
            }

            RealizedR =
                Convert.ToDouble(
                    totalRContribution /
                    Convert.ToDecimal(
                        InitialQuantity));

            if (UnmatchedExitQuantity > 0)
            {
                RealizedRStatus =
                    "PARTIALLY_KNOWN";
            }
            else
            {
                RealizedRStatus =
                    "KNOWN";
            }
        }

        // =========================================================
        // COMMISSION
        // =========================================================

        private decimal CalculateNetPnL(
            out string status)
        {
            int classifiedFillCount =
                AllFills.Count(x => x.IsClassified);

                        if (!Spec.IsValid || _executionCorrectionsRequireReview ||
                _executionIdentityRequiresReview || UnclassifiedFillQuantity > 0 ||
                UnmatchedExitQuantity > 0 ||
                classifiedFillCount == 0)
            {
                status = "UNKNOWN";
                CommissionStatus =
                    _commissionedFillCount > 0
                        ? "PARTIAL"
                        : "UNKNOWN";
                return 0m;
            }

            if (_commissionedFillCount == classifiedFillCount)
            {
                status =
                    "KNOWN";

                CommissionStatus =
                    "KNOWN";

                return
                    TotalRealizedGrossPnL -
                    TotalActualCommission;
            }

            status =
                _commissionedFillCount > 0
                    ? "PARTIAL"
                    : "UNKNOWN";

            CommissionStatus = status;

            // Numeric result deliberately unavailable.
            // Caller must NOT display this as actual zero commission.
            return 0m;
        }

        // =========================================================
        // MARKET DATA
        // =========================================================

        private void SubscribeMarketData()
        {
            try
            {
                if (_instrument != null &&
                    _instrument.MarketData != null)
                {
                    _instrument.MarketData.Update +=
                        OnMarketDataUpdate;
                }
            }
            catch (Exception ex)
            {
                WriteLog(
                    "[MarketData Subscription Warning] " +
                    ex.Message);
            }
        }

        private void UnsubscribeMarketData()
        {
            try
            {
                if (_instrument != null &&
                    _instrument.MarketData != null)
                {
                    _instrument.MarketData.Update -=
                        OnMarketDataUpdate;
                }
            }
            catch
            {
            }
        }

        private void OnMarketDataUpdate(
            object sender,
            MarketDataEventArgs e)
        {
            if (_disposed ||
                e == null)
                return;

            lock (_sessionLock)
            {
                if (e.MarketDataType ==
                    MarketDataType.Last)
                {
                    if (e.Price > 0)
                    {
                        _latestLast =
                            e.Price;

                        // CRITICAL:
                        //
                        // Every valid Last event updates
                        // MAE/MFE extrema.
                        //
                        // The CSV throttle does NOT control
                        // the extrema calculation.
                        if (InitialEntryPrice > 0 &&
                            InitialEntryTime !=
                                DateTime.MinValue &&
                            e.Time >= InitialEntryTime)
                        {
                            if (HighPriceSinceEntry ==
                                double.MinValue ||
                                e.Price >
                                HighPriceSinceEntry)
                            {
                                HighPriceSinceEntry =
                                    e.Price;
                            }

                            if (LowPriceSinceEntry ==
                                double.MaxValue ||
                                e.Price <
                                LowPriceSinceEntry)
                            {
                                LowPriceSinceEntry =
                                    e.Price;
                            }
                        }

                        _marketSamples.Add(
                            new MarketSample(
                                e.Time,
                                e.Price,
                                !double.IsNaN(
                                    e.Bid)
                                    ? e.Bid
                                    : _latestBid,
                                !double.IsNaN(
                                    e.Ask)
                                    ? e.Ask
                                    : _latestAsk,
                                e.Volume));

                        SpillMarketSamplesIfNeeded_NoLock();
                    }
                }
                else if (e.MarketDataType ==
                         MarketDataType.Bid)
                {
                    if (e.Price > 0)
                        _latestBid =
                            e.Price;

                    AddQuoteArchiveSample(
                        e.Time);
                }
                else if (e.MarketDataType ==
                         MarketDataType.Ask)
                {
                    if (e.Price > 0)
                        _latestAsk =
                            e.Price;

                    AddQuoteArchiveSample(
                        e.Time);
                }
            }
        }

        private void AddQuoteArchiveSample(
            DateTime time)
        {
            // Quotes are archival only.
            // They NEVER affect MAE/MFE.
            _marketSamples.Add(
                new MarketSample(
                    time,
                    _latestLast,
                    _latestBid,
                    _latestAsk,
                    0));

            SpillMarketSamplesIfNeeded_NoLock();
        }

        private void SpillMarketSamplesIfNeeded_NoLock()
        {
            const int batchSize = 5000;
            if (_marketSamples.Count < batchSize)
                return;

            List<MarketSample> batch = _marketSamples.GetRange(0, batchSize);
            string[] rows = batch.Select(sample =>
                FormatTimeCsv(sample.Time) + "," + FormatMarketPrice(sample.Last) + "," +
                FormatMarketPrice(sample.Bid) + "," + FormatMarketPrice(sample.Ask) + "," +
                sample.Volume.ToString(CultureInfo.InvariantCulture)).ToArray();
            string content = string.Join(Environment.NewLine, rows) + Environment.NewLine;
            string batchPath = Path.Combine(FolderPath,
                "TradeMarket.batch_" + Interlocked.Increment(ref _marketBatchSequence).ToString("D8", CultureInfo.InvariantCulture) + ".csv");

            if (QueueDurableWrite(batchPath, content, null, "MarketData batch", true, true))
            {
                _marketSamples.RemoveRange(0, batchSize);
            }
            else
            {
                // Keep the in-memory samples if both queue and synchronous fallback fail.
                WriteLog("[MARKET DATA SPOOL ERROR] Batch could not be persisted; samples remain buffered.");
            }
        }

        public void RecordLastPriceSample(
            DateTime time,
            double price,
            double bid,
            double ask,
            long volume = 0)
        {
            if (price <= 0)
                return;

            lock (_sessionLock)
            {
                if (InitialEntryPrice > 0)
                {
                    if (HighPriceSinceEntry ==
                        double.MinValue ||
                        price >
                        HighPriceSinceEntry)
                    {
                        HighPriceSinceEntry =
                            price;
                    }

                    if (LowPriceSinceEntry ==
                        double.MaxValue ||
                        price <
                        LowPriceSinceEntry)
                    {
                        LowPriceSinceEntry =
                            price;
                    }
                }

                _marketSamples.Add(
                    new MarketSample(
                        time,
                        price,
                        bid,
                        ask,
                        volume));

                SpillMarketSamplesIfNeeded_NoLock();
            }
        }

        // =========================================================
        // VIDEO
        // =========================================================

        public void StartRecording(
            BENTradeRecorderSettings settings)
        {
            string ffmpeg =
                FFmpegFinder.ResolveFFmpegPath(
                    settings.FFmpegPath);

            bool started =
                VideoRecorder.Start(
                    ffmpeg,
                    VideoFilePath,
                    settings.FrameRate,
                    settings.VideoQuality,
                    settings.SelectedEncoder,
                    settings.SelectedMonitors,
                    settings.LayoutMode,
                    WriteLog);

            if (started)
            {
                WriteLog(
                    $"Video Recording Active ({settings.LayoutMode}).");
            }
            else
            {
                WriteLog(
                    "WARNING: Video recording could not be started.");
            }
        }

        // =========================================================
        // FINALIZATION
        // =========================================================

        public bool FinalizeAndGenerateJournal(string exitReason, bool terminalCommit = true)
        {
            lock (_sessionLock)
            {
                if (_journalCommitted || (!terminalCommit && _checkpointCommitted))
                    return true;

                SessionState stateBeforeAttempt = State;
                SessionState checkpointState = stateBeforeAttempt == SessionState.Completed
                    ? (CurrentOpenQuantity > 0 ? SessionState.Active : SessionState.Closing)
                    : stateBeforeAttempt;

                if (_finalizationInProgress)
                    return false;

                _finalizationInProgress = true;
                _finalizationAttemptCount++;
                string reason = string.IsNullOrWhiteSpace(exitReason)
                    ? "Unspecified"
                    : exitReason;

                try
                {
                    WriteLog(
                        "=== FINALIZATION ATTEMPT STARTED === " +
                        "Attempt=" + _finalizationAttemptCount +
                        " | Reason=" + reason +
                        " | State=" + State);
                    // Keep State=Closing until disk artifacts are actually committed.
                    // A failure here must never disable the shutdown checkpoint.
                    State = SessionState.Closing;

                    // Write the pending marker BEFORE any potentially long operation.
                    // A forced process kill will therefore leave a durable recovery flag.
                    if (!WritePendingFinalization(reason, "IN_PROGRESS: finalization began; terminal journal commit has not yet completed."))
                        throw new IOException("The durable TradeFinalization.pending marker could not be committed. Finalization was stopped before journal mutation.");

                    bool calculationsOk = true;
                    RunFinalizationStep("VideoRecorder.Stop", () => VideoRecorder?.Stop());
                    calculationsOk &= RunFinalizationStep("ResolveInitialQuantityFallback", ResolveInitialQuantityFallback);
                    calculationsOk &= RunFinalizationStep("RebuildInitialTargets", RebuildInitialTargets);
                    calculationsOk &= RunFinalizationStep("TryLockRiskFromKnownFacts", () => TryLockRiskFromKnownFacts("Finalization"));
                    calculationsOk &= RunFinalizationStep("CalculateRealizedR", CalculateRealizedR);
                    calculationsOk &= RunFinalizationStep("FlushDurableEventWrites", () =>
                    {
                        if (!FlushDurableWrites(TimeSpan.FromSeconds(30)))
                            throw new IOException("Durable event writer did not flush cleanly; financial commit is blocked.");
                    });

                    if (!HasActualCommission)
                        CommissionStatus = "UNKNOWN";

                    IntegrityAuditResult audit;
                    try
                    {
                        audit = RunIntegrityAudit();
                    }
                    catch (Exception auditException)
                    {
                        calculationsOk = false;
                        audit = new IntegrityAuditResult { Passed = false };
                        audit.Errors.Add("Integrity audit threw: " + auditException.Message);
                        WriteDiagnostic("RunIntegrityAudit", auditException);
                    }

                    WriteLog(
                        "=== FINALIZING TRADE === " +
                        "Reason=" + reason +
                        " | GrossPnL=" + Spec.CurrencySymbol + TotalRealizedGrossPnL.ToString("F2", CultureInfo.InvariantCulture) +
                        " | RealizedR=" + (RealizedRStatus == "KNOWN"
                            ? RealizedR.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)
                            : "Unknown"));

                    WriteLog(audit.Passed ? "INTEGRITY_AUDIT=PASS" : "INTEGRITY_AUDIT=FAIL");
                    foreach (string auditError in audit.Errors)
                        WriteLog("INTEGRITY_ERROR " + auditError);

                    // A flat PositionUpdate alone is NOT sufficient to commit a trade.
                    // Terminal state requires a balanced, classified, reconciled fill ledger.
                    bool terminalLedgerEligible =
                        TotalEntryQuantity > 0 &&
                        TotalExitQuantity == TotalEntryQuantity &&
                        CurrentOpenQuantity == 0 &&
                        UnmatchedExitQuantity == 0 &&
                        UnclassifiedFillQuantity == 0 &&
                        !_executionCorrectionsRequireReview &&
                        !_executionIdentityRequiresReview &&
                        !BootstrapIncomplete &&
                        Spec.IsValid &&
                        audit.Passed;

                    if (terminalCommit && !terminalLedgerEligible)
                    {
                        calculationsOk = false;
                        WriteLog(
                            "[FINALIZATION BLOCKED] Terminal commit refused: " +
                            "execution ledger is not proven flat and clean. " +
                            "EntryQty=" + TotalEntryQuantity +
                            " | ExitQty=" + TotalExitQuantity +
                            " | OpenQty=" + CurrentOpenQuantity +
                            " | UnmatchedExitQty=" + UnmatchedExitQuantity +
                            " | UnclassifiedQty=" + UnclassifiedFillQuantity +
                            " | CorrectionsRequireReview=" + _executionCorrectionsRequireReview +
                            " | ExecutionIdentityReview=" + _executionIdentityRequiresReview +
                            " | ContractSpecValid=" + Spec.IsValid +
                            " | AuditPassed=" + audit.Passed);
                    }

                    // Keep collecting Last/Bid/Ask while the execution ledger still
                    // shows an open position. Stop market-data intake only for a
                    // confirmed-flat trade or a terminal shutdown checkpoint.
                    if (!terminalCommit || (TotalEntryQuantity > 0 && CurrentOpenQuantity == 0))
                        calculationsOk &= RunFinalizationStep("UnsubscribeMarketData", UnsubscribeMarketData);

                    // TradeInfo is written first so that a later CSV/video failure
                    // cannot leave the folder without the primary import manifest.
                    bool tradeInfoOk = GenerateTradeInfoFile(reason, audit);
                    bool executionsOk = GenerateTradeExecutionsCsv();
                    bool ordersOk = GenerateTradeOrdersCsv();
                    bool marketOk = GenerateTradeMarketCsv();

                    bool artifactsOk = tradeInfoOk && executionsOk && ordersOk && marketOk;
                    if (artifactsOk && calculationsOk)
                    {
                        if (terminalCommit)
                        {
                            State = SessionState.Completed;

                            // Persist the terminal state only after every required artifact
                            // has already been written successfully.
                            if (GenerateTradeInfoFile(reason, audit))
                            {
                                _journalCommitted = true;
                                _checkpointCommitted = false;
                                TryDeleteFile(Path.Combine(FolderPath, "TradeMarket.spool.csv"));
                                TryDeleteMarketBatchFiles();
                                TryDeleteFile(Path.Combine(FolderPath, "TradeFinalization.pending"));
                                WriteLog("=== JOURNAL COMMIT SUCCEEDED ===");
                                return true;
                            }

                            State = SessionState.Closing;
                        }
                        else
                        {
                            // Shutdown while a position is still open is a checkpoint,
                            // not a completed trade. Preserve the prior lifecycle state.
                            State = checkpointState;
                            if (GenerateTradeInfoFile(reason, audit))
                            {
                                _checkpointCommitted = true;
                                WritePendingFinalization(reason, "CHECKPOINT_ONLY: trade was not declared flat or complete.");
                                WriteLog("=== SHUTDOWN CHECKPOINT COMMITTED; TRADE REMAINS INCOMPLETE ===");
                                return true;
                            }
                            State = checkpointState;
                        }
                    }

                    string failureSummary =
                        "TradeInfo=" + tradeInfoOk +
                        "; Executions=" + executionsOk +
                        "; Orders=" + ordersOk +
                        "; Market=" + marketOk +
                        "; Calculations=" + calculationsOk;

                    WritePendingFinalization(reason, failureSummary);
                    WriteLog("=== JOURNAL COMMIT PENDING === " + failureSummary);
                    return false;
                }
                catch (Exception ex)
                {
                    State = terminalCommit ? SessionState.Closing : checkpointState;
                    WriteDiagnostic("FinalizeAndGenerateJournal", ex);
                    WritePendingFinalization(reason, "Unhandled finalization exception: " + ex.Message);
                    WriteLog("=== JOURNAL COMMIT FAILED; RETRY REQUIRED === " + ex.Message);
                    return false;
                }
                finally
                {
                    _finalizationInProgress = false;
                }
            }
        }

        private bool RunFinalizationStep(string stage, Action action)
        {
            try
            {
                action();
                WriteLog("[FINALIZATION STEP OK] " + stage);
                return true;
            }
            catch (Exception ex)
            {
                WriteDiagnostic(stage, ex);
                WriteLog("[FINALIZATION STEP FAILED] " + stage + " | " + ex.Message);
                return false;
            }
        }

        private bool WritePendingFinalization(string reason, string details)
        {
            try
            {
                string content =
                    "TradeId=" + TradeId + Environment.NewLine +
                    "TimestampUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Reason=" + (reason ?? string.Empty) + Environment.NewLine +
                    "State=" + State + Environment.NewLine +
                    "Attempt=" + _finalizationAttemptCount.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Details=" + (details ?? string.Empty) + Environment.NewLine;

                bool written = WriteTextAtomic(Path.Combine(FolderPath, "TradeFinalization.pending"), content);
                if (!written)
                    WriteDiagnosticDirect("WritePendingFinalization", new IOException("Atomic pending marker write returned false."));
                return written;
            }
            catch (Exception ex)
            {
                WriteDiagnostic("WritePendingFinalization", ex);
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Deleting a stale marker is non-critical; the journal files remain authoritative.
            }
        }
        private void ResolveInitialQuantityFallback()
        {
            if (InitialEntryOrderQuantity > 0)
            {
                InitialQuantity = InitialEntryOrderQuantity;
                _initialQuantityAuthoritative = true;
                return;
            }

            // Reconstruct only the quantity attributed to the first physical
            // entry order. Summing every entry would incorrectly absorb scale-ins.
            if (string.IsNullOrWhiteSpace(InitialEntryOrderId))
            {
                FillRecord firstEntry = AllFills.FirstOrDefault(x => x.IsClassified && x.IsEntry &&
                    !string.IsNullOrWhiteSpace(x.OrderId));
                if (firstEntry != null)
                    InitialEntryOrderId = firstEntry.OrderId;
            }

            if (!string.IsNullOrWhiteSpace(InitialEntryOrderId))
            {
                int firstOrderFilledQuantity = AllFills
                    .Where(x => x.IsClassified && x.IsEntry &&
                        string.Equals(x.OrderId, InitialEntryOrderId, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.Quantity);

                if (firstOrderFilledQuantity > 0)
                {
                    InitialQuantity = firstOrderFilledQuantity;
                    _initialQuantityAuthoritative = true;
                    WriteLog("[INITIAL QUANTITY RESOLVED FROM EXECUTION LEDGER] OrderId=" +
                        InitialEntryOrderId + " Quantity=" + firstOrderFilledQuantity);
                    return;
                }
            }

            _initialQuantityAuthoritative = false;
            InitialRiskStatus = "UNKNOWN - INITIAL ENTRY QUANTITY NOT RECONCILED";
        }

        // =========================================================
        // INTEGRITY AUDIT
        // =========================================================

        public class IntegrityAuditResult
        {
            public bool Passed;
            public List<string> Errors =
                new List<string>();
        }

        private IntegrityAuditResult RunIntegrityAudit()
        {
            var result =
                new IntegrityAuditResult
                {
                    Passed = true
                };

            var executionIds =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (FillRecord fill
                     in AllFills)
            {
                if (!executionIds.Add(
                        fill.ExecutionId))
                {
                    result.Passed = false;

                    result.Errors.Add(
                        $"Duplicate ExecutionId={fill.ExecutionId}");
                }

                if (fill.Quantity < 0)
                {
                    result.Passed = false;

                    result.Errors.Add(
                        $"Negative fill quantity for ExecutionId={fill.ExecutionId}");
                }
            }

            if (TotalExitQuantity >
                TotalEntryQuantity)
            {
                result.Passed = false;

                result.Errors.Add(
                    $"Exit quantity {TotalExitQuantity} > " +
                    $"entry quantity {TotalEntryQuantity}.");
            }

            if (UnmatchedExitQuantity > 0)
            {
                result.Passed = false;

                result.Errors.Add(
                    $"UnmatchedExitQuantity={UnmatchedExitQuantity}");
            }

            if (UnclassifiedFillQuantity > 0)
            {
                result.Passed = false;
                result.Errors.Add(
                    $"UnclassifiedFillQuantity={UnclassifiedFillQuantity}");
            }

            long recalculatedTicks = 0;
            decimal recalculatedPnL = 0m;

            var queue =
                new List<OpenEntryTracker>();

            foreach (FillRecord fill
                     in AllFills)
            {
                if (!fill.IsClassified)
                    continue;

                if (fill.IsEntry)
                {
                    queue.Add(
                        new OpenEntryTracker(fill));

                    continue;
                }

                int remaining =
                    fill.Quantity;

                while (remaining > 0 &&
                       queue.Count > 0)
                {
                    OpenEntryTracker entry =
                        queue[0];

                    int matched =
                        Math.Min(
                            remaining,
                            entry.RemainingQuantity);

                    double diff =
                        Direction ==
                        TradeDirection.Long
                            ? fill.Price -
                              entry.SourceFill.Price
                            : entry.SourceFill.Price -
                              fill.Price;

                    if (Spec.IsValid)
                    {
                        long ticks = (long)Math.Round(
                            diff / Spec.TickSize,
                            MidpointRounding.AwayFromZero);
                        recalculatedTicks += ticks * matched;
                        recalculatedPnL += Convert.ToDecimal(ticks) *
                            Convert.ToDecimal(Spec.TickValue) * Convert.ToDecimal(matched);
                    }

                    remaining -= matched;

                    entry.RemainingQuantity -=
                        matched;

                    if (entry.RemainingQuantity <= 0)
                        queue.RemoveAt(0);
                }
            }

            if (Spec.IsValid && recalculatedTicks !=
                TotalRealizedTicks)
            {
                result.Passed = false;

                result.Errors.Add(
                    $"Realized tick mismatch. " +
                    $"Ledger={TotalRealizedTicks}, " +
                    $"Recalculated={recalculatedTicks}");
            }

            decimal pnlTolerance =
                0.0001m;

            if (Spec.IsValid && Math.Abs(
                    recalculatedPnL -
                    TotalRealizedGrossPnL) >
                pnlTolerance)
            {
                result.Passed = false;

                result.Errors.Add(
                    $"Gross PnL mismatch. " +
                    $"Ledger={TotalRealizedGrossPnL:F4}, " +
                    $"Recalculated={recalculatedPnL:F4}");
            }

            if (InitialQuantity <= 0 &&
                TotalEntryQuantity > 0)
            {
                result.Passed = false;

                result.Errors.Add(
                    "InitialQuantity is unresolved.");
            }

            if (BootstrapIncomplete)
            {
                result.Passed = false;
                result.Errors.Add("Position was already open when Recorder subscribed; entry fills were not reconstructed.");
            }

            if (_executionIdentityRequiresReview)
            {
                result.Passed = false;
                result.Errors.Add("One or more executions did not provide a stable ExecutionId.");
            }

            if (!Spec.IsValid)
            {
                result.Passed = false;
                result.Errors.Add("ContractSpec is invalid; PnL, tick values and risk cannot be trusted.");
            }

            if (_executionCorrectionsRequireReview)
            {
                result.Passed = false;
                result.Errors.Add(
                    "ExecutionUpdate reported Update/Remove. The current in-memory fill ledger is not authoritative until reconciled.");
            }

            // Realized R must use initial quantity,
            // not current open quantity.
            if (InitialQuantity > 0 &&
                InitialRiskTicks > 0)
            {
                double expectedR =
                    CalculateExpectedRealizedRFromRawFills();

                if (Math.Abs(
                        expectedR -
                        RealizedR) >
                    0.000001)
                {
                    result.Passed = false;

                    result.Errors.Add(
                        $"RealizedR mismatch. " +
                        $"Expected={expectedR:F6}, " +
                        $"Actual={RealizedR:F6}");
                }
            }

            return result;
        }

        private double
            CalculateExpectedRealizedRFromRawFills()
        {
            if (!Spec.IsValid || InitialQuantity <= 0 ||
                InitialRiskTicks <= 0)
            {
                return 0.0;
            }

            decimal contribution = 0m;

            foreach (FillRecord fill
                     in AllFills)
            {
                if (!fill.IsClassified || fill.IsEntry)
                    continue;

                double ticks =
                    Direction ==
                    TradeDirection.Long
                        ? (fill.Price -
                           InitialEntryPrice) /
                          Spec.TickSize
                        : (InitialEntryPrice -
                           fill.Price) /
                          Spec.TickSize;

                contribution +=
                    Convert.ToDecimal(ticks) /
                    Convert.ToDecimal(
                        InitialRiskTicks) *
                    Convert.ToDecimal(
                        fill.Quantity);
            }

            return Convert.ToDouble(
                contribution /
                Convert.ToDecimal(
                    InitialQuantity));
        }

        // =========================================================
        // TRADE INFO
        // =========================================================

        private bool GenerateTradeInfoFile(string exitReason, IntegrityAuditResult audit)
        {
            try
            {
                string path = Path.Combine(FolderPath, "TradeInfo.txt");
                TimeSpan duration = InitialEntryTime != DateTime.MinValue && LastExitTime > InitialEntryTime
                    ? LastExitTime - InitialEntryTime
                    : TimeSpan.Zero;

                double mfeTicks = 0.0;
                double maeTicks = 0.0;
                if (Spec.IsValid && InitialEntryPrice > 0 && HighPriceSinceEntry != double.MinValue && LowPriceSinceEntry != double.MaxValue)
                {
                    if (Direction == TradeDirection.Long)
                    {
                        mfeTicks = Math.Max(0.0, (HighPriceSinceEntry - InitialEntryPrice) / Spec.TickSize);
                        maeTicks = Math.Max(0.0, (InitialEntryPrice - LowPriceSinceEntry) / Spec.TickSize);
                    }
                    else
                    {
                        mfeTicks = Math.Max(0.0, (InitialEntryPrice - LowPriceSinceEntry) / Spec.TickSize);
                        maeTicks = Math.Max(0.0, (HighPriceSinceEntry - InitialEntryPrice) / Spec.TickSize);
                    }
                }

                decimal mfeUsd = Spec.IsValid ? Convert.ToDecimal(mfeTicks) * Convert.ToDecimal(Spec.TickValue) * Convert.ToDecimal(Math.Max(0, InitialQuantity)) : 0m;
                decimal maeUsd = Spec.IsValid ? Convert.ToDecimal(maeTicks) * Convert.ToDecimal(Spec.TickValue) * Convert.ToDecimal(Math.Max(0, InitialQuantity)) : 0m;
                string commissionStatus;
                decimal netPnL = CalculateNetPnL(out commissionStatus);
                string videoOffset = CalculateVideoOffsetText();

                var sb = new StringBuilder();
                sb.AppendLine("=================================================");
                sb.AppendLine("         BEN TRADE VIDEO RECORDER JOURNAL       ");
                sb.AppendLine("=================================================");
                sb.AppendLine($"TradeId:                     {TradeId}");
                sb.AppendLine($"DateFolder:                  {DateFolderName}");
                sb.AppendLine($"Account:                     {AccountName}");
                sb.AppendLine($"Instrument:                  {InstrumentFullName}");
                sb.AppendLine($"RootSymbol:                  {Spec.RootSymbol}");
                sb.AppendLine($"Direction:                   {Direction.ToString().ToUpperInvariant()}");
                sb.AppendLine($"SessionState:                {State}");
                sb.AppendLine($"ExitReason:                  {exitReason}");
                sb.AppendLine("-------------------------------------------------");
                sb.AppendLine($"InitialQuantity:             {(BootstrapIncomplete ? "UNKNOWN - BOOTSTRAP" : InitialQuantity.ToString(CultureInfo.InvariantCulture))}");
                sb.AppendLine($"PositionBootstrapStatus:     {(BootstrapIncomplete ? "INCOMPLETE - ENTRY HISTORY NOT AVAILABLE" : "NOT REQUIRED")}");
                sb.AppendLine($"BootstrapPositionQuantity:   {(BootstrapIncomplete ? BootstrapPositionQuantity.ToString(CultureInfo.InvariantCulture) : "N/A")}");
                sb.AppendLine($"BootstrapAveragePrice:       {(BootstrapIncomplete ? FormatPrice(BootstrapAveragePrice) : "N/A")}");
                sb.AppendLine($"BootstrapTimestamp:          {(BootstrapIncomplete ? FormatTime(BootstrapTimestamp) : "N/A")}");
                sb.AppendLine($"InitialEntryOrderId:         {InitialEntryOrderId}");
                sb.AppendLine($"InitialEntryOrderQuantity:   {InitialEntryOrderQuantity}");
                sb.AppendLine($"InitialEntryPrice:           {(_executionCorrectionsRequireReview ? "UNKNOWN" : FormatPrice(InitialEntryPrice))}");
                sb.AppendLine($"InitialEntryTime:            {(_executionCorrectionsRequireReview ? "UNKNOWN" : FormatTime(InitialEntryTime))}");
                sb.AppendLine($"InitialStopPrice:            {(InitialStopPrice > 0 ? FormatPrice(InitialStopPrice) : "UNKNOWN")}");
                sb.AppendLine($"InitialRiskStatus:           {(_executionCorrectionsRequireReview ? "UNKNOWN - EXECUTION CORRECTION" : InitialRiskStatus)}");
                sb.AppendLine($"InitialRiskTicks:            {(!_executionCorrectionsRequireReview && InitialRiskStatus == "KNOWN" ? InitialRiskTicks.ToString("F4", CultureInfo.InvariantCulture) : "UNKNOWN")}");
                sb.AppendLine($"InitialRiskUSD:              {(Spec.IsValid && !_executionCorrectionsRequireReview && !_executionIdentityRequiresReview && InitialRiskStatus == "KNOWN" ? Spec.CurrencySymbol + InitialRiskUSD.ToString("F2", CultureInfo.InvariantCulture) : "UNKNOWN")}");
                sb.AppendLine($"PlannedTargetCount:          {InitialTargets.Count}");

                foreach (InitialTarget target in InitialTargets.OrderBy(x => x.FirstSeen))
                    sb.AppendLine($"InitialTarget:               {target.Name} | Qty={target.Quantity} | Price={FormatPrice(target.LimitPrice)} | R={(Spec.IsValid ? target.ExpectedR.ToString("F4", CultureInfo.InvariantCulture) : "UNKNOWN")}");

                sb.AppendLine("-------------------------------------------------");
                sb.AppendLine($"TotalEntryQuantity:          {(_executionCorrectionsRequireReview ? "UNKNOWN" : TotalEntryQuantity.ToString(CultureInfo.InvariantCulture))}");
                sb.AppendLine($"TotalExitQuantity:           {(_executionCorrectionsRequireReview ? "UNKNOWN" : TotalExitQuantity.ToString(CultureInfo.InvariantCulture))}");
                sb.AppendLine($"CurrentOpenQuantity:         {(_executionCorrectionsRequireReview ? "UNKNOWN" : CurrentOpenQuantity.ToString(CultureInfo.InvariantCulture))}");
                sb.AppendLine($"WeightedAverageEntryPrice:   {(_executionCorrectionsRequireReview ? "UNKNOWN" : FormatPrice(WeightedAverageEntryPrice))}");
                sb.AppendLine($"WeightedAverageExitPrice:    {(_executionCorrectionsRequireReview ? "UNKNOWN" : FormatPrice(WeightedAverageExitPrice))}");
                sb.AppendLine($"FinalExitTime:               {(_executionCorrectionsRequireReview ? "UNKNOWN" : FormatTime(LastExitTime))}");
                sb.AppendLine($"TradeDuration:               {duration:hh\\:mm\\:ss\\.fff}");
                sb.AppendLine($"TotalRealizedTicks:          {(!Spec.IsValid || _executionCorrectionsRequireReview || _executionIdentityRequiresReview ? "UNKNOWN" : TotalRealizedTicks.ToString("+0;-0;0", CultureInfo.InvariantCulture))}");
                sb.AppendLine($"GrossRealizedPnL:            {(!Spec.IsValid || _executionCorrectionsRequireReview || _executionIdentityRequiresReview ? "UNKNOWN - CONTRACT/EXECUTION REVIEW" : Spec.CurrencySymbol + TotalRealizedGrossPnL.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture))}");
                sb.AppendLine($"RealizedRStatus:             {(!Spec.IsValid || _executionCorrectionsRequireReview || _executionIdentityRequiresReview ? "UNKNOWN - CONTRACT/EXECUTION REVIEW" : RealizedRStatus)}");
                sb.AppendLine($"RealizedR:                   {(Spec.IsValid && !_executionCorrectionsRequireReview && !_executionIdentityRequiresReview && !BootstrapIncomplete && RealizedRStatus != "UNKNOWN" ? RealizedR.ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture) : "UNKNOWN")}");
                sb.AppendLine($"ExecutionCorrectionStatus:  {(_executionCorrectionsRequireReview ? "REVIEW REQUIRED - UPDATE/REMOVE EVENT" : "NONE")}");
                sb.AppendLine($"CommissionStatus:            {(_executionCorrectionsRequireReview ? "UNKNOWN - EXECUTION CORRECTION" : commissionStatus)}");
                sb.AppendLine($"Commission:                  {(!Spec.IsValid || _executionCorrectionsRequireReview || _executionIdentityRequiresReview || !HasActualCommission ? "UNKNOWN" : Spec.CurrencySymbol + TotalActualCommission.ToString("F2", CultureInfo.InvariantCulture))}");
                sb.AppendLine($"NetRealizedPnL:              {(Spec.IsValid && commissionStatus == "KNOWN" ? Spec.CurrencySymbol + netPnL.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) : "UNKNOWN")}");
                sb.AppendLine("-------------------------------------------------");
                sb.AppendLine($"MaxPriceSinceEntry:          {(_executionCorrectionsRequireReview || HighPriceSinceEntry == double.MinValue ? "UNKNOWN" : FormatPrice(HighPriceSinceEntry))}");
                sb.AppendLine($"MinPriceSinceEntry:          {(_executionCorrectionsRequireReview || LowPriceSinceEntry == double.MaxValue ? "UNKNOWN" : FormatPrice(LowPriceSinceEntry))}");
                sb.AppendLine($"MFE_Ticks:                   {(!Spec.IsValid || _executionCorrectionsRequireReview || _executionIdentityRequiresReview ? "UNKNOWN" : mfeTicks.ToString("F4", CultureInfo.InvariantCulture))}");
                sb.AppendLine($"MFE_USD:                     {(!Spec.IsValid || _executionCorrectionsRequireReview || _executionIdentityRequiresReview ? "UNKNOWN" : Spec.CurrencySymbol + mfeUsd.ToString("F2", CultureInfo.InvariantCulture))}");
                sb.AppendLine($"MAE_Ticks:                   {(!Spec.IsValid || _executionCorrectionsRequireReview || _executionIdentityRequiresReview ? "UNKNOWN" : maeTicks.ToString("F4", CultureInfo.InvariantCulture))}");
                sb.AppendLine($"MAE_USD:                     {(!Spec.IsValid || _executionCorrectionsRequireReview || _executionIdentityRequiresReview ? "UNKNOWN" : Spec.CurrencySymbol + maeUsd.ToString("F2", CultureInfo.InvariantCulture))}");
                sb.AppendLine("-------------------------------------------------");
                sb.AppendLine($"TickSize:                    {(Spec.IsValid ? Spec.TickSize.ToString("G17", CultureInfo.InvariantCulture) : "UNKNOWN")}");
                sb.AppendLine($"TickValue:                   {(Spec.IsValid ? Spec.CurrencySymbol + Spec.TickValue.ToString("F4", CultureInfo.InvariantCulture) : "UNKNOWN")}");
                sb.AppendLine($"ContractSpecSource:          {Spec.Source}");
                sb.AppendLine($"ContractSpecValid:           {Spec.IsValid}");
                sb.AppendLine($"ExecutionIdentityReview:     {_executionIdentityRequiresReview}");
                sb.AppendLine($"UnmatchedExitQuantity:       {UnmatchedExitQuantity}");
                sb.AppendLine($"UnclassifiedFillQuantity:    {UnclassifiedFillQuantity}");
                sb.AppendLine($"AccountingStatus:            {(BootstrapIncomplete ? "INCOMPLETE - POSITION BOOTSTRAP" : CurrentOpenQuantity > 0 ? "INCOMPLETE - POSITION STILL OPEN" : audit != null && audit.Passed && UnclassifiedFillQuantity == 0 ? "COMPLETE" : "INCOMPLETE - REVIEW REQUIRED")}");
                sb.AppendLine($"IntegrityAudit:              {(audit != null && audit.Passed ? "PASS" : "FAIL")}");
                sb.AppendLine("-------------------------------------------------");
                sb.AppendLine("VideoFile:                   Trade_Video.mp4");

                string videoStatus;
                try
                {
                    videoStatus = File.Exists(VideoFilePath)
                        ? "Trade_Video.mp4 (" + ((double)new FileInfo(VideoFilePath).Length / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture) + " MB)"
                        : "Video File Not Found";
                }
                catch
                {
                    videoStatus = "Video Status Unknown";
                }

                sb.AppendLine("VideoStatus:                 " + videoStatus);
                sb.AppendLine($"VideoRecordingStartTime:     {FormatTime(CaptureEntryTime != DateTime.MinValue ? CaptureEntryTime.AddSeconds(-PreTradeSeconds) : VideoRecorder.RecordingStartTime)}");
                sb.AppendLine($"VideoRecordingStopTime:      {FormatTime(CaptureExitTime != DateTime.MinValue ? CaptureExitTime.AddSeconds(PostTradeSeconds) : VideoRecorder.RecordingStopTime)}");
                sb.AppendLine($"VideoTimestampOffsetSeconds: {videoOffset}");
                sb.AppendLine("=================================================");

                return WriteTextAtomic(path, sb.ToString());
            }
            catch (Exception ex)
            {
                WriteDiagnostic("TradeInfo generation", ex);
                WriteLog("[TradeInfo Generation Error] " + ex.Message);
                return false;
            }
        }
        private string CalculateVideoOffsetText()
        {
            if (InitialEntryTime ==
                DateTime.MinValue)
            {
                return "UNKNOWN";
            }

            DateTime wallClock = CaptureEntryTime != DateTime.MinValue
                ? CaptureEntryTime.AddSeconds(-PreTradeSeconds)
                : VideoRecorder.RecordingStartTime;

            if (wallClock == DateTime.MinValue)
            {
                return "UNKNOWN";
            }

            DateTime eventTime =
                InitialEntryTime;

            TimeSpan delta =
                eventTime - wallClock;

            // Playback may deliberately use historical
            // event timestamps while recorder uses machine
            // wall clock.
            //
            // Never expose that mismatch as a numeric video
            // offset.
            if (Math.Abs(
                    delta.TotalHours) > 24.0)
            {
                return "UNKNOWN";
            }

            return delta.TotalSeconds.ToString(
                "F3",
                CultureInfo.InvariantCulture);
        }

        // =========================================================
        // CSV OUTPUT
        // =========================================================

        private bool GenerateTradeExecutionsCsv()
        {
            try
            {
                string path = Path.Combine(FolderPath, "TradeExecutions.csv");
                var sb = new StringBuilder();
                sb.AppendLine("ExecutionId,Time,AccountName,InstrumentFullName,OrderId,OrderName,OrderAction,ExecutionMarketPosition,IsClassified,IsEntry,FillRole,Quantity,Price,Commission,Rate");

                foreach (FillRecord fill in AllFills)
                {
                    sb.AppendLine(
                        EscapeCsv(fill.ExecutionId) + "," +
                        FormatTimeCsv(fill.Time) + "," +
                        EscapeCsv(fill.AccountName) + "," +
                        EscapeCsv(fill.InstrumentFullName) + "," +
                        EscapeCsv(fill.OrderId) + "," +
                        EscapeCsv(fill.OrderName) + "," +
                        (fill.IsClassified ? fill.Action.ToString() : "UNKNOWN") + "," +
                        fill.ExecutionMarketPosition + "," +
                        fill.IsClassified + "," +
                        fill.IsEntry + "," +
                        EscapeCsv(fill.FillRole) + "," +
                        fill.Quantity.ToString(CultureInfo.InvariantCulture) + "," +
                        FormatPrice(fill.Price) + "," +
                        fill.Commission.ToString("F4", CultureInfo.InvariantCulture) + "," +
                        fill.Rate.ToString("F4", CultureInfo.InvariantCulture));
                }

                return WriteTextAtomic(path, sb.ToString());
            }
            catch (Exception ex)
            {
                WriteDiagnostic("TradeExecutions.csv generation", ex);
                return false;
            }
        }
        private bool GenerateTradeOrdersCsv()
        {
            try
            {
                string path = Path.Combine(FolderPath, "TradeOrders.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Sequence,Timestamp,AccountName,InstrumentFullName,OrderId,OrderName,OrderAction,OrderType,OrderState,LimitPrice,StopPrice,Quantity,Filled,AverageFillPrice,Comment");

                foreach (OrderEventRecord order in OrderEvents.OrderBy(x => x.Sequence))
                {
                    sb.AppendLine(
                        order.Sequence.ToString(CultureInfo.InvariantCulture) + "," +
                        FormatTimeCsv(order.Timestamp) + "," +
                        EscapeCsv(order.AccountName) + "," +
                        EscapeCsv(order.InstrumentFullName) + "," +
                        EscapeCsv(order.OrderId) + "," +
                        EscapeCsv(order.OrderName) + "," +
                        order.Action + "," +
                        order.OrderType + "," +
                        order.State + "," +
                        FormatPrice(order.LimitPrice) + "," +
                        FormatPrice(order.StopPrice) + "," +
                        order.Quantity.ToString(CultureInfo.InvariantCulture) + "," +
                        order.Filled.ToString(CultureInfo.InvariantCulture) + "," +
                        FormatPrice(order.AverageFillPrice) + "," +
                        EscapeCsv(order.Comment));
                }

                return WriteTextAtomic(path, sb.ToString());
            }
            catch (Exception ex)
            {
                WriteDiagnostic("TradeOrders.csv generation", ex);
                return false;
            }
        }
        private bool GenerateTradeMarketCsv()
        {
            string path = Path.Combine(FolderPath, "TradeMarket.csv");
            string spoolPath = Path.Combine(FolderPath, "TradeMarket.spool.csv");
            string temporaryPath = path + ".tmp." + Guid.NewGuid().ToString("N");

            try
            {
                using (var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(false)))
                {
                    writer.WriteLine("Timestamp,Last,Bid,Ask,Volume");

                    if (File.Exists(spoolPath))
                    {
                        using (var reader = new StreamReader(spoolPath, Encoding.UTF8, true))
                        {
                            char[] buffer = new char[8192];
                            int read;
                            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                                writer.Write(buffer, 0, read);
                        }
                    }

                    // Batch files are immutable and already ordered by the writer sequence.
                    foreach (string batchFile in Directory.GetFiles(FolderPath, "TradeMarket.batch_*.csv").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        using (var reader = new StreamReader(batchFile, Encoding.UTF8, true))
                        {
                            char[] buffer = new char[8192];
                            int read;
                            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                                writer.Write(buffer, 0, read);
                        }
                    }

                    foreach (MarketSample sample in _marketSamples)
                    {
                        writer.WriteLine(
                            FormatTimeCsv(sample.Time) + "," +
                            FormatMarketPrice(sample.Last) + "," +
                            FormatMarketPrice(sample.Bid) + "," +
                            FormatMarketPrice(sample.Ask) + "," +
                            sample.Volume.ToString(CultureInfo.InvariantCulture));
                    }

                    writer.Flush();
                    FileStream durableStream = writer.BaseStream as FileStream;
                    if (durableStream != null)
                        durableStream.Flush(true);
                    else
                        writer.BaseStream.Flush();
                }

                if (!CommitTemporaryFile(temporaryPath, path))
                    return false;

                // Keep the spool until the whole journal transaction commits.
                // If a later artifact fails, a retry can rebuild the same complete CSV.
                return true;
            }
            catch (Exception ex)
            {
                TryDeleteFile(temporaryPath);
                WriteDiagnostic("TradeMarket.csv generation", ex);
                return false;
            }
        }
        // =========================================================
        // UTILITIES
        // =========================================================

        private static bool PricesEqual(
            double a,
            double b)
        {
            return Math.Abs(a - b) <
                   0.00000001;
        }

        private static bool ContainsIgnoreCase(
            string source,
            string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(
                       value,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string DetermineStopAdjustmentType(
            double newStop)
        {
            if (InitialEntryPrice <= 0 || !Spec.IsValid)
                return "Unknown";

            if (Math.Abs(
                    newStop -
                    InitialEntryPrice) <=
                Spec.TickSize * 1.5)
            {
                return "Breakeven";
            }

            if (Direction ==
                TradeDirection.Long)
            {
                if (newStop >
                    InitialEntryPrice)
                {
                    return "Profit Lock";
                }

                return "Risk Reduction";
            }

            if (newStop <
                InitialEntryPrice)
            {
                return "Profit Lock";
            }

            return "Risk Reduction";
        }

        private static string EscapeCsv(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Contains(",") ||
                value.Contains("\"") ||
                value.Contains("\r") ||
                value.Contains("\n"))
            {
                return "\"" +
                       value.Replace(
                           "\"",
                           "\"\"") +
                       "\"";
            }

            return value;
        }

        private int PriceDecimalPlaces
        {
            get
            {
                if (!Spec.IsValid)
                    return 8;

                string tick =
                    Spec.TickSize.ToString(
                        "0.############################",
                        CultureInfo.InvariantCulture);

                int decimalPoint =
                    tick.IndexOf('.');

                return decimalPoint < 0
                    ? 0
                    : Math.Min(12, tick.Length - decimalPoint - 1);
            }
        }

        private string FormatPrice(
            double value)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value))
                return "UNKNOWN";

            return value.ToString(
                "F" + PriceDecimalPlaces,
                CultureInfo.InvariantCulture);
        }

        private string FormatMarketPrice(
            double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? string.Empty
                : value.ToString(
                    "F" + PriceDecimalPlaces,
                    CultureInfo.InvariantCulture);
        }

        private static string FormatTime(
            DateTime value)
        {
            if (value ==
                DateTime.MinValue)
            {
                return "UNKNOWN";
            }

            return value.ToString(
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture);
        }

        private static string FormatTimeCsv(
            DateTime value)
        {
            return value.ToString(
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture);
        }

        private void DurableWriterLoop()
        {
            try
            {
                while (!_durableWriteQueue.IsCompleted)
                {
                    DurableWriteRequest first;
                    try
                    {
                        first = _durableWriteQueue.Take();
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }

                    if (first.Barrier != null)
                    {
                        first.Barrier.Set();
                        continue;
                    }

                    var batch = new List<DurableWriteRequest>(128);
                    batch.Add(first);
                    ManualResetEvent pendingBarrier = null;
                    DateTime batchDeadline = DateTime.UtcNow.AddMilliseconds(12);

                    while (batch.Count < 128 && DateTime.UtcNow < batchDeadline)
                    {
                        DurableWriteRequest next;
                        if (!_durableWriteQueue.TryTake(out next, 2))
                            continue;
                        if (next.Barrier != null)
                        {
                            pendingBarrier = next.Barrier;
                            break;
                        }
                        batch.Add(next);
                    }

                    WriteDurableBatch(batch);
                    if (pendingBarrier != null)
                        pendingBarrier.Set();
                }
            }
            catch (Exception ex)
            {
                WriteDiagnosticDirect("DurableWriterLoop", ex);
            }
            finally
            {
                _durableWriterStopped.Set();
            }
        }

        private bool WriteDurableBatch(IList<DurableWriteRequest> requests)
        {
            bool allSucceeded = true;
            foreach (IGrouping<string, DurableWriteRequest> group in requests.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string path = group.Key;
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);

                    bool createNew = group.Any(x => x.CreateNew);
                    bool addHeader = group.Any(x => !string.IsNullOrEmpty(x.HeaderIfEmpty)) &&
                        (!File.Exists(path) || new FileInfo(path).Length == 0);
                    string writePath = createNew
                        ? path + ".tmp." + Guid.NewGuid().ToString("N")
                        : path;

                    try
                    {
                        using (var stream = new FileStream(
                            writePath,
                            createNew ? FileMode.CreateNew : FileMode.Append,
                            FileAccess.Write,
                            FileShare.Read,
                            65536,
                            FileOptions.WriteThrough))
                        {
                            if (addHeader)
                            {
                                string header = group.First(x => !string.IsNullOrEmpty(x.HeaderIfEmpty)).HeaderIfEmpty;
                                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                                stream.Write(headerBytes, 0, headerBytes.Length);
                            }

                            foreach (DurableWriteRequest request in group)
                            {
                                byte[] bytes = Encoding.UTF8.GetBytes(request.Content ?? string.Empty);
                                stream.Write(bytes, 0, bytes.Length);
                            }
                            stream.Flush(true);
                        }

                        if (createNew)
                        {
                            if (File.Exists(path))
                                throw new IOException("Immutable batch path already exists: " + path);
                            File.Move(writePath, path);
                        }
                    }
                    finally
                    {
                        if (createNew && File.Exists(writePath))
                        {
                            try { File.Delete(writePath); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    foreach (DurableWriteRequest request in group)
                    {
                        if (request.Critical)
                            Interlocked.Increment(ref _criticalDurableWriteFailures);
                    }
                    allSucceeded = false;
                    WriteDiagnosticDirect("DurableWrite:" + Path.GetFileName(group.Key), ex);
                }
            }
            return allSucceeded;
        }

        private bool QueueDurableWrite(
            string path,
            string content,
            string headerIfEmpty,
            string label,
            bool critical,
            bool createNew = false)
        {
            var request = new DurableWriteRequest
            {
                Path = path,
                Content = content ?? string.Empty,
                HeaderIfEmpty = headerIfEmpty,
                Critical = critical,
                CreateNew = createNew
            };

            try
            {
                if (!_durableWriteQueue.IsAddingCompleted && _durableWriteQueue.TryAdd(request, 100))
                    return true;
            }
            catch
            {
            }

            // A full/stopped queue must not silently discard an Execution/Order record.
            // The bounded synchronous fallback is intentionally rare and is separately logged.
            WriteDiagnosticDirect("Durable queue saturated/stopped; synchronous fallback for " + label,
                new IOException("The durable writer queue could not accept the event within 100 ms."));
            try
            {
                return WriteDurableBatch(new[] { request });
            }
            catch (Exception ex)
            {
                if (critical)
                    Interlocked.Increment(ref _criticalDurableWriteFailures);
                WriteDiagnosticDirect("Durable synchronous fallback failed: " + label, ex);
                return false;
            }
        }

        private bool FlushDurableWrites(TimeSpan timeout)
        {
            if (_durableWriteQueue.IsAddingCompleted)
                return _durableWriterStopped.WaitOne(timeout) && Interlocked.Read(ref _criticalDurableWriteFailures) == 0;

            using (var barrier = new ManualResetEvent(false))
            {
                var request = new DurableWriteRequest { Barrier = barrier };
                int waitMs = (int)Math.Max(1, Math.Min(int.MaxValue, timeout.TotalMilliseconds));
                try
                {
                    if (!_durableWriteQueue.TryAdd(request, waitMs))
                        return false;
                    if (!barrier.WaitOne(timeout))
                        return false;
                    return Interlocked.Read(ref _criticalDurableWriteFailures) == 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        private void StopDurableWriter()
        {
            try
            {
                FlushDurableWrites(TimeSpan.FromSeconds(5));
                if (!_durableWriteQueue.IsAddingCompleted)
                    _durableWriteQueue.CompleteAdding();
                if (!_durableWriterStopped.WaitOne(TimeSpan.FromSeconds(5)))
                    WriteDiagnosticDirect("DurableWriterShutdown", new TimeoutException("Writer did not drain before shutdown deadline."));
            }
            catch (Exception ex)
            {
                WriteDiagnosticDirect("StopDurableWriter", ex);
            }
        }

        private void WriteDiagnosticDirect(string stage, Exception exception)
        {
            string detail =
                "[" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "] " +
                "TradeId=" + TradeId + " | Stage=" + (stage ?? "Unknown") + Environment.NewLine +
                (exception != null ? exception.ToString() : "Unknown exception") + Environment.NewLine +
                "------------------------------------------------------------" + Environment.NewLine;
            try
            {
                lock (_logSync)
                    AppendDurableText(Path.Combine(FolderPath, "RecorderDiagnostics.log"), detail);
            }
            catch
            {
                try
                {
                    NinjaTrader.Code.Output.Process("[BEN Trade Recorder] " + stage + " | " + exception,
                        PrintTo.OutputTab1);
                }
                catch
                {
                }
            }
        }

        private void TryDeleteMarketBatchFiles()
        {
            try
            {
                foreach (string path in Directory.GetFiles(FolderPath, "TradeMarket.batch_*.csv"))
                    TryDeleteFile(path);
            }
            catch (Exception ex)
            {
                WriteDiagnosticDirect("TryDeleteMarketBatchFiles", ex);
            }
        }

        private static void AppendDurableText(string path, string text)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            using (var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private bool CommitTemporaryFile(string temporaryPath, string destinationPath)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    string backupPath = destinationPath + ".previous";
                    try
                    {
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                        File.Replace(temporaryPath, destinationPath, backupPath);
                        TryDeleteFile(backupPath);
                    }
                    catch (Exception replaceException)
                    {
                        // Portable recovery path for filesystems that do not support Replace.
                        // Keep the previous complete file until the new temp file is moved.
                        string recoveryPath = destinationPath + ".recovery." + Guid.NewGuid().ToString("N");
                        bool oldMoved = false;
                        try
                        {
                            File.Move(destinationPath, recoveryPath);
                            oldMoved = true;
                            File.Move(temporaryPath, destinationPath);
                            if (File.Exists(recoveryPath))
                                File.Delete(recoveryPath);
                        }
                        catch
                        {
                            if (!File.Exists(destinationPath) && oldMoved && File.Exists(recoveryPath))
                                File.Move(recoveryPath, destinationPath);
                            throw replaceException;
                        }
                    }
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }

                return File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0;
            }
            catch (Exception ex)
            {
                WriteDiagnostic("CommitTemporaryFile:" + Path.GetFileName(destinationPath), ex);
                return false;
            }
        }

        private bool WriteTextAtomic(string path, string content)
        {
            string temporaryPath = path + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    65536,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(content ?? string.Empty);
                    writer.Flush();
                    stream.Flush(true);
                }

                return CommitTemporaryFile(temporaryPath, path);
            }
            catch (Exception ex)
            {
                TryDeleteFile(temporaryPath);
                WriteDiagnostic("WriteTextAtomic:" + Path.GetFileName(path), ex);
                return false;
            }
        }

        internal void WriteDiagnostic(string stage, Exception exception)
        {
            string detail =
                "[" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "] " +
                "TradeId=" + TradeId +
                " | Stage=" + (stage ?? "Unknown") + Environment.NewLine +
                (exception != null ? exception.ToString() : "Unknown exception") +
                Environment.NewLine + "------------------------------------------------------------" + Environment.NewLine;

            QueueDurableWrite(
                Path.Combine(FolderPath, "RecorderDiagnostics.log"),
                detail,
                null,
                "WriteDiagnostic:" + (stage ?? "Unknown"),
                false);
        }

        private void PersistFillEvent(FillRecord fill)
        {
            try
            {
                string path = Path.Combine(FolderPath, "TradeExecutions.raw.csv");
                var row = new StringBuilder();
                row.AppendLine(
                    EscapeCsv(fill.ExecutionId) + "," +
                    FormatTimeCsv(fill.Time) + "," +
                    EscapeCsv(fill.AccountName) + "," +
                    EscapeCsv(fill.InstrumentFullName) + "," +
                    EscapeCsv(fill.OrderId) + "," +
                    EscapeCsv(fill.OrderName) + "," +
                    (fill.IsClassified ? fill.Action.ToString() : "UNKNOWN") + "," +
                    fill.ExecutionMarketPosition + "," +
                    fill.IsClassified + "," +
                    fill.IsEntry + "," +
                    EscapeCsv(fill.FillRole) + "," +
                    fill.Quantity.ToString(CultureInfo.InvariantCulture) + "," +
                    FormatPrice(fill.Price) + "," +
                    fill.Commission.ToString("F4", CultureInfo.InvariantCulture) + "," +
                    fill.Rate.ToString("F4", CultureInfo.InvariantCulture));

                QueueDurableWrite(path, row.ToString() + Environment.NewLine,
                    "ExecutionId,Time,AccountName,InstrumentFullName,OrderId,OrderName,OrderAction,ExecutionMarketPosition,IsClassified,IsEntry,FillRole,Quantity,Price,Commission,Rate" + Environment.NewLine,
                    "PersistFillEvent", true);
            }
            catch (Exception ex)
            {
                WriteDiagnostic("PersistFillEvent", ex);
            }
        }

        private void PersistExecutionCorrection(Execution execution, Operation operation)
        {
            try
            {
                string path = Path.Combine(FolderPath, "TradeExecutionCorrections.raw.csv");
                var row = new StringBuilder();
                row.AppendLine(
                    operation + "," +
                    EscapeCsv(execution.ExecutionId) + "," +
                    FormatTimeCsv(execution.Time) + "," +
                    EscapeCsv(execution.Account != null ? execution.Account.Name : AccountName) + "," +
                    EscapeCsv(execution.Instrument != null ? execution.Instrument.FullName : InstrumentFullName) + "," +
                    EscapeCsv(execution.Order != null ? execution.Order.OrderId : string.Empty) + "," +
                    (execution.Order != null ? execution.Order.OrderAction.ToString() : "UNKNOWN") + "," +
                    execution.MarketPosition + "," +
                    execution.Quantity.ToString(CultureInfo.InvariantCulture) + "," +
                    FormatPrice(execution.Price) + "," +
                    execution.Commission.ToString("F4", CultureInfo.InvariantCulture) + "," +
                    execution.Rate.ToString("F4", CultureInfo.InvariantCulture));

                QueueDurableWrite(path, row.ToString() + Environment.NewLine,
                    "Operation,ExecutionId,Time,AccountName,InstrumentFullName,OrderId,OrderAction,MarketPosition,Quantity,Price,Commission,Rate" + Environment.NewLine,
                    "PersistExecutionCorrection", true);
            }
            catch (Exception ex)
            {
                WriteDiagnostic("PersistExecutionCorrection", ex);
            }
        }

        private void PersistOrderEvent(OrderEventRecord order)
        {
            try
            {
                string path = Path.Combine(FolderPath, "TradeOrders.raw.csv");
                var row = new StringBuilder();
                row.AppendLine(
                    order.Sequence.ToString(CultureInfo.InvariantCulture) + "," +
                    FormatTimeCsv(order.Timestamp) + "," +
                    EscapeCsv(order.AccountName) + "," +
                    EscapeCsv(order.InstrumentFullName) + "," +
                    EscapeCsv(order.OrderId) + "," +
                    EscapeCsv(order.OrderName) + "," +
                    order.Action + "," +
                    order.OrderType + "," +
                    order.State + "," +
                    FormatPrice(order.LimitPrice) + "," +
                    FormatPrice(order.StopPrice) + "," +
                    order.Quantity.ToString(CultureInfo.InvariantCulture) + "," +
                    order.Filled.ToString(CultureInfo.InvariantCulture) + "," +
                    FormatPrice(order.AverageFillPrice) + "," +
                    EscapeCsv(order.Comment));

                QueueDurableWrite(path, row.ToString() + Environment.NewLine,
                    "Sequence,Timestamp,AccountName,InstrumentFullName,OrderId,OrderName,OrderAction,OrderType,OrderState,LimitPrice,StopPrice,Quantity,Filled,AverageFillPrice,Comment" + Environment.NewLine,
                    "PersistOrderEvent", true);
            }
            catch (Exception ex)
            {
                WriteDiagnostic("PersistOrderEvent", ex);
            }
        }

        public void WriteLog(string message)
        {
            string line =
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] " +
                (message ?? string.Empty) + Environment.NewLine;

            QueueDurableWrite(
                Path.Combine(FolderPath, "TradeActivity.log"),
                line,
                null,
                "TradeActivity.log",
                false);
        }
        public void Dispose()
        {
            if (_disposed && (_journalCommitted || _checkpointCommitted))
                return;

            _disposed = true;
            try
            {
                UnsubscribeMarketData();
            }
            catch (Exception ex)
            {
                WriteDiagnostic("Dispose.UnsubscribeMarketData", ex);
            }

            // Never infer completion from SessionState. Reconstruct flatness from
            // the execution ledger; otherwise save an explicitly incomplete checkpoint.
            if (!_journalCommitted && !_checkpointCommitted)
            {
                try
                {
                    bool ledgerProvesFlat =
                        TotalEntryQuantity > 0 &&
                        CurrentOpenQuantity == 0 &&
                        UnmatchedExitQuantity == 0 &&
                        UnclassifiedFillQuantity == 0;

                    bool committed = FinalizeAndGenerateJournal(
                        ledgerProvesFlat
                            ? "NinjaTrader shutdown - all recorded fills flat"
                            : "NinjaTrader shutdown - INCOMPLETE CHECKPOINT",
                        ledgerProvesFlat);

                    if (!committed)
                    {
                        WritePendingFinalization(
                            "NinjaTrader shutdown checkpoint",
                            "Checkpoint/Journal could not be fully committed. Raw event ledgers and diagnostics were retained.");
                    }
                }
                catch (Exception ex)
                {
                    WriteDiagnostic("Dispose.FinalizationCheckpoint", ex);
                }
            }

            try
            {
                VideoRecorder?.Dispose();
            }
            catch (Exception ex)
            {
                WriteDiagnostic("Dispose.VideoRecorder", ex);
            }

            StopDurableWriter();
        }

    }

    #endregion

    #region Lifecycle Manager

    public class TradeLifecycleManager
    {
        private readonly object _lifecycleSync = new object();
private readonly ConcurrentDictionary<string, TradeSession>
            _active =
                new ConcurrentDictionary<string, TradeSession>();

        private readonly ConcurrentDictionary<string, TradeSession>
            _closing =
                new ConcurrentDictionary<string, TradeSession>();

        private readonly BENTradeRecorderSettings _settings;

        public TradeLifecycleManager(
            BENTradeRecorderSettings settings)
        {
            _settings =
                settings ??
                BENTradeRecorderSettings.Load();
        }

        public string BuildKey(
            string account,
            string instrument)
        {
            return
                $"{account}|{instrument}"
                .ToUpperInvariant();
        }

        public TradeSession GetActiveSession(
            string account,
            string instrument)
        {
            TradeSession session;

            return _active.TryGetValue(
                BuildKey(account, instrument),
                out session)
                ? session
                : null;
        }

        public TradeSession CreateActiveSession(
            string account,
            Instrument instrument,
            string instrumentName,
            TradeDirection direction)
        {
            string key = BuildKey(account, instrumentName);
            lock (_lifecycleSync)
            {
                TradeSession existing;
                if (_active.TryGetValue(key, out existing))
                    return existing;

                var session = new TradeSession(
                    account,
                    instrument,
                    instrumentName,
                    direction,
                    _settings.SaveFolder,
                    _settings.PreTradeSeconds,
                    _settings.PostTradeSeconds);

                _active[key] = session;
                return session;
            }
        }
        public TradeSession GetClosingSession(string account, string instrument, string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                return null;

            string key = BuildKey(account, instrument);
            lock (_lifecycleSync)
            {
                return _closing.Values.FirstOrDefault(session =>
                    string.Equals(
                        BuildKey(session.AccountName, session.InstrumentFullName),
                        key,
                        StringComparison.OrdinalIgnoreCase) &&
                    session.HasRecordedOrderId(orderId));
            }
        }

        public TradeSession MoveActiveToClosing(string account, string instrument)
        {
            string key = BuildKey(account, instrument);
            lock (_lifecycleSync)
            {
                TradeSession session;
                if (_active.TryRemove(key, out session))
                {
                    session.State = SessionState.Closing;
                    _closing[session.TradeId] = session;
                    return session;
                }

                return null;
            }
        }
        public void RemoveClosingSession(
            string tradeId)
        {
            TradeSession ignored;

            _closing.TryRemove(
                tradeId,
                out ignored);
        }

        public void ClearAll()
        {
            foreach (TradeSession session
                     in _active.Values)
            {
                try
                {
                    session.Dispose();
                }
                catch
                {
                }
            }

            foreach (TradeSession session
                     in _closing.Values)
            {
                try
                {
                    session.Dispose();
                }
                catch
                {
                }
            }

            _active.Clear();
            _closing.Clear();
        }
    }

    #endregion

    #region Trade Monitor

    public class TradeMonitor : IDisposable
    {
        private readonly BENTradeRecorderSettings _settings;
        private readonly TradeLifecycleManager _lifecycle;

        private readonly ConcurrentDictionary<string, int>
            _lastKnownPosition =
                new ConcurrentDictionary<string, int>();
        private readonly object _positionStateSync = new object();

        private readonly ConcurrentDictionary<string, Timer>
            _timers =
                new ConcurrentDictionary<string, Timer>();

        // Orders received before session creation.
        private readonly ConcurrentDictionary<string, List<PendingOrderEvent>>
            _pendingOrders =
                new ConcurrentDictionary<string, List<PendingOrderEvent>>();

        private readonly object _accountSubscriptionsSync = new object();
        private readonly HashSet<Account> _subscribedAccounts = new HashSet<Account>();
        private Timer _accountRefreshTimer;

        private long _pendingSequence;
        private readonly object _globalDiagnosticSync = new object();

        private volatile bool _disposed;

        private readonly
            BENRollingPreRollRecorder
            _preRollRecorder;

        public TradeMonitor(
            BENTradeRecorderSettings settings,
            TradeLifecycleManager lifecycle)
        {
            _settings =
                settings ??
                BENTradeRecorderSettings.Load();

            _lifecycle =
                lifecycle;

            _preRollRecorder =
                new BENRollingPreRollRecorder(
                    _settings,
                    message =>
                        NinjaTrader.Code.Output.Process(
                            message,
                            PrintTo.OutputTab1));

            // The AddOn starts PRE-ROLL automatically when enabled.

            Subscribe();
        }


        // =========================================================
        // MANUAL PRE-ROLL CONTROL
        // =========================================================

        public bool StartPreRoll()
        {
            try
            {
                bool ok =
                    _preRollRecorder.Start();

                NinjaTrader.Code.Output.Process(
                    ok
                        ? "[BEN PRE-ROLL] START OK"
                        : "[BEN PRE-ROLL] START FAILED",
                    PrintTo.OutputTab1);

                return ok;
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process(
                    "[BEN PRE-ROLL] START EXCEPTION: " +
                    ex,
                    PrintTo.OutputTab1);

                return false;
            }
        }

        public void StopPreRoll()
        {
            try
            {
                _preRollRecorder.Stop();

                NinjaTrader.Code.Output.Process(
                    "[BEN PRE-ROLL] STOP OK",
                    PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process(
                    "[BEN PRE-ROLL] STOP EXCEPTION: " +
                    ex,
                    PrintTo.OutputTab1);
            }
        }

        public bool IsPreRollRunning
        {
            get
            {
                return _preRollRecorder.IsRunning;
            }
        }

        public DateTime PreRollStartTime
        {
            get
            {
                return _preRollRecorder.RecordingStartTime;
            }
        }

        public int PreRollBufferedSeconds
        {
            get { return _preRollRecorder.GetBufferedSeconds(); }
        }

        public string PreRollFailureReason
        {
            get { return _preRollRecorder.FailureReason; }
        }
        private void Subscribe()
        {
            try
            {
                lock (Account.All)
                {
                    foreach (Account account
                             in Account.All)
                    {
                        if (account == null)
                            continue;

                        SubscribeAccount(account);
                    }
                }

                Account.AccountStatusUpdate -=
                    OnAccountStatusUpdate;

                Account.AccountStatusUpdate += OnAccountStatusUpdate;

                if (_accountRefreshTimer == null)
                    _accountRefreshTimer = new Timer(
                        _ => RefreshAccountSubscriptions(),
                        null,
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                WriteGlobalDiagnostic("Account subscription startup failed: " + ex);
            }
        }

        private void RefreshAccountSubscriptions()
        {
            if (_disposed)
                return;

            try
            {
                lock (Account.All)
                {
                    foreach (Account account in Account.All)
                    {
                        if (account != null)
                            SubscribeAccount(account);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteGlobalDiagnostic("Account rescan failed: " + ex);
            }
        }

        private void OnAccountStatusUpdate(
            object sender,
            AccountStatusEventArgs e)
        {
            try
            {
                if (_disposed ||
                    e == null ||
                    e.Account == null)
                {
                    return;
                }

                SubscribeAccount(e.Account);
            }
            catch
            {
            }
        }

        private void SubscribeAccount(Account account)
        {
            if (account == null || _disposed)
                return;

            lock (_accountSubscriptionsSync)
            {
                if (_disposed || !_subscribedAccounts.Add(account))
                    return;
            }

            bool executionAttached = false;
            bool orderAttached = false;
            bool positionAttached = false;
            try
            {
                account.ExecutionUpdate -= OnExecutionUpdate;
                account.ExecutionUpdate += OnExecutionUpdate;
                executionAttached = true;
                account.OrderUpdate -= OnOrderUpdate;
                account.OrderUpdate += OnOrderUpdate;
                orderAttached = true;
                account.PositionUpdate -= OnPositionUpdate;
                account.PositionUpdate += OnPositionUpdate;
                positionAttached = true;
            }
            catch
            {
                // Roll back every handler that was already attached. A failed
                // partial subscription must not leak callbacks after disposal.
                try { if (positionAttached) account.PositionUpdate -= OnPositionUpdate; } catch { }
                try { if (orderAttached) account.OrderUpdate -= OnOrderUpdate; } catch { }
                try { if (executionAttached) account.ExecutionUpdate -= OnExecutionUpdate; } catch { }
                lock (_accountSubscriptionsSync)
                    _subscribedAccounts.Remove(account);
                throw;
            }

            // Dispose may race an account-status notification that was already
            // in flight. If this subscription completed after shutdown began,
            // remove it asynchronously so the platform's account locks cannot
            // hold NinjaTrader's exit path open.
            if (_disposed)
            {
                UnsubscribeAccountEventsAsync(new List<Account> { account }, false);
                return;
            }

            BootstrapAccountPositions(account);
        }

        private void BootstrapAccountPositions(Account account)
        {
            if (account == null || _disposed || !_settings.EnableRecorder)
                return;

            try
            {
                Position[] snapshot;
                // NinjaTrader's account position collection is mutable. Take the
                // snapshot under its collection lock, then release it before creating
                // sessions or writing diagnostics.
                lock (account.Positions)
                    snapshot = account.Positions.Cast<Position>().Where(x => x != null).ToArray();

                foreach (Position position in snapshot)
                {
                    if (position.Instrument == null || position.Quantity <= 0 ||
                        position.MarketPosition == MarketPosition.Flat)
                        continue;

                    string instrumentName = position.Instrument.FullName;
                    string key = _lifecycle.BuildKey(account.Name, instrumentName);
                    int signedQuantity = position.MarketPosition == MarketPosition.Long
                        ? Math.Abs(position.Quantity)
                        : -Math.Abs(position.Quantity);

                    lock (_positionStateSync)
                    {
                        int knownQuantity;
                        if (_lastKnownPosition.TryGetValue(key, out knownQuantity))
                            continue;

                        TradeSession active = _lifecycle.GetActiveSession(account.Name, instrumentName);
                        if (active == null)
                        {
                            TradeDirection direction = signedQuantity > 0
                                ? TradeDirection.Long
                                : TradeDirection.Short;
                            active = _lifecycle.CreateActiveSession(
                                account.Name, position.Instrument, instrumentName, direction);
                            active.MarkBootstrapIncomplete(
                                signedQuantity, position.AveragePrice, DateTime.Now);
                            WriteGlobalDiagnostic("Startup position bootstrap: Account=" + account.Name +
                                " Instrument=" + instrumentName + " Quantity=" + signedQuantity +
                                " AveragePrice=" + position.AveragePrice.ToString(CultureInfo.InvariantCulture) +
                                ". Historical entry executions were not fabricated; journal is flagged incomplete.");
                        }

                        _lastKnownPosition[key] = signedQuantity;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteGlobalDiagnostic("Account position bootstrap failed for " + account.Name + ": " + ex);
            }
        }

        private void OnOrderUpdate(
            object sender,
            OrderEventArgs e)
        {
            if (_disposed ||
                !_settings.EnableRecorder ||
                e == null ||
                e.Order == null)
            {
                return;
            }

            try
            {
                Account account =
                    sender as Account ??
                    e.Order.Account;

                if (account == null)
                    return;

                string accountName =
                    account.Name;

                string instrumentName =
                    e.Order.Instrument != null
                        ? e.Order.Instrument.FullName
                        : string.Empty;

                TradeSession session =
                    _lifecycle.GetActiveSession(
                        accountName,
                        instrumentName);

                if (session != null)
                {
                    session.RegisterOrderUpdate(
                        e.Order,
                        e);

                    return;
                }

                BufferPendingOrder(
                    accountName,
                    instrumentName,
                    e);
            }
            catch (Exception ex)
            {
                WriteGlobalDiagnostic("OrderUpdate callback failed: " + ex);
            }
        }

        private void BufferPendingOrder(
            string accountName,
            string instrumentName,
            OrderEventArgs e)
        {
            string key =
                BuildPendingKey(
                    accountName,
                    instrumentName);

            var list =
                _pendingOrders.GetOrAdd(
                    key,
                    _ => new List<PendingOrderEvent>());

            lock (list)
            {
                DateTime now = DateTime.Now;
                list.RemoveAll(x =>
                    x.EventArgs == null ||
                    x.EventArgs.Time == DateTime.MinValue ||
                    x.EventArgs.Time < now.AddMinutes(-5));

                list.Add(
                    new PendingOrderEvent
                    {
                        Sequence =
                            Interlocked.Increment(
                                ref _pendingSequence),
                        EventArgs =
                            e
                    });

                if (list.Count > 500)
                    list.RemoveRange(0, list.Count - 500);
            }
        }

        private void FlushPendingOrders(
            TradeSession session,
            string executionOrderId,
            DateTime executionTime)
        {
            if (session == null || string.IsNullOrWhiteSpace(executionOrderId))
                return;

            string key =
                BuildPendingKey(
                    session.AccountName,
                    session.InstrumentFullName);

            List<PendingOrderEvent> list;

            if (!_pendingOrders.TryGetValue(key, out list))
            {
                return;
            }

            List<PendingOrderEvent> matched;

            lock (list)
            {
                DateTime earliest = executionTime.AddMinutes(-5);
                DateTime latest = executionTime.AddSeconds(1);
                matched = list
                    .Where(x => x.EventArgs != null &&
                                x.EventArgs.Order != null &&
                                string.Equals(x.EventArgs.Order.OrderId,
                                    executionOrderId, StringComparison.OrdinalIgnoreCase) &&
                                x.EventArgs.Time >= earliest &&
                                x.EventArgs.Time <= latest)
                    .OrderBy(x => x.EventArgs.Time)
                    .ThenBy(x => x.Sequence)
                    .ToList();

                foreach (PendingOrderEvent item in matched)
                    list.Remove(item);

                list.RemoveAll(x => x.EventArgs == null ||
                    x.EventArgs.Time < earliest);
            }

            foreach (PendingOrderEvent pending in matched)
            {
                if (pending.EventArgs != null &&
                    pending.EventArgs.Order != null)
                {
                    session.RegisterOrderUpdate(
                        pending.EventArgs.Order,
                        pending.EventArgs);
                }
            }

            // Keep the per-account/instrument list allocated. Removing it
            // here could race an OrderUpdate that already holds this list.
        }

        private string BuildPendingKey(
            string account,
            string instrument)
        {
            return
                $"{account}|{instrument}"
                .ToUpperInvariant();
        }

        private void WriteGlobalDiagnostic(string message)
        {
            string configuredRoot = !string.IsNullOrWhiteSpace(_settings.SaveFolder)
                ? _settings.SaveFolder
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BEN_TradeRecorder");

            string[] roots = new[]
            {
                configuredRoot,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BENTradeRecorder")
            };

            Exception lastError = null;
            foreach (string root in roots)
            {
                try
                {
                    string directory = Path.Combine(root, "Diagnostics");
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, "BENTradeRecorder.log");
                    byte[] bytes = Encoding.UTF8.GetBytes(
                        "[" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "] " +
                        (message ?? string.Empty) + Environment.NewLine);
                    lock (_globalDiagnosticSync)
                    {
                        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
                            FileShare.Read, 4096, FileOptions.WriteThrough))
                        {
                            stream.Write(bytes, 0, bytes.Length);
                            stream.Flush(true);
                        }
                    }
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            try
            {
                NinjaTrader.Code.Output.Process(
                    "[BEN Trade Recorder] Global diagnostic write failed: " + lastError,
                    PrintTo.OutputTab1);
            }
            catch
            {
            }
        }

        private void OnExecutionUpdate(
            object sender,
            ExecutionEventArgs e)
        {
            if (_disposed ||
                !_settings.EnableRecorder ||
                e == null ||
                e.Execution == null)
            {
                return;
            }

            try
            {
                Execution execution =
                    e.Execution;

                Account account =
                    sender as Account ??
                    execution.Account;

                if (account == null)
                    return;

                string accountName =
                    account.Name;

                string instrumentName =
                    execution.Instrument != null
                        ? execution.Instrument.FullName
                        : string.Empty;

                TradeSession session =
                    _lifecycle.GetActiveSession(
                        accountName,
                        instrumentName);

                string executionOrderId = execution.Order != null ? execution.Order.OrderId : string.Empty;

                // Prefer the exact closing trade that already owns this order ID.
                // This covers late Add/Update/Remove notifications after PositionUpdate
                // has moved the prior trade out of the active dictionary.
                session = _lifecycle.GetClosingSession(accountName, instrumentName, executionOrderId);
                if (session == null)
                    session = _lifecycle.GetActiveSession(accountName, instrumentName);

                if (session == null && e.Operation != Operation.Add)
                {
                    WriteGlobalDiagnostic(
                        "Execution correction arrived with no matching active/closing session. Operation=" +
                        e.Operation + " | ExecutionId=" + (execution.ExecutionId ?? string.Empty) +
                        " | OrderId=" + executionOrderId + " | Instrument=" + instrumentName +
                        " | The event was not turned into a new trade.");
                    return;
                }

                if (session == null)
                {
                    TradeDirection direction = ResolveDirection(execution);
                    session = _lifecycle.CreateActiveSession(
                        accountName, execution.Instrument, instrumentName, direction);
                }

                // OrderUpdate may arrive before the first execution. Only
                // events for this exact order and a short time window belong
                // to this trade.
                if (execution.Order != null)
                    FlushPendingOrders(session, execution.Order.OrderId, execution.Time);

                // A single exchange execution can close the old position and open
                // the opposite side. Split it at zero so that the old session gets
                // only its closing quantity and the residual becomes a new entry.
                TradeSession activeSession = _lifecycle.GetActiveSession(accountName, instrumentName);
                bool isActiveSession = session.State != SessionState.Completed &&
                    (activeSession == null || ReferenceEquals(activeSession, session) || session.State == SessionState.Closing);
                int openBeforeFill = session.CurrentOpenQuantity;
                int executionQuantity = Math.Max(0, execution.Quantity);
                bool postFillIsOpposite =
                    (session.Direction == TradeDirection.Long && execution.MarketPosition == MarketPosition.Short) ||
                    (session.Direction == TradeDirection.Short && execution.MarketPosition == MarketPosition.Long);
                OrderAction executionAction = execution.Order != null
                    ? execution.Order.OrderAction
                    : default(OrderAction);
                bool actionCanReverse =
                    (session.Direction == TradeDirection.Long &&
                        (executionAction == OrderAction.Sell || executionAction == OrderAction.SellShort)) ||
                    (session.Direction == TradeDirection.Short &&
                        (executionAction == OrderAction.Buy || executionAction == OrderAction.BuyToCover));

                if (e.Operation == Operation.Add && isActiveSession && openBeforeFill > 0 &&
                    executionQuantity > openBeforeFill && postFillIsOpposite && actionCanReverse)
                {
                    int closingQuantity = openBeforeFill;
                    int residualQuantity = executionQuantity - closingQuantity;
                    double totalCommission = Math.Max(0.0, execution.Commission);
                    double closingCommission = executionQuantity > 0
                        ? totalCommission * closingQuantity / executionQuantity
                        : 0.0;
                    double openingCommission = totalCommission - closingCommission;

                    bool closeRegistered = session.RegisterFill(
                        execution, e.Operation, closingQuantity, false, closingCommission);

                    if (!closeRegistered)
                        return;

                    TradeSession closingSession = session.State == SessionState.Closing
                        ? session
                        : _lifecycle.MoveActiveToClosing(accountName, instrumentName);
                    if (closingSession != null)
                        ScheduleFinalize(closingSession, "Reversal Close Leg");

                    TradeDirection newDirection = execution.MarketPosition == MarketPosition.Short
                        ? TradeDirection.Short
                        : TradeDirection.Long;
                    TradeSession newSession = _lifecycle.CreateActiveSession(
                        accountName, execution.Instrument, instrumentName, newDirection);
                    bool openRegistered = newSession.RegisterFill(
                        execution, e.Operation, residualQuantity, true, openingCommission);

                    int signedNewQuantity = newDirection == TradeDirection.Short
                        ? -residualQuantity
                        : residualQuantity;
                    lock (_positionStateSync)
                        _lastKnownPosition[_lifecycle.BuildKey(accountName, instrumentName)] = signedNewQuantity;

                    newSession.WriteLog("[REVERSAL SPLIT] ExecutionId=" + (execution.ExecutionId ?? string.Empty) +
                        " | OldSessionCloseQty=" + closingQuantity +
                        " | NewSessionEntryQty=" + residualQuantity +
                        " | NewDirection=" + newDirection +
                        " | NewEntryRegistered=" + openRegistered);
                    if (newSession.CaptureEntryTime != DateTime.MinValue)
                        _preRollRecorder.ProtectFrom(newSession.TradeId, newSession.CaptureEntryTime);
                    return;
                }

                bool fillRegistered = session.RegisterFill(execution, e.Operation);

                // Use the execution ledger as a second flatness signal. This avoids
                // depending exclusively on PositionUpdate delivery order/provider behavior.
                if (fillRegistered && e.Operation == Operation.Add &&
                    session.TotalEntryQuantity > 0 && session.CurrentOpenQuantity == 0)
                {
                    TradeSession closing = session.State == SessionState.Closing
                        ? session
                        : _lifecycle.MoveActiveToClosing(accountName, instrumentName);

                    if (closing != null)
                        ScheduleFinalize(closing, "Execution Ledger Flat");
                }

                if (session.CaptureEntryTime != DateTime.MinValue)
                    _preRollRecorder.ProtectFrom(session.TradeId, session.CaptureEntryTime);
            }
            catch (Exception ex)
            {
                WriteGlobalDiagnostic("ExecutionUpdate callback failed: " + ex);
            }
        }

        private TradeDirection ResolveDirection(
            Execution execution)
        {
            if (execution.Order != null)
            {
                if (execution.Order.OrderAction ==
                    OrderAction.SellShort)
                {
                    return TradeDirection.Short;
                }

                if (execution.Order.OrderAction ==
                    OrderAction.Buy)
                {
                    return TradeDirection.Long;
                }
            }

            if (execution.MarketPosition ==
                MarketPosition.Short)
            {
                return TradeDirection.Short;
            }

            return TradeDirection.Long;
        }

        private void OnPositionUpdate(object sender, PositionEventArgs e)
        {
            if (_disposed || !_settings.EnableRecorder || e == null)
                return;

            try
            {
                Position position = e.Position;
                Account account = sender as Account ?? (position != null ? position.Account : null);
                if (account == null || position == null || position.Instrument == null)
                    return;

                string accountName = account.Name;
                string instrumentName = position.Instrument.FullName;
                string key = _lifecycle.BuildKey(accountName, instrumentName);

                lock (_positionStateSync)
                {
                int currentQuantity;
                if (e.Operation == Operation.Remove || e.MarketPosition == MarketPosition.Flat)
                    currentQuantity = 0;
                else if (e.MarketPosition == MarketPosition.Long)
                    currentQuantity = Math.Abs(e.Quantity);
                else if (e.MarketPosition == MarketPosition.Short)
                    currentQuantity = -Math.Abs(e.Quantity);
                else
                    currentQuantity = 0;

                int previousQuantity = _lastKnownPosition.GetOrAdd(key, 0);
                TradeSession activeSession = _lifecycle.GetActiveSession(accountName, instrumentName);

                if (previousQuantity == 0 && currentQuantity != 0 && activeSession == null)
                {
                    // The AddOn cannot reconstruct authoritative entry executions from
                    // a PositionUpdate snapshot. Do not synthesize a fill or PnL. Record
                    // the observed starting exposure and make the missing history explicit.
                    TradeDirection bootstrapDirection = currentQuantity < 0
                        ? TradeDirection.Short
                        : TradeDirection.Long;
                    TradeSession bootstrapSession = _lifecycle.CreateActiveSession(
                        accountName, position.Instrument, instrumentName, bootstrapDirection);
                    bootstrapSession.MarkBootstrapIncomplete(currentQuantity, e.AveragePrice, DateTime.Now);
                    activeSession = bootstrapSession;
                    WriteGlobalDiagnostic("Position bootstrap started with an already-open position. Account=" +
                        accountName + " Instrument=" + instrumentName + " Quantity=" + currentQuantity +
                        " AveragePrice=" + e.AveragePrice.ToString(CultureInfo.InvariantCulture) +
                        ". Entry execution history is unavailable; journal will remain marked incomplete until reconciled.");
                }

                // Account PositionUpdate notifications can be missed during reconnect/startup.
                // The fill ledger is an independent flatness signal after all fills are processed.
                bool executionLedgerFlat = activeSession != null &&
                    activeSession.TotalEntryQuantity > 0 &&
                    activeSession.CurrentOpenQuantity == 0;

                if (currentQuantity == 0 && (previousQuantity != 0 || executionLedgerFlat))
                {
                    TradeSession session = _lifecycle.MoveActiveToClosing(accountName, instrumentName);
                    if (session != null)
                        ScheduleFinalize(session, e.Operation == Operation.Remove ? "Position Remove" : "Position Flat");
                }
                else if (previousQuantity != 0 && currentQuantity != 0 &&
                         Math.Sign(previousQuantity) != Math.Sign(currentQuantity))
                {
                    TradeSession oldSession = _lifecycle.MoveActiveToClosing(accountName, instrumentName);
                    if (oldSession != null)
                        ScheduleFinalize(oldSession, "Position Reversal");

                    TradeDirection newDirection = currentQuantity > 0
                        ? TradeDirection.Long
                        : TradeDirection.Short;

                    _lifecycle.CreateActiveSession(accountName, position.Instrument, instrumentName, newDirection);
                }

                _lastKnownPosition[key] = currentQuantity;
                }
            }
            catch (Exception ex)
            {
                WriteGlobalDiagnostic("PositionUpdate callback failed: " + ex);
            }
        }
        private void ScheduleFinalize(TradeSession session, string reason, int attempt = 0)
        {
            if (session == null || _disposed)
                return;

            int delay = attempt == 0
                ? Math.Max(1000, _settings.PostTradeSeconds * 1000)
                : Math.Min(60000, 1000 * (1 << Math.Min(attempt, 6)));

            Timer timer = null;
            timer = new Timer(
                state =>
                {
                    bool committed = false;
                    bool retry = false;
                    try
                    {
                        // Do not rebuild a valid video on journal retries.
                        bool videoExists = false;
                        try
                        {
                            double expectedVideoDuration =
                                Math.Max(1.0, (session.CaptureExitTime - session.CaptureEntryTime).TotalSeconds +
                                    session.PreTradeSeconds + session.PostTradeSeconds);
                            videoExists = File.Exists(session.VideoFilePath) &&
                                _preRollRecorder.ValidateExistingVideo(
                                    session.VideoFilePath, expectedVideoDuration, session.WriteLog);
                        }
                        catch (Exception fileException)
                        {
                            session.WriteDiagnostic("ScheduleFinalize.VideoFileCheck", fileException);
                        }

                        if (!videoExists && File.Exists(session.VideoFilePath))
                        {
                            try
                            {
                                string unverifiedPath = session.VideoFilePath + ".unverified." +
                                    DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture) + ".mp4";
                                File.Move(session.VideoFilePath, unverifiedPath);
                                session.WriteLog("[VIDEO] Existing MP4 did not pass ffprobe validation and was preserved as " +
                                    Path.GetFileName(unverifiedPath));
                            }
                            catch (Exception quarantineException)
                            {
                                session.WriteDiagnostic("ScheduleFinalize.QuarantineUnverifiedVideo", quarantineException);
                            }
                        }

                        if (!videoExists && session.CaptureEntryTime != DateTime.MinValue &&
                            session.CaptureExitTime != DateTime.MinValue)
                        {
                            try
                            {
                                _preRollRecorder.BuildTradeClip(
                                    session.CaptureEntryTime,
                                    session.CaptureExitTime,
                                    session.VideoFilePath,
                                    session.WriteLog);
                            }
                            catch (Exception videoException)
                            {
                                session.WriteDiagnostic("ScheduleFinalize.BuildTradeClip", videoException);
                                session.WriteLog("[VIDEO] Clip build failed; financial journal will continue independently.");
                            }
                        }

                        committed = session.FinalizeAndGenerateJournal(reason);
                        if (committed)
                        {
                            _lifecycle.RemoveClosingSession(session.TradeId);
                            session.WriteLog("[LIFECYCLE] Closing session removed after journal commit.");
                            session.Dispose();
                        }
                        else if (session.ExecutionCorrectionsRequireReview)
                        {
                            retry = false;
                            session.WriteLog("[FINALIZATION PAUSED] ExecutionUpdate amendment/removal requires authoritative reconciliation; the pending journal and raw correction ledger were retained.");
                        }
                        else if (!_disposed && attempt < 7)
                        {
                            retry = true;
                            session.WriteLog("[FINALIZATION RETRY SCHEDULED] NextAttempt=" + (attempt + 2) + " | MaxAttempts=8");
                        }
                        else if (!_disposed)
                        {
                            retry = false;
                            session.WriteLog("[FINALIZATION HALTED] Automatic retry limit reached. TradeFinalization.pending and raw ledgers were retained; operator recovery is required.");
                        }
                        else
                        {
                            session.WriteLog("[FINALIZATION RETRY PAUSED] Shutdown is in progress; lifecycle disposal will attempt a checkpoint.");
                        }
                    }
                    catch (Exception ex)
                    {
                        session.WriteDiagnostic("ScheduleFinalize", ex);
                        if (!_disposed && attempt < 7)
                        {
                            retry = true;
                            session.WriteLog("[FINALIZATION RETRY SCHEDULED AFTER EXCEPTION] NextAttempt=" +
                                (attempt + 2) + " | MaxAttempts=8");
                        }
                        else if (!_disposed)
                        {
                            retry = false;
                            session.WriteLog("[FINALIZATION HALTED AFTER EXCEPTION] Automatic retry limit reached. " +
                                "Pending marker and raw ledgers were retained; operator recovery is required.");
                        }
                    }
                    finally
                    {
                        Timer current;
                        if (_timers.TryGetValue(session.TradeId, out current) && ReferenceEquals(current, timer))
                        {
                            Timer ignored;
                            if (_timers.TryRemove(session.TradeId, out ignored))
                            {
                                try { ignored.Dispose(); } catch { }
                            }
                        }
                    }

                    if (retry)
                    {
                        ScheduleFinalize(session, reason, attempt + 1);
                    }
                    else
                    {
                        try { _preRollRecorder.ReleaseProtection(session.TradeId); } catch { }
                    }
                },
                null,
                delay,
                Timeout.Infinite);

            Timer previous;
            if (_timers.TryRemove(session.TradeId, out previous))
            {
                try { previous.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                try { previous.Dispose(); } catch { }
            }

            _timers[session.TradeId] = timer;
        }
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed =
                true;

            try
            {
                // Stop capture processes first. Do not wait on NinjaTrader's
                // global Account.All lock during shutdown: the platform may
                // hold it while terminating, which can strand this foreground
                // cleanup thread after all windows have closed.
                _preRollRecorder.Dispose();
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process(
                    "[BEN PRE-ROLL] Shutdown error: " + ex.Message,
                    PrintTo.OutputTab1);
            }

            foreach (Timer timer
                     in _timers.Values)
            {
                try
                {
                    timer.Change(
                        Timeout.Infinite,
                        Timeout.Infinite);

                    timer.Dispose();
                }
                catch
                {
                }
            }

            _timers.Clear();
            try
            {
                _accountRefreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _accountRefreshTimer?.Dispose();
            }
            catch
            {
            }
            _accountRefreshTimer = null;
            _lastKnownPosition.Clear();
            _pendingOrders.Clear();

            try
            {
                Account.AccountStatusUpdate -= OnAccountStatusUpdate;
            }
            catch
            {
            }

            List<Account> subscribed;
            lock (_accountSubscriptionsSync)
            {
                subscribed = _subscribedAccounts.ToList();
                _subscribedAccounts.Clear();
            }

            UnsubscribeAccountEventsAsync(subscribed, true);
        }

        private void UnsubscribeAccountEventsAsync(
            List<Account> accounts,
            bool removeStatusHandler)
        {
            Thread cleanupThread = new Thread(() =>
            {
                try
                {
                    if (removeStatusHandler)
                        Account.AccountStatusUpdate -= OnAccountStatusUpdate;
                }
                catch
                {
                }

                foreach (Account account in accounts)
                {
                    try
                    {
                        account.ExecutionUpdate -= OnExecutionUpdate;
                        account.OrderUpdate -= OnOrderUpdate;
                        account.PositionUpdate -= OnPositionUpdate;
                    }
                    catch
                    {
                    }
                }
            })
            {
                IsBackground = true,
                Name = "BEN Trade Recorder account-event cleanup"
            };

            try
            {
                cleanupThread.Start();
            }
            catch
            {
            }
        }

        private class PendingOrderEvent
        {
            public long Sequence;
            public OrderEventArgs EventArgs;
        }
    }

    #endregion

    #region Settings Window

    public class BENTradeRecorderSettingsWindow : NTWindow
    {
        public BENTradeRecorderSettingsWindow()
        {
            Caption =
                "BEN Trade Recorder";

            Width = 560;
            Height = 700;

            TabControl tc =
                new TabControl();

            Content = tc;

            tc.AddNTTabPage(
                new BENTradeRecorderSettingsTab(
                    BENTradeRecorderAddOn.GetSettings()));
        }
    }
public class BENTradeRecorderSettingsTab : NTTabPage
    {
        private readonly BENTradeRecorderSettings _settings;

        private CheckBox _enable;
        private TextBox _saveFolder;
        private TextBox _ffmpeg;
        private TextBox _preTradeSeconds;
        private TextBox _postSeconds;
        private TextBox _fps;

        private ComboBox _encoder;
        private ComboBox _quality;
        private ComboBox _layout;
        private Button _preRollButton;
        private TextBlock _preRollStatus;
        private int _preRollUiSyncInProgress;

// BEN UI STATE ICON v1
private TextBlock _preRollStateIcon;
        private System.Windows.Threading.DispatcherTimer _preRollTimer;
        private DateTime _preRollUiStart =
            DateTime.MinValue;


        private readonly List<CheckBox>
            _monitorChecks =
                new List<CheckBox>();

        public BENTradeRecorderSettingsTab(
            BENTradeRecorderSettings settings)
        {
            _settings =
                settings ??
                BENTradeRecorderSettings.Load();
            InitializeLayout();

            // BEN UI STATE SYNC v2
            // Settings Window is only a view/controller.
            // Pre-Roll engine state remains the single source of truth.
            Loaded +=
                (s, e) =>
                {
                    SyncPreRollUi(true);
                };

            Unloaded +=
                (s, e) =>
                {
                    StopPreRollTimer();
                };
        }

        private void InitializeLayout()
        {
            var root =
                new Grid
                {
                    Margin =
                        new Thickness(15)
                };

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

            root.Children.Add(
                new TextBlock
                {
                    Text =
                        "BEN TRADE RECORDER",
                    FontSize = 16,
                    FontWeight =
                        FontWeights.Bold,
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                0,
                                174,
                                239)),
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            10)
                });

            var scroll =
                new ScrollViewer
                {
                    VerticalScrollBarVisibility =
                        ScrollBarVisibility.Auto
                };

            var stack =
                new StackPanel();

            _enable =
                new CheckBox
                {
                    Content =
                        "Enable Automated Trade Recorder",
                    IsChecked =
                        _settings.EnableRecorder,
                    Foreground =
                        Brushes.White,
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            10)
                };

            stack.Children.Add(
                _enable);

            stack.Children.Add(
                new TextBlock
                {
                    Text =
                        "AUTOMATIC VIDEO PRE-ROLL",
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                0,
                                174,
                                239)),
                    FontWeight =
                        FontWeights.Bold,
                    Margin =
                        new Thickness(
                            0,
                            5,
                            0,
                            6)
                });

            _preRollButton =
                new Button
                {
                    Content =
                        "AUTO PRE-ROLL",
                    Width = 190,
                    Height = 34,
                    IsEnabled = false,
                    HorizontalAlignment =
                        HorizontalAlignment.Left,
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                0,
                                122,
                                204)),
                    Foreground =
                        Brushes.White,
                    FontWeight =
                        FontWeights.Bold
                };

            stack.Children.Add(
                _preRollButton);
            // BEN UI STATE ICON v1
            _preRollStateIcon =
                new TextBlock
                {
                    Text =
                        "",
                    FontFamily =
                        new System.Windows.Media.FontFamily(
                            "Segoe UI Symbol"),
                    FontSize =
                        22,
                    FontWeight =
                        FontWeights.Bold,
                    Foreground =
                        Brushes.OrangeRed,
                    Margin =
                        new Thickness(
                            0,
                            4,
                            8,
                            0),
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    Visibility =
                        Visibility.Collapsed
                };

            _preRollStatus =
                new TextBlock
                {
                    Text =
                        "PRE-ROLL OFF",
                    Foreground =
                        Brushes.LightGray,
                    Margin =
                        new Thickness(
                            0,
                            6,
                            0,
                            12),
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            var preRollStatusRow =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    HorizontalAlignment =
                        HorizontalAlignment.Left
                };

            preRollStatusRow.Children.Add(
                _preRollStateIcon);

            preRollStatusRow.Children.Add(
                _preRollStatus);

            stack.Children.Add(
                preRollStatusRow);
AddEncoderControl(
                stack);

            AddQualityControl(
                stack);

            AddTextControl(
                stack,
                "Frame Rate",
                _settings.FrameRate.ToString(),
                out _fps);

            AddMonitorControls(
                stack);

            AddLayoutControl(
                stack);

            AddTextControl(
                stack,
                "FFmpeg Path",
                _settings.FFmpegPath,
                out _ffmpeg);

            AddTextControl(
                stack,
                "Save Folder",
                _settings.SaveFolder,
                out _saveFolder);

            AddTextControl(
                stack,
                "Pre-Trade Buffer Seconds",
                _settings.PreTradeSeconds.ToString(),
                out _preTradeSeconds);

            AddTextControl(
                stack,
                "Post Trade Seconds",
                _settings.PostTradeSeconds.ToString(),
                out _postSeconds);

            scroll.Content =
                stack;

            Grid.SetRow(
                scroll,
                1);

            root.Children.Add(
                scroll);

            var buttons =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    HorizontalAlignment =
                        HorizontalAlignment.Right,
                    Margin =
                        new Thickness(
                            0,
                            10,
                            0,
                            0)
                };

            var save =
                new Button
                {
                    Content =
                        "Save Settings",
                    Width = 120,
                    Height = 30,
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                0,
                                122,
                                204)),
                    Foreground =
                        Brushes.White
                };

            save.Click +=
                Save_Click;

            buttons.Children.Add(
                save);

            var cancel =
                new Button
                {
                    Content = "Close",
                    Width = 90,
                    Height = 30,
                    Margin =
                        new Thickness(
                            10,
                            0,
                            0,
                            0),
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                60,
                                60,
                                60)),
                    Foreground =
                        Brushes.White
                };

            cancel.Click +=
                (s, e) => CloseParentWindow();

            buttons.Children.Add(
                cancel);

            Grid.SetRow(
                buttons,
                2);

            root.Children.Add(
                buttons);

            Content =
                root;
        }

        private void AddEncoderControl(
            Panel stack)
        {
            _encoder =
                new ComboBox
                {
                    Height = 26
                };

            _encoder.Items.Add(
                "Auto (GPU Pipeline First)");

            _encoder.Items.Add(
                "NVIDIA GPU Pipeline (CUDA + NVENC)");

            _encoder.Items.Add(
                "CPU Pipeline (GDI + x264)");

            string current =
                _settings.SelectedEncoder ??
                "Auto";

            if (current.IndexOf("nvenc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                current.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                _encoder.SelectedIndex = 1;
            else if (current.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     current.IndexOf("x264", StringComparison.OrdinalIgnoreCase) >= 0)
                _encoder.SelectedIndex = 2;
            else
                _encoder.SelectedIndex = 0;

            var grid =
                CreateLabeledGrid(
                    "Hardware Encoder",
                    _encoder);

            stack.Children.Add(
                grid);

            stack.Children.Add(new TextBlock
            {
                Text = "GPU mode is strict: DXGI Desktop Duplication + direct D3D11/CUDA mapping + CUDA scaling/composition + NVENC. If its live preflight fails, explicit GPU mode stops; it never pretends to be GPU while using CPU. Auto may switch to the separately logged CPU pipeline. CPU mode uses GDI capture, CPU composition and x264.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                Margin = new Thickness(0, 6, 0, 8)
            });
        }

        private void AddQualityControl(
            Panel stack)
        {
            _quality =
                new ComboBox
                {
                    Height = 26
                };

            int[] values =
                {
                    18,19,20,21,22,23,24,25,26
                };

            int selected =
                3;

            for (int i = 0;
                 i < values.Length;
                 i++)
            {
                _quality.Items.Add(
                    values[i].ToString());

                if (values[i] ==
                    _settings.VideoQuality)
                {
                    selected = i;
                }
            }

            _quality.SelectedIndex =
                selected;

            stack.Children.Add(
                CreateLabeledGrid(
                    "Video Quality",
                    _quality));
        }

        private void AddLayoutControl(
            Panel stack)
        {
            _layout =
                new ComboBox
                {
                    Height = 26
                };

            _layout.Items.Add(
                "Horizontal");

            _layout.Items.Add(
                "Vertical");

            _layout.SelectedIndex =
                string.Equals(
                    _settings.LayoutMode,
                    "Vertical",
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;

            stack.Children.Add(
                CreateLabeledGrid(
                    "Multi-Monitor Layout",
                    _layout));
        }

        private void AddTextControl(
            Panel stack,
            string label,
            string value,
            out TextBox box)
        {
            box =
                new TextBox
                {
                    Text =
                        value ?? string.Empty,
                    Height = 26,
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                40,
                                40,
                                40)),
                    Foreground =
                        Brushes.White
                };

            stack.Children.Add(
                CreateLabeledGrid(
                    label,
                    box));
        }

        private Grid CreateLabeledGrid(
            string label,
            Control control)
        {
            var grid =
                new Grid
                {
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            10)
                };

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(220)
                });

            grid.Children.Add(
                new TextBlock
                {
                    Text =
                        label,
                    Foreground =
                        Brushes.LightGray,
                    VerticalAlignment =
                        VerticalAlignment.Center
                });

            Grid.SetColumn(
                control,
                1);

            grid.Children.Add(
                control);

            return grid;
        }

        private void AddMonitorControls(
            Panel stack)
        {
            stack.Children.Add(
                new TextBlock
                {
                    Text =
                        "Select Monitors",
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                0,
                                174,
                                239)),
                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            5)
                });

            var panel =
                new StackPanel();

            List<MonitorInfo> monitors =
                DisplayManager.GetMonitors();

            HashSet<int> selectedIndices =
                new HashSet<int>();
            HashSet<string> selectedNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.Equals(
                    _settings.SelectedMonitors,
                    "ALL",
                    StringComparison.OrdinalIgnoreCase))
            {
                foreach (MonitorInfo m in monitors)
                    selectedIndices.Add(m.Index);
            }
            else
            {
                foreach (string part
                         in (_settings.SelectedMonitors ?? string.Empty)
                             .Split(
                                 new[] { ',', ';' },
                                 StringSplitOptions.RemoveEmptyEntries))
                {
                    string token = part.Trim();
                    int index;

                    if (int.TryParse(
                        token,
                        out index))
                    {
                        selectedIndices.Add(index);
                    }
                    else if (!string.IsNullOrWhiteSpace(token))
                        selectedNames.Add(token);
                }
            }

            foreach (MonitorInfo monitor
                     in monitors)
            {
                var check =
                    new CheckBox
                    {
                        Content =
                            monitor.Description,
                        Tag =
                            monitor.SelectionId,
                        IsChecked =
                            selectedIndices.Contains(monitor.Index) ||
                            selectedNames.Contains(monitor.SelectionId) ||
                            selectedNames.Contains(monitor.DeviceName) ||
                            selectedNames.Contains(monitor.FriendlyName),
                        Foreground =
                            Brushes.White,
                        Margin =
                            new Thickness(
                                0,
                                2,
                                0,
                                2)
                    };

                _monitorChecks.Add(
                    check);

                panel.Children.Add(
                    check);
            }

            stack.Children.Add(
                panel);
        }
        // BEN UI STATE SYNC v2
        // The live Pre-Roll recorder is the source of truth.
        // This method never creates or resets recorder state.
                // BEN UI STATE SYNC v2
        // BEN UI STATE ICON v1
        // Live recorder state is the single source of truth.
        private void SyncPreRollUi(
            bool startTimer)
        {
            if (startTimer && _preRollTimer == null)
                StartPreRollTimer();

            if (Interlocked.CompareExchange(
                    ref _preRollUiSyncInProgress,
                    1,
                    0) != 0)
                return;

            bool enabled = _settings.EnableRecorder;
            int targetSeconds = Math.Max(60, _settings.PreTradeSeconds);

            Task.Run(() =>
            {
                bool running = false;
                DateTime started = DateTime.MinValue;
                int seconds = 0;
                string failure = string.Empty;
                Exception queryError = null;

                try
                {
                    running = BENTradeRecorderAddOn.IsPreRollRunning();
                    failure = BENTradeRecorderAddOn.GetPreRollFailureReason();

                    if (running)
                    {
                        started = BENTradeRecorderAddOn.GetPreRollStartTime();
                        if (started != DateTime.MinValue)
                        {
                            seconds = Math.Min(
                                targetSeconds,
                                BENTradeRecorderAddOn.GetPreRollBufferedSeconds());
                        }
                    }
                }
                catch (Exception ex)
                {
                    queryError = ex;
                }

                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            ApplyPreRollUi(
                                enabled,
                                targetSeconds,
                                running,
                                started,
                                seconds,
                                failure,
                                queryError,
                                startTimer);
                        }
                        finally
                        {
                            Interlocked.Exchange(
                                ref _preRollUiSyncInProgress,
                                0);
                        }
                    }));
                }
                catch
                {
                    Interlocked.Exchange(
                        ref _preRollUiSyncInProgress,
                        0);
                }
            });
        }

        private void ApplyPreRollUi(
            bool enabled,
            int targetSeconds,
            bool running,
            DateTime started,
            int seconds,
            string failure,
            Exception queryError,
            bool startTimer)
        {
            try
            {
                if (queryError != null)
                    throw queryError;

                if (!running)
                {
                    if (!enabled)
                        StopPreRollTimer();
                    else if (startTimer && _preRollTimer == null)
                        StartPreRollTimer();

                    _preRollUiStart = DateTime.MinValue;
                    _preRollButton.Content = "AUTO PRE-ROLL";
                    _preRollStateIcon.Text = "";
                    _preRollStateIcon.Visibility = Visibility.Collapsed;
                    _preRollStatus.Text = !enabled
                        ? "PRE-ROLL DISABLED IN SETTINGS"
                        : string.IsNullOrWhiteSpace(failure)
                            ? "AUTO PRE-ROLL STARTING..."
                            : "AUTO PRE-ROLL FAILED - " + failure;
                    _preRollStatus.Foreground = !enabled || string.IsNullOrWhiteSpace(failure)
                        ? Brushes.LightGray
                        : Brushes.OrangeRed;
                    return;
                }

                if (started == DateTime.MinValue)
                {
                    _preRollButton.Content = "AUTO PRE-ROLL";
                    _preRollStateIcon.Text = "⚠";
                    _preRollStateIcon.Foreground = Brushes.OrangeRed;
                    _preRollStateIcon.Visibility = Visibility.Visible;
                    _preRollStatus.Text = "AUTO PRE-ROLL ACTIVE - CHECKING BUFFER STATUS";
                    _preRollStatus.Foreground = Brushes.OrangeRed;

                    if (startTimer && _preRollTimer == null)
                        StartPreRollTimer();

                    return;
                }

                _preRollUiStart = started;
                _preRollButton.Content = "AUTO PRE-ROLL";

                if (seconds < targetSeconds)
                {
                    _preRollStateIcon.Text = "⚠";
                    _preRollStateIcon.Foreground = Brushes.OrangeRed;
                    _preRollStateIcon.Visibility = Visibility.Visible;
                    _preRollStatus.Text = $"AUTO BUILDING BUFFER: {seconds} / {targetSeconds} seconds";
                    _preRollStatus.Foreground = Brushes.OrangeRed;
                }
                else
                {
                    _preRollStateIcon.Text = "✔";
                    _preRollStateIcon.Foreground = Brushes.LimeGreen;
                    _preRollStateIcon.Visibility = Visibility.Visible;
                    _preRollStatus.Text = $"AUTO PRE-ROLL READY - BUFFER: {targetSeconds} / {targetSeconds} seconds";
                    _preRollStatus.Foreground = Brushes.LightGreen;
                }

                if (startTimer && _preRollTimer == null)
                    StartPreRollTimer();
            }
            catch (Exception ex)
            {
                StopPreRollTimer();
                _preRollButton.Content = "AUTO PRE-ROLL";
                _preRollStateIcon.Text = "⚠";
                _preRollStateIcon.Foreground = Brushes.OrangeRed;
                _preRollStateIcon.Visibility = Visibility.Visible;
                _preRollStatus.Text = "PRE-ROLL UI ERROR";
                _preRollStatus.Foreground = Brushes.OrangeRed;

                NinjaTrader.Code.Output.Process(
                    "[BEN PRE-ROLL UI SYNC ERROR] " + ex,
                    PrintTo.OutputTab1);
            }
        }

                private void StartPreRollTimer()
        {
            StopPreRollTimer();

            _preRollTimer =
                new System.Windows.Threading.DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromSeconds(1)
                };

            _preRollTimer.Tick +=
                (s, e) =>
                {
                    try
                    {
                        SyncPreRollUi(false);
                    }
                    catch (Exception ex)
                    {
                        StopPreRollTimer();

                        NinjaTrader.Code.Output.Process(
                            "[BEN PRE-ROLL UI TIMER ERROR] " +
                            ex,
                            PrintTo.OutputTab1);
                    }
                };

            _preRollTimer.Start();
        }


        private void StopPreRollTimer()
        {
            if (_preRollTimer != null)
            {
                try
                {
                    _preRollTimer.Stop();
                }
                catch
                {
                }

                _preRollTimer = null;
            }
        }
        private void CloseParentWindow()
        {
            try
            {
                Window parent =
                    Window.GetWindow(this);

                if (parent != null)
                    parent.Close();
            }
            catch
            {
            }
        }

        protected override void Restore(
            System.Xml.Linq.XElement element)
        {
            if (element == null)
                return;
        }

        protected override void Save(
            System.Xml.Linq.XElement element)
        {
            if (element == null)
                return;
        }
        private bool VerifyPersistedSettings()
        {
            try
            {
                BENTradeRecorderSettings saved =
                    BENTradeRecorderSettings.Load();

                if (saved == null)
                    return false;

                return
                    saved.EnableRecorder ==
                        _settings.EnableRecorder
                    &&
                    string.Equals(
                        saved.FFmpegPath ?? string.Empty,
                        _settings.FFmpegPath ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    string.Equals(
                        saved.SaveFolder ?? string.Empty,
                        _settings.SaveFolder ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    saved.FrameRate ==
                        _settings.FrameRate
                    &&
                    saved.PreTradeSeconds ==
                        _settings.PreTradeSeconds
                    &&
                    saved.PostTradeSeconds ==
                        _settings.PostTradeSeconds
                    &&
                    string.Equals(
                        saved.SelectedEncoder ?? string.Empty,
                        _settings.SelectedEncoder ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    saved.VideoQuality ==
                        _settings.VideoQuality
                    &&
                    string.Equals(
                        saved.LayoutMode ?? string.Empty,
                        _settings.LayoutMode ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    string.Equals(
                        saved.SelectedMonitors ?? string.Empty,
                        _settings.SelectedMonitors ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process(
                    "[BEN SETTINGS VERIFY ERROR] " +
                    ex,
                    PrintTo.OutputTab1);

                return false;
            }
        }
        private void Save_Click(
            object sender,
            RoutedEventArgs e)
        {
            _settings.EnableRecorder =
                _enable.IsChecked ?? true;

            _settings.FFmpegPath =
                _ffmpeg.Text.Trim();

            _settings.SaveFolder =
                _saveFolder.Text.Trim();

            int fps;

            if (int.TryParse(
                _fps.Text,
                out fps))
            {
                _settings.FrameRate =
                    Math.Max(
                        15,
                        Math.Min(
                            60,
                            fps));
            }

            int postSeconds;

            if (int.TryParse(
                _postSeconds.Text,
                out postSeconds))
            {
                _settings.PostTradeSeconds =
                    Math.Max(
                        1,
                        Math.Min(
                            60,
                            postSeconds));
            }

            int preTradeSeconds;

            if (int.TryParse(
                _preTradeSeconds.Text,
                out preTradeSeconds))
            {
                _settings.PreTradeSeconds =
                    Math.Max(
                        60,
                        Math.Min(
                            600,
                            preTradeSeconds));
            }

            if (_encoder.SelectedIndex == 1)
                _settings.SelectedEncoder = "NVIDIA NVENC";
            else if (_encoder.SelectedIndex == 2)
                _settings.SelectedEncoder = "CPU x264";
            else
                _settings.SelectedEncoder = "Auto";

            int[] values =
                {
                    18,19,20,21,22,23,24,25,26
                };

            if (_quality.SelectedIndex >= 0 &&
                _quality.SelectedIndex <
                values.Length)
            {
                _settings.VideoQuality =
                    values[
                        _quality.SelectedIndex];
            }

            _settings.LayoutMode =
                _layout.SelectedIndex == 1
                    ? "Vertical"
                    : "Horizontal";

            var monitorList =
                new List<string>();

            foreach (CheckBox check
                     in _monitorChecks)
            {
                if (check.IsChecked == true &&
                    check.Tag != null)
                {
                    monitorList.Add(
                        check.Tag.ToString());
                }
            }

            if (monitorList.Count == 0)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "Select at least one monitor for recording.",
                    "BEN Trade Recorder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _settings.SelectedMonitors =
                monitorList.Count == _monitorChecks.Count
                    ? "ALL"
                    : string.Join(",", monitorList);
            _settings.Save();

            if (VerifyPersistedSettings())
            {
                _preRollStatus.Text =
                    "NEW SAVE APPLIED";

                _preRollStatus.Foreground =
                    Brushes.LightGreen;
            }
            else
            {
                _preRollStatus.Text =
                    "SAVE FAILED - NOT VERIFIED";

                _preRollStatus.Foreground =
                    Brushes.OrangeRed;
            }

            SyncPreRollUi(true);

            bool enableRecorder = _settings.EnableRecorder;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (enableRecorder)
                        BENTradeRecorderAddOn.StartPreRoll();
                    else
                        BENTradeRecorderAddOn.StopPreRoll();
                }
                catch (Exception ex)
                {
                    NinjaTrader.Code.Output.Process(
                        "[BEN SETTINGS APPLY ERROR] " + ex,
                        PrintTo.OutputTab1);
                }
                finally
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(() => SyncPreRollUi(true)));
                    }
                    catch
                    {
                    }
                }
            });
        }

        protected override string GetHeaderPart(
            string variable)
        {
            return "Settings";
        }
    }


    #endregion

    #region NinjaTrader AddOn Entry

    public class BENTradeRecorderAddOn :
        AddOnBase
    {
        private static BENTradeRecorderSettings
            _settings;

        private static TradeLifecycleManager
            _lifecycle;

        private static TradeMonitor
            _monitor;
        private NTMenuItem _menuItem;

        private static BENTradeRecorderSettingsWindow
            _settingsWindow;private static readonly object
            InitLock =
                new object();

        private static int _autoStartIssued;
        private static int _autoStartCancelled;

        private static void EnsureInitialized()
        {
            lock (InitLock)
            {
                if (_settings == null)
                {
                    _settings =
                        BENTradeRecorderSettings.Load();
                }

                if (_lifecycle == null)
                {
                    _lifecycle =
                        new TradeLifecycleManager(
                            _settings);
                }

                if (_monitor == null)
                {
                    string ffmpeg =
                        FFmpegFinder.ResolveFFmpegPath(
                            _settings.FFmpegPath);

                    if (!string.IsNullOrWhiteSpace(ffmpeg))
                    {
                        FFmpegEncoderDetector
                            .WarmUpAsync(ffmpeg);
                    }

                    _monitor =
                        new TradeMonitor(
                            _settings,
                            _lifecycle);
                }
            }
        }

        internal static BENTradeRecorderSettings GetSettings()
        {
            EnsureInitialized();
            return _settings;
        }
        public static bool StartPreRoll()
        {
            Interlocked.Exchange(ref _autoStartCancelled, 1);
            EnsureInitialized();

            return _monitor != null &&
                   _monitor.StartPreRoll();
        }

        public static void StopPreRoll()
        {
            Interlocked.Exchange(ref _autoStartCancelled, 1);
            EnsureInitialized();

            if (_monitor != null)
                _monitor.StopPreRoll();
        }

        public static bool IsPreRollRunning()
        {
            EnsureInitialized();

            return _monitor != null &&
                   _monitor.IsPreRollRunning;
        }

        public static DateTime GetPreRollStartTime()
        {
            EnsureInitialized();

            return _monitor != null
                ? _monitor.PreRollStartTime
                : DateTime.MinValue;
        }

        public static int GetPreRollBufferedSeconds()
        {
            EnsureInitialized();
            return _monitor != null
                ? _monitor.PreRollBufferedSeconds
                : 0;
        }

        public static string GetPreRollFailureReason()
        {
            EnsureInitialized();
            return _monitor != null
                ? _monitor.PreRollFailureReason
                : "Recorder is not initialized.";
        }

        private static void StartAutomaticPreRoll()
        {
            if (Interlocked.CompareExchange(ref _autoStartIssued, 1, 0) != 0)
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    if (Interlocked.CompareExchange(ref _autoStartCancelled, 0, 0) != 0)
                        return;

                    TradeMonitor monitor;
                    bool enabled;

                    lock (InitLock)
                    {
                        monitor = _monitor;
                        enabled = _settings != null && _settings.EnableRecorder;
                    }

                    if (monitor == null || !enabled)
                        return;

                    // Starting FFmpeg can take time. Never hold InitLock while
                    // launching processes, because menu/UI requests also need it.
                    bool started = monitor.StartPreRoll();

                    if (started)
                        return;

                    Thread.Sleep(5000);
                }
            });
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description =
                    "BEN Trade Recorder - Deterministic NinjaTrader 8 Trade Journal Recorder";

                Name =
                    "BENTradeRecorder";
            }
            else if (State == State.Configure)
            {
                Interlocked.Exchange(ref _autoStartCancelled, 0);
                EnsureInitialized();
                StartAutomaticPreRoll();
            }
            else if (State == State.Active)
            {
                EnsureInitialized();
                StartAutomaticPreRoll();
            }
            else if (State == State.Terminated)
            {
                try
                {
                    if (_settingsWindow != null)
                    {
                        _settingsWindow.Close();
                        _settingsWindow = null;
                    }
                }
                catch
                {
                    _settingsWindow = null;
                }

                TradeMonitor monitor;
                TradeLifecycleManager lifecycle;

                lock (InitLock)
                {
                    monitor = _monitor;
                    _monitor = null;
                    lifecycle = _lifecycle;
                    _lifecycle = null;
                    Interlocked.Exchange(ref _autoStartIssued, 0);
                    Interlocked.Exchange(ref _autoStartCancelled, 1);
                }

                // Shutdown may flush video and financial files and wait for
                // FFmpeg. Keep that work off NinjaTrader's UI/Control Center
                // thread so its windows can close immediately.
                var shutdownThread = new Thread(() =>
                {
                    try
                    {
                        monitor?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        NinjaTrader.Code.Output.Process(
                            "[BEN PRE-ROLL] Shutdown error: " + ex,
                            PrintTo.OutputTab1);
                    }

                    try
                    {
                        lifecycle?.ClearAll();
                    }
                    catch (Exception ex)
                    {
                        NinjaTrader.Code.Output.Process(
                            "[BEN JOURNAL] Shutdown error: " + ex,
                            PrintTo.OutputTab1);
                    }
                })
                {
                    IsBackground = false,
                    Name = "BEN Trade Recorder shutdown"
                };

                try
                {
                    shutdownThread.Start();
                }
                catch (Exception ex)
                {
                    NinjaTrader.Code.Output.Process(
                        "[BEN ADDON] Could not start background shutdown: " + ex,
                        PrintTo.OutputTab1);
                }
            }
        }

        protected override void OnWindowCreated(
            Window window)
        {
            ControlCenter cc =
                window as ControlCenter;

            if (cc == null)
                return;

            EnsureInitialized();
            StartAutomaticPreRoll();

            NTMenuItem toolsMenu =
                cc.FindFirst(
                    "ControlCenterMenuItemTools")
                as NTMenuItem;

            if (toolsMenu == null)
            {
                toolsMenu =
                    cc.FindFirst(
                        "toolsMenuItem")
                    as NTMenuItem;
            }

            if (toolsMenu == null)
                return;

            foreach (object item
                     in toolsMenu.Items)
            {
                NTMenuItem existing =
                    item as NTMenuItem;

                if (existing != null &&
                    existing.Header != null &&
                    existing.Header
                        .ToString()
                        .Contains(
                            "BEN Trade Recorder"))
                {
                    return;
                }
            }

            _menuItem =
                new NTMenuItem
                {
                    Header =
                        "BEN Trade Recorder Settings..."
                };

            _menuItem.Click +=
                (s, e) =>
                {
                    try
                    {
                        EnsureInitialized();

                        if (_settingsWindow == null)
                        {
                            _settingsWindow =
                                new BENTradeRecorderSettingsWindow();

                            _settingsWindow.Closed +=
                                (sender, args) =>
                                {
                                    _settingsWindow = null;
                                };

                            _settingsWindow.Show();
                        }
                        else
                        {
                            if (_settingsWindow.WindowState ==
                                WindowState.Minimized)
                            {
                                _settingsWindow.WindowState =
                                    WindowState.Normal;
                            }

                            _settingsWindow.Activate();
                        }
                    }
                    catch (Exception ex)
                    {
                        NinjaTrader.Code.Output.Process(
                            "[BEN Trade Recorder] UI Error: " +
                            ex.Message,
                            PrintTo.OutputTab1);
                    }
                };

            toolsMenu.Items.Add(
                _menuItem);
        }

        protected override void OnWindowDestroyed(
            Window window)
        {
            ControlCenter cc =
                window as ControlCenter;

            if (cc == null)
                return;

            if (_menuItem != null &&
                _menuItem.Parent is ItemsControl)
            {
                var parent =
                    _menuItem.Parent
                    as ItemsControl;

                parent.Items.Remove(
                    _menuItem);

                _menuItem = null;
            }
        }
    }

    #endregion
}
