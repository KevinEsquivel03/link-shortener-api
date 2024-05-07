using link_shortener;
using link_shortener.Entities;
using link_shortener.Extensions;
using link_shortener.Models;
using link_shortener.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ApplicationDbContext>(o => 
    o.UseSqlServer(builder.Configuration.GetConnectionString("Database")));

builder.Services.AddScoped<UrlShorteningService>();

builder.Services.AddMemoryCache();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.ApplyMigrations();
}

app.MapPost("api/shorten", async (
    ShortenUrlRequest request,
    UrlShorteningService urlShorteningService, 
    ApplicationDbContext applicationDbContext, 
    HttpContext httpContext) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
    {
        return Results.BadRequest("The specified URL is invalid");
    }

    var code = await urlShorteningService.GenerateUniqueCode();

    var shortenedUrl = new ShortenedUrl
    {
        Id = Guid.NewGuid(),
        LongUrl = request.Url,
        Code = code,
        ShortUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/{code}",
        CreateOnUtc = DateTime.UtcNow
    };

    applicationDbContext.ShortenedUrls.Add(shortenedUrl);

    await applicationDbContext.SaveChangesAsync();

    return Results.Ok(shortenedUrl.ShortUrl);
});

app.MapGet("api/{code}", async (string code, ApplicationDbContext dbContext, IMemoryCache cache) =>
{
    var shortenedCode = await cache.GetOrCreateAsync(code, async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(5);
        return await dbContext.ShortenedUrls.FirstOrDefaultAsync(s => s.Code == code);
    });

    if (shortenedCode is null)
    {
        return Results.NotFound();
    }

    return Results.Redirect(shortenedCode.LongUrl);
});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
