using backend_iot.Services;
using backend_iot.Models;
using backend_iot;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

static string? FirstNonEmpty(params string?[] values) =>
    values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

// 🔥 PUERTO DINÁMICO PARA RAILWAY
var port = Environment.GetEnvironmentVariable("PORT") ?? "5126";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// --- 1. CONFIGURACIÓN DE SEGURIDAD (JWT) ---
var jwtKey = FirstNonEmpty(
    Environment.GetEnvironmentVariable("JWT_KEY"),
    builder.Configuration["Jwt:Key"]);
if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
{
    throw new Exception("Seguridad Crítica: La JWT_KEY no está configurada o es muy corta.");
}

var key = Encoding.ASCII.GetBytes(jwtKey);
var mongoConnectionString = FirstNonEmpty(
    Environment.GetEnvironmentVariable("MONGODB_URI"),
    Environment.GetEnvironmentVariable("MONGO_URL"),
    Environment.GetEnvironmentVariable("MONGO_PUBLIC_URL"),
    Environment.GetEnvironmentVariable("MONGO_PRIVATE_URL"),
    Environment.GetEnvironmentVariable("DATABASE_URL"),
    builder.Configuration["MongoDbSettings:ConnectionString"]);
var mongoDatabaseName = FirstNonEmpty(
    Environment.GetEnvironmentVariable("MONGODB_DATABASE"),
    Environment.GetEnvironmentVariable("MONGO_DATABASE"),
    Environment.GetEnvironmentVariable("MONGODATABASE"),
    builder.Configuration["MongoDbSettings:DatabaseName"])
    ?? "EcoMonitor";
var frontendUrl = FirstNonEmpty(
    Environment.GetEnvironmentVariable("FRONTEND_URL"),
    builder.Configuration["FrontendUrl"])
    ?? "https://ecomonitor-fronend-production.up.railway.app";

builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };
});

// --- 2. CONTROLADORES ---
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// --- 3. SWAGGER ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "EcoMonitor API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Insertar JWT: Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
        {
            new OpenApiSecurityScheme {
                Reference = new OpenApiReference {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// --- 4. MONGODB ---
builder.Services.Configure<MongoDbSettings>(options =>
{
    options.ConnectionString = mongoConnectionString ?? string.Empty;
    options.DatabaseName = mongoDatabaseName;
});

builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(mongoConnectionString)
);

builder.Services.AddScoped<IMongoDatabase>(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    return client.GetDatabase(mongoDatabaseName);
});

builder.Services.AddSingleton<MongoService>();
builder.Services.AddScoped<IAuthService, AuthService>();

//  5. CORS SOLO PARA TU FRONTEND (PRODUCCIÓN)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(frontendUrl)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// --- SWAGGER SOLO EN DEV ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//  ENDPOINT PARA PROBAR QUE VIVE
app.MapGet("/", () => "API funcionando ");

//  ORDEN IMPORTANTE
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
