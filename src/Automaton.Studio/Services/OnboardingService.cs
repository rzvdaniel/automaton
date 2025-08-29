using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Automaton.Studio.Services;

public class OnboardingService
{
    private readonly ILocalStorageService _localStorage;
    private readonly NavigationManager _navigationManager;
    private const string ONBOARDING_COMPLETED_KEY = "onboarding_completed";
    private const string ONBOARDING_SKIP_KEY = "onboarding_skip_until";

    public event Action OnboardingStatusChanged;

    public OnboardingService(ILocalStorageService localStorage, NavigationManager navigationManager)
    {
        _localStorage = localStorage;
        _navigationManager = navigationManager;
    }

    public async Task<bool> IsOnboardingCompletedAsync()
    {
        try
        {
            return await _localStorage.GetItemAsync<bool>(ONBOARDING_COMPLETED_KEY);
        }
        catch (InvalidOperationException)
        {
            // This happens during prerendering - return false as default
            return false;
        }
        catch (JSException)
        {
            // JavaScript not available yet - return false as default
            return false;
        }
    }

    public async Task MarkOnboardingCompletedAsync()
    {
        try
        {
            await _localStorage.SetItemAsync(ONBOARDING_COMPLETED_KEY, true);
            OnboardingStatusChanged?.Invoke();
        }
        catch (InvalidOperationException)
        {
            // Ignore during prerendering - the action will be retried on client-side
        }
        catch (JSException)
        {
            // Ignore if JavaScript not available
        }
    }

    public async Task<bool> ShouldShowOnboardingAsync()
    {
        try
        {
            var completed = await IsOnboardingCompletedAsync();
            if (completed) return false;

            // Check if user chose to skip temporarily
            var skipUntil = await _localStorage.GetItemAsync<DateTime?>(ONBOARDING_SKIP_KEY);
            if (skipUntil.HasValue && DateTime.Now < skipUntil.Value)
            {
                return false;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            // During prerendering, don't show onboarding automatically
            return false;
        }
        catch (JSException)
        {
            // JavaScript not available yet - don't show onboarding
            return false;
        }
    }

    public async Task SkipOnboardingTemporarilyAsync(int hours = 24)
    {
        try
        {
            var skipUntil = DateTime.Now.AddHours(hours);
            await _localStorage.SetItemAsync(ONBOARDING_SKIP_KEY, skipUntil);
            OnboardingStatusChanged?.Invoke();
        }
        catch (InvalidOperationException)
        {
            // Ignore during prerendering
        }
        catch (JSException)
        {
            // Ignore if JavaScript not available
        }
    }

    public async Task ResetOnboardingAsync()
    {
        try
        {
            await _localStorage.RemoveItemAsync(ONBOARDING_COMPLETED_KEY);
            await _localStorage.RemoveItemAsync(ONBOARDING_SKIP_KEY);
            OnboardingStatusChanged?.Invoke();
        }
        catch (InvalidOperationException)
        {
            // Ignore during prerendering
        }
        catch (JSException)
        {
            // Ignore if JavaScript not available
        }
    }

    public void NavigateToOnboarding()
    {
        _navigationManager.NavigateTo("/onboarding");
    }

    /// <summary>
    /// Navigate to the Setup Guide tab in the Account page
    /// </summary>
    public void NavigateToSetupGuide()
    {
        _navigationManager.NavigateTo("/account#setup");
    }

    /// <summary>
    /// Navigate to Account page and open Setup Guide tab with query parameter
    /// </summary>
    public void NavigateToAccountSetup()
    {
        _navigationManager.NavigateTo("/account?tab=setup");
    }

    public async Task HandleDownloadActionAsync(string downloadType)
    {
        // Mark onboarding as completed when user downloads either option
        await MarkOnboardingCompletedAsync();
    }

    /// <summary>
    /// Safe version of IsOnboardingCompletedAsync that can be called during prerendering
    /// Returns false if localStorage is not available
    /// </summary>
    public async Task<bool> TryIsOnboardingCompletedAsync()
    {
        try
        {
            return await IsOnboardingCompletedAsync();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Safe version of ShouldShowOnboardingAsync that can be called during prerendering
    /// Returns false if localStorage is not available
    /// </summary>
    public async Task<bool> TryShouldShowOnboardingAsync()
    {
        try
        {
            return await ShouldShowOnboardingAsync();
        }
        catch
        {
            return false;
        }
    }
}