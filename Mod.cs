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

namespace CustomTags_HeatSuit
{
    /// <summary>
    /// Your mod logic goes here.
    /// </summary>
    public class Mod : ModBase // <= Do not Remove.
    {
        /// <summary>
        /// Provides access to the mod loader API.
        /// </summary>
        private readonly IModLoader _modLoader;

        /// <summary>
        /// Provides access to the Reloaded.Hooks API.
        /// </summary>
        /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
        private readonly Reloaded.Hooks.Definitions.IReloadedHooks _hooks;

        /// <summary>
        /// Provides access to the Reloaded logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Entry point into the mod, instance that created this class.
        /// </summary>
        private readonly IMod _owner;

        /// <summary>
        /// Provides access to this mod's configuration.
        /// </summary>
        private Config _configuration;

        /// <summary>
        /// The configuration of the currently executing mod.
        /// </summary>
        private readonly IModConfig _modConfig;

        private Process _process;
        private IntPtr _allocatedMemory;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks!;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;
            _process = Process.GetCurrentProcess();

            string[] CustomTagNames = {
                "Heat_Protection", "Heat_Protect", "Thermal_Protection",
                "Thermal Protect", "Heat_Suit", "Heat_Resistance", "Heat_Resist"
            };
            List<int> CustomTagDataList = new List<int>();

            string? processPath = Environment.ProcessPath;
            if (processPath == null)
            {
                Console.WriteLine("ProcessPath is null. Exiting method.");
                return;
            }

            string CharsFolder = Path.GetFullPath(Path.Combine(processPath, "..", "CHARS"));
            Console.WriteLine(CharsFolder);
            string fileContents = File.ReadAllText(Path.Combine(CharsFolder, "CHARS.TXT"));
            Console.WriteLine(fileContents);

            string pattern = @"char_start\s*dir\s*""(?<dir>[^""]+)""\s*file\s*""(?<file>[^""]+)""\s*char_end";

            // Create a Regex object
            Regex regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // Match the pattern in the input string
            MatchCollection matches = regex.Matches(fileContents);

            // Iterate over each match
            foreach (Match match in matches)
            {
                // Extract the captured groups
                string dir = match.Groups["dir"].Value;
                string file = match.Groups["file"].Value;
                string fileNameWithExtension = file + ".txt";

                // Output the results
                Console.WriteLine($"Directory: {dir}, File: {file}");

                string fullPath = Path.Combine(CharsFolder, dir, fileNameWithExtension);
                Console.WriteLine($"Full Path: {fullPath}");

                if (File.Exists(fullPath))
                {
                    // Read the content of the file
                    string fileContent = File.ReadAllText(fullPath);

                    // Initialize a flag to check if any keyword is found
                    bool matchFound = false;

                    // Compare the file content with each entry in the weekDays array
                    foreach (string keyword in CustomTagNames)
                    {
                        if (fileContent.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            matchFound = true;
                            break; // Exit the loop on the first match
                        }
                    }

                    // Add 1 to the list if a match is found, otherwise add 0
                    CustomTagDataList.Add(matchFound ? 1 : 0);
                }
                else
                {
                    // If the file doesn't exist, you can decide how to handle it.
                    Console.WriteLine($"File {fileNameWithExtension} not found in directory {dir}.");
                    CustomTagDataList.Add(0); // You could add 0 if the file is missing, or handle it differently.
                }
            }

            // Output the results
            Console.WriteLine("Results:");
            foreach (int result in CustomTagDataList)
            {
                Console.WriteLine(result);
            }

            // Convert the List<int> to a byte array
            byte[] byteArray = CustomTagDataList.ConvertAll(b => (byte)b).ToArray();

            // Allocate memory and write the results to the game's memory
            AllocateMemoryAndWriteData(byteArray);
        }

        private void AllocateMemoryAndWriteData(byte[] data)
        {
            // Allocate memory
            _allocatedMemory = VirtualAlloc(IntPtr.Zero, (uint)data.Length, 0x1000 | 0x2000, 0x40);

            if (_allocatedMemory == IntPtr.Zero)
            {
                Console.WriteLine("Failed to allocate memory.");
                return;
            }

            Console.WriteLine($"Memory allocated at: 0x{_allocatedMemory.ToInt64():X}");

            // Write data to memory
            Marshal.Copy(data, 0, _allocatedMemory, data.Length);

            Console.WriteLine($"Data written to memory at address: 0x{_allocatedMemory.ToInt64():X}");

            string[] HeatSuitWalls =
            {
                $"use32",
                $"xor eax,eax",
                $"mov al,[ebp+0x15B0]",
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
                var hook = _hooks?.CreateAsmHook(HeatSuitWalls, (long)(mainModule.BaseAddress + 0x209E7), AsmHookBehaviour.DoNotExecuteOriginal);
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
                $"mov al,[esi+0x15B0]",
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
                var hook2 = _hooks?.CreateAsmHook(HeatSuitKill, (long)(mainModule.BaseAddress + 0x207AE), AsmHookBehaviour.DoNotExecuteOriginal);
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
                $"mov al,[ecx+0x15B0]",
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
                var hook3 = _hooks?.CreateAsmHook(HeatSuitLedgegrab, (long)(mainModule.BaseAddress + 0x1E0AC8), AsmHookBehaviour.DoNotExecuteOriginal);
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
                $"mov cl,[esi+0x15B0]",
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
                var hook4 = _hooks?.CreateAsmHook(HeatSuitLedgegrabDamage, (long)(mainModule.BaseAddress + 0x1E19BD), AsmHookBehaviour.DoNotExecuteOriginal);
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
                $"mov al,[esi+0x15B0]",
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
                var hook5 = _hooks?.CreateAsmHook(HeatSuitBuildables, (long)(mainModule.BaseAddress + 0x1CE3FC), AsmHookBehaviour.DoNotExecuteOriginal);
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
                $"mov cl,[ebx+0x15B0]",
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
                var hook6 = _hooks?.CreateAsmHook(HeatSuitBuildablesDamage, (long)(mainModule.BaseAddress + 0x1CDD81), AsmHookBehaviour.DoNotExecuteOriginal);
                hook6?.Activate(); // Activates the hook
            }
            else
            {
                _logger?.WriteLine($"[{_modConfig.ModId}] Failed to get MainModule. Cannot create hook.");
            }

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
