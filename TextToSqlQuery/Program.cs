using Microsoft.OpenApi.Models;
using TextToSqlQuery.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

// Registers and configures Swagger for your API documentation
builder.Services.AddSwaggerGen(c =>
{
    // Defines a Swagger document (an API description) with version and metadata
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        // The title that will appear on the Swagger UI page
        Title = "Database Analyzer API",

        // The version of your API
        Version = "v1",

        // A short description shown in the Swagger UI header
        Description = "Analyze any SQL Server database using natural language with Ollama AI"
    });
});


builder.Services.AddHttpClient<OllamaService>();
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddScoped<QueryAnalyzerService>();

// Registers and configures CORS (Cross-Origin Resource Sharing) for the API
builder.Services.AddCors(options =>
{
    // Adds a CORS policy named "AllowAll"
    options.AddPolicy("AllowAll", policy =>
    {
        // Allows requests from any origin (any domain)
        policy.AllowAnyOrigin()

              // Allows all HTTP methods (GET, POST, PUT, DELETE, etc.)
              .AllowAnyMethod()

              // Allows any HTTP headers in the request
              .AllowAnyHeader();
    });
});



var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");
app.UseAuthorization();

app.MapControllers();

app.Run();
