using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RenProfileConsole
{
    /// <summary>
    /// A command line tool to rename a user profile by updating all references to the profile folder in the registry
    /// and renaming the folder.
    /// </summary>
    internal class Program
    {
        private static bool logErrs = false;
        private static bool checkOnly = false;
        private static int errors = 0;
        private static int successful = 0;
        private static int processed = 0;

        private static string oldDir = "";
        private static string newDir = "";
        private static string errPath = "";

        /// <summary>
        /// Entry point for the console application. Validates parameters and initiates profile renaming.
        /// </summary>
        /// <param name="args">Command line arguments: oldProfilePath newProfilePath [errorLogPath]</param>
        private static void Main(string[] args)
        {
            if (args.Length == 3 &&
                string.Equals(args[2], "--check", StringComparison.OrdinalIgnoreCase))
            {
                checkOnly = true;
                args = new[] { args[0], args[1] };
            }
            // Check Safe Mode and prompt if not
            try
            {
                if (!NativeMethods.User32.IsSafeMode())
                {
                    Console.WriteLine("Warning: It is recommended to run this tool in Windows Safe Mode as an administrator (not the owner of the source profile).");
                    Console.Write("Continue? (y/N) ");
                    // Single keypress: 'y' or 'Y' continues; anything else treated as 'N'
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                    Console.WriteLine();

                    // If the key is not 'Y' (covers Enter and any other key), exit with code 1
                    if (key.Key != ConsoleKey.Y)
                    {
                        ExitConsole(1);
                    }
                }
            }
            catch (Exception ex)
            {
                // If detection fails, proceed but log
                LogError($"Safe Mode detection failed. Proceeding.\n{ex}");
            }

            // Validate command line arguments
            try
            {
                if (args.Length == 0)
                {
                    Console.WriteLine("Missing all parameters.");
                    ExitConsole(1);
                }
                else if (args.Length == 1)
                {
                    if (args[0] == "?")
                    {
                        Console.WriteLine(@"Syntax: RenProfile.exe C:\Full\Path\To\OldProfile C:\Full\Path\To\NewProfile C:\Optional\Path\To\Log\Errors.txt");
                        ExitConsole();
                    }
                    else
                    {
                        Console.WriteLine("Missing 1 parameter.");
                        ExitConsole(1);
                    }
                }
                else if (args.Length == 3)
                {
                    errPath = args[2];

                    // Validate error log path: must be long enough, contain no invalid chars,
                    // and its parent directory must exist (file itself doesn't need to exist yet)
                    if (!String.IsNullOrWhiteSpace(errPath)
                        && errPath.Length > 4
                        && errPath.IndexOfAny(Path.GetInvalidPathChars()) == -1
                        && Directory.Exists(Path.GetDirectoryName(errPath)))
                    {
                        logErrs = true;
                    }
                    else
                    {
                        Console.WriteLine("Error log was specified but path is invalid.");
                        ExitConsole(1);
                    }
                }
                else if (args.Length > 3)
                {
                    Console.WriteLine("Missing 1 parameter.");
                    ExitConsole(1);
                }

                oldDir = args[0];
                newDir = args[1];
                if (String.IsNullOrWhiteSpace(oldDir) || oldDir.Length < 4)
                {
                    Console.WriteLine("Old user profile path is invalid.");
                    ExitConsole(1);
                }
                else if (String.IsNullOrWhiteSpace(newDir) || newDir.Length < 4)
                {
                    Console.WriteLine("New user profile path is invalid.");
                    ExitConsole(1);
                }
                else if (!Directory.Exists(oldDir))
                {
                    Console.WriteLine("Old user profile path does not exist.");
                    ExitConsole(1);
                }
                else if (Directory.Exists(newDir))
                {
                    Console.WriteLine("New user profile path already exists. Use another name.");
                    ExitConsole(1);
                }
            }
            catch (Exception ex)
            {
                LogError(ex.ToString());
                Console.WriteLine("Unexpected error occurred, cannot continue. Check error log.");

                ExitConsole(1);
            }

            PerformProfileRename();
        }

        /// <summary>
        /// Performs the profile renaming operation by loading the user registry hive,
        /// updating all references in HKLM and HKU, and renaming the physical directory.
        /// </summary>
        public static void PerformProfileRename()
        {
            string sourceSid = null;
            string sourcePath = Path.GetFullPath(oldDir).TrimEnd('\\');

            using (RegistryKey profiles = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
        {
            if (profiles != null)
            {
                foreach (string sid in profiles.GetSubKeyNames())
                {
                    using (RegistryKey profile = profiles.OpenSubKey(sid))
                    {
                        if (profile == null) continue;

                        string path = profile.GetValue(
                            "ProfileImagePath", null,
                            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

                        if (string.IsNullOrWhiteSpace(path)) continue;

                        string fullPath = Path.GetFullPath(
                            Environment.ExpandEnvironmentVariables(path)
                            ).TrimEnd('\\');

                        if (string.Equals(fullPath, sourcePath,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            sourceSid = sid;
                            break;
                        }
                    }
                }
            }
        }

        if (sourceSid == null)
        {
            Console.WriteLine("ERROR: Source is not a registered Windows profile.");
            ExitConsole(1);
        }

        string currentSid =
            System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;

        if (string.Equals(sourceSid, currentSid,
            StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("ERROR: Cannot rename the current user's profile.");
            ExitConsole(1);
        }

        using (RegistryKey loadedProfile = Registry.Users.OpenSubKey(sourceSid))
        {
            if (loadedProfile != null)
            {
                Console.WriteLine("ERROR: Source profile is currently loaded.");
                ExitConsole(1);
            }
        }
            if (checkOnly)
                {
                    Console.WriteLine("PROFILE CHECK PASSED");
                    Console.WriteLine($"Source: {oldDir}");
                    Console.WriteLine($"Target: {newDir}");
                    Console.WriteLine($"Source SID: {sourceSid}");
                    Console.WriteLine("No changes have been made.");

                    ExitConsole(0);
                return;
                }
            // Ensure we can rename the folder before touching the registry
            // Validate source and destination without moving files
            string sourceFull = Path.GetFullPath(oldDir).TrimEnd('\\');
            string targetFull = Path.GetFullPath(newDir).TrimEnd('\\');

            string sourceParent = Path.GetDirectoryName(sourceFull);
            string targetParent = Path.GetDirectoryName(targetFull);

            if (string.IsNullOrWhiteSpace(sourceParent) ||
                !string.Equals(sourceParent, targetParent,
                    StringComparison.OrdinalIgnoreCase))
                {
                Console.WriteLine(
                    "ERROR: Source and destination must be in the same parent directory.");
                ExitConsole(1);
                }

            if (Directory.Exists(targetFull) || File.Exists(targetFull))
            {
                Console.WriteLine("ERROR: Destination already exists.");
                ExitConsole(1);
            }

            Console.WriteLine("Profile directory paths validated.");

            // Create a temporary unique name to mount the target user's registry hive
            string tempUserHiveName = "__tmpRenProfile_" + Guid.NewGuid().ToString("N");

            // SeRestorePrivilege is required to load/unload registry hives, even when running as administrator
            if (Privileges.EnablePrivileges("SeRestorePrivilege"))
            {
                // Load the user's NTUSER.DAT file into the registry temporarily
                int loadCode = RegistryUtils.LoadUserHive(
                    $"{oldDir}\\NTUSER.dat", tempUserHiveName);
                
                if (loadCode != 0)
                {
                    Console.WriteLine(
                        $"ERROR: Cannot load user registry hive. Error code: {loadCode}");
                    ExitConsole(1);
                }

                // Keep the hive loaded while we update references
                using (RegistryKey userHive = Registry.Users.OpenSubKey(tempUserHiveName, true))
                {
                    if (userHive == null)
                    {
                        Console.WriteLine("ERROR: Cannot access target user registry hive.");
                        RegistryUtils.UnloadUserHive(tempUserHiveName);
                        ExitConsole(1);
                    }
                    // Update HKLM and HKU only; HKCU is for the current user (not the target profile),
                    // and other hives (HKCR, HKCC) are symlinks to subtrees of these two
                    Console.WriteLine("==========Updating HKLM==========");
                    try
                    {
                        Registry.LocalMachine.IterateKeys((key) => UpdateKey(key, oldDir, newDir));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ERROR: Failed to update HKLM registry.");
                        LogError($"HKLM update failed: {ex}");
                        Interlocked.Increment(ref errors);
                    }
                    Console.WriteLine("==========Updating HKU==========");
                    try
                    {
                        userHive.IterateKeys((key) => UpdateKey(key, oldDir, newDir));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ERROR: Failed to update HKU registry.");
                        LogError($"HKU update failed: {ex}");
                        Interlocked.Increment(ref errors);
                    }
                    Console.WriteLine($"Processed: {processed}\nUpdated : {successful}\nFailed   : {errors}");
                }

                // Unload the hive before attempting to rename the directory
                int unloadCode = RegistryUtils.UnloadUserHive(tempUserHiveName);

                if (unloadCode != 0)
                    {
                        Console.WriteLine(
                            $"ERROR: Cannot unload user registry hive. Error code: {unloadCode}");

                        LogError(
                            $"Failed to unload temporary registry hive {tempUserHiveName}. Error: {unloadCode}");

                ExitConsole(1);
                }
                if (errors > 0)
                    {
                        Console.WriteLine(
                            $"ERROR: {errors} registry operations failed. Directory rename cancelled.");

                        LogError(
                            $"Migration stopped because {errors} registry operations failed.");

                        ExitConsole(1);
                    }
                // Rename the actual profile directory on disk
                try
                {
                    Directory.Move(oldDir, newDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error renaming physical profile directories, another process might have a lock still.");
                    Console.WriteLine($"Error details: {ex.Message}");
                    LogError($"Final move failed when renaming '{oldDir}' to '{newDir}'.\n{ex}");
                    ExitConsole(1);
                }

                ExitConsole();
            }
            else
            {
                Console.WriteLine("Load key privilege not granted.");
                ExitConsole(1);
            }
        }

        /// <summary>
        /// Displays the exit code and waits for user input before terminating the application.
        /// </summary>
        /// <param name="exitCode">Code to exit with. Default: 0 (Success)</param>
        private static void ExitConsole(int exitCode = 0)
        {
            Console.WriteLine();
            Console.WriteLine("Exit code is " + exitCode + " (0=success)");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            Environment.Exit(exitCode);
        }

        /// <summary>
        /// Writes an error message to the error log file if logging is enabled.
        /// </summary>
        /// <param name="errorMessage">The error message to log</param>
        internal static void LogError(string errorMessage)
        {
            if (!logErrs)
            {
                return;
            }

            try
            {
                using (FileStream file = File.Exists(errPath) ? File.Open(errPath, FileMode.Append) : File.Open(errPath, FileMode.CreateNew))
                using (StreamWriter sw = new StreamWriter(file))
                {
                    sw.WriteLine($"{DateTime.Now.ToString()}: {errorMessage} \n===========");
                }
            }
            catch
            {
                /* Silently fail if unable to write to error log */
            }
        }

        /// <summary>
        /// Updates all values and subkeys in a registry key that contain the old directory path,
        /// replacing them with the new directory path. Handles both standard and short (8.3) path formats.
        /// </summary>
        /// <param name="key">The registry key to update</param>
        /// <param name="oldDir">The old profile directory path</param>
        /// <param name="newDir">The new profile directory path</param>
        private static void UpdateKey(RegistryKey key, string oldDir, string newDir)
        {
            try
            {
                // Get the 8.3 short path format (e.g., "C:\Users\JOHNDO~1") because some registry
                // entries may store paths in this format for backwards compatibility

                if (string.IsNullOrWhiteSpace(oldDir))
                    throw new ArgumentException("Source profile path is empty.");

                StringBuilder sb = new StringBuilder(300);
                int n = NativeMethods.FileSystem.GetShortPathName(
                    oldDir, sb, sb.Capacity);

                string shortOldDir = n > 0 && n < sb.Capacity
                    ? sb.ToString()
                    : string.Empty;

                string pathAlternatives = Regex.Escape(oldDir);

                if (!string.IsNullOrWhiteSpace(shortOldDir))
                {
                    pathAlternatives += "|" + Regex.Escape(shortOldDir);
                }

                string pattern = "(?:" + pathAlternatives + @")(?=\\|/|""|$)";

                string[] names = key.GetValueNames();
                Console.WriteLine($"={{{key.ToString()}}}=");
                foreach (string name in names)
                {
                    if (String.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }
                    Interlocked.Increment(ref processed);

                    try
                    {
                        // Handle REG_MULTI_SZ (array of strings) - some registry values store multiple paths
                        if (key.GetValueKind(name) == RegistryValueKind.MultiString)
                        {
                            string[] oldValues = (string[])key.GetValue(name);
                            string[] newValues = oldValues.Select(value => Regex.Replace(value, pattern, newDir, RegexOptions.IgnoreCase)).ToArray();

                            // Only update if the replacement actually changed something
                            if (!newValues.SequenceEqual(oldValues))
                            {
                                try
                                {
                                    key.SetValue(name, newValues, RegistryValueKind.MultiString);
                                    Console.WriteLine(String.Join(":", newValues));
                                    Interlocked.Increment(ref successful);
                                }
                                catch (Exception ex)
                                {
                                    LogError($"Error accessing/writing {name}\n{ex.ToString()}");
                                    Console.WriteLine($"Error setting: {newValues}");
                                    Interlocked.Increment(ref errors);
                                }
                            }
                        }
                        // Handle REG_SZ and REG_EXPAND_SZ (regular and expandable strings)
                        // ExpandString is used for paths with environment variables like %USERPROFILE%
                        else if (key.GetValueKind(name) == RegistryValueKind.String || key.GetValueKind(name) == RegistryValueKind.ExpandString)
                        {
                            string oldValue = (string)key.GetValue(
                                name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            string newValue = Regex.Replace(oldValue, pattern, newDir, RegexOptions.IgnoreCase);

                            if (oldValue != newValue)
                            {
                                try
                                {
                                    key.SetValue(name, newValue, key.GetValueKind(name));
                                    Console.WriteLine($"Set: {newValue}");
                                    Interlocked.Increment(ref successful);
                                }
                                catch (Exception ex)
                                {
                                    LogError($"Error accessing/writing {name}\n{ex.ToString()}");
                                    Console.WriteLine($"Error setting: {newValue}");
                                    Interlocked.Increment(ref errors);
                                }
                            }
                        }

                        // Check if the value name itself contains the old path and needs renaming
                        // (not just the value data, but the name of the registry entry)
                        string newValueName = Regex.Replace(name, pattern, newDir, RegexOptions.IgnoreCase);

                        if (newValueName != name)
                        {
                            try
                            {
                                key.RenameValue(name, newValueName);
                                Console.WriteLine($"Renamed: {newValueName}");
                                Interlocked.Increment(ref successful);
                            }
                            catch
                            {
                                Console.WriteLine($"Error renaming: {newValueName}");
                                Interlocked.Increment(ref errors);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error accessing/writing {name}\n{ex.ToString()}");
                        Console.WriteLine($"Error accessing: {name}");
                        Interlocked.Increment(ref errors);
                    }
                }

                // Registry subkey names can't contain backslashes, but paths are sometimes URL-encoded
                // in key names, so we need to match %5C (the URL encoding for backslash) instead of \
                string keyAlternatives = Regex.Escape(
                    oldDir.Replace(@"\", "%5C"));

                if (!string.IsNullOrWhiteSpace(shortOldDir))
                {
                    keyAlternatives += "|" + Regex.Escape(
                        shortOldDir.Replace(@"\", "%5C"));
                }

                string keypattern = "(?:" + keyAlternatives + @")(?=%5C|%2F|$)";
                string keyNewDir = newDir.Replace(@"\", @"%5C");
                foreach (string keyName in key.GetSubKeyNames())
                {
                    if (String.IsNullOrWhiteSpace(keyName))
                    {
                        continue;
                    }
                    Interlocked.Increment(ref processed);

                    string newKeyName = Regex.Replace(keyName, keypattern, keyNewDir, RegexOptions.IgnoreCase);
                    if (keyName != newKeyName)
                    {
                        try
                        {
                            key.RenameSubKey(keyName, newKeyName);
                            Console.WriteLine($"Updated: {newKeyName}");
                            Interlocked.Increment(ref successful);
                        }
                        catch (Exception ex)
                        {
                            LogError($"Error accessing/writing {keyName}\n{ex.ToString()}");
                            Console.WriteLine($"Error updating: {newKeyName}");
                            Interlocked.Increment(ref errors);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error accessing/writing {key.ToString()}\n{ex.ToString()}");
                Interlocked.Increment(ref errors);
            }
        }
    }
}
