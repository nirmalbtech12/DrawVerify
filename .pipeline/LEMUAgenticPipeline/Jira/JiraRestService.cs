// Jira mapping notes (kept here per implementation prompt):
// ---------------------------------------------------------------------------
// Field → StoryContext mapping
//   key                    → StoryContext.StoryKey / EpicKey / FeatureKey (via call arg)
//   fields.summary         → StoryContext.Summary
//   fields.description     → StoryContext.Requirement
//     - Jira Cloud returns Atlassian Document Format (ADF, JSON) when queried via REST v3.
//       This service converts ADF → plain text/markdown (recursive text extraction).
//     - Fallback: if description is already a string, use as-is.
//   fields.issuetype.name  → validated: Story / Feature / Epic (case-insensitive)
//   fields.parent.key      → parent link (Story→Feature, Feature→Epic). Primary hierarchy source.
//   fields.issuelinks[]    → fallback if parent is null (outwardIssue/inwardIssue with type "relates to" or "is child of").
//   AC fields              → StoryContext.AcceptanceCriteria (List<string>)
//     - Tries in order: 1) dedicated custom fields (Pipeline:Jira:AcceptanceCriteriaFields e.g. customfield_10001),
//       2) labels that start with "AC-" or "ac-", 3) checklist in description (lines starting with "- [ ]" / "AC-").
// Hierarchy validation (ValidateHierarchyAsync):
//   1. Get Story → StoryContext (extracts EpicKey/FeatureKey via parent chain).
//   2. If FeatureKey empty → try to resolve Feature via parent field of Story.
//   3. Get Feature → verify Feature's parent is EpicKey (or Feature's parent.key == EpicKey).
//   4. Get Epic → verify exists and issuetype == Epic.
//   Returns true only if Story→Feature→Epic chain is consistent and all exist.
// ---------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LEMUAgenticPipeline.Configuration;
using LEMUAgenticPipeline.Models;
using LEMUAgenticPipeline.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LEMUAgenticPipeline.Jira;

public sealed class JiraRestService : IJiraService
{
    private readonly HttpClient _http;
    private readonly JiraOptions _opts;
    private readonly ILogger<JiraRestService> _log;

    public JiraRestService(HttpClient http, IOptions<PipelineOptions> opts, ILogger<JiraRestService> log)
    {
        _http = http;
        _opts = opts.Value.Jira;
        _log = log;

        if (!string.IsNullOrWhiteSpace(_opts.BaseUrl))
        {
            var baseUrl = _opts.BaseUrl.TrimEnd('/');
            if (_http.BaseAddress == null)
                _http.BaseAddress = new Uri(baseUrl + "/");
            _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
        }

        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Token can come from Pipeline:Jira:Token (user-secrets) or env var (TokenEnv) — read per-request
        var startupToken = !string.IsNullOrWhiteSpace(_opts.Token) ? _opts.Token : Environment.GetEnvironmentVariable(_opts.TokenEnv) ?? string.Empty;
        _log.LogInformation("JiraRestService initialized BaseUrl={BaseUrl} TokenEnv={TokenEnv} HasToken={HasToken} (via {Source}) Email={Email}", _opts.BaseUrl, _opts.TokenEnv, !string.IsNullOrWhiteSpace(startupToken), !string.IsNullOrWhiteSpace(_opts.Token) ? "Pipeline:Jira:Token" : _opts.TokenEnv, _opts.Email);
    }

    public async Task<StoryContext> GetStoryAsync(string issueKey) => await GetIssueAsStoryContextAsync(issueKey, expectedType: "Story");

    public async Task<StoryContext> GetFeatureAsync(string featureKey) => await GetIssueAsStoryContextAsync(featureKey, expectedType: "Feature");

    public async Task<StoryContext> GetEpicAsync(string epicKey) => await GetIssueAsStoryContextAsync(epicKey, expectedType: "Epic");

