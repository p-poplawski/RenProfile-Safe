using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RenProfileConsole
{
    /// <summary>
    /// P/Invoke declarations for Windows API functions used in profile renaming operations.
    /// </summary>
    internal class NativeMethods
    {
        /// <summary>
        /// Native methods and structures for privilege management.
        /// </summary>
        internal class Privileges
        {
            // Flag to enable a privilege in the token
            internal const int SE_PRIVILEGE_ENABLED = 0x00000002;

            internal struct TOKEN_PRIVILEGES
            {
                public int PrivilegeCount;

                // Fixed-size array required for P/Invoke compatibility with Win32 TOKEN_PRIVILEGES structure
                // SizeConst=1 because we typically adjust one privilege at a time
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
                public LUID_AND_ATTRIBUTES[] Privileges;
            }

            // Pack=4 ensures struct layout matches Win32 API expectations (important for 32/64-bit compatibility)
            [StructLayout(LayoutKind.Sequential, Pack = 4)]
            internal struct LUID_AND_ATTRIBUTES
            {
                public LUID Luid;
                public UInt32 Attributes;
            }

            // LUID (Locally Unique Identifier) - 64-bit value split into low/high parts for marshaling
            internal struct LUID
            {
                public UInt32 LowPart;
                public Int32 HighPart;
            }

            // First overload: Use when you need to retrieve the previous privilege state
            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool AdjustTokenPrivileges(
                IntPtr hTok,
                [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
                ref TOKEN_PRIVILEGES NewState,
                UInt32 BufferLengthInBytes,
                ref TOKEN_PRIVILEGES PreviousState,
                out UInt32 ReturnLengthInBytes);

            // Second overload: Use when you don't need the previous state (pass IntPtr.Zero for last two params)
            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool AdjustTokenPrivileges(
                IntPtr hTok,
                [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
                ref TOKEN_PRIVILEGES NewState,
                UInt32 BufferLengthInBytes,
                IntPtr PreviousState,
                IntPtr ReturnLengthInBytes);

            // Converts a privilege name string (e.g., "SeRestorePrivilege") to its LUID representation
            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool LookupPrivilegeValue(
                string lpsystemname,
                string lpname,
                [MarshalAs(UnmanagedType.Struct)] ref LUID lpLuid);
        }

        /// <summary>
        /// Native methods for process and token management.
        /// </summary>
        internal class Process
        {
            [Flags]
            internal enum TokenAccess : uint
            {
                AdjustPrivileges = 0x0020
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern IntPtr GetCurrentProcess();

            [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool OpenProcessToken(
                IntPtr processHandle,
                uint desiredAccess,
                out IntPtr tokenHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr hObject);
        }

        /// <summary>
        /// Native methods for registry hive loading and unloading.
        /// </summary>
        internal class Registry
        {
            // Loads a registry hive file (e.g., NTUSER.DAT) into a temporary location under a registry key
            // Requires SeRestorePrivilege and SeBackupPrivilege to succeed
            [DllImport("advapi32.dll")]
            public static extern int RegLoadKey(IntPtr hkey, string lpSubKey, string lpFile);

            // Unloads a previously loaded registry hive - must have no open handles to the hive
            [DllImport("advapi32.dll")]
            public static extern int RegUnLoadKey(IntPtr hkey, string lpSubKey);

            // Predefined registry root key handle for HKEY_USERS
            // unchecked cast needed because 0x80000003 is larger than int.MaxValue when signed
            internal static readonly IntPtr HKEY_USERS = new IntPtr(unchecked((int)0x80000003));
        }

        /// <summary>
        /// Native methods for file system operations.
        /// </summary>
        internal class FileSystem
        {
            // Converts long path names to 8.3 format short path names
            // Returns the length of the short path, or 0 on failure
            // Useful for working with paths that may contain spaces or special characters
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern int GetShortPathName(
                String pathName,
                StringBuilder shortName,
                int cbShortName);
        }
    }
}
