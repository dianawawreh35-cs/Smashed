using Npgsql;

namespace CallCenter.Server.Data;

/// <summary>
/// Keeps the server's connection pool below PostgreSQL's own connection limit (N-03).
/// </summary>
/// <remarks>
/// <para>
/// Npgsql's pool grows to 100 connections by default, and PostgreSQL accepts
/// 100 in total, three of them kept for a superuser. So one burst of requests
/// could take every connection the database had and keep them for the five
/// minutes an idle pooled connection lives. Everything else that needed one
/// was then refused with <c>53300 too many clients</c>: the nightly
/// <c>pg_dump</c>, <c>psql</c>, a second copy of the server, and the server's
/// own next connection. <c>EnableRetryOnFailure</c> counts 53300 as transient,
/// so those requests were not refused outright. They were retried with growing
/// back-off, which is what "7 of 39 in 25 seconds, with long silent gaps" looks
/// like.
/// </para>
/// <para>
/// With a cap, a burst larger than the pool waits its turn for a connection
/// (Npgsql's 15-second <c>Timeout</c>), rather than failing at the database. 50
/// is well over what 20 agents need at once, and leaves the database half its
/// connections for everything else.
/// </para>
/// </remarks>
public static class ConnectionPool
{
    /// <summary>The cap when neither configuration nor the connection string sets one.</summary>
    public const int DefaultMaxSize = 50;

    /// <summary>
    /// <paramref name="connectionString"/> with <c>Maximum Pool Size</c> set to
    /// <paramref name="maxSize"/>, unless the connection string already sets its
    /// own. An explicit value there is somebody's decision, and it wins.
    /// </summary>
    public static string WithMaxSize(string connectionString, int maxSize = DefaultMaxSize)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        var alreadySet = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0].Trim().Replace(" ", string.Empty))
            .Any(key => key.Equals("MaximumPoolSize", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("MaxPoolSize", StringComparison.OrdinalIgnoreCase));

        if (!alreadySet)
        {
            builder.MaxPoolSize = maxSize;
        }

        return builder.ConnectionString;
    }
}
