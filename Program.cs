using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("OrnataLight")]
[assembly: AssemblyDescription("Razer Ornata V3 X Lighting Control")]
[assembly: AssemblyProduct("OrnataLight")]
[assembly: AssemblyCompany("OrnataLight")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace OrnataLight
{
    internal enum LightMode
    {
        Static,
        Breathing,
        Spectrum,
        Off
    }

    internal sealed class AppSettings
    {
        public LightMode Mode;
        public byte R;
        public byte G;
        public byte B;
        public byte Brightness;

        public AppSettings()
        {
            Mode = LightMode.Static;
            R = 255;
            G = 255;
            B = 255;
            Brightness = 255;
        }

        public static string DataDir
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "OrnataLight");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(DataDir, "settings.ini"); }
        }

        public static string LogPath
        {
            get { return Path.Combine(DataDir, "status.log"); }
        }

        public static AppSettings LoadOrCreateDefault()
        {
            AppSettings s = new AppSettings();
            if (!File.Exists(SettingsPath))
            {
                s.Save();
                return s;
            }

            try
            {
                string[] lines = File.ReadAllLines(SettingsPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();

                    if (key == "mode")
                    {
                        s.Mode = ParseMode(val);
                    }
                    else if (key == "r")
                    {
                        byte parsed;
                        if (byte.TryParse(val, out parsed)) s.R = parsed;
                    }
                    else if (key == "g")
                    {
                        byte parsed;
                        if (byte.TryParse(val, out parsed)) s.G = parsed;
                    }
                    else if (key == "b")
                    {
                        byte parsed;
                        if (byte.TryParse(val, out parsed)) s.B = parsed;
                    }
                    else if (key == "brightness")
                    {
                        byte parsed;
                        if (byte.TryParse(val, out parsed)) s.Brightness = parsed;
                    }
                }
            }
            catch
            {
                // Fall back to defaults on corrupt settings file
            }

            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("mode=" + ModeToString(Mode));
            sb.AppendLine("r=" + R.ToString());
            sb.AppendLine("g=" + G.ToString());
            sb.AppendLine("b=" + B.ToString());
            sb.AppendLine("brightness=" + Brightness.ToString());
            File.WriteAllText(SettingsPath, sb.ToString(), Encoding.UTF8);
        }

        public static string ModeToString(LightMode mode)
        {
            switch (mode)
            {
                case LightMode.Static: return "static";
                case LightMode.Breathing: return "breathing";
                case LightMode.Spectrum: return "spectrum";
                case LightMode.Off: return "off";
                default: return "static";
            }
        }

        public static string ModeToDisplayName(LightMode mode)
        {
            switch (mode)
            {
                case LightMode.Static: return "Static";
                case LightMode.Breathing: return "Breathing";
                case LightMode.Spectrum: return "Spectrum Cycle";
                case LightMode.Off: return "Off";
                default: return "Static";
            }
        }

        public static LightMode ParseMode(string text)
        {
            if (text == null) return LightMode.Static;
            switch (text.Trim().ToLowerInvariant())
            {
                case "static": return LightMode.Static;
                case "breathing": return LightMode.Breathing;
                case "spectrum": return LightMode.Spectrum;
                case "off": return LightMode.Off;
                default: return LightMode.Static;
            }
        }
    }

    internal sealed class ApplyResult
    {
        public bool Found;
        public bool Success;
        public string DevicePath;
        public ushort ProductId;
        public byte BrightnessStatus;
        public byte ModeStatus;
        public byte UsedStorage;
        public string ErrorMessage;

        public ApplyResult()
        {
            DevicePath = "";
            ErrorMessage = "";
        }
    }

    internal static class RazerOrnataHid
    {
        private const ushort RazerVid = 0x1532;
        private const ushort PidOrnataV3X = 0x0294;
        private const ushort PidOrnataV3XRev2 = 0x02A2;

        private const byte TransactionId = 0x1F;
        private const byte LedBacklight = 0x05;
        private const byte StorageVarstore = 0x01;
        private const byte StorageNostore = 0x00;
        private const byte StatusOk = 0x02;
        private const byte StatusNew = 0x00;
        private const byte StatusBusy = 0x01;
        private const int ReportLength = 91;

        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const uint FileShareReadWrite = 0x00000003;
        private const uint OpenExisting = 0x00000003;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
            uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
            out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition,
            uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(IntPtr hidDeviceObject, [Out] byte[] reportBuffer, int reportBufferLength);

        private static bool IsTargetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string lower = path.ToLowerInvariant();
            if (!lower.Contains("vid_1532")) return false;
            return lower.Contains("pid_02a2") || lower.Contains("pid_0294");
        }

        private static bool IsTargetPid(ushort pid)
        {
            return pid == PidOrnataV3XRev2 || pid == PidOrnataV3X;
        }

        private static IntPtr OpenControlInterface(out string selectedPath, out ushort selectedPid, out string error)
        {
            selectedPath = "";
            selectedPid = 0;
            error = "";

            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (devInfo == InvalidHandle)
            {
                error = "SetupDiGetClassDevs failed: Win32=" + Marshal.GetLastWin32Error();
                return InvalidHandle;
            }

            try
            {
                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                uint index = 0;

                while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, index, ref ifData))
                {
                    index++;
                    uint reqSize = 0;
                    SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                    if (reqSize == 0) continue;

                    IntPtr detailBuf = Marshal.AllocHGlobal((int)reqSize);
                    try
                    {
                        Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);
                        uint unused = 0;
                        if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuf, reqSize, out unused, IntPtr.Zero))
                        {
                            continue;
                        }

                        string path = Marshal.PtrToStringAuto(IntPtr.Add(detailBuf, 4));
                        if (!IsTargetPath(path)) continue;

                        IntPtr h = CreateFile(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                        if (h == InvalidHandle) continue;

                        bool keepHandle = false;
                        try
                        {
                            HIDD_ATTRIBUTES attrs = new HIDD_ATTRIBUTES();
                            attrs.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                            if (!HidD_GetAttributes(h, ref attrs)) continue;
                            if (attrs.VendorID != RazerVid || !IsTargetPid(attrs.ProductID)) continue;

                            IntPtr ppd;
                            if (!HidD_GetPreparsedData(h, out ppd)) continue;
                            try
                            {
                                HIDP_CAPS caps;
                                HidP_GetCaps(ppd, out caps);
                                if (caps.FeatureReportByteLength == ReportLength)
                                {
                                    selectedPath = path;
                                    selectedPid = attrs.ProductID;
                                    keepHandle = true;
                                    return h;
                                }
                            }
                            finally
                            {
                                HidD_FreePreparsedData(ppd);
                            }
                        }
                        finally
                        {
                            if (!keepHandle)
                            {
                                CloseHandle(h);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailBuf);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfo);
            }

            error = "Razer Ornata V3 X (VID_1532 PID_02A2/0294, FeatureLen=91) not found.";
            return InvalidHandle;
        }

        private static byte[] BuildReport(byte commandClass, byte commandId, byte dataSize, byte[] args)
        {
            byte[] buf = new byte[ReportLength];
            buf[0] = 0x00;
            buf[1] = 0x00;
            buf[2] = TransactionId;
            buf[3] = 0x00;
            buf[4] = 0x00;
            buf[5] = 0x00;
            buf[6] = dataSize;
            buf[7] = commandClass;
            buf[8] = commandId;

            if (args != null)
            {
                int count = Math.Min(args.Length, 80);
                Buffer.BlockCopy(args, 0, buf, 9, count);
            }

            byte crc = 0;
            for (int i = 3; i < 89; i++)
            {
                crc ^= buf[i];
            }
            buf[89] = crc;
            buf[90] = 0x00;
            return buf;
        }

        private static bool SendAndWait(IntPtr handle, byte[] report, out byte statusByte, out string error)
        {
            statusByte = 0xFF;
            error = "";

            bool setOk = false;
            int lastErr = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (HidD_SetFeature(handle, report, ReportLength))
                {
                    setOk = true;
                    break;
                }
                lastErr = Marshal.GetLastWin32Error();
                Thread.Sleep(40);
            }

            if (!setOk)
            {
                error = "HidD_SetFeature failed: Win32=" + lastErr;
                return false;
            }

            byte[] resp = new byte[ReportLength];
            for (int attempt = 0; attempt < 25; attempt++)
            {
                Thread.Sleep(4);
                Array.Clear(resp, 0, resp.Length);
                resp[0] = 0x00;
                if (!HidD_GetFeature(handle, resp, ReportLength))
                {
                    lastErr = Marshal.GetLastWin32Error();
                    continue;
                }

                if (resp[2] == report[2] && resp[7] == report[7] && resp[8] == report[8] &&
                    resp[1] != StatusNew && resp[1] != StatusBusy)
                {
                    statusByte = resp[1];
                    return true;
                }
            }

            error = "HidD_GetFeature timed out or failed: Win32=" + lastErr + ", lastStatus=0x" + resp[1].ToString("X2");
            return false;
        }

        private static byte[] BuildBrightnessReport(byte storage, byte brightness)
        {
            byte[] args = new byte[] { storage, LedBacklight, brightness };
            return BuildReport(0x0F, 0x04, 0x03, args);
        }

        private static byte[] BuildModeReport(byte storage, AppSettings settings)
        {
            switch (settings.Mode)
            {
                case LightMode.Static:
                    return BuildReport(0x0F, 0x02, 0x09, new byte[]
                    {
                        storage, LedBacklight, 0x01, 0x00, 0x00, 0x01,
                        settings.R, settings.G, settings.B
                    });

                case LightMode.Breathing:
                    return BuildReport(0x0F, 0x02, 0x09, new byte[]
                    {
                        storage, LedBacklight, 0x02, 0x01, 0x00, 0x01,
                        settings.R, settings.G, settings.B
                    });

                case LightMode.Spectrum:
                    return BuildReport(0x0F, 0x02, 0x06, new byte[]
                    {
                        storage, LedBacklight, 0x03
                    });

                case LightMode.Off:
                default:
                    return BuildReport(0x0F, 0x02, 0x06, new byte[]
                    {
                        storage, LedBacklight, 0x00
                    });
            }
        }

        private static bool TryApplyWithStorage(IntPtr handle, byte storage, AppSettings settings,
            out byte brightnessStatus, out byte modeStatus, out string error)
        {
            brightnessStatus = StatusOk;
            modeStatus = 0xFF;
            error = "";

            if (settings.Mode != LightMode.Off)
            {
                byte[] brightReport = BuildBrightnessReport(storage, settings.Brightness);
                string brightErr;
                if (!SendAndWait(handle, brightReport, out brightnessStatus, out brightErr))
                {
                    error = "Brightness send failed: " + brightErr;
                    return false;
                }
                if (brightnessStatus != StatusOk)
                {
                    error = "Brightness firmware status=0x" + brightnessStatus.ToString("X2");
                    return false;
                }
                Thread.Sleep(15);
            }

            byte[] modeReport = BuildModeReport(storage, settings);
            string modeErr;
            if (!SendAndWait(handle, modeReport, out modeStatus, out modeErr))
            {
                error = "Mode send failed: " + modeErr;
                return false;
            }
            if (modeStatus != StatusOk)
            {
                error = "Mode firmware status=0x" + modeStatus.ToString("X2");
                return false;
            }

            return true;
        }

        public static ApplyResult Apply(AppSettings settings)
        {
            ApplyResult result = new ApplyResult();
            string path;
            ushort pid;
            string openErr;

            IntPtr handle = OpenControlInterface(out path, out pid, out openErr);
            if (handle == InvalidHandle)
            {
                result.Found = false;
                result.Success = false;
                result.ErrorMessage = openErr;
                return result;
            }

            result.Found = true;
            result.DevicePath = path;
            result.ProductId = pid;

            try
            {
                byte bStatus;
                byte mStatus;
                string err;

                if (TryApplyWithStorage(handle, StorageVarstore, settings, out bStatus, out mStatus, out err))
                {
                    result.UsedStorage = StorageVarstore;
                    result.BrightnessStatus = bStatus;
                    result.ModeStatus = mStatus;
                    result.Success = true;
                    return result;
                }

                byte bStatus2;
                byte mStatus2;
                string err2;
                if (TryApplyWithStorage(handle, StorageNostore, settings, out bStatus2, out mStatus2, out err2))
                {
                    result.UsedStorage = StorageNostore;
                    result.BrightnessStatus = bStatus2;
                    result.ModeStatus = mStatus2;
                    result.Success = true;
                    return result;
                }

                result.UsedStorage = StorageVarstore;
                result.BrightnessStatus = bStatus;
                result.ModeStatus = mStatus;
                result.Success = false;
                result.ErrorMessage = err + " | Fallback NOSTORE: " + err2;
                return result;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }

    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "OrnataLight";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    object val = key.GetValue(ValueName);
                    return val != null && val.ToString().Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null) return;
                if (enabled)
                {
                    string exePath = Application.ExecutablePath;
                    key.SetValue(ValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                }
                else
                {
                    if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
        }
    }

    internal sealed class EventWatcherWindow : NativeWindow, IDisposable
    {
        private const int WM_POWERBROADCAST = 0x0218;
        private const int WM_DEVICECHANGE = 0x0219;

        private const int PBT_APMRESUMESUSPEND = 0x0007;
        private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        private const int PBT_POWERSETTINGCHANGE = 0x8013;

        private const int DBT_DEVNODES_CHANGED = 0x0007;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;
        private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

        private static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
        private static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
            public char dbcc_name;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POWERBROADCAST_SETTING
        {
            public Guid PowerSetting;
            public int DataLength;
            public byte Data;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr notificationFilter, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterDeviceNotification(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        private IntPtr _devNotifyHandle = IntPtr.Zero;
        private IntPtr _powerNotifyHandle = IntPtr.Zero;
        private readonly int _taskbarCreatedMsg;
        private int _lastDisplayState = -1;
        private bool _disposed;

        public event Action<string, int> ReapplyRequested;
        public event Action TaskbarCreated;

        public EventWatcherWindow()
        {
            _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
            CreateParams cp = new CreateParams();
            cp.Caption = "OrnataLightWatcher";
            CreateHandle(cp);
            RegisterNotifications();
        }

        private void RegisterNotifications()
        {
            DEV_BROADCAST_DEVICEINTERFACE dbi = new DEV_BROADCAST_DEVICEINTERFACE();
            dbi.dbcc_size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE));
            dbi.dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE;
            dbi.dbcc_reserved = 0;
            dbi.dbcc_classguid = GUID_DEVINTERFACE_HID;
            dbi.dbcc_name = '\0';

            IntPtr mem = Marshal.AllocHGlobal(dbi.dbcc_size);
            try
            {
                Marshal.StructureToPtr(dbi, mem, false);
                _devNotifyHandle = RegisterDeviceNotification(Handle, mem, DEVICE_NOTIFY_WINDOW_HANDLE);
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }

            Guid dispGuid = GUID_CONSOLE_DISPLAY_STATE;
            _powerNotifyHandle = RegisterPowerSettingNotification(Handle, ref dispGuid, DEVICE_NOTIFY_WINDOW_HANDLE);
        }

        protected override void WndProc(ref Message m)
        {
            if (_taskbarCreatedMsg != 0 && m.Msg == _taskbarCreatedMsg)
            {
                Action tbHandler = TaskbarCreated;
                if (tbHandler != null)
                {
                    tbHandler();
                }
            }
            else if (m.Msg == WM_DEVICECHANGE)
            {
                int wParam = m.WParam.ToInt32();
                if (wParam == DBT_DEVICEARRIVAL || wParam == DBT_DEVNODES_CHANGED)
                {
                    RaiseReapply("USB_CHANGE", 700);
                }
            }
            else if (m.Msg == WM_POWERBROADCAST)
            {
                int wParam = m.WParam.ToInt32();
                if (wParam == PBT_APMRESUMEAUTOMATIC || wParam == PBT_APMRESUMESUSPEND)
                {
                    RaiseReapply("POWER_RESUME", 1200);
                }
                else if (wParam == PBT_POWERSETTINGCHANGE && m.LParam != IntPtr.Zero)
                {
                    POWERBROADCAST_SETTING ps = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(
                        m.LParam, typeof(POWERBROADCAST_SETTING));
                    if (ps.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
                    {
                        int offset = Marshal.OffsetOf(typeof(POWERBROADCAST_SETTING), "Data").ToInt32();
                        int state = ps.DataLength >= 4
                            ? Marshal.ReadInt32(m.LParam, offset)
                            : Marshal.ReadByte(m.LParam, offset);

                        bool isInitial = (_lastDisplayState == -1);
                        int prev = _lastDisplayState;
                        _lastDisplayState = state;

                        if (!isInitial && state == 1 && prev != 1)
                        {
                            RaiseReapply("DISPLAY_ON", 1200);
                        }
                    }
                }
            }

            base.WndProc(ref m);
        }

        private void RaiseReapply(string reason, int delayMs)
        {
            Action<string, int> handler = ReapplyRequested;
            if (handler != null)
            {
                handler(reason, delayMs);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_devNotifyHandle != IntPtr.Zero)
            {
                UnregisterDeviceNotification(_devNotifyHandle);
                _devNotifyHandle = IntPtr.Zero;
            }
            if (_powerNotifyHandle != IntPtr.Zero)
            {
                UnregisterPowerSettingNotification(_powerNotifyHandle);
                _powerNotifyHandle = IntPtr.Zero;
            }
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private struct ColorPreset
        {
            public string Name;
            public byte R;
            public byte G;
            public byte B;

            public ColorPreset(string name, byte r, byte g, byte b)
            {
                Name = name;
                R = r;
                G = g;
                B = b;
            }
        }

        private static readonly ColorPreset[] Presets = new ColorPreset[]
        {
            new ColorPreset("White (Static)", 255, 255, 255),
            new ColorPreset("Warm White", 255, 215, 160),
            new ColorPreset("Ice Blue", 100, 210, 255),
            new ColorPreset("Soft Cyan", 0, 255, 200),
            new ColorPreset("Amber", 255, 140, 0),
            new ColorPreset("Red", 255, 0, 0),
            new ColorPreset("Green", 0, 255, 0),
            new ColorPreset("Blue", 0, 90, 255),
            new ColorPreset("Purple", 170, 0, 255)
        };

        private static readonly int[] BrightnessLevels = new int[] { 100, 75, 50, 25, 10 };
        private static readonly byte[] BrightnessBytes = new byte[] { 255, 191, 128, 64, 26 };

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private readonly AppSettings _settings;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _headerItem;
        private readonly ToolStripMenuItem[] _presetItems;
        private readonly ToolStripMenuItem _customColorItem;
        private readonly ToolStripMenuItem _modeStaticItem;
        private readonly ToolStripMenuItem _modeBreathingItem;
        private readonly ToolStripMenuItem _modeSpectrumItem;
        private readonly ToolStripMenuItem _modeOffItem;
        private readonly ToolStripMenuItem[] _brightnessItems;
        private readonly ToolStripMenuItem _startupItem;

        private readonly EventWatcherWindow _watcher;
        private readonly System.Windows.Forms.Timer _debounceTimer;
        private readonly System.Windows.Forms.Timer _fallbackTimer;
        private string _pendingReason = "STARTUP";
        private Icon _currentIcon;
        private IntPtr _currentIconHandle = IntPtr.Zero;
        private bool _disposed;

        public TrayApp()
        {
            _settings = AppSettings.LoadOrCreateDefault();

            _menu = new ContextMenuStrip();
            _menu.AutoClose = true;
            _headerItem = new ToolStripMenuItem("Razer Ornata V3 X");
            _headerItem.Enabled = false;
            _menu.Items.Add(_headerItem);
            _menu.Items.Add(new ToolStripSeparator());

            _presetItems = new ToolStripMenuItem[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
            {
                int idx = i;
                ColorPreset p = Presets[i];
                ToolStripMenuItem item = new ToolStripMenuItem(p.Name);
                item.Click += delegate { SelectPreset(Presets[idx]); };
                _presetItems[i] = item;
                _menu.Items.Add(item);
            }

            _customColorItem = new ToolStripMenuItem("Custom Color...");
            _customColorItem.Click += delegate { PickCustomColor(); };
            _menu.Items.Add(_customColorItem);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem modeMenu = new ToolStripMenuItem("Mode");
            _modeStaticItem = new ToolStripMenuItem("Static");
            _modeBreathingItem = new ToolStripMenuItem("Breathing");
            _modeSpectrumItem = new ToolStripMenuItem("Spectrum Cycle");
            _modeOffItem = new ToolStripMenuItem("Off");

            _modeStaticItem.Click += delegate { SetMode(LightMode.Static); };
            _modeBreathingItem.Click += delegate { SetMode(LightMode.Breathing); };
            _modeSpectrumItem.Click += delegate { SetMode(LightMode.Spectrum); };
            _modeOffItem.Click += delegate { SetMode(LightMode.Off); };

            modeMenu.DropDownItems.Add(_modeStaticItem);
            modeMenu.DropDownItems.Add(_modeBreathingItem);
            modeMenu.DropDownItems.Add(_modeSpectrumItem);
            modeMenu.DropDownItems.Add(_modeOffItem);
            _menu.Items.Add(modeMenu);

            ToolStripMenuItem brightMenu = new ToolStripMenuItem("Brightness");
            _brightnessItems = new ToolStripMenuItem[BrightnessLevels.Length];
            for (int i = 0; i < BrightnessLevels.Length; i++)
            {
                int idx = i;
                ToolStripMenuItem bItem = new ToolStripMenuItem(BrightnessLevels[i].ToString() + "%");
                bItem.Click += delegate { SetBrightness(BrightnessBytes[idx]); };
                _brightnessItems[i] = bItem;
                brightMenu.DropDownItems.Add(bItem);
            }
            _menu.Items.Add(brightMenu);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem reapplyItem = new ToolStripMenuItem("Re-apply Now");
            reapplyItem.Click += delegate { ApplyCurrent("MANUAL"); };
            _menu.Items.Add(reapplyItem);

            _startupItem = new ToolStripMenuItem("Start with Windows");
            _startupItem.Click += delegate { ToggleStartup(); };
            _menu.Items.Add(_startupItem);

            _menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { ExitThread(); };
            _menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.MouseUp += OnTrayMouseUp;

            _debounceTimer = new System.Windows.Forms.Timer();
            _debounceTimer.Tick += OnDebounceTick;

            _fallbackTimer = new System.Windows.Forms.Timer();
            _fallbackTimer.Tick += OnFallbackTick;

            _watcher = new EventWatcherWindow();
            _watcher.ReapplyRequested += OnWatcherReapply;
            _watcher.TaskbarCreated += OnTaskbarCreated;

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            UpdateUiState();
            _notifyIcon.Visible = true;

            ApplyResult initial = ApplyCurrent("STARTUP");
            if (!initial.Success)
            {
                ScheduleRetrySequence();
            }
        }

        private void OnTaskbarCreated()
        {
            _notifyIcon.Visible = false;
            UpdateTrayIcon();
            _notifyIcon.Visible = true;
        }

        private void OnTrayMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                ShowTrayMenu();
            }
        }

        private void ShowTrayMenu()
        {
            POINT rawPt;
            Point pt = GetCursorPos(out rawPt)
                ? new Point(rawPt.X, rawPt.Y)
                : Cursor.Position;

            Screen screen = Screen.FromPoint(pt);
            Rectangle wa = screen.WorkingArea;

            if (!_menu.IsHandleCreated)
            {
                IntPtr unused = _menu.Handle;
            }
            _menu.PerformLayout();
            Size menuSize = _menu.GetPreferredSize(Size.Empty);

            int x = ClampMenuCoord(pt.X, menuSize.Width, wa.Left, wa.Right);
            int y = ClampMenuCoord(pt.Y, menuSize.Height, wa.Top, wa.Bottom);

            SetForegroundWindow(_watcher.Handle);
            _menu.Show(new Point(x, y), ToolStripDropDownDirection.BelowRight);
        }

        private static int ClampMenuCoord(int cursorCoord, int menuExtent, int areaMin, int areaMax)
        {
            int pos = cursorCoord;
            if (pos + menuExtent > areaMax)
            {
                pos = cursorCoord - menuExtent;
            }
            if (pos + menuExtent > areaMax)
            {
                pos = areaMax - menuExtent;
            }
            if (pos < areaMin)
            {
                pos = areaMin;
            }
            return pos;
        }

        private void OnWatcherReapply(string reason, int delayMs)
        {
            ScheduleReapply(reason, delayMs, reason == "POWER_RESUME" || reason == "DISPLAY_ON");
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                ScheduleReapply("SYS_POWER_RESUME", 1200, true);
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                ScheduleReapply("SESSION_UNLOCK", 600, false);
            }
        }

        private void ScheduleReapply(string reason, int delayMs, bool scheduleWakeFallback)
        {
            _pendingReason = reason;
            _debounceTimer.Stop();
            _debounceTimer.Interval = Math.Max(100, delayMs);
            _debounceTimer.Start();

            if (scheduleWakeFallback)
            {
                _fallbackTimer.Stop();
                _fallbackTimer.Interval = 3500;
                _fallbackTimer.Start();
            }
        }

        private void ScheduleRetrySequence()
        {
            _pendingReason = "STARTUP_RETRY_1500MS";
            _debounceTimer.Stop();
            _debounceTimer.Interval = 1500;
            _debounceTimer.Start();

            _fallbackTimer.Stop();
            _fallbackTimer.Interval = 4000;
            _fallbackTimer.Start();
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            ApplyCurrent(_pendingReason);
        }

        private void OnFallbackTick(object sender, EventArgs e)
        {
            _fallbackTimer.Stop();
            ApplyCurrent(_pendingReason + "_FALLBACK");
        }

        private void SelectPreset(ColorPreset preset)
        {
            _settings.R = preset.R;
            _settings.G = preset.G;
            _settings.B = preset.B;
            if (_settings.Mode == LightMode.Off || _settings.Mode == LightMode.Spectrum)
            {
                _settings.Mode = LightMode.Static;
            }
            _settings.Save();
            UpdateUiState();
            ApplyCurrent("PRESET_" + preset.Name);
        }

        private void PickCustomColor()
        {
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                dlg.Color = Color.FromArgb(_settings.R, _settings.G, _settings.B);
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _settings.R = dlg.Color.R;
                    _settings.G = dlg.Color.G;
                    _settings.B = dlg.Color.B;
                    if (_settings.Mode == LightMode.Off || _settings.Mode == LightMode.Spectrum)
                    {
                        _settings.Mode = LightMode.Static;
                    }
                    _settings.Save();
                    UpdateUiState();
                    ApplyCurrent("CUSTOM_COLOR");
                }
            }
        }

        private void SetMode(LightMode mode)
        {
            _settings.Mode = mode;
            _settings.Save();
            UpdateUiState();
            ApplyCurrent("MODE_" + AppSettings.ModeToString(mode));
        }

        private void SetBrightness(byte brightness)
        {
            _settings.Brightness = brightness;
            if (_settings.Mode == LightMode.Off)
            {
                _settings.Mode = LightMode.Static;
            }
            _settings.Save();
            UpdateUiState();
            ApplyCurrent("BRIGHTNESS");
        }

        private void ToggleStartup()
        {
            bool next = !StartupManager.IsEnabled();
            StartupManager.SetEnabled(next);
            UpdateUiState();
        }

        private ApplyResult ApplyCurrent(string reason)
        {
            ApplyResult res = RazerOrnataHid.Apply(_settings);
            Program.WriteStatusLog(reason, _settings, res);
            return res;
        }

        private void UpdateUiState()
        {
            string hex = string.Format("#{0:X2}{1:X2}{2:X2}", _settings.R, _settings.G, _settings.B);
            int pct = (int)Math.Round(_settings.Brightness * 100.0 / 255.0);
            string modeLabel = AppSettings.ModeToDisplayName(_settings.Mode);

            if (_settings.Mode == LightMode.Static || _settings.Mode == LightMode.Breathing)
            {
                _headerItem.Text = string.Format("Razer Ornata V3 X — {0} {1} ({2}%)", modeLabel, hex, pct);
                _notifyIcon.Text = TruncateTip(string.Format("OrnataLight: {0} {1} ({2}%)", modeLabel, hex, pct));
            }
            else if (_settings.Mode == LightMode.Spectrum)
            {
                _headerItem.Text = string.Format("Razer Ornata V3 X — {0} ({1}%)", modeLabel, pct);
                _notifyIcon.Text = TruncateTip(string.Format("OrnataLight: {0} ({1}%)", modeLabel, pct));
            }
            else
            {
                _headerItem.Text = "Razer Ornata V3 X — Off";
                _notifyIcon.Text = "OrnataLight: Off";
            }

            _modeStaticItem.Checked = _settings.Mode == LightMode.Static;
            _modeBreathingItem.Checked = _settings.Mode == LightMode.Breathing;
            _modeSpectrumItem.Checked = _settings.Mode == LightMode.Spectrum;
            _modeOffItem.Checked = _settings.Mode == LightMode.Off;

            bool isColorMode = _settings.Mode == LightMode.Static || _settings.Mode == LightMode.Breathing;
            bool matchedPreset = false;
            for (int i = 0; i < Presets.Length; i++)
            {
                bool match = isColorMode &&
                    Presets[i].R == _settings.R &&
                    Presets[i].G == _settings.G &&
                    Presets[i].B == _settings.B;
                _presetItems[i].Checked = match;
                if (match) matchedPreset = true;
            }
            _customColorItem.Checked = isColorMode && !matchedPreset;

            int closestIdx = 0;
            int minDiff = int.MaxValue;
            for (int i = 0; i < BrightnessBytes.Length; i++)
            {
                int diff = Math.Abs((int)BrightnessBytes[i] - (int)_settings.Brightness);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestIdx = i;
                }
            }
            for (int i = 0; i < _brightnessItems.Length; i++)
            {
                _brightnessItems[i].Checked = (i == closestIdx);
            }

            _startupItem.Checked = StartupManager.IsEnabled();
            UpdateTrayIcon();
        }

        private static string TruncateTip(string text)
        {
            if (text.Length <= 63) return text;
            return text.Substring(0, 63);
        }

        private void UpdateTrayIcon()
        {
            Color swatch;
            if (_settings.Mode == LightMode.Off)
            {
                swatch = Color.FromArgb(70, 70, 76);
            }
            else if (_settings.Mode == LightMode.Spectrum)
            {
                swatch = Color.FromArgb(0, 210, 255);
            }
            else
            {
                swatch = Color.FromArgb(_settings.R, _settings.G, _settings.B);
            }

            Icon nextIcon = CreateSwatchIcon(swatch, _settings.Mode == LightMode.Spectrum);
            Icon oldIcon = _currentIcon;
            IntPtr oldHandle = _currentIconHandle;

            _currentIcon = nextIcon;
            _currentIconHandle = nextIcon.Handle;
            _notifyIcon.Icon = nextIcon;

            if (oldIcon != null)
            {
                oldIcon.Dispose();
            }
            if (oldHandle != IntPtr.Zero)
            {
                DestroyIcon(oldHandle);
            }
        }

        private static Brush CreateSwatchBrush(Rectangle rect, Color swatch, int alpha, bool spectrum)
        {
            if (spectrum)
            {
                LinearGradientBrush grad = new LinearGradientBrush(
                    rect,
                    Color.FromArgb(alpha, 255, 70, 110),
                    Color.FromArgb(alpha, 60, 180, 255),
                    0f);
                ColorBlend cb = new ColorBlend(3);
                cb.Positions = new float[] { 0.0f, 0.5f, 1.0f };
                cb.Colors = new Color[]
                {
                    Color.FromArgb(alpha, 255, 70, 110),
                    Color.FromArgb(alpha, 90, 255, 130),
                    Color.FromArgb(alpha, 60, 180, 255)
                };
                grad.InterpolationColors = cb;
                return grad;
            }
            return new SolidBrush(Color.FromArgb(alpha, swatch));
        }

        private static Icon CreateSwatchIcon(Color swatch, bool spectrum)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                using (GraphicsPath bgPath = CreateRoundedRect(new Rectangle(1, 1, 30, 30), 6))
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(22, 24, 31)))
                using (Pen bgPen = new Pen(Color.FromArgb(70, 76, 92), 1.2f))
                {
                    g.FillPath(bgBrush, bgPath);
                    g.DrawPath(bgPen, bgPath);
                }

                Rectangle outerGlowRect = new Rectangle(4, 5, 24, 23);
                using (GraphicsPath outerGlowPath = CreateRoundedRect(outerGlowRect, 5))
                using (Brush outerGlowBrush = CreateSwatchBrush(outerGlowRect, swatch, 70, spectrum))
                {
                    g.FillPath(outerGlowBrush, outerGlowPath);
                }

                Rectangle innerGlowRect = new Rectangle(5, 6, 22, 21);
                using (GraphicsPath innerGlowPath = CreateRoundedRect(innerGlowRect, 4))
                using (Brush innerGlowBrush = CreateSwatchBrush(innerGlowRect, swatch, 130, spectrum))
                {
                    g.FillPath(innerGlowBrush, innerGlowPath);
                }

                Rectangle keyRect = new Rectangle(6, 5, 20, 20);
                using (GraphicsPath keyPath = CreateRoundedRect(keyRect, 4))
                using (SolidBrush keyBrush = new SolidBrush(Color.FromArgb(35, 39, 50)))
                {
                    g.FillPath(keyBrush, keyPath);
                }

                Rectangle baseEdgeRect = new Rectangle(7, 22, 18, 3);
                using (GraphicsPath baseEdgePath = CreateRoundedRect(baseEdgeRect, 1))
                using (Brush baseEdgeBrush = CreateSwatchBrush(baseEdgeRect, swatch, 235, spectrum))
                {
                    g.FillPath(baseEdgeBrush, baseEdgePath);
                }

                Rectangle dishRect = new Rectangle(8, 7, 16, 14);
                using (GraphicsPath dishPath = CreateRoundedRect(dishRect, 3))
                using (SolidBrush dishBrush = new SolidBrush(Color.FromArgb(26, 29, 37)))
                using (Pen dishPen = new Pen(Color.FromArgb(65, 255, 255, 255), 1f))
                {
                    g.FillPath(dishBrush, dishPath);
                    g.DrawPath(dishPen, dishPath);
                }

                Rectangle ledHaloRect = new Rectangle(9, 11, 14, 6);
                using (GraphicsPath ledHaloPath = CreateRoundedRect(ledHaloRect, 3))
                using (Brush ledHaloBrush = CreateSwatchBrush(ledHaloRect, swatch, 95, spectrum))
                {
                    g.FillPath(ledHaloBrush, ledHaloPath);
                }

                Rectangle ledRect = new Rectangle(10, 12, 12, 4);
                using (GraphicsPath ledPath = CreateRoundedRect(ledRect, 2))
                using (Brush ledBrush = CreateSwatchBrush(ledRect, swatch, 255, spectrum))
                {
                    g.FillPath(ledBrush, ledPath);
                }

                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (d < 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void ExitThreadCore()
        {
            Cleanup();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Cleanup();
            }
            base.Dispose(disposing);
        }

        private void Cleanup()
        {
            if (_disposed) return;
            _disposed = true;

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            _debounceTimer.Stop();
            _debounceTimer.Dispose();
            _fallbackTimer.Stop();
            _fallbackTimer.Dispose();

            _watcher.ReapplyRequested -= OnWatcherReapply;
            _watcher.TaskbarCreated -= OnTaskbarCreated;
            _watcher.Dispose();

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();

            if (_currentIcon != null)
            {
                _currentIcon.Dispose();
                _currentIcon = null;
            }
            if (_currentIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_currentIconHandle);
                _currentIconHandle = IntPtr.Zero;
            }
        }
    }

    internal static class Program
    {
        private static readonly object LogLock = new object();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        private static void TryEnablePerMonitorDpi()
        {
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch
            {
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            TryEnablePerMonitorDpi();

            bool oneShot = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].Trim().ToLowerInvariant();
                if (a == "--apply-once" || a == "--test")
                {
                    oneShot = true;
                }
            }

            if (oneShot)
            {
                AppSettings settings = AppSettings.LoadOrCreateDefault();
                ApplyResult res = RazerOrnataHid.Apply(settings);
                WriteStatusLog("CLI_APPLY", settings, res);
                return res.Success ? 0 : 1;
            }

            bool createdNew;
            using (Mutex singleInstance = new Mutex(true, @"Local\OrnataLight_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (TrayApp app = new TrayApp())
                {
                    Application.Run(app);
                }
                return 0;
            }
        }

        public static void WriteStatusLog(string trigger, AppSettings settings, ApplyResult res)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDir);
                string line = string.Format(
                    "{0:yyyy-MM-dd HH:mm:ss.fff} | Trigger={1} | Mode={2} | RGB=({3},{4},{5}) | Brightness={6} | Found={7} | PID=0x{8:X4} | Storage=0x{9:X2} | BrightnessStatus=0x{10:X2} | ModeStatus=0x{11:X2} | Result={12} | Path={13} | Error={14}",
                    DateTime.Now,
                    trigger,
                    AppSettings.ModeToString(settings.Mode),
                    settings.R,
                    settings.G,
                    settings.B,
                    settings.Brightness,
                    res.Found,
                    res.ProductId,
                    res.UsedStorage,
                    res.BrightnessStatus,
                    res.ModeStatus,
                    res.Success ? "OK" : "FAIL",
                    res.DevicePath,
                    res.ErrorMessage);

                lock (LogLock)
                {
                    string[] existing = File.Exists(AppSettings.LogPath)
                        ? File.ReadAllLines(AppSettings.LogPath, Encoding.UTF8)
                        : new string[0];

                    int keep = Math.Min(existing.Length, 49);
                    string[] next = new string[keep + 1];
                    if (keep > 0)
                    {
                        Array.Copy(existing, existing.Length - keep, next, 0, keep);
                    }
                    next[keep] = line;
                    File.WriteAllLines(AppSettings.LogPath, next, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
