using System.Text;
using MeDan.Api.Auth;
using MeDan.Api.Data;
using MeDan.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Ensure the static-files root exists so uploaded images can be served.
var webRoot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(webRoot, "uploads"));

// ---------- Database ----------
// SQL Server is the default. LocalDB is Windows-only, so "Database:Provider":
// "Sqlite" gives macOS/Linux devs a zero-install local database. The committed
// migrations are SQL Server-specific; SQLite builds its schema from the model
// instead (see the bootstrap below).
var dbProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var useSqlite = dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase);
builder.Services.AddDbContext<AppDbContext>(opt =>
{
    var conn = builder.Configuration.GetConnectionString("Default");
    if (useSqlite) opt.UseSqlite(conn);
    else opt.UseSqlServer(conn);
});

// ---------- Authentication ----------
// Identity is owned by this API: /api/auth/login (app users) and
// /api/admin/auth/login (staff) both return a JWT signed here, and every
// [Authorize] endpoint validates it. Firebase is no longer an identity source.
const string ApiScheme = "ApiJwt";

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key))
{
    // A predictable signing key would let anyone mint staff tokens, so this is only
    // ever tolerated on a dev machine.
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Jwt:Key must be set outside Development (dotnet user-secrets set \"Jwt:Key\" \"<32+ random chars>\").");
    jwt.Key = "medan-development-only-signing-key-do-not-use-in-production";
}
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(jwt));
builder.Services.AddScoped<TokenService>();

builder.Services
    .AddAuthentication(ApiScheme)
    .AddJwtBearer(ApiScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes(ApiScheme)
        .Build();
});

// ---------- Per-request current-user resolver ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();

// ---------- Image storage (local disk → swap for blob/S3 in prod) ----------
builder.Services.AddSingleton<IImageStorage, LocalImageStorage>();

// ---------- Payments (Paystack) + referrals ----------
builder.Services.Configure<PaystackOptions>(
    builder.Configuration.GetSection(PaystackOptions.SectionName));
builder.Services.AddHttpClient<IPaystackClient, PaystackClient>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<PayoutService>();
builder.Services.AddScoped<ReferralService>();

// ---------- Push notifications (FCM HTTP v1) ----------
// Without a service account configured this logs instead of sending, so the
// rest of the app behaves identically whether or not push is wired up.
builder.Services.Configure<PushOptions>(
    builder.Configuration.GetSection(PushOptions.SectionName));
builder.Services.AddHttpClient<PushSender>();
builder.Services.AddScoped<BookingNotifier>();

// Closes out escrow on its own once the dispute window passes.
builder.Services.AddHostedService<EscrowReleaseService>();

// ---------- MVC + Swagger ----------
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        // Accept/emit enum values as camelCase strings (e.g. "student", "doublyShared")
        // to match the Flutter app's Dart enum names.
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(
                System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MeDan API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description =
            "Paste a token without the 'Bearer ' prefix. Either a Firebase ID token " +
            "(app users) or the token from POST /api/admin/auth/login (platform staff).",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

// ---------- CORS (open in dev; lock down origins in prod) ----------
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Apply pending migrations on startup in Development.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // The migration history is SQL Server-specific, so under SQLite create the
    // schema straight from the model (HasData seeds included).
    if (useSqlite) db.Database.EnsureCreated();
    else db.Database.Migrate();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Serve uploaded images from wwwroot (e.g. /uploads/hostels/abc.jpg).
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(webRoot)
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
