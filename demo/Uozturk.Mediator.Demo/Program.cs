using Scalar.AspNetCore;
using Uozturk.Mediator;
using Uozturk.Mediator.Demo.Books;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<BookStore>();
builder.Services.AddUozturkMediator(typeof(Program).Assembly);

var app = builder.Build();

app.Services.ValidateUozturkMediator();
app.MapOpenApi();

// Swagger UI (geçici deneme) — Scalar'a dönmek için aşağıdaki iki satırı değiştir:
// app.MapScalarApiReference(); + kök yönlendirmeyi "/scalar/v1" yap.
app.UseSwaggerUI(options =>
    options.SwaggerEndpoint("/openapi/v1.json", "Uozturk.Mediator.Demo v1"));
app.MapGet("/", () => Results.Redirect("/swagger"));

// Scalar:
// app.MapScalarApiReference();
// app.MapGet("/", () => Results.Redirect("/scalar/v1"));

app.MapControllers();

app.Run();
