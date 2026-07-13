using ECommerceStore.Tests.TestInfrastructure;
using ECommerceStore.Web.Data;
using ECommerceStore.Web.Models.Assistant;
using ECommerceStore.Web.Models.Catalog;
using ECommerceStore.Web.Models.Identity;
using ECommerceStore.Web.Services.Assistant;
using ECommerceStore.Web.Services.Assistant.Provider;
using ECommerceStore.Web.Services.Catalog;
using ECommerceStore.Web.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECommerceStore.Tests.Assistant;

public sealed class ShoppingAssistantServiceTests
{
    [Fact]
    public async Task Completed_request_replays_without_duplicate_messages_or_provider_calls()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        await using var context = database.CreateContext();
        var provider = new CountingProvider("Grounded answer");
        var service = CreateService(context, provider);
        var clientRequestId = Guid.NewGuid();

        var first = await service.SendAsync(setup.UserId, setup.ConversationId, clientRequestId, "Find a lamp");
        var second = await service.SendAsync(setup.UserId, setup.ConversationId, clientRequestId, "Find a lamp");

        Assert.Equal(AssistantSendStatus.Completed, first.Status);
        Assert.Equal(first.Message?.Id, second.Message?.Id);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(2, await context.ChatMessages.CountAsync());
        Assert.Single(await context.AssistantRequests.ToListAsync());
        Assert.Equal("Find a lamp", await context.ChatConversations.Select(conversation => conversation.Title).SingleAsync());
    }

    [Fact]
    public async Task Fresh_processing_duplicate_returns_processing_and_does_not_invoke_second_provider()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        var blocking = new BlockingProvider("worker A");
        await using var firstContext = database.CreateContext();
        var firstService = CreateService(firstContext, blocking);
        var requestId = Guid.NewGuid();
        var firstTask = firstService.SendAsync(setup.UserId, setup.ConversationId, requestId, "Find a lamp");
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var duplicateContext = database.CreateContext();
        var duplicateProvider = new CountingProvider("should not run");
        var duplicate = await CreateService(duplicateContext, duplicateProvider)
            .SendAsync(setup.UserId, setup.ConversationId, requestId, "Find a lamp");

        Assert.Equal(AssistantSendStatus.Processing, duplicate.Status);
        Assert.Equal(0, duplicateProvider.CallCount);
        blocking.Release.TrySetResult();
        Assert.Equal(AssistantSendStatus.Completed, (await firstTask).Status);
    }

    [Fact]
    public async Task Stale_worker_cannot_complete_after_new_attempt_takes_ownership()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        var requestId = Guid.NewGuid();
        var workerAProvider = new BlockingProvider("stale A");
        await using var workerAContext = database.CreateContext();
        var workerA = CreateService(workerAContext, workerAProvider, leaseSeconds: 1);
        var workerATask = workerA.SendAsync(setup.UserId, setup.ConversationId, requestId, "Find a lamp");
        await workerAProvider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Guid originalAttempt;
        await using (var ageContext = database.CreateContext())
        {
            var request = await ageContext.AssistantRequests.SingleAsync();
            originalAttempt = request.ProcessingAttemptId;
            await ageContext.AssistantRequests.Where(item => item.Id == request.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.UpdatedAtUtc, DateTime.UtcNow.AddMinutes(-5)));
        }

        await using var workerBContext = database.CreateContext();
        var workerBProvider = new CountingProvider("authoritative B");
        var workerB = CreateService(workerBContext, workerBProvider, leaseSeconds: 1);
        var workerBResult = await workerB.SendAsync(setup.UserId, setup.ConversationId, requestId, "Find a lamp");
        workerAProvider.Release.TrySetResult();
        var workerAResult = await workerATask;

        Assert.Equal(AssistantSendStatus.Completed, workerBResult.Status);
        Assert.Equal(workerBResult.Message?.Id, workerAResult.Message?.Id);
        await using var verify = database.CreateContext();
        var storedRequest = await verify.AssistantRequests.SingleAsync();
        Assert.NotEqual(originalAttempt, storedRequest.ProcessingAttemptId);
        Assert.Equal(AssistantRequestState.Completed, storedRequest.State);
        var assistantMessage = Assert.Single(await verify.ChatMessages.Where(message => message.Role == ChatMessageRole.Assistant).ToListAsync());
        Assert.Equal("authoritative B", assistantMessage.Content);
        Assert.Equal(2, await verify.ChatMessages.CountAsync());
    }

    [Fact]
    public async Task Concurrent_messages_receive_unique_sequences()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var first = CreateService(firstContext, new CountingProvider("first reply"));
        var second = CreateService(secondContext, new CountingProvider("second reply"));

        var results = await Task.WhenAll(
            first.SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "first prompt"),
            second.SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "second prompt"));

        Assert.All(results, result => Assert.Equal(AssistantSendStatus.Completed, result.Status));
        await using var verify = database.CreateContext();
        var sequences = await verify.ChatMessages.OrderBy(message => message.Sequence).Select(message => message.Sequence).ToListAsync();
        Assert.Equal(4, sequences.Count);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, sequences);
        Assert.Equal(sequences.Count, sequences.Distinct().Count());
    }

    [Fact]
    public async Task History_reloads_live_products_and_hides_inactive_product_details()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        await using var context = database.CreateContext();
        var service = CreateService(context, new MockAssistantAiClient());
        var result = await service.SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "Show electronics");
        var liveCard = Assert.Single(result.Message!.Products);
        Assert.False(liveCard.IsUnavailable);

        await using (var update = database.CreateContext())
        {
            var product = await update.Products.SingleAsync();
            product.NormalPrice = 45m;
            await update.SaveChangesAsync();
        }
        var repriced = await service.GetConversationAsync(setup.UserId, setup.ConversationId);
        Assert.Equal(45m, repriced!.Messages.Last().Products.Single().Price);

        await using (var update = database.CreateContext())
        {
            var product = await update.Products.SingleAsync();
            product.IsActive = false;
            await update.SaveChangesAsync();
        }
        var unavailable = await service.GetConversationAsync(setup.UserId, setup.ConversationId);
        var hiddenCard = unavailable!.Messages.Last().Products.Single();
        Assert.True(hiddenCard.IsUnavailable);
        Assert.Equal("Desk Lamp", hiddenCard.Name);
        Assert.Null(hiddenCard.ProductId);
        Assert.Null(hiddenCard.Price);
        Assert.Null(hiddenCard.ProductUrl);
    }

    [Fact]
    public async Task Multi_turn_reference_reloads_current_product_before_stock_answer()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        await using var context = database.CreateContext();
        var service = CreateService(context, new MockAssistantAiClient());
        await service.SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "Show electronics");

        await using (var update = database.CreateContext())
        {
            var product = await update.Products.SingleAsync();
            product.StockQuantity = 2;
            await update.SaveChangesAsync();
        }
        var followup = await service.SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "Does the first product still have stock?");

        Assert.Equal(AssistantSendStatus.Completed, followup.Status);
        Assert.Contains("2 available", followup.Message!.Content);
        Assert.Equal(2, followup.Message.Products.Single().StockQuantity);
    }

    [Fact]
    public async Task Foreign_currency_short_circuits_without_provider_or_price_query()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        await using var context = database.CreateContext();
        var provider = new CountingProvider("should not run");
        var result = await CreateService(context, provider)
            .SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "Show products under 2.000 TL");

        Assert.Equal(AssistantSendStatus.Completed, result.Status);
        Assert.Equal(0, provider.CallCount);
        Assert.Contains("USD", result.Message!.Content);
        Assert.Contains("TRY", result.Message.Content);
        Assert.Empty(result.Message.Products);
    }

    [Fact]
    public async Task Failed_request_retries_with_new_attempt_without_duplicating_user_message()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        await using var context = database.CreateContext();
        var provider = new FlakyProvider();
        var service = CreateService(context, provider);
        var requestId = Guid.NewGuid();

        var failed = await service.SendAsync(setup.UserId, setup.ConversationId, requestId, "Find a lamp");
        var firstAttempt = (await context.AssistantRequests.AsNoTracking().SingleAsync()).ProcessingAttemptId;
        var completed = await service.SendAsync(setup.UserId, setup.ConversationId, requestId, "Find a lamp");
        var finalRequest = await context.AssistantRequests.AsNoTracking().SingleAsync();

        Assert.Equal(AssistantSendStatus.Failed, failed.Status);
        Assert.Equal(AssistantSendStatus.Completed, completed.Status);
        Assert.NotEqual(firstAttempt, finalRequest.ProcessingAttemptId);
        Assert.Equal(2, provider.CallCount);
        Assert.Equal(2, await context.ChatMessages.CountAsync());
        Assert.Single(await context.ChatMessages.Where(message => message.Role == ChatMessageRole.User).ToListAsync());
    }

    [Fact]
    public async Task Provider_wait_holds_no_transaction_and_other_conversation_completes_independently()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        Guid secondConversationId;
        await using (var add = database.CreateContext())
        {
            var conversation = new ChatConversation { Id = Guid.NewGuid(), UserId = setup.UserId };
            add.Add(conversation);
            await add.SaveChangesAsync();
            secondConversationId = conversation.Id;
        }

        var blocking = new BlockingProvider("first conversation");
        await using var firstContext = database.CreateContext();
        var firstTask = CreateService(firstContext, blocking)
            .SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "wait here");
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var secondContext = database.CreateContext();
        var secondTask = CreateService(secondContext, new CountingProvider("second conversation"))
            .SendAsync(setup.UserId, secondConversationId, Guid.NewGuid(), "finish now");
        var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AssistantSendStatus.Completed, second.Status);
        blocking.Release.TrySetResult();
        Assert.Equal(AssistantSendStatus.Completed, (await firstTask).Status);
    }

    [Fact]
    public async Task Hallucinated_product_id_never_becomes_a_card()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        await using var context = database.CreateContext();
        var result = await CreateService(context, new HallucinatedProductProvider())
            .SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "show invented product");
        Assert.Equal(AssistantSendStatus.Completed, result.Status);
        Assert.Empty(result.Message!.Products);
        Assert.Empty(await context.ChatMessageProducts.ToListAsync());
    }

    [Fact]
    public async Task Tool_loop_is_bounded_and_persists_no_partial_assistant_result()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        await using var context = database.CreateContext();
        var provider = new EndlessToolProvider();
        var result = await CreateService(context, provider)
            .SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "keep searching");
        Assert.Equal(AssistantSendStatus.Failed, result.Status);
        Assert.Equal(4, provider.CallCount);
        Assert.Single(await context.ChatMessages.ToListAsync());
        Assert.Empty(await context.ChatMessageProducts.ToListAsync());
    }

    [Fact]
    public async Task Conversations_are_owner_scoped_and_cascade_on_delete()
    {
        await using var database = await SqliteFileTestDatabase.CreateAsync();
        var setup = await SeedAsync(database);
        await using var context = database.CreateContext();
        var service = CreateService(context, new CountingProvider("answer"));
        await service.SendAsync(setup.UserId, setup.ConversationId, Guid.NewGuid(), "hello");

        Assert.Null(await service.GetConversationAsync("foreign-user", setup.ConversationId));
        Assert.False(await service.DeleteConversationAsync("foreign-user", setup.ConversationId));
        Assert.True(await service.DeleteConversationAsync(setup.UserId, setup.ConversationId));
        Assert.Empty(await context.ChatMessages.ToListAsync());
        Assert.Empty(await context.AssistantRequests.ToListAsync());
    }

    private static ShoppingAssistantService CreateService(ApplicationDbContext db, IAssistantAiClient provider, int leaseSeconds = 30)
    {
        var store = Options.Create(new StoreOptions { CurrencyCode = "USD" });
        var productQueries = new ProductQueryService(db, store);
        var tools = new ProductAssistantToolService(productQueries);
        return new ShoppingAssistantService(db, provider, tools, productQueries,
            Options.Create(new AssistantOptions { Provider = "Mock", ProcessingLeaseSeconds = leaseSeconds }), store,
            NullLogger<ShoppingAssistantService>.Instance);
    }

    private static async Task<Setup> SeedAsync(SqliteFileTestDatabase database)
    {
        await using var db = database.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(), UserName = "assistant@test.local", NormalizedUserName = "ASSISTANT@TEST.LOCAL",
            Email = "assistant@test.local", NormalizedEmail = "ASSISTANT@TEST.LOCAL", FirstName = "Test", LastName = "User"
        };
        var category = new Category { Name = "Electronics", NormalizedName = "ELECTRONICS", Slug = "electronics" };
        var product = new Product
        {
            Category = category, Name = "Desk Lamp", Slug = "desk-lamp", ShortDescription = "Warm task light",
            FullDescription = "A lamp description that is untrusted assistant input.", NormalPrice = 35m, StockQuantity = 5
        };
        var conversation = new ChatConversation { Id = Guid.NewGuid(), User = user, UserId = user.Id };
        db.AddRange(user, category, product, conversation);
        await db.SaveChangesAsync();
        return new Setup(user.Id, conversation.Id);
    }

    private sealed record Setup(string UserId, Guid ConversationId);

    private sealed class CountingProvider(string reply) : IAssistantAiClient
    {
        public int CallCount { get; private set; }
        public Task<AssistantAiResponse> CompleteAsync(AssistantAiRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new AssistantAiResponse(reply, []));
        }
    }

    private sealed class BlockingProvider(string reply) : IAssistantAiClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AssistantAiResponse> CompleteAsync(AssistantAiRequest request, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new AssistantAiResponse(reply, []);
        }
    }

    private sealed class FlakyProvider : IAssistantAiClient
    {
        public int CallCount { get; private set; }
        public Task<AssistantAiResponse> CompleteAsync(AssistantAiRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1) throw new AssistantProviderException(503, "safe test failure");
            return Task.FromResult(new AssistantAiResponse("recovered", []));
        }
    }

    private sealed class HallucinatedProductProvider : IAssistantAiClient
    {
        private int _calls;
        public Task<AssistantAiResponse> CompleteAsync(AssistantAiRequest request, CancellationToken cancellationToken = default)
        {
            if (_calls++ > 0) return Task.FromResult(new AssistantAiResponse("Invented product", []));
            using var document = JsonDocument.Parse("""{"productId":999999}""");
            return Task.FromResult(new AssistantAiResponse(null,
                [new AssistantToolCall("fake", "GetProductDetails", document.RootElement.Clone())]));
        }
    }

    private sealed class EndlessToolProvider : IAssistantAiClient
    {
        public int CallCount { get; private set; }
        public Task<AssistantAiResponse> CompleteAsync(AssistantAiRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            using var document = JsonDocument.Parse("""{"keyword":"lamp","limit":1}""");
            return Task.FromResult(new AssistantAiResponse(null,
                [new AssistantToolCall($"loop-{CallCount}", "SearchProducts", document.RootElement.Clone())]));
        }
    }
}
