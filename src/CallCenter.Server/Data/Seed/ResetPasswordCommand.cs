using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Data.Seed;

/// <summary>
/// The <c>reset-password</c> command: the way back in when everybody is locked
/// out.
/// <code>
/// docker compose exec api dotnet CallCenter.Server.dll reset-password \
///   --user supervisor --password 'NewPass!2026'
/// </code>
/// </summary>
/// <remarks>
/// This exists because there was no way back in. Password resets live behind
/// supervisor login (S-42), and <c>seed</c> refuses to create an account once
/// any user exists — so a forgotten supervisor password meant editing the
/// database by hand. That happened for real on 17 September 2026.
///
/// <b>Why a command and not a self-service reset.</b> A "forgot password" flow
/// needs somewhere to send the link, and this system has no email, no SMS and
/// no internet access by design. What it does have is a server the client
/// controls: whoever can run a command on it is already trusted with the
/// database, so this grants nothing that direct database access would not.
///
/// It also re-enables a disabled account and can promote to supervisor, because
/// the lockouts worth planning for are "the only supervisor left" and "the only
/// supervisor was disabled by accident" — and a reset that restores the password
/// but not the access would be a command that looks like it worked.
/// </remarks>
public static class ResetPasswordCommand
{
    public const string Verb = "reset-password";

    /// <summary>The shortest password this will set. Matches what S-42 enforces.</summary>
    public const int MinimumPasswordLength = 8;

    /// <summary><see langword="true"/> when the process was started to reset a password.</summary>
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
            Console.Error.WriteLine($"{Verb}: {options.Error}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(HelpText);
            return 2;
        }

        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Login.ToLower() == options.Login!.ToLower());

            if (user is null)
            {
                Console.Error.WriteLine($"{Verb}: no account with login '{options.Login}'.");
                Console.Error.WriteLine();
                await ListLoginsAsync(db);
                return 1;
            }

            var wasDisabled = !user.IsActive;
            var wasRole = user.Role;

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(options.Password);

            // A locked-out supervisor whose account is also disabled would
            // otherwise get a working password and still no way in.
            user.IsActive = true;

            if (options.MakeSupervisor)
            {
                user.Role = UserRoles.Supervisor;
            }

            // Every session this account has open is now stale. Closing them
            // matters: if the password was reset because it may have leaked,
            // leaving the old tokens working would defeat the point.
            var closed = await db.AgentSessions
                .Where(s => s.UserId == user.Id && s.LoggedOutAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.LoggedOutAt, DateTimeOffset.UtcNow)
                    .SetProperty(x => x.LogoutReason, LogoutReasons.PasswordReset));

            await db.SaveChangesAsync();

            Console.WriteLine();
            Console.WriteLine($"  account    {user.Login} ({user.DisplayName})");
            Console.WriteLine($"  password   reset");

            if (wasDisabled)
            {
                Console.WriteLine("  account    re-enabled (it was disabled)");
            }

            if (options.MakeSupervisor && wasRole != UserRoles.Supervisor)
            {
                Console.WriteLine($"  role       {wasRole} -> {UserRoles.Supervisor}");
            }

            if (closed > 0)
            {
                Console.WriteLine($"  sessions   {closed} open session(s) closed");
            }

            Console.WriteLine();
            Console.WriteLine("Log in and change the password now - it was passed on the command line");
            Console.WriteLine("and will be in this machine's shell history.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{Verb} failed: {ex.Message}");

            if (ex is Npgsql.NpgsqlException)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("The database could not be reached. Check that it is running:");
                Console.Error.WriteLine("  docker compose ps");
            }

            return 1;
        }
    }

    /// <summary>
    /// Shows which logins exist when the one asked for does not. A lockout is
    /// often "I cannot remember whether it was 'supervisor' or 'admin'", and
    /// making somebody open a database client to find out is the problem this
    /// command exists to remove.
    /// </summary>
    private static async Task ListLoginsAsync(CallCenterDbContext db)
    {
        var supervisors = await db.Users
            .Where(u => u.Role == UserRoles.Supervisor)
            .OrderBy(u => u.Login)
            .Select(u => new { u.Login, u.IsActive })
            .ToListAsync();

        if (supervisors.Count == 0)
        {
            Console.Error.WriteLine("There are no supervisor accounts at all. Use --make-supervisor");
            Console.Error.WriteLine("to promote an agent, or seed a fresh database.");
            return;
        }

        Console.Error.WriteLine("Supervisor accounts on this database:");

        foreach (var supervisor in supervisors)
        {
            Console.Error.WriteLine($"  {supervisor.Login}{(supervisor.IsActive ? "" : "  (disabled)")}");
        }
    }

    private const string HelpText = """
        Usage: dotnet CallCenter.Server.dll reset-password --user <login> --password <new>

        Sets a new password for an existing account, from the server's command
        line. The way back in when the only supervisor password is lost - there
        is no self-service reset, because the system has no email or SMS.

        The account is re-enabled if it was disabled, and every session it has
        open is closed.

        Options:
          --user <login>          the account to reset (required)
          --password <new>        the new password, at least 8 characters (required)
          --make-supervisor       also promote the account to Supervisor
          -h, --help              show this

        Run it, log in, then change the password in the app: what is typed here
        stays in the shell history of the machine it was run on.
        """;

    internal record Options
    {
        public string? Login { get; init; }
        public string? Password { get; init; }
        public bool MakeSupervisor { get; init; }
        public bool ShowHelp { get; init; }
        public string? Error { get; init; }
    }

    internal static Options ParseArguments(string[] args)
    {
        string? login = null, password = null;
        var makeSupervisor = false;

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
                case "--user":
                    if (value is null) return new Options { Error = "--user needs a value" };
                    login = value;
                    i++;
                    break;

                case "--password":
                    if (value is null) return new Options { Error = "--password needs a value" };
                    password = value;
                    i++;
                    break;

                case "--make-supervisor":
                    makeSupervisor = true;
                    break;

                default:
                    return new Options { Error = $"unknown option '{arg}'" };
            }
        }

        if (string.IsNullOrWhiteSpace(login))
        {
            return new Options { Error = "--user is required" };
        }

        if (string.IsNullOrEmpty(password))
        {
            return new Options { Error = "--password is required" };
        }

        if (password.Length < MinimumPasswordLength)
        {
            return new Options
            {
                Error = $"--password must be at least {MinimumPasswordLength} characters",
            };
        }

        return new Options
        {
            Login = login,
            Password = password,
            MakeSupervisor = makeSupervisor,
        };
    }
}
