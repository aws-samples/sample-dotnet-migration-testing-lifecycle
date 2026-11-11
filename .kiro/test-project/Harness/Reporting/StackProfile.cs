using System.Text.RegularExpressions;

namespace MigrationTests.Reporting;

/// <summary>
/// Shared stack knowledge for one Reference/Target technology pair (profiles/&lt;name&gt;.json), so every team
/// does not re-accept the same framework noise page by page. Data only: the comparison core stays generic.
/// A profile may "extends" another; generic.json is always the last link of the chain when it exists.
/// Lists merge child first (child acceptances are tried before the parent's).
/// </summary>
public sealed class StackProfile
{
    public const string GenericName = "generic";

    public string Name { get; set; } = GenericName;
    public string? Extends { get; set; }
    public string? Description { get; set; }
    /// <summary>Groups of roles that are the same thing on the two stacks, e.g. ["gridcell", "cell"].</summary>
    public List<List<string>> RoleEquivalences { get; set; } = new();
    /// <summary>Widget part (grid, header, row, pager, filter, dialog) → selector hints per side (documentation for planners).</summary>
    public Dictionary<string, SelectorHint> SelectorHints { get; set; } = new();
    /// <summary>Differences accepted by default for this stack pair, each with a reason shown in the report.</summary>
    public List<ProfileAcceptance> Accepted { get; set; } = new();
    /// <summary>Properties never compared by the automatic comparison (exact name or "prefix*").</summary>
    public List<string> IgnoreProperties { get; set; } = new();
    /// <summary>Roles excluded from the inventory matching (e.g. framework-only decoration).</summary>
    public List<string> IgnoreRoles { get; set; } = new();

    /// <summary>Profile files loaded, requested profile first (e.g. ["webforms-jqgrid__blazor-mudblazor", "generic"]).</summary>
    public List<string> Chain { get; set; } = new();
    /// <summary>Load problems (missing file in the chain); the profile still works with what was loaded.</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Loads profiles/&lt;name&gt;.json and its extends chain (generic last). Empty name = generic. A missing file is a
    /// warning (the rest of the chain still applies); an unparsable file throws <see cref="ReportInputException"/>.
    /// </summary>
    public static StackProfile Load(string dir, string? name)
    {
        var requested = string.IsNullOrWhiteSpace(name) ? GenericName : name.Trim();
        var merged = new StackProfile { Name = requested };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var next = requested;
        while (!string.IsNullOrWhiteSpace(next) && seen.Add(next))
        {
            var p = ReportJson.Read<StackProfile>(Path.Combine(dir, next + ".json"));
            if (p == null)
            {
                merged.Warnings.Add($"profile '{next}' not found in {dir}");
                next = string.Equals(next, GenericName, StringComparison.OrdinalIgnoreCase) ? null : GenericName;
                continue;
            }
            merged.Chain.Add(next);
            merged.Description ??= p.Description;
            merged.RoleEquivalences.AddRange(p.RoleEquivalences ?? new());
            foreach (var (k, v) in p.SelectorHints ?? new())
                if (!merged.SelectorHints.ContainsKey(k)) merged.SelectorHints[k] = v;
            foreach (var a in p.Accepted ?? new()) { a.Source = next; merged.Accepted.Add(a); }
            merged.IgnoreProperties.AddRange(p.IgnoreProperties ?? new());
            merged.IgnoreRoles.AddRange(p.IgnoreRoles ?? new());
            next = !string.IsNullOrWhiteSpace(p.Extends) ? p.Extends.Trim()
                : string.Equals(next, GenericName, StringComparison.OrdinalIgnoreCase) ? null : GenericName;
        }
        return merged;
    }

    /// <summary>Same role, or both roles in one RoleEquivalences group (case-insensitive).</summary>
    public bool RolesEquivalent(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return RoleEquivalences.Any(g => g.Contains(a, StringComparer.OrdinalIgnoreCase) && g.Contains(b, StringComparer.OrdinalIgnoreCase));
    }

    public bool IgnoresRole(string? role) =>
        !string.IsNullOrWhiteSpace(role) && IgnoreRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool IgnoresProperty(string property) => IgnoreProperties.Any(p => PropertyMatches(p, property));

    /// <summary>"prefix*" matches by prefix, anything else exactly (case-insensitive).</summary>
    internal static bool PropertyMatches(string pattern, string property)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var p = pattern.Trim();
        return p.EndsWith('*')
            ? property.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(p, property, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reason of the first acceptance that matches the row (property, optional role, element substring and
    /// Reference/Target value regexes), else null. role = the element's accessibility role when known.
    /// </summary>
    public string? TryAccept(CssDiffEntry d, string? role = null) => FindAcceptance(d, role)?.Reason;

    public ProfileAcceptance? FindAcceptance(CssDiffEntry d, string? role = null)
    {
        foreach (var a in Accepted)
        {
            if (!PropertyMatches(a.Property, d.Property)) continue;
            if (!string.IsNullOrWhiteSpace(a.Role) && !RolesEquivalent(a.Role, role)) continue;
            if (!string.IsNullOrWhiteSpace(a.Element) && d.Element.IndexOf(a.Element, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!ValueMatches(a.Reference, d.Reference) || !ValueMatches(a.Target, d.Target)) continue;
            return a;
        }
        return null;
    }

    private static bool ValueMatches(string? pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try { return Regex.IsMatch(value ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); }
        catch (ArgumentException) { return false; } // invalid regex never accepts (false PASS is never acceptable)
        catch (RegexMatchTimeoutException) { return false; }
    }
}

public sealed class SelectorHint
{
    public List<string> Reference { get; set; } = new();
    public List<string> Target { get; set; } = new();
}

/// <summary>One accepted-by-default difference of a stack profile.</summary>
public sealed class ProfileAcceptance
{
    /// <summary>CSS diff property, exact or "prefix*" (e.g. "hover.*" style families, "disabled.cursor").</summary>
    public string Property { get; set; } = "";
    /// <summary>Optional accessibility role of the element (profile role equivalences apply).</summary>
    public string? Role { get; set; }
    /// <summary>Optional case-insensitive substring of the element label.</summary>
    public string? Element { get; set; }
    /// <summary>Optional regex the Reference value must match.</summary>
    public string? Reference { get; set; }
    /// <summary>Optional regex the Target value must match.</summary>
    public string? Target { get; set; }
    public string Reason { get; set; } = "";
    /// <summary>Profile file the entry came from (set by Load).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Source { get; set; } = "";
}
