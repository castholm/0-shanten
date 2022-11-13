using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text;

namespace ZeroShanten;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var scriptFileArgument = new Argument<FileInfo>("SCRIPT", "The path to the script").ExistingOnly();

        var invokeCommand = new Command("invoke", "Invoke a script") { scriptFileArgument };
        invokeCommand.SetHandler(async context =>
        {
            var scriptFile = context.ParseResult.GetValueForArgument(scriptFileArgument);
            var script = await LoadScript(scriptFile);
            if (!script.IsValid)
            {
                WriteParseErrorsToConsole(scriptFile, script);

                context.ExitCode = 1;
                return;
            }

            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            script.Invoke(stdin, stdout);
        });

        var checkCommand = new Command("check", "Check a script for errors") { scriptFileArgument };
        checkCommand.SetHandler(async context =>
        {
            var scriptFile = context.ParseResult.GetValueForArgument(scriptFileArgument);
            var script = await LoadScript(scriptFile);
            if (!script.IsValid)
            {
                WriteParseErrorsToConsole(scriptFile, script);

                context.ExitCode = 1;
            }
        });

        var rootCommand = new RootCommand("Process 0-shanten scripts")
        {
            invokeCommand,
            checkCommand,
        };

        return await new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build()
            .InvokeAsync(args);
    }

    static async Task<ZeroShantenScript> LoadScript(FileInfo scriptFile)
    {
        string scriptSource;
        {
            using var stream = scriptFile.OpenRead();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            scriptSource = await reader.ReadToEndAsync();
        }

        return ZeroShantenScript.Parse(scriptSource);
    }

    static void WriteParseErrorsToConsole(FileInfo scriptFile, ZeroShantenScript script)
    {
        var scriptPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), scriptFile.FullName);
        if (Path.DirectorySeparatorChar is '\\')
        {
            scriptPath = scriptPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Red;
        foreach (var error in script.ParseErrors)
        {
            var errorPosition = (error.Line, error.Character) == (error.EndLine, error.EndCharacter)
                ? $"{error.Line}:{error.Character}"
                : $"{error.Line}:{error.Character}-{error.EndLine}:{error.EndCharacter}";
            var errorMessage = error.Code switch
            {
                ParseErrorCode.InstructionCountIsNotAMultipleOf13 =>
                    "instruction count is not a multiple of 13",
                >= ParseErrorCode.HandIs1Shanten and <= ParseErrorCode.HandIs6Shanten =>
                    $"hand is {(int)error.Code}-shanten",
                ParseErrorCode.HandContainsMoreThanFourCopiesOfATile =>
                    "hand contains more than four copies of a tile",
                ParseErrorCode.HandContainsUnsupportedTiles =>
                    "hand contains unsupported tiles",
                _ =>
                    "unknown error",
            };
            Console.Error.WriteLine($"{scriptPath}:{errorPosition}: error: {errorMessage}");
        }
        Console.ResetColor();
    }
}
