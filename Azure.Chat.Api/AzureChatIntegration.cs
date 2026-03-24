using Azure.Communication;
using Azure.Communication.Chat;
using Azure.Communication.Identity;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Azure.Chat.Api
{
	public interface IAzureChatIntegration
	{
		NewChatResponse CreateNewDirectChat(NewDirectChatRequest request);
		NewChatResponse CreateNewGroupChat(NewGroupChatRequest request);
		PaginatedResponse<AvailableThreadsResponse> GetAvailableThreads(AvailableThreadsRequest request);
		PaginatedResponse<MessageResponse> GetChatHistory(HistoryChatRequest request);		
		ChatTokenResponse GetChatToken();
		MessageResponse SendAudioMessage(SendAudioMessageRequest request);
		MessageResponse SendTextMessage(SendTextMessageRequest request);
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

			ChatParticipant participantA = new(currentUser)
			{
				DisplayName = repository.GetUserName(currentUserId),
				Metadata =
				{
					["userId"] = currentUserId.ToString()
				}
			};

			ChatParticipant participantB = new(destinationUser)
			{
				DisplayName = repository.GetUserName(request.DestinationUserId),
				Metadata =
				{
					["userId"] = request.DestinationUserId.ToString()
				}
			};

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

		public MessageResponse SendTextMessage(SendTextMessageRequest request)
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
				foreach (var attachment in request.Attachments) //ToDo: Consider parallel upload for better performance
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
					["type"] = "text",
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

		public MessageResponse SendAudioMessage(SendAudioMessageRequest request)
		{
			if (string.IsNullOrEmpty(request.ChatTokenId))
				throw new InvalidOperationException("Chat token is required to create a new group chat.");

			if (string.IsNullOrEmpty(request.ThreadId))
				throw new InvalidOperationException("Thread ID is required to send a message.");

			if (request.AudioFile.Length > 10485760)
				throw new InvalidOperationException("Audio file must be less than 10 MB.");

			if (request.AudioFile.ContentType != "audio/mpeg")
				throw new InvalidOperationException("Only MP3 audio format are supported.");

			var uploadedFile = azureBlobIntegration.Upload("chat-audio-messages", request.AudioFile);

			var options = new SendChatMessageOptions
			{
				Content = string.Empty,
				SenderDisplayName = repository.GetUserName(currentUserId),
				Metadata =
				{
					["type"] = "audio",
					["userId"] = currentUserId.ToString(),
					["audio"] = uploadedFile.Link,
					["mime"] = "audio/mpeg"
				}
			};

			var chatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(request.ChatTokenId));

			var chatThreadClient = chatClient.GetChatThreadClient(request.ThreadId);

			var sendMessageResult = chatThreadClient.SendMessage(options);

			var message = chatThreadClient.GetMessage(sendMessageResult.Value.Id);

			return MessageResponse.FromChatMessage(message);
		}

		public PaginatedResponse<MessageResponse> GetChatHistory(HistoryChatRequest request)
		{
			if (string.IsNullOrEmpty(request.ChatTokenId))
				throw new InvalidOperationException("Chat token is required to create a new group chat.");

			if (string.IsNullOrEmpty(request.ThreadId))
				throw new InvalidOperationException("Thread ID is required to send a message.");

			if (request.PageSize < 0 || request.PageSize > 20)
				throw new InvalidOperationException("PageSize must be between 0 and 20.");

			var chatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(request.ChatTokenId));

			var chatThreadClient = chatClient.GetChatThreadClient(request.ThreadId);
			var messages = chatThreadClient.GetMessages().AsPages(request.ContinuationToken, request.PageSize).First();

			return new PaginatedResponse<MessageResponse>
			{
				Items = messages.Values.Select(MessageResponse.FromChatMessage),
				ContinuationToken = messages.ContinuationToken
			};
		}

		public PaginatedResponse<AvailableThreadsResponse> GetAvailableThreads(AvailableThreadsRequest request)
		{
			if (string.IsNullOrEmpty(request.ChatTokenId))
				throw new InvalidOperationException("Chat token is required to create a new group chat.");

			var chatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(request.ChatTokenId));

			var chatThreads = chatClient.GetChatThreads().AsPages(request.ContinuationToken, request.PageSize).First();

			List<AvailableThreadsResponse> result = new();

			foreach (var chatThread in chatThreads.Values)
			{
				var chatThreadClient = chatClient.GetChatThreadClient(chatThread.Id);
				var participants = chatThreadClient.GetParticipants();
				var lastMessage = chatThreadClient.GetMessages().FirstOrDefault();

				MessageType? lastMessageType = null;

				if (lastMessage?.Metadata.ContainsKey("type") == true)
				{
					lastMessageType = lastMessage.Metadata["type"] switch
					{
						"text" => MessageType.Text,
						"audio" => MessageType.Audio,
						_ => null
					};
				}

				result.Add(new AvailableThreadsResponse
				{
					ThreadId = chatThread.Id,
					LastMessageReceivedOn = chatThread.LastMessageReceivedOn,
					LastMessageType = lastMessageType,
					LastMessage = lastMessageType == MessageType.Text ? lastMessage?.Content.Message : null,
					ThreadType = participants.Count() > 2 ? ChatThreadType.Group : ChatThreadType.Direct,
					Participants = participants.Select(p => new Participant
					{
						UserId = p.Metadata.ContainsKey("userId") ? int.Parse(p.Metadata["userId"]) : 0,
						DisplayName = p.DisplayName
					})
				});
			}

			return new PaginatedResponse<AvailableThreadsResponse>
			{
				Items = result,
				ContinuationToken = chatThreads.ContinuationToken
			};
		}
	}

	// These classes are for demonstration purposes and may not represent the actual data models used in a production application.

	public abstract class PaginatedRequest
	{
		public string? ContinuationToken { get; set; }

		public int PageSize { get; set; } = 20;
	}

	public class PaginatedResponse<T> where T : class
	{
		public string? ContinuationToken { get; set; }
		public IEnumerable<T> Items { get; set; } = [];
	}

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

	public abstract class SendMessageRequest
	{
		public required string ChatTokenId { get; set; }
		public required string ThreadId { get; set; }
	}

	public class SendTextMessageRequest : SendMessageRequest
	{
		public required string Message { get; set; }
		public IFormFileCollection? Attachments { get; set; }
	}

	public class SendAudioMessageRequest : SendMessageRequest
	{
		public required IFormFile AudioFile { get; set; }
	}

	public class HistoryChatRequest : PaginatedRequest
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
		public required int UserId { get; set; }
		public required string DisplayName { get; set; }
	}

	public class AvailableThreadsRequest : PaginatedRequest
	{
		public required string ChatTokenId { get; set; }
	}

	public enum MessageType
	{
		Text,
		Audio
	}

	public enum ChatThreadType
	{
		Direct,
		Group
	}

	public class AvailableThreadsResponse
	{
		public required string ThreadId { get; set; }
		public MessageType? LastMessageType { get; set; }
		public string? LastMessage { get; set;  }
		public DateTimeOffset? LastMessageReceivedOn { get; set; }
		public required ChatThreadType ThreadType { get; set; }
		public required IEnumerable<Participant> Participants { get; set; }
	}
}
