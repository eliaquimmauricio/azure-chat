namespace Azure.Chat.Api
{
	public interface IChatRepository
	{
		string GetAzureUserId(int userId);
		string GetThreadId(int userId1, int userId2);
		int GetUserIdByAzureUserId(string azureUserId);
		string GetUserName(int userId);
		void StoreAzureUserId(int userId, string azureUserId);
		void StoreThreadId(int userId1, int userId2, string threadId);
	}

	public class ChatRepository : IChatRepository
	{
		public string GetAzureUserId(int userId)
		{
			//Get from database based on currentUserId, if the user doesn't exist, create a new user in Azure Communication Services and store the id in the database
			string id = userId == 1
				? "8:acs:cee70198-e79a-43fd-9040-03048fd32161_0000002d-bc71-143a-0f5f-48521e7c1156"
				: "8:acs:cee70198-e79a-43fd-9040-03048fd32161_0000002d-bc90-9c22-1759-48521e7c117d";

			return id;
		}

		public void StoreAzureUserId(int userId, string azureUserId)
		{
			//Store the mapping of userId and azureUserId in the database
		}

		public string GetThreadId(int userId1, int userId2)
		{
			//Get the thread id for the chat between userId1 and userId2 from the database
			return "19:acsV2_As7i6JedVC6WkojJ06Fd3HXxMHMHqZVdmvriYis4I-o1@thread.v2";
		}

		public void StoreThreadId(int userId1, int userId2, string threadId)
		{
			//Store the mapping of userId1, userId2 and threadId in the database
		}

		public string GetUserName(int userId)
		{
			return "Michael Jackson";
		}

		public int GetUserIdByAzureUserId(string azureUserId)
		{
			int id = azureUserId == "8:acs:cee70198-e79a-43fd-9040-03048fd32161_0000002d-bc71-143a-0f5f-48521e7c1156"
				? 1
				: 2;

			return id;
		}
	}
}
