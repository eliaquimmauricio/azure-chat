using Azure.Communication;
using Azure.Communication.Chat;
using Azure.Communication.Identity;

namespace Azure.Chat.Api
{
	public interface IAzureChatIntegration
	{
		void Initialize();
		SendMessageResponse SendDirectMessage(SendMessageRequest request);
		IEnumerable<HistoryMessage> GetChatHistory(string threadId);
		IEnumerable<Participant> GetChatParticipants(string threadId);
	}

	public class AzureChatIntegration(IConfiguration configuration, IChatRepository repository) : IAzureChatIntegration
	{
		private readonly int currentUserId = 1;
		private CommunicationUserIdentifier? currentUser;
		private ChatClient? chatClient;
		private CommunicationIdentityClient? communicationIdentityClient;

		public void Initialize()
		{
			var endpoint = configuration["AzureCommunicationServices:Endpoint"]
				?? throw new InvalidOperationException("Azure Communication Services endpoint is not configured.");

			var connectionString = configuration["AzureCommunicationServices:ConnectionString"]
				?? throw new InvalidOperationException("Azure Communication Services connection string is not configured.");

			communicationIdentityClient = new CommunicationIdentityClient(connectionString);

			currentUser = GetCommunicationUserIdentifier(currentUserId);

			var token = communicationIdentityClient.GetToken(currentUser, scopes: [CommunicationTokenScope.Chat]);

			chatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(token.Value.Token));
		}

		private CommunicationUserIdentifier GetCommunicationUserIdentifier(int userId)
		{
			if (communicationIdentityClient is null)
				throw new InvalidOperationException("Communication Identity Client is not initialized.");

			string id = repository.GetAzureUserId(userId);

			if (string.IsNullOrEmpty(id))
			{
				id = communicationIdentityClient.CreateUser().Value.Id;
				repository.StoreAzureUserId(userId, id);
			}

			return new CommunicationUserIdentifier(id);
		}

		public SendMessageResponse SendDirectMessage(SendMessageRequest request)
		{
			if (chatClient is null)
				throw new InvalidOperationException("Chat client is not initialized.");

			if (currentUser is null)
				throw new InvalidOperationException("Current user is not set.");

			var threadId = repository.GetThreadId(currentUserId, request.UserId);

			if (string.IsNullOrEmpty(threadId))
			{
				ChatParticipant participantA = new(currentUser);

				ChatParticipant participantB = new ChatParticipant(GetCommunicationUserIdentifier(request.UserId));

				List<ChatParticipant> participants = new() { participantA, participantB };

				CreateChatThreadResult chatThreadResult = chatClient.CreateChatThread("Direct Chat", participants);

				threadId = chatThreadResult.ChatThread.Id;

				repository.StoreThreadId(currentUserId, request.UserId, threadId);
			}

			string userName = repository.GetUserName(request.UserId);

			if (request.Attachment is not null)
			{
				// Store the attachment and get its URL and the content type. After, add this information to the message metadata so it can be rendered properly in the client applications.
			}

			var options = new SendChatMessageOptions
			{
				Content = request.Message,
				SenderDisplayName = userName,
				Metadata =
				{
					["userId"] = request.UserId.ToString(),
					// Add more metadata as needed, for example, you could include attachment URLs, message types, etc.
				},
				MessageType = ChatMessageType.Text
			};


			chatClient.GetChatThreadClient(threadId).SendMessage(options);

			return new SendMessageResponse { ThreadId = threadId };
		}

		public IEnumerable<HistoryMessage> GetChatHistory(string threadId)
		{
			if (chatClient is null)
				throw new InvalidOperationException("Chat client is not initialized.");

			if (currentUser is null)
				throw new InvalidOperationException("Current user is not set.");

			var chatThreadClient = chatClient.GetChatThreadClient(threadId);
			var messages = chatThreadClient.GetMessages();

			return messages
				.Where(m => m.Content.Message is not null)
				.Select(m => new HistoryMessage
				{
					SenderUserId = m.Metadata.ContainsKey("userId") ? int.Parse(m.Metadata["userId"]) : 0,
					SenderName = m.SenderDisplayName,
					SenderProfilePictureUrl = "https://example.com/profile-picture.jpg",
					Message = m.Content.Message,
					CreatedOn = m.CreatedOn
				});
		}

		public IEnumerable<Participant> GetChatParticipants(string threadId)
		{
			if (chatClient is null)
				throw new InvalidOperationException("Chat client is not initialized.");

			var chatThreadClient = chatClient.GetChatThreadClient(threadId);
			var participants = chatThreadClient.GetParticipants();

			return participants.Select(p => new Participant
			{
				UserId = repository.GetUserIdByAzureUserId(((CommunicationUserIdentifier)p.User).Id),
				UserName = p.DisplayName,
				ProfilePictureUrl = "https://example.com/profile-picture.jpg"
			});
		}
	}

	// These classes are for demonstration purposes and may not represent the actual data models used in a production application.

	public class Chat
	{
		public required string ThreadId { get; set; }
		public required int SenderUserId { get; set; }
		public required int DestinationUserId { get; set; }
	}

	public class HistoryMessage
	{
		public int SenderUserId { get; set; }
		public required string SenderName { get; set; }
		public required string SenderProfilePictureUrl { get; set; }
		public required string Message { get; set; }
		public required DateTimeOffset CreatedOn { get; set; }
	}

	public class Participant
	{
		public int UserId { get; set; }
		public required string UserName { get; set; }
		public required string ProfilePictureUrl { get; set; }
	}

	public class SendMessageRequest
	{
		public int UserId { get; set; }
		public required string Message { get; set; }
		public IFormFile? Attachment { get; set; }
	}

	public class SendMessageResponse
	{
		public required string ThreadId { get; set; }
	}
}
