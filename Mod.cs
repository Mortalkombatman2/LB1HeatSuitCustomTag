using CustomTags_HeatSuit.Configuration;
using CustomTags_HeatSuit.Template;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.Enums;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using System.IO;
using System.Collections.Generic;
using Reloaded.Mod.Interfaces.Internal;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Memory.Sigscan.Definitions.Structs;

namespace CustomTags_HeatSuit
{
    public class Mod : ModBase
    {
        private readonly IModLoader _modLoader;
        private readonly Reloaded.Hooks.Definitions.IReloadedHooks _hooks;
        private readonly ILogger _logger;
        private readonly IMod _owner;
        private Config _configuration;
        private readonly IModConfig _modConfig;
        private Process _process;
        private IntPtr _allocatedMemory;
        private string? _charsFilePath;

        private long _wallsOffset;
        private long _killOffset;
        private long _ledgegrabOffset;
        private long _ledgegrabDamageOffset;
        private long _buildablesOffset;
        private long _buildablesDamageOffset;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks!;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;
            _process = Process.GetCurrentProcess();

            // Subscribe to ModLoaded event
            _modLoader.ModLoaded += OnModLoaded;

            // Use another event to determine when all mods are loaded
            _modLoader.OnModLoaderInitialized += OnModLoaderFinishedLoading;

            // _logger.WriteLine("Starting scan");
            _modLoader.GetController<IStartupScanner>().TryGetTarget(out var startupScanner);
            startupScanner!.AddMainModuleScan("8B 85 ?? ?? ?? ?? 3B C3 74 ?? F6 40 14", OnWallsScan);
            startupScanner!.AddMainModuleScan("8B 86 ?? ?? ?? ?? 85 C0 74 ?? F6 40 14 ?? 74 ?? 85 ED", OnKillScan);
            startupScanner!.AddMainModuleScan("8B 81 ?? ?? ?? ?? 85 C0 0F 84 ?? ?? ?? ?? F6 40 14", OnLedgegrabScan);
            startupScanner!.AddMainModuleScan("8B 8E ?? ?? ?? ?? 85 C9 0F 84 ?? ?? ?? ?? F6 41 14", OnLedgegrabDamageScan);
            startupScanner!.AddMainModuleScan("8B 86 ?? ?? ?? ?? 85 C0 74 ?? F6 40 14 ?? 75", OnBuildablesScan);
            startupScanner!.AddMainModuleScan("8B 8B ?? ?? ?? ?? 85 C9 74 ?? F6 41 14", OnBuildablesDamageScan);
            // _logger.WriteLine("Ending scan");
        }

        private void OnModLoaded(IModV1 modInstance, IModConfigV1 modConfig)
        {
            if (_charsFilePath == null)
            {
                // Check if the mod has a chars.txt file
                var modPath = _modLoader.GetDirectoryForModId(modConfig.ModId);
                var charsPath = Path.Combine(modPath, "Redirector", "CHARS", "CHARS.TXT");
                // _logger?.WriteLine(modPath + "\n" + charsPath);

                if (File.Exists(charsPath))
                {
                    _charsFilePath = charsPath;
                    // _logger?.WriteLine("Found path");
                }
                else
                {
                    // _logger?.WriteLine("Path not found");
                }
            }
        }

        private void OnModLoaderFinishedLoading()
        {
            if (_charsFilePath == null)
            {
                // No mod-specific chars.txt found, use the default one
                string processPath = Environment.ProcessPath ?? string.Empty;
                _charsFilePath = Path.Combine(Path.GetFullPath(Path.Combine(processPath, "..", "CHARS")), "CHARS.TXT");
            }

            // _logger?.WriteLine(_charsFilePath);

            // Load data from _charsFilePath
            LoadDataFromCharsFile(_charsFilePath);
        }

        private void LoadDataFromCharsFile(string? charsFilePath)
        {
            // Ensure charsFilePath is not null
            if (string.IsNullOrEmpty(charsFilePath))
            {
                _logger?.WriteLine("charsFilePath is null or empty. Cannot load data.");
                return;
            }

            string[] CustomTagNames = {
                "Heat_Protection", "Heat_Protect", "Thermal_Protection",
                "Thermal_Protect", "Heat_Suit", "Heat_Resistance", "Heat_Resist",
                "Cannot_Be_Burnt", "Cannot_Be_Burned", "Cannot_Burn",
                "Cant_Burn","Fireproof"
            };
            List<int> CustomTagDataList = new List<int>();

            string fileContents = File.ReadAllText(charsFilePath);
            string pattern = @"char_start\s*dir\s*""(?<dir>[^""]+)""\s*file\s*""(?<file>[^""]+)""\s*char_end";

            Regex regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            MatchCollection matches = regex.Matches(fileContents);

            foreach (Match match in matches)
            {
                string dir = match.Groups["dir"].Value;
                string file = match.Groups["file"].Value;
                string fileNameWithExtension = file + ".txt";

                // Ensure that Path.GetDirectoryName(charsFilePath) does not return null
                string? directoryName = Path.GetDirectoryName(charsFilePath);
                if (string.IsNullOrEmpty(directoryName))
                {
                    _logger?.WriteLine("Directory name could not be determined.");
                    continue;
                }

                string fullPath = Path.Combine(directoryName, dir, fileNameWithExtension);

                if (File.Exists(fullPath))
                {
                    LoadAndCheckCustomTags(fullPath, CustomTagNames, CustomTagDataList);
                }
                else
                {
                    // _logger?.WriteLine($"Mod char text file not found: {fullPath}, checking default game folder...");
                    // Check the default game folder if the mod file isn't found
                    string gameFolderPath = Path.Combine(Path.GetFullPath(Path.Combine(Environment.ProcessPath ?? string.Empty, "..", "CHARS")), dir, fileNameWithExtension);

                    if (File.Exists(gameFolderPath))
                    {
                        LoadAndCheckCustomTags(gameFolderPath, CustomTagNames, CustomTagDataList);
                    }
                    else
                    {
                        // _logger?.WriteLine($"Default game char text file not found: {gameFolderPath}");
                        CustomTagDataList.Add(0);
                    }
                }
            }

            byte[] byteArray = CustomTagDataList.ConvertAll(b => (byte)b).ToArray();
            AllocateMemoryAndWriteData(byteArray);
        }

