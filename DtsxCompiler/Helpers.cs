using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrbitalForge
{
    public static class Helpers
    {
        const string ValidChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private static volatile Random random = new Random();
        public static string GenerateName(int length)
        {
            char[] chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = ValidChars[random.Next(ValidChars.Length)];
            }
            return new string(chars);
        }

        public static string GetTempPath(EnvironmentVariableTarget target = EnvironmentVariableTarget.User)
        {
            var envTmpPath = Environment.GetEnvironmentVariable("TEMP", target);
            var tmpFolderName = Guid.NewGuid().ToString().Replace("-", "");
            var tmpPath = Path.Combine(envTmpPath, tmpFolderName);

            int tries = 1;

            while(Directory.Exists(tmpPath))
            {
                tmpFolderName = Guid.NewGuid().ToString().Replace("-", "");
                tmpPath = Path.Combine(envTmpPath, tmpFolderName);

                if (tries > 10) throw new Exception("ERROR Creating Temp Path - Exceeded number of tries, 10");
            }

            Directory.CreateDirectory(tmpPath);

            return tmpPath;
        }
    }
}
