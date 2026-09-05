using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using FluentAssertions;
using NGB.Api.Models;
using NGB.Api.Sso;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class KeycloakAdminClientFullCoverageTests
{
    [Fact]
    public async Task User_lookup_cache_coalesces_requests_and_isolates_caller_cancellation()
    {
        var cache = new KeycloakUserLookupCache(Settings(), TimeProvider.System);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IdentityProviderUserDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<IdentityProviderUserDto?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            return await release.Task;
        }

        using var canceledCaller = new CancellationTokenSource();
        var canceled = cache.GetByIdAsync("user-1", Load, canceledCaller.Token);
        await started.Task;
        var healthy = cache.GetByIdAsync("user-1", Load, CancellationToken.None);

        canceledCaller.Cancel();
        await ((Func<Task>)(async () => await canceled)).Should().ThrowAsync<OperationCanceledException>();

        var expected = new IdentityProviderUserDto(
            "user-1", "User@Example.com", "First", "Last", "Display", true);
        release.SetResult(expected);

        (await healthy).Should().BeSameAs(expected);
        (await cache.GetByIdAsync(
            " user-1 ",
            _ => throw new Xunit.Sdk.XunitException("A cached id must not be reloaded."),
            default)).Should().BeSameAs(expected);
        (await cache.GetByEmailAsync(
            " USER@example.COM ",
            _ => throw new Xunit.Sdk.XunitException("A remembered email must not be reloaded."),
            default)).Should().BeSameAs(expected);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task User_lookup_cache_bounds_unique_pending_populations_but_still_coalesces_the_same_key()
    {
        var settings = Settings() with { MaxPendingUserLookups = 1 };
        var cache = new KeycloakUserLookupCache(settings, TimeProvider.System);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IdentityProviderUserDto?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IdentityProviderUserDto?> Load(CancellationToken _)
        {
            started.TrySetResult();
            return await release.Task;
        }

        var first = cache.GetByIdAsync("user-1", Load, default);
        await started.Task;
        var coalesced = cache.GetByIdAsync("user-1", Load, default);
        cache.PendingPopulationCount.Should().Be(1);

        var rejected = await ((Func<Task>)(async () =>
                await cache.GetByIdAsync("user-2", Load, default)))
            .Should().ThrowAsync<KeycloakAdminClientException>();
        rejected.Which.Context.Should()
            .Contain("reason", "pending_lookup_capacity_exceeded")
            .And.Contain("maxPendingUserLookups", 1);

        release.SetResult(null);
        (await first).Should().BeNull();
        (await coalesced).Should().BeNull();
        cache.PendingPopulationCount.Should().Be(0);
    }

    [Fact]
    public async Task Admin_request_gate_enforces_process_wide_concurrency_bounded_fifo_queue_and_cancellation()
    {
        var settings = Settings() with
        {
            MaxConcurrentAdminRequests = 1,
            MaxQueuedAdminRequests = 1
        };
        await using var gate = new KeycloakAdminRequestGate(settings);

        using var active = await gate.AcquireAsync(default);
        active.IsAcquired.Should().BeTrue();

        var queued = gate.AcquireAsync(default).AsTask();
        queued.IsCompleted.Should().BeFalse();

        using var rejected = await gate.AcquireAsync(default);
        rejected.IsAcquired.Should().BeFalse();

        active.Dispose();
        using var promoted = await queued;
        promoted.IsAcquired.Should().BeTrue();

        using var cancellation = new CancellationTokenSource();
        var canceled = gate.AcquireAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        await ((Func<Task>)(async () => await canceled)).Should().ThrowAsync<OperationCanceledException>();

        ((Action)(() => new KeycloakAdminRequestGate(null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Admin_client_rejects_when_the_shared_request_queue_is_full()
    {
        var settings = Settings() with
        {
            MaxConcurrentAdminRequests = 1,
            MaxQueuedAdminRequests = 0
        };
        await using var gate = new KeycloakAdminRequestGate(settings);
        using var active = await gate.AcquireAsync(default);
        var cache = new KeycloakUserLookupCache(settings, TimeProvider.System);
        var sut = new KeycloakAdminClient(
            new HttpClient(new RejectingHandler()),
            CachedTokenService(),
            cache,
            settings,
            gate);

        var error = await ((Func<Task>)(async () => await sut.GetUserByIdAsync("user-1", default)))
            .Should().ThrowAsync<KeycloakAdminClientException>();

        error.Which.Context.Should()
            .Contain("reason", "admin_request_queue_full")
            .And.Contain("maxConcurrentAdminRequests", 1)
            .And.Contain("maxQueuedAdminRequests", 0);
    }

    [Fact]
    public async Task User_lookup_cache_negative_entries_invalidation_and_bound_are_deterministic()
    {
        var settings = Settings() with { MaxCachedUserLookups = 100 };
        var cache = new KeycloakUserLookupCache(settings, TimeProvider.System);
        var missingCalls = 0;

        (await cache.GetByEmailAsync(
            "missing@example.com",
            _ => Task.FromResult<IdentityProviderUserDto?>(null),
            default)).Should().BeNull();
        (await cache.GetByEmailAsync(
            "MISSING@example.com",
            _ =>
            {
                Interlocked.Increment(ref missingCalls);
                return Task.FromResult<IdentityProviderUserDto?>(null);
            },
            default)).Should().BeNull();
        missingCalls.Should().Be(0);

        cache.InvalidateEmail("missing@example.com");
        (await cache.GetByEmailAsync(
            "missing@example.com",
            _ =>
            {
                Interlocked.Increment(ref missingCalls);
                return Task.FromResult<IdentityProviderUserDto?>(null);
            },
            default)).Should().BeNull();
        missingCalls.Should().Be(1);

        for (var i = 0; i < 101; i++)
        {
            await cache.GetByIdAsync(
                $"absent-{i}",
                _ => Task.FromResult<IdentityProviderUserDto?>(null),
                default);
        }

        var reloads = 0;
        await cache.GetByIdAsync(
            "absent-0",
            _ =>
            {
                Interlocked.Increment(ref reloads);
                return Task.FromResult<IdentityProviderUserDto?>(null);
            },
            default);
        reloads.Should().Be(1);

        var remembered = new IdentityProviderUserDto("remembered", "remembered@example.com", null, null, null, true);
        cache.Remember(remembered);
        cache.InvalidateUser("remembered");
        (await cache.GetByEmailAsync(
            "remembered@example.com",
            _ => Task.FromResult<IdentityProviderUserDto?>(null),
            default)).Should().BeNull();
        cache.InsertionMetadataCount.Should().BeLessThanOrEqualTo(settings.MaxCachedUserLookups);
    }

    [Fact]
    public void User_lookup_cache_repeated_refresh_and_invalidation_keep_eviction_metadata_bounded()
    {
        var cache = new KeycloakUserLookupCache(
            Settings() with { MaxCachedUserLookups = 100 },
            TimeProvider.System);

        for (var index = 0; index < 10_000; index++)
        {
            cache.Remember(new IdentityProviderUserDto(
                "same-user",
                "same@example.com",
                null,
                null,
                $"Display {index}",
                true));
        }

        cache.InsertionMetadataCount.Should().Be(2);
        cache.InvalidateUser("same-user");
        cache.InsertionMetadataCount.Should().Be(0);
    }

    [Fact]
    public void User_lookup_cache_reassigns_reverse_alias_when_an_email_is_reused()
    {
        var cache = new KeycloakUserLookupCache(Settings(), TimeProvider.System);
        var previous = new IdentityProviderUserDto("previous", "shared@example.com", null, null, null, true);
        var current = new IdentityProviderUserDto("current", "shared@example.com", null, null, null, true);

        cache.Remember(previous);
        cache.Remember(current);
        cache.InvalidateUser(previous.UserId);

        cache.TryGetByEmail(current.Email!, out var cached).Should().BeTrue();
        cached.Should().BeSameAs(current);

        cache.InvalidateUser(current.UserId);
        cache.TryGetByEmail(current.Email!, out _).Should().BeFalse();
    }

    [Fact]
    public async Task User_lookup_cache_expires_entries_skips_disabled_ttls_and_tolerates_stale_metadata()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var expiring = new KeycloakUserLookupCache(
            Settings() with
            {
                UserLookupCacheTtl = TimeSpan.FromSeconds(1),
                MissingUserCacheTtl = TimeSpan.FromSeconds(1)
            },
            clock);
        var user = new IdentityProviderUserDto("expiring", "expiring@example.com", null, null, "Initial", true);
        (await expiring.GetByIdAsync("expiring", _ => Task.FromResult<IdentityProviderUserDto?>(user), default))
            .Should().BeSameAs(user);
        clock.Advance(TimeSpan.FromSeconds(2));
        expiring.TryGetById("expiring", out _).Should().BeFalse();

        var disabled = new KeycloakUserLookupCache(
            Settings() with
            {
                UserLookupCacheTtl = TimeSpan.Zero,
                MissingUserCacheTtl = TimeSpan.Zero
            },
            clock);
        var loads = 0;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await disabled.GetByIdAsync("uncached", _ =>
            {
                loads++;
                return Task.FromResult<IdentityProviderUserDto?>(user);
            }, default);
        }
        loads.Should().Be(2);

        var resilient = new KeycloakUserLookupCache(Settings(), clock);
        var original = new IdentityProviderUserDto("stale", "stale@example.com", null, null, "Original", true);
        resilient.Remember(original);
        var entries = typeof(KeycloakUserLookupCache)
            .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resilient)!;
        var entryIndexer = entries.GetType().GetProperty("Item")!;
        var staleEntry = entryIndexer.GetValue(entries, ["id:stale"]);
        var current = original with { DisplayName = "Current" };
        resilient.Remember(current);
        Invoke<object?>(resilient, "RemoveLocked", "id:stale", staleEntry);
        resilient.TryGetById("stale", out var retained).Should().BeTrue();
        retained.Should().BeSameAs(current);

        var orderNodes = typeof(KeycloakUserLookupCache)
            .GetField("_orderNodes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resilient)!;
        orderNodes.GetType().GetMethod("Remove", [typeof(string)])!
            .Invoke(orderNodes, ["id:stale"]);
        resilient.InvalidateUser("stale");

        var orphan = new IdentityProviderUserDto("orphan", "orphan@example.com", null, null, null, true);
        resilient.Remember(orphan);
        var reverseAliases = typeof(KeycloakUserLookupCache)
            .GetField("_keysByUserId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resilient)!;
        reverseAliases.GetType().GetMethod("Remove", [typeof(string)])!
            .Invoke(reverseAliases, ["orphan"]);
        resilient.InvalidateEmail("orphan@example.com");
        resilient.TryGetByEmail("orphan@example.com", out _).Should().BeFalse();
    }

    [Fact]
    public async Task User_lookup_cache_does_not_remove_a_newer_pending_population_during_stale_cleanup()
    {
        var cache = new KeycloakUserLookupCache(Settings(), TimeProvider.System);
        var release = new TaskCompletionSource<IdentityProviderUserDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var original = cache.GetByIdAsync("user-1", _ => release.Task, default);
        var pendingField = typeof(KeycloakUserLookupCache)
            .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pending = (System.Collections.IDictionary)pendingField.GetValue(cache)!;
        var pendingType = pendingField.FieldType.GenericTypeArguments[1];
        var replacementFactory = (Func<Task<IdentityProviderUserDto?>>)(() =>
            Task.FromResult<IdentityProviderUserDto?>(null));
        var replacement = pendingType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single()
            .Invoke([replacementFactory]);

        pending["id:user-1"] = replacement;
        release.SetResult(new IdentityProviderUserDto(
            "user-1", "user-1@example.com", null, null, "User 1", true));

        (await original).Should().NotBeNull();
        cache.PendingPopulationCount.Should().Be(1);
        pending.Remove("id:user-1");
        cache.PendingPopulationCount.Should().Be(0);

        var secondRelease = new TaskCompletionSource<IdentityProviderUserDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var removedBeforeCleanup = cache.GetByIdAsync("user-2", _ => secondRelease.Task, default);
        pending.Remove("id:user-2");
        secondRelease.SetResult(null);

        (await removedBeforeCleanup).Should().BeNull();
        cache.PendingPopulationCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateUser_validates_request_sends_normalized_payload_and_loads_location_user()
    {
        var (sut, handler) = Client(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var created = Response(HttpStatusCode.Created);
                created.Headers.Location = new Uri("/admin/realms/test/users/user-1", UriKind.Relative);
                return created;
            }

            return Json(HttpStatusCode.OK, UserJson("user-1", attributes: "{\"ngb_display_name\":[\"  Display Name  \"]}"));
        });

        await ((Func<Task>)(() => sut.CreateUserAsync(null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        var user = await sut.CreateUserAsync(
            new CreateIdentityProviderUserRequest(
                "  User@Example.com  ", "First", "Last", " Display Name ", true, null, true),
            default);

        user.UserId.Should().Be("user-1");
        user.DisplayName.Should().Be("Display Name");
        var create = handler.Requests.First();
        create.Uri.Should().Be("https://keycloak.example/admin/realms/test/users");
        create.Authorization.Should().Be("Bearer cached-token");
        create.Body.Should().Contain("\"username\":\"User@Example.com\"")
            .And.Contain("\"requiredActions\":[\"UPDATE_PASSWORD\"]")
            .And.Contain("\"ngb_display_name\":[\"Display Name\"]");
    }

    [Fact]
    public async Task CreateUser_returns_existing_user_on_conflict_and_covers_empty_required_actions()
    {
        var (sut, handler) = Client(request => request.Method == HttpMethod.Post
            ? Response(HttpStatusCode.Conflict, "duplicate")
            : Json(HttpStatusCode.OK, $"[{UserJson("existing", email: "user@example.com")}]") );

        var user = await sut.CreateUserAsync(
            new CreateIdentityProviderUserRequest(
                "user@example.com", null, null, null, true, null, false),
            default);

        user.UserId.Should().Be("existing");
        handler.Requests.First().Body.Should().Contain("\"requiredActions\":[]")
            .And.NotContain("attributes");
    }

    [Fact]
    public async Task CreateUser_without_location_uses_lookup_or_throws_safe_configuration_errors()
    {
        var foundClient = Client(request => request.Method == HttpMethod.Post
            ? Response(HttpStatusCode.Created)
            : Json(HttpStatusCode.OK, $"[{UserJson("found", email: "found@example.com")}]")).Client;
        (await foundClient.CreateUserAsync(CreateRequest("found@example.com"), default)).UserId.Should().Be("found");

        var missingClient = Client(request => request.Method == HttpMethod.Post
            ? Response(HttpStatusCode.Created)
            : Json(HttpStatusCode.OK, "[]")).Client;
        await ((Func<Task>)(() => missingClient.CreateUserAsync(CreateRequest("missing@example.com"), default)))
            .Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*did not include a user id*");

        var unloadedClient = Client(request => request.Method == HttpMethod.Post
            ? CreatedAt("missing-user")
            : Response(HttpStatusCode.NotFound)).Client;
        await ((Func<Task>)(() => unloadedClient.CreateUserAsync(CreateRequest("unloaded@example.com"), default)))
            .Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*could not be loaded*");
    }

    [Fact]
    public async Task CreateUser_conflict_without_match_surfaces_keycloak_error()
    {
        var (sut, _) = Client(request => request.Method == HttpMethod.Post
            ? Response(HttpStatusCode.Conflict, "duplicate")
            : Json(HttpStatusCode.OK, "[]"));

        var error = await ((Func<Task>)(() => sut.CreateUserAsync(CreateRequest("missing@example.com"), default)))
            .Should().ThrowAsync<KeycloakAdminClientException>();

        error.Which.Context.Should().Contain("statusCode", 409)
            .And.Contain("keycloakError", "keycloak_error_body_present");
    }

    [Fact]
    public async Task Update_enable_and_password_operations_validate_inputs_and_send_expected_payloads()
    {
        var (sut, handler) = Client(_ => Response(HttpStatusCode.NoContent));

        await ((Func<Task>)(() => sut.UpdateUserAsync(" ", new UpdateIdentityProviderUserRequest(null, null, null, null, true), default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.UpdateUserAsync("id", null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.SetUserEnabledAsync("", true, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.SetTemporaryPasswordAsync(" ", "password", true, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.SetTemporaryPasswordAsync("id", " ", true, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        await sut.UpdateUserAsync(
            " user/id ",
            new UpdateIdentityProviderUserRequest(" updated@example.com ", "First", "Last", " ", false),
            default);
        await sut.UpdateUserAsync(
            "id-2",
            new UpdateIdentityProviderUserRequest(null, null, null, "Display", true),
            default);
        await sut.SetUserEnabledAsync(" id ", false, default);
        await sut.SetTemporaryPasswordAsync(" id ", "secret", true, default);

        handler.Requests.Should().HaveCount(4);
        handler.Requests[0].Uri.Should().Contain("users/user%2Fid");
        handler.Requests[0].Body.Should().Contain("\"email\":\"updated@example.com\"")
            .And.NotContain("attributes");
        handler.Requests[1].Body.Should().Contain("\"ngb_display_name\":[\"Display\"]");
        handler.Requests[2].Body.Should().Be("{\"enabled\":false}");
        handler.Requests[3].Uri.Should().EndWith("/users/id/reset-password");
        handler.Requests[3].Body.Should().Contain("\"type\":\"password\"")
            .And.Contain("\"temporary\":true");
    }

    [Fact]
    public async Task GetUser_covers_validation_not_found_null_and_every_display_name_fallback()
    {
        var blank = Client(_ => throw new Xunit.Sdk.XunitException("HTTP should not be called")).Client;
        await ((Func<Task>)(() => blank.GetUserByIdAsync(" ", default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        (await Client(_ => Response(HttpStatusCode.NotFound)).Client.GetUserByIdAsync("id", default)).Should().BeNull();
        (await Client(_ => Json(HttpStatusCode.OK, "null")).Client.GetUserByIdAsync("id", default)).Should().BeNull();

        var attribute = await GetMapped(UserJson("1", attributes: "{\"ngb_display_name\":[\" \",\" Attribute \"]}"));
        attribute.DisplayName.Should().Be("Attribute");
        (await GetMapped(UserJson("2", firstName: " Ada ", lastName: " Lovelace "))).DisplayName
            .Should().Be("Ada Lovelace");
        (await GetMapped(UserJson("3", username: "username", email: "mail@example.com", firstName: null, lastName: null,
            attributes: "{\"other\":[\"value\"]}"))).DisplayName.Should().Be("mail@example.com");
        (await GetMapped(UserJson("4", username: "username", email: null, firstName: null, lastName: null)))
            .DisplayName.Should().Be("username");
        (await GetMapped(UserJson("5", username: null, email: null, firstName: null, lastName: null)))
            .DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task Batch_get_validates_normalizes_deduplicates_and_skips_missing_users()
    {
        var (sut, handler) = Client(request => request.Uri.Contains("/users/id-1", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, UserJson("id-1"))
            : Response(HttpStatusCode.NotFound), settings: Settings(adminBatchConcurrency: 0));

        await ((Func<Task>)(() => sut.GetUsersByIdsAsync(null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetUsersByIdsAsync([" ", ""], default)).Should().BeEmpty();
        var result = await sut.GetUsersByIdsAsync([" id-1 ", "id-1", "missing", " "], default);

        result.Should().ContainSingle("id-1");
        handler.Requests.Should().HaveCount(2);
        handler.Requests.Should().OnlyContain(request => !request.Uri.Contains("first=", StringComparison.Ordinal));
        Invoke<int>(sut, "ResolveBatchConcurrency").Should().Be(1);
        Invoke<int>(Client(_ => Response(HttpStatusCode.OK), settings: Settings(100)).Client, "ResolveBatchConcurrency")
            .Should().Be(32);
        Invoke<int>(Client(_ => Response(HttpStatusCode.OK), settings: Settings(8)).Client, "ResolveBatchConcurrency")
            .Should().Be(8);
    }

    [Fact]
    public async Task Bulk_get_resolves_known_ids_directly_and_only_looks_up_unresolved_emails()
    {
        var (sut, handler) = Client(request =>
        {
            if (request.Uri.Contains("/users/target-id", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, UserJson("target-id", email: "TARGET@example.com"));

            if (request.Uri.Contains("/users/missing-id", StringComparison.Ordinal))
                return Response(HttpStatusCode.NotFound);

            if (request.Uri.Contains("/users/unrequested-email", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, UserJson("unrequested-email", email: "other@example.com"));

            if (request.Uri.Contains("/users/no-email", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, UserJson("no-email", email: null));

            if (request.Uri.Contains("email=lookup", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, $"[{UserJson("lookup-id", email: "lookup@example.com") }]");

            throw new Xunit.Sdk.XunitException($"Unexpected Keycloak request: {request.Uri}");
        });

        await ((Func<Task>)(() => sut.GetUsersAsync(null!, [], default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.GetUsersAsync([], null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetUsersAsync([" "], [""], default)).ById.Should().BeEmpty();
        var emailOnly = await sut.GetUsersAsync([], [" lookup@example.com "], default);

        var result = await sut.GetUsersAsync(
            [" target-id ", "target-id", "missing-id", "unrequested-email", "no-email"],
            [" target@example.com ", "TARGET@example.com", "lookup@example.com"],
            default);

        emailOnly.ByEmail.Should().ContainSingle("lookup@example.com");
        result.ById.Should().ContainKeys("target-id", "unrequested-email", "no-email")
            .And.HaveCount(3);
        result.ByEmail.Should().ContainKeys("target@example.com", "lookup@example.com");
        handler.Requests.Should().HaveCount(5);
        handler.Requests.Should().Contain(request => request.Uri.Contains("/users/target-id", StringComparison.Ordinal));
        handler.Requests.Should().Contain(request => request.Uri.Contains("/users/missing-id", StringComparison.Ordinal));
        handler.Requests.Should().Contain(request => request.Uri.Contains("/users/unrequested-email", StringComparison.Ordinal));
        handler.Requests.Should().Contain(request => request.Uri.Contains("/users/no-email", StringComparison.Ordinal));
        handler.Requests.Should().Contain(request => request.Uri.Contains("email=lookup", StringComparison.Ordinal));
        handler.Requests.Should().OnlyContain(request => !request.Uri.Contains("first=", StringComparison.Ordinal));
    }

    [Fact]
    public void Cached_page_snapshot_never_calls_Keycloak_and_normalizes_lookup_keys()
    {
        var settings = Settings();
        var cache = new KeycloakUserLookupCache(settings, TimeProvider.System);
        var cached = new IdentityProviderUserDto(
            "cached-id", "Cached@Example.com", null, null, "Cached", false);
        cache.Remember(cached);
        var handler = new RecordingHandler(_ =>
            throw new Xunit.Sdk.XunitException("A cached page snapshot must not issue HTTP requests."));
        var sut = new KeycloakAdminClient(
            new HttpClient(handler), CachedTokenService(), cache, settings, new KeycloakAdminRequestGate(settings));

        var snapshot = sut.GetCachedUsers(
            [" cached-id ", "cached-id", "missing", " "],
            [" cached@example.com ", "CACHED@example.com", "missing@example.com"]);

        snapshot.ById.Should().ContainSingle("cached-id");
        snapshot.ById["cached-id"].Should().BeSameAs(cached);
        snapshot.ByEmail.Should().ContainSingle("cached@example.com");
        snapshot.ByEmail["cached@example.com"].Should().BeSameAs(cached);
        handler.Requests.Should().BeEmpty();
        ((Action)(() => sut.GetCachedUsers(null!, []))).Should().Throw<ArgumentNullException>();
        ((Action)(() => sut.GetCachedUsers([], null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task FindUser_covers_email_match_username_fallback_null_arrays_and_no_match()
    {
        var direct = Client(_ => Json(HttpStatusCode.OK,
            $"[{UserJson("email-id", email: "USER@example.com")}]"));
        await ((Func<Task>)(() => direct.Client.FindUserByEmailAsync(" ", default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await direct.Client.FindUserByEmailAsync(" user@example.com ", default))!.UserId.Should().Be("email-id");
        direct.Handler.Requests.Should().ContainSingle();

        var username = Client(request => request.Uri.Contains("username=", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, $"[{UserJson("username-id", username: "user@example.com", email: null)}]")
            : Json(HttpStatusCode.OK, "null"));
        (await username.Client.FindUserByEmailAsync("user@example.com", default))!.UserId.Should().Be("username-id");

        var missing = Client(_ => Json(HttpStatusCode.OK, "[]"));
        (await missing.Client.FindUserByEmailAsync("missing@example.com", default)).Should().BeNull();
        missing.Handler.Requests.Should().HaveCount(2);

        var nullUsernameRows = Client(request => request.Uri.Contains("username=", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, "null")
            : Json(HttpStatusCode.OK, "[]"));
        (await nullUsernameRows.Client.FindUserByEmailAsync("missing@example.com", default)).Should().BeNull();
    }

    [Fact]
    public async Task Batch_find_validates_normalizes_deduplicates_and_keeps_case_insensitive_keys()
    {
        var (sut, handler) = Client(request =>
        {
            var email = request.Uri.Contains("first", StringComparison.OrdinalIgnoreCase)
                ? "first@example.com"
                : "missing@example.com";
            return email == "first@example.com"
                ? Json(HttpStatusCode.OK, $"[{UserJson("first-id", email: email)}]")
                : Json(HttpStatusCode.OK, "[]");
        }, settings: Settings(2));

        await ((Func<Task>)(() => sut.FindUsersByEmailsAsync(null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.FindUsersByEmailsAsync([" ", ""], default)).Should().BeEmpty();
        var result = await sut.FindUsersByEmailsAsync(
            [" first@example.com ", "FIRST@example.com", "missing@example.com"], default);

        result.Should().ContainSingle("first@example.com");
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Error_handling_covers_empty_short_and_truncated_bodies()
    {
        var empty = Client(_ => Response(HttpStatusCode.ServiceUnavailable, " ")).Client;
        var emptyError = await ((Func<Task>)(() => empty.GetUserByIdAsync("id", default)))
            .Should().ThrowAsync<KeycloakAdminClientException>();
        emptyError.Which.Context.Should().NotContainKey("keycloakError");

        var shortBody = Client(_ => Response(HttpStatusCode.BadGateway, "safe-body")).Client;
        var shortError = await ((Func<Task>)(() => shortBody.GetUserByIdAsync("id", default)))
            .Should().ThrowAsync<KeycloakAdminClientException>();
        shortError.Which.Context.Should().Contain("keycloakErrorBody", "safe-body");

        var longText = new string('x', 600);
        var longBody = Client(_ => Response(HttpStatusCode.BadGateway, longText)).Client;
        var longError = await ((Func<Task>)(() => longBody.GetUserByIdAsync("id", default)))
            .Should().ThrowAsync<KeycloakAdminClientException>();
        ((string)longError.Which.Context["keycloakErrorBody"]!).Should().HaveLength(512);

    }

    [Fact]
    public async Task Settings_validation_rejects_each_missing_required_value_and_uri_builder_handles_absolute_paths()
    {
        foreach (var settings in new[]
                 {
                     Settings() with { BaseUrl = " " },
                     Settings() with { Realm = "" },
                     Settings() with { ClientId = " " },
                     Settings() with { ClientSecret = "" }
                 })
        {
            var sut = Client(_ => Response(HttpStatusCode.OK), settings: settings).Client;
            await ((Func<Task>)(() => sut.GetUserByIdAsync("id", default)))
                .Should().ThrowAsync<NgbConfigurationViolationException>();
        }

        var valid = Client(_ => Response(HttpStatusCode.OK)).Client;
        Invoke<string>(valid, "BuildUri", "http://other.example/path").Should().Be("http://other.example/path");
        Invoke<string>(valid, "BuildUri", "https://other.example/path").Should().Be("https://other.example/path");
        Action unsupportedScheme = () => Invoke<string>(valid, "BuildUri", "ftp://other.example/path");
        unsupportedScheme.Should().Throw<TargetInvocationException>()
            .WithInnerException<NgbConfigurationViolationException>();
        Invoke<string>(valid, "BuildUri", "/relative").Should().Be("https://keycloak.example/relative");
    }

    private static async Task<IdentityProviderUserDto> GetMapped(string json)
        => (await Client(_ => Json(HttpStatusCode.OK, json)).Client.GetUserByIdAsync("id", default))!;

    private static CreateIdentityProviderUserRequest CreateRequest(string email)
        => new(email, null, null, null, true, null, false);

    private static string UserJson(
        string id,
        string? username = "username",
        string? email = "user@example.com",
        string? firstName = "First",
        string? lastName = "Last",
        string? attributes = null)
        => $$"""
             {
               "id": {{JsonString(id)}},
               "username": {{JsonString(username)}},
               "email": {{JsonString(email)}},
               "firstName": {{JsonString(firstName)}},
               "lastName": {{JsonString(lastName)}},
               "enabled": true,
               "attributes": {{attributes ?? "null"}}
             }
             """;

    private static string JsonString(string? value)
        => value is null ? "null" : $"\"{value}\"";

    private static HttpResponseMessage CreatedAt(string userId)
    {
        var response = Response(HttpStatusCode.Created);
        response.Headers.Location = new Uri($"/admin/realms/test/users/{userId}", UriKind.Relative);
        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => Response(status, json, "application/json");

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string? body = null,
        string mediaType = "text/plain")
        => new(status)
        {
            Content = body is null ? null : new StringContent(body, Encoding.UTF8, mediaType)
        };

    private static KeycloakAdminClientSettings Settings(int adminBatchConcurrency = 8)
        => new()
        {
            BaseUrl = "https://keycloak.example/",
            Realm = " test ",
            ClientId = "client",
            ClientSecret = "secret",
            AdminBatchConcurrency = adminBatchConcurrency
        };

    private static ClientFixture Client(
        Func<RecordedRequest, HttpResponseMessage> response,
        KeycloakAdminClientSettings? settings = null)
    {
        var handler = new RecordingHandler(response);
        var client = new HttpClient(handler);
        var effectiveSettings = settings ?? Settings();
        var cache = new KeycloakUserLookupCache(effectiveSettings, TimeProvider.System);

        return new ClientFixture(
            new KeycloakAdminClient(
                client,
                CachedTokenService(),
                cache,
                effectiveSettings,
                new KeycloakAdminRequestGate(effectiveSettings)),
            handler);
    }

    private static TokenCacheService CachedTokenService()
    {
        var service = new TokenCacheService(
            new HttpClient(new RejectingHandler()),
            new KeycloakApiClientSettings("https://keycloak.example", "test", "client", "secret"),
            TimeProvider.System);
        typeof(TokenCacheService).GetField("_cacheEntry", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, new TokenCacheService.TokenCacheEntry("cached-token", DateTime.UtcNow.AddDays(1)));

        return service;
    }

    private static T Invoke<T>(object target, string methodName, params object?[] arguments)
        => (T)(target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, arguments)!);

    private sealed record ClientFixture(KeycloakAdminClient Client, RecordingHandler Handler)
    {
        public void Deconstruct(out KeycloakAdminClient client, out RecordingHandler handler)
        {
            client = Client;
            handler = Handler;
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string? Body);

    private sealed class RecordingHandler(Func<RecordedRequest, HttpResponseMessage> response) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<RecordedRequest> _requests = new();

        public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            _requests.Enqueue(recorded);
            return response(recorded);
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new Xunit.Sdk.XunitException("Token endpoint should not be called while token is cached.");
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }

}
