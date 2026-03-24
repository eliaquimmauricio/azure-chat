using Azure.Communication;
using Azure.Communication.Chat;
using Azure.Communication.Identity;
using Newtonsoft.Json;

namespace Azure.Chat.Api
{
	public interface IAzureChatIntegration
	{
		NewChatResponse CreateNewDirectChat(NewDirectChatRequest request);
		NewChatResponse CreateNewGroupChat(NewGroupChatRequest request);
		IEnumerable<MessageResponse> GetChatHistory(HistoryChatRequest request);
		IEnumerable<Participant> GetChatParticipants(string threadId);
		ChatTokenResponse GetChatToken();
		MessageResponse SendMessage(SendMessageRequest request);
	}

	public class AzureChatIntegration(
			IConfiguration configuration,
			IChatRepository repository,
			IAzureBlobIntegration azureBlobIntegration) : IAzureChatIntegration
	{
		private readonly int currentUserId = 1;

		private readonly string connectionString = configuration["AzureCommunicationServices:ConnectionString"]
			?? throw new InvalidOperationException("Azure Communication Services connection string is not configured.");

		private readonly string endpoint = configuration["AzureCommunicationServices:Endpoint"]
			?? throw new InvalidOperationException("Azure Communication Services endpoint is not configured.");

		private CommunicationUserIdentifier GetCommunicationUserIdentifier(CommunicationIdentityClient communicationIdentityClient, int userId)
		{
			string id = repository.GetAzureUserId(userId);

			if (string.IsNullOrEmpty(id))
			{
				id = communicationIdentityClient.CreateUser().Value.Id;
				repository.StoreAzureUserId(userId, id);
			}

			return new CommunicationUserIdentifier(id);
		}

		public ChatTokenResponse GetChatToken()
		{
			var communicationIdentityClient = new CommunicationIdentityClient(connectionString);

			var currentUser = GetCommunicationUserIdentifier(communicationIdentityClient, currentUserId);

			var token = communicationIdentityClient.GetToken(currentUser, scopes: [CommunicationTokenScope.Chat]);

			return new ChatTokenResponse { ChatTokenId = token.Value.Token };
		}

		public NewChatResponse CreateNewDirectChat(NewDirectChatRequest request)
		{
			if (string.IsNullOrEmpty(request.ChatTokenId))
				throw new InvalidOperationException("Chat token is required to create a new direct chat.");

			string threadId = repository.GetThreadId(currentUserId, request.DestinationUserId);

			if (string.IsNullOrEmpty(threadId))
				return new NewChatResponse { ThreadId = threadId };

			var communicationIdentityClient = new CommunicationIdentityClient(connectionString);

			var currentUser = GetCommunicationUserIdentifier(communicationIdentityClient, currentUserId);

			var destinationUser = GetCommunicationUserIdentifier(communicationIdentityClient, request.DestinationUserId);

			ChatParticipant participantA = new(currentUser);

			ChatParticipant participantB = new(destinationUser);

			List<ChatParticipant> participants = [participantA, participantB];

			var chatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(request.ChatTokenId));

			CreateChatThreadResult chatThreadResult = chatClient.CreateChatThread("Direct Chat", participants);

			threadId = chatThreadResult.ChatThread.Id;

			repository.StoreThreadId(currentUserId, request.DestinationUserId, threadId);

			return new NewChatResponse { ThreadId = threadId };
		}

		public NewChatResponse CreateNewGroupChat(NewGroupChatRequest request)
		{
			if (string.IsNullOrEmpty(request.ChatTokenId))
				throw new InvalidOperationException("Chat token is required to create a new group chat.");

			var communicationIdentityClient = new CommunicationIdentityClient(connectionString);

			var currentUser = GetCommunicationUserIdentifier(communicationIdentityClient, currentUserId);

			List<ChatParticipant> participants = [new ChatParticipant(currentUser)];

			foreach (var participantUserId in request.ParticipantUserIds)
			{
				var participant = GetCommunicationUserIdentifier(communicationIdentityClient, participantUserId);
				participants.Add(new ChatParticipant(participant));
			}

			var chatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(request.ChatTokenId));

			CreateChatThreadResult chatThreadResult = chatClient.CreateChatThread(request.GroupName, participants);

			string threadId = chatThreadResult.ChatThread.Id;

