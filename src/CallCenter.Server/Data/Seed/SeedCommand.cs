using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Data.Seed;

/// <summary>
/// The <c>seed</c> command from step 7 of the deployment runbook:
/// <code>
/// docker compose exec api dotnet CallCenter.Server.dll seed \
///   --admin-user supervisor --admin-password 'TempPass!2026' \
///   --branches "Branch 1,Branch 2,Branch 3,Branch 4"
/// </code>
/// Run once at installation. It applies migrations first, so it also works
/// against a database that has only just been created.
/// </summary>
public static class SeedCommand
{
    public const string Verb = "seed";

    /// <summary><see langword="true"/> when the process was started to seed rather than serve.</summary>
    public static bool IsRequested(string[] args) =>
        args.Length > 0 && args[0].Equals(Verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the command. Returns the process exit code.</summary>
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var options = ParseArguments(args);

        if (options.ShowHelp)
        {
            Console.WriteLine(HelpText);
            return 0;
        }

        if (options.Error is not null)
        {
            Console.Error.WriteLine($"seed: {options.Error}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(HelpText);
            return 2;
        }

        try
        {
            await DatabaseInitialiser.MigrateAsync(app.Services);

            await using var scope = app.Services.CreateAsyncScope();

            // Resolved rather than constructed by hand: the seeder now also
            // writes the menu photographs to disk, and asking the container for
            // it keeps this command from having to know what else it needs.
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

            var result = await seeder.SeedAsync(
                options.AdminLogin,
                options.AdminPassword,
                options.AdminDisplayName,
                options.Branches);

            Console.WriteLine();
            Console.WriteLine($"  branches   {Describe(result.BranchesAdded)}");
            Console.WriteLine($"  channels   {Describe(result.ChannelsAdded)}");
            Console.WriteLine($"  types      {Describe(result.TypesAdded)}");
            Console.WriteLine($"  form v1    {(result.FormAdded ? "created" : "already present")}");
            Console.WriteLine($"  settings   {Describe(result.SettingsAdded)}");
            Console.WriteLine($"  delivery   {Describe(result.DeliveryAreasAdded)}");
            Console.WriteLine($"  menu       {Describe(result.MenuItemsAdded)}");

            if (result.UserCreated)
            {
                Console.WriteLine($"  supervisor {options.AdminLogin} created");
                Console.WriteLine();
                Console.WriteLine("Log in and change the password now - it was passed on the command line");
                Console.WriteLine("and will be in this machine's shell history.");
            }
            else if (result.UserSkippedReason is { } reason)
            {
                Console.WriteLine($"  supervisor not created ({reason})");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"seed failed: {ex.Message}");

            if (ex is Npgsql.NpgsqlException)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("The database could not be reached. Check that it is running:");
                Console.Error.WriteLine("  docker compose ps");
            }

            return 1;
        }

        static string Describe(int added) => added == 0 ? "already present" : $"{added} created";
    }

    private const string HelpText = """
        Usage: dotnet CallCenter.Server.dll seed [options]

        Creates the starting data for a new database: branches, channels,
        classification types, form version 1, default settings, and the first
        supervisor account. Applies any pending migrations first.

        Safe to run twice - nothing existing is changed or overwritten.

        Options:
          --admin-user <login>        create the first supervisor with this login
          --admin-password <pass>     their password (required with --admin-user)
          --admin-name <name>         display name (defaults to the login)
          --branches "A,B,C,D"        branch names (defaults to Branch 1-4;
                                      ignored if branches already exist)
          -h, --help                  show this

        The first supervisor is only created when the users table is empty.
        Further accounts are added in the supervisor app (S-42).
        """;

    internal record Options
    {
        public string? AdminLogin { get; init; }
        public string? AdminPassword { get; init; }
        public string? AdminDisplayName { get; init; }
        public IReadOnlyList<string>? Branches { get; init; }
        public bool ShowHelp { get; init; }
        public string? Error { get; init; }
    }

    internal static Options ParseArguments(string[] args)
    {
        string? login = null, password = null, displayName = null;
        List<string>? branches = null;

        // args[0] is the verb itself.
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-h" or "--help")
            {
                return new Options { ShowHelp = true };
            }

            var value = i + 1 < args.Length ? args[i + 1] : null;

            switch (arg)
            {
                case "--admin-user":
                    if (value is null) return new Options { Error = "--admin-user needs a value" };
                    login = value;
                    i++;
                    break;

                case "--admin-password":
                    if (value is null) return new Options { Error = "--admin-password needs a value" };
                    password = value;
                    i++;
                    break;

                case "--admin-name":
                    if (value is null) return new Options { Error = "--admin-name needs a value" };
                    displayName = value;
                    i++;
                    break;

                case "--branches":
                    if (value is null) return new Options { Error = "--branches needs a value" };
                    branches = value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    if (branches.Count == 0) return new Options { Error = "--branches listed no names" };
                    i++;
                    break;

                default:
                    return new Options { Error = $"unknown option '{arg}'" };
            }
        }

        if (login is not null && password is null)
        {
            return new Options { Error = "--admin-user also needs --admin-password" };
        }

        return new Options
        {
            AdminLogin = login,
            AdminPassword = password,
            AdminDisplayName = displayName,
            Branches = branches,
        };
    }
}