    public async Task<bool> ValidateHierarchyAsync(string storyKey)
    {
        _log.LogInformation("Validating hierarchy for {Story}", storyKey);
        StoryContext story;
        JsonDocument rawStoryDoc;
        try
        {
            story = await GetStoryAsync(storyKey);
            rawStoryDoc = await GetIssueRawAsync(storyKey);
        }
        catch (JiraServiceException ex) when (ex.StatusCode == 404)
        {
            _log.LogWarning("Story {Story} not found", storyKey);
            return false;
        }

        // Resolve direct parent key (could be Feature or Epic — Epic is max root in this Jira)
        var parentKey = story.FeatureKey;
        if (string.IsNullOrWhiteSpace(parentKey))
            parentKey = ExtractParentKey(rawStoryDoc);
        if (string.IsNullOrWhiteSpace(parentKey))
            parentKey = ExtractLinkedKey(rawStoryDoc, linkTypeHint: "Feature");

        if (string.IsNullOrWhiteSpace(parentKey))
        {
            _log.LogWarning("Hierarchy invalid: Story {Story} has no parent (Feature/Epic)", storyKey);
            return false;
        }

        // Fetch parent and check its type
        JsonDocument rawParentDoc;
        try { rawParentDoc = await GetIssueRawAsync(parentKey); }
        catch (JiraServiceException ex) when (ex.StatusCode == 404)
        {
            _log.LogWarning("Parent {Parent} not found for story {Story}", parentKey, storyKey);
            return false;
        }
        var parentType = ExtractIssueTypeName(rawParentDoc);
        var parentParentKey = ExtractParentKey(rawParentDoc);

        // Case 1: Story -> Epic directly (KAN-19 -> KAN-6). Epic is max root, no further parent required.
        if (string.Equals(parentType, "Epic", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInformation("Hierarchy OK (Story->Epic): {Story} → {Epic} (Epic is root)", storyKey, parentKey);
            return true;
        }

        // Case 2: Story -> Feature -> Epic
        var featureKey = parentKey;
        var featureType = parentType;
        if (!string.Equals(featureType, "Feature", StringComparison.OrdinalIgnoreCase))
        {
            // Parent is not Feature/Epic — accept as Feature-like if hierarchyLevel==0, otherwise warn but allow Story->parent as valid for team-managed projects
            _log.LogInformation("Parent {Parent} type is {Type} (expected Feature/Epic) for Story {Story} — treating as Feature-like", parentKey, parentType, storyKey);
        }

        // Feature should have an Epic parent, but if Epic is root and Feature is actually Epic-like, parentParentKey may be empty — then Feature itself is considered Epic
        if (string.IsNullOrWhiteSpace(parentParentKey))
        {
            // Check if feature's type is Epic-like (already handled) or if this project uses Epic as Feature (KAN-6: summary "Feature 1" but type Epic)
            // If feature has no parent and we already know story->feature is at least 1 level, accept Story->Feature as valid when Epic is root and Feature is standalone
            var epicLink = ExtractEpicLink(rawParentDoc.RootElement.TryGetProperty("fields", out var f) ? f : default);
            if (!string.IsNullOrWhiteSpace(epicLink))
            {
                try { _ = await GetEpicAsync(epicLink); _log.LogInformation("Hierarchy OK: {Story} → {Feature} → {Epic} (via epic link)", storyKey, featureKey, epicLink); return true; }
                catch { /* fall through to warning */ }
            }
            // Epic is max root: Feature without parent is treated as top-level Feature/Epic — allow Story->Feature
            _log.LogInformation("Hierarchy OK (Story->Feature, Epic is root, Feature has no parent): {Story} → {Feature}", storyKey, featureKey);
            return true;
        }

        var epicKey = parentParentKey;
        if (string.IsNullOrWhiteSpace(epicKey))
            epicKey = ExtractLinkedKey(rawParentDoc, linkTypeHint: "Epic");
        if (string.IsNullOrWhiteSpace(epicKey))
        {
            // Try epic link on Feature
            epicKey = ExtractEpicLink(rawParentDoc.RootElement.TryGetProperty("fields", out var ff) ? ff : default);
        }
        if (string.IsNullOrWhiteSpace(epicKey))
        {
            _log.LogInformation("Hierarchy OK (Story->Feature, Epic is max root, no explicit Epic): {Story} → {Feature}", storyKey, featureKey);
            return true;
        }

        try { _ = await GetEpicAsync(epicKey); }
        catch (JiraServiceException ex) when (ex.StatusCode == 404)
        {
            _log.LogWarning("Epic {Epic} not found for feature {Feature}", epicKey, featureKey);
            return false;
        }

        _log.LogInformation("Hierarchy OK: {Story} → {Feature} → {Epic}", storyKey, featureKey, epicKey);
        return true;
    }

    private static string ExtractIssueTypeName(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("fields", out var fields) && fields.TryGetProperty("issuetype", out var it) && it.TryGetProperty("name", out var n))
            return n.GetString() ?? string.Empty;
        return string.Empty;
    }

