using System;
using DotaTranspiler;

// Parse arguments
string? sourceDir = null;
string? outputDir = null;
bool debug = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-s" when i + 1 < args.Length:
            sourceDir = args[++i];
            break;
        case "-d" when i + 1 < args.Length:
            outputDir = args[++i];
            break;
        case "--debug":
            debug = true;
            break;
        case "-h":
        case "--help":
            PrintHelp();
            return 0;
    }
}

if (sourceDir == null || outputDir == null)
{
    Console.Error.WriteLine("Error: -s (source) and -d (output) are required.");
    PrintHelp();
    return 1;
}

Console.WriteLine($"DotaTranspiler");
Console.WriteLine($"  Source : {sourceDir}");
Console.WriteLine($"  Output : {outputDir}");
Console.WriteLine($"  Debug  : {debug}");
Console.WriteLine();

var transpiler = new Transpiler(new TranspilerOptions
{
    SourceDirectory = sourceDir,
    OutputDirectory = outputDir,
    Debug = debug,
});

var result = transpiler.Run();

foreach (var warning in result.Warnings)
    Console.WriteLine($"[WARN] {warning}");

foreach (var error in result.Errors)
    Console.Error.WriteLine($"[ERROR] {error}");

if (result.Success)
{
    Console.WriteLine($"OK — {result.OutputFiles.Count} file(s) written:");
    foreach (var f in result.OutputFiles)
        Console.WriteLine($"  {f}");
    return 0;
}
else
{
    Console.Error.WriteLine($"\nFailed with {result.Errors.Count} error(s).");
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("Usage: DotaTranspiler -s <source_dir> -d <output_dir> [--debug]");
    Console.WriteLine();
    Console.WriteLine("  -s  Source directory containing .cs files");
    Console.WriteLine("  -d  Output directory (game/scripts/vscripts/)");
    Console.WriteLine("  --debug  Emit C# source line comments in generated Lua");
}