			return new NewChatResponse { ThreadId = threadId };
		}

		public MessageResponse SendMessage(SendMessageRequest request)
		{
			if (string.IsNullOrEmpty(request.ChatTokenId))
				throw new InvalidOperationException("Chat token is required to create a new group chat.");

			if (string.IsNullOrEmpty(request.ThreadId))
				throw new InvalidOperationException("Thread ID is required to send a message.");

			if (request.Attachments is not null && request.Attachments.Count > 10)
				throw new InvalidOperationException("A maximum of 10 attachments are allowed.");

			if (request.Attachments is not null && request.Attachments.Any(a => a.Length > 10485760))
				throw new InvalidOperationException("Each attachment must be less than 10 MB.");

			List<UploadedFileInfo> uploadedFiles = new();

			if (request.Attachments is not null)
			{
				foreach (var attachment in request.Attachments)
				{
					var uploadedFile = azureBlobIntegration.Upload("chat-attachments", attachment);
					uploadedFiles.Add(uploadedFile);
				}
			}

			var options = new SendChatMessageOptions
			{
				Content = request.Message,
				SenderDisplayName = repository.GetUserName(currentUserId),
				MessageType = ChatMessageType.Text,
				Metadata =
				{
					["userId"] = currentUserId.ToString(),
					["attachments"] = JsonConvert.SerializeObject(uploadedFiles)
				}
			};

			var chatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(request.ChatTokenId));

			var chatThreadClient = chatClient.GetChatThreadClient(request.ThreadId);

			var sendMessageResult = chatThreadClient.SendMessage(options);

			var message = chatThreadClient.GetMessage(sendMessageResult.Value.Id);

			return MessageResponse.FromChatMessage(message);
		}

		public IEnumerable<MessageResponse> GetChatHistory(HistoryChatRequest request)
		{
			if (string.IsNullOrEmpty(request.ChatTokenId))
				throw new InvalidOperationException("Chat token is required to create a new group chat.");

			if (string.IsNullOrEmpty(request.ThreadId))
				throw new InvalidOperationException("Thread ID is required to send a message.");

			var chatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(request.ChatTokenId));

			var chatThreadClient = chatClient.GetChatThreadClient(request.ThreadId);
			var messages = chatThreadClient.GetMessages();

			return messages.Select(MessageResponse.FromChatMessage);
		}

		public IEnumerable<Participant> GetChatParticipants(string threadId)
		{
			throw new NotImplementedException();
		}
	}

	// These classes are for demonstration purposes and may not represent the actual data models used in a production application.

	public class Chat
	{
		public required string ThreadId { get; set; }
		public required int SenderUserId { get; set; }
		public required int DestinationUserId { get; set; }
	}

	public class ChatTokenResponse
	{
		public required string ChatTokenId { get; set; }
	}

	public class NewChatResponse
	{
		public required string ThreadId { get; set; }
	}

	public abstract class NewChatRequest
	{
		public required string ChatTokenId { get; set; }
	}

	public class NewDirectChatRequest : NewChatRequest
	{
		public required int DestinationUserId { get; set; }
	}

	public class NewGroupChatRequest : NewChatRequest
	{
		public required string GroupName { get; set; }
		public required List<int> ParticipantUserIds { get; set; }		
	}

	public class SendMessageRequest
	{
		public required string ChatTokenId { get; set; }
		public required string ThreadId { get; set; }
		public required string Message { get; set; }
		public IFormFileCollection? Attachments { get; set; }
	}

	public class HistoryChatRequest
	{
		public required string ChatTokenId { get; set; }
		public required string ThreadId { get; set; }
	}

	public class MessageResponse
	{
		public required string MessageId { get; set; }
		public required string Message { get; set; }
		public required string SenderDisplayName { get; set; }
		public required IReadOnlyDictionary<string, string> Metadata { get; set; }
		public required DateTimeOffset CreatedOn { get; set; }
		public required DateTimeOffset? DeletedOn { get; set; }

		public static MessageResponse FromChatMessage(ChatMessage chatMessage)
		{
			return new MessageResponse
			{
				MessageId = chatMessage.Id,
				Message = chatMessage.Content.Message,
				SenderDisplayName = chatMessage.SenderDisplayName,
				Metadata = chatMessage.Metadata,
				CreatedOn = chatMessage.CreatedOn,
				DeletedOn = chatMessage.DeletedOn
			};
		}
	}

	public class Participant
	{
		public int UserId { get; set; }
		public required string UserName { get; set; }
		public required string ProfilePictureUrl { get; set; }
	}
}
