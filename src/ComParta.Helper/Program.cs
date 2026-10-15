using ComParta.Helper;

// The ComParta helper (AD-23). Settings come from helper.json beside the program, then environment
// variables such as Helper__Key, then the command line such as --Helper:Key=...
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("helper.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();
var options = new HelperOptions();
configuration.GetSection(HelperOptions.Section).Bind(options);

if (options.Problems() is { Count: > 0 } problems)
{
    Console.Error.WriteLine("The helper cannot start. Fix helper.json:");
    foreach (var problem in problems)
        Console.Error.WriteLine("  " + problem);
    return 1;
}

await using var helper = await HelperHost.StartAsync(options, AppContext.BaseDirectory);
Console.WriteLine("ComParta helper running.");
Console.WriteLine($"  Public server: {helper.TrackerUri} and {helper.UdpTrackerUri}");
Console.WriteLine($"  Files served at: {helper.PeerEndPoint}");
Console.WriteLine($"  Control: {helper.ControlUri}{(options.UsesCertificate ? "" : " (no certificate: use only on a network you trust)")}");
await helper.WaitForShutdownAsync();
return 0;
