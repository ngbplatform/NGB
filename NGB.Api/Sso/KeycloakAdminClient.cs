using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;

namespace NGB.Api.Sso;

public sealed class KeycloakAdminClient(
    HttpClient httpClient,
    TokenCacheService tokenCache,
    KeycloakUserLookupCache lookupCache,
    KeycloakAdminClientSettings settings,
    KeycloakAdminRequestGate requestGate)
    : IIdentityProviderUserAdminClient, IIdentityProviderBulkUserReader, IIdentityProviderUserPageSnapshotReader
{
    private const int MinAdminBatchConcurrency = 1;
    private const int MaxAdminBatchConcurrency = 32;
    
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string UsersCreateOperation = "keycloak.users.create";
    private const string UsersUpdateOperation = "keycloak.users.update";
    private const string UsersSetEnabledOperation = "keycloak.users.set_enabled";
    private const string UsersGetOperation = "keycloak.users.get";
    private const string UsersFindByEmailOperation = "keycloak.users.find_by_email";
    private const string UsersFindByUsernameOperation = "keycloak.users.find_by_username";
    private const string UsersResetPasswordOperation = "keycloak.users.reset_password";

    public async Task<IdentityProviderUserDto> CreateUserAsync(
        CreateIdentityProviderUserRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var email = request.Email.Trim();
        var payload = new KeycloakUserWriteDto(
            Username: email,
            Email: email,
            FirstName: request.FirstName,
            LastName: request.LastName,
            Enabled: request.Enabled,
            EmailVerified: true,
            RequiredActions: request.RequirePasswordUpdate ? ["UPDATE_PASSWORD"] : [],
            Attributes: BuildAttributes(request.DisplayName));

        using var response = await SendAsync(
            HttpMethod.Post,
            AdminPath("users"),
            payload,
            operation: UsersCreateOperation,
            ct);

        lookupCache.InvalidateEmail(email);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await FindUserByEmailAsync(email, ct);
            if (existing is not null)
                return existing;
        }

        await EnsureSuccessAsync(response, UsersCreateOperation, ct);

        var location = response.Headers.Location?.ToString();
        var userId = location?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(userId))
        {
            var created = await FindUserByEmailAsync(email, ct);
            if (created is not null)
                return created;

            throw new NgbConfigurationViolationException("Keycloak create-user response did not include a user id.");
        }

        return await GetUserByIdAsync(userId, ct)
               ?? throw new NgbConfigurationViolationException("Keycloak created user could not be loaded.");
    }

    public async Task UpdateUserAsync(
        string identityProviderUserId,
        UpdateIdentityProviderUserRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identityProviderUserId))
            throw new NgbArgumentRequiredException(nameof(identityProviderUserId));

        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var email = request.Email?.Trim();
        var payload = new KeycloakUserWriteDto(
            Username: email,
            Email: email,
            FirstName: request.FirstName,
            LastName: request.LastName,
            Enabled: request.Enabled,
            EmailVerified: true,
            RequiredActions: null,
            Attributes: BuildAttributes(request.DisplayName));

        using var response = await SendAsync(
            HttpMethod.Put,
            AdminPath($"users/{Uri.EscapeDataString(identityProviderUserId.Trim())}"),
            payload,
            operation: UsersUpdateOperation,
            ct);

        await EnsureSuccessAsync(response, UsersUpdateOperation, ct);
        lookupCache.InvalidateUser(identityProviderUserId.Trim(), email);
    }

    public async Task SetUserEnabledAsync(string identityProviderUserId, bool enabled, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identityProviderUserId))
            throw new NgbArgumentRequiredException(nameof(identityProviderUserId));

        using var response = await SendAsync(
            HttpMethod.Put,
            AdminPath($"users/{Uri.EscapeDataString(identityProviderUserId.Trim())}"),
            new { enabled },
            operation: UsersSetEnabledOperation,
            ct);

        await EnsureSuccessAsync(response, UsersSetEnabledOperation, ct);
        lookupCache.InvalidateUser(identityProviderUserId.Trim());
    }

    public async Task<IdentityProviderUserDto?> GetUserByIdAsync(string identityProviderUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identityProviderUserId))
            throw new NgbArgumentRequiredException(nameof(identityProviderUserId));

        var normalizedId = identityProviderUserId.Trim();

        return await lookupCache.GetByIdAsync(
            normalizedId,
            innerCt => GetUserByIdCoreAsync(normalizedId, innerCt),
            ct);
    }

    private async Task<IdentityProviderUserDto?> GetUserByIdCoreAsync(string identityProviderUserId, CancellationToken ct)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            AdminPath($"users/{Uri.EscapeDataString(identityProviderUserId)}"),
            body: null,
            operation: UsersGetOperation,
            ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, UsersGetOperation, ct);
        var dto = await response.Content.ReadFromJsonAsync<KeycloakUserDto>(Json, ct);
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyDictionary<string, IdentityProviderUserDto>> GetUsersByIdsAsync(
        IReadOnlyList<string> identityProviderUserIds,
        CancellationToken ct)
    {
        if (identityProviderUserIds is null)
            throw new NgbArgumentRequiredException(nameof(identityProviderUserIds));

        var ids = identityProviderUserIds
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<string, IdentityProviderUserDto>(StringComparer.Ordinal);

        var rows = await ExecuteBoundedBatchAsync(ids, GetUserByIdAsync, ct);
        var result = new Dictionary<string, IdentityProviderUserDto>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Length; i++)
        {
            if (rows[i] is { } row)
                result[ids[i]] = row;
        }

        return result;
    }

    public async Task<IdentityProviderUserBatch> GetUsersAsync(
        IReadOnlyList<string> identityProviderUserIds,
        IReadOnlyList<string> emails,
        CancellationToken ct)
    {
        if (identityProviderUserIds is null)
            throw new NgbArgumentRequiredException(nameof(identityProviderUserIds));

        if (emails is null)
            throw new NgbArgumentRequiredException(nameof(emails));

        var ids = identityProviderUserIds
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var normalizedEmails = emails
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ids.Length == 0 && normalizedEmails.Length == 0)
        {
            return new IdentityProviderUserBatch(
                new Dictionary<string, IdentityProviderUserDto>(StringComparer.Ordinal),
                new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase));
        }

        // A realm-wide paged scan has unbounded cost as the realm grows and can make a single
        // platform-user page read traverse every Keycloak user. Resolve known subjects directly,
        // then issue exact lookups only for emails that were not already supplied by those users.
        var byId = ids.Length == 0
            ? new Dictionary<string, IdentityProviderUserDto>(StringComparer.Ordinal)
            : new Dictionary<string, IdentityProviderUserDto>(await GetUsersByIdsAsync(ids, ct), StringComparer.Ordinal);
        var byEmail = new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase);
        var requestedEmails = normalizedEmails.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var user in byId.Values)
        {
            if (string.IsNullOrWhiteSpace(user.Email) || !requestedEmails.Remove(user.Email))
                continue;

            byEmail[user.Email] = user;
        }

        if (requestedEmails.Count > 0)
        {
            var foundByEmail = await FindUsersByEmailsAsync(requestedEmails.ToArray(), ct);
            foreach (var (email, user) in foundByEmail)
            {
                byEmail[email] = user;
            }
        }

        return new IdentityProviderUserBatch(byId, byEmail);
    }

    public IdentityProviderUserBatch GetCachedUsers(
        IReadOnlyList<string> identityProviderUserIds,
        IReadOnlyList<string> emails)
    {
        ArgumentNullException.ThrowIfNull(identityProviderUserIds);
        ArgumentNullException.ThrowIfNull(emails);

        var byId = new Dictionary<string, IdentityProviderUserDto>(StringComparer.Ordinal);
        foreach (var id in identityProviderUserIds
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Select(static value => value.Trim())
                     .Distinct(StringComparer.Ordinal))
        {
            if (lookupCache.TryGetById(id, out var user) && user is not null)
                byId[id] = user;
        }

        var byEmail = new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var email in emails
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Select(static value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (lookupCache.TryGetByEmail(email, out var user) && user is not null)
                byEmail[email] = user;
        }

        return new IdentityProviderUserBatch(byId, byEmail);
    }

    public async Task<IdentityProviderUserDto?> FindUserByEmailAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new NgbArgumentRequiredException(nameof(email));

        var normalizedEmail = email.Trim();

        return await lookupCache.GetByEmailAsync(
            normalizedEmail,
            innerCt => FindUserByEmailCoreAsync(normalizedEmail, innerCt),
            ct);
    }

    private async Task<IdentityProviderUserDto?> FindUserByEmailCoreAsync(string normalizedEmail, CancellationToken ct)
    {
        var query = QueryHelpers.AddQueryString(
            AdminPath("users"),
            new Dictionary<string, string?>
            {
                ["email"] = normalizedEmail,
                ["exact"] = "true"
            });

        using var response = await SendAsync(HttpMethod.Get, query, body: null, operation: UsersFindByEmailOperation, ct);
        await EnsureSuccessAsync(response, UsersFindByEmailOperation, ct);

        var rows = await response.Content.ReadFromJsonAsync<KeycloakUserDto[]>(Json, ct) ?? [];
        var match = FindUserByEmailOrUsername(rows, normalizedEmail);
        if (match is not null)
            return Map(match);

        var usernameQuery = QueryHelpers.AddQueryString(
            AdminPath("users"),
            new Dictionary<string, string?>
            {
                ["username"] = normalizedEmail,
                ["exact"] = "true"
            });

        using var usernameResponse = await SendAsync(HttpMethod.Get, usernameQuery, body: null, operation: UsersFindByUsernameOperation, ct);
        await EnsureSuccessAsync(usernameResponse, UsersFindByUsernameOperation, ct);

        var usernameRows = await usernameResponse.Content.ReadFromJsonAsync<KeycloakUserDto[]>(Json, ct) ?? [];
        var usernameMatch = FindUserByEmailOrUsername(usernameRows, normalizedEmail);

        return usernameMatch is null ? null : Map(usernameMatch);
    }

    public async Task<IReadOnlyDictionary<string, IdentityProviderUserDto>> FindUsersByEmailsAsync(
        IReadOnlyList<string> emails,
        CancellationToken ct)
    {
        if (emails is null)
            throw new NgbArgumentRequiredException(nameof(emails));

        var normalizedEmails = emails
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedEmails.Length == 0)
            return new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase);

        var rows = await ExecuteBoundedBatchAsync(normalizedEmails, FindUserByEmailAsync, ct);
        var result = new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < normalizedEmails.Length; i++)
        {
            if (rows[i] is { } row)
                result[normalizedEmails[i]] = row;
        }

        return result;
    }

    private async Task<TResult?[]> ExecuteBoundedBatchAsync<TInput, TResult>(
        IReadOnlyList<TInput> inputs,
        Func<TInput, CancellationToken, Task<TResult?>> action,
        CancellationToken ct)
        where TResult : class
    {
        var result = new TResult?[inputs.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, inputs.Count),
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = ResolveBatchConcurrency()
            },
            async (index, innerCt) => result[index] = await action(inputs[index], innerCt));

        return result;
    }

    public async Task SetTemporaryPasswordAsync(
        string identityProviderUserId,
        string temporaryPassword,
        bool requireUpdate,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identityProviderUserId))
            throw new NgbArgumentRequiredException(nameof(identityProviderUserId));

        if (string.IsNullOrWhiteSpace(temporaryPassword))
            throw new NgbArgumentRequiredException(nameof(temporaryPassword));

        var payload = new
        {
            type = "password",
            value = temporaryPassword,
            temporary = requireUpdate
        };

        using var response = await SendAsync(
            HttpMethod.Put,
            AdminPath($"users/{Uri.EscapeDataString(identityProviderUserId.Trim())}/reset-password"),
            payload,
            operation: UsersResetPasswordOperation,
            ct);

        await EnsureSuccessAsync(response, UsersResetPasswordOperation, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string pathOrUri,
        object? body,
        string operation,
        CancellationToken ct)
    {
        ValidateSettings();

        var token = await tokenCache.GetTokenAsync(ct);
        using var lease = await requestGate.AcquireAsync(ct);
        if (!lease.IsAcquired)
        {
            throw new KeycloakAdminClientException(
                operation,
                (int)HttpStatusCode.ServiceUnavailable,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["reason"] = "admin_request_queue_full",
                    ["maxConcurrentAdminRequests"] = settings.MaxConcurrentAdminRequests,
                    ["maxQueuedAdminRequests"] = settings.MaxQueuedAdminRequests
                });
        }

        using var request = new HttpRequestMessage(method, BuildUri(pathOrUri));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);

        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var statusCode = (int)response.StatusCode;
        string? errorCode = null;
        string? errorBody = null;

        var text = await ReadBoundedErrorBodyAsync(response.Content, ct);
        if (!string.IsNullOrWhiteSpace(text))
        {
            errorCode = "keycloak_error_body_present";
            errorBody = text;
        }

        var context = new Dictionary<string, object?>();
        if (errorCode is not null)
            context["keycloakError"] = errorCode;

        if (!string.IsNullOrWhiteSpace(errorBody))
            context["keycloakErrorBody"] = errorBody;

        throw new KeycloakAdminClientException(operation, statusCode, context.Count == 0 ? null : context);
    }

    private static async Task<string> ReadBoundedErrorBodyAsync(HttpContent content, CancellationToken ct)
    {
        const int maxChars = 512;
        var buffer = ArrayPool<char>.Shared.Rent(maxChars + 1);

        try
        {
            await using var stream = await content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: maxChars + 1,
                leaveOpen: false);

            var total = 0;
            while (total < maxChars)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(total, maxChars - total), ct);
                if (read == 0)
                    break;

                total += read;
            }

            return new string(buffer, 0, total);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private string BuildUri(string pathOrUri)
    {
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (pathOrUri.Contains("://", StringComparison.Ordinal))
            throw new NgbConfigurationViolationException("Keycloak Admin URI must use the http or https scheme.");

        return $"{settings.BaseUrl.TrimEnd('/')}/{pathOrUri.TrimStart('/')}";
    }

    private string AdminPath(string relative)
        => $"admin/realms/{Uri.EscapeDataString(settings.Realm.Trim())}/{relative.TrimStart('/')}";

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl)
            || string.IsNullOrWhiteSpace(settings.Realm)
            || string.IsNullOrWhiteSpace(settings.ClientId)
            || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new NgbConfigurationViolationException("Keycloak Admin client settings are required.");
        }
    }

    private int ResolveBatchConcurrency()
        => Math.Clamp(settings.AdminBatchConcurrency, MinAdminBatchConcurrency, MaxAdminBatchConcurrency);

    private static Dictionary<string, string[]>? BuildAttributes(string? displayName)
        => string.IsNullOrWhiteSpace(displayName)
            ? null
            : new Dictionary<string, string[]> { ["ngb_display_name"] = [displayName.Trim()] };

    private static IdentityProviderUserDto Map(KeycloakUserDto dto)
        => new(
            UserId: dto.Id,
            Email: dto.Email,
            FirstName: dto.FirstName,
            LastName: dto.LastName,
            DisplayName: ResolveDisplayName(dto),
            Enabled: dto.Enabled);

    private static string? ResolveDisplayName(KeycloakUserDto dto)
    {
        if (dto.Attributes is not null
            && dto.Attributes.TryGetValue("ngb_display_name", out var values)
            && values.FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x)) is { } displayName)
        {
            return displayName.Trim();
        }

        var fullName = string.Join(
            " ",
            new[] { dto.FirstName, dto.LastName }
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x!.Trim()));

        return string.IsNullOrWhiteSpace(fullName) ? dto.Email ?? dto.Username : fullName.Trim();
    }

    private static KeycloakUserDto? FindUserByEmailOrUsername(IEnumerable<KeycloakUserDto> rows, string email)
    {
        var list = rows.ToList();
        return list.FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(x => string.Equals(x.Username, email, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record KeycloakUserDto(
        string Id,
        string? Username,
        string? Email,
        string? FirstName,
        string? LastName,
        bool Enabled,
        Dictionary<string, string[]>? Attributes);

    private sealed record KeycloakUserWriteDto(
        string? Username,
        string? Email,
        string? FirstName,
        string? LastName,
        bool Enabled,
        bool? EmailVerified,
        IReadOnlyList<string>? RequiredActions,
        Dictionary<string, string[]>? Attributes);
}
