using Azure.Storage.Blobs;
using System.Text;

namespace Azure.Chat.Api
{
	public interface IAzureBlobIntegration
	{
		UploadedFileInfo Upload(string containerName, IFormFile file);
	}

	public class AzureBlobIntegration(IConfiguration configuration) : IAzureBlobIntegration
	{
		public UploadedFileInfo Upload(string containerName, IFormFile file)
		{
			string connectionString = configuration["BlobStorage:ConnectionString"]
				?? throw new InvalidOperationException("Blob storage connection string is not configured.");

			var blobServiceClient = new BlobServiceClient(connectionString);

			var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

			containerClient.CreateIfNotExists();

			string fileExtension = Path.GetExtension(file.FileName);

			string blobName = Guid.NewGuid().ToString() + fileExtension;

			BlobClient blobClient = containerClient.GetBlobClient(blobName);

			using (Stream stream = file.OpenReadStream())
			{
				blobClient.Upload(stream);
			}

			Dictionary<string, string> metadata = new()
			{
				{ "OriginalFileName", Convert.ToBase64String(Encoding.UTF8.GetBytes(file.FileName)) }
			};

			blobClient.SetMetadata(metadata);

			return new UploadedFileInfo
			{
				OriginalFileName = file.FileName,
				Link = blobClient.Uri.ToString(),
				ContentType = file.ContentType
			};
		}
	}

	public class UploadedFileInfo
	{
		public required string OriginalFileName { get; set; }
		public required string Link { get; set; }
		public required string ContentType { get; set; }
	}
}