        private void LoadAndCheckCustomTags(string filePath, string[] customTagNames, List<int> customTagDataList)
        {
            string[] lines = File.ReadAllLines(filePath);
            bool matchFound = false;

            foreach (string line in lines)
            {
                // Rule 3: Check for comments
                string trimmedLine = line.TrimStart(); // Trim leading whitespace
                if (trimmedLine.StartsWith(";") || trimmedLine.StartsWith("//"))
                {
                    continue;
                }

                // Rule 1: Only one tag per line
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && Array.Exists(customTagNames, tag => tag.Equals(parts[1], StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (string tag in customTagNames)
                {
                    // Rule 2: No text before the tag
                    if (!line.TrimStart().StartsWith(tag, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Rule 4: No characters connected to the tag
                    if (Regex.IsMatch(line, @"\b" + Regex.Escape(tag) + @"\b", RegexOptions.IgnoreCase))
                    {
                        // Rule 5: Check for 'off' with surrounding whitespace or at the end of the line
                        if (Regex.IsMatch(line, $@"\b{tag}\b.*\boff\b(\s|$)", RegexOptions.IgnoreCase))
                        {
                            matchFound = false; // Disable the tag
                        }
                        else
                        {
                            matchFound = true; // Enable the tag
                        }
                        break; // Stop checking other tags on this line
                    }
                }
            }

            // After iterating through all lines, add the final result to the list
            customTagDataList.Add(matchFound ? 1 : 0);
        }

        private void AllocateMemoryAndWriteData(byte[] data)
        {
            // Allocate memory
            _allocatedMemory = VirtualAlloc(IntPtr.Zero, (uint)data.Length, 0x1000 | 0x2000, 0x40);

            if (_allocatedMemory == IntPtr.Zero)
            {
                //Console.WriteLine("Failed to allocate memory.");
                return;
            }

            //Console.WriteLine($"Memory allocated at: 0x{_allocatedMemory.ToInt64():X}");

            // Write data to memory
            Marshal.Copy(data, 0, _allocatedMemory, data.Length);

            //Console.WriteLine($"Data written to memory at address: 0x{_allocatedMemory.ToInt64():X}");

            string[] HeatSuitWalls =
            {
                $"use32",
                $"xor eax,eax",
                $"mov ax,[ebp+0x15B0]",
                $"cmp byte [0x{_allocatedMemory.ToInt64():X}+eax],0x1",
                $"jne originalcode1",
                $"mov eax,0x936658",
                $"jmp exit1",

                $"originalcode1:",
                $"mov eax,[ebp+0x1144]",

                $"exit1:",
                $"cmp eax,ebx"
            };
            var mainModule = Process.GetCurrentProcess().MainModule;
            if (mainModule != null)
            {
                var hook = _hooks?.CreateAsmHook(HeatSuitWalls, (long)(mainModule.BaseAddress + _wallsOffset), AsmHookBehaviour.DoNotExecuteOriginal);
                hook?.Activate(); // Activates the hook
            }
            else
            {
                _logger?.WriteLine($"[{_modConfig.ModId}] Failed to get MainModule. Cannot create hook.");
            }

            string[] HeatSuitKill =
            {
                $"use32",
                $"xor eax,eax",
                $"mov ax,[esi+0x15B0]",
                $"cmp byte [0x{_allocatedMemory.ToInt64():X}+eax],0x1",
                $"jne originalcode2",
                $"mov eax,0x936658",
                $"jmp exit2",

                $"originalcode2:",
                $"mov eax,[esi+0x1144]",

                $"exit2:",
                $"test eax,eax"
            };

            if (mainModule != null)
            {
                var hook2 = _hooks?.CreateAsmHook(HeatSuitKill, (long)(mainModule.BaseAddress + _killOffset), AsmHookBehaviour.DoNotExecuteOriginal);
                hook2?.Activate(); // Activates the hook
            }
            else
            {
                _logger?.WriteLine($"[{_modConfig.ModId}] Failed to get MainModule. Cannot create hook.");
            }

            string[] HeatSuitLedgegrab =
            {
                $"use32",
                $"xor eax,eax",
                $"mov ax,[ecx+0x15B0]",
                $"cmp byte [0x{_allocatedMemory.ToInt64():X}+eax],0x1",
                $"jne originalcode3",
                $"mov eax,0x936658",
                $"jmp exit3",

                $"originalcode3:",
                $"mov eax,[ecx+0x1144]",

                $"exit3:",
                $"test eax,eax"
            };
            if (mainModule != null)
            {
                var hook3 = _hooks?.CreateAsmHook(HeatSuitLedgegrab, (long)(mainModule.BaseAddress + _ledgegrabOffset), AsmHookBehaviour.DoNotExecuteOriginal);
                hook3?.Activate(); // Activates the hook
            }
            else
            {
                _logger?.WriteLine($"[{_modConfig.ModId}] Failed to get MainModule. Cannot create hook.");
            }

            string[] HeatSuitLedgegrabDamage =
            {
                $"use32",
                $"xor ecx,ecx",
                $"mov cx,[esi+0x15B0]",
                $"cmp byte [0x{_allocatedMemory.ToInt64():X}+ecx],0x1",
                $"jne originalcode4",
                $"mov ecx,0x936658",
                $"jmp exit4",

                $"originalcode4:",
                $"mov ecx,[esi+0x1144]",

                $"exit4:",
                $"test ecx,ecx"
            };
            if (mainModule != null)
            {
                var hook4 = _hooks?.CreateAsmHook(HeatSuitLedgegrabDamage, (long)(mainModule.BaseAddress + _ledgegrabDamageOffset), AsmHookBehaviour.DoNotExecuteOriginal);
                hook4?.Activate(); // Activates the hook
            }
            else
            {
                _logger?.WriteLine($"[{_modConfig.ModId}] Failed to get MainModule. Cannot create hook.");
            }

            string[] HeatSuitBuildables =
            {
                $"use32",
                $"xor eax,eax",
                $"mov ax,[esi+0x15B0]",
                $"cmp byte [0x{_allocatedMemory.ToInt64():X}+eax],0x1",
                $"jne originalcode5",
                $"mov eax,0x936658",
                $"jmp exit5",

                $"originalcode5:",
                $"mov eax,[esi+0x1144]",

                $"exit5:",
                $" test eax,eax"
            };
            if (mainModule != null)
            {
                var hook5 = _hooks?.CreateAsmHook(HeatSuitBuildables, (long)(mainModule.BaseAddress + _buildablesOffset), AsmHookBehaviour.DoNotExecuteOriginal);
                hook5?.Activate(); // Activates the hook
            }
            else
            {
                _logger?.WriteLine($"[{_modConfig.ModId}] Failed to get MainModule. Cannot create hook.");
            }

            string[] HeatSuitBuildablesDamage =
            {
                $"use32",
                $"xor ecx,ecx",
                $"mov cx,[ebx+0x15B0]",
                $"cmp byte [0x{_allocatedMemory.ToInt64():X}+ecx],0x1",
                $"jne originalcode6",
                $"mov ecx,0x936658",
                $"jmp exit6",

                $"originalcode6:",
                $"mov ecx,[ebx+0x1144]",

                $"exit6:",
                $" test ecx,ecx"
            };
            if (mainModule != null)
            {
                var hook6 = _hooks?.CreateAsmHook(HeatSuitBuildablesDamage, (long)(mainModule.BaseAddress + _buildablesDamageOffset), AsmHookBehaviour.DoNotExecuteOriginal);
                hook6?.Activate(); // Activates the hook
            }
            else
            {
                _logger?.WriteLine($"[{_modConfig.ModId}] Failed to get MainModule. Cannot create hook.");
            }

        }

        private void OnWallsScan(PatternScanResult result)
        {
            _wallsOffset = result.Offset;
            // _logger.WriteLine($"Found 'Walls' offset at: {result.Offset}");
        }

        private void OnKillScan(PatternScanResult result)
        {
            _killOffset = result.Offset;
            // _logger.WriteLine($"Found 'Kill' offset at: {result.Offset}");
        }

        private void OnLedgegrabScan(PatternScanResult result)
        {
            _ledgegrabOffset = result.Offset;
            // _logger.WriteLine($"Found 'Ledgrab' offset at: {result.Offset}");
        }

        private void OnLedgegrabDamageScan(PatternScanResult result)
        {
            _ledgegrabDamageOffset = result.Offset;
            // _logger.WriteLine($"Found 'Ledgegrab Damage' offset at: {result.Offset}");
        }

        private void OnBuildablesScan(PatternScanResult result)
        {
            _buildablesOffset = result.Offset;
            // _logger.WriteLine($"Found 'Buildables' offset at: {result.Offset}");
        }

        private void OnBuildablesDamageScan(PatternScanResult result)
        {
            _buildablesDamageOffset = result.Offset;
            // _logger.WriteLine($"Found 'Buildables Damage' offset at: {result.Offset}");
        }


        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            // Apply settings from configuration.
            _configuration = configuration;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}