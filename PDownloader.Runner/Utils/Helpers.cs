using System;
using System.Collections.Generic;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace PDownloader.Runner.Utils
{
    public class Helpers
    {
        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public static string Base64Decode(string base64EncodedData)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64EncodedData));
            }
            catch (FormatException)
            {
                // Không phải Base64
                return base64EncodedData;
            }
        }
    }
}
