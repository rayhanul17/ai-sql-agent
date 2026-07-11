using Serilog;
using SqlAgent.Application;
using SqlAgent.Application.Options;
using SqlAgent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Structured logging (also captures every generated SQL for audit).
// Writes to the console AND a rolling daily file under logs/ so nothing is
// lost if the app exits unexpectedly.
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(AppContext.BaseDirectory, "logs", "agent-.log"),
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

app.Run();
