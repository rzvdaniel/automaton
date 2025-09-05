using Automaton.Studio.Pages.ApiKeys;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.JSInterop;
using AntDesign;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Automaton.Studio.Tests.ViewModels;

[TestFixture]
public class ApiKeysViewModelTests
{
    private ApiKeysViewModel viewModel;
    private IApiKeysService mockApiKeysService;
    private MessageService mockMessageService;
    private IJSRuntime mockJsRuntime;

    [SetUp]
    public void Setup()
    {
        mockApiKeysService = Substitute.For<IApiKeysService>();
        mockMessageService = Substitute.For<MessageService>();
        mockJsRuntime = Substitute.For<IJSRuntime>();

        viewModel = new ApiKeysViewModel(
            mockApiKeysService,
            mockMessageService,
            mockJsRuntime);
    }

    [Test]
    public async Task LoadApiKeys_Success_ShouldSetApiKeysAndClearLoading()
    {
        // Arrange
        var apiKeys = new List<ApiKeyResponse>
        {
            new() { Id = Guid.NewGuid(), Name = "Test Key", KeyPrefix = "ak_test", Scope = ApiKeyScope.Runner, CreatedAt = DateTime.Now, IsActive = true }
        };
        mockApiKeysService.GetApiKeysAsync().Returns(apiKeys);

        // Act
        await viewModel.LoadApiKeys();

        // Assert
        Assert.That(viewModel.ApiKeys, Is.EqualTo(apiKeys));
        Assert.That(viewModel.Loading, Is.False);
    }

    [Test]
    public async Task LoadApiKeys_Exception_ShouldShowErrorAndClearLoading()
    {
        // Arrange
        mockApiKeysService.GetApiKeysAsync().ThrowsAsync(new Exception("API Error"));

        // Act
        await viewModel.LoadApiKeys();

        // Assert
        Assert.That(viewModel.Loading, Is.False);
        await mockMessageService.Received(1).ErrorAsync("Failed to load API keys: API Error");
    }

    [Test]
    public void ShowCreateModal_ShouldResetRequestAndShowModal()
    {
        // Act
        viewModel.ShowCreateModal();

        // Assert
        Assert.That(viewModel.CreateModalVisible, Is.True);
        Assert.That(viewModel.CreateRequest.Name, Is.EqualTo(""));
        Assert.That(viewModel.CreateRequest.Scope, Is.EqualTo(ApiKeyScope.FullAccess));
    }

    [Test]
    public async Task CreateApiKey_EmptyName_ShouldShowWarning()
    {
        // Arrange
        viewModel.CreateRequest = new CreateApiKeyRequest { Name = "", Scope = ApiKeyScope.FullAccess };

        // Act
        await viewModel.CreateApiKey();

        // Assert
        await mockMessageService.Received(1).WarningAsync("Please enter a name for the API key");
        await mockApiKeysService.DidNotReceive().CreateApiKeyAsync(Arg.Any<CreateApiKeyRequest>());
    }

    [Test]
    public async Task CreateApiKey_Success_ShouldShowSuccessModalAndRefreshList()
    {
        // Arrange
        viewModel.CreateRequest = new CreateApiKeyRequest { Name = "Test Key", Scope = ApiKeyScope.FullAccess };
        var createResponse = new CreateApiKeyResponse 
        { 
            Id = Guid.NewGuid(), 
            Name = "Test Key", 
            ApiKey = "ak_fullaccess_12345", 
            KeyPrefix = "ak_fullaccess",
            Scope = ApiKeyScope.FullAccess, 
            CreatedAt = DateTime.Now 
        };
        mockApiKeysService.CreateApiKeyAsync(Arg.Any<CreateApiKeyRequest>()).Returns(createResponse);
        mockApiKeysService.GetApiKeysAsync().Returns(new List<ApiKeyResponse>());

        // Act
        await viewModel.CreateApiKey();

        // Assert
        Assert.That(viewModel.NewApiKey, Is.EqualTo("ak_fullaccess_12345"));
        Assert.That(viewModel.CreateModalVisible, Is.False);
        Assert.That(viewModel.SuccessModalVisible, Is.True);
        Assert.That(viewModel.CreateLoading, Is.False);
        await mockApiKeysService.Received(1).GetApiKeysAsync(); // Refresh call
    }

    [Test]
    public async Task RevokeApiKey_Success_ShouldShowSuccessAndRefresh()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        mockApiKeysService.RevokeApiKeyAsync(apiKeyId).Returns(true);
        mockApiKeysService.GetApiKeysAsync().Returns(new List<ApiKeyResponse>());

        // Act
        await viewModel.RevokeApiKey(apiKeyId);

        // Assert
        await mockMessageService.Received(1).SuccessAsync("API key revoked successfully");
        await mockApiKeysService.Received(1).GetApiKeysAsync(); // Refresh call
    }

    [Test]
    [TestCase(ApiKeyScope.ReadOnly, "orange")]
    [TestCase(ApiKeyScope.Execute, "green")]
    [TestCase(ApiKeyScope.Runner, "blue")]
    [TestCase(ApiKeyScope.FullAccess, "purple")]
    public void GetScopeColor_ReturnsCorrectColor(ApiKeyScope scope, string expectedColor)
    {
        // Act
        var result = ApiKeysViewModel.GetScopeColor(scope);

        // Assert
        Assert.That(result, Is.EqualTo(expectedColor));
    }

    [Test]
    [TestCase(ApiKeyScope.ReadOnly, "scope-readonly")]
    [TestCase(ApiKeyScope.Execute, "scope-execute")]
    [TestCase(ApiKeyScope.Runner, "scope-runner")]
    [TestCase(ApiKeyScope.FullAccess, "scope-fullaccess")]
    public void GetScopeClass_ReturnsCorrectClass(ApiKeyScope scope, string expectedClass)
    {
        // Act
        var result = ApiKeysViewModel.GetScopeClass(scope);

        // Assert
        Assert.That(result, Is.EqualTo(expectedClass));
    }
}