using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text;

namespace ZeroShanten;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var scriptFileArgument = new Argument<FileInfo>(
            "SCRIPT",
            "The path to the script to run")
            .ExistingOnly();

        var rootCommand = new RootCommand("Executes a 0-shanten script")
        {
            scriptFileArgument,
        };
        rootCommand.SetHandler(async context =>
        {
            var scriptFile = context.ParseResult.GetValueForArgument(scriptFileArgument);

            string scriptString;
            {
                using var stream = scriptFile.OpenRead();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

                scriptString = await reader.ReadToEndAsync();
            }

            try
            {
                var script = new Script(scriptString);

                using var stdin = Console.OpenStandardInput();
                using var stdout = Console.OpenStandardOutput();

                script.Invoke(stdin, stdout);
            }
            catch (ScriptException e)
            {
                var scriptPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), scriptFile.FullName);

                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Red;

                Console.Error.WriteLine($"{scriptPath}:{e.Message}");

                Console.ResetColor();

                context.ExitCode = 1;

                return;
            }
        });

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }
}
