using System;
using System.IO;
using Microsoft.Win32;

namespace EyeCareNative
{
    public sealed class StartupState
    {
        public string Command;
        public byte[] Approval;
    }

    public static class StartupRegistration
    {
        public const string Name = "EyeCareBreakNative";
        const string Run = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string Approved = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";
        public static string CommandFor(string executable) { return "\"" + Path.GetFullPath(executable) + "\""; }
        public static StartupState Capture()
        {
            var value = new StartupState();
            using (var key = Registry.CurrentUser.OpenSubKey(Run)) if (key != null) value.Command = key.GetValue(Name) as string;
            using (var key = Registry.CurrentUser.OpenSubKey(Approved)) if (key != null) value.Approval = key.GetValue(Name) as byte[];
            return value;
        }
        public static bool IsEnabled(string executable)
        {
            var value = Capture();
            return String.Equals(value.Command, CommandFor(executable), StringComparison.OrdinalIgnoreCase) && (value.Approval == null || (value.Approval.Length >= 1 && (value.Approval[0] == 2 || value.Approval[0] == 6)));
        }
        public static void Restore(StartupState state)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Run)) { if (state.Command == null) key.DeleteValue(Name, false); else key.SetValue(Name, state.Command, RegistryValueKind.String); }
            using (var key = Registry.CurrentUser.CreateSubKey(Approved)) { if (state.Approval == null) key.DeleteValue(Name, false); else key.SetValue(Name, state.Approval, RegistryValueKind.Binary); }
        }
        public static void Set(bool enabled, string executable)
        {
            var before = Capture();
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(Run)) { if (enabled) key.SetValue(Name, CommandFor(executable), RegistryValueKind.String); else key.DeleteValue(Name, false); }
                if (enabled && before.Approval != null) using (var key = Registry.CurrentUser.CreateSubKey(Approved)) { var approval = new byte[12]; approval[0] = 2; key.SetValue(Name, approval, RegistryValueKind.Binary); }
                if (IsEnabled(executable) != enabled) throw new IOException("登录启动设置未能生效");
            }
            catch { Restore(before); throw; }
        }
    }
}
