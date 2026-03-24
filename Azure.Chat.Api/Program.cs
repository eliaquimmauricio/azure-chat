using Azure.Chat.Api;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddScoped<IAzureChatIntegration, AzureChatIntegration>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IAzureBlobIntegration, AzureBlobIntegration>();
builder.Services.AddAntiforgery();

var app = builder.Build();

app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapGet("chat/token", async (IAzureChatIntegration chatIntegration) =>
{
	return chatIntegration.GetChatToken();
});

app.MapPost("chat/new/direct", async (NewDirectChatRequest request, IAzureChatIntegration chatIntegration) =>
{
	return chatIntegration.CreateNewDirectChat(request);
});

app.MapPost("chat/new/group", async (NewGroupChatRequest request, IAzureChatIntegration chatIntegration) =>
{
	return chatIntegration.CreateNewGroupChat(request);
});

app.MapPost("chat/message/text", async ([FromForm] SendTextMessageRequest request, IAzureChatIntegration chatIntegration) =>
{
	return chatIntegration.SendTextMessage(request);
})
.DisableAntiforgery();

app.MapPost("chat/message/audio", async ([FromForm] SendAudioMessageRequest request, IAzureChatIntegration chatIntegration) =>
{
	return chatIntegration.SendAudioMessage(request);
})
.DisableAntiforgery();

app.MapPost("chat/history", async (HistoryChatRequest request, IAzureChatIntegration chatIntegration) =>
{
	return chatIntegration.GetChatHistory(request);
});

app.MapPost("chat/threads", async (AvailableThreadsRequest request, IAzureChatIntegration chatIntegration) =>
{
	return chatIntegration.GetAvailableThreads(request);
});

app.Run();