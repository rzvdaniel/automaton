using Serilog;
using System.Text;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class ConsoleAuthenticationService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly FileStorageService storageService;
    private readonly ILogger logger;

    public ConsoleAuthenticationService(
        HttpClient httpClient, 
        ConfigurationService configService,
        FileStorageService storageService)
    {
        this.httpClient = httpClient;
        this.configService = configService;
        this.storageService = storageService;
        this.logger = Log.ForContext<ConsoleAuthenticationService>();
    }

    public async Task<bool> LoginAsync(string? serverUrl = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(serverUrl))
            {
                configService.UpdateServerUrl(serverUrl);
            }

            System.Console.WriteLine("=== Automaton Runner Login ===");
            System.Console.WriteLine($"Server: {configService.BaseUrl}");
            System.Console.WriteLine();

            System.Console.Write("Email: ");
            var email = System.Console.ReadLine();

            if (string.IsNullOrWhiteSpace(email))
            {
                System.Console.WriteLine("Email cannot be empty.");
                return false;
            }

            System.Console.Write("Password: ");
            var password = ReadPassword();
            System.Console.WriteLine();

            if (string.IsNullOrWhiteSpace(password))
            {
                System.Console.WriteLine("Password cannot be empty.");
                return false;
            }

            System.Console.WriteLine("Authenticating...");

            // Use Email instead of UserName to match the server's SignInUserCommand model
            var loginRequest = new
            {
                Email = email,
                Password = password
            };

            var json = JsonSerializer.Serialize(loginRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Use the correct authentication endpoint
            var response = await httpClient.PostAsync($"{configService.BaseUrl}/api/account/loginuser", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var authResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                var accessToken = authResponse.TryGetProperty("accessToken", out var at) ? at.GetString() : null;
                var refreshToken = authResponse.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;

                if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
                {
                    await storageService.SaveCredentialsAsync(accessToken, refreshToken);
                    System.Console.WriteLine("? Login successful!");
                    logger.Information("User {Email} logged in successfully", email);
                    return true;
                }
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            System.Console.WriteLine($"? Login failed: {response.StatusCode}");
            if (!string.IsNullOrEmpty(errorContent))
            {
                System.Console.WriteLine($"Error: {errorContent}");
            }
            
            logger.Warning("Login failed for user {Email}: {StatusCode}", email, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"? Login error: {ex.Message}");
            logger.Error(ex, "Login error occurred");
            return false;
        }
    }

    public async Task<string?> GetValidAccessTokenAsync()
    {
        var (accessToken, refreshToken) = await storageService.LoadCredentialsAsync();

        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        // Simple token expiry check (you might want to implement JWT parsing for better validation)
        if (IsTokenExpired(accessToken))
        {
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var newToken = await RefreshTokenAsync(refreshToken);
                if (!string.IsNullOrEmpty(newToken))
                {
                    return newToken;
                }
            }

            System.Console.WriteLine("Authentication expired. Please login again.");
            await storageService.DeleteCredentialsAsync();
            return null;
        }

        return accessToken;
    }

    private async Task<string?> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var refreshRequest = new { RefreshToken = refreshToken };
            var json = JsonSerializer.Serialize(refreshRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Use the correct refresh token endpoint
            var response = await httpClient.PostAsync($"{configService.BaseUrl}/api/token/refreshaccesstoken", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var authResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                var newAccessToken = authResponse.TryGetProperty("accessToken", out var at) ? at.GetString() : null;
                var newRefreshToken = authResponse.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;

                if (!string.IsNullOrEmpty(newAccessToken) && !string.IsNullOrEmpty(newRefreshToken))
                {
                    await storageService.SaveCredentialsAsync(newAccessToken, newRefreshToken);
                    return newAccessToken;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Token refresh failed");
        }

        return null;
    }

    private static bool IsTokenExpired(string token)
    {
        // Simple implementation - in a real scenario, you'd parse the JWT and check exp claim
        // For now, we'll assume tokens are valid for a reasonable time
        return false;
    }

    private static string ReadPassword()
    {
        var password = new StringBuilder();
        ConsoleKeyInfo key;

        do
        {
            key = System.Console.ReadKey(true);

            if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
            {
                password.Append(key.KeyChar);
                System.Console.Write("*");
            }
            else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                System.Console.Write("\b \b");
            }
        } while (key.Key != ConsoleKey.Enter);

        return password.ToString();
    }
}