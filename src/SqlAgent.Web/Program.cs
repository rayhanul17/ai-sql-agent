using Serilog;
using SqlAgent.Application;
using SqlAgent.Application.Options;
using SqlAgent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Structured logging (also captures every generated SQL for audit).
// Console + a daily rolling file (agent-yyyyMMdd.log) under the project's
// Logs/ folder — not bin/, which gets wiped on clean. Serilog rolls the file
// automatically each day.
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(ctx.HostingEnvironment.ContentRootPath, "Logs", "agent-.log"),
        rollingInterval: RollingInterval.Day,
        shared: true,
        retainedFileCountLimit: 7));

// Options.
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<GroqOptions>(builder.Configuration.GetSection(GroqOptions.SectionName));

// App layers.
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// MVC.
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Chat}/{action=Index}/{id?}");

// Log the effective startup configuration (what the app defaults to).
{
    var agent = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
    var ollama = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
    var groq = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<GroqOptions>>().Value;
    var defaultModel = agent.DefaultProvider == SqlAgent.Domain.Models.LlmProvider.Groq
        ? groq.DefaultModel : ollama.DefaultModel;
    var maskedConn = System.Text.RegularExpressions.Regex.Replace(
        agent.DefaultConnectionString, @"(?i)(password|pwd)\s*=\s*[^;]*", "$1=***");
    Log.Information(
        "Startup config: provider={Provider} model={Model} dialect={Dialect} ollamaUrl={OllamaUrl} groqConfigured={Groq} defaultDb=[{Conn}]",
        agent.DefaultProvider, defaultModel, agent.DefaultDialect, ollama.BaseUrl, groq.IsConfigured, maskedConn);
}

app.Run();
