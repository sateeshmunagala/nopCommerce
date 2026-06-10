using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AgoraIO.Media2
{
    public class AccessToken2
    {
        public class PrivilegeMessage
        {
            public int privilege;
            public uint expireTs;

            public PrivilegeMessage(int privilege, uint expireTs)
            {
                this.privilege = privilege;
                this.expireTs = expireTs;
            }
        }

        private string appId;
        private string appCertificate;
        private string channelName;
        private string uid;
        private Dictionary<int, uint> privileges = new Dictionary<int, uint>();

        // Common RTC privileges
        public const int PrivilegeJoinChannel = 1;
        public const int PrivilegePublishAudioStream = 2;
        public const int PrivilegePublishVideoStream = 3;
        public const int PrivilegePublishDataStream = 4;

        public AccessToken2(string appId, string appCertificate, string channelName, string uid)
        {
            this.appId = appId;
            this.appCertificate = appCertificate;
            this.channelName = channelName;
            this.uid = uid;
        }

        /// <summary>
        /// Add privilege with expiry timestamp
        /// </summary>
        public void AddPrivilege(int privilege, uint expireTs)
        {
            privileges[privilege] = expireTs;
        }

        /// <summary>
        /// Build the 007 token string
        /// </summary>
        public string Build()
        {
            int salt = new Random().Next();
            uint ts = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(salt);
                bw.Write(ts);

                // Write privileges
                bw.Write((ushort)privileges.Count);
                foreach (var kv in privileges)
                {
                    bw.Write((ushort)kv.Key);
                    bw.Write(kv.Value);
                }

                var message = ms.ToArray();
                var signature = GenerateHmacSha256(appCertificate, Pack(appId, channelName, uid, message));

                using (var tokenStream = new MemoryStream())
                using (var writer = new BinaryWriter(tokenStream))
                {
                    writer.Write((ushort)signature.Length);
                    writer.Write(signature);
                    writer.Write(message);

                    var tokenBytes = tokenStream.ToArray();
                    var base64 = Convert.ToBase64String(tokenBytes);

                    return "007" + appId + base64;
                }
            }
        }

        private static byte[] GenerateHmacSha256(string key, byte[] message)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                return hmac.ComputeHash(message);
            }
        }

        private static byte[] Pack(string appId, string channelName, string uid, byte[] message)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Encoding.UTF8.GetBytes(appId));
                bw.Write(Encoding.UTF8.GetBytes(channelName));
                bw.Write(Encoding.UTF8.GetBytes(uid));
                bw.Write(message);
                return ms.ToArray();
            }
        }
    }
}