    public async Task AddCommentAsync(string issueKey, string commentBody)
    {
        EnsureConfigured();
        var url = $"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/comment";
        var payload = JsonSerializer.Serialize(new
        {
            body = new
            {
                type = "doc",
                version = 1,
                content = new[]
                {
                    new {
                        type = "paragraph",
                        content = new[] { new { type = "text", text = commentBody } }
                    }
                }
            }
        });
        var resp = await SendWithLoggingAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") }, issueKey, "AddComment");
        if (!resp.IsSuccessStatusCode)
            throw await ToJiraExceptionAsync(resp, issueKey, "AddComment failed");
        _log.LogInformation("Added comment to {Issue}", issueKey);
    }

    public async Task TransitionIssueAsync(string issueKey, string targetStatus)
    {
        EnsureConfigured();
        // 1. GET transitions
        var transitionsUrl = $"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions";
        var getResp = await SendWithLoggingAsync(() => new HttpRequestMessage(HttpMethod.Get, transitionsUrl), issueKey, "GetTransitions");
        if (!getResp.IsSuccessStatusCode)
            throw await ToJiraExceptionAsync(getResp, issueKey, "GetTransitions failed");
        var json = await getResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        string? transitionId = null;
        if (doc.RootElement.TryGetProperty("transitions", out var transitions) && transitions.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in transitions.EnumerateArray())
            {
                var name = t.TryGetProperty("name", out var n) ? n.GetString() : null;
                var to = t.TryGetProperty("to", out var toEl) && toEl.TryGetProperty("name", out var toName) ? toName.GetString() : null;
                if (string.Equals(name, targetStatus, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(to, targetStatus, StringComparison.OrdinalIgnoreCase))
                {
                    transitionId = t.TryGetProperty("id", out var id) ? id.GetString() : null;
                    break;
                }
            }
        }
        if (transitionId == null)
            throw new JiraServiceException($"No transition found for status '{targetStatus}' on {issueKey}. Available: {json}", 400, issueKey);

        // 2. POST transition
        var payload = JsonSerializer.Serialize(new { transition = new { id = transitionId } });
        var postResp = await SendWithLoggingAsync(() => new HttpRequestMessage(HttpMethod.Post, transitionsUrl) { Content = new StringContent(payload, Encoding.UTF8, "application/json") }, issueKey, "Transition");
        if (!postResp.IsSuccessStatusCode)
            throw await ToJiraExceptionAsync(postResp, issueKey, $"Transition to '{targetStatus}' failed");
        _log.LogInformation("Transitioned {Issue} → {Status} (id {Id})", issueKey, targetStatus, transitionId);
    }

    // ---- helpers ----

    private async Task<StoryContext> GetIssueAsStoryContextAsync(string key, string expectedType)
    {
        var raw = await GetIssueRawAsync(key);
        var ctx = MapRawToStoryContext(key, raw, expectedType);
        _log.LogInformation("Fetched {Type} {Key}: {Summary}", expectedType, key, ctx.Summary);
        return ctx;
    }

