using System.Text.Json;

namespace Ngb.Validation;

public static class Program
{
    public static int Main(string[] args)
    {
        return Run(args, Console.Out, Console.Error);
    }

    public static int Run(string[] args, TextWriter output, TextWriter errors)
    {
        if (args.Length != 2 || args[0] != "--assembly")
        {
            errors.WriteLine("Internal NGB metadata helper: --assembly <managed-assembly-path>");
            return 1;
        }

        try
        {
            using var stream = new FileStream(args[1], FileMode.Open, FileAccess.Read, FileShare.Read);
            var contract = AssemblyInspector.Inspect(stream);
            output.WriteLine(JsonSerializer.Serialize(contract, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return 0;
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            errors.WriteLine("NGB could not inspect managed metadata. Verify the assembly path and rebuild the declared project.");
            return 2;
        }
    }
}
