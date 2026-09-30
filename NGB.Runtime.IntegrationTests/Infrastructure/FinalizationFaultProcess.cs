using System.Diagnostics;
using System.Text.Json;
using Npgsql;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>Runs the production runner/rebuilder/UoW in a disposable child process.</summary>
internal sealed class FinalizationFaultProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _errors;

    private FinalizationFaultProcess(Process process)
    {
        _process = process;
        _errors = process.StandardError.ReadToEndAsync();
    }

    public static async Task<FinalizationFaultProcess> StartAsync(string connectionString, Guid id)
    {
        var assembly = Path.Combine(AppContext.BaseDirectory, "finalization-fault-worker", "NGB.Finalization.TestWorker.dll");
        if (!File.Exists(assembly)) throw new FileNotFoundException("Build the test worker before running crash tests.", assembly);
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(assembly);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start finalization test worker.");
        var owner = new FinalizationFaultProcess(process);
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, SslMode = SslMode.Disable, GssEncryptionMode = GssEncryptionMode.Disable };
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { ConnectionString = isolated.ConnectionString, RegisterId = id }));
            await process.StandardInput.FlushAsync();
            return owner;
        }
        catch { await owner.DisposeAsync(); throw; }
    }

    public async Task<int> WaitForPhaseAsync(string expected)
    {
        var line = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var parts = line?.Split('|');
        if (parts is not ["PHASE", var phase, var pid] || phase != expected)
            throw new InvalidOperationException($"Expected worker checkpoint {expected}; got {line ?? "EOF"}.");
        return int.Parse(pid);
    }

    public async Task ContinueAsync()
    {
        await _process.StandardInput.WriteLineAsync("continue");
        await _process.StandardInput.FlushAsync();
    }

    public async Task<int> ReadResultAsync()
    {
        var line = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if (line?.Split('|') is not ["RESULT", var count])
            throw new InvalidOperationException($"Expected worker result; got {line ?? "EOF"}.");
        return int.Parse(count);
    }

    public async Task<int> ExitAsync()
    {
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        return _process.ExitCode;
    }

    public async Task KillAsync()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await KillAsync();
            await _errors;
        }
        finally
        {
            _process.Dispose();
        }
    }
}
