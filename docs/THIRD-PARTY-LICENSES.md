# Third-party licences

Every direct dependency of the Restaurant Call Center System, with the version
pinned in the repository and its licence. Transitive dependencies are not listed
individually; all of them are MIT, Apache-2.0 or BSD.

Update this file whenever a package is added, removed or bumped.
NuGet versions come from [`Directory.Packages.props`](../Directory.Packages.props);
npm versions are the resolved versions in `src/CallCenter.Web/package-lock.json`.

---

## NuGet — server, agent app, shared and tests

| Package | Version | Licence |
| --- | --- | --- |
| CommunityToolkit.Mvvm | 8.3.2 | MIT |
| coverlet.collector | 6.0.2 | MIT |
| FluentAssertions | 6.12.2 | Apache-2.0 |
| Microsoft.AspNetCore.Mvc.Testing | 8.0.25 | MIT |
| Microsoft.EntityFrameworkCore | 8.0.11 | MIT |
| Microsoft.EntityFrameworkCore.Design | 8.0.11 | MIT |
| Microsoft.EntityFrameworkCore.Relational | 8.0.11 | MIT |
| Microsoft.EntityFrameworkCore.Sqlite | 8.0.11 | MIT |
| Microsoft.Extensions.Hosting | 8.0.1 | MIT |
| Microsoft.Extensions.Http | 8.0.1 | MIT |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.11 | PostgreSQL License |
| Serilog.AspNetCore | 8.0.3 | Apache-2.0 |
| Serilog.Extensions.Hosting | 8.0.0 | Apache-2.0 |
| Serilog.Settings.Configuration | 8.0.4 | Apache-2.0 |
| Serilog.Sinks.Console | 6.0.0 | Apache-2.0 |
| Serilog.Sinks.File | 6.0.0 | Apache-2.0 |
| SIPSorcery | 8.0.23 | BSD-3-Clause |
| SIPSorceryMedia.Windows | 8.0.14 | BSD-3-Clause |
| Swashbuckle.AspNetCore | 6.9.0 | MIT |
| xunit | 2.9.2 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 |

> **Note on FluentAssertions.** Pinned to 6.x deliberately: version 7 and later
> are distributed under a licence that requires a paid commercial subscription
> for non-open-source use. Do not bump past 6.12.x without a licence decision.

> **Note on SIPSorcery.** 8.x carries two known high-severity advisories
> (GHSA-28gm-jrmw-xx93, GHSA-jwjp-4649-v8jp) fixed only in the 10.x line, which
> requires .NET 10. See [DECISIONS.md](DECISIONS.md).

## npm — supervisor web app

### Runtime dependencies

| Package | Version | Licence |
| --- | --- | --- |
| @tanstack/react-query | 5.102.8 | MIT |
| i18next | 23.16.8 | MIT |
| i18next-browser-languagedetector | 8.2.1 | MIT |
| react | 18.3.1 | MIT |
| react-dom | 18.3.1 | MIT |
| react-i18next | 15.7.4 | MIT |
| react-router-dom | 6.30.6 | MIT |
| recharts | 2.15.4 | MIT |

### Development dependencies

| Package | Version | Licence |
| --- | --- | --- |
| @testing-library/jest-dom | 6.9.1 | MIT |
| @testing-library/react | 16.3.3 | MIT |
| @types/node | 20.19.43 | MIT |
| @types/react | 18.3.31 | MIT |
| @types/react-dom | 18.3.7 | MIT |
| @typescript-eslint/eslint-plugin | 8.70.0 | MIT |
| @typescript-eslint/parser | 8.70.0 | MIT |
| @vitejs/plugin-react | 4.7.0 | MIT |
| autoprefixer | 10.6.0 | MIT |
| eslint | 8.57.1 | MIT |
| eslint-plugin-react-hooks | 4.6.2 | MIT |
| eslint-plugin-react-refresh | 0.4.26 | MIT |
| jsdom | 25.0.1 | MIT |
| postcss | 8.5.28 | MIT |
| tailwindcss | 3.4.19 | MIT |
| typescript | 5.9.3 | Apache-2.0 |
| vite | 5.4.21 | MIT |
| vitest | 2.1.9 | MIT |

## Container base images

| Image | Tag | Licence |
| --- | --- | --- |
| mcr.microsoft.com/dotnet/sdk | 8.0 | MIT (.NET); base OS packages under their own licences |
| mcr.microsoft.com/dotnet/aspnet | 8.0 | MIT (.NET); base OS packages under their own licences |
| node | 20-alpine | MIT (Node.js) |
| postgres | 16 | PostgreSQL License |
