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

app.MapPost("chat/message", async ([FromForm] SendMessageRequest request, IAzureChatIntegration chatIntegration) =>
{
	return chatIntegration.SendMessage(request);
})
.DisableAntiforgery();

app.MapPost("chat/history", async (HistoryChatRequest request, IAzureChatIntegration chatIntegration) =>
{
	return chatIntegration.GetChatHistory(request);
});

app.Run();