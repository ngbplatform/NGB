using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters.Contracts;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Posting;

public sealed class DocumentRegisterPostingResolversFullCoverageTests
{
    [Fact]
    public void ConstructorsAndLookup_CoverNullMissingAndUnboundDefinitions()
    {
        Action opNullDefinitions = () => _ = new DefinitionsDocumentOperationalRegisterPostingActionResolver(null!, []);
        Action opNullHandlers = () => _ = new DefinitionsDocumentOperationalRegisterPostingActionResolver(Definitions(), null!);
        Action referenceNullDefinitions = () => _ = new DefinitionsDocumentReferenceRegisterPostingActionResolver(null!, []);
        Action referenceNullHandlers = () => _ = new DefinitionsDocumentReferenceRegisterPostingActionResolver(Definitions(), null!);
        opNullDefinitions.Should().Throw<NgbArgumentRequiredException>();
        opNullHandlers.Should().Throw<NgbArgumentRequiredException>();
        referenceNullDefinitions.Should().Throw<NgbArgumentRequiredException>();
        referenceNullHandlers.Should().Throw<NgbArgumentRequiredException>();

        var definitions = Definitions(("plain", null, null));
        var op = new DefinitionsDocumentOperationalRegisterPostingActionResolver(definitions, []);
        var reference = new DefinitionsDocumentReferenceRegisterPostingActionResolver(definitions, []);
        Action opNullDocument = () => op.TryResolve(null!);
        Action referenceNullDocument = () => reference.TryResolve(null!);
        opNullDocument.Should().Throw<NgbArgumentRequiredException>();
        referenceNullDocument.Should().Throw<NgbArgumentRequiredException>();
        op.TryResolve(Document("missing")).Should().BeNull();
        reference.TryResolve(Document("missing")).Should().BeNull();
        op.TryResolve(Document("plain")).Should().BeNull();
        reference.TryResolve(Document("plain")).Should().BeNull();
    }

    [Fact]
    public async Task OperationalResolver_CoversInvalidMissingMultipleMismatchDelegateAndCache()
    {
        var unnamedType = typeof(GenericHolder<>).GetGenericArguments()[0];
        AssertMisconfigured(() => new DefinitionsDocumentOperationalRegisterPostingActionResolver(
            Definitions(("doc", unnamedType, null)), []).TryResolve(Document()), "must implement");
        AssertMisconfigured(() => new DefinitionsDocumentOperationalRegisterPostingActionResolver(
            Definitions(("doc", typeof(NotAHandler), null)), []).TryResolve(Document()), "must implement");
        AssertMisconfigured(() => new DefinitionsDocumentOperationalRegisterPostingActionResolver(
            Definitions(("doc", typeof(OpHandler), null)), []).TryResolve(Document()), "not registered");
        AssertMisconfigured(() => new DefinitionsDocumentOperationalRegisterPostingActionResolver(
            Definitions(("doc", typeof(OpHandler), null)), [new OpHandler("doc"), new OpHandler("doc")])
            .TryResolve(Document()), "Multiple");
        AssertMisconfigured(() => new DefinitionsDocumentOperationalRegisterPostingActionResolver(
            Definitions(("doc", typeof(OpHandler), null)), [new OpHandler("other")])
            .TryResolve(Document()), "does not match");

        var handler = new OpHandler("doc");
        var valid = new DefinitionsDocumentOperationalRegisterPostingActionResolver(
            Definitions(("doc", typeof(OpHandler), null)), [handler]);
        var first = valid.TryResolve(Document());
        var second = valid.TryResolve(Document("DOC"));
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        await first!(Mock.Of<IOperationalRegisterMovementsBuilder>(), default);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ReferenceResolver_CoversInvalidMissingMultipleMismatchDelegateOperationAndCache()
    {
        var unnamedType = typeof(GenericHolder<>).GetGenericArguments()[0];
        AssertMisconfigured(() => new DefinitionsDocumentReferenceRegisterPostingActionResolver(
            Definitions(("doc", null, unnamedType)), []).TryResolve(Document()), "must implement");
        AssertMisconfigured(() => new DefinitionsDocumentReferenceRegisterPostingActionResolver(
            Definitions(("doc", null, typeof(NotAHandler))), []).TryResolve(Document()), "must implement");
        AssertMisconfigured(() => new DefinitionsDocumentReferenceRegisterPostingActionResolver(
            Definitions(("doc", null, typeof(ReferenceHandler))), []).TryResolve(Document()), "not registered");
        AssertMisconfigured(() => new DefinitionsDocumentReferenceRegisterPostingActionResolver(
            Definitions(("doc", null, typeof(ReferenceHandler))),
            [new ReferenceHandler("doc"), new ReferenceHandler("doc")]).TryResolve(Document()), "Multiple");
        AssertMisconfigured(() => new DefinitionsDocumentReferenceRegisterPostingActionResolver(
            Definitions(("doc", null, typeof(ReferenceHandler))), [new ReferenceHandler("other")])
            .TryResolve(Document()), "does not match");

        var handler = new ReferenceHandler("doc");
        var valid = new DefinitionsDocumentReferenceRegisterPostingActionResolver(
            Definitions(("doc", null, typeof(ReferenceHandler))), [handler]);
        var first = valid.TryResolve(Document());
        var second = valid.TryResolve(Document("DOC"));
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        await first!(Mock.Of<IReferenceRegisterRecordsBuilder>(), ReferenceRegisterWriteOperation.Repost, default);
        handler.Calls.Should().Be(1);
        handler.LastOperation.Should().Be(ReferenceRegisterWriteOperation.Repost);
    }

    [Fact]
    public async Task OperationalResolver_WaitingCallerUsesValueCachedInsideLock()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var handler = new BlockingOpHandler("doc", entered, release);
        var resolver = new DefinitionsDocumentOperationalRegisterPostingActionResolver(
            Definitions(("doc", typeof(BlockingOpHandler), null)), [handler]);

        var first = Task.Run(() => resolver.TryResolve(Document()));
        entered.Wait();
        var second = Task.Run(() => resolver.TryResolve(Document()));
        await Task.Delay(25);
        release.Set();

        (await first).Should().NotBeNull();
        (await second).Should().NotBeNull();
    }

