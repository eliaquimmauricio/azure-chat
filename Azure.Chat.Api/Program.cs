using Azure.Chat.Api;
using Azure.Storage.Blobs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddScoped<IAzureChatIntegration, AzureChatIntegration>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration["BlobStorage:ConnectionString"]));

var app = builder.Build();

app.MapOpenApi();
app.UseHttpsRedirection();

app.MapPost("chat/message", async (SendMessageRequest request, IAzureChatIntegration chatIntegration) =>
{
	chatIntegration.Initialize();
	return chatIntegration.SendDirectMessage(request);
});

app.MapGet("chat/history/{threadId}", async (string threadId, IAzureChatIntegration chatIntegration) =>
{
	chatIntegration.Initialize();
	return chatIntegration.GetChatHistory(threadId);
});

app.MapGet("chat/participants/{threadId}", async (string threadId, IAzureChatIntegration chatIntegration) =>
{
	chatIntegration.Initialize();
	return chatIntegration.GetChatParticipants(threadId);
});

app.MapGet("user/{userId}/status", async (int userId, IChatRepository repository) =>
{
	throw new NotImplementedException(); // This endpoint if for the client to check the status of a user (online, offline, away, etc.) and update the UI accordingly.
});

app.MapPost("chat/heartbeat", () => 
{ 
	throw new NotImplementedException(); // This endpoint is for the client to keep the connection alive and save the current status. 
});

app.Run();