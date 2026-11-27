using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace SwiftRide.Infrastructure;

public static class CommandLineHealthProbe
{
    public static async Task<bool> Run(string[] args)
    {
        if (!args.Contains("--health"))
            return false;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await client.GetAsync("http://localhost:8080/health/ready");
            Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception)
        {
            Environment.ExitCode = 1;
        }
        return true;
    }
}
