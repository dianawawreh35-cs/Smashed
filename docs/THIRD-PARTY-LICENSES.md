# Third-party licences

Every direct dependency of the Restaurant Call Center System, with the version
pinned in the repository and its licence. Transitive dependencies are not listed
individually; nearly all are MIT, ISC, Apache-2.0 or BSD. The exceptions are
build tools that never reach the browser: lightningcss (MPL-2.0, Vite 8's CSS
tool), argparse (Python-2.0, under ESLint), caniuse-lite (CC-BY-4.0) and
minimatch (BlueOak-1.0.0).

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
| Microsoft.Web.WebView2 | 1.0.4258.31 | BSD-3-Clause (Microsoft); the Edge WebView2 Runtime it runs on is part of Windows |
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
| react-router-dom | 7.18.4 | MIT |
| recharts | 2.15.4 | MIT |
| @fontsource/cairo | 5.3.0 | OFL-1.1 |

> **Note on Cairo.** The font files are bundled into the web app and served
> by the server itself (27 Sep; the LAN server has no internet, so the Google
> Fonts link never loaded). The SIL Open Font License 1.1 allows bundling and
> redistributing the font with software, sold or not, on three conditions that
> apply here: the licence and copyright notice travel with it (the build
> serves them at `/licenses/cairo-OFL.txt`, from
> `src/CallCenter.Web/public/licenses/`, a copy of the package's LICENSE; copy
> it again when the package is bumped), the font is not sold on its own, and a
> modified version is not called Cairo. We ship it unmodified. Copyright 2009
> The Cairo Project Authors.

### Development dependencies

| Package | Version | Licence |
| --- | --- | --- |
| @testing-library/jest-dom | 6.9.1 | MIT |
| @testing-library/react | 16.3.3 | MIT |
| @types/node | 22.20.4 | MIT |
| @types/react | 18.3.31 | MIT |
| @types/react-dom | 18.3.7 | MIT |
| @typescript-eslint/eslint-plugin | 8.70.0 | MIT |
| @typescript-eslint/parser | 8.70.0 | MIT |
| @vitejs/plugin-react | 6.1.1 | MIT |
| autoprefixer | 10.6.0 | MIT |
| eslint | 8.57.1 | MIT |
| eslint-plugin-react-hooks | 4.6.2 | MIT |
| eslint-plugin-react-refresh | 0.4.26 | MIT |
| jsdom | 25.0.1 | MIT |
| postcss | 8.5.28 | MIT |
| tailwindcss | 3.4.19 | MIT |
| typescript | 5.9.3 | Apache-2.0 |
| vite | 8.3.1 | MIT |
| vitest | 5.0.2 | MIT |

## Fonts inside the Agent App

| Font | Version | Licence |
| --- | --- | --- |
| Cairo (Regular, SemiBold, Bold) | 3.130 | OFL-1.1 |
| Cascadia Mono (Regular, SemiBold, Bold) | 2407.024 | OFL-1.1 |

> **Note.** Since 4 Oct 2026 the Agent App draws its text with these fonts,
> built into the program from `src/CallCenter.AgentApp/Assets/Fonts/`, rather
> than with fonts installed on the laptop (N-10). The `.ttf` files are the
> static weights Google Fonts serves (`fonts.googleapis.com/css2`, no browser
> named, so it answers with whole TrueType files); Cairo's are the Cairo
> project's own 3.130 release, with every character it has. Same three conditions as the
> web app's Cairo above: the licence travels with the fonts (each one's OFL
> text is copied next to the program, `Assets\Fonts\*-OFL.txt`), they are
> not sold on their own, and they are unmodified. Cascadia's licence reserves
> the name *Cascadia Code*; Cascadia Mono is shipped unmodified under its own
> name. Copyright 2009 The Cairo Project Authors; copyright 2019 - present,
> Microsoft Corporation. Fetch them again from the same place when bumping.

## Container base images

| Image | Tag | Licence |
| --- | --- | --- |
| mcr.microsoft.com/dotnet/sdk | 8.0 | MIT (.NET); base OS packages under their own licences |
| mcr.microsoft.com/dotnet/aspnet | 8.0 | MIT (.NET); base OS packages under their own licences |
| node | 20-alpine | MIT (Node.js) |
| postgres | 16 | PostgreSQL License |