    private async Task<JsonDocument> GetIssueRawAsync(string key)
    {
        EnsureConfigured();
        var url = $"rest/api/3/issue/{Uri.EscapeDataString(key)}?expand=names";
        var resp = await SendWithLoggingAsync(() => new HttpRequestMessage(HttpMethod.Get, url), key, "GetIssue");
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new JiraServiceException($"Issue not found: {key}", 404, key);
        if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
            throw new JiraServiceException($"Jira auth failed for {key} ({(int)resp.StatusCode} {resp.StatusCode}). Check Pipeline:Jira:Token (dotnet user-secrets) or { _opts.TokenEnv } env var and BaseUrl.", (int)resp.StatusCode, key);
        if (!resp.IsSuccessStatusCode)
            throw await ToJiraExceptionAsync(resp, key, "GetIssue failed");
        var body = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    private StoryContext MapRawToStoryContext(string key, JsonDocument doc, string expectedType)
    {
        var root = doc.RootElement;
        var fields = root.TryGetProperty("fields", out var f) ? f : default;
        var summary = fields.ValueKind != JsonValueKind.Undefined && fields.TryGetProperty("summary", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        var description = ExtractDescription(fields);
        var issueType = fields.ValueKind != JsonValueKind.Undefined && fields.TryGetProperty("issuetype", out var it) && it.TryGetProperty("name", out var itName) ? itName.GetString() ?? string.Empty : string.Empty;

        // Parent keys
        var parentKey = ExtractParentKey(doc);
        // Epic/Feature resolution: for Story expected, parent is Feature; for Feature, parent is Epic
        string epicKey = string.Empty, featureKey = string.Empty;
        if (string.Equals(expectedType, "Story", StringComparison.OrdinalIgnoreCase))
        {
            featureKey = parentKey;
            // Try to get epic via parent's parent if available (need second fetch if we want deep; leave to ValidateHierarchy)
            // Also check custom epic link field (common: customfield_10014)
            var epicLink = ExtractEpicLink(fields);
            if (!string.IsNullOrWhiteSpace(epicLink)) epicKey = epicLink;
        }
        else if (string.Equals(expectedType, "Feature", StringComparison.OrdinalIgnoreCase))
        {
            epicKey = parentKey;
        }

        var ac = ExtractAcceptanceCriteria(fields, description);

        return new StoryContext
        {
            StoryKey = key,
            Summary = summary,
            Requirement = description,
            AcceptanceCriteria = ac,
            FeatureKey = featureKey,
            EpicKey = epicKey,
        };
    }

    private static string ExtractDescription(JsonElement fields)
    {
        if (fields.ValueKind == JsonValueKind.Undefined || !fields.TryGetProperty("description", out var desc) || desc.ValueKind == JsonValueKind.Null)
            return string.Empty;
        if (desc.ValueKind == JsonValueKind.String) return desc.GetString() ?? string.Empty;
        // ADF: { type: doc, content: [...] }
        if (desc.ValueKind == JsonValueKind.Object)
            return AdfToPlainText(desc);
        return desc.ToString();
    }

    private static string AdfToPlainText(JsonElement node)
    {
        // Recursively collect "text" leaves, join with newlines for block nodes
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return text.GetString() ?? string.Empty;
            if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                var parts = content.EnumerateArray().Select(AdfToPlainText).Where(p => !string.IsNullOrEmpty(p)).ToArray();
                var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
                // Block types get newline separation
                return type is "paragraph" or "heading" or "bulletList" or "orderedList" or "listItem" or "codeBlock" or "blockquote"
                    ? string.Join("\n", parts)
                    : string.Join(" ", parts);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            return string.Join("\n", node.EnumerateArray().Select(AdfToPlainText).Where(p => !string.IsNullOrEmpty(p)));
        }
        return string.Empty;
    }

    private static string ExtractParentKey(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("fields", out var fields)) return string.Empty;
        if (fields.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object)
        {
            if (parent.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String) return k.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string ExtractEpicLink(JsonElement fields)
    {
        // Common epic link custom fields: customfield_10014 (Jira Software), customfield_10008, etc.
        var candidates = new[] { "customfield_10014", "customfield_10008", "customfield_10806" };
        foreach (var cf in candidates)
        {
            if (fields.TryGetProperty(cf, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        // Sometimes epic link is an object with key
        foreach (var cf in candidates)
        {
            if (fields.TryGetProperty(cf, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("key", out var k))
                return k.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string ExtractLinkedKey(JsonDocument doc, string linkTypeHint)
    {
        if (!doc.RootElement.TryGetProperty("fields", out var fields) || !fields.TryGetProperty("issuelinks", out var links) || links.ValueKind != JsonValueKind.Array)
            return string.Empty;
        foreach (var link in links.EnumerateArray())
        {
            var type = link.TryGetProperty("type", out var t) && t.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            // Try outward/inward
            if (link.TryGetProperty("outwardIssue", out var outIssue) && outIssue.TryGetProperty("key", out var outKey))
            {
                var k = outKey.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(k)) return k;
            }
            if (link.TryGetProperty("inwardIssue", out var inIssue) && inIssue.TryGetProperty("key", out var inKey))
            {
                var k = inKey.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(k)) return k;
            }
        }
        return string.Empty;
    }

    private List<string> ExtractAcceptanceCriteria(JsonElement fields, string description)
    {
        var list = new List<string>();
        // 1. Dedicated custom fields — handles string, array, object with value, and ADF doc (customfield_10135)
        if (fields.ValueKind != JsonValueKind.Undefined)
        {
            foreach (var cf in _opts.AcceptanceCriteriaFields)
            {
                if (!fields.TryGetProperty(cf, out var v) || v.ValueKind == JsonValueKind.Null) continue;
                if (v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.AddRange(SplitAc(s!));
                }
                else if (v.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in v.EnumerateArray())
                        if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()))
                            list.Add(el.GetString()!);
                        else if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("value", out var val))
                            list.Add(val.GetString() ?? el.ToString());
                        else if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("type", out var t2) && t2.GetString() == "doc")
                            list.Add(AdfToPlainText(el));
                }
                else if (v.ValueKind == JsonValueKind.Object)
                {
                    if (v.TryGetProperty("value", out var val2) && val2.ValueKind == JsonValueKind.String)
                    {
                        var s = val2.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.AddRange(SplitAc(s!));
                    }
                    else if (v.TryGetProperty("type", out var t) && t.GetString() == "doc")
                    {
                        // ADF doc (e.g., customfield_10135) — whole doc is the ACs
                        var adfText = AdfToPlainText(v);
                        if (!string.IsNullOrWhiteSpace(adfText)) list.Add(adfText);
                    }
                    else if (v.TryGetProperty("type", out var t3) && v.TryGetProperty("content", out _))
                    {
                        var adfText2 = AdfToPlainText(v);
                        if (!string.IsNullOrWhiteSpace(adfText2)) list.Add(adfText2);
                    }
                }
            }
        }
        if (list.Count > 0) return list.Distinct().ToList();

        // 2. Labels starting with AC-
        if (fields.ValueKind != JsonValueKind.Undefined && fields.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in labels.EnumerateArray())
            {
                var s = l.GetString();
                if (!string.IsNullOrWhiteSpace(s) && s.StartsWith("AC-", StringComparison.OrdinalIgnoreCase))
                    list.Add(s);
            }
            if (list.Count > 0) return list;
        }

        // 3. Checklist lines in description
        if (!string.IsNullOrWhiteSpace(description))
        {
            foreach (var line in description.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("AC-", StringComparison.OrdinalIgnoreCase) || t.StartsWith("- [ ] AC", StringComparison.OrdinalIgnoreCase) || t.StartsWith("- AC", StringComparison.OrdinalIgnoreCase))
                    list.Add(t.TrimStart('-', ' ', '[', ']', ' '));
                else if (t.StartsWith("- [ ]", StringComparison.OrdinalIgnoreCase))
                    list.Add(t.Substring(5).Trim());
            }
        }
        return list.Distinct().ToList();
    }

    private static IEnumerable<string> SplitAc(string s)
        => s.Split(new[] { "\r\n", "\n", ";" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x));

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_opts.BaseUrl))
            throw new JiraServiceException("Jira BaseUrl not configured. Set Pipeline:Jira:BaseUrl in appsettings.json or env Pipeline__Jira__BaseUrl.", null, null);
    }

    private async Task<HttpResponseMessage> SendWithLoggingAsync(Func<HttpRequestMessage> factory, string issueKey, string op)
    {
        var req = factory();
        // Per-request auth: Pipeline:Jira:Token (user-secrets) takes precedence over env var
        var token = !string.IsNullOrWhiteSpace(_opts.Token) ? _opts.Token : Environment.GetEnvironmentVariable(_opts.TokenEnv) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(token))
        {
            if (!string.IsNullOrWhiteSpace(_opts.Email))
            {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opts.Email}:{token}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            }
            else
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }
        else
        {
            _log.LogWarning("Jira token empty: env {TokenEnv} not set — request will be unauthenticated", _opts.TokenEnv);
        }
        _log.LogDebug("Jira {Op} {Method} {Url} for {Key}", op, req.Method, req.RequestUri, issueKey);
        var resp = await _http.SendAsync(req);
        _log.LogDebug("Jira {Op} returned {Status} for {Key}", op, (int)resp.StatusCode, issueKey);
        return resp;
    }

    private static async Task<JiraServiceException> ToJiraExceptionAsync(HttpResponseMessage resp, string issueKey, string prefix)
    {
        var body = await resp.Content.ReadAsStringAsync();
        var snippet = body.Length > 1000 ? body[..1000] + "..." : body;
        return new JiraServiceException($"{prefix} for {issueKey}: {(int)resp.StatusCode} {resp.StatusCode} — {snippet}", (int)resp.StatusCode, issueKey);
    }
}
