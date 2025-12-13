# RenProfile
### Tool to Rename Profile Folder Path in Windows Without Creating New User Account
#### Rename Windows Profile Path - Registry Find and Replace All

(For usage info, see below the introduction.)

## Introduction to RenProfile:  Use Cases and Considerations 

RenProfile is a tool to rename the Windows profile folder path of your user account. Run from a separate administrator account. It recursively performs a `find and replace all` operation within the registry – a method I developed for this specific purpose – changing every instance of your old profile path to your new profile path. **Sounds dangerous, and it might be, but we've tested this successfully many times on Windows 10 and Windows 11, but it should work on Windows 7 and higher.**

Technically, there's no reason why this *should* cause problems, except in poorly written programs that don't properly check the user account path using relevant APIs (like `%userprofile%`) or programs that hard-code this path in settings files (which no program *should* ever do). While some developers might follow these bad practices, in my testing, this seems rare. (Windows Server environments, for example, offer built-in user profile migration.) In such cases, you might need to inform the affected program of the new path.

## How RenProfile Works (Internal Logic)

RenProfile operates through a carefully orchestrated sequence of steps to ensure safe profile renaming:

### 1. **Prerequisites and Safety Checks**
- **Administrator Privileges Required:** Must be run as Administrator from a **different** user account than the profile being renamed
- **Safe Mode Recommended:** The tool detects if Windows is running in Safe Mode. If not, it prompts for confirmation before proceeding. Safe Mode ensures minimal processes are locking files or registry keys.
- **Path Validation:** Both old and new paths are validated to ensure they exist (old) or don't exist (new), preventing accidental overwrites
- **Test Move:** Before touching the registry, RenProfile attempts a test rename of the profile folder to verify permissions and detect locks

### 2. **Registry Hive Loading**
- **SeRestorePrivilege:** The tool requests and enables the `SeRestorePrivilege` Windows privilege, which is required to load and unload registry hives (even for administrators)
- **NTUSER.DAT Loading:** The target user's `NTUSER.DAT` file (their user registry hive) is temporarily loaded under `HKEY_USERS` with a unique temporary name
- This allows the tool to modify the user's registry settings even when they're not logged in

### 3. **Recursive Registry Update**
RenProfile performs a comprehensive search-and-replace operation across the entire registry:

- **Hives Updated:** 
  - `HKEY_LOCAL_MACHINE` (HKLM) - system-wide settings
  - `HKEY_USERS` (HKU) - all user profiles including the loaded target profile
  - Note: HKCU is skipped because it belongs to the current user (the admin running the tool), not the target profile

- **Path Format Handling:**
  - **Long Path Format:** Standard paths like `C:\Users\OldName`
  - **Short (8.3) Path Format:** Legacy DOS paths like `C:\Users\OLDNAM~1`
  - Both formats are detected and replaced using regex pattern matching

- **Registry Data Types:**
  - `REG_SZ` (String values)
  - `REG_EXPAND_SZ` (Expandable strings with environment variables)
  - `REG_MULTI_SZ` (Multi-string arrays)
  
