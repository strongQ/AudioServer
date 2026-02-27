using Audio.Core.Extensions;
using Audio.Core.Interfaces;
using Audio.Core.LogServices;
using Audio.Core.Options;
using AudioServer.Web.HostedServices;
using AudioServer.Web.Services;
using log4net.Config;
using System.Reflection;
using XT.Common;
using XT.Common.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSignalRService();
builder.Services.ConfigureLog4net();
builder.Services.AddSingleton<ILogService, LogService>();

builder.Services.AddSingleton<IVoiceEngineService,SherpaEngineService>();
builder.Services.Configure<VoskServerOption>(builder.Configuration.GetSection("Vosk"));
builder.Services.AddHostedService<VoiceTcpHostedService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
