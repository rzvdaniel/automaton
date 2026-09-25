using Automaton.Api.Extensions;

var applicationBuilder = WebApplication.CreateBuilder(args);
var corsOrigins = applicationBuilder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
applicationBuilder.Services.AddAutomatonApiServices(applicationBuilder, corsOrigins);

var app = applicationBuilder.Build();
var logSuccessfulRequests = applicationBuilder.Configuration.GetValue<bool>("RequestLogging:LogSuccessfulRequests");

await app.InitializeAutomatonApiAsync();
app.UseAutomatonApiPipeline(corsOrigins, logSuccessfulRequests);

app.Run();