- **What Gets Updated:**
  - **Value Data:** The content/data stored in registry values
  - **Value Names:** The names of registry entries themselves (if they contain the path)
  - **Subkey Names:** Registry key names that contain paths (with special handling for URL-encoded paths using `%5C` instead of `\`)

- **Parallel Processing:** Registry keys are processed in parallel for improved performance, with proper error handling for locked or inaccessible keys

### 4. **Physical Directory Rename**
- After all registry updates complete successfully, the user hive is unloaded
- The physical profile folder is renamed from the old path to the new path using a standard directory move operation

### 5. **Error Logging**
- If a log file path is provided, all errors encountered during the process are written with timestamps
- Errors don't stop the process; the tool continues and reports the count of successful vs. failed operations

## Usage Info

RenProfile is a CLI application that must be run from a separate Administrator account in an elevated Command Prompt or PowerShell. It takes 2 required arguments and 1 optional argument.

**Basic Command:**
```
RenProfile C:\Users\OldUserPath C:\Users\NewDesiredUserPath [LogFilePath]
```

**Arguments:**
- **Argument 1 (Required):** Old profile path - Full absolute path to the existing profile folder (e.g., `C:\Users\OldUserName`)
- **Argument 2 (Required):** New profile path - Full absolute path for the renamed profile folder (e.g., `C:\Users\NewUserName`)
- **Argument 3 (Optional):** Log file path - Full path to where error logs should be written (e.g., `C:\IT\RenProfile.log`)

**Important Usage Notes:**
- **Paths must be absolute:** Relative paths like `..\Users\NewName` are not supported
- **Minimum path length:** Paths must be at least 4 characters long
- **Case-insensitive matching:** The find-and-replace operation is case-insensitive, so it will match paths regardless of capitalization
- **No quotes needed:** Unless your paths contain spaces, you don't need to wrap them in quotes (though it doesn't hurt)

**Help Command:**
```
RenProfile ?
```
This displays the syntax help and exits.

**Example with Error Logging:**
```
RenProfile C:\Users\JohnDoe C:\Users\John.Doe C:\Logs\ProfileRename.log
```

### Exit Codes
- **0:** Success - All operations completed successfully
- **1:** Failure - Missing parameters, invalid paths, insufficient permissions, or critical errors occurred

The tool will display the exit code and wait for a keypress before closing, giving you time to review the output.

**After Running RenProfile:**

Navigate to `C:\Users` and verify the user profile folder was renamed. If the folder wasn't renamed, it's likely due to NTFS permissions. In such cases, this is considered intended behavior - we will not change NTFS permissions for you due to the serious implications this could cause. You'll need to:

1. Manually rename the folder
2. Reboot the system
3. (Optional) Use the `move` command if you prefer a command-line approach

**Understanding the Output:**

During execution, RenProfile displays:
- **Current registry key being processed:** Shown as `={HKEY_PATH}=`
- **Updated values:** Shown with `Set:` or `Renamed:` prefix
- **Errors:** Displayed immediately with descriptive messages
- **Final statistics:**
  - `Processed:` Total registry entries examined
  - `Updated:` Successfully modified entries
  - `Failed:` Entries that couldn't be updated (check log for details)

**If RenProfile doesn't work or causes problems, follow these troubleshooting steps:**

## Troubleshooting Steps
#### If renaming the user profile folder path causes issues with some applications, or otherwise doesn't work as expected, go through these troubleshooting steps. If you need to reverse the changes, you can find steps to do this further below.
* **Double-Check Paths:** Carefully verify the old and new profile paths you entered. Even a small typo can cause issues. The paths should be the full, absolute paths (e.g., `C:\Users\OldUsername` and `C:\Users\NewUsername`), not relative paths.
* **Check for Open Files/Processes:** Before running RenProfile, close all programs running under the user account you're modifying, then reboot. Open files or running processes can lock registry keys and prevent changes. Use Task Manager to ensure no lingering processes are associated with the target user.
* **Run from Safe Mode:** Reboot into Safe Mode with Networking using `msconfig` (🠆 Boot 🠆 Boot options 🠆 Safe mode 🠆 Network). This minimizes locking programs and provides a cleaner environment for the operation.
* **Run RenProfile Again:** It won't harm the system to reboot and run it again to ensure that all registry paths have been successfully modified. Check the error count in the final statistics - if errors occurred, the log file will contain details.
* **Check Event Viewer:** Look in the Windows Event Viewer for any errors or warnings related to user profiles, applications, or the registry around the time you ran RenProfile. This can give clues about the cause of the problem.
* **Review the Log File:** If you specified a log file path, examine it for specific errors. Each error entry includes a timestamp and details about what failed.
* **Hidden Files/Folders:** Some profile data might be stored in hidden folders. Check `%AppData%` (Roaming AppData), `%LocalAppData%` (Local AppData), and `%ProgramData%` for config files related to the application
* **Long Paths:** Be mindful of long file paths. Windows has limitations on path lengths, so extremely long profile paths can cause issues. Might also be helpful to enable Long Path support in Windows to be sure this isn't causing you an issue.
* **Reinstall the Problematic Application:** If a specific application is misbehaving after renaming the profile, check their documentation, but we at Invise Labs and Invise Solutions, have had success with backing up the program's locations, such as it's folder in `%ProgramFiles%` (should be `C:\Program Files`), `%ProgramData%` (should be `C:\ProgramData`), `%LocalAppData%` (Local AppData), and `%AppData%` (Roaming AppData), then after the backup, check if the program has a backup or export option. Then reinstall the program. We've even had success with just using the relevant program's backup and restore method and then reinstalling the program, but you should backup the app's data locations to be on the safe side. Such steps would take you less than 30 min and if the user really wants their name changed or it's in the best interest that it is changed, such as for standardization, etc.
* **Open an Issue Report:** We've never had a case of RenProfile not working but if it doesn't, feel free to open a discussion or issue report with the relevant details. When doing so, you will need to provide a good detailed description of the problem, screenshot snippet of any errors, and any relevant logs. This would be worst case scenario, as we have yet to come across any situations where renaming the Windows user's profile path did not work.

**Important Considerations for User Profile Paths:**
* **NTFS Permissions:** In rare cases, incorrect NTFS permissions on the profile folder can cause problems. Check the permissions to ensure the user account has the necessary access rights. If permissions are suspected to be an issue, use a Take Ownership right-click registry file to create this option for you.
  * Check that your username has Full Control of the user folder. Go to `C:\Users` right-click on the user folder 🠆 Security 🠆 Edit 🠆 check `Replace all child object permission entries with inheritable permission entries from this object` and ensure that your username has full permissions, including all special permissions. Then click OK and OK. Windows should have these permissions set, but this will take Full Control of all files over again, which can ensure that any applications configuration data is owned by the user. You can also download a right-click Take Ownership registry modification and forcibly take ownership of the profile folder.
* **Special Characters:** Avoid using special characters in user profile names or paths. This can also lead to compatibility problems.

**Reversing the Changes**
* **Run RenProfile Again (Reversal):** The quickest way to undo changes is to run RenProfile again, specifying the *new* profile path as argument 1 (old path) and the *original* profile path as argument 2 (new path). This effectively reverses the initial operation.
  ```
  RenProfile C:\Users\NewUserName C:\Users\OldUserName
  ```
* **System Restore (If Available):** If you have a recent system restore point created *before* running RenProfile, restoring to that point can revert all system changes, including registry modifications. This is a more drastic step, but it can be effective.

## Technical Details

**System Requirements:**
- Windows 7 or higher (tested extensively on Windows 10 and Windows 11)
- .NET Framework 4.7 or higher
- Administrator privileges
- Access to a separate administrator account (not the profile being renamed)

**Registry Hives Modified:**
- `HKEY_LOCAL_MACHINE` (HKLM)
- `HKEY_USERS` (HKU)
- Target user's NTUSER.DAT (loaded temporarily)

**Windows Privileges Required:**
- `SeRestorePrivilege` - Required for loading and unloading registry hives

**Performance:**
- Uses parallel processing for registry iteration
- Typical execution time: 2-10 minutes depending on system size and registry complexity
- Real-time progress display shows current registry key being processed
