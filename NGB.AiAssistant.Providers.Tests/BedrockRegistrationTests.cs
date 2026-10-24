using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NGB.AiAssistant.Bedrock;
using Xunit;

namespace NGB.AiAssistant.Providers.Tests;

[CollectionDefinition("AWS credential configuration", DisableParallelization = true)]
public sealed class AwsCredentialConfigurationCollection;

[Collection("AWS credential configuration")]
public sealed class BedrockRegistrationTests
{
    [Fact]
    public void DefaultRegistrationUsesTheConfiguredRegionWithoutSendingRequests()
    {
        var original = AWSConfigs.AWSCredentialsGenerators;
        try
        {
            AWSConfigs.AWSCredentialsGenerators = [() => new AnonymousAWSCredentials()];
            using var services = new ServiceCollection().AddNgbBedrockChatClient(options =>
            {
                options.Model = "test-model";
                options.Region = "us-east-1";
            }).BuildServiceProvider();
            using var scope = services.CreateScope();

            var runtime = scope.ServiceProvider.GetRequiredService<IAmazonBedrockRuntime>();
            var client = scope.ServiceProvider.GetRequiredService<IChatClient>();

            Assert.Equal("us-east-1", runtime.Config.RegionEndpoint.SystemName);
            Assert.Equal(0, runtime.Config.MaxErrorRetry);
            Assert.NotNull(client);
            Assert.Same(runtime, scope.ServiceProvider.GetRequiredService<IAmazonBedrockRuntime>());
        }
        finally
        {
            AWSConfigs.AWSCredentialsGenerators = original;
        }
    }
}
