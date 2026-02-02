
var builder = WebApplication.CreateBuilder(args);
        
// Basic setup for the host
builder.Services.AddLogging();
        
var app = builder.Build();
app.Run();