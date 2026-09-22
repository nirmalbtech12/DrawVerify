using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public static class ReportTemplatePlaceholderValidator
{
    public static void Validate(IEnumerable<TemplatePlaceholder>? placeholders)
    {
        var values = (placeholders ?? []).ToArray();
        if (values.Select(x => x.Id).Distinct().Count() != values.Length)
            throw new InvalidOperationException("Placeholder identifiers must be unique.");

        foreach (var placeholder in values) placeholder.Validate();

        var signatures = values.Where(x => x.IsSignature && !x.AllowDuplicateRole && !string.IsNullOrWhiteSpace(x.Role));
        var duplicate = signatures.GroupBy(x => $"{x.LogicalBlock}\u001f{x.Role}", StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Signature role '{duplicate.First().Role}' is duplicated in logical block '{duplicate.First().LogicalBlock}'.");
    }

    public static IReadOnlyList<string> ValidateAndCollect(IEnumerable<TemplatePlaceholder>? placeholders)
    {
        try { Validate(placeholders); return []; }
        catch (InvalidOperationException ex) { return [ex.Message]; }
    }
}
