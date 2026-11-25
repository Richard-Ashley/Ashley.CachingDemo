using Ashley.CachingDemo.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Ashley.CachingDemo API",
        Version = "v1",
        Description = "Four .NET 10 caching strategies compared side-by-side with live benchmarks.",
        Contact = new Microsoft.OpenApi.OpenApiContact
        {
            Name = "Richard Nii Asuku Ashley",
            Url = new Uri("https://github.com/Richard-Ashley")
        }
    });
});

builder.Services.AddSingleton<IProductSource, ProductSource>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(policy => policy.Expire(TimeSpan.FromMinutes(5)));

    options.AddPolicy("ProductsPolicy", policy => policy
        .Expire(TimeSpan.FromMinutes(5))
        .SetVaryByRouteValue("id")
        .Tag("products"));
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis")
        ?? "localhost:6379";
    options.InstanceName = "ashley-cachingdemo:";
});

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ashley.CachingDemo v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseOutputCache();
app.MapControllers();

app.Run();
