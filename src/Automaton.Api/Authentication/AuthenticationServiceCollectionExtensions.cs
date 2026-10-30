using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Services;

namespace Automaton.Api.Authentication
{
    public static class AuthenticationServiceCollectionExtensions
    {
        public static void AddJwtAuthentication(this IServiceCollection services,
            IConfiguration configuration,
            TokenValidationParameters parameters = null)
        {
            JwtOptions jwtOptions = new JwtOptions();
            var authSettings = new AuthenticationSettings();

            services.AddHttpContextAccessor();
            configuration.GetSection(nameof(AuthenticationSettings)).Bind(authSettings);

            configuration.GetSection(nameof(JwtOptions)).Bind(jwtOptions);
            jwtOptions.SigningCredentials = GetCredentialSigningKey(authSettings)
                ?? throw new InvalidOperationException("Authentication signing credentials are not configured.");

            services.TryAddSingleton(jwtOptions);
            services.TryAddSingleton(authSettings);
            services.TryAddSingleton<IJwtService, JwtService>();
            var validationParameters = CreateTokenValidationParameters(jwtOptions);

            services.AddAuthentication(ConfigureAuthenticationDefaults)
                .AddJwtBearer(bearerOptions =>
                    ConfigureBearerOptions(bearerOptions, jwtOptions, validationParameters));
        }

        private static void ConfigureAuthenticationDefaults(AuthenticationOptions options)
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }

        private static TokenValidationParameters CreateTokenValidationParameters(JwtOptions options)
            => new()
            {
                // Ensure the token was issued by a trusted authorization server (default true)
                ValidateIssuer = options.ValidateIssuer,
                ValidIssuer = options.Issuer,
                // Ensure the token audience matches our audience value (default true)
                ValidateAudience = options.ValidateAudience,
                ValidAudience = options.Audience,
                // Specify the key used to sign the token
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                IssuerSigningKey = options.SigningCredentials?.Key
                    ?? throw new InvalidOperationException("JWT signing credentials are not configured."),
                // Ensure the token hasn't expired
                RequireExpirationTime = true,
                ValidateLifetime = true,
                // Clock skew compensates for server time drift.
                ClockSkew = TimeSpan.FromMinutes(5)
            };

        private static void ConfigureBearerOptions(
            JwtBearerOptions options,
            JwtOptions jwtOptions,
            TokenValidationParameters validationParameters)
        {
            options.Events = CreateBearerEvents();
            options.RequireHttpsMetadata = jwtOptions.RequireHttpsMetadata;
            //set SaveTokens to save tokens to the AuthenticationProperties
            //https://www.jerriepelser.com/blog/aspnetcore-jwt-saving-bearer-token-as-claim/
            //https://www.jerriepelser.com/blog/accessing-tokens-aspnet-core-2/
            options.SaveToken = jwtOptions.SaveToken;
            options.ClaimsIssuer = jwtOptions.Issuer;
            options.TokenValidationParameters = validationParameters;
        }

        private static JwtBearerEvents CreateBearerEvents()
            => new()
            {
                OnTokenValidated = _ => Task.CompletedTask,
                OnMessageReceived = SetAccessTokenFromQuery,
                OnAuthenticationFailed = SetExpiredTokenHeader
            };

        private static Task SetAccessTokenFromQuery(MessageReceivedContext context)
        {
            var path = context.HttpContext.Request.Path;
            var runtimeToken = context.Request.Query["token"].FirstOrDefault();
            var hubAccessToken = context.Request.Query["access_token"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(runtimeToken) &&
                (path.StartsWithSegments("/a") || path.StartsWithSegments("/api/web-apps/app")))
            {
                context.Token = runtimeToken;
            }
            else if (!string.IsNullOrWhiteSpace(hubAccessToken) &&
                     path.StartsWithSegments("/api/workflow/hub"))
            {
                context.Token = hubAccessToken;
            }

            return Task.CompletedTask;
        }

        private static Task SetExpiredTokenHeader(AuthenticationFailedContext context)
        {
            if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                context.Response.Headers["Token-Expired"] = "true";

            return Task.CompletedTask;
        }


        public static void AddAccessTokenValidator(this IServiceCollection services)
        {
            services.AddSingleton<IAccessTokenManagerService, AccessTokenManagerService>();
            services.AddSingleton<AccessTokenMiddleware>();
        }

        public static long ToUnixEpochDate(this DateTime date)
            => (long) Math.Round((date.ToUniversalTime() - DateTimeOffset.UnixEpoch)
                .TotalSeconds);

        private static SigningCredentials? GetCredentialSigningKey(AuthenticationSettings setting)
        {
            if (setting.RsaSettings is not null)
            {
                return GetRsaKey(setting);
            }

            return GetHmacKey(setting);
        }

        private static SigningCredentials GetHmacKey(AuthenticationSettings setting)
        {
            if (string.IsNullOrWhiteSpace(setting.HMacSettings?.SecretKey))
            {
                return null;
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(setting.HMacSettings.SecretKey));
            return new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        }

        private static SigningCredentials GetRsaKey(AuthenticationSettings setting)
        {
            if (setting.RsaSettings is null)
                return null;

            if (setting.RsaSettings.IsIssuer)
            {
                if (string.IsNullOrWhiteSpace(setting.RsaSettings.RsaPrivateKey))
                {
                    return null;
                }

                RSA privateRsa = RSA.Create();
                var privateKeyXml = setting.RsaSettings.RsaPrivateKey;
                privateRsa.FromXmlString(privateKeyXml);
                var privateKey = new RsaSecurityKey(privateRsa);
                return new SigningCredentials(privateKey, SecurityAlgorithms.RsaSha256);
            }
            else
            {
                RSA publicRsa = RSA.Create();
                var publicKeyXml = setting.RsaSettings.RsaPublicKey;
                if (string.IsNullOrWhiteSpace(publicKeyXml))
                    return null;
                publicRsa.FromXmlString(publicKeyXml);
                var publicKey = new RsaSecurityKey(publicRsa);
                return new SigningCredentials(publicKey, SecurityAlgorithms.RsaSha256);
            }
        }
    }
}
