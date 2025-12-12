using Microsoft.Win32;
using System;
using System.Threading.Tasks;

namespace RenProfileConsole
{
    /// <summary>
    /// Extension methods for registry manipulation operations including iteration, renaming, and hive management.
    /// </summary>
    public static class RegistryUtils
    {
        /// <summary>
        /// Recursively iterates through all subkeys of a registry key and executes an action on each.
        /// Uses parallel processing for improved performance.
        /// </summary>
        /// <param name="root">The root registry key to iterate from</param>
        /// <param name="action">The action to execute on each registry key</param>
        public static void IterateKeys(this RegistryKey root, Action<RegistryKey> action)
        {
            if (root == null)
            {
                return;
            }

            // Process subkeys in parallel for better performance on large registry trees
            // Each subkey and its descendants are processed independently
            Parallel.ForEach(root.GetSubKeyNames(), keyname =>
            {
                try
                {
                    // Open with write access (true) so the action can modify the key
                    // Using statement ensures the key is closed even if an exception occurs
                    using (RegistryKey key = root.OpenSubKey(keyname, true))
                    {
                        // Recursively process all descendants first (depth-first traversal)
                        key.IterateKeys(action);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to open subkey '{keyname}' under '{root.Name}': {ex.Message}");

                    if (Program.logErrs)
                    {
                        Program.LogError($"Failed to open subkey '{keyname}' under '{root.Name}'\n{ex}");
                    }
                }
            });

            // Execute the action on this key AFTER processing all children
            // This ensures we handle leaf nodes first, which is safer when renaming/deleting
            action(root);
        }

        /// <summary>
        /// Renames a registry value by copying it to a new name and deleting the old one.
        /// </summary>
        /// <param name="key">The registry key containing the value</param>
        /// <param name="oldName">The current name of the value</param>
        /// <param name="newName">The new name for the value</param>
        public static void RenameValue(this RegistryKey key, string oldName, string newName)
        {
            // Windows registry API doesn't have a direct rename operation
            // so we copy the value (preserving its type) then delete the original
            key.SetValue(newName, key.GetValue(oldName), key.GetValueKind(oldName));
            key.DeleteValue(oldName);
        }

        /// <summary>
        /// Renames a registry subkey by cloning it to a new name and deleting the original.
        /// </summary>
        /// <param name="root">The parent registry key</param>
        /// <param name="oldName">The current name of the subkey</param>
        /// <param name="newName">The new name for the subkey</param>
        public static void RenameSubKey(this RegistryKey root, string oldName, string newName)
        {
            // Clone the entire subkey tree to the new location
            using (RegistryKey subKey = root.OpenSubKey(oldName))
            {
                CloneSubKey(root, subKey, newName);
            }

            // Delete the old subkey tree after successful cloning
            root.DeleteSubKeyTree(oldName);
        }

        /// <summary>
        /// Recursively clones a registry subkey and all its values and nested subkeys to a new location.
        /// </summary>
        /// <param name="root">The parent key where the clone will be created</param>
        /// <param name="source">The source subkey to clone</param>
        /// <param name="newName">The name for the cloned subkey</param>
        public static void CloneSubKey(RegistryKey root, RegistryKey source, string newName)
        {
            using (RegistryKey target = root.CreateSubKey(newName))
            {
                // Copy all values from source to target, preserving their types
                foreach (string name in source.GetValueNames())
                {
                    target.SetValue(name, source.GetValue(name), source.GetValueKind(name));
                }

                // Recursively clone all subkeys
                foreach (string name in source.GetSubKeyNames())
                {
                    using (RegistryKey subKey = source.OpenSubKey(name))
                    {
                        CloneSubKey(target, subKey, name);
                    }
                }
            }
        }

        /// <summary>
        /// Load a registry hive file under HKEY_USERS
        /// </summary>
        /// <param name="hiveFilePath">The path to the registry hive file to load (typically NTUSER.DAT)</param>
        /// <param name="subKeyName">The name of the subkey of HKEY_USERS to load the hive under</param>
        /// <returns>0 on success, an error code on failure as defined by RegLoadKey in the Windows APIs</returns>
        public static int LoadUserHive(string hiveFilePath, string subKeyName)
        {
            return NativeMethods.Registry.RegLoadKey(NativeMethods.Registry.HKEY_USERS, subKeyName, hiveFilePath);
        }

        /// <summary>
        /// Unload a registry hive file under HKEY_USERS
        /// </summary>
        /// <param name="subKeyName">The name of the subkey of HKEY_USERS to unload</param>
        /// <returns>0 on success, an error code on failure as defined by RegUnLoadKey in the Windows APIs</returns>
        public static int UnloadUserHive(string subKeyName)
        {
            return NativeMethods.Registry.RegUnLoadKey(NativeMethods.Registry.HKEY_USERS, subKeyName);
        }
    }
}
