using System;

namespace RenProfileConsole
{
    /// <summary>
    /// Provides functionality to request and enable Windows privileges for the current process.
    /// </summary>
    public class Privileges
    {
        /// <summary>
        /// Enables a specific privilege for the current process.
        /// </summary>
        /// <param name="privilege">The privilege name as defined in Windows headers (e.g., "SeRestorePrivilege")</param>
        /// <returns>True if the privilege was successfully enabled; otherwise, false</returns>
        public static bool EnablePrivileges(string privilege)
        {
            // First, convert the privilege name string to a LUID (Locally Unique Identifier)
            NativeMethods.Privileges.LUID luid = new NativeMethods.Privileges.LUID();

            if (!NativeMethods.Privileges.LookupPrivilegeValue(null, privilege, ref luid))
            {
                return false;
            }

            // Build the TOKEN_PRIVILEGES structure to tell Windows which privilege to enable
            NativeMethods.Privileges.TOKEN_PRIVILEGES tokenPrivileges = new NativeMethods.Privileges.TOKEN_PRIVILEGES();

            tokenPrivileges.PrivilegeCount = 1;
            tokenPrivileges.Privileges = new NativeMethods.Privileges.LUID_AND_ATTRIBUTES[1];
            tokenPrivileges.Privileges[0].Attributes = NativeMethods.Privileges.SE_PRIVILEGE_ENABLED;
            tokenPrivileges.Privileges[0].Luid = luid;

            // Get a handle to the current process
            IntPtr hProc = NativeMethods.Process.GetCurrentProcess();

            if (hProc == IntPtr.Zero)
            {
                return false;
            }

            // Open the process token with permission to adjust privileges
            if (!NativeMethods.Process.OpenProcessToken(
                    hProc,
                    (uint)NativeMethods.Process.TokenAccess.AdjustPrivileges,
                    out IntPtr hToken))
            {
                return false;
            }

            try
            {
                // Actually enable the privilege on the token
                // Using the simpler overload since we don't need to retrieve the previous state
                return NativeMethods.Privileges.AdjustTokenPrivileges(
                    hToken,
                    false,
                    ref tokenPrivileges,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            finally
            {
                // Always close the token handle to avoid leaks, even if AdjustTokenPrivileges fails
                NativeMethods.Process.CloseHandle(hToken);
            }
        }
    }
}
