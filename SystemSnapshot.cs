// Snaptool is a simple CLI tool for Linux that snapshots your processes, RAM, CPU temps and CPU usage.
// Available on GitHub at https://github.com/c3rt1fiedd/snaptool
// Available on the AUR (Arch User Repository) at https://aur.archlinux.org/packages/snaptool/
// If you want to install it on something other than Arch, idfk man do it yourself compile it fuck off
using System.Diagnostics;
using System.Text.Json;

// Declare these at the VERY TOP so every block can see them
// That is crucial.
string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "snapshotter");
string filePath = Path.Combine(dataDir, "history.jsonl");

// Handle the arguments
try 
{
    if (args.Length == 0 || args[0] == "save")
    {
        // Define the snapshot INSIDE here because we only gather data on 'save'
        var snapshot = new SystemSnapshot {
            CpuUsage = GetCpuUsage(),
            CpuTemp = GetCpuTemp(),
            AvailableMemoryMB = GetMeminfoValue("MemAvailable"),
            TotalMemoryMB = GetMeminfoValue("MemTotal"),
            TopProcesses = Process.GetProcesses()
                .OrderByDescending(p => p.WorkingSet64)
                .Take(5)
                .Select(p => p.ProcessName)
                .ToList()
        };

        Directory.CreateDirectory(dataDir);
        string singleLineJson = JsonSerializer.Serialize(snapshot); 
        File.AppendAllText(filePath, singleLineJson + Environment.NewLine);
        
        Console.WriteLine($"Snapshot saved to {filePath}");
    }
    else if (args[0] == "diff")
    {
        PerformDiff(filePath);
    }
    else if (args[0] == "clear")
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            Console.WriteLine("\u001b[32m[✓]\u001b[0m History cleared successfully!");
        }
    }
    else if (args[0] == "-h" || args[0] == "--help" || args[0] == "help")
    {
        PrintHelp();
    }
    else 
    {
        Console.WriteLine("Usage: snapshotter [save|diff]");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

// --- HELPER METHODS ---
static double GetCpuUsage() {
    // Read the file, split by space, take the first number (1-min load avg)
    string content = File.ReadAllText("/proc/loadavg");
    string firstPart = content.Split(' ')[0]; 
    return double.Parse(firstPart);
}

static double GetCpuTemp() // what a fucking NEST this is bro holy shit
{
    try
    {
        // Iterate through HW monitor directories
        foreach (var dir in Directory.GetDirectories("/sys/class/hwmon/"))
        {
            string name = File.ReadAllText(Path.Combine(dir, "name")).Trim();

            // coretemp is for Intel, k10temp is AMD
            if (name == "coretemp" || name == "k10temp")
            {
                // temp1_input is usually the package temparature
                string tempRaw = File.ReadAllText(Path.Combine(dir, "temp1_input")).Trim();
                return double.Parse(tempRaw) / 1000.0;
            }
        }
    } catch
    {
        // Fallback if we can't read a sensor (common in VMs or fucked ass kernels)
        // That's your fault for wanting to be different you slimy bitch, be normal
        // like the rest of us
        return -1;
    }
    return -1;
} // seriously though I'll add more support for weird ass boards later

// Whatever the fuck this is
// FUCK! I CAN'T READ ANY OF THIS SHIT
static void PerformDiff(string filePath)
{
    if (!File.Exists(filePath)) {
        Console.WriteLine("\u001b[31m[!]\u001b[0m No snapshots found! Run 'save' at least twice first.");
        return;
    }

    var lines = File.ReadAllLines(filePath);
    if (lines.Length < 2) {
        Console.WriteLine("\u001b[31m[!]\u001b[0m Run 'save' at least twice first.");
        return;
    }

    var last = JsonSerializer.Deserialize<SystemSnapshot>(lines[^1]);
    var prev = JsonSerializer.Deserialize<SystemSnapshot>(lines[^2]);
// Calculate percentages SAFELY this time. It fucking threw out
// -∞% and NaN% when testing
// HOW THE FUCK IS NEGATIVE INFINITY EVEN POSSIBLE
// I'm tweaking THE DEMONS THE DEMONS THEY'RE AFTER ME

// We truly live in a society.
double lastUsedPct = last.TotalMemoryMB > 0 
    ? 100 - ((double)last.AvailableMemoryMB / last.TotalMemoryMB * 100) 
    : 0;

double prevUsedPct = prev.TotalMemoryMB > 0 
    ? 100 - ((double)prev.AvailableMemoryMB / prev.TotalMemoryMB * 100) 
    : 0;

    // Helper for colors: Green for down (good), Red for up (hot/heavy)
    string Colorize(double diff, bool inverse = false) {
        bool isBad = inverse ? diff < 0 : diff > 0; 
        string color = isBad ? "\u001b[31m" : "\u001b[32m"; // Red vs Green
        return $"{color}{diff:+#.##;-#.##;0}\u001b[0m";
    }

    Console.WriteLine("\n\u001b[1m--- System Shift ---\u001b[0m");
    Console.WriteLine($"{"Metric",-10} | {"Current",-10} | {"Change",-10}");
    Console.WriteLine(new string('-', 35));
    
    Console.WriteLine($"{"Temp",-10} | {last.CpuTemp + "°C",-10} | {Colorize(last.CpuTemp - prev.CpuTemp)}°C");
    Console.WriteLine($"{"CPU",-10} | {last.CpuUsage + "%",-10} | {Colorize(last.CpuUsage - prev.CpuUsage)}%");
    Console.WriteLine($"{"Mem",-10} | {Math.Round(lastUsedPct, 1)}% Used  | {Colorize(lastUsedPct - prevUsedPct, true)}%");
    Console.WriteLine();
}

// Show total memory available
static long GetMeminfoValue(string key) {
    // Look through /proc/meminfo for a specific key like "MemAvailable" or "MemTotal"
    var line = File.ReadLines("/proc/meminfo")
        .FirstOrDefault(l => l.StartsWith(key));
    
    if (line == null) return 0;

    // Line looks like: "MemAvailable:    15931204 kB"
    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return long.Parse(parts[1]) / 1024; // Convert KB to MB
}

// What --help actually prints
static void PrintHelp()
{
    Console.WriteLine("\n\u001b[1mSnaptool - A Minimal System Delta Tracker\u001b[0m");
    Console.WriteLine("Usage: snapshotter [command]");
    Console.WriteLine("\nCommands:");
    Console.WriteLine("  save    - Capture current CPU temp, load and memory usage.");
    Console.WriteLine("  diff    - Compare the last two snapshots and show changes.");
    Console.WriteLine("  help    - Show this help message.");
    Console.WriteLine("  clear   - Clear all saved snapshots.");
    Console.WriteLine("\nStorage: ~/.local/share/snapshotter/history.jsonl\n");
}

// --- 3. TYPE DECLARATIONS (Must come last) ---
public class SystemSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow; // guess what this possibly could mean
    public double CpuUsage { get; set; } // CPU usage
    public double CpuTemp { get; set; } // CPU temps
    public long AvailableMemoryMB { get; set; } // Total RAM
    public long TotalMemoryMB { get; set; } // Available RAM (yes they're swapped for.. whatever reason stfu)
    public List<string> TopProcesses { get; set; } = new();
}