    [Fact]
    public async Task ReferenceResolver_WaitingCallerUsesValueCachedInsideLock()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var handler = new BlockingReferenceHandler("doc", entered, release);
        var resolver = new DefinitionsDocumentReferenceRegisterPostingActionResolver(
            Definitions(("doc", null, typeof(BlockingReferenceHandler))), [handler]);

        var first = Task.Run(() => resolver.TryResolve(Document()));
        entered.Wait();
        var second = Task.Run(() => resolver.TryResolve(Document()));
        await Task.Delay(25);
        release.Set();

        (await first).Should().NotBeNull();
        (await second).Should().NotBeNull();
    }

    private static void AssertMisconfigured(Action action, string reason)
        => action.Should().Throw<DocumentPostingHandlerMisconfiguredException>()
            .Which.Message.Should().Contain(reason);

    private static DefinitionsRegistry Definitions(
        params (string Code, Type? Operational, Type? Reference)[] values)
    {
        var builder = new DefinitionsBuilder();
        foreach (var (code, operational, reference) in values)
        {
            builder.AddDocument(code, document =>
            {
                document.Metadata(new DocumentTypeMetadata(code, []));
                if (operational is not null)
                    document.OperationalRegisterPostingHandler(operational);
                if (reference is not null)
                    document.ReferenceRegisterPostingHandler(reference);
            });
        }

        return builder.Build();
    }

    private static DocumentRecord Document(string code = "doc") => new()
    {
        Id = Guid.NewGuid(),
        TypeCode = code,
        DateUtc = DateTime.UtcNow,
        Status = DocumentStatus.Draft,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private sealed class NotAHandler;
    private sealed class GenericHolder<T>;

    private class OpHandler(string code) : IDocumentOperationalRegisterPostingHandler
    {
        public virtual string TypeCode => code;
        public int Calls { get; private set; }

        public Task BuildMovementsAsync(
            DocumentRecord document,
            IOperationalRegisterMovementsBuilder builder,
            CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingOpHandler(
        string code,
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : OpHandler(code)
    {
        public override string TypeCode
        {
            get
            {
                entered.Set();
                release.Wait();
                return base.TypeCode;
            }
        }
    }

    private class ReferenceHandler(string code) : IDocumentReferenceRegisterPostingHandler
    {
        public virtual string TypeCode => code;
        public int Calls { get; private set; }
        public ReferenceRegisterWriteOperation? LastOperation { get; private set; }

        public Task BuildRecordsAsync(
            DocumentRecord document,
            ReferenceRegisterWriteOperation operation,
            IReferenceRegisterRecordsBuilder builder,
            CancellationToken ct)
        {
            Calls++;
            LastOperation = operation;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingReferenceHandler(
        string code,
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : ReferenceHandler(code)
    {
        public override string TypeCode
        {
            get
            {
                entered.Set();
                release.Wait();
                return base.TypeCode;
            }
        }
    }
}
