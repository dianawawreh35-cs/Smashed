using System.Text.Json;
using CallCenter.Shared.Contracts.Websites;

namespace CallCenter.AgentApp.Services.Websites;

/// <summary>
/// Signs a tab in with the login the supervisor set (A-88): finds the site's
/// username and password boxes, types into them, and presses its login button.
/// </summary>
/// <remarks>
/// <b>Only on the site's own pages.</b> The password goes into a page whose
/// host is the tab's, or a sibling under the same parent domain (a login at
/// <c>login.example.com</c> for <c>www.example.com</c>), and never anywhere
/// else the tab is taken: <see cref="MayFillOn"/> here, and the page checks
/// its own address again before it fills anything.
///
/// <b>Finding the boxes.</b> The visible password box, and the visible text
/// box before it in the same form; the form's submit button, or a button that
/// says Log in. A site this cannot read gets the supervisor's CSS selectors.
/// The values go in through the input element's own setter, with input and
/// change events, so sites built on React or Angular see them as typed.
///
/// <b>Once, not for ever.</b> <see cref="Gate"/> stops a wrong password from
/// being tried again and again until the site locks the account.
/// </remarks>
public static class AutoLogin
{
    /// <summary>What the page script answers.</summary>
    public const string Submitted = "submitted";

    public const string NoForm = "no-form";

    public const string OtherSite = "other-site";

    /// <summary>Whether the tab's login may be typed into a page at <paramref name="page"/>.</summary>
    public static bool MayFillOn(string siteUrl, Uri? page) =>
        page is not null
        && (page.Scheme == Uri.UriSchemeHttps || page.Scheme == Uri.UriSchemeHttp)
        && Uri.TryCreate(siteUrl, UriKind.Absolute, out var site)
        && AllowedHosts(site).Any(h => SameOrUnder(page.Host, h));

    /// <summary>
    /// The script for one attempt. The values travel as one JSON literal, so
    /// a password with quotes or backslashes in it cannot break out of it.
    /// </summary>
    public static string Script(AgentWebsiteDto site)
    {
        var allowed = Uri.TryCreate(site.Url, UriKind.Absolute, out var uri) ? AllowedHosts(uri) : [];

        var config = JsonSerializer.Serialize(new
        {
            hosts = allowed,
            username = site.Username ?? string.Empty,
            password = site.Password ?? string.Empty,
            usernameSelector = site.UsernameSelector,
            passwordSelector = site.PasswordSelector,
            submitSelector = site.SubmitSelector,
        });

        return "(" + PageScript + ")(" + config + ")";
    }

    /// <summary>
    /// The site's host, and its parent domain when it has one: <c>www.x.com</c>
    /// allows <c>x.com</c> and so <c>login.x.com</c>. Two labels are the
    /// parent; a <c>co.uk</c>-style suffix would allow more than it should,
    /// and none of the restaurant's sites has one.
    /// </summary>
    private static string[] AllowedHosts(Uri site)
    {
        var labels = site.Host.Split('.');
        return labels.Length > 2
            ? [site.Host, string.Join('.', labels[^2..])]
            : [site.Host];
    }

    private static bool SameOrUnder(string host, string allowed) =>
        host.Equals(allowed, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs in the page. Returns one of the answers above.</summary>
    private const string PageScript = """
        function (cfg) {
          var host = location.hostname.toLowerCase();
          var ok = cfg.hosts.some(function (h) { h = h.toLowerCase(); return host === h || host.endsWith('.' + h); });
          if (!ok) return 'other-site';

          function shown(el) {
            if (!el || el.disabled || el.readOnly) return false;
            var r = el.getBoundingClientRect();
            return r.width > 0 && r.height > 0 && getComputedStyle(el).visibility !== 'hidden';
          }
          function pick(selector) {
            try { return selector ? document.querySelector(selector) : null; } catch (e) { return null; }
          }

          var pw = pick(cfg.passwordSelector)
            || Array.prototype.slice.call(document.querySelectorAll('input[type=password]')).filter(shown)[0];
          if (!pw) return 'no-form';

          var user = pick(cfg.usernameSelector);
          if (!user) {
            var scope = pw.form || document;
            var boxes = Array.prototype.slice.call(scope.querySelectorAll('input')).filter(function (i) {
              var type = (i.getAttribute('type') || 'text').toLowerCase();
              return shown(i) && ['text', 'email', 'tel', 'number'].indexOf(type) >= 0
                && !/search|captcha|otp|code/i.test((i.name || '') + ' ' + (i.id || ''));
            });
            var before = boxes.filter(function (i) {
              return i.compareDocumentPosition(pw) & Node.DOCUMENT_POSITION_FOLLOWING;
            });
            user = before.length ? before[before.length - 1] : boxes[0];
          }

          var setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
          function type(el, value) {
            el.focus();
            setter.call(el, value);
            el.dispatchEvent(new Event('input', { bubbles: true }));
            el.dispatchEvent(new Event('change', { bubbles: true }));
          }
          if (user && cfg.username) type(user, cfg.username);
          type(pw, cfg.password);

          var submit = pick(cfg.submitSelector);
          if (!submit && pw.form) {
            submit = pw.form.querySelector('button[type=submit], input[type=submit]')
              || pw.form.querySelector('button:not([type=button])');
          }
          if (!submit) {
            submit = Array.prototype.slice.call(document.querySelectorAll('button, input[type=submit], [role=button]'))
              .filter(shown)
              .filter(function (b) { return /log ?in|sign ?in|دخول/i.test(b.textContent || b.value || ''); })[0];
          }

          setTimeout(function () {
            if (submit) submit.click();
            else if (pw.form && pw.form.requestSubmit) pw.form.requestSubmit();
          }, 250);
          return 'submitted';
        }
        """;

    /// <summary>
    /// When a tab may try. A login page that comes back within a minute of
    /// signing in means the password was refused (or the site wants a code):
    /// the tab stops and says so, and tries again only when the agent asks.
    /// </summary>
    public sealed class Gate(TimeProvider? clock = null)
    {
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        private readonly TimeProvider _clock = clock ?? TimeProvider.System;
        private DateTimeOffset? _lastSubmitted;

        /// <summary>The login was refused; no more tries until <see cref="Reset"/>.</summary>
        public bool Failed { get; private set; }

        /// <summary>
        /// Whether to fill a login page that has just appeared. Answers false,
        /// and marks the gate failed, when the last try was under a minute ago.
        /// </summary>
        public bool MayTry()
        {
            if (Failed)
            {
                return false;
            }

            if (_lastSubmitted is { } last && _clock.GetUtcNow() - last < Window)
            {
                Failed = true;
                return false;
            }

            return true;
        }

        public void Submitted() => _lastSubmitted = _clock.GetUtcNow();

        /// <summary>The agent asked: try again from the start.</summary>
        public void Reset()
        {
            Failed = false;
            _lastSubmitted = null;
        }
    }
}
