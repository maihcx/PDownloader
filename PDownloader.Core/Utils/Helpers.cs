using System;
using System.Collections.Generic;
using System.Text;

namespace PDownloader.Core.Utils
{
    public class Helpers
    {
        public static string CreateMD5(string input)
        {
            using System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            return Convert.ToHexString(hashBytes);
        }

        public static string GetDefaultFolder()
        {
            string? saved = UserDataStore.GetValue<string>("DefaultDownloadFolder");
            if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved)) return saved;
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}
