using Microsoft.EntityFrameworkCore;
using WorkflowApp.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// --- Persistence (SQL Server, source of truth) ---
builder.Services.AddDbContext<WorkflowDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// --- Real-time (SignalR) ---
builder.Services.AddSignalR();

// --- API surface ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS for the Angular dev client.
builder.Services.AddCors(o => o.AddPolicy("client", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// TODO Phase 1: AddAuthentication(JwtBearer) + permission-based AddAuthorization policies.

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("client");
// TODO Phase 1: app.UseAuthentication(); app.UseAuthorization();

app.MapControllers();
// TODO Phase 9: app.MapHub<WorkflowHub>("/hubs/workflow");

app.Run();
